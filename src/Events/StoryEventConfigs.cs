using Mortal.Story;

namespace LOMSave.Probe
{
    // Retain native references when Story unloads; do not load a scene or
    // enumerate resources to obtain the Story-only configuration assets.
    internal static class StoryEventConfigs
    {
        internal static PositionResultConfig Position { get; private set; }
        internal static ConditionResultConfig Condition { get; private set; }
        internal static SwitchResultConfig[] Switches { get; private set; } = new SwitchResultConfig[0];
        internal static int Revision { get; private set; }
        private static CheckPointManager _owner;

        internal static void Bind(CheckPointManager manager)
        {
            if (manager == null || ReferenceEquals(manager, _owner)) return;
            _owner = manager;
            Position = EventInspector.Read<PositionResultConfig>(manager, "_position");
            Condition = EventInspector.Read<ConditionResultConfig>(manager, "_condition");
            Switches = EventInspector.Read<SwitchResultConfig[]>(manager, "_switchList") ?? new SwitchResultConfig[0];
            Revision++;
        }
    }
}
