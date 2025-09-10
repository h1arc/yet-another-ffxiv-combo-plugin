using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace yetanotherffxivcomboplugin.src.Snapshot;

public sealed partial class GameSnapshot
{
    // Target presence checks
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SimpleHasTarget() => Target.HasValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AdvancedHasTarget() => SimpleHasTarget();

    /// <summary>Try to get the world position of the current hard target.</summary>
    public bool TryGetTargetPosition(out Vector3 position)
    {
        position = default;
        if (!SimpleHasTarget()) return false;
        var tid = Target!.Value.Id;
        foreach (var ch in _game.EnumerateCharacters())
        {
            if (ch.Id == tid) { position = ch.Position; return true; }
        }
        return false;
    }

    /// <summary>Squared distance to target center (yalms^2).</summary>
    public bool TryGetTargetDistanceSq(out float distanceSq)
    {
        distanceSq = 0f;
        if (!TryGetTargetPosition(out var tpos)) return false;
        var p = PlayerPos;
        var dx = tpos.X - p.X; var dy = tpos.Y - p.Y; var dz = tpos.Z - p.Z;
        distanceSq = dx * dx + dy * dy + dz * dz;
        return true;
    }

    /// <summary>Distance to target center (yalms).</summary>
    public bool TryGetTargetDistance(out float distance)
    {
        distance = 0f;
        if (!TryGetTargetDistanceSq(out var d2)) return false;
        distance = MathF.Sqrt(d2);
        return true;
    }

    /// <summary>Center-to-center range check.</summary>
    public bool IsTargetWithin(float yalms)
    {
        if (yalms <= 0f) return false;
        if (!TryGetTargetDistanceSq(out var d2)) return false;
        var r2 = yalms * yalms;
        return d2 <= r2;
    }

    /// <summary>Edge-to-edge range check (accounts for hitbox radii).</summary>
    public bool IsTargetEdgeWithin(float yalms, float epsilon = 0.01f)
    {
        if (yalms <= 0f) return false;
        if (!TryGetTargetDistance(out var center)) return false;
        _game.TryGetHitboxRadius(Player.Id, out float playerR);
        float targetR = 0f;
        if (Target.HasValue) _game.TryGetHitboxRadius(Target.Value.Id, out targetR);
        var edge = center - (playerR + targetR);
        return edge <= (yalms + MathF.Max(0f, epsilon));
    }
}
