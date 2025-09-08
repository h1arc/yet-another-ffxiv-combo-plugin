using System;
using System.Runtime.CompilerServices;
using yetanotherffxivcomboplugin.Snapshot;

namespace yetanotherffxivcomboplugin.Core;

public enum OpenerFamily : byte { None = 0 }

public readonly struct OpenerStep(bool isGcd, int actionId, OpenerFamily family = OpenerFamily.None, bool allowFamilyFallback = false)
{
    public bool IsGcd { get; } = isGcd;
    public int ActionId { get; } = actionId; // exact action id to execute
    public OpenerFamily Family { get; } = family;
    public bool AllowFamilyFallback { get; } = allowFamilyFallback;
}

public readonly struct Suggestion(bool isGcd, int actionId, byte order = 0)
{
    public readonly bool IsGcd = isGcd;
    public readonly int ActionId = actionId;
    public readonly byte Order = order;
}

public interface IRule
{
    void Reset();
    void Apply(in PlannerContext ctx, ref PlanBuilder b);
}

public readonly struct PlannerContext(GameSnapshot cache, long now, int pressedActionId = 0)
{
    public readonly GameSnapshot Cache = cache;
    public readonly long Now = now;
    public readonly int PressedActionId = pressedActionId;
}

public ref struct PlanBuilder(Span<Suggestion> gcd, Span<Suggestion> ogcd)
{
    private Span<Suggestion> _gcd = gcd;
    private Span<Suggestion> _ogcd = ogcd;
    private byte _g = 0;
    private byte _o = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public bool TryAddGcd(in Suggestion s) { if (_g >= _gcd.Length) return false; _gcd[_g++] = s; return true; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public bool TryAddOgcd(in Suggestion s) { if (_o >= _ogcd.Length) return false; _ogcd[_o++] = s; return true; }
}

public sealed class Plan
{
    private readonly Suggestion[] _gcd = new Suggestion[4];
    private readonly Suggestion[] _ogcd = new Suggestion[6];
    public ReadOnlySpan<Suggestion> Gcd => _gcd;
    public ReadOnlySpan<Suggestion> Ogcd => _ogcd;
    internal Span<Suggestion> GcdMut => _gcd;
    internal Span<Suggestion> OgcdMut => _ogcd;
    internal void Clear()
    {
        Array.Clear(_gcd, 0, _gcd.Length);
        Array.Clear(_ogcd, 0, _ogcd.Length);
    }
}

// Removed job-specific ActionFamily mapping; jobs provide any family keys in their own rules.

public sealed class OpenerSequenceRule : IRule
{
    private OpenerStep[] _script = [];
    private int _idx = -1;
    private long _timeout;

    public void Reset()
    {
        _script = [];
        _idx = -1;
        _timeout = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Arm(Func<OpenerStep[]> factory)
    {
        _script = factory() ?? [];
        _idx = _script.Length > 0 ? 0 : -1;
        _timeout = Environment.TickCount64 + 30000; // 30s safety
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnActionResult(bool success, int usedActionId)
    {
        if (!success || _idx < 0 || _idx >= _script.Length) return;
        var step = _script[_idx];
        var match = usedActionId == step.ActionId;
        // Family fallback removed from engine; jobs may encode fallback behavior in their rules.
        if (match) _idx++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Apply(in PlannerContext ctx, ref PlanBuilder b)
    {
        if (_idx < 0 || _idx >= _script.Length) return;
        if (ctx.Now >= _timeout) { _idx = -1; return; }
        // Emit OGCDs that precede the next GCD (if any), then the next GCD
        int i = _idx;
        int j = i;
        while (j < _script.Length && !_script[j].IsGcd)
        {
            var og = _script[j];
            // Only suggest if ready now; planner remains stateless about recast order
            if (ctx.Cache.IsActionReady(og.ActionId))
                b.TryAddOgcd(new Suggestion(false, og.ActionId, (byte)(j - i)));
            j++;
        }
        // Suggest the next GCD (if present)
        if (j >= _script.Length) return;
        var step = _script[j];
        b.TryAddGcd(new Suggestion(true, step.ActionId, 0));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsActive(long now)
    {
        if (_idx < 0 || _idx >= _script.Length) return false;
        if (now >= _timeout) { _idx = -1; return false; }
        return true;
    }
}

public sealed class Planner
{
    private readonly Plan _plan = new();
    private readonly OpenerSequenceRule _opener = new();
    private bool _armed;
    private int _pressedActionHint;
    private int _lastGcdAnchorHint;
    // Sticky active anchor: persists until a different anchor is pressed/used
    private int _activeAnchor;
    // Cache last-seen anchors from the snapshot so OnActionResult can resolve anchors without cache
    private readonly int[] _lastAnchors = new int[8];
    private byte _lastAnchorCount;
    public delegate void JobRule(in PlannerContext ctx, ref PlanBuilder b);
    private JobRule? _jobRule;
    private ulong _lastPlanHash;

    public ReadOnlySpan<Suggestion> Gcd => _plan.Gcd;
    public ReadOnlySpan<Suggestion> Ogcd => _plan.Ogcd;
    public int ActiveAnchor => _activeAnchor;
    public ReadOnlySpan<int> LastAnchors => _lastAnchors.AsSpan(0, _lastAnchorCount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        _opener.Reset();
        _armed = false;
        _jobRule = null;
        _lastGcdAnchorHint = 0;
        _activeAnchor = 0;
        _lastAnchorCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ArmOpener(Func<OpenerStep[]> factory)
    {
        _opener.Arm(factory);
        _armed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnActionResult(bool success, int usedActionId)
    {
        _opener.OnActionResult(success, usedActionId);
        if (!success || usedActionId == 0) return;
        // If the used action matches a recently seen anchor, adopt it as the active anchor
        for (int i = 0; i < _lastAnchorCount; i++)
        {
            if (_lastAnchors[i] == usedActionId)
            {
                _activeAnchor = usedActionId;
                break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetJobRule(JobRule? rule)
        => _jobRule = rule;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPressedActionHint(int actionId)
        => _pressedActionHint = actionId;

    /// <summary>
    /// Build a transient suggestion set for the given pressed action id without mutating the persistent plan.
    /// Used by the hook-time resolver to get up-to-date job-rule suggestions with the current hint.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void BuildForPressed(GameSnapshot cache, int pressedActionId, Span<Suggestion> gcdOut, Span<Suggestion> ogcdOut)
    {
        gcdOut.Clear();
        ogcdOut.Clear();
        var now = Environment.TickCount64;
        // Update sticky anchor immediately when a valid anchor press occurs
        if (pressedActionId != 0 && cache.IsAnchorAction(pressedActionId))
            _activeAnchor = pressedActionId;

        var hint = pressedActionId != 0 ? pressedActionId : _activeAnchor;
        var ctx = new PlannerContext(cache, now, hint);
        var b = new PlanBuilder(gcdOut, ogcdOut);
        // Include opener if armed and active
        if (_armed)
        {
            if (_opener.IsActive(ctx.Now))
                _opener.Apply(in ctx, ref b);
            else
                _armed = false;
        }
        // Job rule next
        _jobRule?.Invoke(in ctx, ref b);

        // Append a fallback anchor suggestion to keep lane stable for this press
        var anchors = cache.Anchors;
        // Cache anchors for OnActionResult usage
        _lastAnchorCount = (byte)Math.Min(anchors.Length, _lastAnchors.Length);
        for (int i = 0; i < _lastAnchorCount; i++) _lastAnchors[i] = anchors[i];
        if (anchors.Length > 0)
        {
            int fallback = 0;
            if (_activeAnchor != 0 && cache.IsAnchorAction(_activeAnchor))
                fallback = _activeAnchor;
            else if (pressedActionId != 0 && cache.IsAnchorAction(pressedActionId))
                fallback = pressedActionId;
            else if (_lastGcdAnchorHint != 0 && cache.IsAnchorAction(_lastGcdAnchorHint))
                fallback = _lastGcdAnchorHint;
            else fallback = anchors[0];

            if (fallback != 0)
            {
                var mut = gcdOut;
                bool hasAnchor = false;
                int lastNonEmpty = -1;
                for (int i = 0; i < mut.Length; i++)
                {
                    if (mut[i].ActionId == fallback) { hasAnchor = true; break; }
                    if (mut[i].ActionId != 0) lastNonEmpty = i;
                }
                if (!hasAnchor && lastNonEmpty < mut.Length - 1)
                {
                    mut[lastNonEmpty + 1] = new Suggestion(true, fallback, 250);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Build(GameSnapshot cache)
    {
        // Build into temporaries to avoid mutating the plan unless contents actually change
        Span<Suggestion> gTemp = stackalloc Suggestion[4]; gTemp.Clear();
        Span<Suggestion> oTemp = stackalloc Suggestion[6]; oTemp.Clear();
        var now = Environment.TickCount64;
        var hint = _pressedActionHint;
        // Update sticky anchor when a valid anchor is pressed
        if (hint != 0 && cache.IsAnchorAction(hint))
            _activeAnchor = hint;

        // Use sticky anchor as the default hint when no current press is active
        if (hint == 0 && _activeAnchor != 0)
            hint = _activeAnchor;

        _pressedActionHint = 0;
        var ctx = new PlannerContext(cache, now, hint);
        var b = new PlanBuilder(gTemp, oTemp);
        // Opener first (if armed)
        if (_armed)
        {
            if (_opener.IsActive(ctx.Now))
                _opener.Apply(in ctx, ref b);
            else
                _armed = false;
        }
        // Job rule next
        _jobRule?.Invoke(in ctx, ref b);

        // Stable filler: always append a filler anchor as the last GCD suggestion if available and not already present.
        // This keeps the GCD lane stable between presses without adding extra gating.
        var anchors = cache.Anchors;
        // Cache anchors for OnActionResult usage
        _lastAnchorCount = (byte)Math.Min(anchors.Length, _lastAnchors.Length);
        for (int i = 0; i < _lastAnchorCount; i++) _lastAnchors[i] = anchors[i];
        if (anchors.Length > 0)
        {
            int fallback = 0;
            if (_activeAnchor != 0 && cache.IsAnchorAction(_activeAnchor))
                fallback = _activeAnchor; // sticky active anchor
            else if (hint != 0 && cache.IsAnchorAction(hint))
                fallback = hint; // current press
            else if (_lastGcdAnchorHint != 0 && cache.IsAnchorAction(_lastGcdAnchorHint))
                fallback = _lastGcdAnchorHint; // historical
            else fallback = anchors[0]; // stable default

            if (fallback != 0)
            {
                var mut = gTemp;
                bool hasAnchor = false;
                int lastNonEmpty = -1;

                // Single pass: detect existing fallback and track last filled slot
                for (int i = 0; i < mut.Length; i++)
                {
                    if (mut[i].ActionId == fallback)
                    {
                        hasAnchor = true;
                        break;
                    }
                    if (mut[i].ActionId != 0)
                        lastNonEmpty = i;
                }

                // Only add if not present and there's space to append at the end
                if (!hasAnchor && lastNonEmpty < mut.Length - 1)
                {
                    mut[lastNonEmpty + 1] = new Suggestion(true, fallback, 250);
                }
            }
        }
        // Commit if plan contents changed; otherwise skip hashing entirely
        bool dirty = !SeqEqual(_plan.Gcd, gTemp) || !SeqEqual(_plan.Ogcd, oTemp);
        if (!dirty)
            return false;
        // Copy new contents into the plan buffers
        gTemp.CopyTo(_plan.GcdMut);
        oTemp.CopyTo(_plan.OgcdMut);
        // Compute a lightweight hash of the current plan to detect changes for downstream consumers
        var h = HashPlan();
        bool changed = h != _lastPlanHash;
        _lastPlanHash = h;
        return changed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong HashPlan()
    {
        // FNV-1a 64-bit over (isGcd, actionId, order) triplets for both arrays
        const ulong offset = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        var g = _plan.Gcd;
        for (int i = 0; i < g.Length; i++)
        {
            var s = g[i];
            if (s.ActionId == 0) continue; // ignore empty slots to avoid false stability
            unchecked
            {
                hash ^= s.IsGcd ? 1UL : 0UL; hash *= prime;
                hash ^= (uint)s.ActionId; hash *= prime;
                hash ^= s.Order; hash *= prime;
            }
        }
        var o = _plan.Ogcd;
        for (int i = 0; i < o.Length; i++)
        {
            var s = o[i];
            if (s.ActionId == 0) continue; // ignore empty slots
            unchecked
            {
                hash ^= s.IsGcd ? 1UL : 0UL; hash *= prime;
                hash ^= (uint)s.ActionId; hash *= prime;
                hash ^= s.Order; hash *= prime;
            }
        }
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SeqEqual(ReadOnlySpan<Suggestion> a, ReadOnlySpan<Suggestion> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.IsGcd != y.IsGcd || x.ActionId != y.ActionId || x.Order != y.Order)
                return false;
        }
        return true;
    }
}
