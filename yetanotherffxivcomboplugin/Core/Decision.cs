using System.Numerics;
using System.Runtime.CompilerServices;

namespace yetanotherffxivcomboplugin.Core;

public enum DecisionKind : byte
{
    None = 0,
    TargetOverride = 1,
    ActionOverride = 2,
    ActionAndTargetOverride = 3,
    GroundTarget = 4,
}

public readonly struct Decision
{
    public readonly DecisionKind Kind;
    public readonly int ActionId;      // 0 means keep original
    public readonly ulong TargetId;    // 0 means keep current
    public readonly Vector3 Ground;    // valid when Kind == GroundTarget

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Decision(DecisionKind kind, int actionId, ulong targetId, Vector3 ground)
    { Kind = kind; ActionId = actionId; TargetId = targetId; Ground = ground; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Decision None() => new(DecisionKind.None, 0, 0, default);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Decision Target(ulong targetId) => new(DecisionKind.TargetOverride, 0, targetId, default);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Decision Action(int actionId) => new(DecisionKind.ActionOverride, actionId, 0, default);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Decision ActionAndTarget(int actionId, ulong targetId) => new(DecisionKind.ActionAndTargetOverride, actionId, targetId, default);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Decision GroundAt(Vector3 pos) => new(DecisionKind.GroundTarget, 0, 0, pos);
}
