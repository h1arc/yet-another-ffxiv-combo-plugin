using System;
using System.Runtime.CompilerServices;
using yetanotherffxivcomboplugin.src.Snapshot;

namespace yetanotherffxivcomboplugin.src.Core;

/// <summary>
/// Pure, allocation-free helper for automatic target retracing of single-target utility/heal actions.
/// Keeps logic centralized; forced target override system has been removed for now.
/// </summary>
public static class Retrace
{
    // Constant Action IDs we support for ultra-lean auto-targeting
    private const int EsunaActionId = 7568;      // Dispel / Esuna (disabled: requires scans)
    private const int RaiseActionId = 125;       // WHM Raise
    private const int EgeiroActionId = 24287;    // SGE Raise

    /// <summary>
    /// Returns best automatic retrace target for the given action or 0 when none.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong GetAutoTarget(GameSnapshot snapshot, int actionId, ReadOnlySpan<int> retraceList, ulong currentTarget)
    {
        if (snapshot == null || retraceList.Length == 0) return 0UL;
        bool retraceable = false;
        for (int i = 0; i < retraceList.Length; i++) if (retraceList[i] == actionId) { retraceable = true; break; }
        if (!retraceable) return 0UL;

        // 1) Raises: pick the first dead party member if any
        if (actionId == RaiseActionId || actionId == EgeiroActionId)
        {
            if (snapshot.TryGetFirstDeadPartyMember(out var deadId) && deadId != 0 && deadId != currentTarget)
                return deadId;
            return 0UL;
        }

        // 2) Cleanse: use top-two cleansable (lowest HP first)
        if (actionId == EsunaActionId)
        {
            var pair = snapshot.GetTopCleansablePair(actionId, includeSelf: true);
            if (pair.TryGetFirst(out var a) && a != currentTarget) return a;
            if (pair.TryGetSecond(out var b) && b != currentTarget) return b;
            return 0UL;
        }

        // 3) Generic single-target heals/support: use top-two healable (lowest HP first)
        {
            var pair = snapshot.GetTopHealablePair(actionId, includeSelf: true);
            if (pair.TryGetFirst(out var a) && a != currentTarget) return a;
            if (pair.TryGetSecond(out var b) && b != currentTarget) return b;
            return 0UL;
        }
    }
}
