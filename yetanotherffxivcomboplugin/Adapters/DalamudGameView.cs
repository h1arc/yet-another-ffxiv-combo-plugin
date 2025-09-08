using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects;
using yetanotherffxivcomboplugin.Snapshot;

namespace yetanotherffxivcomboplugin.Adapters;

/// <summary>
/// Concrete IGameView backed by Dalamud services exposed by Plugin.
/// This isolates all direct API calls from the engine.
/// </summary>
internal sealed class DalamudGameView : IGameView
{
    private readonly IClientState _clientState;
    private readonly ITargetManager _targets;
    private readonly IPartyList _party;
    private readonly IObjectTable _objects;
    private readonly ICondition _condition;

    public DalamudGameView(IClientState clientState, ITargetManager targets, IPartyList party, IObjectTable objects, ICondition condition)
    {
        _clientState = clientState;
        _targets = targets;
        _party = party;
        _objects = objects;
        _condition = condition;
    }

    public bool TryGetLocalPlayer(out PlayerSnapshot player)
    {
        if (_clientState.LocalPlayer is not IPlayerCharacter p) { player = default; return false; }
        var pos = p.Position;
        var hp = p.CurrentHp;
        var maxHp = p.MaxHp;
        var mp = p.CurrentMp;
        var level = p.Level;
        var alive = hp > 0;
        var jobId = (int)p.ClassJob.Value.RowId;
        player = new PlayerSnapshot(p.GameObjectId, pos, jobId, hp, maxHp, mp, level, alive);
        return true;
    }

    public void GetTargets(out ulong hardTargetId, out ulong focusTargetId, out ulong softTargetId)
    {
        hardTargetId = _targets.Target?.GameObjectId ?? 0UL;
        focusTargetId = _targets.FocusTarget?.GameObjectId ?? 0UL;
        softTargetId = _targets.SoftTarget?.GameObjectId ?? 0UL;
    }

    public IReadOnlyList<PartyMemberSnapshot> GetPartySnapshot()
    {
        var list = new List<PartyMemberSnapshot>(8);
        foreach (var m in _party)
        {
            if (m == null) { list.Add(new PartyMemberSnapshot(0, string.Empty, 0, 0, false, default, false)); continue; }
            string name = m.Name?.TextValue ?? m.Name?.ToString() ?? string.Empty;
            ulong id = m.GameObject?.GameObjectId ?? m.ObjectId;
            uint hp = m.CurrentHP;
            uint max = m.MaxHP;
            bool alive = hp > 0;
            Vector3 pos = default;
            bool hasPos = false;
            if (m.GameObject is ICharacter ch)
            { pos = ch.Position; hasPos = true; }
            list.Add(new PartyMemberSnapshot(id, name, hp, max, alive, pos, hasPos));
        }
        return list;
    }

    public (bool InCombat, bool Mounted, bool Casting) GetCombatFlags()
    {
        return (
            _condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat],
            _condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted],
            _condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Casting]
        );
    }

    public IEnumerable<CharacterSnapshot> EnumerateCharacters()
    {
        foreach (var o in _objects)
        {
            if (o is not ICharacter ch) continue;
            ulong id = ch.GameObjectId;
            if (id == 0) continue;
            string name = ch.Name.TextValue;
            int job = (int)ch.ClassJob.Value.RowId;
            uint hp = ch.CurrentHp;
            uint max = ch.MaxHp;
            Vector3 pos = ch.Position;
            yield return new CharacterSnapshot(id, name, job, hp, max, pos);
        }
    }

    public unsafe bool CanUseActionOnTarget(int actionId, ulong targetId)
    {
        if (targetId == 0) return false;
        IGameObject? obj = null;
        foreach (var o in _objects) { if (o != null && o.GameObjectId == targetId) { obj = o; break; } }
        if (obj is null) return false;
        var ptr = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
        if (ptr == null) return false;
        return FFXIVClientStructs.FFXIV.Client.Game.ActionManager.CanUseActionOnTarget((uint)actionId, ptr);
    }

    public unsafe bool IsActionReady(int actionId)
    {
        var am = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
        if (am == null) return false;
        // Probe both Action (spells/GCD) and Ability (oGCD).
        // For charge actions, the recast timer can be > 0 while the action is still usable (has charges).
        // Trust the game's usability flag first, then fall back to recast time as a conservative check.
        var stAct = am->GetActionStatus(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action, (uint)actionId) == 0;
        var stAbl = am->GetActionStatus(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Ability, (uint)actionId) == 0;
        if (stAct || stAbl)
            return true;

        // If neither path reports usable, consider recast timers; zero combined remaining means ready.
        var tAct = am->GetRecastTime(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action, (uint)actionId);
        var eAct = am->GetRecastTimeElapsed(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action, (uint)actionId);
        var remAct = MathF.Max(0f, tAct - eAct);
        var tAbl = am->GetRecastTime(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Ability, (uint)actionId);
        var eAbl = am->GetRecastTimeElapsed(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Ability, (uint)actionId);
        var remAbl = MathF.Max(0f, tAbl - eAbl);
        var remaining = MathF.Max(remAct, remAbl);
        return remaining <= 0.0001f;
    }

    public unsafe bool TryGetActionRemainingMs(int actionId, out int remainingMs)
    {
        remainingMs = 0;
        var am = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
        if (am == null) return false;
        float remAction = 0f;
        float remAbility = 0f;
        // Action
        {
            var t = am->GetRecastTime(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action, (uint)actionId);
            var e = am->GetRecastTimeElapsed(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action, (uint)actionId);
            remAction = MathF.Max(0f, t - e);
        }
        // Ability
        {
            var t = am->GetRecastTime(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Ability, (uint)actionId);
            var e = am->GetRecastTimeElapsed(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Ability, (uint)actionId);
            remAbility = MathF.Max(0f, t - e);
        }
        var sec = MathF.Max(remAction, remAbility);
        if (sec > 0f)
        {
            remainingMs = (int)MathF.Round(sec * 1000f);
            return true;
        }
        return false;
    }

    public bool PlayerHasStatus(ushort statusId)
    {
        if (_clientState.LocalPlayer is not IPlayerCharacter p) return false;
        foreach (var st in p.StatusList) { if (st != null && st.StatusId == statusId) return true; }
        return false;
    }

    public bool HasCleansableDebuff(ulong targetId)
    {
        foreach (var o in _objects)
        {
            if (o is not IBattleChara ch) continue;
            if (o.GameObjectId != targetId) continue;
            foreach (var st in ch.StatusList)
            {
                if (st == null) continue;
                if (GameSnapshot.IsDispellableStatus(st.StatusId))
                    return true;
            }
            break;
        }
        return false;
    }

    public bool TryGetMaxStatusRemaining(ulong targetId, ushort statusA, ushort statusB, ushort statusC, out float remainingSeconds)
    {
        remainingSeconds = -1f;
        if (targetId == 0) return false;
        IBattleChara? found = null;
        foreach (var o in _objects)
        {
            if (o is not IBattleChara ch) continue;
            if (ch.GameObjectId != targetId) continue;
            found = ch; break;
        }
        if (found is null) return false;
        float best = -1f;
        foreach (var st in found.StatusList)
        {
            if (st == null) continue;
            var sid = st.StatusId;
            if (sid == statusA || sid == statusB || sid == statusC)
            {
                var rem = st.RemainingTime; // seconds
                if (rem > best) best = rem;
            }
        }
        if (best >= 0f) { remainingSeconds = best; return true; }
        return false;
    }

    public bool TryReadGauge<T>(out T gauge)
    {
        gauge = default!;
        // WHM mapping
        if (typeof(T) == typeof(Jobs.WHM.WhmGaugeSnapshot))
        {
            var g = Plugin.Gauges.Get<Dalamud.Game.ClientState.JobGauge.Types.WHMGauge>();
            if (g is null) return false;
            var snap = new Jobs.WHM.WhmGaugeSnapshot(g.Lily, g.BloodLily, g.LilyTimer);
            gauge = (T)(object)snap;
            return true;
        }
        // SGE mapping
        if (typeof(T) == typeof(Jobs.SGE.SgeGaugeSnapshot))
        {
            var g = Plugin.Gauges.Get<Dalamud.Game.ClientState.JobGauge.Types.SGEGauge>();
            if (g is null) return false;
            var snap = new Jobs.SGE.SgeGaugeSnapshot(g.Addersgall, g.Addersting, g.AddersgallTimer);
            gauge = (T)(object)snap;
            return true;
        }
        return false;
    }
}
