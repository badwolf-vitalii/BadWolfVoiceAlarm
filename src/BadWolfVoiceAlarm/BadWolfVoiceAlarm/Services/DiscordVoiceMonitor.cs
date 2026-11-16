using System;
using System.Threading;
using System.Threading.Tasks;
using BadWolfVoiceAlarm.Models;
using Discord;
using Discord.WebSocket;

namespace BadWolfVoiceAlarm.Services;

public sealed class DiscordVoiceMonitor : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private DiscordSocketClient? _client;
    private VoiceFilter _filter = VoiceFilter.Create(string.Empty, string.Empty, true, true);

    public event Action<string>? StatusChanged;
    public event Action<string>? MessageLogged;
    public event Action<VoiceJoinEvent>? UserEnteredChannel;

    public bool IsRunning => _client is not null;

    public void UpdateFilter(VoiceFilter filter) => Volatile.Write(ref _filter, filter);

    public async Task ConnectAsync(string token, VoiceFilter filter)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Enter a Discord bot token before connecting.");
        }

        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            UpdateFilter(filter);

            var client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates
            });
            client.Ready += OnReadyAsync;
            client.Disconnected += OnDisconnectedAsync;
            client.UserVoiceStateUpdated += OnVoiceStateUpdatedAsync;
            client.Log += OnLogAsync;
            _client = client;

            StatusChanged?.Invoke("Connecting...");
            try
            {
                await client.LoginAsync(TokenType.Bot, token.Trim()).ConfigureAwait(false);
                await client.StartAsync().ConfigureAwait(false);
            }
            catch
            {
                await DisconnectCoreAsync().ConfigureAwait(false);
                StatusChanged?.Invoke("Disconnected");
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            StatusChanged?.Invoke("Disconnected");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task DisconnectCoreAsync()
    {
        DiscordSocketClient? client = _client;
        _client = null;
        if (client is null)
        {
            return;
        }

        client.Ready -= OnReadyAsync;
        client.Disconnected -= OnDisconnectedAsync;
        client.UserVoiceStateUpdated -= OnVoiceStateUpdatedAsync;
        client.Log -= OnLogAsync;

        try
        {
            await client.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MessageLogged?.Invoke($"Disconnect warning: {ex.Message}");
        }

        try
        {
            await client.LogoutAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MessageLogged?.Invoke($"Logout warning: {ex.Message}");
        }
        finally
        {
            client.Dispose();
        }
    }

    private Task OnReadyAsync()
    {
        DiscordSocketClient? client = _client;
        if (client is null)
        {
            return Task.CompletedTask;
        }

        VoiceFilter filter = Volatile.Read(ref _filter);
        StatusChanged?.Invoke("Connected");
        MessageLogged?.Invoke($"Discord gateway ready as {client.CurrentUser?.Username ?? "unknown bot"}. " +
                              $"Listening in {client.Guilds.Count} Discord server(s).");
        MessageLogged?.Invoke($"Active voice filters: {filter.Describe()}");

        if (client.Guilds.Count == 0)
        {
            MessageLogged?.Invoke("WARNING: The bot is in no servers. Install the bot on the target Discord server.");
        }

        foreach (SocketGuild guild in client.Guilds)
        {
            MessageLogged?.Invoke(
                $"Server available: {guild.Name} (ID {guild.Id}), visible voice channels: {guild.VoiceChannels.Count}.");
        }

        if (filter.GuildId.HasValue && client.GetGuild(filter.GuildId.Value) is null)
        {
            MessageLogged?.Invoke(
                $"WARNING: Server filter ID {filter.GuildId.Value} does not match any server available to the bot.");
        }

        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(Exception exception)
    {
        StatusChanged?.Invoke("Reconnecting...");
        MessageLogged?.Invoke($"Discord connection lost: {exception?.Message ?? "Reason unavailable"}");
        return Task.CompletedTask;
    }

    private Task OnLogAsync(LogMessage message)
    {
        if (message.Severity <= LogSeverity.Warning)
        {
            MessageLogged?.Invoke($"Discord {message.Severity}: {message.Message}");
        }

        return Task.CompletedTask;
    }

    private Task OnVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        try
        {
            HandleVoiceStateUpdate(user, before, after);
        }
        catch (Exception ex)
        {
            MessageLogged?.Invoke($"Voice-state event processing error: {ex}");
        }

        return Task.CompletedTask;
    }

    private void HandleVoiceStateUpdate(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        // VoiceChannel can be null when channel metadata is not cached, even when the ID is present.
        ulong? previousId = before.VoiceChannelId;
        ulong? currentId = after.VoiceChannelId;
        VoiceChannelTransition transition = VoiceChannelTransitionClassifier.Classify(previousId, currentId);

        // Mute/deafen/video changes in the same channel are not join events.
        if (transition == VoiceChannelTransition.Unchanged)
        {
            return;
        }

        SocketGuild? guild = (user as SocketGuildUser)?.Guild ??
                             after.VoiceChannel?.Guild ??
                             before.VoiceChannel?.Guild;
        string userName = user is SocketGuildUser member ? member.DisplayName : user.Username;
        string previousName = previousId.HasValue
            ? before.VoiceChannel?.Name ?? previousId.Value.ToString()
            : "outside voice";
        string currentName = currentId.HasValue
            ? after.VoiceChannel?.Name ?? currentId.Value.ToString()
            : "outside voice";
        MessageLogged?.Invoke(
            $"Voice {transition}: {userName} | {previousName} -> {currentName} | " +
            $"server={guild?.Name ?? "unknown"} (ID {guild?.Id.ToString() ?? "unknown"}).");

        if (transition == VoiceChannelTransition.Left)
        {
            return;
        }

        VoiceFilter filter = Volatile.Read(ref _filter);
        if (filter.IgnoreBots && user.IsBot)
        {
            MessageLogged?.Invoke($"Voice event ignored: {userName} is a bot (Ignore bots enabled).");
            return;
        }

        if (transition == VoiceChannelTransition.Moved && !filter.AlertOnMoves)
        {
            MessageLogged?.Invoke("Voice event ignored: channel moves are disabled in settings.");
            return;
        }

        if (guild is null || !currentId.HasValue)
        {
            MessageLogged?.Invoke(
                "Voice event ignored: Discord did not provide a resolvable server/channel ID. " +
                "Check the bot's View Channels permission.");
            return;
        }

        if (!filter.Matches(guild.Id, currentId.Value))
        {
            MessageLogged?.Invoke(
                $"Voice event ignored: guild/channel does not match the active filters ({filter.Describe()}).");
            return;
        }

        if (after.VoiceChannel is null)
        {
            MessageLogged?.Invoke(
                $"Voice channel {currentId.Value} metadata is not cached; the event will still trigger the alarm.");
        }

        UserEnteredChannel?.Invoke(new VoiceJoinEvent(
            userName, guild.Name, currentName, guild.Id, currentId.Value,
            transition == VoiceChannelTransition.Moved));
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _lifecycleLock.Dispose();
    }
}
