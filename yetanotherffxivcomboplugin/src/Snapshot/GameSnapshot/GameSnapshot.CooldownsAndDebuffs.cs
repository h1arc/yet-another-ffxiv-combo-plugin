using System;
using System.Runtime.CompilerServices;

namespace yetanotherffxivcomboplugin.src.Snapshot;

public sealed partial class GameSnapshot
{
    public ReadOnlySpan<int> TrackedCooldowns => _trackedRecasts.AsSpan(0, _trackedCount);

    public bool TryGetCooldownRemainingMs(int actionId, out int remainingMs)
        => _game.TryGetActionRemainingMs(actionId, out remainingMs);

    public bool IsActionReady(int actionId)
    {
        if (_justUsedActionId == actionId) return false;
        var n = _trackedCount;
        for (int i = 0; i < n; i++) if (_trackedRecasts[i] == actionId) return _trackedReady[i];
        return _game.IsActionReady(actionId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkActionUsed(int actionId) { _justUsedActionId = actionId; }

    public void ConfigureRecasts(ReadOnlySpan<int> actionIds)
    {
        var max = Math.Min(actionIds.Length, _trackedRecasts.Length);
        for (int i = 0; i < max; i++) _trackedRecasts[i] = actionIds[i];
        for (int i = max; i < _trackedRecasts.Length; i++) _trackedRecasts[i] = 0;
        _trackedCount = (byte)max;
    }

    public void ConfigureCooldowns(ReadOnlySpan<int> actionIds) => ConfigureRecasts(actionIds);

    public ReadOnlySpan<int> TrackedDebuffActions => _debuffActionIds.AsSpan(0, _debuffMapCount);

    public void ConfigureDebuffMapping(int actionId, ReadOnlySpan<ushort> statusIds)
    {
        if (actionId == 0) return;
        bool any = false; for (int i = 0; i < statusIds.Length; i++) if (statusIds[i] != 0) { any = true; break; }
        if (!any) return;
        var idx = _debuffMapCount; if (idx >= _debuffActionIds.Length) return;
        _debuffActionIds[idx] = actionId;
        _debuffStatusA[idx] = statusIds.Length > 0 ? statusIds[0] : (ushort)0;
        _debuffStatusB[idx] = statusIds.Length > 1 ? statusIds[1] : (ushort)0;
        _debuffStatusC[idx] = statusIds.Length > 2 ? statusIds[2] : (ushort)0;
        _debuffMapCount++;
    }

    public void ClearMappings()
    {
        for (int i = 0; i < _debuffActionIds.Length; i++) { _debuffActionIds[i] = 0; _debuffStatusA[i] = 0; _debuffStatusB[i] = 0; _debuffStatusC[i] = 0; }
        _debuffMapCount = 0;
        for (int i = 0; i < _trackedRecasts.Length; i++) { _trackedRecasts[i] = 0; _trackedReady[i] = false; }
        _trackedCount = 0;
    }

    public bool TryGetDebuffRemainingMsForActionOnTarget(int actionId, ulong targetId, out int remainingMs)
    {
        remainingMs = 0;
        if (targetId == 0) return false;
        ushort a = 0, b = 0, c = 0;
        var n = _debuffMapCount;
        for (int i = 0; i < n; i++)
        {
            if (_debuffActionIds[i] == actionId) { a = _debuffStatusA[i]; b = _debuffStatusB[i]; c = _debuffStatusC[i]; break; }
        }
        if (a == 0 && b == 0 && c == 0) return false;
        if (_game.TryGetMaxStatusRemaining(targetId, a, b, c, out var seconds))
        { remainingMs = (int)MathF.Round(MathF.Max(0f, seconds * 1000f)); return true; }
        return false;
    }

    public bool DoesDebuffNeedRefresh(int actionId, int thresholdMs)
        => DoesDebuffNeedRefresh(actionId, thresholdMs, ignoreTargetability: false);

    public bool DoesDebuffNeedRefresh(int actionId, int thresholdMs, bool ignoreTargetability)
    {
        if (!SimpleHasTarget()) return false;
        var targetId = Target!.Value.Id;
        ushort a = 0, b = 0, c = 0;
        var n = _debuffMapCount;
        for (int i = 0; i < n; i++)
        {
            if (_debuffActionIds[i] == actionId) { a = _debuffStatusA[i]; b = _debuffStatusB[i]; c = _debuffStatusC[i]; break; }
        }
        if (a == 0 && b == 0 && c == 0) return false;
        if (_game.TryGetMaxStatusRemaining(targetId, a, b, c, out var bestRem))
            return bestRem * 1000f <= thresholdMs;
        return true;
    }
}
