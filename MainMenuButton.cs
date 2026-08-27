using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOStatsLogger
{
    [HarmonyPatch]
    internal static class MainMenuButton
    {
        private class AircraftStats
        {
            public int Flights { get; set; }
            public int AirKills { get; set; }
            public int GroundKills { get; set; }
            public int Losses { get; set; }
            public long DurationSeconds { get; set; }
        }

        private static readonly Color BgCardColor = new Color(0.10f, 0.14f, 0.18f, 0.95f);
        private static readonly Color BgButtonNormal = new Color(0.14f, 0.19f, 0.25f, 0.85f);
        private static readonly Color BgButtonActive = new Color(0.00f, 0.45f, 0.38f, 0.95f);
        private static readonly Color AccentGreen = new Color(0.00f, 1.00f, 0.78f);
        private static readonly Color AccentOrange = new Color(1.00f, 0.72f, 0.20f);
        private static readonly Color AccentBlue = new Color(0.20f, 0.80f, 1.00f);
        private static readonly Color TextDim = new Color(0.65f, 0.72f, 0.80f);

        private static string activeAircraftFilter = null;

        private static TMP_Text kpiFlightsVal;
        private static TMP_Text kpiDurationVal;
        private static TMP_Text kpiAirVal;
        private static TMP_Text kpiGroundVal;
        private static TMP_Text kpiKdVal;
        private static TMP_Text kpiSurvivalVal;
        private static TMP_Text kpiAvgTimeVal;

        private static Transform tableContentTransform;
        private static TMP_Text filterLabel;
        private static readonly Dictionary<string, Image> aircraftBtnImages = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        [HarmonyPatch(typeof(MainMenu), "Awake")]
        [HarmonyPostfix]
        private static void MainMenu_Awake_Postfix(MainMenu __instance)
        {
            try
            {
                var missionsButtonField = typeof(MainMenu).GetField(
                    "missionsButton",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                );
                var anyButton = missionsButtonField?.GetValue(__instance) as Button;
                if (anyButton == null) return;

                Transform parent = anyButton.transform.parent;
                if (parent == null || parent.Find("StatsButton") != null) return;

                Transform workshopTransform = parent.Find("WorkshopButton") ?? parent.Find("SettingsButton");
                if (workshopTransform == null) return;

                GameObject statsBtnObj = UnityEngine.Object.Instantiate(workshopTransform.gameObject, parent);
                statsBtnObj.name = "StatsButton";

                var btnText = statsBtnObj.GetComponentInChildren<TMP_Text>();
                if (btnText != null) btnText.text = "STATS";

                var button = statsBtnObj.GetComponent<Button>();
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => OpenStatsMenu(__instance, anyButton.gameObject));

                statsBtnObj.transform.SetAsLastSibling();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[MainMenuButton] Button creation error: {ex}");
            }
        }

        private static void OpenStatsMenu(MainMenu mainMenu, GameObject buttonPrefab)
        {
            try
            {
                activeAircraftFilter = null;
                aircraftBtnImages.Clear();

                var overlayField = typeof(MainMenu).GetField("overlayMenuLayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Transform overlayLayer = overlayField?.GetValue(mainMenu) as Transform;

                if (overlayLayer == null || GameAssets.i?.settingsMenu == null) return;

                GameObject statsMenuObj = UnityEngine.Object.Instantiate(GameAssets.i.settingsMenu, overlayLayer);
                statsMenuObj.name = "StatsMenu(Clone)";

                RectTransform statsRt = statsMenuObj.GetComponent<RectTransform>();
                if (statsRt != null)
                {
                    statsRt.anchorMin = Vector2.zero;
                    statsRt.anchorMax = Vector2.one;
                    statsRt.offsetMin = Vector2.zero;
                    statsRt.offsetMax = Vector2.zero;
                }

                TMP_FontAsset font = null;
                foreach (var tmp in statsMenuObj.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (font == null) font = tmp.font;
                }

                foreach (Transform child in statsMenuObj.transform)
                {
                    child.gameObject.SetActive(false);
                }

                // 1. Полноэкранная темная подложка
                GameObject fullBackdrop = new GameObject("FullBackdrop", typeof(RectTransform), typeof(Image));
                fullBackdrop.transform.SetParent(statsMenuObj.transform, false);
                
                RectTransform bdRt = fullBackdrop.GetComponent<RectTransform>();
                bdRt.anchorMin = Vector2.zero;
                bdRt.anchorMax = Vector2.one;
                bdRt.offsetMin = new Vector2(-1000, -1000);
                bdRt.offsetMax = new Vector2(1000, 1000);
                fullBackdrop.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.10f, 0.98f);

                // 2. Основной макет
                GameObject dashboardRoot = new GameObject("DashboardRoot", typeof(RectTransform), typeof(VerticalLayoutGroup));
                dashboardRoot.transform.SetParent(statsMenuObj.transform, false);

                RectTransform rootRt = dashboardRoot.GetComponent<RectTransform>();
                rootRt.anchorMin = new Vector2(0.03f, 0.03f);
                rootRt.anchorMax = new Vector2(0.97f, 0.95f);
                rootRt.offsetMin = Vector2.zero;
                rootRt.offsetMax = Vector2.zero;

                var vlg = dashboardRoot.GetComponent<VerticalLayoutGroup>();
                vlg.spacing = 10;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                List<FlightRecord> flights = StatsStorage.LoadAll();

                BuildHeaderRow(dashboardRoot.transform, font);
                BuildKpiRow(dashboardRoot.transform, font);
                BuildMainGrid(dashboardRoot.transform, flights, font);
                BuildFooter(dashboardRoot.transform, statsMenuObj, buttonPrefab);

                UpdateDashboardData(flights);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[MainMenuButton] UI rendering error: {ex}");
            }
        }

        private static void BuildHeaderRow(Transform parent, TMP_FontAsset font)
        {
            GameObject header = new GameObject("HeaderPanel", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            header.transform.SetParent(parent, false);
            header.GetComponent<Image>().color = BgCardColor;

            var le = header.GetComponent<LayoutElement>();
            le.preferredHeight = 36;
            le.flexibleHeight = 0;
            le.flexibleWidth = 1;

            TMP_Text headerText = CreateText(header.transform, "FLIGHT STATISTICS DASHBOARD", 18, Color.white, font);
            headerText.fontStyle = FontStyles.Bold;
            headerText.alignment = TextAlignmentOptions.Center;

            RectTransform txtRt = headerText.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
        }

        private static void BuildKpiRow(Transform parent, TMP_FontAsset font)
        {
            GameObject kpiRow = new GameObject("KpiRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            kpiRow.transform.SetParent(parent, false);

            var le = kpiRow.GetComponent<LayoutElement>();
            le.preferredHeight = 65;
            le.flexibleHeight = 0;
            le.flexibleWidth = 1;

            var hlg = kpiRow.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            kpiFlightsVal = CreateKpiCard(kpiRow.transform, "FLIGHTS", Color.white, font);
            kpiDurationVal = CreateKpiCard(kpiRow.transform, "TOTAL TIME", Color.white, font);
            kpiAvgTimeVal = CreateKpiCard(kpiRow.transform, "AVG FLIGHT", TextDim, font);
            kpiAirVal = CreateKpiCard(kpiRow.transform, "AIR KILLS", AccentBlue, font);
            kpiGroundVal = CreateKpiCard(kpiRow.transform, "GND KILLS", AccentOrange, font);
            kpiKdVal = CreateKpiCard(kpiRow.transform, "K/D RATIO", AccentGreen, font);
            kpiSurvivalVal = CreateKpiCard(kpiRow.transform, "SURVIVAL", AccentGreen, font);
        }

        private static TMP_Text CreateKpiCard(Transform parent, string label, Color valColor, TMP_FontAsset font)
        {
            GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            card.transform.SetParent(parent, false);
            card.GetComponent<Image>().color = BgCardColor;

            var vlg = card.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 6, 6);
            vlg.spacing = 2;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;

            TMP_Text lblText = CreateText(card.transform, label.ToUpper(), 10, TextDim, font);
            lblText.alignment = TextAlignmentOptions.Center;
            lblText.fontStyle = FontStyles.Bold;

            TMP_Text valText = CreateText(card.transform, "0", 20, valColor, font);
            valText.alignment = TextAlignmentOptions.Center;
            valText.fontStyle = FontStyles.Bold;

            return valText;
        }

        private static void BuildMainGrid(Transform parent, List<FlightRecord> flights, TMP_FontAsset font)
        {
            GameObject grid = new GameObject("MainGrid", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            grid.transform.SetParent(parent, false);

            var le = grid.GetComponent<LayoutElement>();
            le.flexibleHeight = 1;
            le.flexibleWidth = 1;

            var hlg = grid.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // --- Левая колонка (История вылетов) ---
            GameObject leftCol = CreatePanel(grid.transform, "LeftColumn", BgCardColor);
            var leftLe = leftCol.GetComponent<LayoutElement>();
            leftLe.preferredWidth = 0;
            leftLe.minWidth = 0;
            leftLe.flexibleWidth = 0.67f;

            // Шапка левой колонки (Название + Фильтр)
            GameObject leftHeader = new GameObject("LeftHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            leftHeader.transform.SetParent(leftCol.transform, false);

            var lhLe = leftHeader.GetComponent<LayoutElement>();
            lhLe.preferredHeight = 24;
            lhLe.flexibleHeight = 0;
            lhLe.flexibleWidth = 1;

            var lhHlg = leftHeader.GetComponent<HorizontalLayoutGroup>();
            lhHlg.spacing = 10;
            lhHlg.childControlWidth = true;
            lhHlg.childControlHeight = true;
            lhHlg.childForceExpandWidth = false;
            lhHlg.childForceExpandHeight = true;

            TMP_Text leftTitle = CreateText(leftHeader.transform, "FLIGHT LOGS HISTORY", 14, Color.white, font);
            leftTitle.fontStyle = FontStyles.Bold;
            var titleLe = leftTitle.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 0;

            filterLabel = CreateText(leftHeader.transform, "", 12, AccentGreen, font);
            filterLabel.alignment = TextAlignmentOptions.Right;

            var flLe = filterLabel.gameObject.AddComponent<LayoutElement>();
            flLe.flexibleWidth = 1;

            var filterBtn = filterLabel.gameObject.AddComponent<Button>();
            filterBtn.onClick.AddListener(() =>
            {
                activeAircraftFilter = null;
                UpdateDashboardData(flights);
            });

            // Статичная шапка таблицы с жесткими пиксельными размерами
            GameObject tableHeaderObj = new GameObject("TableHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            tableHeaderObj.transform.SetParent(leftCol.transform, false);
            var thLe = tableHeaderObj.AddComponent<LayoutElement>();
            thLe.preferredHeight = 26;
            thLe.flexibleHeight = 0;
            thLe.flexibleWidth = 1;

            var thHlg = tableHeaderObj.GetComponent<HorizontalLayoutGroup>();
            thHlg.spacing = 6;
            thHlg.childControlWidth = true;
            thHlg.childControlHeight = true;
            thHlg.childForceExpandWidth = false; // Строго по пикселям
            thHlg.padding = new RectOffset(8, 8, 0, 0);

            CreateHeaderCell(tableHeaderObj.transform, "DATE", 115, font);
            CreateHeaderCell(tableHeaderObj.transform, "AIRCRAFT", 155, font);
            CreateHeaderCell(tableHeaderObj.transform, "AIR", 45, font, TextAlignmentOptions.Center);
            CreateHeaderCell(tableHeaderObj.transform, "GND", 45, font, TextAlignmentOptions.Center);
            CreateHeaderCell(tableHeaderObj.transform, "RESULT", 95, font);
            CreateHeaderCell(tableHeaderObj.transform, "TIME", 60, font, TextAlignmentOptions.Right);

            // ScrollView для строк таблицы
            GameObject tableScrollView = CreateScrollView(leftCol.transform, "TableScrollView", out tableContentTransform);

            var tableVlg = tableContentTransform.gameObject.AddComponent<VerticalLayoutGroup>();
            tableVlg.spacing = 4;
            tableVlg.childControlWidth = true;
            tableVlg.childControlHeight = true;
            tableVlg.childForceExpandWidth = true;
            tableVlg.childForceExpandHeight = false;

            var tableCsf = tableContentTransform.gameObject.AddComponent<ContentSizeFitter>();
            tableCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // --- Правая колонка (Список техники) ---
            GameObject rightCol = CreatePanel(grid.transform, "RightColumn", BgCardColor);
            var rightLe = rightCol.GetComponent<LayoutElement>();
            rightLe.preferredWidth = 0;
            rightLe.minWidth = 0;
            rightLe.flexibleWidth = 0.33f;

            GameObject rightHeader = new GameObject("RightHeader", typeof(RectTransform), typeof(LayoutElement));
            rightHeader.transform.SetParent(rightCol.transform, false);
            var rhLe = rightHeader.GetComponent<LayoutElement>();
            rhLe.preferredHeight = 24;
            rhLe.flexibleHeight = 0;
            rhLe.flexibleWidth = 1;

            TMP_Text rightTitle = CreateText(rightHeader.transform, "AIRCRAFT FLEET (CLICK TO FILTER)", 13, Color.white, font);
            rightTitle.fontStyle = FontStyles.Bold;

            GameObject acScrollView = CreateScrollView(rightCol.transform, "AcScrollView", out Transform acContent);

            var acVlg = acContent.gameObject.AddComponent<VerticalLayoutGroup>();
            acVlg.spacing = 6;
            acVlg.childControlWidth = true;
            acVlg.childControlHeight = true;
            acVlg.childForceExpandWidth = true;
            acVlg.childForceExpandHeight = false;

            var acCsf = acContent.gameObject.AddComponent<ContentSizeFitter>();
            acCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var byAircraft = new Dictionary<string, AircraftStats>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in flights)
            {
                string ac = string.IsNullOrEmpty(f.Aircraft) ? "Unknown" : f.Aircraft;
                if (!byAircraft.TryGetValue(ac, out var stats))
                {
                    stats = new AircraftStats();
                    byAircraft[ac] = stats;
                }
                stats.Flights++;
                stats.AirKills += f.AirKills;
                stats.GroundKills += f.GroundKills;
                stats.DurationSeconds += f.DurationSeconds;
                if (f.Result == FlightState.ResultShotDown) stats.Losses++;
            }

            var sortedAircraft = byAircraft
                .OrderByDescending(kvp => kvp.Value.Flights)
                .ThenByDescending(kvp => kvp.Value.AirKills + kvp.Value.GroundKills);

            foreach (var kvp in sortedAircraft)
            {
                string acName = kvp.Key;
                var stats = kvp.Value;

                GameObject acBtnObj = new GameObject($"Item_{acName}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(VerticalLayoutGroup), typeof(LayoutElement));
                acBtnObj.transform.SetParent(acContent, false);

                var btnLe = acBtnObj.GetComponent<LayoutElement>();
                btnLe.preferredHeight = 46;
                btnLe.minHeight = 46;
                btnLe.preferredWidth = 0;
                btnLe.flexibleHeight = 0;
                btnLe.flexibleWidth = 1;

                var img = acBtnObj.GetComponent<Image>();
                img.color = BgButtonNormal;
                aircraftBtnImages[acName] = img;

                var btn = acBtnObj.GetComponent<Button>();

                var itemVlg = acBtnObj.GetComponent<VerticalLayoutGroup>();
                itemVlg.padding = new RectOffset(8, 8, 4, 4);
                itemVlg.spacing = 1;
                itemVlg.childControlWidth = true;
                itemVlg.childControlHeight = true;
                itemVlg.childForceExpandWidth = true;

                int maxF = Math.Max(1, flights.Count);
                int pct = (stats.Flights * 100) / maxF;
                int bars = Math.Max(1, pct / 10);

                string filled = new string('=', bars);
                string empty = new string('-', 10 - bars);
                string barStr = $"[<color=#00FFC8>{filled}</color><color=#2A3744>{empty}</color>]";

                TMP_Text acInfoText = CreateText(acBtnObj.transform, "", 11, Color.white, font);
                acInfoText.richText = true;
                acInfoText.text = $"<b><color=#00FFC8>{acName}</color></b> <color=#8A99AD>({stats.Flights} flights)</color>\n" +
                                  $"{barStr} Kills: {stats.AirKills + stats.GroundKills}";

                btn.onClick.AddListener(() =>
                {
                    activeAircraftFilter = string.Equals(activeAircraftFilter, acName, StringComparison.OrdinalIgnoreCase) ? null : acName;
                    UpdateDashboardData(flights);
                });
            }
        }

        private static void CreateHeaderCell(Transform parent, string text, float width, TMP_FontAsset font, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            TMP_Text lbl = CreateText(parent, text, 11, TextDim, font);
            lbl.fontStyle = FontStyles.Bold;
            lbl.alignment = alignment;
            var le = lbl.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.flexibleWidth = 0;
        }

        private static GameObject CreateScrollView(Transform parent, string name, out Transform contentTransform)
        {
            GameObject scrollObj = new GameObject(name, typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
            scrollObj.transform.SetParent(parent, false);

            var le = scrollObj.GetComponent<LayoutElement>();
            le.flexibleHeight = 1;
            le.flexibleWidth = 1;

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(scrollObj.transform, false);

            var vpRt = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;

            var vpImg = viewport.GetComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0.01f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);

            var cRt = content.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0, 1);
            cRt.anchorMax = new Vector2(1, 1);
            cRt.pivot = new Vector2(0, 1);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;

            var scrollRect = scrollObj.GetComponent<ScrollRect>();
            scrollRect.content = cRt;
            scrollRect.viewport = vpRt;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            contentTransform = content.transform;
            return scrollObj;
        }

        private static void UpdateDashboardData(List<FlightRecord> allFlights)
        {
            var filtered = string.IsNullOrEmpty(activeAircraftFilter)
                ? allFlights
                : allFlights.Where(f => string.Equals(f.Aircraft, activeAircraftFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            int totalFlights = filtered.Count;
            int airKills = 0, groundKills = 0, losses = 0, landedCount = 0;
            long totalSeconds = 0;

            foreach (var f in filtered)
            {
                airKills += f.AirKills;
                groundKills += f.GroundKills;
                totalSeconds += f.DurationSeconds;
                if (f.Result == FlightState.ResultLanded) landedCount++;
                if (f.Result == FlightState.ResultShotDown) losses++;
            }

            int totalKills = airKills + groundKills;
            float kd = losses > 0 ? (float)totalKills / losses : totalKills;
            float survivalRate = totalFlights > 0 ? ((float)landedCount / totalFlights) * 100f : 0f;
            long avgSeconds = totalFlights > 0 ? totalSeconds / totalFlights : 0;

            TimeSpan totalTime = TimeSpan.FromSeconds(totalSeconds);
            TimeSpan avgTime = TimeSpan.FromSeconds(avgSeconds);

            if (kpiFlightsVal != null) kpiFlightsVal.text = totalFlights.ToString();
            if (kpiDurationVal != null) kpiDurationVal.text = $"{(int)totalTime.TotalHours}h {totalTime.Minutes}m";
            if (kpiAvgTimeVal != null) kpiAvgTimeVal.text = $"{avgTime.Minutes}m {avgTime.Seconds}s";
            if (kpiAirVal != null) kpiAirVal.text = airKills.ToString();
            if (kpiGroundVal != null) kpiGroundVal.text = groundKills.ToString();
            if (kpiKdVal != null) kpiKdVal.text = kd.ToString("F2");
            if (kpiSurvivalVal != null) kpiSurvivalVal.text = $"{survivalRate:F0}%";

            if (filterLabel != null)
            {
                filterLabel.text = string.IsNullOrEmpty(activeAircraftFilter)
                    ? "<color=#8A99AD>[ ALL AIRCRAFT ]</color>"
                    : $"FILTER: <color=#00FFC8><b>{activeAircraftFilter}</b></color> <color=#FF5555>[RESET]</color>";
            }

            if (tableContentTransform != null)
            {
                foreach (Transform child in tableContentTransform)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }

                if (filtered.Count == 0)
                {
                    TMP_Text emptyTxt = CreateText(tableContentTransform, "No flight records found for this aircraft.", 12, TextDim, font: null);
                    emptyTxt.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
                }
                else
                {
                    for (int i = filtered.Count - 1; i >= 0; i--)
                    {
                        var f = filtered[i];
                        GameObject rowObj = new GameObject($"Row_{i}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement), typeof(Image));
                        rowObj.transform.SetParent(tableContentTransform, false);
                        
                        var rowImg = rowObj.GetComponent<Image>();
                        rowImg.color = new Color(0.12f, 0.16f, 0.22f, 0.6f);

                        var rowLe = rowObj.GetComponent<LayoutElement>();
                        rowLe.preferredHeight = 28;
                        rowLe.flexibleWidth = 1;

                        var rowHlg = rowObj.GetComponent<HorizontalLayoutGroup>();
                        rowHlg.spacing = 6;
                        rowHlg.childControlWidth = true;
                        rowHlg.childControlHeight = true;
                        rowHlg.childForceExpandWidth = false; // Строго по пикселям шапки
                        rowHlg.padding = new RectOffset(8, 8, 2, 2);

                        CreateTableCell(rowObj.transform, $"{f.Timestamp:HH:mm dd/MM}", 115, Color.white, false);
                        CreateTableCell(rowObj.transform, Truncate(f.Aircraft, 16), 155, Color.white, true);
                        CreateTableCell(rowObj.transform, f.AirKills.ToString(), 45, Color.white, false, TextAlignmentOptions.Center);
                        CreateTableCell(rowObj.transform, f.GroundKills.ToString(), 45, Color.white, false, TextAlignmentOptions.Center);

                        string statusColor = f.Result == FlightState.ResultLanded ? "#00FFC8" :
                                             f.Result == FlightState.ResultEjected ? "#FFB833" : "#FF5555";
                        string statusText = f.Result == FlightState.ResultLanded ? "LANDED" :
                                            f.Result == FlightState.ResultEjected ? "EJECTED" : "SHOT DOWN";
                        CreateTableCell(rowObj.transform, $"<color={statusColor}><b>{statusText}</b></color>", 95, Color.white, false);

                        TimeSpan dur = TimeSpan.FromSeconds(f.DurationSeconds);
                        CreateTableCell(rowObj.transform, $"{dur.Minutes}m {dur.Seconds}s", 60, TextDim, false, TextAlignmentOptions.Right);
                    }
                }
            }

            foreach (var kvp in aircraftBtnImages)
            {
                bool isSelected = string.Equals(kvp.Key, activeAircraftFilter, StringComparison.OrdinalIgnoreCase);
                kvp.Value.color = isSelected ? BgButtonActive : BgButtonNormal;
            }
        }

        private static void CreateTableCell(Transform parent, string text, float width, Color color, bool bold, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            GameObject cellObj = new GameObject("Cell", typeof(RectTransform));
            cellObj.transform.SetParent(parent, false);
            TMP_Text tmp = cellObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 12;
            tmp.color = color;
            tmp.richText = true;
            tmp.alignment = alignment;
            if (bold) tmp.fontStyle = FontStyles.Bold;

            var le = cellObj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.flexibleWidth = 0;
        }

        private static void BuildFooter(Transform parent, GameObject menuToClose, GameObject buttonPrefab)
        {
            GameObject footer = new GameObject("Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            footer.transform.SetParent(parent, false);

            var le = footer.GetComponent<LayoutElement>();
            le.preferredHeight = 36;
            le.flexibleHeight = 0;
            le.flexibleWidth = 1;

            var hlg = footer.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;

            if (buttonPrefab != null)
            {
                // Кнопка открытия папки логов
                GameObject folderBtnObj = UnityEngine.Object.Instantiate(buttonPrefab, footer.transform);
                folderBtnObj.name = "OpenFolderButton";

                var folderLe = folderBtnObj.GetComponent<LayoutElement>() ?? folderBtnObj.AddComponent<LayoutElement>();
                folderLe.preferredWidth = 160;

                var folderTxt = folderBtnObj.GetComponentInChildren<TMP_Text>();
                if (folderTxt != null) folderTxt.text = "OPEN LOGS";

                var folderBtn = folderBtnObj.GetComponent<Button>();
                folderBtn.onClick = new Button.ButtonClickedEvent();
                folderBtn.onClick.AddListener(() =>
                {
                    try
                    {
                        string dir = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "NOStatsLogger");
                        if (System.IO.Directory.Exists(dir)) 
                        {
                            System.Diagnostics.Process.Start(dir);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.LogError($"[MainMenuButton] Folder open error: {ex}");
                    }
                });

                // Кнопка BACK
                GameObject backBtnObj = UnityEngine.Object.Instantiate(buttonPrefab, footer.transform);
                backBtnObj.name = "BackButton";

                var btnLe = backBtnObj.GetComponent<LayoutElement>() ?? backBtnObj.AddComponent<LayoutElement>();
                btnLe.preferredWidth = 140;

                var btnText = backBtnObj.GetComponentInChildren<TMP_Text>();
                if (btnText != null) btnText.text = "BACK";

                var button = backBtnObj.GetComponent<Button>();
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => UnityEngine.Object.Destroy(menuToClose));
            }
        }

        private static GameObject CreatePanel(Transform parent, string name, Color bgColor)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = bgColor;

            var vlg = panel.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 8;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            return panel;
        }

        private static TMP_Text CreateText(Transform parent, string content, float fontSize, Color color, TMP_FontAsset font)
        {
            GameObject txtObj = new GameObject("Text", typeof(RectTransform));
            txtObj.transform.SetParent(parent, false);

            TMP_Text tmp = txtObj.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            return tmp;
        }

        private static string Truncate(string val, int max)
        {
            if (string.IsNullOrEmpty(val)) return "";
            return val.Length <= max ? val : val.Substring(0, max - 1) + "…";
        }
    }
}