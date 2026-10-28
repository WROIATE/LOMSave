using System;

namespace LOMSave.Probe
{
    internal enum TrainingLimitKind { Martial, Alchemy, Forge }

    internal sealed class TrainingLimitSettings
    {
        public int? Martial, Alchemy, Forge;
        public int Version;
        public bool MartialUnlimited, AlchemyUnlimited, ForgeUnlimited;
        // Old numeric caps do not silently turn into unlimited training.
        internal void Migrate()
        { if (Version < 2) { MartialUnlimited = AlchemyUnlimited = ForgeUnlimited = false; Version = 2; } Martial = Alchemy = Forge = null; }
        internal bool Get(TrainingLimitKind kind)
        { return kind == TrainingLimitKind.Martial ? MartialUnlimited : kind == TrainingLimitKind.Alchemy ? AlchemyUnlimited : ForgeUnlimited; }
        internal void Set(TrainingLimitKind kind, bool value)
        {
            Version = 2;
            if (kind == TrainingLimitKind.Martial) MartialUnlimited = value;
            else if (kind == TrainingLimitKind.Alchemy) AlchemyUnlimited = value;
            else ForgeUnlimited = value;
        }
        internal int Remaining(TrainingLimitKind kind, int nativeRemaining, int availablePoints)
        { return Math.Max(0, Get(kind) ? availablePoints : nativeRemaining); }
    }
}
