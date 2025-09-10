using yetanotherffxivcomboplugin.src.Core;

namespace yetanotherffxivcomboplugin.src.Jobs.SGE;

/// <summary>
/// Single-target DPS anchor rule for SGE.
/// </summary>
public sealed class SGESTDPS : CompiledRule
{
    private readonly SGE _sge;
    public override int Anchor => _sge.StDps;

    public SGESTDPS(SGE sge)
    {
        _sge = sge;

        // Addersgall overcap spend (shared action)
        AddAction(SGECompositeActions.AddersgallOvercap);

        AddAction(new CompiledAction(
            actionId: _sge.DotDps,
            priority: 1,
            debounceMs: 1000,
            condition: (cache, anchor) => cache.DoesDebuffNeedRefresh(_sge.DotDps, 5000),
            sequence: ActionSequence.TwoStep(SGEIDs.Eukrasia, _sge.DotDps)
        ));

        foreach (var action in SGECompositeActions.GetDPSoGCDs(_sge))
            AddAction(action);

        AddAction(SGECompositeActions.GetMovingDPSAction(_sge));

        AddAction(SGECompositeActions.LucidDreaming);
    }
}

/// <summary>
/// AoE DPS anchor rule for SGE.
/// </summary>
public sealed class SGEAoEDPS : CompiledRule
{
    private readonly SGE _sge;
    public override int Anchor => _sge.AoeDps;

    public SGEAoEDPS(SGE sge)
    {
        _sge = sge;

        // Addersgall overcap spend (shared action)
        AddAction(SGECompositeActions.AddersgallOvercap);

        AddAction(new CompiledAction(
            actionId: _sge.AoeDotDps,
            priority: 1,
            debounceMs: 5000,
            condition: (cache, anchor) => cache.DoesDebuffNeedRefresh(_sge.AoeDotDps, 5000),
            sequence: ActionSequence.TwoStep(SGEIDs.Eukrasia, _sge.AoeDotDps)
        ));

        foreach (var action in SGECompositeActions.GetDPSoGCDs(_sge))
            AddAction(action);

        AddAction(SGECompositeActions.LucidDreaming);
    }
}

// Raise handling (Egeiro) anchor
public sealed class SGESwiftRaise : CompiledRule
{
    public override int Anchor => SGEIDs.Egeiro;

    public SGESwiftRaise()
    {
        // Swiftcast -> Egeiro
        AddAction(new CompiledAction(
            actionId: 0,
            priority: 1,
            condition: static (cache, anchor) => cache.AnyPartyDead && cache.IsActionReady(SGEIDs.Swiftcast),
            sequence: ActionSequence.TwoStep(SGEIDs.Swiftcast, SGEIDs.Egeiro)
        ));

        // Direct Egeiro
        AddAction(new CompiledAction(
            actionId: SGEIDs.Egeiro,
            priority: 2,
            condition: static (cache, anchor) => cache.AnyPartyDead && cache.IsActionReady(SGEIDs.Egeiro)
        ));

        AddAction(SGECompositeActions.LucidDreaming);
    }
}

/// <summary>
/// Shared reusable SGE action components.
/// </summary>
public static class SGECompositeActions
{
    public static readonly CompiledAction AddersgallOvercap = new(
        actionId: SGEIDs.Druochole,
        priority: 0,
        isGcd: false,
        condition: static (_, __) =>
        {
            var g = SgeGaugeReader.Read();
            return g.Addersgall >= 3 || g.OvercapSoon;
        }
    );

    public static CompiledAction[] GetDPSoGCDs(SGE sge) => [
        new CompiledAction(
            actionId: SGEIDs.Psyche,
            priority: 5,
            isGcd: false,
            condition: static (cache, _) => cache.IsActionReady(SGEIDs.Psyche)
        ),
        new CompiledAction(
            actionId: sge.Phlegma,
            priority: 6,
            condition: (cache, _) => sge.IsPhlegmaReady(cache)
        )
    ];

    public static CompiledAction GetMovingDPSAction(SGE sge) => new(
        actionId: sge.Toxikon,
        priority: 4, // above Phlegma filler, below DoT sequences
        condition: static (cache, _) =>
        {
            var g = SgeGaugeReader.Read();
            if (!cache.PlayerMoving || g.Addersting == 0) return false;
            // Avoid preempting an active Eukrasia sequence; cheap check: don’t fire if player has Eukrasia buff
            // (casting Eukrasia sets a brief status; if unavailable, this still works fine as a simple gate)
            return !cache.PlayerHasStatus(SGEIDs.EukrasiaStatus);
        }
    );

    public static readonly CompiledAction LucidDreaming = new(
        actionId: SGEIDs.LucidDreaming,
        priority: 10,
        isGcd: false,
        condition: static (cache, _) => cache.PlayerMp <= 7000 && cache.IsActionReady(SGEIDs.LucidDreaming)
    );
}
