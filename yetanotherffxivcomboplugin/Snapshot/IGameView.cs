using System;
using System.Collections.Generic;
using System.Numerics;

namespace yetanotherffxivcomboplugin.Snapshot;

/// <summary>
/// Backend-agnostic read-only view of game state. Implementations adapt specific APIs (Dalamud/FFXIV) to this contract.
/// Engine code should depend on this interface rather than concrete services.
/// </summary>
public interface IGameView
{
    // Player and targets
    bool TryGetLocalPlayer(out PlayerSnapshot player);
    void GetTargets(out ulong hardTargetId, out ulong focusTargetId, out ulong softTargetId);

    // Common combat flags
    (bool InCombat, bool Mounted, bool Casting) GetCombatFlags();

    // Party snapshot (at most 8 entries for standard party)
    IReadOnlyList<PartyMemberSnapshot> GetPartySnapshot();

    // World character enumeration (used for candidate building)
    IEnumerable<CharacterSnapshot> EnumerateCharacters();

    // Action/query helpers
    bool CanUseActionOnTarget(int actionId, ulong targetId);
    bool IsActionReady(int actionId);
    // Returns true if a cooldown entry exists; remainingMs is clamped to >= 0. Probes both Action and Ability internally.
    bool TryGetActionRemainingMs(int actionId, out int remainingMs);
    bool PlayerHasStatus(ushort statusId);
    bool HasCleansableDebuff(ulong targetId);

    // Status query: get the max remaining time (seconds) among up to three status IDs on a target.
    // Returns true if at least one status is present; false otherwise.
    bool TryGetMaxStatusRemaining(ulong targetId, ushort statusA, ushort statusB, ushort statusC, out float remainingSeconds);

    // Generic job gauge accessor. Implementations may pattern-match on T.
    bool TryReadGauge<T>(out T gauge);
}

public readonly struct PlayerSnapshot
{
    public readonly ulong Id;
    public readonly Vector3 Position;
    public readonly int JobId;
    public readonly uint CurrentHp;
    public readonly uint MaxHp;
    public readonly uint CurrentMp;
    public readonly byte Level;
    public readonly bool Alive;
    public PlayerSnapshot(ulong id, Vector3 pos, int jobId, uint hp, uint maxHp, uint mp, byte level, bool alive)
    { Id = id; Position = pos; JobId = jobId; CurrentHp = hp; MaxHp = maxHp; CurrentMp = mp; Level = level; Alive = alive; }
}

public readonly struct PartyMemberSnapshot
{
    public readonly ulong Id;           // 0 when unresolved
    public readonly string Name;        // may be empty
    public readonly uint CurrentHp;
    public readonly uint MaxHp;
    public readonly bool Alive;
    public readonly Vector3 Position;   // default if unknown
    public readonly bool HasPosition;
    public PartyMemberSnapshot(ulong id, string name, uint hp, uint maxHp, bool alive, Vector3 pos, bool hasPos)
    { Id = id; Name = name; CurrentHp = hp; MaxHp = maxHp; Alive = alive; Position = pos; HasPosition = hasPos; }
}

public readonly struct CharacterSnapshot
{
    public readonly ulong Id;
    public readonly string Name;
    public readonly int JobId;          // 0 when unknown
    public readonly uint CurrentHp;
    public readonly uint MaxHp;
    public readonly Vector3 Position;
    public CharacterSnapshot(ulong id, string name, int jobId, uint hp, uint maxHp, Vector3 pos)
    { Id = id; Name = name; JobId = jobId; CurrentHp = hp; MaxHp = maxHp; Position = pos; }
}
