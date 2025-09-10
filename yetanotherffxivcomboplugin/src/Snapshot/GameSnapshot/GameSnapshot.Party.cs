using System.Runtime.CompilerServices;

namespace yetanotherffxivcomboplugin.src.Snapshot;

public sealed partial class GameSnapshot
{
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
        for (int i = 0; i < _partyCount; i++) if (_partyRefs[i].IsValid && _partyRefs[i].Id == id) return true;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsPartyMember(Dalamud.Game.ClientState.Objects.Types.ICharacter ch)
    {
        if (ch == null) return false;
        var id = ch.GameObjectId;
        if (IsPartyMember(id)) return true;
        var name = ch.Name?.TextValue;
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < _partyCount; i++) if (!string.IsNullOrEmpty(_partyNames[i]) && _partyNames[i] == name) return true;
        return false;
    }

    public struct TopTwo
    {
        public ulong A;
        public ulong B;
        public byte KeyA;
        public byte KeyB;
        public byte Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() { A = 0; B = 0; KeyA = 255; KeyB = 255; Count = 0; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Offer(ulong id, byte key)
        {
            if (id == 0) return;
            if (Count == 0) { A = id; KeyA = key; Count = 1; return; }
            if (Count == 1)
            {
                if (key < KeyA) { B = A; KeyB = KeyA; A = id; KeyA = key; }
                else { B = id; KeyB = key; }
                Count = 2; return;
            }
            if (key < KeyA) { B = A; KeyB = KeyA; A = id; KeyA = key; }
            else if (key < KeyB) { B = id; KeyB = key; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool TryGetFirst(out ulong id) { id = A; return Count > 0 && id != 0; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool TryGetSecond(out ulong id) { id = B; return Count > 1 && id != 0; }
    }

    public TopTwo GetTopHealablePair(int actionId, bool includeSelf = false)
    {
        if (_healablePairFrame == _frame) return _healablePair;
        var pair = new TopTwo(); pair.Reset();
        var selfId = Player.Id;
        for (int i = 0; i < _partyCount; i++)
        {
            var id = _partyRefs[i].Id;
            if (id == 0) continue;
            if (!includeSelf && id == selfId) continue;
            var hp = _partyHpPct[i];
            if (hp == 0 || hp >= 100) continue;
            pair.Offer(id, hp);
        }
        if (includeSelf && selfId != 0)
        {
            var hp = PlayerHpPct;
            if (hp > 0 && hp < 100) pair.Offer(selfId, hp);
        }
        _healablePair = pair; _healablePairFrame = _frame; return pair;
    }

    public TopTwo GetTopCleansablePair(int actionId, bool includeSelf = false)
    {
        if (_cleansablePairFrame == _frame) return _cleansablePair;
        var pair = new TopTwo(); pair.Reset();
        var selfId = Player.Id;
        for (int i = 0; i < _partyCount; i++)
        {
            var id = _partyRefs[i].Id;
            if (id == 0) continue;
            if (!includeSelf && id == selfId) continue;
            if (!HasCleansableOnId(id)) continue;
            var hp = _partyHpPct[i];
            if (hp == 0) continue;
            pair.Offer(id, hp);
        }
        if (includeSelf && selfId != 0 && HasCleansableOnId(selfId))
        {
            var hp = PlayerHpPct;
            if (hp > 0) pair.Offer(selfId, hp);
        }
        _cleansablePair = pair; _cleansablePairFrame = _frame; return pair;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasCleansableOnId(ulong targetId)
    {
        if (targetId == 0) return false;
        return IsCleansableCached(targetId);
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
}
