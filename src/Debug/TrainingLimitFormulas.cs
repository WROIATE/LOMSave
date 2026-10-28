using System;
using System.Collections.Generic;
using Mortal.Core;
using UnityEngine;

namespace LOMSave.Probe
{
    // Used only until the original training scene has loaded its converters.
    // The shared index has already verified the game and all source hashes.
    internal static class TrainingLimitFormulas
    {
        private sealed class Part { internal GameStat Stat; internal AnimationCurve Curve; }
        private sealed class Formula { internal float Rate; internal Part[] Parts; }
        private static readonly Dictionary<TrainingLimitKind, Formula> Cache = new Dictionary<TrainingLimitKind, Formula>();
        private static StoryEventIndexData _index;
        private static PlayerStatData _stats;

        internal static bool TryGet(TrainingLimitKind kind, out int value)
        {
            value = 0;
            var index = StoryEventIndex.Data;
            var player = PlayerStatManagerData.Instance;
            if (index == null || index.TrainingLimits == null || player == null || player.Stats == null) return false;
            if (!ReferenceEquals(_index, index) || !ReferenceEquals(_stats, player.Stats))
            { Cache.Clear(); _index = index; _stats = player.Stats; }
            Formula formula;
            if (!Cache.TryGetValue(kind, out formula))
            {
                TrainingFormulaData source;
                if (!index.TrainingLimits.TryGetValue(kind.ToString(), out source) || source == null || source.Curves == null || source.Curves.Length == 0) return false;
                var parts = new List<Part>();
                foreach (var part in source.Curves)
                {
                    if (part == null || part.Keys == null || part.Keys.Length == 0) return false;
                    var stat = player.Stats.Get((GameStatType)part.Stat);
                    if (stat == null) return false;
                    var keys = new Keyframe[part.Keys.Length];
                    for (var i = 0; i < keys.Length; i++)
                    {
                        var key = part.Keys[i];
                        if (key == null || key.Length != 7) return false;
                        keys[i] = new Keyframe(key[0], key[1], key[2], key[3], key[5], key[6]) { weightedMode = (WeightedMode)(int)key[4] };
                    }
                    parts.Add(new Part { Stat = stat, Curve = new AnimationCurve(keys) { preWrapMode = (WrapMode)part.PreWrap, postWrapMode = (WrapMode)part.PostWrap } });
                }
                formula = new Formula { Rate = source.Rate, Parts = parts.ToArray() };
                Cache[kind] = formula;
            }
            // Same operations/order as GameStatConvertData + collection GetSummary.
            // Cache only the curve and identity; stat value/range stay live.
            float sum = 0;
            foreach (var part in formula.Parts)
                sum += part.Curve.Evaluate(Mathf.InverseLerp(part.Stat.Min, part.Stat.Max, part.Stat.FinalValue));
            value = Mathf.RoundToInt(sum * formula.Rate);
            return true;
        }
    }
}
