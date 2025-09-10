using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using yetanotherffxivcomboplugin.src.Snapshot;

namespace yetanotherffxivcomboplugin.src.Core;

/// <summary>
/// Optimized CompiledRule: single anchor per rule with fixed-size action array.
/// Uses compact state storage and aggressive inlining for performance.
/// </summary>
public abstract class CompiledRule
{
    // Use fixed-size array instead of List for actions
    private readonly CompiledAction[] _actions = new CompiledAction[32];
    private byte _actionCount;

    // Compact state storage (action ID -> last execution time)
    private readonly Dictionary<int, long> _debounceState = new(8);

    public abstract int Anchor { get; } // Single anchor per rule

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HandlesAnchor(int anchor) => Anchor == anchor;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int actionId, bool isGcd) Evaluate(GameSnapshot cache, int anchor)
    {
        var now = Environment.TickCount64;

        // Iterate only populated actions
        for (byte i = 0; i < _actionCount; i++)
        {
            ref readonly var action = ref _actions[i];
            // Primary (anchor) evaluation previously skipped all non-GCD actions; however
            // we need to allow sequence wrappers whose first steps are oGCD abilities
            // (e.g. Swiftcast -> Thin Air -> Raise) to substitute onto the anchor key.
            // To keep rotation cleanliness we *still* ignore bare oGCD actions (no sequence)
            // so that pure cooldown abilities are only injected via the weave scanner.
            if (!action.IsGcd && action.Sequence == null) continue;

            // Early debounce gate applies only to non-sequence actions.
            if (action.Sequence == null && action.DebounceMs > 0 && action.ActionId != 0)
            {
                if (_debounceState.TryGetValue(action.ActionId, out var lastTime))
                {
                    if (now - lastTime < action.DebounceMs)
                        continue;
                }
            }

            // Condition check (inlined delegate call)
            if (action.Condition(cache, anchor))
            {
                // Handle sequence inline with step-aware debounce
                if (action.Sequence != null)
                {
                    if (action.Sequence.TryGetCurrentStep(cache, out var seqAction, out var seqIsGcd) && seqAction != 0)
                    {
                        // If terminal action is inside debounce, suppress the whole sequence this tick
                        // to avoid re-opening with step 1 (e.g., Eukrasia) during long terminal animations.
                        if (action.DebounceMs > 0 && action.ActionId != 0)
                        {
                            if (_debounceState.TryGetValue(action.ActionId, out var lastTime) && now - lastTime < action.DebounceMs)
                                continue;
                        }
                        // Otherwise, offer the current sequence step.
                        return (seqAction, seqIsGcd);
                    }
                    continue; // sequence not ready to advance
                }

                // Non-sequence action: apply debounce immediately
                if (action.DebounceMs > 0 && action.ActionId != 0)
                {
                    if (_debounceState.TryGetValue(action.ActionId, out var lastTime))
                    {
                        if (now - lastTime < action.DebounceMs)
                            continue;
                    }
                    _debounceState[action.ActionId] = now;
                }
                return (action.ActionId, action.IsGcd);
            }
        }
        return (anchor, true); // Fallback
    }

    /// <summary>
    /// Evaluate this rule for the first matching oGCD (non-GCD) action whose condition passes.
    /// Returns (0, false) when no oGCD candidate found.
    /// Note: ordering relies on insertion order (caller is responsible for priority ordering when adding actions).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (int actionId, byte priority, CompiledAction.TargetHint targetHint) EvaluateOgcd(GameSnapshot cache)
    {
        var now = Environment.TickCount64;
        for (byte i = 0; i < _actionCount; i++)
        {
            ref readonly var action = ref _actions[i];
            if (action.IsGcd) continue; // only consider non-GCD (oGCD) actions

            // For sequences, debounce should only apply when suggesting the terminal mapped action.
            // For non-sequence actions, apply debounce as usual before condition evaluation.
            if (action.Sequence == null)
            {
                if (action.DebounceMs > 0 && action.ActionId != 0 && _debounceState.TryGetValue(action.ActionId, out var lastTime))
                {
                    if (now - lastTime < action.DebounceMs)
                        continue;
                }
            }

            if (action.Condition(cache, Anchor))
            {
                if (action.Sequence != null)
                {
                    if (action.Sequence.TryGetCurrentStep(cache, out var seqAction, out _) && seqAction != 0)
                    {
                        // If terminal action is inside debounce, suppress the sequence for oGCD pass too.
                        if (action.DebounceMs > 0 && action.ActionId != 0)
                        {
                            if (_debounceState.TryGetValue(action.ActionId, out var lastTime) && now - lastTime < action.DebounceMs)
                                continue;
                        }
                        return (seqAction, action.Priority, CompiledAction.TargetHint.Auto);
                    }
                    continue; // sequence not progressed yet
                }

                if (action.DebounceMs > 0 && action.ActionId != 0)
                    _debounceState[action.ActionId] = now;

                return (action.ActionId, action.Priority, action.Targeting);
            }
        }
        return (0, 0, CompiledAction.TargetHint.Auto);
    }

    protected void AddAction(in CompiledAction action)
    {
        if (_actionCount < _actions.Length)
            _actions[_actionCount++] = action;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnActionUsed(int usedActionId)
    {
        // Update sequences
        for (byte i = 0; i < _actionCount; i++)
        {
            _actions[i].Sequence?.OnActionUsed(usedActionId);
            // Also progress sequences when a preparatory action for the mapped action is pressed.
            // Example: Eukrasia -> Eukrasian Dosis: when Eukrasia (24290) is used, we want the sequence
            // to move to the next step so the next suggestion becomes EDosis rather than re-suggesting Eukrasia.

            // Debounce stamp on actual use for terminal step actions
            if (_actions[i].Sequence != null && _actions[i].ActionId == usedActionId && _actions[i].DebounceMs > 0 && _actions[i].ActionId != 0)
            {
                _debounceState[_actions[i].ActionId] = Environment.TickCount64;
            }
        }
    }
}

/// <summary>
/// Global factory helpers for concise single-rule creation.
/// </summary>
public static class RuleFactory
{
    private sealed class SingleRule : CompiledRule
    {
        private readonly int _anchor;
        public override int Anchor => _anchor;
        public SingleRule(int anchor, in CompiledAction action)
        {
            _anchor = anchor;
            AddAction(action);
        }
    }

    public static CompiledRule Create(int anchor, int actionId, byte priority, Func<GameSnapshot, int, bool> condition, short debounceMs = 0, ActionSequence? sequence = null)
        => new SingleRule(anchor, new CompiledAction(actionId, priority, condition, debounceMs, sequence));
}

// Make CompiledAction a readonly struct to avoid allocations
public readonly struct CompiledAction(int actionId, byte priority, Func<GameSnapshot, int, bool> condition, short debounceMs = 0, ActionSequence? sequence = null, bool isGcd = true, CompiledAction.TargetHint targetHint = CompiledAction.TargetHint.Auto)
{
    public readonly int ActionId = actionId;
    public readonly byte Priority = priority;
    public readonly short DebounceMs = debounceMs;
    public readonly bool IsGcd = isGcd;
    public readonly Func<GameSnapshot, int, bool> Condition = condition;
    public readonly ActionSequence? Sequence = sequence;
    public readonly TargetHint Targeting = targetHint;

    public enum TargetHint : byte
    {
        Auto = 0,        // leave as-is (hook decides)
        Self = 1,        // force self target
        Enemy = 2,       // prefer current hard target
        LowestInjuredAlly = 3, // healer single target
        CleansableAlly = 4     // Esuna-style
    }
}

/// <summary>
/// Simplified action sequence for multi-step actions.
/// </summary>
public sealed class ActionSequence
{
    private readonly int[] _steps;          // action IDs
    private readonly bool[] _stepIsGcd;     // parallel isGcd flags
    private int _currentStep;

    // Back-compat constructor: all steps treated as GCD
    public ActionSequence(params int[] steps)
    {
        _steps = steps ?? [];
        _stepIsGcd = new bool[_steps.Length];
        for (int i = 0; i < _stepIsGcd.Length; i++) _stepIsGcd[i] = true;
        _currentStep = 0;
    }

    private ActionSequence((int actionId, bool isGcd)[] steps)
    {
        if (steps == null) { _steps = []; _stepIsGcd = []; return; }
        _steps = new int[steps.Length];
        _stepIsGcd = new bool[steps.Length];
        for (int i = 0; i < steps.Length; i++) { _steps[i] = steps[i].actionId; _stepIsGcd[i] = steps[i].isGcd; }
        _currentStep = 0;
    }

    public static ActionSequence FromSteps(params (int actionId, bool isGcd)[] steps) => new(steps);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => _currentStep = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCurrentStep(GameSnapshot cache, out int actionId, out bool isGcd)
    {
        if (_currentStep >= _steps.Length) Reset();
        if (_currentStep < _steps.Length)
        {
            actionId = _steps[_currentStep];
            isGcd = _stepIsGcd[_currentStep];
            return true;
        }
        actionId = 0; isGcd = true; return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnActionUsed(int usedActionId)
    {
        if (_currentStep < _steps.Length && _steps[_currentStep] == usedActionId)
            _currentStep++;
    }

    public static ActionSequence TwoStep(int firstAction, int secondAction) => new(firstAction, secondAction);
}