using System;
using System.Linq;
using yetanotherffxivcomboplugin.Core;
using yetanotherffxivcomboplugin.Jobs.Interfaces;
using yetanotherffxivcomboplugin.Snapshot;

namespace yetanotherffxivcomboplugin.Jobs.SGE;

public sealed class SGE : BaseJob
{
    public override ushort JobId => 40; // SGE
    public override string JobName => "Sage ()";

    // Resolved actions by level
    internal int _stDps;     // Dosis progression
    internal int _aoeDps;    // Dyskrasia progression
    internal int _dotDps;    // Eukrasian Dosis progression
    internal int _aoeDotDps; // Eukrasian Dyskrasia progression
    internal int _phlegma;   // Phlegma progression
    internal int _toxikon;   // Toxikon progression
    internal ushort _dotDebuff;     // DoT status effect
    internal ushort _aoeDotDebuff;  // AoE DoT status effect

    //  engines
    private SGECompositeRule? _compiled;
    private OpenerExecutor? _openerExec;

    public int StDps => _stDps;
    public int AoeDps => _aoeDps;
    public int DotDps => _dotDps;
    public int AoeDotDps => _aoeDotDps;
    public int Phlegma => _phlegma;
    public int Toxikon => _toxikon;

    public override void Initialize()
    {
        // Prepare inert opener executor; UI may Start() it later
        _openerExec = new OpenerExecutor(SGEOpeners.ToxikonOpener);
    }

    public override void Dispose()
    {
        _compiled = null;
        _openerExec = null;
    }

    public override void RefreshResolvedActions(byte level)
    {
        _stDps = ResolveDosis(level);
        _aoeDps = ResolveDyskrasia(level);
        _dotDps = ResolveEukrasianDosis(level);
        _aoeDotDps = SGEIDs.EukrasianDyskrasia;
        _dotDebuff = ResolveEukrasianDosisStatus(level);
        _aoeDotDebuff = SGEIDs.EukrasianDyskrasiaStatus;
        _phlegma = ResolvePhlegma(level);
        _toxikon = ResolveToxikon(level);
    }

    protected override void ClearResolvedActions()
    {
        _stDps = _aoeDps = _dotDps = _phlegma = _toxikon = 0; _dotDebuff = _aoeDotDebuff = 0;
    }

    public override int ResolveAction(int baseActionId, byte level)
    {
        return baseActionId switch
        {
            SGEIDs.DosisI => ResolveDosis(level),
            SGEIDs.EukrasianDosisI => ResolveEukrasianDosis(level),
            SGEIDs.DyskrasiaI => ResolveDyskrasia(level),
            SGEIDs.EukrasianDyskrasia => SGEIDs.EukrasianDyskrasia,
            SGEIDs.PhlegmaI => ResolvePhlegma(level),
            SGEIDs.ToxikonI => ResolveToxikon(level),
            _ => baseActionId
        };
    }

    protected override int[] GetTrackedCooldowns()
    {
        return [
            _phlegma,
            SGEIDs.Psyche,
            SGEIDs.LucidDreaming
        ];
    }

    public override void ConfigureDebuffs(GameSnapshot cache)
    {
        cache.ConfigureDebuffMapping(_dotDps, [_dotDebuff]);
        cache.ConfigureDebuffMapping(_aoeDotDps, [_aoeDotDebuff]);
    }

    protected override int[] GetComboAnchorActions() => [_stDps, _aoeDps, SGEIDs.Egeiro];
    // Note: Egeiro is handled as a pressed anchor context, not a filler anchor

    public override void RegisterPlannerRules(Planner? planner)
    {
        if (planner == null) return;
        // Ensure compiled rules exist and match current resolved actions
        _compiled ??= new SGECompositeRule(this);

        planner.SetJobRule(static (in PlannerContext ctx, ref PlanBuilder b) =>
        {
            var self = JobRegistry.GetCurrentJob<SGE>();
            if (self == null) return;

            // 1) Opener (if active)
            if (self._openerExec != null && self._openerExec.TryGetNext(out var step))
            {
                if (step.IsGcd) b.TryAddGcd(new Suggestion(true, step.ActionId, 0));
                else b.TryAddOgcd(new Suggestion(false, step.ActionId, 0));
                return; // opener takes priority
            }

            // 2) Compiled DPS rules with current anchor context
            self._compiled?.Execute(ctx.Cache, ctx.PressedActionId, ref b);
        });
    }

    public override void OnActionUsed(bool success, int usedActionId, GameSnapshot? snap)
    {
        _compiled?.OnActionResult(success, usedActionId, snap);
        _openerExec?.OnActionUsed(success, usedActionId);
    }

    // Job identification and registries
    public override int[] GetGcdActionIds()
    {
        return
        [
            SGEIDs.DosisI, SGEIDs.DosisII, SGEIDs.DosisIII,
            SGEIDs.EukrasianDosisI, SGEIDs.EukrasianDosisII, SGEIDs.EukrasianDosisIII,
            SGEIDs.Eukrasia,
            SGEIDs.PhlegmaI, SGEIDs.PhlegmaII, SGEIDs.PhlegmaIII,
            SGEIDs.ToxikonI, SGEIDs.ToxikonII,
            SGEIDs.Pneuma,
            SGEIDs.DyskrasiaI, SGEIDs.DyskrasiaII,
            SGEIDs.EukrasianDyskrasia,
            SGEIDs.Egeiro
        ];
    }

    public override int[] GetOgcdActionIds()
    {
        return [SGEIDs.Psyche, SGEIDs.LucidDreaming, SGEIDs.Druochole];
    }

    public override ushort[] GetStatusIds()
    {
        return [
            SGEIDs.EukrasianDosisIStatus,
            SGEIDs.EukrasianDosisIIStatus,
            SGEIDs.EukrasianDosisIIIStatus,
            SGEIDs.EukrasianDyskrasiaStatus,
            SGEIDs.EukrasiaStatus
        ];
    }

    private static readonly int[] s_retraceable =
    [
        SGEIDs.Diagnosis, SGEIDs.EukrasianDiagnosis, SGEIDs.Druochole, SGEIDs.Taurochole, SGEIDs.Haima, SGEIDs.Krasis,
        SGEIDs.Egeiro
    ];

    public override int[] GetRetraceableActionIds() => s_retraceable;

    public override bool IsJobAction(int actionId)
    {
        var a = GetGcdActionIds().Concat(GetOgcdActionIds()).ToArray();
        return Array.IndexOf(a, actionId) >= 0;
    }

    public override bool IsJobStatus(ushort statusId)
    {
        return Array.IndexOf(GetStatusIds(), statusId) >= 0;
    }

    public override int[] GetOwnedActionIds()
    {
        return GetGcdActionIds().Concat(GetOgcdActionIds()).Concat(GetRetraceableActionIds()).Distinct().ToArray();
    }

    // Helper: treat Phlegma as available when remaining CD is 0ms (2 charges) or ≤ 40,000ms (≥1 charge)
    internal bool IsPhlegmaReady(GameSnapshot cache)
    {
        var phlegmaActionId = _phlegma;
        if (phlegmaActionId == 0) return false;
        if (!cache.TryGetCooldownRemainingMs(phlegmaActionId, out var remMs))
            return true;
        return remMs <= 40_000;
    }

    private static int ResolveDosis(byte level) => level switch
    {
        >= 82 => SGEIDs.DosisIII,
        >= 72 => SGEIDs.DosisII,
        _ => SGEIDs.DosisI
    };

    private static int ResolveDyskrasia(byte level) => level switch
    {
        >= 82 => SGEIDs.DyskrasiaII,
        _ => SGEIDs.DyskrasiaI
    };

    private static int ResolveEukrasianDosis(byte level) => level switch
    {
        >= 82 => SGEIDs.EukrasianDosisIII,
        >= 72 => SGEIDs.EukrasianDosisII,
        _ => SGEIDs.EukrasianDosisI
    };

    private static ushort ResolveEukrasianDosisStatus(byte level) => level switch
    {
        >= 82 => SGEIDs.EukrasianDosisIIIStatus,
        >= 72 => SGEIDs.EukrasianDosisIIStatus,
        _ => SGEIDs.EukrasianDosisIStatus
    };

    private static int ResolvePhlegma(byte level) => level switch
    {
        >= 82 => SGEIDs.PhlegmaIII,
        >= 72 => SGEIDs.PhlegmaII,
        _ => SGEIDs.PhlegmaI
    };

    private static int ResolveToxikon(byte level) => level switch
    {
        >= 82 => SGEIDs.ToxikonII,
        _ => SGEIDs.ToxikonI
    };
}
