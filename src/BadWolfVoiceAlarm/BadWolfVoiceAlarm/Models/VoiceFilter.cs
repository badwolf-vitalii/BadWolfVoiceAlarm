using System;
using System.Collections.Generic;

namespace BadWolfVoiceAlarm.Models;

public sealed class VoiceFilter
{
    private readonly HashSet<ulong> _channelIds;

    private VoiceFilter(ulong? guildId, HashSet<ulong> channelIds, bool alertOnMoves, bool ignoreBots)
    {
        GuildId = guildId;
        _channelIds = channelIds;
        AlertOnMoves = alertOnMoves;
        IgnoreBots = ignoreBots;
    }

    public ulong? GuildId { get; }
    public bool AlertOnMoves { get; }
    public bool IgnoreBots { get; }

    public bool Matches(ulong guildId, ulong channelId) =>
        (GuildId is null || GuildId == guildId) &&
        (_channelIds.Count == 0 || _channelIds.Contains(channelId));

    public static VoiceFilter Create(string guildId, string channelIds, bool alertOnMoves, bool ignoreBots)
    {
        ulong? guildFilter = null;
        if (!string.IsNullOrWhiteSpace(guildId))
        {
            if (!ulong.TryParse(guildId.Trim(), out ulong parsed) || parsed == 0)
            {
                throw new ArgumentException("Server ID must be a valid numeric Discord ID.");
            }

            guildFilter = parsed;
        }

        var channels = new HashSet<ulong>();
        foreach (string raw in channelIds.Split(new[] { ',', ';', ' ', '\r', '\n', '\t' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ulong.TryParse(raw, out ulong channelId) || channelId == 0)
            {
                throw new ArgumentException($"Invalid channel ID: {raw}");
            }

            channels.Add(channelId);
        }

        return new VoiceFilter(guildFilter, channels, alertOnMoves, ignoreBots);
    }
}
