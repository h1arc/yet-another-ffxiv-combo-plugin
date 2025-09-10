using System.Runtime.CompilerServices;
using yetanotherffxivcomboplugin; // for Plugin

namespace yetanotherffxivcomboplugin.src.Jobs.WHM;

/// <summary>
/// Lightweight snapshot of WHM lilies and blood lily. Immutable per tick.
/// LilyTimerMs is elapsed time since the last lily (ms), increasing toward LilyIntervalMs.
/// </summary>
public readonly struct WhmGaugeSnapshot(byte lilies, byte blood, int lilyTimerMs)
{
    public readonly byte Lilies = lilies;
    public readonly byte BloodLilies = blood; // 0..3
    public readonly int LilyTimerMs = lilyTimerMs;  // elapsed since last lily (ms), -1 if unknown
    public bool HasFullLilies => Lilies >= 3;
    public bool BloodLilyBloomed => BloodLilies >= 3;

    // Lily generation interval (ms). In Dawntrail, lilies generate roughly every 20s in combat.
    private const int LilyIntervalMs = 20_000;

    // Remaining time until next lily (ms). Returns -1 when unknown.
    // LilyTimerMs is treated as an ever-increasing elapsed timer; use modulo to find time since last tick.
    public int NextLilyRemainingMs
        => LilyTimerMs < 0 ? -1 : (LilyIntervalMs - (LilyTimerMs % LilyIntervalMs));

    // Simple overcap check: if we have 2 lilies and the next lily arrives within threshold, we will overcap.
    public bool OvercapWithinMs(int thresholdMs)
        => Lilies == 2 && NextLilyRemainingMs >= 0 && NextLilyRemainingMs <= thresholdMs;

    public bool OvercapSoon => OvercapWithinMs(10_000);
}

public static class WhmGaugeReader
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WhmGaugeSnapshot Read()
    {
        var cache = Plugin.Runtime.Snap;
        return cache != null && cache.TryReadGauge<WhmGaugeSnapshot>(out var snap) ? snap : default;
    }
}
