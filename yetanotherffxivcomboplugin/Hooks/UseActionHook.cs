using System;
using System.Runtime.CompilerServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using yetanotherffxivcomboplugin.Core;

namespace yetanotherffxivcomboplugin.Hooks;

[SkipLocalsInit]
internal unsafe sealed class UseActionHooker : IDisposable
{
    private readonly ActionExecutionPipeline _pipeline;
    private readonly Hook<UseActionDelegate> _hook;

    private delegate bool UseActionDelegate(ActionManager* am, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted);

    public UseActionHooker(ActionExecutionPipeline pipeline, IGameInteropProvider interop)
    {
        _pipeline = pipeline;
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
        Plugin.Planner?.SetPressedActionHint(aid);

        // Early skip: if there is no forced retrace and we're not in combat and not an anchor, 
        // nothing can change — bypass resolution entirely.
        var snap = Plugin.GameSnapshot;
        bool hasForcedRetrace = _pipeline.HasRetraceFor(aid);
        bool inCombat = snap?.Flags.InCombat ?? false;
        bool isAnchor = snap?.IsAnchorAction(aid) ?? false;

        Decision decision;
        if (!hasForcedRetrace && !inCombat && !isAnchor)
            decision = Decision.None();
        else
            decision = _pipeline.Resolve(aid, targetId); // Quiet hot path: only compute when necessary

        var changedTarget = (decision.Kind == DecisionKind.TargetOverride || decision.Kind == DecisionKind.ActionAndTargetOverride) && decision.TargetId != 0 && decision.TargetId != targetId;
        var changedAction = (decision.Kind == DecisionKind.ActionOverride || decision.Kind == DecisionKind.ActionAndTargetOverride) && decision.ActionId != 0 && decision.ActionId != aid;
        if (changedAction)
            actionId = (uint)decision.ActionId;
        if (changedTarget)
            targetId = decision.TargetId;
        // else keep quiet on no-op

        // Planner already received the pressed hint above.

        var result = _hook.Original(am, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
        // Inform planner about actual action usage result so opener rule can advance
        Plugin.Planner?.OnActionResult(result, (int)actionId);
        // Mark action as used for this tick to avoid immediate repeats in readiness checks
        Plugin.GameSnapshot?.MarkActionUsed((int)actionId);
        // Job-local deterministic tracking (e.g., WHM Sacred Sight mock stacks)
        Jobs.Interfaces.JobRegistry.OnActionUsed(result, (int)actionId, Plugin.GameSnapshot);
        // Only set for area-targeted abilities; single-target heals don't need this and it can confuse logging.
        if (changedTarget && outOptAreaTargeted != null && *outOptAreaTargeted)
        {
            var amInst = ActionManager.Instance();
            if (amInst != null)
                amInst->AreaTargetingExecuteAtObject = targetId;
        }
        return result;
    }
}
