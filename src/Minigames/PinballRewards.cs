using System;

namespace LOMSave.Probe
{
    internal static class PinballRewards
    {
        // release_1.0.6000.0: Pinball_Score_001 tests >=100, >=60, >=30, then fallback.
        internal static readonly int[] Scores = { 0, 30, 60, 100 };
        internal static readonly string[] Labels = { "0 分 · 未达奖励门槛", "30 分 · 三奖", "60 分 · 二奖", "100 分 · 大奖" };
        internal static bool IsSupported(int score) { return Array.IndexOf(Scores, score) >= 0; }
    }
}
