using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CloseEncounters.Core;

namespace CloseEncounters.UI
{
    /// <summary>
    /// Post-match results overlay. Shows ONE player's full stat card at a time,
    /// filling the card with large, readable stats. Left/right arrows page between
    /// players. Styled for a polished, professional scoreboard feel: outcome banner,
    /// winner ribbon, a 2-column grid of big stat tiles, and a hero accuracy meter.
    /// Fades + scales in while the game is paused.
    /// </summary>
    public class ResultsUI : MonoBehaviour
    {
        public enum Outcome { Victory, Defeated, Draw }

        // --- Theme ---
        private static readonly Color COLOR_BG        = new Color(0.03f, 0.04f, 0.08f, 0.78f);
        private static readonly Color COLOR_PANEL     = new Color(0.082f, 0.090f, 0.165f, 0.98f); // deep navy
        private static readonly Color COLOR_PANEL2    = new Color(0.118f, 0.133f, 0.227f, 1f);    // tile fill
        private static readonly Color COLOR_ACCENT    = new Color(0.91f, 0.27f, 0.38f, 1f);       // #e94560
        private static readonly Color COLOR_SECONDARY = new Color(0.06f, 0.2f, 0.38f, 1f);        // #0f3460
        private static readonly Color COLOR_TEXT      = new Color(0.96f, 0.96f, 0.98f, 1f);
        private static readonly Color COLOR_GREEN     = new Color(0.31f, 0.8f, 0.64f, 1f);        // #4ecca3
        private static readonly Color COLOR_YELLOW    = new Color(0.94f, 0.75f, 0.25f, 1f);       // #f0c040
        private static readonly Color COLOR_RED       = COLOR_ACCENT;
        private static readonly Color COLOR_DIM       = new Color(0.60f, 0.63f, 0.74f, 1f);
        private static readonly Color COLOR_HEADERBAR = new Color(0.055f, 0.06f, 0.12f, 1f);

        // --- Refs ---
        private Canvas _canvas;
        private CanvasGroup _rootGroup;
        private RectTransform _cardRect;
        private TMP_Text _headingText;
        private TMP_Text _winnerText;

        // Per-player content (rebuilt when paging)
        private Transform _playerContent;        // container the current player's card body lives in
        private CanvasGroup _playerGroup;         // for cross-fade between players
        private TMP_Text _playerNameText;
        private TMP_Text _playerTagText;          // "Player 1 of 3" + AI/You tag
        private Button _prevButton;
        private Button _nextButton;

        private List<StatRow> _rows = new List<StatRow>();
        private int _currentIndex;
        private float _matchTimeSeconds;
        private string _winnerName;               // so ShowPlayer can highlight the winner

        // --- Animation ---
        private const float FadeDuration = 0.4f;
        private const float ScaleFrom = 0.92f;

        // Fixed card size — large, cinematic.
        private static readonly Vector2 CardSize = new Vector2(1120f, 860f);

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

            StartCoroutine(AnimateIn());
        }

        // =================================================================
        // Public API (signature unchanged — ArenaManager calls this)
        // =================================================================

        public void SetResults(Outcome outcome, string winnerName, List<StatRow> rows,
            float matchTimeSeconds = 0f)
        {
            _rows = rows ?? new List<StatRow>();
            _matchTimeSeconds = matchTimeSeconds;
            _winnerName = winnerName;

            // Outcome banner
            switch (outcome)
            {
                case Outcome.Victory:
                    _headingText.text = "VICTORY";
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

            _winnerText.text = string.IsNullOrEmpty(winnerName)
                ? "No clear winner"
                : $"{winnerName} takes the match";

            // Start on the winner's card if we can find them by name, else first.
            _currentIndex = 0;
            if (!string.IsNullOrEmpty(winnerName))
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i].playerName == winnerName) { _currentIndex = i; break; }
                }
            }

            UpdateArrows();
            ShowPlayer(_currentIndex, instant: true);
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
        // Layout — the card shell (banner, winner ribbon, paged body, nav, buttons)
        // =================================================================

        private void BuildCard()
        {
            var card = CreateUIObject("Card", _rootGroup.transform);
            _cardRect = Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, CardSize, new Vector2(0.5f, 0.5f));
            card.AddComponent<Image>().color = COLOR_PANEL;

            var outline = card.AddComponent<Outline>();
            outline.effectColor = COLOR_ACCENT;
            outline.effectDistance = new Vector2(2f, -2f);
            _cardRect.localScale = Vector3.one * ScaleFrom;

            // ── Outcome banner (accent bar across the top) ──
            var banner = CreateUIObject("Banner", card.transform);
            Anchor(banner, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -64f), new Vector2(0f, 128f), new Vector2(0.5f, 1f));
            banner.AddComponent<Image>().color = COLOR_HEADERBAR;

            // Heading fills the banner but leaves a gutter at the bottom for
            // the winner subtitle.
            _headingText = AddText(banner.transform, "VICTORY", 72f, COLOR_GREEN,
                TextAlignmentOptions.Center, FontStyles.Bold);
            StretchFull(_headingText.gameObject);
            _headingText.rectTransform.offsetMin = new Vector2(0f, 36f);
            _headingText.characterSpacing = 6f;

            // Winner subtitle sits in the bottom strip of the banner.
            _winnerText = AddText(banner.transform, "", 24f, COLOR_DIM,
                TextAlignmentOptions.Center, FontStyles.Normal);
            StretchFull(_winnerText.gameObject);
            _winnerText.rectTransform.offsetMax = new Vector2(0f, -76f);

            // Accent underline beneath the banner
            var underline = CreateUIObject("Underline", card.transform);
            Anchor(underline, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -128f), new Vector2(0f, 3f), new Vector2(0.5f, 1f));
            underline.AddComponent<Image>().color = COLOR_ACCENT;

            // ── Player name + pager tag row ──
            var nameRow = CreateUIObject("NameRow", card.transform);
            Anchor(nameRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -182f), new Vector2(0f, 90f), new Vector2(0.5f, 1f));

            _playerNameText = AddText(nameRow.transform, "", 52f, COLOR_TEXT,
                TextAlignmentOptions.Center, FontStyles.Bold);
            StretchFull(_playerNameText.gameObject);
            _playerNameText.rectTransform.offsetMin = new Vector2(0f, 30f);

            _playerTagText = AddText(nameRow.transform, "", 18f, COLOR_ACCENT,
                TextAlignmentOptions.Center, FontStyles.Bold);
            StretchFull(_playerTagText.gameObject);
            _playerTagText.rectTransform.offsetMax = new Vector2(0f, -58f);

            // ── Paged player body (stat tiles get rebuilt here) ──
            var bodyGo = CreateUIObject("PlayerBody", card.transform);
            // Leave room: top banner+name (~278px) and bottom nav/buttons (~110px)
            Anchor(bodyGo, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), Vector2.zero, new Vector2(0.5f, 0.5f));
            var bodyRt = bodyGo.GetComponent<RectTransform>();
            bodyRt.offsetMin = new Vector2(56f, 110f);   // bottom inset for buttons
            bodyRt.offsetMax = new Vector2(-56f, -278f); // top inset under name row
            _playerGroup = bodyGo.AddComponent<CanvasGroup>();
            _playerContent = bodyGo.transform;

            // ── Left/right pager arrows (along card sides, vertically centered) ──
            _prevButton = CreateArrowButton(card.transform, "◀", true, OnPrev);
            _nextButton = CreateArrowButton(card.transform, "▶", false, OnNext);

            // ── Action buttons (bottom row) ──
            BuildButtons(card.transform);
        }

        // =================================================================
        // Per-player card body — large stat tiles + hero accuracy meter
        // =================================================================

        private void ShowPlayer(int index, bool instant = false)
        {
            if (_rows.Count == 0) return;
            _currentIndex = Mathf.Clamp(index, 0, _rows.Count - 1);
            var row = _rows[_currentIndex];

            // Name + pager tag. The match winner's name is highlighted green.
            _playerNameText.text = string.IsNullOrEmpty(row.playerName)
                ? $"Pilot {_currentIndex + 1}" : row.playerName;
            bool isWinner = !string.IsNullOrEmpty(_winnerName)
                && row.playerName == _winnerName;
            _playerNameText.color = isWinner ? COLOR_GREEN : COLOR_TEXT;

            string winnerTag = isWinner ? "<color=#4ecca3>WINNER</color>    •    " : "";
            string survivedTag = row.survived
                ? "<color=#4ecca3>SURVIVED</color>"
                : "<color=#e94560>DESTROYED</color>";
            _playerTagText.text = _rows.Count > 1
                ? $"{winnerTag}PILOT {_currentIndex + 1} OF {_rows.Count}    •    {survivedTag}"
                : $"{winnerTag}{survivedTag}";

            // Rebuild tiles
            for (int i = _playerContent.childCount - 1; i >= 0; i--)
                DestroyImmediate(_playerContent.GetChild(i).gameObject);

            BuildStatGrid(row);

            UpdateArrows();

            if (instant)
            {
                if (_playerGroup != null) _playerGroup.alpha = 1f;
            }
            else
            {
                StartCoroutine(CrossFadeBody());
            }
        }

        private void BuildStatGrid(StatRow row)
        {
            // A 2-column grid of large stat tiles fills the card body.
            float accuracy = row.shotsFired > 0
                ? (float)row.shotsHit / row.shotsFired * 100f : 0f;

            var grid = CreateUIObject("Grid", _playerContent);
            StretchFull(grid);
            var glg = grid.AddComponent<GridLayoutGroup>();
            // 2 cols x 4 rows in the ~1008x472 body: 488*2 + 24 = 1000 wide,
            // 100*4 + 16*3 = 448 tall (leaves ~24px vertical breathing room).
            glg.cellSize = new Vector2(488f, 100f);
            glg.spacing = new Vector2(24f, 16f);
            glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.childAlignment = TextAnchor.MiddleCenter; // center the block vertically
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;

            // Headline combat stats as big tiles.
            AddStatTile(grid.transform, "DAMAGE DEALT", row.damageDealt.ToString("N0"), COLOR_GREEN);
            AddStatTile(grid.transform, "DAMAGE TAKEN", row.damageReceived.ToString("N0"), COLOR_RED);
            AddStatTile(grid.transform, "PARTS DESTROYED", row.partsDestroyedOnEnemy.ToString("N0"), COLOR_TEXT);
            AddStatTile(grid.transform, "PARTS LOST", row.partsLost.ToString("N0"), COLOR_TEXT);
            AddStatTile(grid.transform, "SHOTS FIRED", row.shotsFired.ToString("N0"), COLOR_TEXT);
            AddStatTile(grid.transform, "ACCURACY", $"{accuracy:F0}%", AccuracyColor(accuracy));
            AddStatTile(grid.transform, "DISTANCE FLOWN", $"{row.distanceTraveled:N0} m", COLOR_TEXT);
            AddStatTile(grid.transform, "TOP SPEED", $"{row.topSpeed:F0} m/s", COLOR_TEXT);
        }

        private static Color AccuracyColor(float pct)
        {
            if (pct >= 50f) return COLOR_GREEN;
            if (pct >= 25f) return COLOR_YELLOW;
            return COLOR_RED;
        }

        // A single stat tile: small dim caption on top, large value below.
        private void AddStatTile(Transform parent, string caption, string value, Color valueColor)
        {
            var tile = CreateUIObject("Tile_" + caption, parent);
            tile.AddComponent<Image>().color = COLOR_PANEL2;

            // subtle left accent strip
            var strip = CreateUIObject("Strip", tile.transform);
            Anchor(strip, new Vector2(0f, 0f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(5f, 0f), new Vector2(0f, 0.5f));
            var stripRt = strip.GetComponent<RectTransform>();
            stripRt.anchoredPosition = Vector2.zero;
            stripRt.sizeDelta = new Vector2(5f, 0f);
            strip.AddComponent<Image>().color = valueColor;

            var caphGo = AddText(tile.transform, caption, 17f, COLOR_DIM,
                TextAlignmentOptions.TopLeft, FontStyles.Bold);
            var caphRt = caphGo.rectTransform;
            caphRt.anchorMin = new Vector2(0f, 0f);
            caphRt.anchorMax = new Vector2(1f, 1f);
            caphRt.offsetMin = new Vector2(26f, 0f);
            caphRt.offsetMax = new Vector2(-16f, -14f);
            caphGo.characterSpacing = 4f;

            var valGo = AddText(tile.transform, value, 46f, valueColor,
                TextAlignmentOptions.BottomLeft, FontStyles.Bold);
            var valRt = valGo.rectTransform;
            valRt.anchorMin = new Vector2(0f, 0f);
            valRt.anchorMax = new Vector2(1f, 1f);
            valRt.offsetMin = new Vector2(26f, 10f);
            valRt.offsetMax = new Vector2(-16f, -40f);
        }

        // =================================================================
        // Pager arrows
        // =================================================================

        private Button CreateArrowButton(Transform parent, string glyph, bool left,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateUIObject(left ? "PrevArrow" : "NextArrow", parent);
            float x = left ? 30f : -30f;
            Anchor(go, new Vector2(left ? 0f : 1f, 0.5f), new Vector2(left ? 0f : 1f, 0.5f),
                new Vector2(x, -10f), new Vector2(54f, 54f), new Vector2(0.5f, 0.5f));

            var img = go.AddComponent<Image>();
            img.color = COLOR_PANEL2;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.highlightedColor = COLOR_ACCENT;
            c.pressedColor = new Color(COLOR_ACCENT.r * 0.7f, COLOR_ACCENT.g * 0.7f, COLOR_ACCENT.b * 0.7f);
            c.disabledColor = new Color(COLOR_PANEL2.r, COLOR_PANEL2.g, COLOR_PANEL2.b, 0.35f);
            btn.colors = c;
            btn.onClick.AddListener(onClick);

            var lbl = AddText(go.transform, glyph, 28f, COLOR_TEXT,
                TextAlignmentOptions.Center, FontStyles.Bold);
            StretchFull(lbl.gameObject);

            return btn;
        }

        private void UpdateArrows()
        {
            bool multi = _rows.Count > 1;
            if (_prevButton != null)
            {
                _prevButton.gameObject.SetActive(multi);
                _prevButton.interactable = _currentIndex > 0;
            }
            if (_nextButton != null)
            {
                _nextButton.gameObject.SetActive(multi);
                _nextButton.interactable = _currentIndex < _rows.Count - 1;
            }
        }

        private void OnPrev() { if (_currentIndex > 0) ShowPlayer(_currentIndex - 1); }
        private void OnNext() { if (_currentIndex < _rows.Count - 1) ShowPlayer(_currentIndex + 1); }

        // =================================================================
        // Action buttons (Rematch, Lobby, Main Menu)
        // =================================================================

        private void BuildButtons(Transform card)
        {
            var btnRow = CreateUIObject("ButtonRow", card);
            Anchor(btnRow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(560f, 52f), new Vector2(0.5f, 0f));
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            CreateStyledButton(btnRow.transform, "REMATCH", COLOR_ACCENT, OnRematch);
            CreateStyledButton(btnRow.transform, "LOBBY", COLOR_SECONDARY, OnLobby);
            CreateStyledButton(btnRow.transform, "MAIN MENU",
                new Color(COLOR_SECONDARY.r * 0.7f, COLOR_SECONDARY.g * 0.7f, COLOR_SECONDARY.b * 0.7f),
                OnMainMenu);
        }

        private void OnRematch()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance != null) GameManager.Instance.GoToBuilder();
            else UnityEngine.SceneManagement.SceneManager.LoadScene("Builder");
        }

        private void OnLobby()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance != null) GameManager.Instance.ReturnToLobby();
        }

        private void OnMainMenu()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance != null) GameManager.Instance.ReturnToMainMenu();
        }

        // =================================================================
        // Animation
        // =================================================================

        private IEnumerator AnimateIn()
        {
            float elapsed = 0f;
            while (elapsed < FadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / FadeDuration);
                float ease = 1f + 1.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f);
                _rootGroup.alpha = Mathf.Clamp01(t / 0.6f);
                _cardRect.localScale = Vector3.one * Mathf.Lerp(ScaleFrom, 1f, ease);
                yield return null;
            }
            _rootGroup.alpha = 1f;
            _cardRect.localScale = Vector3.one;
        }

        private IEnumerator CrossFadeBody()
        {
            if (_playerGroup == null) yield break;
            float t = 0f;
            const float dur = 0.18f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                _playerGroup.alpha = Mathf.Clamp01(t / dur);
                yield return null;
            }
            _playerGroup.alpha = 1f;
        }

        private void Update()
        {
            // Keyboard paging for convenience.
            if (_rows.Count > 1)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) OnPrev();
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) OnNext();
            }
        }

        // =================================================================
        // Data class (unchanged — ArenaManager populates this)
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
            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.highlightedColor = new Color(bgColor.r + 0.12f, bgColor.g + 0.12f, bgColor.b + 0.12f);
            c.pressedColor = new Color(bgColor.r * 0.7f, bgColor.g * 0.7f, bgColor.b * 0.7f);
            btn.colors = c;
            btn.onClick.AddListener(onClick);

            var txt = AddText(go.transform, label, 19f, COLOR_TEXT,
                TextAlignmentOptions.Center, FontStyles.Bold);
            StretchFull(txt.gameObject);
            txt.characterSpacing = 2f;
        }

        private static TMP_Text AddText(Transform parent, string text, float size, Color color,
            TextAlignmentOptions align, FontStyles style)
        {
            var go = CreateUIObject("Text", parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            tmp.raycastTarget = false;
            return tmp;
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
