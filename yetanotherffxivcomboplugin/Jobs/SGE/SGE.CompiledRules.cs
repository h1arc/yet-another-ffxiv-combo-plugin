using yetanotherffxivcomboplugin.Core;

namespace yetanotherffxivcomboplugin.Jobs.SGE;

/// <summary>
/// Compiled DPS rule set for SGE using the  engine.
/// Maintains behavior parity with the prior plan but with deterministic sequences and priorities.
/// </summary>
public sealed class SGECompositeRule : CompiledRule
{
    private readonly SGE _sge;

    public SGECompositeRule(SGE sge)
    {
        _sge = sge;
        CompileRules();
    }

    private void CompileRules()
    {
        // Accessory: Quick Raise (Egeiro) — only when pressing Egeiro anchor
        // Variant 1: Swiftcast available -> Swiftcast (oGCD) then Egeiro (GCD)
        AddAction(new CompiledAction
        {
            ActionId = 0,
            IsGcd = false, // sequence drives both lanes
            Priority = 1,
            Condition = static (cache, anchor) =>
            {
                if (anchor != SGEIDs.Egeiro) return false;
                if (!cache.AnyPartyDead) return false;
                // Require both actions available
                return cache.IsActionReady(SGEIDs.Swiftcast);
            },
            Sequence = new ActionSequence(
                new ActionSequence.SequenceStep(SGEIDs.Swiftcast, false, ActionSequence.TransitionType.Guaranteed),
                new ActionSequence.SequenceStep(SGEIDs.Egeiro, true, ActionSequence.TransitionType.Terminal)
            )
        });

        // Variant 2: No Swiftcast, cast Egeiro directly when pressing Egeiro
        AddAction(new CompiledAction
        {
            ActionId = 0,
            IsGcd = true,
            Priority = 2,
            Condition = static (cache, anchor) =>
            {
                if (anchor != SGEIDs.Egeiro) return false;
                if (!cache.AnyPartyDead) return false;
                return cache.IsActionReady(SGEIDs.Egeiro);
            },
            Sequence = new ActionSequence(
                new ActionSequence.SequenceStep(SGEIDs.Egeiro, true, ActionSequence.TransitionType.Terminal)
            )
        });

        // Rule: Prevent Addersgall overcap — Druochole oGCD at very high priority.
        AddAction(new CompiledAction
        {
            ActionId = SGEIDs.Druochole,
            IsGcd = false,
            Priority = 0,
            Condition = static (_, __) =>
            {
                var g = SgeGaugeReader.Read();
                return g.Addersgall >= 3 || g.OvercapSoon;
            }
        });

        // Unified Rule: DoT refresh — branches based on current anchor (ST vs AoE)
        AddAction(new CompiledAction
        {
            ActionId = 0, // sequence chosen by branch
            IsGcd = true,
            Priority = 1,
            DebounceMs = 5000,
            DebouncePerAnchor = true,
            Branch = AnchorBranch.StAoe(
                _sge.StDps,
                ActionSequence.TwoStepGuaranteed(SGEIDs.Eukrasia, true, _sge.DotDps, true),
                _sge.AoeDps,
                ActionSequence.TwoStepGuaranteed(SGEIDs.Eukrasia, true, _sge.AoeDotDps, true)
            ),
            // Only consider refresh when we have a recognized anchor
            Condition = (cache, anchor) =>
            {
                if (anchor == _sge.StDps)
                    return cache.DoesDebuffNeedRefresh(_sge.DotDps, 5000);
                if (anchor == _sge.AoeDps)
                    return cache.DoesDebuffNeedRefresh(_sge.AoeDotDps, 5000);
                return false;
            }
        });

        // Rule: Psyche oGCD when ready
        AddAction(new CompiledAction
        {
            ActionId = SGEIDs.Psyche,
            IsGcd = false,
            Priority = 10,
            Condition = static (cache, _) => cache.IsActionReady(SGEIDs.Psyche)
        });

        // Rule: Phlegma GCD when a charge available (condition checks charges via helper)
        AddAction(new CompiledAction
        {
            ActionId = 0, // sequence will drive the action pick
            IsGcd = true,
            Priority = 12,
            Condition = (cache, _) => _sge.IsPhlegmaReady(cache),
            Sequence = new ActionSequence(
                new ActionSequence.SequenceStep(_sge.Phlegma, true, ActionSequence.TransitionType.Terminal)
            )
        });

        // Rule: If moving and have Addersting, prefer Toxikon as GCD
        AddAction(new CompiledAction
        {
            ActionId = 0,
            IsGcd = true,
            Priority = 14,
            Condition = static (cache, _) =>
            {
                var g = SgeGaugeReader.Read();
                return cache.PlayerMoving && g.Addersting > 0;
            },
            Sequence = new ActionSequence(
                new ActionSequence.SequenceStep(_sge.Toxikon, true, ActionSequence.TransitionType.Terminal)
            )
        });

        // Rule: Lucid Dreaming when low MP
        AddAction(new CompiledAction
        {
            ActionId = SGEIDs.LucidDreaming,
            IsGcd = false,
            Priority = 20,
            Condition = static (cache, _) => cache.PlayerMp <= 7000 && cache.IsActionReady(SGEIDs.LucidDreaming)
        });

        // Avoid always-on filler here; the Planner appends anchor fallback automatically.
    }
}
