using System;
using System.Runtime.CompilerServices;
using yetanotherffxivcomboplugin.src.Snapshot;

namespace yetanotherffxivcomboplugin.src.Core;

/// <summary>
/// Fast action resolver using compiled rules. Caches per-frame anchor resolution.
/// (Ported from lastoptimized version for use without Planner.)
/// </summary>
public sealed class ActionResolver
{
    private readonly CompiledRule[] _rules = new CompiledRule[8];
    private readonly int[] _anchors = new int[8];
    private byte _ruleCount;
    private int _cachedAction;
    private bool _cachedIsGcd;
    private int _cachedForAnchor;
    private ulong _lastResolveFrame;
    private int _lastOgcdSuggested;
    private byte _lastOgcdPriority;
    private ulong _lastOgcdFrame;
    private long _lastOgcdSuccessTick;
    private int _lastActionUsed;
    private long _lastActionUsedTick;

    // Exposed (read-only) debug data
    public int LastAnchor => _cachedForAnchor;
    public int LastResolved => _cachedAction;
    public bool LastResolvedIsGcd => _cachedIsGcd;
    public ulong LastResolvedFrame => _lastResolveFrame;
    public int LastOgcdSuggested => _lastOgcdSuggested;
    public byte LastOgcdPriority => _lastOgcdPriority;
    public ulong LastOgcdFrame => _lastOgcdFrame;
    public byte RuleCount => _ruleCount;
    public int LastActionUsed => _lastActionUsed;
    public long LastActionUsedTick => _lastActionUsedTick;
    public int OgcdThrottleRemainingMs
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var rem = 400 - (int)(Environment.TickCount64 - _lastOgcdSuccessTick);
            return rem > 0 ? rem : 0;
        }
    }

#if DEBUG
    // DEBUG ONLY: copy current anchor list for more accurate debug UI (avoids heuristic probing there)
    public int CopyAnchors(Span<int> dst)
    {
        var n = _ruleCount;
        if (n > dst.Length) n = (byte)dst.Length;
        for (int i = 0; i < n; i++) dst[i] = _anchors[i];
        return n;
    }
#endif

    // Fast anchor membership check to avoid stackalloc each call.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAnchor(int actionId)
    {
        for (byte i = 0; i < _ruleCount; i++)
            if (_anchors[i] == actionId) return true;
        return false;
    }

    // Opener logic fully removed; future opener module can drive substitution externally if reintroduced.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearRules() { _ruleCount = 0; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRules(ReadOnlySpan<CompiledRule> rules)
    {
        var count = rules.Length;
        if (count > 8) count = 8;
        _ruleCount = (byte)count;
        for (int i = 0; i < count; i++)
        {
            var r = rules[i];
            _rules[i] = r;
            _anchors[i] = r.Anchor;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnActionUsed(bool success, int usedActionId)
    {
        if (!success) return;
        _lastActionUsed = usedActionId;
        _lastActionUsedTick = Environment.TickCount64;
        for (byte i = 0; i < _ruleCount; i++)
            _rules[i].OnActionUsed(usedActionId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int actionId, bool isGcd) Resolve(GameSnapshot cache, int pressedActionId)
    {
        if (_cachedForAnchor == pressedActionId && _lastResolveFrame == cache.FrameCount)
            return (_cachedAction, _cachedIsGcd);

        CompiledRule? rule = null;
        for (byte i = 0; i < _ruleCount; i++)
        {
            if (_anchors[i] == pressedActionId) { rule = _rules[i]; break; }
        }
        if (rule == null)
        {
            _cachedAction = pressedActionId; _cachedIsGcd = true; _cachedForAnchor = pressedActionId; _lastResolveFrame = cache.FrameCount; return (pressedActionId, true);
        }
        var (resId, isGcd) = rule.Evaluate(cache, pressedActionId);
        _cachedAction = resId; _cachedIsGcd = isGcd; _cachedForAnchor = pressedActionId; _lastResolveFrame = cache.FrameCount;
        return (resId, isGcd);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int actionId, bool isOgcd, ulong target) GetNextOgcd(GameSnapshot snap)
    {
        var now = Environment.TickCount64;
        if (now - _lastOgcdSuccessTick < 400) return (0, false, 0);
        int bestId = 0; byte bestPrio = 0; ulong chosenTarget = 0;
        for (byte i = 0; i < _ruleCount; i++)
        {
            var (id, prio, hint) = _rules[i].EvaluateOgcd(snap);
            if (id == 0) continue;
            if (!snap.IsActionReady(id)) continue;
            if (snap.TryGetCooldownRemainingMs(id, out var remMs) && remMs > 0) continue;
            ulong tgt = 0; // target hints currently ignored
            if (bestId == 0 || prio > bestPrio) { bestId = id; bestPrio = prio; chosenTarget = tgt; }
        }
        if (bestId != 0)
        { _lastOgcdSuggested = bestId; _lastOgcdPriority = bestPrio; _lastOgcdFrame = snap.FrameCount; }
        return (bestId, bestId != 0, chosenTarget);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public void MarkOgcdSuccess() => _lastOgcdSuccessTick = Environment.TickCount64;
}
