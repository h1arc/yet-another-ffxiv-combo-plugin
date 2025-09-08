using System;
using System.Linq;
using yetanotherffxivcomboplugin.Core;
using yetanotherffxivcomboplugin.Jobs.Interfaces;
using yetanotherffxivcomboplugin.Snapshot;

namespace yetanotherffxivcomboplugin.Jobs.WHM;

public sealed class WHM : BaseJob
{
    public override ushort JobId => 24;
    public override string JobName => "White Mage ()";

    // Resolved actions
    internal int _stDps;        // Stone/Glare
    internal int _aoeDps;       // Holy
    internal int _dotDps;       // Aero/Dia
    internal ushort _dotStatus; // Debuff status id

    private WHMCompositeRule? _compiled;

    public int StDps => _stDps;
    public int AoeDps => _aoeDps;
    public int DotDps => _dotDps;

    public override void Initialize() { }
    public override void Dispose() { _compiled = null; }

    public override void RefreshResolvedActions(byte level)
    {
        _stDps = ResolveStoneGlare(level);
        _aoeDps = ResolveHoly(level);
        _dotDps = ResolveAeroDia(level);
        _dotStatus = ResolveAeroDiaStatus(level);
    }

    protected override void ClearResolvedActions()
    {
        _stDps = _aoeDps = _dotDps = 0; _dotStatus = 0;
    }

    public override int ResolveAction(int baseActionId, byte level)
    {
        return baseActionId switch
        {
            WHMIDs.StoneI => ResolveStoneGlare(level),
            WHMIDs.Holy => ResolveHoly(level),
            WHMIDs.Aero => ResolveAeroDia(level),
            _ => baseActionId
        };
    }

    public override void ConfigureDebuffs(GameSnapshot cache)
    {
        if (_dotDps != 0 && _dotStatus != 0)
            cache.ConfigureDebuffMapping(_dotDps, new ReadOnlySpan<ushort>(in _dotStatus));
    }

    protected override int[] GetTrackedCooldowns()
    {
        return [WHMIDs.Assize, WHMIDs.PresenceOfMind, WHMIDs.SacredSightStatus, WHMIDs.LucidDreaming, WHMIDs.Swiftcast, WHMIDs.ThinAir];
    }

    protected override int[] GetComboAnchorActions() => [_stDps, _aoeDps, WHMIDs.Raise];

    public override void RegisterPlannerRules(Planner? planner)
    {
        if (planner == null) return;
        _compiled ??= new WHMCompositeRule(this);
        planner.SetJobRule(static (in PlannerContext ctx, ref PlanBuilder b) =>
        {
            var self = JobRegistry.GetCurrentJob<WHM>();
            if (self == null) return;
            self._compiled?.Execute(ctx.Cache, ctx.PressedActionId, ref b);
        });
    }

    public override void OnActionUsed(bool success, int usedActionId, GameSnapshot? snap)
    {
        _compiled?.OnActionResult(success, usedActionId, snap);
    }

    public override int[] GetGcdActionIds()
    {
        return [
            WHMIDs.StoneI, WHMIDs.StoneII, WHMIDs.StoneIII, WHMIDs.StoneIV,
            WHMIDs.GlareI, WHMIDs.GlareIII, WHMIDs.GlareIV,
            WHMIDs.Aero, WHMIDs.AeroII, WHMIDs.Dia,
            WHMIDs.Holy, WHMIDs.HolyIII,
            WHMIDs.AfflatusMisery, WHMIDs.AfflatusRapture,
            WHMIDs.Raise
        ];
    }

    public override int[] GetOgcdActionIds()
    {
        return [WHMIDs.Assize, WHMIDs.PresenceOfMind, WHMIDs.LucidDreaming, WHMIDs.Swiftcast, WHMIDs.ThinAir];
    }

    public override ushort[] GetStatusIds()
    {
        return [WHMIDs.AeroStatus, WHMIDs.AeroIIStatus, WHMIDs.DiaStatus];
    }

    private static readonly int[] s_retraceable = [
        WHMIDs.Cure, WHMIDs.CureII, WHMIDs.CureIII, WHMIDs.Regen,
        WHMIDs.AfflatusSolace, WHMIDs.DivineBenison, WHMIDs.Aquaveil,
        WHMIDs.Tetragrammaton, WHMIDs.Benediction, WHMIDs.Esuna,
        WHMIDs.Raise
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
        return [.. GetGcdActionIds().Concat(GetOgcdActionIds()).Concat(GetRetraceableActionIds()).Distinct()];
    }

    private static int ResolveStoneGlare(byte level) => level switch
    {
        >= 82 => WHMIDs.GlareIII,
        >= 72 => WHMIDs.GlareI,
        >= 64 => WHMIDs.StoneIV,
        >= 54 => WHMIDs.StoneIII,
        >= 18 => WHMIDs.StoneII,
        _ => WHMIDs.StoneI
    };

    private static int ResolveHoly(byte level) => level switch
    {
        >= 82 => WHMIDs.HolyIII,
        _ => WHMIDs.Holy
    };

    private static int ResolveAeroDia(byte level) => level switch
    {
        >= 72 => WHMIDs.Dia,
        >= 46 => WHMIDs.AeroII,
        _ => WHMIDs.Aero
    };

    private static ushort ResolveAeroDiaStatus(byte level) => level switch
    {
        >= 72 => WHMIDs.DiaStatus,
        >= 46 => WHMIDs.AeroIIStatus,
        _ => WHMIDs.AeroStatus
    };
}
