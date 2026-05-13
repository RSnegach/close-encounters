using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CloseEncounters.Core;

namespace CloseEncounters.UI
{
    /// <summary>
    /// Post-match results overlay matching Godot's results_screen.gd exactly:
    /// dark 75% backdrop, styled card with red accent border + shadow,
    /// outcome heading, winner name, scrollable 2-column stats grid,
    /// and three navigation buttons. Fades + scales in while paused.
    /// </summary>
    public class ResultsUI : MonoBehaviour
    {
        public enum Outcome { Victory, Defeated, Draw }

        // --- Theme (matching Godot) ---
        private static readonly Color COLOR_BG     = new Color(0.05f, 0.05f, 0.1f, 0.55f);
        private static readonly Color COLOR_PANEL  = new Color(0.071f, 0.071f, 0.165f, 0.85f); // #12122a
        private static readonly Color COLOR_ACCENT = new Color(0.91f, 0.27f, 0.38f, 1f);    // #e94560
        private static readonly Color COLOR_SECONDARY = new Color(0.06f, 0.2f, 0.38f, 1f);  // #0f3460
        private static readonly Color COLOR_TEXT   = new Color(0.93f, 0.93f, 0.93f, 1f);    // #eeeeee
        private static readonly Color COLOR_GREEN  = new Color(0.31f, 0.8f, 0.64f, 1f);     // #4ecca3
        private static readonly Color COLOR_YELLOW = new Color(0.94f, 0.75f, 0.25f, 1f);    // #f0c040
        private static readonly Color COLOR_RED    = COLOR_ACCENT;
        private static readonly Color COLOR_DIM    = new Color(0.53f, 0.53f, 0.53f, 1f);    // #888888

        // --- Refs ---
        private Canvas _canvas;
        private CanvasGroup _rootGroup;
        private RectTransform _cardRect;
        private TMP_Text _headingText;
        private TMP_Text _winnerText;
        private Transform _tableContent;

        // --- Animation (matching Godot: 0.4s, scale 0.9→1.0) ---
        private const float FadeDuration = 0.4f;
        private const float ScaleFrom = 0.9f;

        // =================================================================
        // Init
        // =================================================================

        public void Initialize()
        {
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();

            Time.timeScale = 0f;

            BuildOverlay();
            BuildCard();
            BuildCardContent();
            BuildButtons();

            // Don't call SetResults here — ArenaManager calls it with real data
            StartCoroutine(AnimateIn());
        }

        // =================================================================
        // Public API
        // =================================================================

        public void SetResults(Outcome outcome, string winnerName, List<StatRow> rows,
            float matchTimeSeconds = 0f)
        {
            // Heading
            switch (outcome)
            {
                case Outcome.Victory:
                    _headingText.text = "VICTORY!";
                    _headingText.color = COLOR_GREEN;
                    break;
                case Outcome.Defeated:
                    _headingText.text = "DEFEATED";
                    _headingText.color = COLOR_RED;
                    break;
                default:
                    _headingText.text = "DRAW";
                    _headingText.color = COLOR_YELLOW;
                    break;
            }

            _winnerText.text = string.IsNullOrEmpty(winnerName) ? "No winner"
                : $"{winnerName} wins";

            // Clear stats immediately (not deferred) so new content doesn't conflict
            for (int i = _tableContent.childCount - 1; i >= 0; i--)
                DestroyImmediate(_tableContent.GetChild(i).gameObject);

            // Match time (always first, matching Godot)
            if (matchTimeSeconds > 0f)
            {
                int mins = (int)matchTimeSeconds / 60;
                int secs = (int)matchTimeSeconds % 60;
                AddStatRow("Match Time", $"{mins}:{secs:D2}");
            }

            AddSpacer(4f);

            // Per-player stats
            for (int p = 0; p < rows.Count; p++)
            {
                var row = rows[p];

                if (rows.Count > 1)
                {
                    if (p > 0) AddSpacer(8f);
                    AddStatRow(row.playerName, "", bold: true);
                    AddSeparator();
                }

                AddStatRow("Damage Dealt", row.damageDealt.ToString());
                AddStatRow("Damage Received", row.damageReceived.ToString());
                AddStatRow("Shots Fired", row.shotsFired.ToString());
                AddStatRow("Shots Hit", row.shotsHit.ToString());

                float accuracy = row.shotsFired > 0
                    ? (float)row.shotsHit / row.shotsFired * 100f : 0f;
                AddStatRow("Accuracy", $"{accuracy:F1}%");

                AddStatRow("Parts Destroyed", row.partsDestroyedOnEnemy.ToString());
                AddStatRow("Parts Lost", row.partsLost.ToString());
                AddStatRow("Distance Traveled", $"{row.distanceTraveled:F0} m");
                AddStatRow("Top Speed", $"{row.topSpeed:F1} m/s");

                AddStatRow("Survived",
                    row.survived ? "Yes" : "No",
                    valueColor: row.survived ? COLOR_GREEN : COLOR_RED);
            }
        }

        // =================================================================
        // Layout — Dark backdrop
        // =================================================================

        private void BuildOverlay()
        {
            var overlay = CreateUIObject("Overlay", _canvas.transform);
            StretchFull(overlay);
            var img = overlay.AddComponent<Image>();
            img.color = COLOR_BG;
            img.raycastTarget = true;
            _rootGroup = overlay.AddComponent<CanvasGroup>();
            _rootGroup.alpha = 0f;
        }

        // =================================================================
        // Layout — Styled card (matching Godot PanelContainer)
        // =================================================================

        private void BuildCard()
        {
            // Single card panel — wide, semi-transparent, red outline border
            var card = CreateUIObject("Card", _rootGroup.transform);
            _cardRect = Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(700f, 620f), new Vector2(0.5f, 0.5f));

            card.AddComponent<Image>().color = COLOR_PANEL;

            var outline = card.AddComponent<Outline>();
            outline.effectColor = COLOR_ACCENT;
            outline.effectDistance = new Vector2(2f, -2f);

            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 14, 14);
            layout.spacing = 0f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childScaleWidth = false;
            layout.childScaleHeight = false;

            _cardRect.localScale = Vector3.one * ScaleFrom;
        }

        // =================================================================
        // Card content: heading, winner, stats heading, stats scroll, buttons
        // =================================================================

        private void BuildCardContent()
        {
            var card = _cardRect; // Card is the layout root itself

            // Result heading
            var headingGo = CreateUIObject("Heading", card);
            headingGo.AddComponent<LayoutElement>().preferredHeight = 50f;
            _headingText = headingGo.AddComponent<TextMeshProUGUI>();
            _headingText.text = "RESULTS";
            _headingText.fontSize = 42f;
            _headingText.color = COLOR_TEXT;
            _headingText.alignment = TextAlignmentOptions.Center;
            _headingText.fontStyle = FontStyles.Bold;

            // Winner name
            var winnerGo = CreateUIObject("WinnerName", card);
            winnerGo.AddComponent<LayoutElement>().preferredHeight = 24f;
            _winnerText = winnerGo.AddComponent<TextMeshProUGUI>();
            _winnerText.text = "";
            _winnerText.fontSize = 18f;
            _winnerText.color = COLOR_DIM;
            _winnerText.alignment = TextAlignmentOptions.Center;

            // "Combat Statistics" heading (accent red)
            var shGo = CreateUIObject("StatsHeading", card);
            shGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var shTmp = shGo.AddComponent<TextMeshProUGUI>();
            shTmp.text = "Combat Statistics";
            shTmp.fontSize = 18f;
            shTmp.color = COLOR_ACCENT;
            shTmp.alignment = TextAlignmentOptions.Center;

            // Stats container (no scroll — all stats visible at once)
            var statsGo = CreateUIObject("StatsContent", card);
            var vlg = statsGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 1f;
            vlg.padding = new RectOffset(0, 0, 2, 2);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            _tableContent = statsGo.transform;
        }

        // =================================================================
        // Buttons (matching Godot: Rematch, Lobby, Main Menu in a row)
        // =================================================================

        private void BuildButtons()
        {
            AddLayoutSpacer(_cardRect, 12f);

            var btnRow = CreateUIObject("ButtonRow", _cardRect);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 52f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;

            CreateStyledButton(btnRow.transform, "Rematch", COLOR_ACCENT, OnRematch);
            CreateStyledButton(btnRow.transform, "Lobby", COLOR_SECONDARY, OnLobby);
            CreateStyledButton(btnRow.transform, "Main Menu",
                new Color(COLOR_SECONDARY.r * 0.7f, COLOR_SECONDARY.g * 0.7f, COLOR_SECONDARY.b * 0.7f),
                OnMainMenu);
        }

        private void OnRematch()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance != null)
                GameManager.Instance.GoToBuilder();
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene("Builder");
        }

        private void OnLobby()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance != null)
                GameManager.Instance.ReturnToLobby();
        }

        private void OnMainMenu()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance != null)
                GameManager.Instance.ReturnToMainMenu();
        }

        // =================================================================
        // Animation (Godot: 0.4s, scale 0.9→1.0, TRANS_BACK ease)
        // =================================================================

        private IEnumerator AnimateIn()
        {
            float elapsed = 0f;
            while (elapsed < FadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / FadeDuration);

                // Back ease-out (slight overshoot, matching Godot TRANS_BACK)
                float ease = 1f + 1.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f);

                _rootGroup.alpha = Mathf.Clamp01(t / 0.6f); // fade faster than scale
                _cardRect.localScale = Vector3.one * Mathf.Lerp(ScaleFrom, 1f, ease);

                yield return null;
            }

            _rootGroup.alpha = 1f;
            _cardRect.localScale = Vector3.one;
        }

        private void Update()
        {
            // Escape during Results does nothing
        }

        // =================================================================
        // Stat row helpers
        // =================================================================

        public void AddStatRow(string label, string value, Color? valueColor = null, bool bold = false)
        {
            var rowGo = CreateUIObject("Stat", _tableContent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 18f;

            bool isHeader = string.IsNullOrEmpty(value);

            var labelGo = CreateUIObject("Label", rowGo.transform);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = new Vector2(isHeader ? 1f : 0.55f, 1f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var lTmp = labelGo.AddComponent<TextMeshProUGUI>();
            lTmp.text = isHeader ? label : (label + ":");
            lTmp.fontSize = 13f;
            lTmp.color = isHeader ? COLOR_TEXT : COLOR_DIM;
            lTmp.fontStyle = (isHeader || bold) ? FontStyles.Bold : FontStyles.Normal;
            lTmp.alignment = isHeader ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;

            if (!isHeader)
            {
                var valGo = CreateUIObject("Value", rowGo.transform);
                var valRt = valGo.GetComponent<RectTransform>();
                valRt.anchorMin = new Vector2(0.55f, 0f);
                valRt.anchorMax = Vector2.one;
                valRt.offsetMin = Vector2.zero;
                valRt.offsetMax = Vector2.zero;
                var vTmp = valGo.AddComponent<TextMeshProUGUI>();
                vTmp.text = value;
                vTmp.fontSize = 13f;
                vTmp.color = valueColor ?? COLOR_TEXT;
                vTmp.alignment = TextAlignmentOptions.MidlineRight;
            }
        }

        private void AddSpacer(float height)
        {
            var go = CreateUIObject("Spacer", _tableContent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

        private void AddSeparator()
        {
            var go = CreateUIObject("Sep", _tableContent);
            go.AddComponent<LayoutElement>().preferredHeight = 2f;
            var img = go.AddComponent<Image>();
            img.color = new Color(COLOR_ACCENT.r, COLOR_ACCENT.g, COLOR_ACCENT.b, 0.3f);
        }

        // =================================================================
        // Data class
        // =================================================================

        [System.Serializable]
        public class StatRow
        {
            public string playerName;
            public bool survived;
            public int damageDealt;
            public int damageReceived;
            public int shotsFired;
            public int shotsHit;
            public int partsDestroyedOnEnemy;
            public int partsLost;
            public float distanceTraveled;
            public float topSpeed;
        }

        // =================================================================
        // UI helpers
        // =================================================================

        private void CreateStyledButton(Transform parent, string label, Color bgColor,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateUIObject("Btn_" + label, parent);
            go.AddComponent<LayoutElement>().preferredWidth = 140f;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.highlightedColor = new Color(bgColor.r + 0.12f, bgColor.g + 0.12f, bgColor.b + 0.12f);
            c.pressedColor = new Color(bgColor.r * 0.7f, bgColor.g * 0.7f, bgColor.b * 0.7f);
            btn.colors = c;
            btn.onClick.AddListener(onClick);

            var txtGo = CreateUIObject("Label", go.transform);
            StretchFull(txtGo);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 18f;
            txt.color = COLOR_TEXT;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;
        }

        private static void AddLayoutSpacer(Transform parent, float height)
        {
            var go = CreateUIObject("Spacer", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static RectTransform Anchor(GameObject go, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 sizeDelta, Vector2 pivot)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return rt;
        }

        private static RectTransform StretchFull(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }
    }
}
