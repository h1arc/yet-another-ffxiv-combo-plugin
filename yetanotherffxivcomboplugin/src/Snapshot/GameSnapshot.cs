using System;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using yetanotherffxivcomboplugin.src.Interfaces;

namespace yetanotherffxivcomboplugin.src.Snapshot;

/// <summary>
/// GameSnapshot is a per-frame, read-only snapshot of game state used by resolvers/combos.
/// Updated on Framework.Update; consumers should treat it as immutable between ticks.
/// Zero-alloc accessors, readonly structs where possible.
/// </summary>
[SkipLocalsInit]
public sealed partial class GameSnapshot(IGameView game)
{
    private readonly IGameView _game = game;
    private ulong _frame; // advanced once per Update

    // Group frequently-read fields into a tightly-packed struct for cache locality
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct HotData
    {
        public Vector3 PlayerPos;
        public CombatFlags Flags;
        public byte PlayerHpPct;
        public bool PlayerAlive;
        public int PlayerMp;
        public byte PlayerLevel;
        public int PlayerJobId;
    }
    private HotData _hot;
    // private Vector3 _prevPlayerPos;
    public bool PlayerMoving { get; private set; }
    // Movement hysteresis: keep PlayerMoving true for a short period after last detected movement (frame-based to avoid system clock calls)
    private ulong _movingUntilFrame;
    private const int MoveHoldFrames = 5; // ~200ms at 60 FPS
    private const float MoveEpsilonSq = 0.0001f; // squared distance threshold to consider as movement

    // Immutable, process-wide rules data initialized once at startup.
    // Holds the set and a fast LUT of dispellable status IDs (Esuna-removable).
    private static FrozenSet<uint> s_dispellableStatuses = [];
    private static volatile bool s_rulesInited;
    private static volatile bool[]? s_dispellableLut; // length 65536, built once per process
    private static volatile bool s_lutInited;
    private static volatile int s_dispellableLutCount;

    // Sanctuary micro-cache (process-wide)
    private static volatile bool s_inSanctuary;

    // Snapshot fields (immutable between Update calls)
    public ObjRef Player { get; private set; }
    public ObjRef? Target { get; private set; }
    public ObjRef? FocusTarget { get; private set; }
    public ObjRef? SoftTarget { get; private set; }
    public CombatFlags Flags => _hot.Flags;
    public Vector3 PlayerPos => _hot.PlayerPos;
    public byte PlayerHpPct => _hot.PlayerHpPct;
    public bool PlayerAlive => _hot.PlayerAlive;
    public int PlayerMp => _hot.PlayerMp;
    public byte PlayerLevel => _hot.PlayerLevel;
    public int PlayerJobId => _hot.PlayerJobId;

    // Simple party cache (refs only to avoid allocations)
    private const int MaxParty = 8; // party list size (not full alliance)
    private readonly ObjRef[] _partyRefs = new ObjRef[MaxParty];
    private readonly byte[] _partyHpPct = new byte[MaxParty]; // 0..100
    private readonly bool[] _partyAlive = new bool[MaxParty];
    private readonly Vector3[] _partyPos = new Vector3[MaxParty];
    private readonly bool[] _partyPosKnown = new bool[MaxParty];
    private readonly string[] _partyNames = new string[MaxParty];
    private int _partyCount;
    private byte _partyDeadCount; // computed per-tick
    private ulong _firstDeadPartyMember; // computed per-tick for fast raise targeting
    public ReadOnlySpan<ObjRef> Party => _partyRefs.AsSpan(0, _partyCount);
    public ReadOnlySpan<byte> PartyHpPct => _partyHpPct.AsSpan(0, _partyCount);
    public ReadOnlySpan<bool> PartyAlive => _partyAlive.AsSpan(0, _partyCount);
    public bool AnyPartyDead => _partyDeadCount > 0;
    public int PartyDeadCount => _partyDeadCount;

    // Tiny per-frame micro-caches for top-two healable/cleansable party members
    private TopTwo _healablePair; private ulong _healablePairFrame;
    private TopTwo _cleansablePair; private ulong _cleansablePairFrame;

    // Generic gauge access (engine-agnostic): ask for a typed gauge snapshot.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadGauge<T>(out T gauge) => _game.TryReadGauge<T>(out gauge);

    // Lazily built per-tick cache: party/self who currently have a dispellable debuff
    private readonly ulong[] _cleansableIds = new ulong[16];
    private byte _cleansableCount;
    private bool _cleansableBuilt;
    private ulong _cleansableSetFrame;

    // Per-tick action recast tracking (dynamic per-job)
    private readonly int[] _trackedRecasts = new int[16];
    private readonly bool[] _trackedReady = new bool[16];
    private byte _trackedCount;
    private int _justUsedActionId; // cleared every Update; lets us avoid same-tick repeats

    // Debuff tracking map: actionId -> up to 3 statusIds it applies (configured by job)
    private readonly int[] _debuffActionIds = new int[16];
    private readonly ushort[] _debuffStatusA = new ushort[16];
    private readonly ushort[] _debuffStatusB = new ushort[16];
    private readonly ushort[] _debuffStatusC = new ushort[16];
    private byte _debuffMapCount;

    // Role classifier for card targeting
    private enum Role : byte { Unknown = 0, Tank, Healer, Melee, Ranged, Caster }

    // Compact, branchless job->role lookup (covers battle jobs; others remain Unknown)
    // private static readonly Role[] s_jobToRole = InitJobRoleLut();
    // private static Role[] InitJobRoleLut()
    // {
    //     var lut = new Role[64];
    //     // Tanks
    //     lut[19] = Role.Tank; // PLD
    //     lut[21] = Role.Tank; // WAR
    //     lut[32] = Role.Tank; // DRK
    //     lut[37] = Role.Tank; // GNB
    //     // Healers
    //     lut[24] = Role.Healer; // WHM
    //     lut[28] = Role.Healer; // SCH
    //     lut[33] = Role.Healer; // AST
    //     lut[40] = Role.Healer; // SGE
    //     // Melee DPS
    //     lut[20] = Role.Melee; // MNK
    //     lut[22] = Role.Melee; // DRG
    //     lut[30] = Role.Melee; // NIN
    //     lut[34] = Role.Melee; // SAM
    //     lut[39] = Role.Melee; // RPR
    //     lut[41] = Role.Melee; // VPR
    //     // Physical Ranged
    //     lut[23] = Role.Ranged; // BRD
    //     lut[31] = Role.Ranged; // MCH
    //     lut[38] = Role.Ranged; // DNC
    //     // Casters
    //     lut[25] = Role.Caster; // BLM
    //     lut[35] = Role.Caster; // SMN
    //     lut[36] = Role.Caster; // RDM
    //     lut[42] = Role.Caster; // PCT
    //     return lut;
    // }

    /// <summary> Returns true if the local player currently has the given status. </summary>
    public bool PlayerHasStatus(ushort statusId) => _game.PlayerHasStatus(statusId);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EnsureCleansableBuilt()
    {
        if (_cleansableBuilt && _cleansableSetFrame == _frame) return true;
        var self = Player.Id;
        foreach (var ch in _game.EnumerateCharacters())
        {
            var id = ch.Id; if (id == 0) continue;
            if (!IsPartyMember(id) && id != self) continue;
            if (!_game.HasCleansableDebuff(id)) continue;
            bool exists = false;
            for (int i = 0; i < _cleansableCount; i++) if (_cleansableIds[i] == id) { exists = true; break; }
            if (!exists && _cleansableCount < _cleansableIds.Length) _cleansableIds[_cleansableCount++] = id;
        }
        _cleansableBuilt = true; _cleansableSetFrame = _frame;
        return true;
    }
}

/// <summary> Light-weight object reference (readonly struct) with cached id. </summary>
public readonly struct ObjRef
{
    public readonly ulong Id;
    private ObjRef(ulong id)
    { Id = id; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ObjRef FromId(ulong id)
        => id == 0 ? default : new ObjRef(id);

    public bool IsValid => Id != 0;
}

/// <summary> Common fast flags for hot-path checks. </summary>
public readonly struct CombatFlags(bool inCombat = false, bool mounted = false, bool casting = false)
{
    public readonly bool InCombat = inCombat;
    public readonly bool Mounted = mounted;
    public readonly bool Casting = casting;
}

/// <summary> Small, no-alloc holder for up to 4 candidates with duplicate filtering and iteration. </summary>
public struct CandidateList
{
    private ulong _a, _b, _c, _d;
    private byte _count;
    private byte _idx;

    public readonly int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryTakeNext(out ulong id)
    {
        if (_idx >= _count) { id = 0; return false; }
        id = _idx switch { 0 => _a, 1 => _b, 2 => _c, _ => _d };
        _idx++;
        return id != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsFull(int max) => _count >= max || _count >= 4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ulong id, int max)
    {
        if (id == 0 || IsFull(max)) return;
        if (id == _a || id == _b || id == _c || id == _d) return;
        switch (_count)
        {
            case 0: _a = id; break;
            case 1: _b = id; break;
            case 2: _c = id; break;
            case 3: _d = id; break;
            default: return;
        }
        _count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddRange(in CandidateList other, int max)
    {
        var tmp = other; // copy struct to own cursor
        while (!IsFull(max) && tmp.TryTakeNext(out var id))
            Add(id, max);
    }

}