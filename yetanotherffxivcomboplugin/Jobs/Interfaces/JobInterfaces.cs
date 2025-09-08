using yetanotherffxivcomboplugin.Core;
using yetanotherffxivcomboplugin.Snapshot;

namespace yetanotherffxivcomboplugin.Jobs.Interfaces;

// Minimal, opt-in contracts to keep jobs consistent and lean.
// Implement as needed; current runtime uses a delegate registry, so these are advisory.

public interface IJobComboHooks
{
    // Engine wiring
    void ConfigureJob(GameSnapshot cache);
    void ClearJob(GameSnapshot cache);

    // Optional: cooldown tracking setup per job
    void ConfigureCooldowns(GameSnapshot cache);

    // Optional: buff tracking setup per job
    void ConfigureBuffs(GameSnapshot cache);

    // Optional: debuff/cache setup per job (no-op for jobs that don't need it)
    void ConfigureDebuffs(GameSnapshot cache);

    // Optional: register job-specific planner rules
    void RegisterPlannerRules(Planner? planner);

    // Opener and family mapping
    // OpenerStep[] BuildOpenerScript();
    // OpenerFamily ResolveFamily(int actionId);
}

public interface IJobGaugeHooks
{
    // Gauge updater lifecycle
    void Register(GameSnapshot cache);
    void Unregister(GameSnapshot cache);

    // Optional: Action usage tracking for gauge management
    void OnActionUsed(bool success, int usedActionId, GameSnapshot? snap);
}

// Action resolution by level - standardize level-based action upgrades
public interface IJobActionResolver
{
    // Resolve action IDs based on current level
    int ResolveAction(int baseActionId, byte level);

    // Get current resolved actions for the job
    void RefreshResolvedActions(byte level);
}

// Job ID constants - standardize action and status ID organization
public interface IJobIds
{
    // Core job identification
    ushort JobId { get; }

    // Get all GCD action IDs for this job
    int[] GetGcdActionIds();

    // Get all oGCD action IDs for this job  
    int[] GetOgcdActionIds();

    // Get all status effect IDs (buffs/debuffs) for this job
    ushort[] GetStatusIds();

    // Get retraceable action IDs (subset of all actions)
    int[] GetRetraceableActionIds();

    // Helper method to check if an action ID belongs to this job
    bool IsJobAction(int actionId);

    // Helper method to check if a status ID belongs to this job
    bool IsJobStatus(ushort statusId);
}

// Job action registry - standardize retraceable actions and constants
public interface IJobActionRegistry
{
    // Register retraceable actions for this job
    void RegisterRetraceableActions();

    // Check if an action belongs to this job and is retraceable
    bool IsRetraceable(int actionId);

    // Get all action IDs that this job owns/handles
    int[] GetOwnedActionIds();
}

// Placeholder for targeting/retrace job nuances
// Keep minimal—expand only if we find stable, job-specific targeting needs.
public interface IJobTargetingHooks
{
    // Check if action belongs to this job
    bool IsOwnedAction(int actionId);

    // Attempt job-specific target resolution
    ulong TryResolveTarget(int actionId, ulong currentTargetId, GameSnapshot cache);

    // Optional: preferred AoE threshold or filters
    // int GetAoeThreshold(int actionId);
}

// Comprehensive job interface - implement this for full job support
// Combines all the individual interfaces for a complete job implementation
public interface IJob : IJobComboHooks, IJobGaugeHooks, IJobActionResolver, IJobActionRegistry, IJobTargetingHooks, IJobIds
{
    // Job identification
    string JobName { get; }

    // Job initialization/cleanup
    void Initialize();
    void Dispose();

    // Configuration validation
    bool IsConfigurationValid(GameSnapshot cache);
}
