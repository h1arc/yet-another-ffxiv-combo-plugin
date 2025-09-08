using System;
using yetanotherffxivcomboplugin.Core;
using yetanotherffxivcomboplugin.Snapshot;

namespace yetanotherffxivcomboplugin.Jobs.Interfaces;

/// <summary>
/// Base implementation for jobs providing default implementations for common patterns.
/// Jobs can inherit from this to reduce boilerplate and only override what they need.
/// </summary>
public abstract class BaseJob : IJob
{
    public abstract ushort JobId { get; }
    public abstract string JobName { get; }

    // Combo hooks with default implementations
    public virtual void ConfigureJob(GameSnapshot cache)
    {
        // Default implementation calls individual configuration methods
        RefreshResolvedActions(cache.PlayerLevel);
        ConfigureDebuffs(cache);
        ConfigureBuffs(cache);
        ConfigureCooldowns(cache);
        // Note: RetraceableActions registration now handled by JobRegistry

        // Configure anchors if job has combo actions
        var anchors = GetComboAnchorActions();
        if (anchors.Length > 0)
            cache.ConfigureAnchors(anchors);
    }

    public virtual void ClearJob(GameSnapshot cache)
    {
        cache.ClearMappings();
        cache.ClearAnchors();
        // Note: RetraceableActions.Clear() now handled by JobRegistry
        ClearResolvedActions();
    }

    public virtual void ConfigureDebuffs(GameSnapshot cache)
    {
        // Override in jobs that need debuff tracking
    }

    public virtual void ConfigureBuffs(GameSnapshot cache)
    {
        // Override in jobs that need buff tracking
    }

    public virtual void ConfigureCooldowns(GameSnapshot cache)
    {
        // Override in jobs that need cooldown tracking
        var cooldowns = GetTrackedCooldowns();
        if (cooldowns.Length > 0)
            cache.ConfigureCooldowns(cooldowns);
    }

    public virtual void RegisterPlannerRules(Planner? planner)
    {
        // Override in jobs that use the planner system
    }

    // Gauge hooks with default implementations
    public virtual void Register(GameSnapshot cache)
    {
        // Override in jobs that need gauge tracking
    }

    public virtual void Unregister(GameSnapshot cache)
    {
        // Override in jobs that need gauge tracking
    }

    public virtual void OnActionUsed(bool success, int usedActionId, GameSnapshot? snap)
    {
        // Override in jobs that need action usage tracking
    }

    // Action resolver with default implementations
    public virtual int ResolveAction(int baseActionId, byte level)
    {
        // Default: no level-based resolution
        return baseActionId;
    }

    public abstract void RefreshResolvedActions(byte level);

    protected virtual void ClearResolvedActions()
    {
        // Override in jobs that cache resolved actions
    }

    // Action registry with default implementations
    public virtual void RegisterRetraceableActions()
    {
        // This method is kept for interface compatibility but is now handled automatically
        // by JobRegistry via GetRetraceableActionIds()
    }

    public virtual bool IsRetraceable(int actionId)
    {
        // Check if action is in the retraceable list for this job
        return Array.IndexOf(GetRetraceableActionIds(), actionId) >= 0;
    }

    public abstract int[] GetOwnedActionIds();

    // Targeting hooks with default implementations
    public virtual bool IsOwnedAction(int actionId)
    {
        var owned = GetOwnedActionIds();
        return Array.IndexOf(owned, actionId) >= 0;
    }

    public virtual ulong TryResolveTarget(int actionId, ulong currentTargetId, GameSnapshot cache)
    {
        // Default: no special targeting
        return 0;
    }

    // Job lifecycle
    public virtual void Initialize()
    {
        // Override for job-specific initialization
    }

    public virtual void Dispose()
    {
        // Override for job-specific cleanup
    }

    public virtual bool IsConfigurationValid(GameSnapshot cache)
    {
        // Default: always valid
        return true;
    }

    // Helper methods for subclasses
    protected virtual int[] GetComboAnchorActions()
    {
        // Override to provide combo anchor actions
        return [];
    }

    protected virtual int[] GetTrackedCooldowns()
    {
        // Override to provide cooldowns to track
        return [];
    }

    public virtual int[] GetGcdActionIds()
    {
        // Override to provide GCD action IDs
        return [];
    }

    public virtual int[] GetOgcdActionIds()
    {
        // Override to provide oGCD action IDs
        return [];
    }

    public virtual ushort[] GetStatusIds()
    {
        // Override to provide status IDs
        return [];
    }

    public virtual int[] GetRetraceableActionIds()
    {
        // Override to provide retraceable action IDs
        return [];
    }

    public virtual bool IsJobAction(int actionId)
    {
        // Override to provide job-specific action checks
        return false;
    }

    public virtual bool IsJobStatus(ushort statusId)
    {
        // Override to provide job-specific status checks
        return false;
    }
}
