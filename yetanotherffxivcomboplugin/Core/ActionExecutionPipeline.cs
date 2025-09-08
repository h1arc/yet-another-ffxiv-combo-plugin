using System;
using System.Runtime.CompilerServices;
using yetanotherffxivcomboplugin.Snapshot;

namespace yetanotherffxivcomboplugin.Core;

/// <summary>
/// Unified execution pipeline: per-tick planning + hook-time action/target resolution.
/// Rebuilds plan once per Framework.Update (Plugin-level gating handles when updates are skipped).
/// Handles single-target healer & cleanse retrace (lowest-HP / cleansable ally) only.
/// </summary>
[SkipLocalsInit]
public sealed class ActionExecutionPipeline(GameSnapshot snapshot, Planner planner, RetraceRegistry retraces)
{
    private ulong _tick;                // framework ticks processed
    private ulong _planVersion;         // incremented when plan rebuilt

    private PipelineMetrics _metrics;

    public ref readonly PipelineMetrics Metrics => ref _metrics;
    public ulong PlanVersion => _planVersion;

    // ForceRebuildNextTick no longer needed; main thread gating controls cadence.

    /// <summary> Called once per Framework.Update AFTER the snapshot has been refreshed. </summary>
    public void Tick()
    {
        _tick++;
        _metrics.Frame = _tick;
        // Planner rebuild cadence is externally throttled by the Plugin's UpdateGate.
        // Build a plan and only bump version when it actually changes to reduce UI flicker/noise.
        if (planner.Build(snapshot))
        {
            _planVersion++;
            _metrics.PlanRebuilds++;
        }
    }

    /// <summary>
    /// Cheap check used by the hook to know if a forced retrace exists for the given action.
    /// Avoids invoking the full resolution pipeline when nothing can change.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasRetraceFor(int actionId)
    {
        if (retraces == null) return false;
        return retraces.TryGetTargetFor(actionId, out _);
    }

    /// <summary> Resolve an action request (hook path): retrace first (works outside combat), then planner in combat.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Decision Resolve(int actionId, ulong currentTargetId)
    {
        int plannedOverride = 0;

        // 1) Forced target overrides, always allowed
        ulong plannedTarget = GetForcedTargetOverride(actionId, currentTargetId);

        // 2) Automatic healer retrace (Esuna / single-target heals), always allowed
        if (plannedTarget == 0)
        {
            var auto = GetBestRetraceTarget(actionId, currentTargetId);
            if (auto != 0 && auto != currentTargetId)
                plannedTarget = auto;
        }

        // Outside combat: only apply target overrides, never planner
        if (!snapshot.Flags.InCombat)
        {
            if (plannedTarget != 0)
            { _metrics.Retraces++; return Decision.Target(plannedTarget); }
            return Decision.None();
        }

        // 3) In combat: planner-driven replacement/weave only for anchors
        bool isAnchor = snapshot.IsAnchorAction(actionId);
        // Allow quick-raise accessory sequences even though they aren't filler anchors
        if (!isAnchor)
        {
            if (actionId is Jobs.SGE.SGEIDs.Egeiro or Jobs.WHM.WHMIDs.Raise)
                isAnchor = true;
        }

        // Avoid weaving into the void during untargetable phases on anchors: require any hard target to run planner
        if (isAnchor && !HasHostileHardTarget())
        {
            if (plannedTarget != 0)
                return Decision.Target(plannedTarget);
            return Decision.Action(actionId);
        }

        // Compute planner suggestions when applicable
        if (isAnchor)
            plannedOverride = ComputePlannerOverride(actionId);

        // If we selected an override that is retraceable (e.g., switching to a heal), try to find a better target for it
        if (plannedTarget == 0 && plannedOverride != 0)
        {
            var job = Jobs.Interfaces.JobRegistry.GetCurrentJob();
            if (job != null && job.IsRetraceable(plannedOverride))
            {
                var auto2 = GetBestRetraceTarget(plannedOverride, currentTargetId);
                if (auto2 != 0 && auto2 != currentTargetId)
                    plannedTarget = auto2;
            }
        }

        // 4) Apply decisions (prefer combining action+target when both available)
        if (plannedOverride != 0 && plannedTarget != 0)
            return Decision.ActionAndTarget(plannedOverride, plannedTarget);
        if (plannedTarget != 0)
        { _metrics.Retraces++; return Decision.Target(plannedTarget); }
        if (plannedOverride != 0)
            return Decision.Action(plannedOverride);

        return Decision.None();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong GetForcedTargetOverride(int actionId, ulong currentTargetId)
    {
        if (retraces == null) return 0UL;
        if (retraces.TryConsume(actionId, out var forcedOnce) && forcedOnce != 0 && forcedOnce != currentTargetId)
            return forcedOnce;
        if (retraces.TryGetTargetFor(actionId, out var forced) && forced != 0 && forced != currentTargetId)
            return forced;
        return 0UL;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong GetBestRetraceTarget(int actionId, ulong currentTargetId)
    {
        var currentJob = Jobs.Interfaces.JobRegistry.GetCurrentJob();
        if (currentJob == null || !currentJob.IsRetraceable(actionId))
            return 0UL;

        if (actionId == 7568) // Esuna
        {
            var list = snapshot.BuildCleansableFromObjectTable(actionId, 1, includeSelf: true);
            if (list.TryTakeNext(out var cid) && cid != currentTargetId)
                return cid;
            return 0UL;
        }
        else if (actionId is Jobs.SGE.SGEIDs.Egeiro or Jobs.WHM.WHMIDs.Raise)
        {
            // Prefer a dead ally using cached field
            snapshot.TryGetFirstDeadPartyMember(out ulong deadTarget);
            if (deadTarget != 0 && deadTarget != currentTargetId)
                return deadTarget;
            return 0UL;
        }
        else
        {
            var list = snapshot.BuildHealableFromObjectTable(actionId, 1, includeSelf: true);
            if (list.TryTakeNext(out var hid) && hid != currentTargetId)
                return hid;
            return 0UL;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasHostileHardTarget()
    {
        ulong hardId = snapshot.Target.HasValue && snapshot.Target.Value.IsValid ? snapshot.Target.Value.Id : 0UL;
        return hardId != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ComputePlannerOverride(int actionId)
    {
        int planned = 0;
        // Build a transient plan for the pressed action to ensure fresh suggestions with current hint
        Span<Suggestion> gTemp = stackalloc Suggestion[4];
        Span<Suggestion> oTemp = stackalloc Suggestion[6];
        planner.BuildForPressed(snapshot, actionId, gTemp, oTemp);

        // Try OGCD first
        var og = oTemp;
        for (int i = 0; i < og.Length; i++)
        {
            var s = og[i];
            if (s.ActionId == 0 || s.IsGcd) continue;
            if (!snapshot.IsActionReady(s.ActionId)) continue;
            if (snapshot.TryGetCooldownRemainingMs(s.ActionId, out var remMs) && remMs > 0)
                continue;
            planned = s.ActionId;
            break;
        }

        // If no OGCD selected, consider GCD replacement
        if (planned == 0)
        {
            var g = gTemp;
            for (int i = 0; i < g.Length; i++)
            {
                var s = g[i];
                if (!s.IsGcd || s.ActionId == 0) continue;
                // Ignore fallback anchor suggestions (order >= 250) to avoid stale overrides when switching quickly
                if (s.Order >= 250) continue;
                if (s.ActionId == actionId) break; // already pressing the suggested action
                if (!snapshot.IsActionReady(s.ActionId)) continue;
                planned = s.ActionId;
                break;
            }
        }
        return planned;
    }
}

public struct PipelineMetrics
{
    public ulong Frame;
    public uint PlanRebuilds;
    public uint Retraces;
    public override readonly string ToString() => $"F={Frame} Rebuilds={PlanRebuilds} Retraces={Retraces}";
}
