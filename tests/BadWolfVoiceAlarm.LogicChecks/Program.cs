using System;
using BadWolfVoiceAlarm.Models;

int checks = 0;

void Check(bool condition, string message)
{
    checks++;
    if (!condition)
    {
        throw new InvalidOperationException($"Test failed: {message}");
    }
}

void Expect(ulong? previous, ulong? current, VoiceChannelTransition expected)
{
    Check(VoiceChannelTransitionClassifier.Classify(previous, current) == expected,
        $"Transition {previous?.ToString() ?? "none"} -> {current?.ToString() ?? "none"} must be {expected}");
}

Expect(null, 456, VoiceChannelTransition.Joined); // The channel ID is enough; no cached channel object required.
Expect(456, 789, VoiceChannelTransition.Moved);
Expect(456, null, VoiceChannelTransition.Left);
Expect(null, null, VoiceChannelTransition.Unchanged);
Expect(456, 456, VoiceChannelTransition.Unchanged); // Mute, deafen, or video changes must not alarm.

VoiceFilter all = VoiceFilter.Create("", "", alertOnMoves: true, ignoreBots: true);
Check(all.Matches(123, 456), "Unrestricted filter matches server/channel.");
Check(all.Matches(999, 789), "Unrestricted filter matches any server/channel.");

VoiceFilter restricted = VoiceFilter.Create("123", "456,789", alertOnMoves: false, ignoreBots: true);
Check(restricted.Matches(123, 456), "Allowed channel.");
Check(restricted.Matches(123, 789), "Second allowed channel.");
Check(!restricted.Matches(123, 999), "Other channels excluded.");
Check(!restricted.Matches(999, 456), "Other servers excluded.");
Check(!restricted.AlertOnMoves, "Moves disabled when configured.");
Check(restricted.IgnoreBots, "Ignore bots enabled when configured.");
Check(restricted.Describe().Contains("server=123"), "Filter diagnostics include server.");

Console.WriteLine($"Passed {checks} voice transition and filter checks.");
