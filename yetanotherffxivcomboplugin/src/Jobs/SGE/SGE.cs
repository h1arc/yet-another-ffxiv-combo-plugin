using System;
using yetanotherffxivcomboplugin.src.Core;
using yetanotherffxivcomboplugin.src.Jobs.Interfaces;
using yetanotherffxivcomboplugin.src.Snapshot;

namespace yetanotherffxivcomboplugin.src.Jobs.SGE;

public sealed class SGE : BaseJob
{
    public override ushort JobId => 40;
    public override string JobName => "Sage";
    public override ReadOnlySpan<int> RetraceActions => s_retrace;

    private int _stDps;      // Dosis progression anchor
    private int _aoeDps;     // Dyskrasia progression anchor
    private int _dotDps;     // Eukrasian Dosis (single target DoT applicator after Eukrasia)
    private int _aoeDotDps;  // Eukrasian Dyskrasia (AoE DoT if unlocked)
    private int _phlegma;    // Phlegma progression (not an anchor, referenced by rules later if added)
    private int _toxikon;    // Toxikon progression (future rules)
    private ushort _dotDebuff;      // Single-target DoT status
    private ushort _aoeDotDebuff;   // AoE DoT status (if any)

    private CompiledRule[]? _rules;

    public int StDps => _stDps;
    public int AoeDps => _aoeDps;
    public int DotDps => _dotDps;
    public int AoeDotDps => _aoeDotDps;
    public int Toxikon => _toxikon;
    public int Phlegma => _phlegma;

    protected override void ConfigureRules(GameSnapshot snapshot)
    {
        var lvl = snapshot.PlayerLevel;

        _stDps = ResolveDosis(lvl);
        _aoeDps = ResolveDyskrasia(lvl);
        _dotDps = ResolveEukrasianDosis(lvl);
        _dotDebuff = ResolveEukrasianDosisStatus(lvl);
        _aoeDotDps = lvl >= 82 ? SGEIDs.EukrasianDyskrasia : 0; // unlocked at 82
        _aoeDotDebuff = _aoeDotDps != 0 ? SGEIDs.EukrasianDyskrasiaStatus : (ushort)0;
        _phlegma = ResolvePhlegma(lvl);
        _toxikon = ResolveToxikon(lvl);

        // Debuff mappings for refresh logic
        snapshot.ConfigureDebuffMapping(_dotDps, [_dotDebuff]);
        snapshot.ConfigureDebuffMapping(_aoeDotDps, [_aoeDotDebuff]);

        // Track commonly used cooldowns we care about in rules (Swiftcast, Lucid, Psyche)
        snapshot.ConfigureCooldowns([SGEIDs.Swiftcast, SGEIDs.LucidDreaming, SGEIDs.Psyche, _phlegma]);

        _rules = [new SGESTDPS(this), new SGEAoEDPS(this), new SGESwiftRaise()];
        Resolver!.SetRules(_rules);
    }

    private static readonly int[] s_retrace =
    [
        SGEIDs.Diagnosis,
        SGEIDs.EukrasianDiagnosis,
        SGEIDs.Druochole,
        SGEIDs.Taurochole,
        SGEIDs.Haima,
        SGEIDs.Krasis,
        SGEIDs.Egeiro
    ];

    // Helper: Phlegma ready if at least one charge (<=40s remaining) and target within ~6 yalms.
    internal bool IsPhlegmaReady(GameSnapshot cache)
    {
        var id = _phlegma;
        if (id == 0) return false;
        if (!cache.TryGetCooldownRemainingMs(id, out var remMs)) return false;
        if (remMs > 40_000) return false; // no charges
                                          // Use edge distance instead of center-to-center; matches engine CanUse at melee-ish ranges
        return cache.IsTargetEdgeWithin(6f);
    }


    public override void OnActionUsed(bool success, int actionId, GameSnapshot snapshot)
    {
        if (!success || _rules == null) return;
        foreach (var r in _rules) r.OnActionUsed(actionId);
    }

    // --- Progression helpers ---
    private static int ResolveDosis(byte level) => level switch
    {
        >= 82 => SGEIDs.DosisIII,
        >= 72 => SGEIDs.DosisII,
        _ => SGEIDs.DosisI
    };

    private static int ResolveDyskrasia(byte level) => level switch
    {
        >= 82 => SGEIDs.DyskrasiaII,
        >= 46 => SGEIDs.DyskrasiaI,
        _ => 0
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
        >= 66 => SGEIDs.ToxikonI,
        _ => 0
    };
}
