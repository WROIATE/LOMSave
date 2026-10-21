using System;

namespace LOMSave.Probe
{
    internal enum TrainingLimitKind { Martial, Alchemy, Forge }

    internal sealed class TrainingLimitSettings
    {
        public int? Martial, Alchemy, Forge;
        internal const int Maximum = 9999;
        internal int? Get(TrainingLimitKind kind)
        {
            var value = kind == TrainingLimitKind.Martial ? Martial : kind == TrainingLimitKind.Alchemy ? Alchemy : Forge;
            return value >= 1 && value <= Maximum ? value : null;
        }
        internal void Set(TrainingLimitKind kind, int? value)
        {
            if (value.HasValue && (value < 1 || value > Maximum)) throw new ArgumentOutOfRangeException(nameof(value));
            if (kind == TrainingLimitKind.Martial) Martial = value;
            else if (kind == TrainingLimitKind.Alchemy) Alchemy = value;
            else Forge = value;
        }
        internal float Resolve(TrainingLimitKind kind, float native) { return Get(kind) ?? native; }
        internal int Adjusted(TrainingLimitKind kind, int native, int delta)
        { return (int)Math.Max(1, Math.Min(Maximum, (long)(Get(kind) ?? native) + delta)); }
    }
}
