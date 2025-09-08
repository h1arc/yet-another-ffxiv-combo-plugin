using yetanotherffxivcomboplugin.Core;

namespace yetanotherffxivcomboplugin.Jobs.WHM;

public sealed class WHMCompositeRule : CompiledRule
{
    private readonly WHM _whm;

    public WHMCompositeRule(WHM whm)
    {
        _whm = whm;
        Compile();
    }

    private void Compile()
    {
        // Accessory: Quick Raise — only when pressing Raise and someone is dead
        // Variant 1: Swiftcast + Thin Air available -> Swiftcast (oGCD) -> Thin Air (oGCD) -> Raise (GCD)
        AddAction(new CompiledAction
        {
            ActionId = 0,
            IsGcd = false,
            Priority = 1,
            DebounceMs = 3000, // debounce on sequence start (Thin Air step)
            Condition = static (cache, anchor) =>
            {
                if (anchor != WHMIDs.Raise) return false;
                if (!cache.AnyPartyDead) return false;
                return cache.IsActionReady(WHMIDs.Swiftcast) && cache.IsActionReady(WHMIDs.ThinAir);
            },
            Sequence = new ActionSequence(
                new ActionSequence.SequenceStep(WHMIDs.Swiftcast, false, ActionSequence.TransitionType.Guaranteed),
                new ActionSequence.SequenceStep(WHMIDs.ThinAir, false, ActionSequence.TransitionType.Guaranteed),
                new ActionSequence.SequenceStep(WHMIDs.Raise, true, ActionSequence.TransitionType.Terminal)
            )
        });

        // Variant 2: No Swiftcast, but Thin Air available -> Thin Air (oGCD) -> Raise (GCD)
        AddAction(new CompiledAction
        {
            ActionId = 0,
            IsGcd = false,
            Priority = 2,
            DebounceMs = 3000,
            Condition = static (cache, anchor) =>
            {
                if (anchor != WHMIDs.Raise) return false;
                if (!cache.AnyPartyDead) return false;
                return cache.IsActionReady(WHMIDs.ThinAir);
            },
            Sequence = new ActionSequence(
                new ActionSequence.SequenceStep(WHMIDs.ThinAir, false, ActionSequence.TransitionType.Guaranteed),
                new ActionSequence.SequenceStep(WHMIDs.Raise, true, ActionSequence.TransitionType.Terminal)
            )
        });

        // Afflatus Misery when Blood Lily is available
        AddAction(new CompiledAction
        {
            ActionId = WHMIDs.AfflatusMisery,
            IsGcd = true,
            Priority = 10,
            Condition = static (cache, _) =>
            {
                var g = WhmGaugeReader.Read();
                return g.BloodLilyBloomed;
            }
        });

        // Afflatus Rapture when lilies at 3 or overcap soon
        AddAction(new CompiledAction
        {
            ActionId = WHMIDs.AfflatusRapture,
            IsGcd = true,
            Priority = 11,
            Condition = static (cache, _) =>
            {
                var g = WhmGaugeReader.Read();
                return g.HasFullLilies || g.OvercapSoon;
            }
        });

        // DoT refresh: only on ST anchor; Aero/Dia have no prerequisites — direct action
        AddAction(new CompiledAction
        {
            ActionId = _whm.DotDps,
            IsGcd = true,
            Priority = 12,
            Condition = (cache, anchor) => anchor == _whm.StDps && cache.DoesDebuffNeedRefresh(_whm.DotDps, 3000)
        });

        // Glare IV spender
        AddAction(new CompiledAction
        {
            ActionId = WHMIDs.GlareIV,
            IsGcd = true,
            Priority = 13,
            // If you want to gate by Sacred Sight window, uncomment the _whm.HasSacredSightStacksActive check.
            Condition = (cache, _) => cache.PlayerHasStatus(WHMIDs.SacredSightStatus)
        });

        // Assize oGCD when ready
        AddAction(new CompiledAction
        {
            ActionId = WHMIDs.Assize,
            IsGcd = false,
            Priority = 14,
            Condition = static (cache, _) => cache.IsActionReady(WHMIDs.Assize)
        });

        // Presence of Mind oGCD to arm Sacred Sight (3 stacks, 30s dump window) — placed under Assize (direct action)
        AddAction(new CompiledAction
        {
            ActionId = WHMIDs.PresenceOfMind,
            IsGcd = false,
            Priority = 15,
            Condition = (cache, _) => cache.IsActionReady(WHMIDs.PresenceOfMind)
        });

        // Lucid Dreaming under MP threshold
        AddAction(new CompiledAction
        {
            ActionId = WHMIDs.LucidDreaming,
            IsGcd = false,
            Priority = 16,
            Condition = static (cache, _) => cache.PlayerMp <= 7000 && cache.IsActionReady(WHMIDs.LucidDreaming)
        });
    }
}
