// using System.Runtime.CompilerServices;
// using yetanotherffxivcomboplugin.Snapshot;

// namespace yetanotherffxivcomboplugin.Jobs.AST;

// /// <summary>
// /// AST-specific targeting logic (cards). Kept lean; engine provides cache/buckets.
// /// </summary>
// public static partial class ASTTargeting
// {
//     private enum DrawnCard : byte { None = 0, Balance, Spear, Arrow, Bole, Spire, Ewer }

//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     private static DrawnCard MapDirectCard(int actionId)
//     {
//         return actionId switch
//         {
//             37023 => DrawnCard.Balance,
//             37024 => DrawnCard.Arrow,
//             37025 => DrawnCard.Spire,
//             37026 => DrawnCard.Spear,
//             37027 => DrawnCard.Bole,
//             37028 => DrawnCard.Ewer,
//             _ => DrawnCard.None,
//         };
//     }

//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public static bool IsAstCardActionOrDirect(int actionId)
//         => ASTActions.IsCardAction(actionId) || (actionId >= 37023 && actionId <= 37028);

//     /// <summary>
//     /// Resolve an AST card target based on action and cache buckets. Returns 0 to keep current.
//     /// </summary>
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public static ulong TryResolveTarget(int actionId, ulong currentTargetId, GameSnapshot cache, byte lastDrawPolarity)
//     {
//         if (ASTActions.IsDrawAction(actionId)) return 0; // Draw has no retrace

//         var buckets = cache.Buckets;
//         var self = buckets.Self;

//         var direct = MapDirectCard(actionId);
//         if (direct != DrawnCard.None)
//         {
//             // Direct card policies
//             switch (direct)
//             {
//                 case DrawnCard.Balance:
//                     if (buckets.Melee != 0) return buckets.Melee;
//                     if (buckets.FirstTank != 0) return buckets.FirstTank;
//                     if (buckets.AnyDps != 0) return buckets.AnyDps;
//                     return self;
//                 case DrawnCard.Spear:
//                     if (buckets.Ranged != 0) return buckets.Ranged;
//                     return self;
//                 case DrawnCard.Arrow:
//                 case DrawnCard.Bole:
//                     if (buckets.FirstTank != 0) return buckets.FirstTank;
//                     return self;
//                 case DrawnCard.Spire:
//                 case DrawnCard.Ewer:
//                     if (buckets.LowestHpAlly != 0) return buckets.LowestHpAlly;
//                     if (buckets.FirstTank != 0) return buckets.FirstTank;
//                     return self;
//                 default:
//                     return 0;
//             }
//         }

//         // Play1/2/3: map via polarity else union fallback
//         if (ASTActions.IsCardAction(actionId))
//         {
//             if (lastDrawPolarity == 1) // Astral
//             {
//                 if (actionId == ASTActions.Play1) return buckets.Melee != 0 ? buckets.Melee : (buckets.FirstTank != 0 ? buckets.FirstTank : (buckets.AnyDps != 0 ? buckets.AnyDps : self)); // Balance policy
//                 if (actionId == ASTActions.Play3) return buckets.FirstTank != 0 ? buckets.FirstTank : self; // Arrow
//                 if (actionId == ASTActions.Play2) // Spire
//                 {
//                     if (buckets.LowestHpAlly != 0) return buckets.LowestHpAlly;
//                     if (buckets.FirstTank != 0) return buckets.FirstTank;
//                     return self;
//                 }
//             }
//             else if (lastDrawPolarity == 2) // Umbral
//             {
//                 if (actionId == ASTActions.Play1) return buckets.Ranged != 0 ? buckets.Ranged : self; // Spear
//                 if (actionId == ASTActions.Play3) return buckets.FirstTank != 0 ? buckets.FirstTank : self; // Bole
//                 if (actionId == ASTActions.Play2) // Ewer
//                 {
//                     if (buckets.LowestHpAlly != 0) return buckets.LowestHpAlly;
//                     if (buckets.FirstTank != 0) return buckets.FirstTank;
//                     return self;
//                 }
//             }

//             // Unknown polarity -> slot union fallback
//             if (actionId == ASTActions.Play1)
//             {
//                 if (buckets.Melee != 0) return buckets.Melee;
//                 if (buckets.Ranged != 0) return buckets.Ranged;
//                 if (buckets.FirstTank != 0) return buckets.FirstTank;
//                 return self;
//             }
//             if (actionId == ASTActions.Play3)
//             {
//                 if (buckets.FirstTank != 0) return buckets.FirstTank;
//                 return self;
//             }
//             if (actionId == ASTActions.Play2)
//             {
//                 if (buckets.LowestHpAlly != 0) return buckets.LowestHpAlly;
//                 if (buckets.FirstTank != 0) return buckets.FirstTank;
//                 return self;
//             }
//         }

//         return 0;
//     }
// }
