using System;
using System.Runtime.CompilerServices;

namespace yetanotherffxivcomboplugin.src.Core;

/// <summary>
/// Ultra-lean opener representation: a fixed array of steps with no embedded logic.
/// </summary>
public readonly struct Opener(params Opener.Step[] steps)
{
    public readonly Step[] Steps = steps ?? [];

    public readonly struct Step(int actionId, bool isGcd)
    {
        public readonly int ActionId = actionId;
        public readonly bool IsGcd = isGcd;
    }
}

/// <summary>
/// Minimal opener executor that walks the Opener steps in order.
/// </summary>
public sealed class OpenerExecutor(Opener opener)
{
    private readonly Opener _opener = opener;
    private int _idx = 0;
    private bool _active = false;
    private long _timeoutAt;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start(int timeoutMs = 30000)
    {
        _idx = 0;
        _active = _opener.Steps.Length > 0;
        _timeoutAt = Environment.TickCount64 + timeoutMs;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        _idx = 0; _active = false; _timeoutAt = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsActive()
    {
        if (!_active) return false;
        if (Environment.TickCount64 >= _timeoutAt) { Reset(); return false; }
        if (_idx >= _opener.Steps.Length) { Reset(); return false; }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetNext(out Opener.Step step)
    {
        if (!IsActive()) { step = default; return false; }
        step = _opener.Steps[_idx];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnActionUsed(bool success, int usedActionId)
    {
        if (!success || !IsActive()) return;
        var s = _opener.Steps[_idx];
        if (s.ActionId == usedActionId) _idx++;
    }
}
