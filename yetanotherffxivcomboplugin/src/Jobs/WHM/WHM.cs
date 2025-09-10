using System;
using yetanotherffxivcomboplugin.src.Core;
using yetanotherffxivcomboplugin.src.Jobs.Interfaces;
using yetanotherffxivcomboplugin.src.Snapshot;

namespace yetanotherffxivcomboplugin.src.Jobs.WHM;

public sealed class WHM : BaseJob
{
    public override ushort JobId => 24;
    public override string JobName => "White Mage";
    public override ReadOnlySpan<int> RetraceActions => s_retrace;

    private int _stDps;   // Stone/Glare progression anchor
    private int _aoeDps;  // Holy progression anchor
    private int _dotDps;  // Aero/Dia progression (direct GCD applying DoT)
    private ushort _dotStatus;

    public int StDps => _stDps;
    public int AoeDps => _aoeDps;
    public int DotDps => _dotDps;

    private CompiledRule[]? _rules;

    protected override void ConfigureRules(GameSnapshot snapshot)
    {
        var lvl = snapshot.PlayerLevel;
        _stDps = ResolveStoneGlare(lvl);
        _aoeDps = ResolveHoly(lvl);
        _dotDps = ResolveAeroDia(lvl);
        _dotStatus = ResolveAeroDiaStatus(lvl);

        // Map DoT status for refresh logic
        snapshot.ConfigureDebuffMapping(_dotDps, [_dotStatus]);

        // Track important oGCD cooldowns for readiness queries
        snapshot.ConfigureCooldowns([WHMIDs.Assize, WHMIDs.PresenceOfMind, WHMIDs.LucidDreaming, WHMIDs.Swiftcast, WHMIDs.ThinAir]);

        // Rebuild rules each configuration pass so level-based progression updates reflected in compiled actions
        _rules = [new WHMSTDPS(this), new WHMAoEDPS(this), new WHMSwiftRaise()];
        Resolver!.SetRules(_rules);
    }

    public override void OnActionUsed(bool success, int actionId, GameSnapshot snapshot)
    {
        if (!success || _rules == null) return;
        foreach (var r in _rules) r.OnActionUsed(actionId);
    }

    private static readonly int[] s_retrace = [
        WHMIDs.Cure,
        WHMIDs.CureII,
        WHMIDs.CureIII,
        WHMIDs.Regen,
        WHMIDs.AfflatusSolace,
        WHMIDs.DivineBenison,
        WHMIDs.Aquaveil,
        WHMIDs.Tetragrammaton,
        WHMIDs.Benediction,
        WHMIDs.Esuna,
        WHMIDs.Raise
    ];

    // --- Progression helpers ---
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
