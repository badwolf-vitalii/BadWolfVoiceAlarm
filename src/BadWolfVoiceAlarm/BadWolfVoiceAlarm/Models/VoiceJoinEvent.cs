namespace BadWolfVoiceAlarm.Models;

public sealed record VoiceJoinEvent(
    string UserName,
    string GuildName,
    string ChannelName,
    ulong GuildId,
    ulong ChannelId,
    bool IsMove);
