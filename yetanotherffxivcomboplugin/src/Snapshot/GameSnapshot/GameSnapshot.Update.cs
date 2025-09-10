using System.Runtime.CompilerServices;

namespace yetanotherffxivcomboplugin.src.Snapshot;

public sealed partial class GameSnapshot
{
    public void Update()
    {
        _frame++;

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
            var movedSq = dx * dx + dy * dy + dz * dz;
            if (movedSq > MoveEpsilonSq) _movingUntilFrame = _frame + MoveHoldFrames;
            PlayerMoving = _frame < _movingUntilFrame;
        }
        else { Player = default; _hot.PlayerPos = default; _hot.PlayerHpPct = 0; _hot.PlayerAlive = false; _hot.PlayerMp = 0; _hot.PlayerLevel = 0; _hot.PlayerJobId = 0; PlayerMoving = false; _movingUntilFrame = 0; }

        _game.GetTargets(out var hard, out var focus, out var soft);
        Target = hard != 0 ? ObjRef.FromId(hard) : null;
        FocusTarget = focus != 0 ? ObjRef.FromId(focus) : null;
        SoftTarget = soft != 0 ? ObjRef.FromId(soft) : null;

        var cf = _game.GetCombatFlags();
        _hot.Flags = new CombatFlags(cf.InCombat, cf.Mounted, cf.Casting);

        var ps = _game.GetPartySnapshot();
        var count = ps.Count; if (count > MaxParty) count = MaxParty;
        var oldCount = _partyCount; _partyCount = count;
        var idx = 0; _partyDeadCount = 0; _firstDeadPartyMember = 0UL;
        for (int k = 0; k < count; k++)
        {
            var pm = ps[k];
            _partyRefs[idx] = ObjRef.FromId(pm.Id);
            _partyHpPct[idx] = HpPct(pm.CurrentHp, pm.MaxHp);
            _partyAlive[idx] = pm.Alive;
            if (!pm.Alive)
            {
                if (_partyDeadCount < byte.MaxValue) _partyDeadCount++;
                if (_firstDeadPartyMember == 0UL && pm.Id != 0) _firstDeadPartyMember = pm.Id;
            }
            _partyPos[idx] = pm.Position;
            _partyPosKnown[idx] = pm.HasPosition;
            _partyNames[idx] = pm.Name ?? string.Empty;
            idx++; if (idx >= count) break;
        }
        if (oldCount > count)
        {
            for (int j = count; j < oldCount; j++)
            { _partyRefs[j] = default; _partyHpPct[j] = 0; _partyAlive[j] = false; _partyPos[j] = default; _partyPosKnown[j] = false; _partyNames[j] = string.Empty; }
        }

        ResetPerTickCaches();

        var n = _trackedCount;
        for (int i = 0; i < n; i++)
        {
            var id = _trackedRecasts[i];
            _trackedReady[i] = _game.IsActionReady(id);
        }
    }

    public ulong FrameCount => _frame;

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
        _justUsedActionId = 0;
    }

    public bool TryGetFirstDeadPartyMember(out ulong id)
    {
        id = _firstDeadPartyMember;
        return id != 0;
    }
}
