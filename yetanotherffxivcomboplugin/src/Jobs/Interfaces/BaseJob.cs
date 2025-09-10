using System;
using yetanotherffxivcomboplugin.src.Core;
using yetanotherffxivcomboplugin.src.Snapshot;

namespace yetanotherffxivcomboplugin.src.Jobs.Interfaces;

public abstract class BaseJob : IJob
{
    public abstract ushort JobId { get; }
    public abstract string JobName { get; }
    // Optional per-job retraceable (auto-target) single-target heal / utility actions.
    // Returning null or empty means no special handling beyond generic logic.
    public virtual ReadOnlySpan<int> RetraceActions => ReadOnlySpan<int>.Empty;

    protected ActionResolver? Resolver { get; private set; }

    public virtual void Initialize() { }
    public virtual void Dispose() { }

    public void RegisterRules(ActionResolver resolver, GameSnapshot snapshot)
    {
        Resolver = resolver;
        ConfigureRules(snapshot);
    }

    protected abstract void ConfigureRules(GameSnapshot snapshot);

    public virtual void OnActionUsed(bool success, int actionId, GameSnapshot snapshot) { }
}
