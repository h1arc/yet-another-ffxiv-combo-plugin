using System;
using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace yetanotherffxivcomboplugin.Core;

/// <summary>
/// Centralizes lightweight, intrinsic checks that indicate we should skip per-frame work.
/// Keeps logic self-contained and cheap: a handful of flag reads and a login check.
/// </summary>
public readonly struct UpdateGate(IClientState clientState, ICondition condition, Func<bool>? isInSanctuary = null)
{
    // Tune this to control how often we run outside combat. Values that divide 30/60/120 work well (e.g., 10, 15).
    public static int NonCombatThrottleInterval = 30;
    private static int _nonCombatCounter;

    public bool ShouldSkip(out UpdateSkipReason reason)
    {
        // 1) Not logged in
        if (clientState?.IsLoggedIn != true)
        { reason = UpdateSkipReason.NotLoggedIn; return true; }

        // 2) Between areas (loading screens)
        if (IsFlag(ConditionFlag.BetweenAreas) || IsFlag(ConditionFlag.BetweenAreas51))
        { reason = UpdateSkipReason.Loading; return true; }

        // 3) Cutscenes
        if (IsFlag(ConditionFlag.OccupiedInCutSceneEvent) || IsFlag(ConditionFlag.WatchingCutscene))
        { reason = UpdateSkipReason.Cutscene; return true; }

        // 4) Occupied by conversations/interactions (talk to NPC, quest events, etc.)
        if (IsFlag(ConditionFlag.OccupiedInEvent))
        { reason = UpdateSkipReason.InConversation; return true; }

        // 5) Sanctuary/home areas (delegate supplied by host to avoid exceptions or heavy lookups here)
        if (isInSanctuary != null && isInSanctuary())
        { reason = UpdateSkipReason.InSanctuary; return true; }

        // 6) Throttle outside combat to reduce overhead (allow every Nth tick)
        if (!IsFlag(ConditionFlag.InCombat))
        {
            int n = Interlocked.Increment(ref _nonCombatCounter);
            int interval = NonCombatThrottleInterval <= 1 ? 1 : NonCombatThrottleInterval;

            if ((n % interval) != 0)
            { reason = UpdateSkipReason.ThrottledNonCombat; return true; }
        }
        else
            _nonCombatCounter = 0; // reset when entering combat

        reason = UpdateSkipReason.None;
        return false;
    }

    private bool IsFlag(ConditionFlag flag) => condition != null && condition[flag];
}

public enum UpdateSkipReason : byte
{
    None = 0,
    NotLoggedIn,
    Loading,
    Cutscene,
    InConversation,
    InSanctuary,
    ThrottledNonCombat,
}
