using System;
using System.Runtime.CompilerServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using yetanotherffxivcomboplugin.src.Core; // ActionResolver
using yetanotherffxivcomboplugin.src.Jobs.Interfaces;

namespace yetanotherffxivcomboplugin.Hooks;

[SkipLocalsInit]
internal unsafe sealed class UseActionHook : IDisposable
{
    private readonly ActionResolver _resolver;
    private readonly Hook<UseActionDelegate> _hook;

    private delegate bool UseActionDelegate(ActionManager* am, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted);

    public UseActionHook(ActionResolver resolver, IGameInteropProvider interop)
    {
        _resolver = resolver;
        _hook = interop.HookFromAddress<UseActionDelegate>(ActionManager.Addresses.UseAction.Value, Detour);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enable() => _hook.Enable();

    public void Dispose() => _hook.Dispose();

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool Detour(ActionManager* am, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted)
    {
        // Ultra-fast bail: only handle player Action/Ability; ignore mounts, items, etc.
        if (actionType is not (ActionType.Action or ActionType.Ability))
            return _hook.Original(am, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);

        var aid = (int)actionId;

        // Provide planner with the original pressed action before any override.
        // Plugin.Planner?.SetPressedActionHint(aid);

        // Early skip: if there is no forced retrace and we're not in combat and not an anchor, 
        // nothing can change — bypass resolution entirely.
        var snap = Plugin.Runtime.Snap;
        bool hasForcedRetrace = false; // forced retrace removed
        bool inCombat = snap?.Flags.InCombat ?? false;
        bool isAnchor = _resolver.IsAnchor(aid);

        int overrideAction = 0; ulong overrideTarget = 0;
        if (hasForcedRetrace || inCombat || isAnchor)
        {
            // --- Begin inlined pipeline logic ---
            ulong forcedTarget = 0; // no forced target system

            // Automatic retrace (if no forced) only if the pressed action is in the job's retrace list
            if (forcedTarget == 0 && JobRegistry.Current is { } job && job.RetraceActions.Length > 0)
            {
                var auto = Retrace.GetAutoTarget(snap!, aid, job.RetraceActions, targetId);
                if (auto != 0) forcedTarget = auto;
            }

            if (!inCombat)
            {
                if (forcedTarget != 0) overrideTarget = forcedTarget; // only target override out of combat
            }
            else
            {
                int replacement = 0;
                if (isAnchor)
                {
                    var tupleResolved = _resolver.Resolve(snap!, aid);
                    int resolvedId = tupleResolved.actionId; bool isGcd = tupleResolved.isGcd;
                    if (isGcd && resolvedId != 0 && resolvedId != aid)
                        replacement = resolvedId;
                    if (replacement == 0)
                    {
                        var ogTuple = _resolver.GetNextOgcd(snap!);
                        int ogId = ogTuple.actionId; bool isOgcd = ogTuple.isOgcd; ulong ogTgt = ogTuple.target;
                        if (isOgcd && ogId != 0)
                        { replacement = ogId; if (ogTgt != 0) forcedTarget = ogTgt; }
                    }
                }

                if (replacement != 0 && forcedTarget == 0 && JobRegistry.Current is { } job2 && job2.RetraceActions.Length > 0)
                {
                    var auto2 = Retrace.GetAutoTarget(snap!, replacement, job2.RetraceActions, targetId);
                    if (auto2 != 0) forcedTarget = auto2;
                }

                if (replacement != 0) overrideAction = replacement;
                if (forcedTarget != 0) overrideTarget = forcedTarget;
            }
            // --- End inlined pipeline logic ---
        }

        bool changedAction = overrideAction != 0 && overrideAction != aid;
        bool changedTarget = overrideTarget != 0 && overrideTarget != targetId;
        if (changedAction) actionId = (uint)overrideAction;
        if (changedTarget) targetId = overrideTarget;
        // else keep quiet on no-op

        // Planner removed in this lean path.

        var result = _hook.Original(am, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
        // Resolver will learn sequence progression via Job OnActionUsed handlers already.
        // Mark action as used for this tick to avoid immediate repeats in readiness checks
        Plugin.Runtime.Snap?.MarkActionUsed((int)actionId);
        // Job-local deterministic tracking (e.g., WHM Sacred Sight mock stacks)
        if (Plugin.Runtime.Snap != null)
            JobRegistry.OnActionUsed(result, (int)actionId, Plugin.Runtime.Snap);
        // Only set for area-targeted abilities; single-target heals don't need this and it can confuse logging.
        if (changedTarget && outOptAreaTargeted != null && *outOptAreaTargeted)
        {
            var amInst = ActionManager.Instance();
            if (amInst != null)
                amInst->AreaTargetingExecuteAtObject = targetId;
        }
        return result;
    }

    // Forced target override system removed for now.
}
