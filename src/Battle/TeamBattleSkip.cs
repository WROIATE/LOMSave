using HarmonyLib;
using Mortal.Battle;
using Mortal.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class TeamBattleSkip
    {
        private const string ButtonLabel = "直接胜利";
        private const string ButtonName = "LOMSaveTeamBattleWinButton";
        private const float DefaultButtonStep = -70f;
        private const int SimulatedMoneyCap = 2500;
        // Game saves restore Unity's RNG. Keep convenience-reward variation
        // independent, without changing the random stream used by story events.
        private static readonly System.Random RewardRandom = new System.Random(Guid.NewGuid().GetHashCode());
        private static GameLevelManager _settledManager;

        [HarmonyPostfix, HarmonyPatch(typeof(ReadyPanel), "Setup")]
        private static void ConfigureReadyButton(ReadyPanel __instance, Button ____startButton)
        {
            if (____startButton == null) return;
            var parent = ____startButton.transform.parent;
            var existing = parent.Find(ButtonName);
            var button = existing == null
                ? UnityEngine.Object.Instantiate(____startButton, parent, false)
                : existing.GetComponent<Button>();
            button.name = ButtonName;
            var position = ____startButton.transform.localPosition;
            position.y -= ((RectTransform)____startButton.transform).rect.height + 24;
            button.transform.localPosition = position;
            SetButtonLabel(button);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() =>
            {
                var manager = GameLevelManager.Instance;
                if (manager == null || manager.IsGameOver || !button.interactable) return;
                button.interactable = false;
                ____startButton.onClick.Invoke();
                manager.StartCoroutine(FinishFromReady(manager));
            });
            button.interactable = true;
            button.gameObject.SetActive(true);
        }

        private static IEnumerator FinishFromReady(GameLevelManager manager)
        {
            var deadline = Time.realtimeSinceStartup + 15f;
            while (manager != null && !manager.IsReady && !manager.IsGameOver && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (manager == null || !manager.IsReady || manager.IsGameOver) yield break;
            HandleDirectVictory();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PausePanel), "Setup", typeof(bool), typeof(bool))]
        private static void ConfigureButton(PausePanel __instance)
        {
            Button displayButton = null;
            try
            {
                var sourceWinButton = Traverse.Create(__instance)
                    .Field("_testWinButton")
                    .GetValue<Button>();
                var giveupButton = Traverse.Create(__instance)
                    .Field("_giveupButton")
                    .GetValue<Button>();
                var titleButton = Traverse.Create(__instance)
                    .Field("_titleButton")
                    .GetValue<Button>();
                var manager = GameLevelManager.Instance;
                displayButton = GetOrCreateButton(
                    giveupButton,
                    titleButton,
                    out var created);
                SetButtonAction(displayButton);
                var visible = displayButton != null
                    && manager != null;

                if (sourceWinButton != null)
                {
                    sourceWinButton.gameObject.SetActive(false);
                }
                if (displayButton != null)
                {
                    RegisterButton(__instance, displayButton);
                    displayButton.interactable = giveupButton != null && giveupButton.interactable
                        && manager != null && !manager.IsGameOver;
                    displayButton.gameObject.SetActive(visible);
                }

                string reason = null;
                if (giveupButton == null)
                {
                    reason = "button_template_missing";
                }
                else if (manager == null)
                {
                    reason = "battle_manager_missing";
                }
                else if (displayButton == null)
                {
                    reason = "button_creation_failed";
                }

                LOMSaveProbePlugin.WriteEvent(
                    "team_battle_skip_button_configured",
                    new
                    {
                        visible,
                        interactable = displayButton != null && displayButton.interactable,
                        created,
                        reason
                    });
            }
            catch (Exception exception)
            {
                if (displayButton != null)
                {
                    displayButton.gameObject.SetActive(false);
                }
                LOMSaveProbePlugin.WriteError(
                    "team_battle_skip_button_configure_failed",
                    exception);
            }
        }

        private static Button GetOrCreateButton(
            Button giveupButton,
            Button titleButton,
            out bool created)
        {
            created = false;
            if (giveupButton == null
                || giveupButton.transform.parent == null)
            {
                return null;
            }

            var parent = giveupButton.transform.parent;
            var existing = parent.Find(ButtonName);
            if (existing != null)
            {
                var existingButton = existing.GetComponent<Button>();
                if (existingButton != null)
                {
                    SetButtonLabel(existingButton);
                }
                return existingButton;
            }

            var buttonObject = UnityEngine.Object.Instantiate(
                giveupButton.gameObject,
                parent,
                false);
            buttonObject.name = ButtonName;
            buttonObject.transform.SetSiblingIndex(
                giveupButton.transform.GetSiblingIndex() + 1);

            var position = giveupButton.transform.localPosition;
            var buttonStep = titleButton == null
                ? DefaultButtonStep
                : giveupButton.transform.localPosition.y
                    - parent.InverseTransformPoint(
                        titleButton.transform.position).y;
            if (Mathf.Abs(buttonStep) < 1f)
            {
                buttonStep = DefaultButtonStep;
            }
            position.y += buttonStep;
            buttonObject.transform.localPosition = position;

            var button = buttonObject.GetComponent<Button>();
            if (button == null)
            {
                UnityEngine.Object.Destroy(buttonObject);
                return null;
            }

            SetButtonLabel(button);
            created = true;
            return button;
        }

        private static void RegisterButton(PausePanel panel, Button button)
        {
            var field = Traverse.Create(panel).Field("_interactableButtons");
            var buttons = field.GetValue<Button[]>() ?? new Button[0];
            if (Array.IndexOf(buttons, button) >= 0) return;
            Array.Resize(ref buttons, buttons.Length + 1);
            buttons[buttons.Length - 1] = button;
            field.SetValue(buttons);
        }

        private static void SetButtonAction(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(HandleDirectVictory);
        }

        private static void HandleDirectVictory()
        {
            var manager = GameLevelManager.Instance;
            LOMSaveProbePlugin.WriteEvent("team_battle_skip_requested", new
            { managerPresent = manager != null, gameOver = manager != null && manager.IsGameOver,
                alreadySettled = manager != null && manager == _settledManager });
            if (manager == null || manager.IsGameOver || manager == _settledManager) return;
            _settledManager = manager;
            if (manager != null && !manager.IsGameOver)
            {
                try { SimulatePerformance(manager); }
                catch (Exception exception) { LOMSaveProbePlugin.WriteError("team_battle_skip_performance_failed", exception); }
                try
                {
                    var money = SimulateMoneyPickup(
                        manager,
                        out var enemyCount,
                        out var eligibleEnemyCount,
                        out var moneyDropCount,
                        out var pickupCount,
                        out var moneyPerPickup);
                    if (money > 0)
                    {
                        manager.AddPickMoney(money);
                    }

                    LOMSaveProbePlugin.WriteEvent(
                        "team_battle_skip_money_simulated",
                        new
                        {
                            enemyCount,
                            eligibleEnemyCount,
                            moneyDropCount,
                            pickupCount,
                            moneyPerPickup,
                            money,
                            cap = SimulatedMoneyCap
                        });
                }
                catch (Exception exception)
                {
                    LOMSaveProbePlugin.WriteError(
                        "team_battle_skip_money_simulation_failed",
                        exception);
                }
            }

            // Call the native settlement directly. Its hidden debug button can
            // be inactive/unwired, and pause must be lifted for the result UI.
            manager.ContinueGame();
            manager.ShowGameOver(GameOverType.FriendWin, true);
            LOMSaveProbePlugin.WriteEvent("team_battle_skip_settled", new { manager.IsGameOver });
        }

        private static void SimulatePerformance(GameLevelManager manager)
        {
            // Native ShowGameOver computes the third (combined) track, applies
            // MartialModifyStat rewards and displays them exactly once.
            foreach (var suffix in new[] { "01", "02" })
            {
                var data = Traverse.Create(manager).Field("_progressLevelData_" + suffix).GetValue<ProgressLevelData>();
                if (data == null) continue;
                var maximum = Traverse.Create(data).Field("_max").GetValue<int>();
                var flag = Traverse.Create(data).Field("_flagData").GetValue<FlagData>();
                if (flag == null) continue;
                var before = flag.State;
                var after = BattleRewardPolicy.Performance(RewardRandom, before, maximum);
                data.SetData(after);
                LOMSaveProbePlugin.WriteEvent("team_battle_skip_performance", new { track = suffix, before, after, maximum });
            }
        }

        private static int SimulateMoneyPickup(
            GameLevelManager manager,
            out int enemyCount,
            out int eligibleEnemyCount,
            out int moneyDropCount,
            out int pickupCount,
            out int moneyPerPickup)
        {
            var enemies = GetRemainingEnemies(manager);
            enemyCount = enemies.Count;
            eligibleEnemyCount = 0;
            moneyDropCount = 0;
            pickupCount = 0;
            moneyPerPickup = GetMoneyPerPickup();
            if (moneyPerPickup <= 0)
            {
                return 0;
            }

            var money = 0;
            var budget = BattleRewardPolicy.MoneyBudget(RewardRandom, SimulatedMoneyCap);
            var collectionChance = 0.3 + RewardRandom.NextDouble() * 0.6;
            foreach (var enemy in enemies)
            {
                var traverse = Traverse.Create(enemy);
                var dropRate = traverse
                    .Field("_dropItemRate")
                    .GetValue<float>();
                var dropTypes = traverse
                    .Field("_dropItemType")
                    .GetValue<DropItemType[]>();
                if (dropRate <= 0f
                    || dropTypes == null
                    || Array.IndexOf(dropTypes, DropItemType.銀兩) < 0)
                {
                    continue;
                }

                eligibleEnemyCount++;
                if (money >= budget
                    || RewardRandom.NextDouble() >= dropRate)
                {
                    continue;
                }

                var dropType = dropTypes[
                    RewardRandom.Next(0, dropTypes.Length)];
                if (dropType != DropItemType.銀兩)
                {
                    continue;
                }

                moneyDropCount++;
                var dropPickupCount = RewardRandom.Next(5, 15);
                var collected = 0;
                for (var index = 0; index < dropPickupCount; index++)
                    if (RewardRandom.NextDouble() < collectionChance) collected++;
                var acceptedPickupCount = Math.Min(
                    collected,
                    (budget - money) / moneyPerPickup);
                pickupCount += acceptedPickupCount;
                money += acceptedPickupCount * moneyPerPickup;
            }

            return money;
        }

        private static List<NpcCharacter> GetRemainingEnemies(
            GameLevelManager manager)
        {
            var enemies = new List<NpcCharacter>();
            var enemySpawner = Traverse.Create(manager)
                .Field("_enemySpawner")
                .GetValue<NpcSpawner>();
            if (enemySpawner == null)
            {
                return enemies;
            }

            var spawnPoints = Traverse.Create(enemySpawner)
                .Field("_spawnPoints")
                .GetValue<CharacterSpawnPoint[]>();
            if (spawnPoints != null)
            {
                foreach (var spawnPoint in spawnPoints)
                {
                    if (spawnPoint == null || spawnPoint.IsEmpty)
                    {
                        continue;
                    }

                    var character = Traverse.Create(spawnPoint)
                        .Field("_instance")
                        .GetValue<Character>();
                    if (character == null || character.IsDead)
                    {
                        continue;
                    }

                    var enemy = character.GetComponent<NpcCharacter>();
                    if (enemy != null)
                    {
                        enemies.Add(enemy);
                    }
                }
            }

            var queuedEnemies = Traverse.Create(enemySpawner)
                .Field("_npcQueue")
                .GetValue<Queue<NpcSpawnPreset>>();
            if (queuedEnemies != null)
            {
                foreach (var preset in queuedEnemies)
                {
                    if (preset == null || preset.Prefab == null)
                    {
                        continue;
                    }

                    var enemy = preset.Prefab.GetComponent<NpcCharacter>();
                    if (enemy != null)
                    {
                        enemies.Add(enemy);
                    }
                }
            }

            return enemies;
        }

        private static int GetMoneyPerPickup()
        {
            var pool = GenericObjectPool<Money>.Instance;
            if (pool == null)
            {
                return 0;
            }

            Money pickup = null;
            try
            {
                pickup = pool.Get();
                return pickup == null ? 0 : pickup.Count;
            }
            finally
            {
                if (pickup != null)
                {
                    pickup.ReturnToPool();
                }
            }
        }

        private static void SetButtonLabel(Button button)
        {
            foreach (var behaviour in
                button.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var typeName = behaviour.GetType().FullName;
                if (typeName != null
                    && typeName.StartsWith(
                        "Lean.Localization.LeanLocalizedText",
                        StringComparison.Ordinal))
                {
                    behaviour.enabled = false;
                }
            }

            var textButton =
                button.GetComponentInChildren<TextButton>(true);
            foreach (var label in button.GetComponentsInChildren<Text>(true))
            {
                label.resizeTextForBestFit = true;
                label.resizeTextMaxSize = label.fontSize;
                label.resizeTextMinSize = Math.Min(18, label.fontSize);
            }
            if (textButton != null)
            {
                textButton.Setup(ButtonLabel);
                return;
            }

            foreach (var label in
                button.GetComponentsInChildren<Text>(true))
            {
                label.text = ButtonLabel;
            }
        }
    }
}
