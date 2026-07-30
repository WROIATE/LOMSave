using HarmonyLib;
using Mortal.Battle;
using Mortal.Core;
using System;
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
                    sourceWinButton,
                    giveupButton,
                    titleButton,
                    out var created);
                SetButtonAction(displayButton, sourceWinButton);
                var visible = displayButton != null
                    && manager != null;

                if (sourceWinButton != null)
                {
                    sourceWinButton.gameObject.SetActive(false);
                }
                if (displayButton != null)
                {
                    displayButton.gameObject.SetActive(visible);
                }

                string reason = null;
                if (sourceWinButton == null)
                {
                    reason = "win_action_button_missing";
                }
                else if (giveupButton == null)
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
            Button sourceWinButton,
            Button giveupButton,
            Button titleButton,
            out bool created)
        {
            created = false;
            if (sourceWinButton == null
                || giveupButton == null
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

        private static void SetButtonAction(
            Button button,
            Button sourceWinButton)
        {
            if (button == null || sourceWinButton == null)
            {
                return;
            }

            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(
                () => HandleDirectVictory(sourceWinButton));
        }

        private static void HandleDirectVictory(Button sourceWinButton)
        {
            var manager = GameLevelManager.Instance;
            if (manager != null && !manager.IsGameOver)
            {
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

            sourceWinButton.onClick.Invoke();
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
                if (money >= SimulatedMoneyCap
                    || UnityEngine.Random.value >= dropRate)
                {
                    continue;
                }

                var dropType = dropTypes[
                    UnityEngine.Random.Range(0, dropTypes.Length)];
                if (dropType != DropItemType.銀兩)
                {
                    continue;
                }

                moneyDropCount++;
                var dropPickupCount = UnityEngine.Random.Range(5, 15);
                var acceptedPickupCount = Math.Min(
                    dropPickupCount,
                    (SimulatedMoneyCap - money) / moneyPerPickup);
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
