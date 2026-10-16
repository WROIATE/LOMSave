using System;

namespace LOMSave.Probe
{
    internal static class BattleRewardPolicy
    {
        internal static int MoneyBudget(Random random, int cap)
        { return random.Next(Math.Max(1, cap / 4), cap + 1); }

        internal static int Performance(Random random, int earned, int maximum)
        {
            if (maximum <= 0) return earned;
            // A simulated victory reaches one of the game's three reward tiers.
            // Never replace better performance already earned by the player.
            var tier = random.Next(1, 4);
            var minimum = Math.Min(tier * 100, maximum);
            var simulated = random.Next(minimum, Math.Min(tier * 100 + 99, maximum) + 1);
            return Math.Max(earned, simulated);
        }
    }
}
