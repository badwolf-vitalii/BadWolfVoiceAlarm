namespace BadWolfVoiceAlarm.Models;

public enum VoiceChannelTransition
{
    Unchanged,
    Joined,
    Moved,
    Left
}

public static class VoiceChannelTransitionClassifier
{
    public static VoiceChannelTransition Classify(ulong? previousChannelId, ulong? currentChannelId)
    {
        if (previousChannelId == currentChannelId)
        {
            return VoiceChannelTransition.Unchanged;
        }

        if (!currentChannelId.HasValue)
        {
            return VoiceChannelTransition.Left;
        }

        return previousChannelId.HasValue
            ? VoiceChannelTransition.Moved
            : VoiceChannelTransition.Joined;
    }
}
