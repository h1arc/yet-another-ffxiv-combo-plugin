using System;
using System.Collections.Generic;
using yetanotherffxivcomboplugin.src.Core;
using yetanotherffxivcomboplugin.src.Snapshot;

namespace yetanotherffxivcomboplugin.src.Jobs.Interfaces;

public static class JobRegistry
{
    private static readonly Dictionary<ushort, IJob> _jobs = [];
    private static IJob? _current;
    private static ushort _currentId;

    static JobRegistry()
    {
        RegisterJob(new WHM.WHM());
        RegisterJob(new SGE.SGE());
    }

    public static void RegisterJob(IJob job)
    {
        _jobs[job.JobId] = job;
        job.Initialize();
    }

    public static void SetJob(ushort jobId, GameSnapshot snapshot, ActionResolver resolver)
    {
        if (jobId == 0) return;
        if (_currentId == jobId && _current != null)
        {
            resolver.ClearRules();
            _current.RegisterRules(resolver, snapshot);
            return;
        }
        _current?.Dispose();
        _current = null; _currentId = 0;
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _current = job; _currentId = jobId;
            resolver.ClearRules();
            job.RegisterRules(resolver, snapshot);
        }
    }

    public static void OnActionUsed(bool success, int actionId, GameSnapshot snapshot)
    {
        _current?.OnActionUsed(success, actionId, snapshot);
        Plugin.Runtime.Resolver?.OnActionUsed(success, actionId);
    }

    public static IJob? Current => _current;
    public static IEnumerable<IJob> All => _jobs.Values;
}
