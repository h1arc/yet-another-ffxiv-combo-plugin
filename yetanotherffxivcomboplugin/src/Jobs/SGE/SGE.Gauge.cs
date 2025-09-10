using System.Runtime.CompilerServices;

namespace yetanotherffxivcomboplugin.src.Jobs.SGE;

/// <summary>
/// Lightweight snapshot of SGE gauge: Addersgall and Addersting, with Addersgall timer in ms.
/// </summary>
public readonly struct SgeGaugeSnapshot(byte addersgall, byte addersting, int addersgallTimerMs)
{
    public readonly byte Addersgall = addersgall;   // 0..3
    public readonly byte Addersting = addersting;   // 0..3
    public readonly int AddersgallTimerMs = addersgallTimerMs; // elapsed since last tick (ms), -1 if unknown

    private const int TickIntervalMs = 20_000; // Addersgall generation interval in combat

    public int NextAddersgallRemainingMs
        => AddersgallTimerMs < 0 ? -1 : (TickIntervalMs - (AddersgallTimerMs % TickIntervalMs));

    // 0+threshold style: consider overcap soon once elapsed-in-window >= threshold
    private int ElapsedInWindowMs
        => AddersgallTimerMs < 0 ? -1 : (AddersgallTimerMs % TickIntervalMs);

    public bool OvercapWithinMs(int thresholdMs)
        => Addersgall == 3 && ElapsedInWindowMs >= 0 && ElapsedInWindowMs >= thresholdMs;

    public bool OvercapSoon => OvercapWithinMs(18_000);
}

public static class SgeGaugeReader
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SgeGaugeSnapshot Read()
    {
        var cache = Plugin.Runtime.Snap;
        return cache != null && cache.TryReadGauge<SgeGaugeSnapshot>(out var snap) ? snap : default;
    }
}
