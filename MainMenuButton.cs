using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NOStatsLogger
{
    [HarmonyPatch]
    internal static class MainMenuButton
    {
        private static GameObject customStatsPanelGO;
        private static Transform buttonsSidebarContainer;
        private static Transform mainContentContainer;
        
        private static TextMeshProUGUI statsTitleText;
        private static TextMeshProUGUI statsDetailsText;
        private static TMP_FontAsset globalFont;

        private static List<FlightRecord> allFlightLogs = new List<FlightRecord>();
        private static string selectedAircraftFilter = "ALL";
        
        private static GameObject buttonPrefabSample;
        private static List<GameObject> spawnedButtons = new List<GameObject>();

        [HarmonyPatch(typeof(MainMenu), "Awake")]
        [HarmonyPostfix]
        private static void MainMenu_Awake_Postfix(MainMenu __instance)
        {
            try
            {
                var missionsBtnField = typeof(MainMenu).GetField("missionsButton", BindingFlags.NonPublic | BindingFlags.Instance);
                var missionsButton = missionsBtnField?.GetValue(__instance) as Button;

                if (missionsButton == null) return;

                Transform menuButtonsParent = missionsButton.transform.parent;
                if (menuButtonsParent.Find("StatsButton") != null) return;

                Transform sampleBtnTransform = menuButtonsParent.GetChild(menuButtonsParent.childCount - 1);
                GameObject statsBtnGO = UnityEngine.Object.Instantiate(sampleBtnTransform.gameObject, menuButtonsParent);
                statsBtnGO.name = "StatsButton";
                statsBtnGO.transform.SetAsLastSibling();

                var customControllers = statsBtnGO.GetComponents<MonoBehaviour>();
                foreach (var ctrl in customControllers)
                {
                    if (ctrl == null) continue;
                    string typeName = ctrl.GetType().Name;
                    if (typeName.Contains("ButtonController") || typeName.Contains("SettingsMenuButton"))
                    {
                        UnityEngine.Object.Destroy(ctrl);
                    }
                }

                var tmpText = statsBtnGO.GetComponentInChildren<TMP_Text>();
                if (tmpText != null)
                {
                    tmpText.text = "STATS";
                    if (tmpText.font != null) globalFont = tmpText.font;
                }

                Button btnComponent = statsBtnGO.GetComponent<Button>();
                if (btnComponent == null)
                {
                    btnComponent = statsBtnGO.AddComponent<Button>();
                }

                btnComponent.onClick = new Button.ButtonClickedEvent();
                btnComponent.onClick.AddListener(() =>
                {
                    Plugin.Log?.LogInfo("[MainMenuButton] Нажата кнопка STATS!");
                    OpenStatsMenu();
                });

                if (customStatsPanelGO == null)
                {
                    Transform topCanvasTransform = missionsButton.transform.root;
                    BuildAutonomousUI(topCanvasTransform);
                }

                Plugin.Log?.LogInfo("[MainMenuButton] Кнопка STATS успешно внедрена!");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[MainMenuButton] Ошибка инициализации UI: {ex}");
            }
        }

        private static TextMeshProUGUI AddTMPText(GameObject parent, string text, float fontSize, TextAlignmentOptions align)
        {
            GameObject go = new GameObject("TMPText", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (globalFont != null) tmp.font = globalFont;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.color = Color.white;
            return tmp;
        }

        private static void BuildAutonomousUI(Transform parentCanvasTransform)
        {
            try
            {
                // 1. Root
                customStatsPanelGO = new GameObject("CustomStatsPanel_UI", typeof(RectTransform));
                if (parentCanvasTransform != null)
                {
                    customStatsPanelGO.transform.SetParent(parentCanvasTransform, false);
                }
                customStatsPanelGO.SetActive(false);

                RectTransform rootRect = customStatsPanelGO.GetComponent<RectTransform>();
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.sizeDelta = Vector2.zero;

                Image bgImg = customStatsPanelGO.AddComponent<Image>();
                bgImg.color = new Color(0.04f, 0.06f, 0.09f, 0.98f);

                // 2. Header
                TextMeshProUGUI header = AddTMPText(customStatsPanelGO, "PILOT FLIGHT LOGS & STATISTICS", 30, TextAlignmentOptions.Left);
                header.fontStyle = FontStyles.Bold;
                RectTransform headerRect = header.GetComponent<RectTransform>();
                headerRect.anchorMin = new Vector2(0, 1);
                headerRect.anchorMax = new Vector2(0, 1);
                headerRect.pivot = new Vector2(0, 1);
                headerRect.anchoredPosition = new Vector2(320, -40);
                headerRect.sizeDelta = new Vector2(800, 50);

                // 3. Back Button
                GameObject backBtnGO = new GameObject("BackButton", typeof(RectTransform));
                backBtnGO.transform.SetParent(customStatsPanelGO.transform, false);
                
                Image backImg = backBtnGO.AddComponent<Image>();
                backImg.color = new Color(0.18f, 0.22f, 0.28f, 0.9f);

                Button backBtn = backBtnGO.AddComponent<Button>();
                backBtn.onClick.AddListener(() => customStatsPanelGO.SetActive(false));

                RectTransform backRect = backBtnGO.GetComponent<RectTransform>();
                backRect.anchorMin = new Vector2(0, 1);
                backRect.anchorMax = new Vector2(0, 1);
                backRect.pivot = new Vector2(0, 1);
                backRect.anchoredPosition = new Vector2(60, -40);
                backRect.sizeDelta = new Vector2(140, 45);

                TextMeshProUGUI backTxt = AddTMPText(backBtnGO, "< BACK", 20, TextAlignmentOptions.Center);
                RectTransform backTxtRect = backTxt.GetComponent<RectTransform>();
                backTxtRect.anchorMin = Vector2.zero;
                backTxtRect.anchorMax = Vector2.one;
                backTxtRect.sizeDelta = Vector2.zero;

                // 4. Sidebar
                GameObject sidebar = new GameObject("Sidebar_Container", typeof(RectTransform), typeof(VerticalLayoutGroup));
                sidebar.transform.SetParent(customStatsPanelGO.transform, false);
                buttonsSidebarContainer = sidebar.transform;

                VerticalLayoutGroup vlg = sidebar.GetComponent<VerticalLayoutGroup>();
                vlg.spacing = 8;
                vlg.childControlHeight = false;
                vlg.childControlWidth = true;

                RectTransform sidebarRect = sidebar.GetComponent<RectTransform>();
                sidebarRect.anchorMin = new Vector2(0, 0);
                sidebarRect.anchorMax = new Vector2(0, 1);
                sidebarRect.pivot = new Vector2(0, 1);
                sidebarRect.anchoredPosition = new Vector2(60, -110);
                sidebarRect.sizeDelta = new Vector2(240, -140);

                // Sample button prefab
                buttonPrefabSample = new GameObject("AircraftBtn_Sample", typeof(RectTransform), typeof(LayoutElement));
                buttonPrefabSample.transform.SetParent(customStatsPanelGO.transform, false);
                
                buttonPrefabSample.AddComponent<Image>().color = new Color(0.12f, 0.16f, 0.22f, 0.9f);
                buttonPrefabSample.AddComponent<Button>();
                buttonPrefabSample.GetComponent<LayoutElement>().minHeight = 40;

                TextMeshProUGUI sampleTxt = AddTMPText(buttonPrefabSample, "FILTER", 18, TextAlignmentOptions.Center);
                RectTransform sampleTxtRect = sampleTxt.GetComponent<RectTransform>();
                sampleTxtRect.anchorMin = Vector2.zero;
                sampleTxtRect.anchorMax = Vector2.one;
                sampleTxtRect.sizeDelta = Vector2.zero;

                buttonPrefabSample.SetActive(false);

                // 5. Content Panel
                GameObject contentPanel = new GameObject("Content_Panel", typeof(RectTransform));
                contentPanel.transform.SetParent(customStatsPanelGO.transform, false);
                mainContentContainer = contentPanel.transform;

                RectTransform contentRect = contentPanel.GetComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0, 0);
                contentRect.anchorMax = new Vector2(1, 1);
                contentRect.offsetMin = new Vector2(340, 40);
                contentRect.offsetMax = new Vector2(-40, -110);

                statsTitleText = AddTMPText(contentPanel, "", 26, TextAlignmentOptions.Left);
                statsTitleText.fontStyle = FontStyles.Bold;
                statsTitleText.color = new Color(0.35f, 0.75f, 1.0f);
                RectTransform titleRect = statsTitleText.GetComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0, 1);
                titleRect.anchorMax = new Vector2(1, 1);
                titleRect.pivot = new Vector2(0, 1);
                titleRect.anchoredPosition = Vector2.zero;
                titleRect.sizeDelta = new Vector2(0, 40);

                statsDetailsText = AddTMPText(contentPanel, "", 20, TextAlignmentOptions.Left);
                statsDetailsText.lineSpacing = 10;
                statsDetailsText.color = new Color(0.9f, 0.9f, 0.95f);
                RectTransform bodyRect = statsDetailsText.GetComponent<RectTransform>();
                bodyRect.anchorMin = new Vector2(0, 0);
                bodyRect.anchorMax = new Vector2(1, 1);
                bodyRect.pivot = new Vector2(0, 1);
                bodyRect.anchoredPosition = new Vector2(0, -50);
                bodyRect.sizeDelta = new Vector2(0, -50);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[MainMenuButton] Ошибка генерации UI элементов: {ex}");
            }
        }

        private static void OpenStatsMenu()
        {
            if (customStatsPanelGO == null) return;

            allFlightLogs = StatsStorage.LoadAllFlights();
            selectedAircraftFilter = "ALL";

            PopulateAircraftSidebar();
            UpdateStatsDisplay();

            customStatsPanelGO.SetActive(true);
            customStatsPanelGO.transform.SetAsLastSibling();
        }

        private static void PopulateAircraftSidebar()
        {
            if (buttonsSidebarContainer == null || buttonPrefabSample == null) return;

            foreach (var go in spawnedButtons)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            spawnedButtons.Clear();

            List<string> aircraftList = new List<string> { "ALL" };

            if (allFlightLogs != null && allFlightLogs.Count > 0)
            {
                var loggedNames = allFlightLogs
                    .Select(f => f.Aircraft)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct()
                    .OrderBy(a => a);

                aircraftList.AddRange(loggedNames);
            }

            foreach (string aircraftName in aircraftList)
            {
                GameObject newBtnGO = UnityEngine.Object.Instantiate(buttonPrefabSample, buttonsSidebarContainer);
                newBtnGO.SetActive(true);
                spawnedButtons.Add(newBtnGO);

                TMP_Text btnText = newBtnGO.GetComponentInChildren<TMP_Text>();
                if (btnText != null)
                {
                    btnText.text = aircraftName == "ALL" ? "ALL AIRCRAFT" : aircraftName.ToUpper();
                }

                Button btn = newBtnGO.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    string targetFilter = aircraftName;
                    btn.onClick.AddListener(() =>
                    {
                        selectedAircraftFilter = targetFilter;
                        UpdateStatsDisplay();
                    });
                }
            }
        }

        private static void UpdateStatsDisplay()
        {
            if (statsTitleText == null || statsDetailsText == null) return;

            var filtered = selectedAircraftFilter == "ALL" 
                ? allFlightLogs 
                : allFlightLogs.Where(f => f.Aircraft == selectedAircraftFilter).ToList();

            int totalFlights = filtered.Count;
            int landed = filtered.Count(f => f.Result == FlightState.ResultLanded);
            int ejected = filtered.Count(f => f.Result == FlightState.ResultEjected);
            int shotDown = filtered.Count(f => f.Result == FlightState.ResultShotDown);
            int airKills = filtered.Sum(f => f.AircraftKills);
            int groundKills = filtered.Sum(f => f.VehicleKills);
            int totalSec = filtered.Sum(f => f.DurationSeconds);

            TimeSpan t = TimeSpan.FromSeconds(totalSec);
            string timeFormatted = $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s";
            float survivalRate = totalFlights > 0 ? ((float)landed / totalFlights) * 100f : 0f;

            statsTitleText.text = selectedAircraftFilter == "ALL" 
                ? "FLEET OVERALL STATISTICS" 
                : $"AIRCRAFT LOGS: {selectedAircraftFilter.ToUpper()}";

            statsDetailsText.text = 
                $"<b>Total Sorties:</b>  <color=#FFFFFF>{totalFlights}</color>\n" +
                $"<b>Total Flight Time:</b>  <color=#FFFFFF>{timeFormatted}</color>\n" +
                $"<b>Survival Rate:</b>  <color=#FFFFFF>{survivalRate:F1}%</color>\n\n" +
                $"<b>Successful Landings:</b>  <color=#55FF55>{landed}</color>\n" +
                $"<b>Ejections:</b>  <color=#FFAA00>{ejected}</color>\n" +
                $"<b>Shot Down / Crashed:</b>  <color=#FF5555>{shotDown}</color>\n\n" +
                $"<b>Aerial Kills:</b>  <color=#FFFF55>{airKills}</color>\n" +
                $"<b>Ground/Naval Targets:</b>  <color=#FFFF55>{groundKills}</color>\n" +
                $"<b>Total Kills:</b>  <color=#55AAFF>{airKills + groundKills}</color>";
        }
    }
}