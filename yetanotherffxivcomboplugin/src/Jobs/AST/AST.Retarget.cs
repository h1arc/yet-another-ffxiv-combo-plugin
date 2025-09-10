// namespace yetanotherffxivcomboplugin.Jobs.AST;

// /// <summary> Minimal set of AST action IDs we may retrace in pure backend mode. </summary>
// public static class ASTActions
// {
//     // Draw actions
//     public const int AstralDraw = 37017;
//     public const int UmbralDraw = 37018;

//     // Card Play actions (legacy Play1/2/3) and direct card actions (DT+)
//     public const int Play1 = 37019;
//     public const int Play2 = 37020;
//     public const int Play3 = 37021;

//     // Direct card actions (handled internally by backend; IDs: 37023..37028)

//     // Healing actions
//     public const int Benefic = 3594;                 // Benefic
//     public const int BeneficII = 3610;               // Benefic II
//     public const int AspectedBenefic = 3595;         // Aspected Benefic
//     public const int EssentialDignity = 3614;        // Essential Dignity
//     public const int Synastry = 3612;                // Synastry
//     public const int CelestialIntersection = 16556;  // Celestial Intersection
//     public const int Exaltation = 25873;             // Exaltation

//     // Note: Buff scanning removed; we rely on Draw polarity and action IDs only.

//     static ASTActions()
//     {
//         // Note: RetraceableActions integration now handled by JobRegistry
//     }

//     public static bool IsRetraceable(int actionId) =>
//         System.Array.IndexOf(GetRetraceableIds(), actionId) >= 0;

//     private static int[] GetRetraceableIds() =>
//         [
//             Benefic, BeneficII, AspectedBenefic, EssentialDignity, Synastry, CelestialIntersection, Exaltation
//         ];

//     public static bool IsCardAction(int actionId)
//         => actionId == Play1 || actionId == Play2 || actionId == Play3;

//     public static bool IsDrawAction(int actionId)
//         => actionId == AstralDraw || actionId == UmbralDraw;
// }
