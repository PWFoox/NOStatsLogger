using System;
using System.Collections.Generic;
using System.Linq;
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
            public int Landed { get; set; }
            public int Ejected { get; set; }
            public int ShotDown { get; set; }
            public long DurationSeconds { get; set; }
        }

        private static readonly Color BgCardColor = new Color(0.10f, 0.14f, 0.18f, 0.95f);
        private static readonly Color BgSidebarColor = new Color(0.07f, 0.10f, 0.13f, 0.98f);
        private static readonly Color BgButtonNormal = new Color(0.14f, 0.19f, 0.25f, 0.85f);
        private static readonly Color BgButtonActive = new Color(0.00f, 0.45f, 0.38f, 0.95f);
        private static readonly Color AccentGreen = new Color(0.00f, 1.00f, 0.78f);
        private static readonly Color AccentOrange = new Color(1.00f, 0.72f, 0.20f);
        private static readonly Color AccentBlue = new Color(0.20f, 0.80f, 1.00f);
        private static readonly Color TextDim = new Color(0.65f, 0.72f, 0.80f);

        private static string activeAircraftFilter = null;
        private static string activeTab = "overview";

        // ---- Общие для нескольких вкладок ----
        private static TMP_Text kpiFlightsVal;
        private static TMP_Text kpiDurationVal;
        private static TMP_Text kpiAirVal;
        private static TMP_Text kpiGroundVal;
        private static TMP_Text kpiKdVal;
        private static TMP_Text kpiSurvivalVal;
        private static TMP_Text kpiAvgTimeVal;

        private static Transform recentContentTransform;   // вкладка "Обзор" — короткий список
        private static Transform tableContentTransform;     // вкладка "Полёты" — полная таблица
        private static Transform aircraftTableContent;      // вкладка "Техника" — таблица по самолётам
        private static TMP_Text filterLabel;

        private static readonly Dictionary<string, GameObject> tabPanels = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Image> navButtonImages = new Dictionary<string, Image>();

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
                activeTab = "overview";
                navButtonImages.Clear();
                tabPanels.Clear();

                var overlayField = typeof(MainMenu).GetField("overlayMenuLayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Transform overlayLayer = overlayField?.GetValue(mainMenu) as Transform;

                if (overlayLayer == null || GameAssets.i?.settingsMenu == null) return;

                // Не даём открыть второй экземпляр поверх первого.
                if (overlayLayer.Find("StatsMenu(Clone)") != null) return;

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

                // 1. Полноэкранная тёмная подложка
                GameObject fullBackdrop = new GameObject("FullBackdrop", typeof(RectTransform), typeof(Image));
                fullBackdrop.transform.SetParent(statsMenuObj.transform, false);

                RectTransform bdRt = fullBackdrop.GetComponent<RectTransform>();
                bdRt.anchorMin = Vector2.zero;
                bdRt.anchorMax = Vector2.one;
                bdRt.offsetMin = new Vector2(-1000, -1000);
                bdRt.offsetMax = new Vector2(1000, 1000);
                fullBackdrop.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.10f, 0.98f);

                // 2. Основной макет: Header / Body(Sidebar+Content) / Footer
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
                BuildBody(dashboardRoot.transform, flights, font, buttonPrefab);
                BuildFooter(dashboardRoot.transform, statsMenuObj, buttonPrefab);

                UpdateDashboardData(flights, font);
                SwitchTab("overview");
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

        // ================== BODY: SIDEBAR + CONTENT ==================

        private static void BuildBody(Transform parent, List<FlightRecord> flights, TMP_FontAsset font, GameObject buttonPrefab)
        {
            GameObject body = new GameObject("Body", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            body.transform.SetParent(parent, false);

            var bodyLe = body.GetComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1;
            bodyLe.flexibleWidth = 1;

            var bodyHlg = body.GetComponent<HorizontalLayoutGroup>();
            bodyHlg.spacing = 10;
            bodyHlg.childControlWidth = true;
            bodyHlg.childControlHeight = true;
            bodyHlg.childForceExpandWidth = false;
            bodyHlg.childForceExpandHeight = true;

            BuildSidebar(body.transform, font);

            // Контейнер, в котором панели вкладок лежат друг на друге
            // (растянуты на весь размер), но активна только одна.
            GameObject contentArea = new GameObject("ContentArea", typeof(RectTransform), typeof(LayoutElement));
            contentArea.transform.SetParent(body.transform, false);
            var caLe = contentArea.GetComponent<LayoutElement>();
            caLe.flexibleWidth = 1;
            caLe.flexibleHeight = 1;

            tabPanels["overview"] = BuildOverviewPanel(contentArea.transform, flights, font);
            tabPanels["flights"] = BuildFlightsPanel(contentArea.transform, flights, font);
            tabPanels["aircraft"] = BuildAircraftPanel(contentArea.transform, flights, font);

            foreach (var panel in tabPanels.Values)
            {
                RectTransform rt = panel.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }

        private static void BuildSidebar(Transform parent, TMP_FontAsset font)
        {
            GameObject sidebar = new GameObject("Sidebar", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            sidebar.transform.SetParent(parent, false);
            sidebar.GetComponent<Image>().color = BgSidebarColor;

            var le = sidebar.GetComponent<LayoutElement>();
            le.preferredWidth = 150;
            le.minWidth = 150;
            le.flexibleWidth = 0;
            le.flexibleHeight = 1;

            var vlg = sidebar.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 10, 10);
            vlg.spacing = 4;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateNavButton(sidebar.transform, "overview", "ОБЗОР", font);
            CreateNavButton(sidebar.transform, "flights", "ПОЛЁТЫ", font);
            CreateNavButton(sidebar.transform, "aircraft", "ТЕХНИКА", font);
        }

        private static void CreateNavButton(Transform parent, string tabId, string label, TMP_FontAsset font)
        {
            GameObject btnObj = new GameObject($"Nav_{tabId}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            btnObj.transform.SetParent(parent, false);

            var le = btnObj.GetComponent<LayoutElement>();
            le.preferredHeight = 34;
            le.flexibleHeight = 0;
            le.flexibleWidth = 1;

            var img = btnObj.GetComponent<Image>();
            img.color = BgButtonNormal;
            navButtonImages[tabId] = img;

            TMP_Text txt = CreateText(btnObj.transform, label, 12, Color.white, font);
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;
            RectTransform txtRt = txt.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            var btn = btnObj.GetComponent<Button>();
            btn.onClick.AddListener(() => SwitchTab(tabId));
        }

        private static void SwitchTab(string tabId)
        {
            activeTab = tabId;

            foreach (var kvp in tabPanels)
            {
                kvp.Value.SetActive(kvp.Key == tabId);
            }

            foreach (var kvp in navButtonImages)
            {
                kvp.Value.color = kvp.Key == tabId ? BgButtonActive : BgButtonNormal;
            }
        }

        // ================== ВКЛАДКА: ОБЗОР ==================

        private static GameObject BuildOverviewPanel(Transform parent, List<FlightRecord> flights, TMP_FontAsset font)
        {
            GameObject panel = CreatePanel(parent, "OverviewPanel", new Color(0, 0, 0, 0));
            var vlg = panel.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.spacing = 10;

            BuildKpiRow(panel.transform, font);

            GameObject recentCard = CreatePanel(panel.transform, "RecentCard", BgCardColor);
            var recentLe = recentCard.GetComponent<LayoutElement>() ?? recentCard.AddComponent<LayoutElement>();
            recentLe.flexibleHeight = 1;
            recentLe.flexibleWidth = 1;

            TMP_Text recentTitle = CreateText(recentCard.transform, "RECENT FLIGHTS", 13, Color.white, font);
            recentTitle.fontStyle = FontStyles.Bold;
            var rtLe = recentTitle.gameObject.AddComponent<LayoutElement>();
            rtLe.preferredHeight = 20;
            rtLe.flexibleHeight = 0;

            GameObject recentScroll = CreateScrollView(recentCard.transform, "RecentScrollView", out recentContentTransform);
            var recentVlg = recentContentTransform.gameObject.AddComponent<VerticalLayoutGroup>();
            recentVlg.spacing = 4;
            recentVlg.childControlWidth = true;
            recentVlg.childControlHeight = true;
            recentVlg.childForceExpandWidth = true;
            recentVlg.childForceExpandHeight = false;

            var recentCsf = recentContentTransform.gameObject.AddComponent<ContentSizeFitter>();
            recentCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return panel;
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

        // ================== ВКЛАДКА: ПОЛЁТЫ ==================

        private static GameObject BuildFlightsPanel(Transform parent, List<FlightRecord> flights, TMP_FontAsset font)
        {
            GameObject panel = CreatePanel(parent, "FlightsPanel", BgCardColor);

            GameObject header = new GameObject("FlightsHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            header.transform.SetParent(panel.transform, false);

            var hLe = header.GetComponent<LayoutElement>();
            hLe.preferredHeight = 24;
            hLe.flexibleHeight = 0;
            hLe.flexibleWidth = 1;

            var hHlg = header.GetComponent<HorizontalLayoutGroup>();
            hHlg.spacing = 10;
            hHlg.childControlWidth = true;
            hHlg.childControlHeight = true;
            hHlg.childForceExpandWidth = false;
            hHlg.childForceExpandHeight = true;

            TMP_Text title = CreateText(header.transform, "FLIGHT LOGS HISTORY", 14, Color.white, font);
            title.fontStyle = FontStyles.Bold;
            var titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 0;

            filterLabel = CreateText(header.transform, "", 12, AccentGreen, font);
            filterLabel.alignment = TextAlignmentOptions.Right;

            var flLe = filterLabel.gameObject.AddComponent<LayoutElement>();
            flLe.flexibleWidth = 1;

            var filterBtn = filterLabel.gameObject.AddComponent<Button>();
            filterBtn.onClick.AddListener(() =>
            {
                activeAircraftFilter = null;
                UpdateDashboardData(StatsStorage.LoadAll(), font);
            });

            GameObject tableHeaderObj = new GameObject("TableHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            tableHeaderObj.transform.SetParent(panel.transform, false);
            var thLe = tableHeaderObj.AddComponent<LayoutElement>();
            thLe.preferredHeight = 26;
            thLe.flexibleHeight = 0;
            thLe.flexibleWidth = 1;

            var thHlg = tableHeaderObj.GetComponent<HorizontalLayoutGroup>();
            thHlg.spacing = 6;
            thHlg.childControlWidth = true;
            thHlg.childControlHeight = true;
            thHlg.childForceExpandWidth = false;
            thHlg.padding = new RectOffset(8, 8, 0, 0);

            CreateHeaderCell(tableHeaderObj.transform, "DATE", 115, font);
            CreateHeaderCell(tableHeaderObj.transform, "AIRCRAFT", 155, font);
            CreateHeaderCell(tableHeaderObj.transform, "AIR", 45, font, TextAlignmentOptions.Center);
            CreateHeaderCell(tableHeaderObj.transform, "GND", 45, font, TextAlignmentOptions.Center);
            CreateHeaderCell(tableHeaderObj.transform, "RESULT", 95, font);
            CreateHeaderCell(tableHeaderObj.transform, "TIME", 60, font, TextAlignmentOptions.Right);

            GameObject tableScrollView = CreateScrollView(panel.transform, "TableScrollView", out tableContentTransform);

            var tableVlg = tableContentTransform.gameObject.AddComponent<VerticalLayoutGroup>();
            tableVlg.spacing = 4;
            tableVlg.childControlWidth = true;
            tableVlg.childControlHeight = true;
            tableVlg.childForceExpandWidth = true;
            tableVlg.childForceExpandHeight = false;

            var tableCsf = tableContentTransform.gameObject.AddComponent<ContentSizeFitter>();
            tableCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return panel;
        }

        // ================== ВКЛАДКА: ТЕХНИКА ==================

        private static GameObject BuildAircraftPanel(Transform parent, List<FlightRecord> flights, TMP_FontAsset font)
        {
            GameObject panel = CreatePanel(parent, "AircraftPanel", BgCardColor);

            TMP_Text title = CreateText(panel.transform, "AIRCRAFT FLEET STATISTICS", 14, Color.white, font);
            title.fontStyle = FontStyles.Bold;
            var titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 24;
            titleLe.flexibleHeight = 0;

            GameObject tableHeaderObj = new GameObject("AcTableHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            tableHeaderObj.transform.SetParent(panel.transform, false);
            var thLe = tableHeaderObj.AddComponent<LayoutElement>();
            thLe.preferredHeight = 26;
            thLe.flexibleHeight = 0;
            thLe.flexibleWidth = 1;

            var thHlg = tableHeaderObj.GetComponent<HorizontalLayoutGroup>();
            thHlg.spacing = 6;
            thHlg.childControlWidth = true;
            thHlg.childControlHeight = true;
            thHlg.childForceExpandWidth = false;
            thHlg.padding = new RectOffset(8, 8, 0, 0);

            CreateHeaderCell(tableHeaderObj.transform, "AIRCRAFT", 170, font);
            CreateHeaderCell(tableHeaderObj.transform, "FLIGHTS", 60, font, TextAlignmentOptions.Center);
            CreateHeaderCell(tableHeaderObj.transform, "AIR", 45, font, TextAlignmentOptions.Center);
            CreateHeaderCell(tableHeaderObj.transform, "GND", 45, font, TextAlignmentOptions.Center);
            CreateHeaderCell(tableHeaderObj.transform, "K/D", 55, font, TextAlignmentOptions.Center);
            CreateHeaderCell(tableHeaderObj.transform, "AVG TIME", 70, font, TextAlignmentOptions.Center);
            CreateHeaderCell(tableHeaderObj.transform, "LAND/EJ/DOWN", 110, font, TextAlignmentOptions.Right);

            GameObject scrollView = CreateScrollView(panel.transform, "AcTableScrollView", out aircraftTableContent);

            var vlg2 = aircraftTableContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg2.spacing = 4;
            vlg2.childControlWidth = true;
            vlg2.childControlHeight = true;
            vlg2.childForceExpandWidth = true;
            vlg2.childForceExpandHeight = false;

            var csf2 = aircraftTableContent.gameObject.AddComponent<ContentSizeFitter>();
            csf2.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return panel;
        }

        // ================== ОБЩИЕ ЭЛЕМЕНТЫ ==================

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

        private static void UpdateDashboardData(List<FlightRecord> allFlights, TMP_FontAsset font)
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
            if (kpiAvgTimeVal != null) kpiAvgTimeVal.text = $"{(int)avgTime.TotalHours}h {avgTime.Minutes}m {avgTime.Seconds}s";
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

            RebuildFlightRows(tableContentTransform, filtered, showAll: true);
            RebuildFlightRows(recentContentTransform, filtered, showAll: false, maxRows: 8);
            RebuildAircraftTable(allFlights, font);
        }

        private static void RebuildFlightRows(Transform container, List<FlightRecord> filtered, bool showAll, int maxRows = int.MaxValue)
        {
            if (container == null)
                return;

            foreach (Transform child in container)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            if (filtered.Count == 0)
            {
                TMP_Text emptyTxt = CreateText(container, "No flight records found.", 12, TextDim, font: null);
                emptyTxt.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
                return;
            }

            int shown = 0;
            for (int i = filtered.Count - 1; i >= 0 && shown < maxRows; i--, shown++)
            {
                var f = filtered[i];
                GameObject rowObj = new GameObject($"Row_{i}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement), typeof(Image));
                rowObj.transform.SetParent(container, false);

                var rowImg = rowObj.GetComponent<Image>();
                rowImg.color = new Color(0.12f, 0.16f, 0.22f, 0.6f);

                var rowLe = rowObj.GetComponent<LayoutElement>();
                rowLe.preferredHeight = 28;
                rowLe.flexibleWidth = 1;

                var rowHlg = rowObj.GetComponent<HorizontalLayoutGroup>();
                rowHlg.spacing = 6;
                rowHlg.childControlWidth = true;
                rowHlg.childControlHeight = true;
                rowHlg.childForceExpandWidth = false;
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

        private static void RebuildAircraftTable(List<FlightRecord> allFlights, TMP_FontAsset font)
        {
            if (aircraftTableContent == null)
                return;

            foreach (Transform child in aircraftTableContent)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            var byAircraft = new Dictionary<string, AircraftStats>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in allFlights)
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

                if (f.Result == FlightState.ResultLanded) stats.Landed++;
                else if (f.Result == FlightState.ResultEjected) stats.Ejected++;
                else if (f.Result == FlightState.ResultShotDown) stats.ShotDown++;
            }

            var sortedAircraft = byAircraft
                .OrderByDescending(kvp => kvp.Value.Flights)
                .ThenByDescending(kvp => kvp.Value.AirKills + kvp.Value.GroundKills);

            if (byAircraft.Count == 0)
            {
                TMP_Text emptyTxt = CreateText(aircraftTableContent, "No flight records found.", 12, TextDim, font);
                emptyTxt.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
                return;
            }

            foreach (var kvp in sortedAircraft)
            {
                string acName = kvp.Key;
                var stats = kvp.Value;

                int totalKills = stats.AirKills + stats.GroundKills;
                float kd = stats.ShotDown > 0 ? (float)totalKills / stats.ShotDown : totalKills;
                long avgSeconds = stats.Flights > 0 ? stats.DurationSeconds / stats.Flights : 0;
                TimeSpan avgTime = TimeSpan.FromSeconds(avgSeconds);

                GameObject rowObj = new GameObject($"AcRow_{acName}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement), typeof(Image), typeof(Button));
                rowObj.transform.SetParent(aircraftTableContent, false);

                var rowImg = rowObj.GetComponent<Image>();
                bool isSelected = string.Equals(acName, activeAircraftFilter, StringComparison.OrdinalIgnoreCase);
                rowImg.color = isSelected ? BgButtonActive : new Color(0.12f, 0.16f, 0.22f, 0.6f);

                var rowLe = rowObj.GetComponent<LayoutElement>();
                rowLe.preferredHeight = 30;
                rowLe.flexibleWidth = 1;

                var rowHlg = rowObj.GetComponent<HorizontalLayoutGroup>();
                rowHlg.spacing = 6;
                rowHlg.childControlWidth = true;
                rowHlg.childControlHeight = true;
                rowHlg.childForceExpandWidth = false;
                rowHlg.padding = new RectOffset(8, 8, 2, 2);

                CreateTableCell(rowObj.transform, Truncate(acName, 20), 170, Color.white, true);
                CreateTableCell(rowObj.transform, stats.Flights.ToString(), 60, Color.white, false, TextAlignmentOptions.Center);
                CreateTableCell(rowObj.transform, stats.AirKills.ToString(), 45, AccentBlue, false, TextAlignmentOptions.Center);
                CreateTableCell(rowObj.transform, stats.GroundKills.ToString(), 45, AccentOrange, false, TextAlignmentOptions.Center);
                CreateTableCell(rowObj.transform, kd.ToString("F2"), 55, AccentGreen, false, TextAlignmentOptions.Center);
                CreateTableCell(rowObj.transform, $"{(int)avgTime.TotalMinutes}m {avgTime.Seconds}s", 70, TextDim, false, TextAlignmentOptions.Center);
                CreateTableCell(rowObj.transform,
                    $"<color=#00FFC8>{stats.Landed}</color>/<color=#FFB833>{stats.Ejected}</color>/<color=#FF5555>{stats.ShotDown}</color>",
                    110, Color.white, false, TextAlignmentOptions.Right);

                var btn = rowObj.GetComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    activeAircraftFilter = string.Equals(activeAircraftFilter, acName, StringComparison.OrdinalIgnoreCase) ? null : acName;
                    UpdateDashboardData(StatsStorage.LoadAll(), font);
                    SwitchTab("flights"); // сразу показываем отфильтрованный список вылетов
                });
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
