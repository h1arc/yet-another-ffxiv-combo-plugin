using System;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System.Reflection;

namespace yetanotherffxivcomboplugin.Snapshot;

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
    private Vector3 _prevPlayerPos;
    public bool PlayerMoving { get; private set; }

    // Immutable, process-wide rules data initialized once at startup.
    // Holds the set and a fast LUT of dispellable status IDs (Esuna-removable).
    private static FrozenSet<uint> s_dispellableStatuses = [];
    private static volatile bool s_rulesInited;
    private static volatile bool[]? s_dispellableLut; // length 65536, built once per process
    private static volatile bool s_lutInited;
    private static volatile int s_dispellableLutCount;

    // Sanctuary micro-cache (process-wide)
    private static volatile bool s_inSanctuary;

    public static void InitializeRules(FrozenSet<uint> dispellableStatuses)
    {
        if (s_rulesInited) return;
        if (dispellableStatuses == null || dispellableStatuses.Count == 0) return;
        s_dispellableStatuses = dispellableStatuses;
        // Build a dense LUT once for O(1) array lookup by ushort status id
        var lut = new bool[ushort.MaxValue + 1];
        int count = 0;
        foreach (var id in dispellableStatuses)
        {
            if (id <= ushort.MaxValue)
            {
                lut[(ushort)id] = true;
                count++;
            }
        }
        s_dispellableLut = lut;
        s_dispellableLutCount = count;
        s_lutInited = true;
        s_rulesInited = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsDispellableStatus(uint statusId)
    {
        // Fast path: boolean LUT built once per process
        if (s_lutInited && s_dispellableLut is { Length: > 0 } lut)
        {
            return statusId <= ushort.MaxValue && lut[(ushort)statusId];
        }
        // Fallback to set until InitializeRules runs
        return s_dispellableStatuses.Contains(statusId);
    }

    // Diagnostics for Debug UI: quick accessors for the dispellable status LUT
    public static bool DispellableLutInitialized => s_lutInited;
    public static int DispellableLutCount => s_dispellableLutCount;

    public static bool TryGetDispellableLutSample(Span<ushort> buffer, out int filled)
    {
        filled = 0;
        if (!s_lutInited || s_dispellableLut is not { Length: > 0 } lut || buffer.Length == 0)
            return false;
        int f = 0;
        for (int i = 0; i < lut.Length && f < buffer.Length; i++)
        {
            if (lut[i])
            {
                buffer[f++] = (ushort)i;
            }
        }
        filled = f;
        return f > 0;
    }

    // Sanctuary API
    public static bool IsInSanctuary => s_inSanctuary;

    public static void UpdateSanctuary(IDataManager data, ushort territoryId)
    {
        if (territoryId == 0 || data == null) { s_inSanctuary = false; return; }
        var sheet = data.GetExcelSheet<TerritoryType>();
        if (sheet == null) { s_inSanctuary = false; return; }
        var row = sheet.GetRow(territoryId);
        // Lumina rows are structs; treat RowId==0 as invalid/missing for safety
        if (row.RowId == 0) { s_inSanctuary = false; return; }
        bool isTown = TryGetBoolProperty(row, "IsTown");
        bool isResidential = TryGetBoolProperty(row, "IsResidentialArea") || TryGetBoolProperty(row, "IsHousing") || TryGetBoolProperty(row, "IsResidential");
        s_inSanctuary = isTown || isResidential;
    }

    private static bool TryGetBoolProperty(object row, string propName)
    {
        var t = row.GetType();
        var p = t.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p == null) return false;
        var v = p.GetValue(row);
        if (v is bool b) return b;
        if (v is byte by) return by != 0;
        if (v is sbyte sb) return sb != 0;
        if (v is int i) return i != 0;
        if (v is uint ui) return ui != 0;
        return false;
    }

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

    // Generic gauge access (engine-agnostic): ask for a typed gauge snapshot.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadGauge<T>(out T gauge) => _game.TryReadGauge<T>(out gauge);

    // Lazily built per-tick cache: party/self who currently have a dispellable debuff
    private readonly ulong[] _cleansableIds = new ulong[16];
    private byte _cleansableCount;
    private bool _cleansableBuilt;
    private ulong _cleansableSetFrame;

    // Per-tick top candidates caches to avoid redundant object table enumerations
    private ulong _healableA, _healableB, _healableC, _healableD; private bool _healableTopBuilt; private ulong _healableTopFrame;
    private ulong _cleansableTopA, _cleansableTopB, _cleansableTopC, _cleansableTopD; private bool _cleansableTopBuilt; private ulong _cleansableTopFrame;

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

    // Anchor actions registry: base actions that can trigger planner GCD replacements (e.g., fillers)
    private readonly int[] _anchorActions = new int[8];
    private byte _anchorCount;

    // No separate flags needed for no-target actions; planner gating uses presence of a hard target only.

    // Alliance/raid support intentionally omitted for lean core.

    /// <summary> Count party members at or below the given HP% (inclusive). </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CountPartyBelow(byte hpPercent)
    {
        var span = PartyHpPct;
        var n = 0;
        for (int i = 0; i < span.Length; i++) if (span[i] <= hpPercent && span[i] != 0) n++;
        return n;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsPartyMember(ulong id)
    {
        if (id == 0 || _partyCount == 0) return false;
        // Linear scan of small fixed-size array; no allocations
        for (int i = 0; i < _partyCount; i++)
        {
            if (_partyRefs[i].IsValid && _partyRefs[i].Id == id) return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsPartyMember(Dalamud.Game.ClientState.Objects.Types.ICharacter ch)
    {
        if (ch == null) return false;
        var id = ch.GameObjectId;
        if (IsPartyMember(id)) return true;
        // Fallback to name match (covers Trusts/NPCs when GO id changes)
        var name = ch.Name?.TextValue;
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < _partyCount; i++)
        {
            if (!string.IsNullOrEmpty(_partyNames[i]) && _partyNames[i] == name) return true;
        }
        return false;
    }

    // Role classifier for card targeting
    private enum Role : byte { Unknown = 0, Tank, Healer, Melee, Ranged, Caster }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Role ClassifyRole(int job)
    {
        if ((uint)job < (uint)s_jobToRole.Length)
        {
            var r = s_jobToRole[job];
            if (r != Role.Unknown) return r;
        }
        return Role.Unknown;
    }

    // Compact, branchless job->role lookup (covers battle jobs; others remain Unknown)
    private static readonly Role[] s_jobToRole = InitJobRoleLut();
    private static Role[] InitJobRoleLut()
    {
        var lut = new Role[64];
        // Tanks
        lut[19] = Role.Tank; // PLD
        lut[21] = Role.Tank; // WAR
        lut[32] = Role.Tank; // DRK
        lut[37] = Role.Tank; // GNB
        // Healers
        lut[24] = Role.Healer; // WHM
        lut[28] = Role.Healer; // SCH
        lut[33] = Role.Healer; // AST
        lut[40] = Role.Healer; // SGE
        // Melee DPS
        lut[20] = Role.Melee; // MNK
        lut[22] = Role.Melee; // DRG
        lut[30] = Role.Melee; // NIN
        lut[34] = Role.Melee; // SAM
        lut[39] = Role.Melee; // RPR
        lut[41] = Role.Melee; // VPR
        // Physical Ranged
        lut[23] = Role.Ranged; // BRD
        lut[31] = Role.Ranged; // MCH
        lut[38] = Role.Ranged; // DNC
        // Casters
        lut[25] = Role.Caster; // BLM
        lut[35] = Role.Caster; // SMN
        lut[36] = Role.Caster; // RDM
        lut[42] = Role.Caster; // PCT
        return lut;
    }

    public readonly struct RoleBuckets
    {
        public readonly ulong Self;
        public readonly ulong FirstTank;
        public readonly ulong Melee;
        public readonly ulong Ranged;
        public readonly ulong AnyDps;
        public readonly ulong LowestHpAlly;
        public readonly byte LowestHpPct;
        public readonly int Seen;
        public readonly int SkippedNonParty;
        public readonly int UnknownRoles;

        public RoleBuckets(ulong self, ulong firstTank, ulong melee, ulong ranged, ulong anyDps, ulong lowestHpAlly, byte lowestHpPct, int seen, int skipped, int unknown)
        { Self = self; FirstTank = firstTank; Melee = melee; Ranged = ranged; AnyDps = anyDps; LowestHpAlly = lowestHpAlly; LowestHpPct = lowestHpPct; Seen = seen; SkippedNonParty = skipped; UnknownRoles = unknown; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RoleBuckets BuildRoleBucketsFromObjectTable(ulong selfId)
    {
        ulong melee = 0, ranged = 0, anyDps = 0, lowestHpAlly = 0; byte lowestHp = 101;
        ulong firstTank = 0;
        int seen = 0, skipped = 0, unknown = 0;
        foreach (var ch in _game.EnumerateCharacters())
        {
            var id = ch.Id; if (id == 0 || id == selfId) continue;
            if (!IsPartyMember(id)) { skipped++; continue; }
            seen++;
            byte hpPct = HpPct(ch.CurrentHp, ch.MaxHp);
            if (hpPct > 0 && hpPct < lowestHp) { lowestHp = hpPct; lowestHpAlly = id; }
            var role = ClassifyRole(ch.JobId);
            if (role == Role.Unknown) unknown++;
            switch (role)
            {
                case Role.Tank: if (firstTank == 0) firstTank = id; break;
                case Role.Melee: if (melee == 0) melee = id; if (anyDps == 0) anyDps = id; break;
                case Role.Ranged: if (ranged == 0) ranged = id; if (anyDps == 0) anyDps = id; break;
                case Role.Caster: if (ranged == 0) ranged = id; if (anyDps == 0) anyDps = id; break;
                default: break;
            }
        }
        return new RoleBuckets(selfId, firstTank, melee, ranged, anyDps, lowestHpAlly, lowestHp, seen, skipped, unknown);
    }

    // Cached per-tick role buckets built lazily from the ObjectTable and party/trust membership
    private RoleBuckets _roleBuckets;
    private bool _roleBucketsBuilt;
    private ulong _roleBucketsFrame;
    public RoleBuckets Buckets
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetRoleBuckets(Player.Id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RoleBuckets GetRoleBuckets(ulong selfId)
    {
        if (_roleBucketsBuilt && _roleBucketsFrame == _frame && _roleBuckets.Self == selfId && selfId != 0)
            return _roleBuckets;
        _roleBuckets = BuildRoleBucketsFromObjectTable(selfId);
        _roleBucketsBuilt = true;
        _roleBucketsFrame = _frame;
        return _roleBuckets;
    }

    public void Update()
    {
        _frame++;
        // Player
        if (_game.TryGetLocalPlayer(out var pl))
        {
            var prevPos = _hot.PlayerPos;
            Player = ObjRef.FromId(pl.Id);
            _hot.PlayerPos = pl.Position;
            _hot.PlayerHpPct = HpPct(pl.CurrentHp, pl.MaxHp);
            _hot.PlayerAlive = pl.Alive;
            _hot.PlayerMp = (int)pl.CurrentMp;
            _hot.PlayerLevel = pl.Level;
            _hot.PlayerJobId = pl.JobId;
            var dx = _hot.PlayerPos.X - prevPos.X;
            var dy = _hot.PlayerPos.Y - prevPos.Y;
            var dz = _hot.PlayerPos.Z - prevPos.Z;
            PlayerMoving = (dx * dx + dy * dy + dz * dz) > 0.0001f;
            _prevPlayerPos = prevPos;
        }
        else { Player = default; _hot.PlayerPos = default; _hot.PlayerHpPct = 0; _hot.PlayerAlive = false; _hot.PlayerMp = 0; _hot.PlayerLevel = 0; _hot.PlayerJobId = 0; PlayerMoving = false; _prevPlayerPos = default; }

        // Targets
        _game.GetTargets(out var hard, out var focus, out var soft);
        Target = hard != 0 ? ObjRef.FromId(hard) : null;
        FocusTarget = focus != 0 ? ObjRef.FromId(focus) : null;
        SoftTarget = soft != 0 ? ObjRef.FromId(soft) : null;

        // Flags (fast checks only)
        var cf = _game.GetCombatFlags();
        _hot.Flags = new CombatFlags(cf.InCombat, cf.Mounted, cf.Casting);

        // Party refs
        var ps = _game.GetPartySnapshot();
        var count = ps.Count;
        if (count > MaxParty) count = MaxParty;
        var oldCount = _partyCount;
        _partyCount = count;
        var idx = 0;
        var partyIdx = 0;
        _partyDeadCount = 0;
        _firstDeadPartyMember = 0UL;
        for (int k = 0; k < count; k++)
        {
            var pm = ps[k];
            _partyRefs[idx] = ObjRef.FromId(pm.Id);
            byte pct = HpPct(pm.CurrentHp, pm.MaxHp);
            _partyHpPct[idx] = pct;
            _partyAlive[idx] = pm.Alive;
            if (!pm.Alive)
            {
                if (_partyDeadCount < byte.MaxValue) _partyDeadCount++;
                if (_firstDeadPartyMember == 0UL && pm.Id != 0) _firstDeadPartyMember = pm.Id;
            }
            _partyPos[idx] = pm.Position;
            _partyPosKnown[idx] = pm.HasPosition;
            _partyNames[idx] = pm.Name ?? string.Empty;
            idx++;
            partyIdx++;
            if (idx >= count) break;
        }
        // Clear stale slots if party shrank
        if (oldCount > count)
        {
            for (int j = count; j < oldCount; j++)
            {
                _partyRefs[j] = default;
                _partyHpPct[j] = 0;
                _partyAlive[j] = false;
                _partyPos[j] = default;
                _partyPosKnown[j] = false;
                _partyNames[j] = string.Empty;
            }
        }

        ResetPerTickCaches();

        // Update tracked action readiness (abilities/spells) for this tick
        {
            var n = _trackedCount;
            for (int i = 0; i < n; i++)
            {
                var id = _trackedRecasts[i];
                _trackedReady[i] = _game.IsActionReady(id);
            }
        }

        // Job-specific gauges are not handled here; jobs may query their gauges directly as needed.
    }

    /// <summary>
    /// Build candidates directly from ObjectTable for objects we can heal with the current action.
    /// Prioritize lowest HP% (1..99). Can optionally include self so self can win when lowest.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CandidateList BuildHealableFromObjectTable(int actionId, int maxCount, bool includeSelf = false)
    {
        if (maxCount < 1) maxCount = 1; else if (maxCount > 4) maxCount = 4;
        CandidateList list = default;
        EnsureHealableTopBuilt();
        var self = Player.Id;
        // Filter cached top by includeSelf and action-specific readiness
        if (_healableA != 0 && (includeSelf || _healableA != self) && _game.CanUseActionOnTarget(actionId, _healableA)) list.Add(_healableA, maxCount);
        if (!list.IsFull(maxCount) && _healableB != 0 && (includeSelf || _healableB != self) && _game.CanUseActionOnTarget(actionId, _healableB)) list.Add(_healableB, maxCount);
        if (!list.IsFull(maxCount) && _healableC != 0 && (includeSelf || _healableC != self) && _game.CanUseActionOnTarget(actionId, _healableC)) list.Add(_healableC, maxCount);
        if (!list.IsFull(maxCount) && _healableD != 0 && (includeSelf || _healableD != self) && _game.CanUseActionOnTarget(actionId, _healableD)) list.Add(_healableD, maxCount);
        return list;
    }


    /// <summary>
    /// Build candidates from ObjectTable for party members who currently have a dispellable debuff.
    /// Sorted by lowest HP% first. Optionally includes self if affected.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CandidateList BuildCleansableFromObjectTable(int actionId, int maxCount, bool includeSelf = false)
    {
        if (maxCount < 1) maxCount = 1; else if (maxCount > 4) maxCount = 4;
        CandidateList list = default;
        EnsureCleansableTopBuilt();
        var self = Player.Id;
        if (_cleansableTopA != 0 && (includeSelf || _cleansableTopA != self) && _game.CanUseActionOnTarget(actionId, _cleansableTopA)) list.Add(_cleansableTopA, maxCount);
        if (!list.IsFull(maxCount) && _cleansableTopB != 0 && (includeSelf || _cleansableTopB != self) && _game.CanUseActionOnTarget(actionId, _cleansableTopB)) list.Add(_cleansableTopB, maxCount);
        if (!list.IsFull(maxCount) && _cleansableTopC != 0 && (includeSelf || _cleansableTopC != self) && _game.CanUseActionOnTarget(actionId, _cleansableTopC)) list.Add(_cleansableTopC, maxCount);
        if (!list.IsFull(maxCount) && _cleansableTopD != 0 && (includeSelf || _cleansableTopD != self) && _game.CanUseActionOnTarget(actionId, _cleansableTopD)) list.Add(_cleansableTopD, maxCount);

        // Include self last if requested and affected
        if (!list.IsFull(maxCount) && includeSelf && Player.IsValid)
        {
            if (IsCleansableCached(self) && _game.CanUseActionOnTarget(actionId, self))
                list.Add(self, maxCount);
        }

        return list;
    }



    /// <summary>
    /// Fast healability check: can the given action be used on the object with id now?
    /// Iterates the object table once to find the IGameObject, then calls ActionManager.
    /// </summary>
    // CanUseOnTarget moved behind IGameView; keep no static here

    /// <summary>
    /// Returns true if the object with the given id is a character and currently has a dispellable debuff.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasCleansableOnId(ulong targetId)
    {
        if (targetId == 0) return false;
        return IsCleansableCached(targetId);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public bool CanUseOnTarget(int actionId, ulong targetId) => _game.CanUseActionOnTarget(actionId, targetId);

    /// <summary> Returns true if the local player currently has the given status. </summary>
    public bool PlayerHasStatus(ushort statusId) => _game.PlayerHasStatus(statusId);

    /// <summary>
    /// Configure which action IDs to track for readiness each tick (per job/profile). Max 16.
    /// </summary>
    public void ConfigureRecasts(ReadOnlySpan<int> actionIds)
    {
        var max = Math.Min(actionIds.Length, _trackedRecasts.Length);
        for (int i = 0; i < max; i++) _trackedRecasts[i] = actionIds[i];
        for (int i = max; i < _trackedRecasts.Length; i++) _trackedRecasts[i] = 0;
        _trackedCount = (byte)max;
    }

    /// <summary>Alias for ConfigureRecasts for clearer semantics.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ConfigureCooldowns(ReadOnlySpan<int> actionIds) => ConfigureRecasts(actionIds);

    public ReadOnlySpan<int> TrackedCooldowns
        => _trackedRecasts.AsSpan(0, _trackedCount);

    public bool TryGetCooldownRemainingMs(int actionId, out int remainingMs)
        => _game.TryGetActionRemainingMs(actionId, out remainingMs);

    /// <summary>
    /// Fast readiness check for an action. Uses tracked map when available; otherwise queries ActionManager directly.
    /// </summary>
    public bool IsActionReady(int actionId)
    {
        // Same-tick guard: if we just used this action, consider not-ready to avoid repeats
        if (_justUsedActionId == actionId) return false;
        var n = _trackedCount;
        for (int i = 0; i < n; i++) if (_trackedRecasts[i] == actionId) return _trackedReady[i];
        return _game.IsActionReady(actionId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkActionUsed(int actionId)
    {
        _justUsedActionId = actionId;
    }

    /// <summary>
    /// Register a debuff mapping: the given action applies one or more statusIds on its target.
    /// Max 16 entries; each entry stores up to 3 status ids.
    /// </summary>
    public void ConfigureDebuffMapping(int actionId, ReadOnlySpan<ushort> statusIds)
    {
        var idx = _debuffMapCount;
        if (idx >= _debuffActionIds.Length) return;
        _debuffActionIds[idx] = actionId;
        _debuffStatusA[idx] = statusIds.Length > 0 ? statusIds[0] : (ushort)0;
        _debuffStatusB[idx] = statusIds.Length > 1 ? statusIds[1] : (ushort)0;
        _debuffStatusC[idx] = statusIds.Length > 2 ? statusIds[2] : (ushort)0;
        _debuffMapCount++;
    }

    /// <summary>
    /// Configure which pressed actions are considered anchors for planner GCD replacements.
    /// Typically set to job fillers (e.g., ST and AoE). Max 8.
    /// </summary>
    public void ConfigureAnchors(ReadOnlySpan<int> actionIds)
    {
        var max = Math.Min(actionIds.Length, _anchorActions.Length);
        for (int i = 0; i < max; i++) _anchorActions[i] = actionIds[i];
        for (int i = max; i < _anchorActions.Length; i++) _anchorActions[i] = 0;
        _anchorCount = (byte)max;
    }

    /// <summary> Clear anchor registry. </summary>
    public void ClearAnchors()
    {
        for (int i = 0; i < _anchorActions.Length; i++) _anchorActions[i] = 0;
        _anchorCount = 0;
    }

    /// <summary> Returns true if actionId is currently an anchor (base/filler) for GCD replacement. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAnchorAction(int actionId)
    {
        var n = _anchorCount;
        for (int i = 0; i < n; i++) if (_anchorActions[i] == actionId) return true;
        return false;
    }

    /// <summary> Return the currently configured anchor actions. </summary>
    public ReadOnlySpan<int> Anchors => _anchorActions.AsSpan(0, _anchorCount);

    // Family/alias matching removed: dynamic anchors are the source of truth.

    // No-target action configuration removed; anchors alone determine planner eligibility.

    /// <summary>
    /// Clear all configured mappings (buff and debuff). Currently clears debuffs; extend when buff mappings are added.
    /// </summary>
    public void ClearMappings()
    {
        // Debuff mappings
        for (int i = 0; i < _debuffActionIds.Length; i++) { _debuffActionIds[i] = 0; _debuffStatusA[i] = 0; _debuffStatusB[i] = 0; _debuffStatusC[i] = 0; }
        _debuffMapCount = 0;

        // Buff mappings: none yet (placeholder for future self-buff tracking)

        // Cooldown tracking
        for (int i = 0; i < _trackedRecasts.Length; i++) { _trackedRecasts[i] = 0; _trackedReady[i] = false; }
        _trackedCount = 0;

        // Anchors
        ClearAnchors();

        // No-target action flags: none
    }

    public ReadOnlySpan<int> TrackedDebuffActions
        => _debuffActionIds.AsSpan(0, _debuffMapCount);

    /// <summary>
    /// Try to get the max remaining time in milliseconds for any debuff mapped to the given actionId on targetId.
    /// Returns true when at least one mapped status is present; false when none found or target invalid.
    /// </summary>
    public bool TryGetDebuffRemainingMsForActionOnTarget(int actionId, ulong targetId, out int remainingMs)
    {
        remainingMs = 0;
        if (targetId == 0) return false;
        ushort a = 0, b = 0, c = 0;
        var n = _debuffMapCount;
        for (int i = 0; i < n; i++)
        {
            if (_debuffActionIds[i] == actionId)
            {
                a = _debuffStatusA[i]; b = _debuffStatusB[i]; c = _debuffStatusC[i];
                break;
            }
        }
        if (a == 0 && b == 0 && c == 0) return false;
        if (_game.TryGetMaxStatusRemaining(targetId, a, b, c, out var seconds))
        {
            remainingMs = (int)MathF.Round(MathF.Max(0f, seconds * 1000f));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the debuff applied by the given actionId is missing on the current target, or has remaining time <= thresholdMs.
    /// If there is no valid target or no mapping exists, returns false (no refresh pressure). If target is valid but uninspectable, returns true conservatively.
    /// </summary>
    public bool DoesDebuffNeedRefresh(int actionId, int thresholdMs)
    {
        // Resolve a valid target: prefer hard, then focus, then soft
        ulong targetId = 0;
        if (Target.HasValue && Target.Value.IsValid) targetId = Target.Value.Id;
        else if (FocusTarget.HasValue && FocusTarget.Value.IsValid) targetId = FocusTarget.Value.Id;
        else if (SoftTarget.HasValue && SoftTarget.Value.IsValid) targetId = SoftTarget.Value.Id;
        if (targetId == 0) return false;

        // Lookup status ids mapped to this action
        ushort a = 0, b = 0, c = 0;
        var n = _debuffMapCount;
        for (int i = 0; i < n; i++)
        {
            if (_debuffActionIds[i] == actionId)
            {
                a = _debuffStatusA[i]; b = _debuffStatusB[i]; c = _debuffStatusC[i];
                break;
            }
        }
        if (a == 0 && b == 0 && c == 0) return false; // nothing to check

        if (_game.TryGetMaxStatusRemaining(targetId, a, b, c, out var bestRem))
            return bestRem * 1000f <= thresholdMs;
        // If we can't find the target or the statuses, conservatively say it needs refresh
        return true;
    }

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

    // no misc helpers beyond what hot path needs
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

// Private helpers
partial class GameSnapshot
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureHealableTopBuilt()
    {
        if (_healableTopBuilt && _healableTopFrame == _frame) return;
        ulong aId = 0, bId = 0, cId = 0, dId = 0; byte aHp = 101, bHp = 101, cHp = 101, dHp = 101;
        var self = Player.Id;
        foreach (var ch in _game.EnumerateCharacters())
        {
            var id = ch.Id;
            if (id == 0) continue;
            byte hpPct = HpPct(ch.CurrentHp, ch.MaxHp);
            // Only consider entries that are actually missing HP
            if (hpPct > 0 && hpPct < 100)
            {
                if (hpPct < aHp) { dHp = cHp; dId = cId; cHp = bHp; cId = bId; bHp = aHp; bId = aId; aHp = hpPct; aId = id; }
                else if (hpPct < bHp && id != aId) { dHp = cHp; dId = cId; cHp = bHp; cId = bId; bHp = hpPct; bId = id; }
                else if (hpPct < cHp && id != aId && id != bId) { dHp = cHp; dId = cId; cHp = hpPct; cId = id; }
                else if (hpPct < dHp && id != aId && id != bId && id != cId) { dHp = hpPct; dId = id; }
            }
        }
        _healableA = aId; _healableB = bId; _healableC = cId; _healableD = dId; _healableTopBuilt = true; _healableTopFrame = _frame;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCleansableTopBuilt()
    {
        if (_cleansableTopBuilt && _cleansableTopFrame == _frame) return;
        EnsureCleansableBuilt();
        ulong aId = 0, bId = 0, cId = 0, dId = 0; byte aHp = 101, bHp = 101, cHp = 101, dHp = 101;
        foreach (var ch in _game.EnumerateCharacters())
        {
            var id = ch.Id;
            if (id == 0) continue;
            if (!IsPartyMember(id) && id != Player.Id) continue;
            if (!IsCleansableCached(id)) continue;
            byte hpPct = HpPct(ch.CurrentHp, ch.MaxHp);
            if (hpPct > 0)
            {
                if (hpPct < aHp) { dHp = cHp; dId = cId; cHp = bHp; cId = bId; bHp = aHp; bId = aId; aHp = hpPct; aId = id; }
                else if (hpPct < bHp && id != aId) { dHp = cHp; dId = cId; cHp = bHp; cId = bId; bHp = hpPct; bId = id; }
                else if (hpPct < cHp && id != aId && id != bId) { dHp = cHp; dId = cId; cHp = hpPct; cId = id; }
                else if (hpPct < dHp && id != aId && id != bId && id != cId) { dHp = hpPct; dId = id; }
            }
        }
        _cleansableTopA = aId; _cleansableTopB = bId; _cleansableTopC = cId; _cleansableTopD = dId; _cleansableTopBuilt = true; _cleansableTopFrame = _frame;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsCleansableCached(ulong id)
    {
        EnsureCleansableBuilt();
        var n = _cleansableCount;
        if (n == 0 || id == 0) return false;
        for (int i = 0; i < n; i++)
            if (_cleansableIds[i] == id) return true;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte HpPct(uint current, uint max)
    {
        if (max == 0) return 0;
        var pct = (int)(current * 100UL / max);
        if (pct <= 0) return 0;
        if (pct >= 100) return 100;
        return (byte)pct;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetPerTickCaches()
    {
        _cleansableCount = 0;
        _cleansableBuilt = false;
        _cleansableTopA = _cleansableTopB = _cleansableTopC = _cleansableTopD = 0; _cleansableTopBuilt = false;

        _healableA = _healableB = _healableC = _healableD = 0; _healableTopBuilt = false;

        _justUsedActionId = 0;
        _roleBucketsBuilt = false;
    }

    /// <summary>Try to get the first dead party member id, if any.</summary>
    public bool TryGetFirstDeadPartyMember(out ulong id)
    {
        id = _firstDeadPartyMember;
        return id != 0;
    }
}
