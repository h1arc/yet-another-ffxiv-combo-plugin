using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace yetanotherffxivcomboplugin.src.Helpers;

/// <summary>
/// Lightweight, compiled gating pipeline that returns a reason to skip per-frame work.
/// The checks are compiled into a static array of steps executed in order without a
/// long if/else chain. This minimizes branch mispredictions and keeps the hot path tiny.
/// </summary>
public sealed class UpdateGate
{
    // Tune this to control how often we run outside combat. Values that divide 30/60/120 work well (e.g., 10, 15).
    public static int NonCombatThrottleInterval { get; set; } = 30;

    // Per-instance state carried through the compiled checks
    private State _state;
    // Persist throttle across transient instances (Plugin creates a new gate each frame)
    private static int s_NonCombatCounter;

    public UpdateGate(IClientState clientState, ICondition condition, Func<bool>? isInSanctuary = null)
    {
        _state = new State
        {
            ClientState = clientState,
            Condition = condition,
            IsInSanctuary = isInSanctuary,
            NonCombatCounter = 0
        };
    }

    // The order of checks is important and matches historical behavior
    private delegate UpdateSkipReason GateCheck(ref State s);
    private static readonly GateCheck[] s_checks =
    [
        CheckLogin,
        CheckLoading,
        CheckCutscene,
        CheckConversation,
        CheckSanctuary,
        CheckNonCombatThrottle,
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldSkip(out UpdateSkipReason reason)
    {
        // Fast iteration over small static array; first non-None ends evaluation
        for (int i = 0; i < s_checks.Length; i++)
        {
            var r = s_checks[i](ref _state);
            if (r != UpdateSkipReason.None)
            { reason = r; return true; }
        }
        reason = UpdateSkipReason.None; return false;
    }

    private struct State
    {
        public IClientState? ClientState;
        public ICondition? Condition;
        public Func<bool>? IsInSanctuary;
        public int NonCombatCounter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsFlag(ref State s, ConditionFlag flag)
        => s.Condition != null && s.Condition[flag];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UpdateSkipReason CheckLogin(ref State s)
        => s.ClientState?.IsLoggedIn != true ? UpdateSkipReason.NotLoggedIn : UpdateSkipReason.None;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UpdateSkipReason CheckLoading(ref State s)
    {
        if (IsFlag(ref s, ConditionFlag.BetweenAreas) || IsFlag(ref s, ConditionFlag.BetweenAreas51))
            return UpdateSkipReason.Loading;
        return UpdateSkipReason.None;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UpdateSkipReason CheckCutscene(ref State s)
    {
        if (IsFlag(ref s, ConditionFlag.OccupiedInCutSceneEvent) || IsFlag(ref s, ConditionFlag.WatchingCutscene))
            return UpdateSkipReason.Cutscene;
        return UpdateSkipReason.None;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UpdateSkipReason CheckConversation(ref State s)
        => IsFlag(ref s, ConditionFlag.OccupiedInEvent) ? UpdateSkipReason.InConversation : UpdateSkipReason.None;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UpdateSkipReason CheckSanctuary(ref State s)
        => s.IsInSanctuary != null && s.IsInSanctuary() ? UpdateSkipReason.InSanctuary : UpdateSkipReason.None;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static UpdateSkipReason CheckNonCombatThrottle(ref State s)
    {
        if (!IsFlag(ref s, ConditionFlag.InCombat))
        {
            int n = Interlocked.Increment(ref s_NonCombatCounter);
            int interval = NonCombatThrottleInterval <= 1 ? 1 : NonCombatThrottleInterval;
            return (n % interval) != 0 ? UpdateSkipReason.ThrottledNonCombat : UpdateSkipReason.None;
        }
        // reset when entering/being in combat
        s.NonCombatCounter = 0;
        Interlocked.Exchange(ref s_NonCombatCounter, 0);
        return UpdateSkipReason.None;
    }
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
