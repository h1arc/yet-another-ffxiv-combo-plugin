using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using yetanotherffxivcomboplugin.Snapshot;

namespace yetanotherffxivcomboplugin.Core;

/// <summary>
/// Compiled rule engine: jobs pre-register lightweight rules with optional action sequences.
/// Evaluates conditions per tick, but avoids branching trees and keeps sequence state between frames.
/// </summary>
public class CompiledRule
{
    private readonly List<CompiledAction> _actions = [];
    private bool _sorted;
    // Debounce state per action index; rebuilt on sort to keep alignment simple
    private long[] _lastExecAt = [];
    private Dictionary<int, long>[] _lastExecPerAnchor = [];

    public struct CompiledAction
    {
        public int ActionId;                         // Base action for this rule when not a sequence
        public Func<GameSnapshot, int, bool>? Condition;  // Gate for this rule; arg2 = current anchor id (0 if none)
        public byte Priority;                        // Lower = earlier placement in lane
        public bool IsGcd;                           // True for GCD, false for oGCD
        public ActionSequence? Sequence;             // Optional deterministic sequence handler
        public AnchorBranch? Branch;                 // Optional anchor-aware branching to distinct sequences
        public int DebounceMs;                       // Minimum interval between rule triggers (ms); 0 disables
        public bool DebouncePerAnchor;               // Track debounce separately per anchor id when true
    }

    /// <summary>
    /// Anchor-based branching: choose a sequence based on the current anchor action id.
    /// </summary>
    public readonly struct AnchorBranch
    {
        public readonly Dictionary<int, ActionSequence> Branches; // anchorId -> sequence
        public readonly ActionSequence? DefaultSequence;           // Fallback when no match (optional)

        public AnchorBranch(Dictionary<int, ActionSequence> branches, ActionSequence? @default = null)
        {
            Branches = branches ?? new Dictionary<int, ActionSequence>(0);
            DefaultSequence = @default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int anchorId, out ActionSequence seq)
        {
            if (anchorId != 0 && Branches != null && Branches.TryGetValue(anchorId, out var s))
            { seq = s; return true; }
            if (DefaultSequence != null)
            { seq = DefaultSequence; return true; }
            seq = null!; return false;
        }

        /// <summary>
        /// Helper: common ST/AoE pattern mapping.
        /// </summary>
        public static AnchorBranch StAoe(int stAnchor, ActionSequence stSequence, int aoeAnchor, ActionSequence aoeSequence)
        {
            var dict = new Dictionary<int, ActionSequence>(2)
            {
                { stAnchor, stSequence },
                { aoeAnchor, aoeSequence }
            };
            return new AnchorBranch(dict, null);
        }
    }

    /// <summary>
    /// Deterministic action sequence that advances only on observed action results.
    /// </summary>
    public sealed class ActionSequence(params ActionSequence.SequenceStep[] steps)
    {
        private readonly SequenceStep[] _steps = steps ?? [];
        private int _currentStep = 0;

        public readonly struct SequenceStep(int actionId, bool isGcd, TransitionType transition = TransitionType.Immediate, ushort? expectedBuff = null, Func<GameSnapshot, bool>? continueCondition = null)
        {
            public readonly int ActionId = actionId;
            public readonly bool IsGcd = isGcd;
            public readonly TransitionType Transition = transition;
            public readonly ushort? ExpectedBuff = expectedBuff;                      // Optional for diagnostics/validation
            public readonly Func<GameSnapshot, bool>? ContinueCondition = continueCondition; // Optional gating before advancing
        }

        public enum TransitionType : byte
        {
            Immediate = 0,   // Next step available immediately after use
            Guaranteed = 1,  // Use implies success (e.g., self-buff application)
            Conditional = 2, // Requires ContinueCondition(cache) to advance
            Terminal = 3     // End sequence after this action is observed
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => _currentStep = 0;

        public bool IsAtStart => _currentStep <= 0;
        public bool IsComplete => _currentStep >= _steps.Length;

        /// <summary>
        /// Peek the next action to suggest without advancing the sequence.
        /// Returns false if sequence is complete or has no steps.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPeekNext(out int actionId, out bool isGcd)
        {
            if (_currentStep < 0 || _currentStep >= _steps.Length) { actionId = 0; isGcd = true; return false; }
            var s = _steps[_currentStep];
            actionId = s.ActionId; isGcd = s.IsGcd; return actionId != 0;
        }

        /// <summary>
        /// Advance the sequence based on an observed action result.
        /// Returns true when the current step matched and sequence advanced; startedAtStepZero is true when this was the first step.
        /// </summary>
        public bool OnActionUsed(bool success, int usedActionId, GameSnapshot? cache, out bool startedAtStepZero)
        {
            startedAtStepZero = false;
            if (!success || _currentStep < 0 || _currentStep >= _steps.Length)
                return false;
            var s = _steps[_currentStep];
            if (usedActionId != s.ActionId)
                return false;
            if (_currentStep == 0) startedAtStepZero = true;

            switch (s.Transition)
            {
                case TransitionType.Terminal:
                    _currentStep = _steps.Length; // mark complete
                    break;
                case TransitionType.Conditional:
                    if (s.ContinueCondition != null && cache != null && !s.ContinueCondition(cache))
                    {
                        _currentStep = _steps.Length; // stop sequence if condition failed
                        return true;
                    }
                    _currentStep++;
                    break;
                case TransitionType.Immediate:
                case TransitionType.Guaranteed:
                default:
                    _currentStep++;
                    break;
            }
            return true;
        }

        /// <summary>
        /// Utility: build a generic two-step guaranteed sequence.
        /// </summary>
        public static ActionSequence TwoStepGuaranteed(int firstActionId, bool firstIsGcd, int secondActionId, bool secondIsGcd)
        {
            return new ActionSequence(
                new SequenceStep(firstActionId, firstIsGcd, TransitionType.Guaranteed),
                new SequenceStep(secondActionId, secondIsGcd, TransitionType.Terminal)
            );
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAction(in CompiledAction action)
    {
        _actions.Add(action);
        _sorted = false;
        // Ensure debounce arrays have capacity
        if (_lastExecAt.Length < _actions.Count)
        {
            Array.Resize(ref _lastExecAt, _actions.Count);
            Array.Resize(ref _lastExecPerAnchor, _actions.Count);
        }
    }

    /// <summary>
    /// Notify sequences about observed action usage so they can advance.
    /// Jobs should call this from their OnActionUsed hook.
    /// </summary>
    public void OnActionResult(bool success, int usedActionId, GameSnapshot? cache)
    {
        if (!success || _actions.Count == 0) return;
        long now = Environment.TickCount64;
        for (int i = 0; i < _actions.Count; i++)
        {
            var a = _actions[i];
            // Non-branch sequence
            if (a.Sequence != null)
            {
                if (a.Sequence.OnActionUsed(success, usedActionId, cache, out var started) && started && a.DebounceMs > 0)
                {
                    if (a.DebouncePerAnchor)
                    {
                        // No anchor context for non-branch sequence; fall back to global
                        if (_lastExecAt.Length > i) _lastExecAt[i] = now;
                    }
                    else if (_lastExecAt.Length > i)
                    {
                        _lastExecAt[i] = now;
                    }
                }
            }

            // Branch sequences: update per-branch (anchor-specific) debounce when a branch sequence starts
            if (a.Branch.HasValue)
            {
                var br = a.Branch.Value;
                if (br.Branches != null)
                {
                    foreach (var kv in br.Branches)
                    {
                        var seq = kv.Value;
                        if (seq != null && seq.OnActionUsed(success, usedActionId, cache, out var started) && started && a.DebounceMs > 0)
                        {
                            if (a.DebouncePerAnchor)
                            {
                                var map = _lastExecPerAnchor[i] ??= new Dictionary<int, long>(2);
                                map[kv.Key] = now;
                            }
                            else if (_lastExecAt.Length > i)
                            {
                                _lastExecAt[i] = now;
                            }
                        }
                    }
                }
                // Default sequence does not have an anchor; treat as global
                if (br.DefaultSequence != null && br.DefaultSequence.OnActionUsed(success, usedActionId, cache, out var defStarted) && defStarted && a.DebounceMs > 0)
                {
                    if (_lastExecAt.Length > i) _lastExecAt[i] = now;
                }
            }

            // Direct actions (no sequence/branch): if the action id matches, set debounce globally
            if (a.Sequence == null && !a.Branch.HasValue && a.ActionId != 0 && a.DebounceMs > 0 && usedActionId == a.ActionId)
            {
                if (_lastExecAt.Length > i) _lastExecAt[i] = now;
            }
        }
    }

    /// <summary>
    /// Execute compiled actions in ascending priority order and emit suggestions.
    /// </summary>
    public void Execute(GameSnapshot cache, int currentAnchor, ref PlanBuilder b)
    {
        if (_actions.Count == 0) return;
        if (!_sorted)
        {
            _actions.Sort(static (x, y) => x.Priority.CompareTo(y.Priority));
            _sorted = true;
            // Reset debounce state to align with sorted order (safe to clear)
            _lastExecAt = new long[_actions.Count];
            _lastExecPerAnchor = new Dictionary<int, long>[_actions.Count];
        }

        // Iterate in priority order; append to plan while capacity allows.
        for (int i = 0; i < _actions.Count; i++)
        {
            var a = _actions[i];
            if (a.Condition != null && !a.Condition(cache, currentAnchor))
                continue;

            // Determine which sequence to use: explicit Sequence or branch-selected sequence
            ActionSequence? seqToUse = a.Sequence;
            if (a.Branch.HasValue)
            {
                if (a.Branch.Value.TryGet(currentAnchor, out var branchSeq))
                    seqToUse = branchSeq;
            }

            // Debounce: only enforce when at the start of a sequence (or no sequence)
            int debounceMs = a.DebounceMs;
            if (debounceMs > 0)
            {
                bool atStart = seqToUse == null || seqToUse.IsAtStart;
                if (atStart)
                {
                    long now = Environment.TickCount64;
                    long last = 0;
                    if (a.DebouncePerAnchor)
                    {
                        var map = _lastExecPerAnchor[i] ??= new Dictionary<int, long>(2);
                        if (map.TryGetValue(currentAnchor, out var ts)) last = ts;
                        if (now - last < debounceMs) continue;
                        // eligible; we'll update timestamp after we enqueue suggestion
                    }
                    else
                    {
                        last = _lastExecAt.Length > i ? _lastExecAt[i] : 0;
                        if (now - last < debounceMs) continue;
                    }
                }
            }

            // If a sequence exists (possibly branch-selected), suggest the current step; otherwise suggest the action itself.
            if (seqToUse != null)
            {
                // Restart the sequence if condition is true but sequence finished previously
                if (!seqToUse.TryPeekNext(out var seqAction, out var seqIsGcd) || seqAction == 0)
                {
                    seqToUse.Reset();
                    if (!seqToUse.TryPeekNext(out seqAction, out seqIsGcd) || seqAction == 0)
                        continue;
                }

                var s = new Suggestion(seqIsGcd, seqAction, a.Priority);
                if (seqIsGcd) { if (!b.TryAddGcd(s)) return; }
                else { if (!b.TryAddOgcd(s)) return; }
                continue;
            }

            if (a.ActionId == 0)
                continue;
            var sug = new Suggestion(a.IsGcd, a.ActionId, a.Priority);
            if (a.IsGcd)
            {
                if (!b.TryAddGcd(sug)) return;
            }
            else
            {
                if (!b.TryAddOgcd(sug)) return;
            }
            // For direct actions, debounce timestamp will be set on successful OnActionResult
        }
    }
}
