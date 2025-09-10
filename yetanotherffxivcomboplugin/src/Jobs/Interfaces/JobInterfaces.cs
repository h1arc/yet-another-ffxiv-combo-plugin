using System;
using yetanotherffxivcomboplugin.src.Core;
using yetanotherffxivcomboplugin.src.Snapshot;

namespace yetanotherffxivcomboplugin.src.Jobs.Interfaces;

// Slim job contract: jobs register rules and optionally observe action usage.
public interface IJob
{
    ushort JobId { get; }
    string JobName { get; }
    ReadOnlySpan<int> RetraceActions { get; } // actions eligible for auto target retrace

    // Lifecycle
    void Initialize();
    void Dispose();

    // Register job-specific compiled rules.
    void RegisterRules(ActionResolver resolver, GameSnapshot snapshot);

    // Optional notification after an action is used.
    void OnActionUsed(bool success, int actionId, GameSnapshot snapshot) { }
}
