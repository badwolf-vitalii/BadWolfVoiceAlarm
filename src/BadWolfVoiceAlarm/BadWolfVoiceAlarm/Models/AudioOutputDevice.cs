namespace BadWolfVoiceAlarm.Models;

public sealed record AudioOutputDevice(string Id, string Name, bool IsAvailable = true)
{
    public const string SystemDefaultId = "";

    public string DisplayName => IsAvailable ? Name : $"{Name} (unavailable)";
}
