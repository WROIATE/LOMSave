using System;
using System.Collections.Generic;

namespace LOMSave.Probe
{
    public static class ActionAutoSaveTests
    {
        private static int _checks;

        private static void Check(bool result, string name)
        {
            _checks++;
            if (!result) throw new Exception(name);
        }

        public static void Run()
        {
            _checks = 0;
            // Actual rows from section_01_free_start_02, plus other native action types.
            Check(ActionAutoSavePolicy.OptionCost("default+O_Section_01_Free_Start_02_001+1+0", true, false) == 1, "lodging costs one action");
            Check(ActionAutoSavePolicy.OptionCost("move+O_Section_01_Free_Start_02_003+1+0", true, false) == 1, "Kongtong movement costs one action");
            Check(ActionAutoSavePolicy.OptionCost("shop+Any_New_Map_Row+2+5", true, false) == 2, "cost detection is independent of map IDs");
            Check(ActionAutoSavePolicy.OptionCost("event+Any_Event+1+0", true, false) == 1, "events can spend actions");
            Check(ActionAutoSavePolicy.OptionCost("talk+Any_Character+1+0", true, false) == 1, "talking can spend actions");
            Check(ActionAutoSavePolicy.OptionCost("exit+Any_Exit+0+0", true, false) == 0, "free exits do not overwrite action save");
            Check(ActionAutoSavePolicy.OptionCost("shop+Any_Shop+0+5", true, false) == 0, "contribution-only costs do not overwrite action save");
            Check(ActionAutoSavePolicy.OptionCost("move+Any_Move+1+0", false, false) == 0, "disabled choices are excluded");
            Check(ActionAutoSavePolicy.OptionCost("move+Any_Move+1+0", true, true) == 0, "hidden choices are excluded");
            foreach (var text in new[] { null, "", "~x", "普通剧情选项", "move+O_Row", "move+O_Row+x+0", "move+O_Row+-1+0", "move+O_Row+1+-1", "move+O_Row+1+x", "move+O_Row+1+0+extra", "move++1+0", "+O_Row+1+0", "move+O_Row+2147483648+0" })
                Check(ActionAutoSavePolicy.OptionCost(text, true, false) == 0, "malformed/ordinary option is ignored: " + text);

            var actions = 4;
            var choiceVisible = true;
            var recordedChoices = 0;
            var savedActions = -1;
            var savedVisible = false;
            var savedChoiceHistory = -1;
            var order = new List<string>();
            Action selected = () => { order.Add("select"); choiceVisible = false; recordedChoices++; actions--; };
            Action<int> save = cost => { order.Add("save"); savedActions = actions; savedVisible = choiceVisible; savedChoiceHistory = recordedChoices; };
            var restoring = false;
            var wrapped = ActionAutoSavePolicy.BeforeOption(1, selected, () => !restoring, save);
            Check(order.Count == 0 && savedActions == -1, "populating menu never saves or selects");
            wrapped();
            Check(order.Count == 2 && order[0] == "save" && order[1] == "select", "save occurs before native callback");
            Check(savedActions == 4 && actions == 3, "snapshot retains pre-action count");
            Check(savedVisible && !choiceVisible, "snapshot retains choice UI");
            Check(savedChoiceHistory == 0 && recordedChoices == 1, "selected choice is not part of saved replay history");

            order.Clear();
            restoring = true;
            wrapped();
            Check(order.Count == 1 && order[0] == "select", "restoring at click time suppresses save but preserves callback");
            restoring = false;
            order.Clear();
            wrapped();
            Check(order.Count == 2 && order[0] == "save", "saving resumes after restoration without rebuilding menu");

            var allow = false;
            order.Clear();
            ActionAutoSavePolicy.BeforeOption(1, selected, () => allow, save)();
            Check(order.Count == 1 && order[0] == "select", "missing or mismatched checkpoint does not save");
            Check(ReferenceEquals(ActionAutoSavePolicy.BeforeOption(0, selected, () => true, save), selected), "zero-cost callback is untouched");
            Check(ActionAutoSavePolicy.BeforeOption(1, null, () => true, save) == null, "missing callback is untouched");
            Console.WriteLine("Action automatic save checks passed: " + _checks);
        }
    }
}
