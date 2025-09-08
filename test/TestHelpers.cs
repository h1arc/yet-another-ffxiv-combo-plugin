using System.Runtime.CompilerServices;

namespace yetanotherffxivcomboplugin.Core;

/// <summary>
/// Pure, test-friendly helpers that mirror core decision policies without touching Dalamud.
/// </summary>
public static class TestHelpers
{
    public enum Card { None = 0, Balance, Spear, Arrow, Bole, Spire, Ewer }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DecideCardTarget(Card card, ulong self, ulong firstTank, ulong melee, ulong ranged, ulong anyDps, ulong lowestHpAlly, byte lowestHpPct)
    {
        switch (card)
        {
            case Card.Balance:
                if (melee != 0) return melee;
                if (firstTank != 0) return firstTank;
                if (anyDps != 0) return anyDps;
                return self;
            case Card.Spear:
                if (ranged != 0) return ranged;
                return self;
            case Card.Arrow:
            case Card.Bole:
                if (firstTank != 0) return firstTank;
                return self;
            case Card.Spire:
            case Card.Ewer:
                if (lowestHpAlly != 0) return lowestHpAlly;
                if (firstTank != 0) return firstTank;
                return self;
            default:
                return 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PickFromCandidatesOrSelf(ulong a, ulong b, ulong c, ulong d, ulong self)
    {
        if (a != 0) return a;
        if (b != 0) return b;
        if (c != 0) return c;
        if (d != 0) return d;
        return self;
    }
}
