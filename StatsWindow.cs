using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NOStatsLogger
{
    public class StatsWindow : MonoBehaviour
    {
        public static StatsWindow Instance;

        private bool isOpen = false;
        private Rect windowRect = new Rect(320, 100, 750, 520);

        private List<FlightRecord> allFlights = new List<FlightRecord>();
        private string selectedAircraft = "ALL"; // "ALL" или конкретное название самолёта

        // Текстуры для оформления в стиле HUD/Game UI
        private Texture2D bgTexture;
        private Texture2D boxTexture;
        private GUIStyle windowStyle;
        private GUIStyle headerStyle;
        private GUIStyle labelStyle;
        private GUIStyle btnStyle;
        private GUIStyle activeBtnStyle;
        private bool stylesInitialized = false;

        private void Awake()
        {
            Instance = this;
        }

        public void Toggle()
        {
            isOpen = !isOpen;
            if (isOpen)
            {
                allFlights = StatsStorage.LoadAllFlights();
            }
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            // Тёмный полупрозрачный фон
            bgTexture = MakeTex(2, 2, new Color(0.08f, 0.10f, 0.12f, 0.94f));
            boxTexture = MakeTex(2, 2, new Color(0.15f, 0.18f, 0.22f, 0.80f));

            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = bgTexture;
            windowStyle.onNormal.background = bgTexture;
            windowStyle.padding = new RectOffset(15, 15, 20, 15);
            windowStyle.border = new RectOffset(2, 2, 2, 2);

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.9f, 0.95f, 1.0f) }
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.8f, 0.85f, 0.9f) }
            };

            btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedHeight = 30,
                normal = { background = boxTexture, textColor = new Color(0.7f, 0.8f, 0.9f) }
            };

            activeBtnStyle = new GUIStyle(btnStyle);
            Texture2D activeTex = MakeTex(2, 2, new Color(0.25f, 0.45f, 0.65f, 0.9f));
            activeBtnStyle.normal.background = activeTex;
            activeBtnStyle.normal.textColor = Color.white;

            stylesInitialized = true;
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnGUI()
        {
            if (!isOpen) return;

            InitStyles();
            GUI.depth = -1000;
            windowRect = GUILayout.Window(888123, windowRect, DrawWindow, "", windowStyle);
        }

        private void DrawWindow(int windowID)
        {
            // Заголовок
            GUILayout.Label("ПАНЕЛЬ СТАТИСТИКИ ПИЛОТА", headerStyle);
            GUILayout.Space(15);

            GUILayout.BeginHorizontal();

            // --- ЛЕВАЯ КОЛОНКА: Выбор самолёта ---
            GUILayout.BeginVertical(GUILayout.Width(200));
            GUILayout.Label("<b>САМОЛЁТЫ</b>", labelStyle);
            GUILayout.Space(5);

            // Получаем уникальный список самолётов из истории
            var aircraftList = new List<string> { "ALL" };
            var uniqueInStats = allFlights.Select(f => f.Aircraft).Where(a => !string.IsNullOrEmpty(a)).Distinct();
            aircraftList.AddRange(uniqueInStats);

            foreach (var ac in aircraftList)
            {
                string title = ac == "ALL" ? "Все борты" : ac;
                bool isActive = selectedAircraft == ac;
                if (GUILayout.Button(title, isActive ? activeBtnStyle : btnStyle))
                {
                    selectedAircraft = ac;
                }
                GUILayout.Space(3);
            }
            GUILayout.EndVertical();

            GUILayout.Space(15);

            // --- ПРАВАЯ КОЛОНКА: Метрики ---
            GUILayout.BeginVertical(boxTexture != null ? GUI.skin.box : GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            
            // Фильтрация вылетов
            var filtered = selectedAircraft == "ALL" 
                ? allFlights 
                : allFlights.Where(f => f.Aircraft == selectedAircraft).ToList();

            int totalFlights = filtered.Count;
            int landed = filtered.Count(f => f.Result == FlightState.ResultLanded);
            int ejected = filtered.Count(f => f.Result == FlightState.ResultEjected);
            int shotDown = filtered.Count(f => f.Result == FlightState.ResultShotDown);
            int airKills = filtered.Sum(f => f.AircraftKills);
            int groundKills = filtered.Sum(f => f.VehicleKills);
            int totalSec = filtered.Sum(f => f.DurationSeconds);

            TimeSpan t = TimeSpan.FromSeconds(totalSec);
            string timeStr = $"{(int)t.TotalHours}ч {t.Minutes}м {t.Seconds}с";

            float survivalRate = totalFlights > 0 ? ((float)landed / totalFlights) * 100f : 0f;

            GUILayout.Space(10);
            GUILayout.Label($"<b>Выбранный борт:</b> <color=#55aaff>{(selectedAircraft == "ALL" ? "Все самолёты" : selectedAircraft)}</color>", labelStyle);
            GUILayout.Space(10);

            DrawMetricRow("Всего вылетов:", totalFlights.ToString());
            DrawMetricRow("Общий налёт:", timeStr);
            DrawMetricRow("Процент выживаемости:", $"{survivalRate:F1}%");
            
            GUILayout.Space(10);
            DrawMetricRow("Успешных посадок:", landed.ToString(), "#55ff55");
            DrawMetricRow("Катапультирований:", ejected.ToString(), "#ffaa00");
            DrawMetricRow("Сбит / Разбит:", shotDown.ToString(), "#ff5555");

            GUILayout.Space(10);
            DrawMetricRow("Воздушных фрагов:", airKills.ToString());
            DrawMetricRow("Наземных фрагов:", groundKills.ToString());
            DrawMetricRow("Всего уничтожено целей:", (airKills + groundKills).ToString(), "#ffff55");

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.Space(15);
            if (GUILayout.Button("ЗАКРЫТЬ", btnStyle, GUILayout.Height(35)))
            {
                isOpen = false;
            }

            GUI.DragWindow();
        }

        private void DrawMetricRow(string label, string val, string colorHex = "#ffffff")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(220));
            GUILayout.Label($"<color={colorHex}><b>{val}</b></color>", labelStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }
    }
}