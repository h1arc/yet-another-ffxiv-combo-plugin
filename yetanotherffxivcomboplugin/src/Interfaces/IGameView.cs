using System;
using System.Collections.Generic;
using System.Numerics;

namespace yetanotherffxivcomboplugin.src.Interfaces;

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
    // Object metrics
    bool TryGetHitboxRadius(ulong objectId, out float radius);

    // Status query: get the max remaining time (seconds) among up to three status IDs on a target.
    // Returns true if at least one status is present; false otherwise.
    bool TryGetMaxStatusRemaining(ulong targetId, ushort statusA, ushort statusB, ushort statusC, out float remainingSeconds);

    // Generic job gauge accessor. Implementations may pattern-match on T.
    bool TryReadGauge<T>(out T gauge);
}

public readonly struct PlayerSnapshot(ulong id, Vector3 pos, int jobId, uint hp, uint maxHp, uint mp, byte level, bool alive)
{
    public readonly ulong Id = id;
    public readonly Vector3 Position = pos;
    public readonly int JobId = jobId;
    public readonly uint CurrentHp = hp;
    public readonly uint MaxHp = maxHp;
    public readonly uint CurrentMp = mp;
    public readonly byte Level = level;
    public readonly bool Alive = alive;
}

public readonly struct PartyMemberSnapshot(ulong id, string name, uint hp, uint maxHp, bool alive, Vector3 pos, bool hasPos)
{
    public readonly ulong Id = id;              // 0 when unresolved
    public readonly string Name = name;         // may be empty
    public readonly uint CurrentHp = hp;
    public readonly uint MaxHp = maxHp;
    public readonly bool Alive = alive;
    public readonly Vector3 Position = pos;     // default if unknown
    public readonly bool HasPosition = hasPos;
}

public readonly struct CharacterSnapshot(ulong id, string name, int jobId, uint hp, uint maxHp, Vector3 pos)
{
    public readonly ulong Id = id;
    public readonly string Name = name;
    public readonly int JobId = jobId;          // 0 when unknown
    public readonly uint CurrentHp = hp;
    public readonly uint MaxHp = maxHp;
    public readonly Vector3 Position = pos;
}
