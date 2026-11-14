namespace BadWolfVoiceAlarm.Models;

public sealed class AppSettings
{
    public string EncryptedBotToken { get; set; } = string.Empty;
    public string GuildId { get; set; } = string.Empty;
    public string ChannelIds { get; set; } = string.Empty;
    public bool AlertOnMoves { get; set; } = true;
    public bool IgnoreBots { get; set; } = true;
    public bool ForceMaximumVolume { get; set; } = true;
    public bool AutoConnect { get; set; } = true;
}
