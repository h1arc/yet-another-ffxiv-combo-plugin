using System;
using System.Collections.Generic;
using yetanotherffxivcomboplugin.Core;
using yetanotherffxivcomboplugin.Snapshot;

namespace yetanotherffxivcomboplugin.Jobs.Interfaces;

/// <summary>
/// Centralized job registry and factory for standardized job management.
/// This replaces the delegate-based approach in JobProfiles with interface-based jobs.
/// </summary>
public static class JobRegistry
{
    private static readonly Dictionary<ushort, IJob> _jobs = new();
    private static IJob? _currentJob;
    private static ushort _currentJobId;

    static JobRegistry()
    {
        RegisterJob(new WHM.WHM());
        RegisterJob(new SGE.SGE());
        // RegisterJob(new SCHJob());
        // RegisterJob(new ASTJob());
        // RegisterJob(new SGEJob());
        // etc.
    }

    public static void RegisterJob(IJob job)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));

        _jobs[job.JobId] = job;
        job.Initialize();
    }

    public static void UnregisterJob(ushort jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Dispose();
            _jobs.Remove(jobId);

            if (_currentJobId == jobId)
            {
                _currentJob = null;
                _currentJobId = 0;
            }
        }
    }

    public static void ApplyForJob(ushort jobId, GameSnapshot cache, Planner? planner = null)
    {
        if (jobId == 0) return; // Ignore transient zero

        // If job stays the same, we still need to (re)configure and (re)register rules, because
        // the planner may have been Reset() on territory/level/job events (clearing job rules).
        if (_currentJobId == jobId && _currentJob != null)
        {
            if (_currentJob.IsConfigurationValid(cache))
            {
                _currentJob.ConfigureJob(cache);
                _currentJob.RegisterPlannerRules(planner);
            }
            return;
        }

        // Clear previous job
        _currentJob?.ClearJob(cache);

        // Set new job
        _currentJobId = jobId;
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _currentJob = job;

            if (job.IsConfigurationValid(cache))
            {
                job.ConfigureJob(cache);
                job.RegisterPlannerRules(planner);
                // Note: Retraceable actions now handled directly via job interface
            }
        }
        else
        {
            _currentJob = null;
            // Default cleanup for unsupported jobs
            cache.ConfigureRecasts([]);
            // Note: No retraceable actions to clear since they're handled per-job
        }
    }

    public static IJob? GetCurrentJob() => _currentJob;

    public static T? GetCurrentJob<T>() where T : class, IJob => _currentJob as T;

    public static IJob? GetJob(ushort jobId) => _jobs.TryGetValue(jobId, out var job) ? job : null;

    public static T? GetJob<T>(ushort jobId) where T : class, IJob => GetJob(jobId) as T;

    public static bool IsJobSupported(ushort jobId) => _jobs.ContainsKey(jobId);

    public static IEnumerable<IJob> GetAllJobs() => _jobs.Values;

    public static void OnActionUsed(bool success, int usedActionId, GameSnapshot? snap)
    {
        // Forward action usage to current job for gauge/state tracking
        _currentJob?.OnActionUsed(success, usedActionId, snap);
    }

    public static void Shutdown()
    {
        _currentJob?.ClearJob(Plugin.GameSnapshot!);
        _currentJob = null;
        _currentJobId = 0;

        foreach (var job in _jobs.Values)
        {
            job.Dispose();
        }
        _jobs.Clear();
    }
}
