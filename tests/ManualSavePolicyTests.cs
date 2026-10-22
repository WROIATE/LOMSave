using System;
namespace LOMSave.Probe
{
    public static class ManualSavePolicyTests
    {
        private static int _checks;
        private static void Check(bool value, string text) { _checks++; if (!value) throw new Exception(text); }
        public static string Run()
        {
            Check(ManualSavePolicy.Story("say", true, true, false, false, false) == ManualSaveBlock.None,
                "Completed text waiting on input is saveable, independently of Fungus IsWriting");
            Check(ManualSavePolicy.Story("say", true, true, true, false, false) == ManualSaveBlock.TextWriting,
                "A mid-text input pause is not a completed line");
            Check(ManualSavePolicy.Story("say", true, false, false, false, false) == ManualSaveBlock.TextWriting,
                "Completed automatic text still needs a stable waiting point");
            Check(ManualSavePolicy.Story("choose", true, false, true, true, false) == ManualSaveBlock.None,
                "Ready ordinary choices do not depend on a stale hidden dialogue writer");
            Check(ManualSavePolicy.Story("choose", true, true, false, false, false) == ManualSaveBlock.ChoiceNotReady,
                "Preparing/hidden choices cannot borrow a completed dialogue");
            foreach(var kind in new[]{"say","choose"})
            {
                Check(ManualSavePolicy.Story(kind, true, true, false, true, true) == ManualSaveBlock.Presentation,
                    "Moving portraits/camera block both completed text and visible choices");
                Check(ManualSavePolicy.Story(kind, false, true, false, true, false) == ManualSaveBlock.CheckpointMissing,
                    "Visible stable UI without matching continuation must remain disabled");
            }
            foreach(var kind in new[]{"dice","choosetimer"})
            {
                Check(ManualSavePolicy.Story(kind, true, false, true, true, false) == ManualSaveBlock.None,
                    "Modern and legacy dice waiting to start are stable branch points");
                Check(ManualSavePolicy.Story(kind, true, true, false, false, false) == ManualSaveBlock.Dice,
                    "Spinning/settled dice cannot borrow a stale completed dialogue");
                Check(ManualSavePolicy.Story(kind, true, true, false, true, true) == ManualSaveBlock.Presentation,
                    "Waiting dice still require presentation to finish");
            }
            for(var mask=0;mask<64;mask++)
                Check(ManualSavePolicy.DiceReady((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0,(mask&16)!=0,(mask&32)!=0)
                    == (mask==7), "Only visible/enabled and unpressed start without a result is safe: " + mask);
            Check(ManualSavePolicy.Story("shop", true, true, false, true, false) == ManualSaveBlock.CheckpointMissing,
                "An unsupported modal cannot impersonate a stable dialogue/choice");
            foreach (ManualSaveBlock phase in Enum.GetValues(typeof(ManualSaveBlock)))
            {
                Check(ManualSavePolicy.Evaluate("Story", false, false, false, true, phase) == ManualSaveBlock.None,
                    "Native allowed story menu is not limited by dialogue/choice state: " + phase);
                Check(ManualSavePolicy.Evaluate("Story", false, false, false, false, phase) == phase,
                    "Extra story save and UI preserve detailed block reason: " + phase);
                Check(ManualSavePolicy.Evaluate("Story", true, false, false, true, phase) == ManualSaveBlock.Loading,
                    "Restoration cannot be saved despite a previously allowed native menu");
                Check(ManualSavePolicy.Evaluate("Free", false, false, false, true, phase) == ManualSaveBlock.None,
                    "Cultivation idle has no dialogue requirement");
                Check(ManualSavePolicy.Evaluate("Free", false, false, true, true, phase) == ManualSaveBlock.Processing,
                    "Action execution is not a cultivation idle save");
                Check(ManualSavePolicy.Evaluate("Free", false, true, false, true, phase) == ManualSaveBlock.Loading,
                    "Scene transition cannot inherit cultivation permission");
                Check((phase == ManualSaveBlock.None) == (ManualSavePolicy.Hint(phase).Length == 0),
                    "Disabled controls always have an inline reason; enabled controls have no warning");
            }
            foreach (var scene in new[] { "Battle", "Combat", "Pinball", "Loading1", "GameOver", "End", "DemoEnd", "Title", "CoreScenes", "", null })
                Check(ManualSavePolicy.Evaluate(scene, false, false, false, true, ManualSaveBlock.None) == ManualSaveBlock.UnsupportedScene,
                    "Unknown/unsupported scenes cannot borrow a stable dialogue or native permission");
            return _checks + " manual-save availability assertions passed";
        }
    }
}
