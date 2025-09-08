using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Generic;


namespace yetanotherffxivcomboplugin.Core;

/// <summary>
/// Minimal retrace registry: maps actionId to a target object id with optional culling.
/// Call TryGetTargetFor before executing an action to apply a target override.
/// </summary>
[SkipLocalsInit]
public sealed class RetraceRegistry
{
    private readonly Dictionary<int, Entry> _byAction = new(capacity: 64);
    private long _nextSweepTicks = 0; // throttle culling

    public readonly struct Entry(int actionId, ulong targetId, bool dontCull, bool oneShot)
    {
        public readonly int ActionId = actionId;
        public readonly ulong TargetId = targetId;
        public readonly long CreatedTicks = DateTime.UtcNow.Ticks;
        public readonly bool DontCull = dontCull;
        public readonly bool OneShot = oneShot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddOrUpdate(int actionId, ulong targetId, bool dontCull = false, bool oneShot = false)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_byAction, actionId, out _);
        slot = new Entry(actionId, targetId, dontCull, oneShot);
        // quiet: avoid verbose spam in hot path
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetTargetFor(int actionId, out ulong targetId)
    {
        ref var e = ref CollectionsMarshal.GetValueRefOrNullRef(_byAction, actionId);
        if (!Unsafe.IsNullRef(ref e)) { targetId = e.TargetId; return true; }
        targetId = 0; return false;
    }

    /// <summary>
    /// Like TryGetTargetFor, but consumes one-shot entries.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryConsume(int actionId, out ulong targetId)
    {
        ref var e = ref CollectionsMarshal.GetValueRefOrNullRef(_byAction, actionId);
        if (!Unsafe.IsNullRef(ref e))
        {
            targetId = e.TargetId;
            if (e.OneShot) _byAction.Remove(actionId);
            return true;
        }
        targetId = 0; return false;
    }

    public void ClearOld(TimeSpan maxAge)
    {
        var now = DateTime.UtcNow.Ticks;
        if (now < _nextSweepTicks) return;
        _nextSweepTicks = now + TimeSpan.FromSeconds(11).Ticks; // sweep every ~11s

        var cutoff = now - maxAge.Ticks;
        var toRemove = new List<int>(8);
        foreach (var kv in _byAction)
        {
            var e = kv.Value;
            if (!e.DontCull && e.CreatedTicks < cutoff)
                toRemove.Add(kv.Key);
        }
        for (var i = 0; i < toRemove.Count; i++)
            _byAction.Remove(toRemove[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearAll() => _byAction.Clear();
}

// RetracePolicy removed for now; range gating is deferred to avoid overhead.
