using yetanotherffxivcomboplugin.src.Core;
using yetanotherffxivcomboplugin.src.Snapshot;

static void AssertEq<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        Console.WriteLine($"FAIL: {name} expected={expected} actual={actual}");
        Environment.ExitCode = 1;
    }
    else
    {
        Console.WriteLine($"PASS: {name}");
    }
}

// Test 1: Heal retrace policy ordering via ActionResolver.BuildCandidates-like flow
{
    // We simulate the candidate picking by feeding four ids in order soft>hard>ot>self and expect first non-zero returned
    var self = 0x11UL;
    var soft = 0x22UL;
    var hard = 0x33UL;
    var ot = 0x44UL;
    var picked = TestHelpers.PickFromCandidatesOrSelf(soft, hard, ot, 0, self);
    AssertEq(soft, picked, "heal_policy_prefers_soft");

    picked = TestHelpers.PickFromCandidatesOrSelf(0, hard, ot, 0, self);
    AssertEq(hard, picked, "heal_policy_prefers_hard_when_no_soft");

    picked = TestHelpers.PickFromCandidatesOrSelf(0, 0, ot, 0, self);
    AssertEq(ot, picked, "heal_policy_prefers_ot_when_no_soft_hard");

    picked = TestHelpers.PickFromCandidatesOrSelf(0, 0, 0, 0, self);
    AssertEq(self, picked, "heal_policy_falls_back_to_self");
}

// Test 2: AST card target selection
{
    ulong self = 0xAA;
    ulong tank = 0xBB;
    ulong melee = 0xCC;
    ulong ranged = 0xDD;
    ulong anyDps = 0xEE;
    ulong low = 0xFF;

    // Balance: melee > tank > anyDps > self
    var t = TestHelpers.DecideCardTarget(TestHelpers.Card.Balance, self, tank, melee, ranged, anyDps, 0, 0);
    AssertEq(melee, t, "card_balance_melee");
    t = TestHelpers.DecideCardTarget(TestHelpers.Card.Balance, self, tank, 0, ranged, anyDps, 0, 0);
    AssertEq(tank, t, "card_balance_tank_when_no_melee");

    // Spear: ranged > self
    t = TestHelpers.DecideCardTarget(TestHelpers.Card.Spear, self, tank, melee, ranged, anyDps, 0, 0);
    AssertEq(ranged, t, "card_spear_ranged");

    // Arrow/Bole: tank > self
    t = TestHelpers.DecideCardTarget(TestHelpers.Card.Arrow, self, tank, 0, 0, 0, 0, 0);
    AssertEq(tank, t, "card_arrow_tank");

    // Spire/Ewer: lowestHp > tank > self
    t = TestHelpers.DecideCardTarget(TestHelpers.Card.Spire, self, tank, 0, 0, 0, low, 30);
    AssertEq(low, t, "card_spire_lowestHp");
}

Console.WriteLine("Done.");
