using yetanotherffxivcomboplugin.src.Core;

namespace yetanotherffxivcomboplugin.src.Jobs.WHM;

public sealed class WHMSTDPS : CompiledRule
{
    private readonly WHM _whm;
    public override int Anchor => _whm.StDps;

    public WHMSTDPS(WHM whm)
    {
        _whm = whm;

        // Add lily actions
        foreach (var action in WHMCompositeActions.GetLilyActions)
            AddAction(action);

        // DoT refresh: only on ST anchor; Aero/Dia have no prerequisites — direct action
        AddAction(new CompiledAction(
            actionId: _whm.DotDps,
            priority: 12,
            condition: (cache, anchor) => cache.DoesDebuffNeedRefresh(_whm.DotDps, 3000)
        ));

        foreach (var action in WHMCompositeActions.GetDPSoGCDs)
            AddAction(action);
    }
}

public sealed class WHMAoEDPS : CompiledRule
{
    private readonly WHM _whm;
    public override int Anchor => _whm.AoeDps;

    public WHMAoEDPS(WHM whm)
    {
        _whm = whm;

        // Add lily actions
        foreach (var action in WHMCompositeActions.GetLilyActions)
            AddAction(action);

        foreach (var action in WHMCompositeActions.GetDPSoGCDs)
            AddAction(action);
    }
}

public sealed class WHMSwiftRaise : CompiledRule
{
    public override int Anchor => WHMIDs.Raise;

    public WHMSwiftRaise()
    {
        // Swiftcast + Thin Air -> Raise
        AddAction(new CompiledAction(
            actionId: 0, // Will be determined by sequence
            priority: 1,
            isGcd: false,
            condition: static (cache, anchor) =>
            {
                if (!cache.AnyPartyDead) return false;
                return cache.IsActionReady(WHMIDs.Swiftcast) && cache.IsActionReady(WHMIDs.ThinAir);
            },
            debounceMs: 3000,
            sequence: ActionSequence.FromSteps((WHMIDs.Swiftcast, false), (WHMIDs.ThinAir, false), (WHMIDs.Raise, true))
        ));

        // Thin Air -> Raise
        AddAction(new CompiledAction(
            actionId: 0, // Will be determined by sequence
            priority: 2,
            condition: static (cache, anchor) =>
            {
                if (!cache.AnyPartyDead) return false;
                return cache.IsActionReady(WHMIDs.ThinAir);
            },
            debounceMs: 3000,
            sequence: ActionSequence.FromSteps((WHMIDs.ThinAir, false), (WHMIDs.Raise, true))
        ));
    }
}

public static class WHMCompositeActions
{
    public static readonly CompiledAction[] GetLilyActions = [
        new CompiledAction(
            actionId: WHMIDs.AfflatusMisery,
            priority: 10,
            condition: static (cache, _) =>
            {
                var g = WhmGaugeReader.Read();
                return g.BloodLilyBloomed;
            }
        ),
        new CompiledAction(
            actionId: WHMIDs.AfflatusRapture,
            priority: 12,
            condition: static (cache, _) =>
            {
                var g = WhmGaugeReader.Read();
                return g.HasFullLilies || g.OvercapSoon;
            }
        )
    ];

    public static readonly CompiledAction[] GetDPSoGCDs = [
        new CompiledAction(
            actionId: WHMIDs.GlareIV,
            priority: 13,
            condition: (cache, _) => cache.PlayerHasStatus(WHMIDs.SacredSightStatus)
        ),
        new CompiledAction(
            actionId: WHMIDs.Assize,
            priority: 14,
            isGcd: false,
            condition: static (cache, _) => cache.IsActionReady(WHMIDs.Assize)
        ),
        new CompiledAction(
            actionId: WHMIDs.PresenceOfMind,
            priority: 15,
            isGcd: false,
            condition: (cache, _) => cache.IsActionReady(WHMIDs.PresenceOfMind)
        ),
        new CompiledAction(
            actionId: WHMIDs.LucidDreaming,
            priority: 16,
            isGcd: false,
            condition: static (cache, _) => cache.PlayerMp <= 7000 && cache.IsActionReady(WHMIDs.LucidDreaming)
        )
    ];
}
