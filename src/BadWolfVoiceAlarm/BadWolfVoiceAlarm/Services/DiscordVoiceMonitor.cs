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
        StatusChanged?.Invoke("Connected");
        MessageLogged?.Invoke($"Listening in {_client?.Guilds.Count ?? 0} Discord server(s).");
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(Exception exception)
    {
        StatusChanged?.Invoke("Reconnecting...");
        MessageLogged?.Invoke($"Discord connection lost: {exception.Message}");
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
        SocketVoiceChannel? newChannel = after.VoiceChannel;
        if (newChannel is null)
        {
            return Task.CompletedTask;
        }

        VoiceFilter filter = Volatile.Read(ref _filter);
        if (filter.IgnoreBots && user.IsBot)
        {
            return Task.CompletedTask;
        }

        bool isJoin = before.VoiceChannel is null;
        bool isMove = before.VoiceChannel is not null && before.VoiceChannel.Id != newChannel.Id;
        if (!isJoin && !(isMove && filter.AlertOnMoves))
        {
            return Task.CompletedTask;
        }

        if (!filter.Matches(newChannel.Guild.Id, newChannel.Id))
        {
            return Task.CompletedTask;
        }

        string userName = user is SocketGuildUser member ? member.DisplayName : user.Username;
        UserEnteredChannel?.Invoke(new VoiceJoinEvent(
            userName, newChannel.Guild.Name, newChannel.Name,
            newChannel.Guild.Id, newChannel.Id, isMove));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _lifecycleLock.Dispose();
    }
}
