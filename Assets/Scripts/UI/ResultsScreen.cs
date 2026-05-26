using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
        private static readonly Color COLOR_BG     = new Color(0.05f, 0.05f, 0.1f, 0.75f);
        private static readonly Color COLOR_PANEL  = new Color(0.071f, 0.071f, 0.165f, 0.92f); // #12122a
        private static readonly Color COLOR_PANEL_BOTTOM = new Color(0.04f, 0.04f, 0.10f, 0.95f);
        private static readonly Color COLOR_ACCENT = new Color(0.91f, 0.27f, 0.38f, 1f);    // #e94560
        private static readonly Color COLOR_SECONDARY = new Color(0.06f, 0.2f, 0.38f, 1f);  // #0f3460
        private static readonly Color COLOR_TEXT   = new Color(0.93f, 0.93f, 0.93f, 1f);    // #eeeeee
        private static readonly Color COLOR_GREEN  = new Color(0.31f, 0.8f, 0.64f, 1f);     // #4ecca3
        private static readonly Color COLOR_YELLOW = new Color(0.94f, 0.75f, 0.25f, 1f);    // #f0c040
        private static readonly Color COLOR_RED    = COLOR_ACCENT;
        private static readonly Color COLOR_DIM    = new Color(0.53f, 0.53f, 0.53f, 1f);    // #888888
        private static readonly Color COLOR_BLUEGRAY = new Color(0.45f, 0.55f, 0.7f, 1f);
        private static readonly Color COLOR_BAR_BG  = new Color(0.15f, 0.15f, 0.20f, 0.6f);

        // --- Refs ---
        private Canvas _canvas;
        private CanvasGroup _rootGroup;
        private RectTransform _cardRect;
        private TMP_Text _headingText;
        private TMP_Text _headingShadow;
        private RectTransform _headingUnderline;
        private TMP_Text _winnerText;
        private TMP_Text _summaryText;
        private Transform _tableContent;
        private RectTransform _trophyRect;
        private GameObject _trophyGo;

        // --- Animation (matching Godot: 0.4s, scale 0.9→1.0) ---
        private const float FadeDuration = 0.4f;
        private const float ScaleFrom = 0.9f;
        private const float RowStagger = 0.05f;
        private const float BarFillDuration = 0.6f;

        private readonly List<Coroutine> _pendingAnims = new List<Coroutine>();
        private int _rowRevealIndex;

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
            // Cancel any in-flight row animations from a prior SetResults
            foreach (var c in _pendingAnims) if (c != null) StopCoroutine(c);
            _pendingAnims.Clear();
            _rowRevealIndex = 0;

            // Heading
            Color outcomeColor;
            switch (outcome)
            {
                case Outcome.Victory:
                    _headingText.text = "VICTORY!";
                    outcomeColor = COLOR_GREEN;
                    break;
                case Outcome.Defeated:
                    _headingText.text = "DEFEATED";
                    outcomeColor = COLOR_RED;
                    break;
                default:
                    _headingText.text = "DRAW";
                    outcomeColor = COLOR_YELLOW;
                    break;
            }
            _headingText.color = outcomeColor;
            if (_headingShadow != null) _headingShadow.text = _headingText.text;

            // Underline color + slide-in
            if (_headingUnderline != null)
            {
                var ulImg = _headingUnderline.GetComponent<Image>();
                if (ulImg != null) ulImg.color = outcomeColor;
                _pendingAnims.Add(StartCoroutine(AnimateUnderline(_headingUnderline)));
            }

            // Trophy: only for victory
            if (_trophyGo != null)
            {
                bool showTrophy = outcome == Outcome.Victory;
                _trophyGo.SetActive(showTrophy);
                if (showTrophy)
                    _pendingAnims.Add(StartCoroutine(PulseTrophy(_trophyRect)));
            }

            _winnerText.text = string.IsNullOrEmpty(winnerName) ? "No winner"
                : $"{winnerName} wins";

            // Summary line (arena · vehicles · time)
            if (_summaryText != null)
            {
                int mins = (int)matchTimeSeconds / 60;
                int secs = (int)matchTimeSeconds % 60;
                string arenaName = TryGetArenaName();
                string vehicleStr = $"{rows.Count} Vehicle{(rows.Count == 1 ? "" : "s")}";
                string timeStr = $"{mins:D2}:{secs:D2}";
                if (!string.IsNullOrEmpty(arenaName))
                    _summaryText.text = $"{arenaName}  ·  {vehicleStr}  ·  {timeStr}";
                else
                    _summaryText.text = $"{vehicleStr}  ·  {timeStr}";
            }

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

                // Key stats — value-bar style
                AddStatBar("Damage Dealt", row.damageDealt, 5000f, BarTier.DamageParts,
                    row.damageDealt.ToString());

                AddStatRow("Damage Received", row.damageReceived.ToString());
                AddStatRow("Shots Fired", row.shotsFired.ToString());
                AddStatRow("Shots Hit", row.shotsHit.ToString());

                float accuracy = row.shotsFired > 0
                    ? (float)row.shotsHit / row.shotsFired * 100f : 0f;
                AddStatBar("Accuracy", accuracy, 100f, BarTier.Accuracy,
                    $"{accuracy:F1}%");

                AddStatBar("Parts Destroyed", row.partsDestroyedOnEnemy, 30f, BarTier.DamageParts,
                    row.partsDestroyedOnEnemy.ToString());

                AddStatRow("Parts Lost", row.partsLost.ToString());
                AddStatRow("Distance Traveled", $"{row.distanceTraveled:F0} m");

                AddStatBar("Top Speed", row.topSpeed, 80f, BarTier.TopSpeed,
                    $"{row.topSpeed:F1} m/s");

                AddStatRow("Survived",
                    row.survived ? "Yes" : "No",
                    valueColor: row.survived ? COLOR_GREEN : COLOR_RED);
            }
        }

        private string TryGetArenaName()
        {
            var arenaType = System.Type.GetType("CloseEncounters.Core.ArenaManager, Assembly-CSharp")
                ?? System.Type.GetType("ArenaManager, Assembly-CSharp");
            if (arenaType == null) return null;
            var instProp = arenaType.GetProperty("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (instProp == null) return null;
            var inst = instProp.GetValue(null);
            if (inst == null) return null;

            string[] candidates = { "ArenaName", "CurrentArenaName", "DisplayName", "arenaName" };
            foreach (var name in candidates)
            {
                var p = arenaType.GetProperty(name);
                if (p != null) { var v = p.GetValue(inst) as string; if (!string.IsNullOrEmpty(v)) return v; }
                var f = arenaType.GetField(name);
                if (f != null) { var v = f.GetValue(inst) as string; if (!string.IsNullOrEmpty(v)) return v; }
            }
            return null;
        }

        // =================================================================
        // Layout — Dark backdrop + vignette
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

            // Vignette: radial-darkening texture stretched over the screen
            var vignetteGo = CreateUIObject("Vignette", _rootGroup.transform);
            StretchFull(vignetteGo);
            var vImg = vignetteGo.AddComponent<Image>();
            vImg.sprite = CreateVignetteSprite(256);
            vImg.color = new Color(0f, 0f, 0f, 0.55f);
            vImg.raycastTarget = false;
            vImg.type = Image.Type.Simple;
        }

        private static Sprite CreateVignetteSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float cx = (size - 1) * 0.5f;
            float cy = (size - 1) * 0.5f;
            float maxR = Mathf.Sqrt(cx * cx + cy * cy);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r * 1.1f - 0.1f));
                    pixels[y * size + x] = new Color(0f, 0f, 0f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // =================================================================
        // Layout — Styled card (matching Godot PanelContainer)
        // =================================================================

        private void BuildCard()
        {
            var card = CreateUIObject("Card", _rootGroup.transform);
            _cardRect = Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(720f, 660f), new Vector2(0.5f, 0.5f));

            // Bottom solid color (back-most layer)
            card.AddComponent<Image>().color = COLOR_PANEL_BOTTOM;

            // Gradient overlay (top-left → bottom-right) on top of the bottom color
            var gradGo = CreateUIObject("CardGradient", card.transform);
            StretchFull(gradGo);
            var gradImg = gradGo.AddComponent<Image>();
            gradImg.sprite = CreateGradientSprite();
            gradImg.color = Color.white;
            gradImg.raycastTarget = false;

            // Soft outer glow outline
            var glow = card.AddComponent<Outline>();
            glow.effectColor = new Color(COLOR_ACCENT.r, COLOR_ACCENT.g, COLOR_ACCENT.b, 0.3f);
            glow.effectDistance = new Vector2(6f, -6f);

            // Crisp inner outline
            var outline = card.AddComponent<Outline>();
            outline.effectColor = COLOR_ACCENT;
            outline.effectDistance = new Vector2(3f, -3f);

            // Optional 1px highlight stroke (skipped — adds risk for marginal gain)

            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 16, 16);
            layout.spacing = 0f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childScaleWidth = false;
            layout.childScaleHeight = false;

            _cardRect.localScale = Vector3.one * ScaleFrom;
        }

        private static Sprite CreateGradientSprite()
        {
            // 2x2 bilinearly-filtered gradient: TL→BR
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            // top-left light, bottom-right dark
            Color tl = new Color(0.10f, 0.10f, 0.20f, 0.92f);
            Color br = new Color(0.04f, 0.04f, 0.10f, 0.95f);
            Color tr = Color.Lerp(tl, br, 0.5f);
            Color bl = Color.Lerp(tl, br, 0.5f);
            // Texture coords: (0,0) is bottom-left
            tex.SetPixel(0, 1, tl);
            tex.SetPixel(1, 1, tr);
            tex.SetPixel(0, 0, bl);
            tex.SetPixel(1, 0, br);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
        }

        // =================================================================
        // Card content: heading, winner, stats heading, stats scroll, buttons
        // =================================================================

        private void BuildCardContent()
        {
            var card = _cardRect;

            // Trophy (above heading) — hidden by default; shown only on Victory
            _trophyGo = CreateUIObject("Trophy", card);
            _trophyGo.AddComponent<LayoutElement>().preferredHeight = 64f;
            _trophyRect = _trophyGo.GetComponent<RectTransform>();
            BuildTrophy(_trophyGo.transform);
            _trophyGo.SetActive(false);

            // Heading container (so we can stack shadow + text + underline)
            var headingHolder = CreateUIObject("HeadingHolder", card);
            headingHolder.AddComponent<LayoutElement>().preferredHeight = 84f;
            var hhRt = headingHolder.GetComponent<RectTransform>();

            // Shadow (behind, offset 3,-3)
            var shadowGo = CreateUIObject("HeadingShadow", headingHolder.transform);
            var shadowRt = shadowGo.GetComponent<RectTransform>();
            shadowRt.anchorMin = Vector2.zero;
            shadowRt.anchorMax = Vector2.one;
            shadowRt.offsetMin = new Vector2(3f, -3f);
            shadowRt.offsetMax = new Vector2(3f, -3f);
            _headingShadow = shadowGo.AddComponent<TextMeshProUGUI>();
            _headingShadow.text = "RESULTS";
            _headingShadow.fontSize = 64f;
            _headingShadow.color = new Color(0f, 0f, 0f, 0.6f);
            _headingShadow.alignment = TextAlignmentOptions.Center;
            _headingShadow.fontStyle = FontStyles.Bold;
            _headingShadow.characterSpacing = 6f;

            // Foreground heading
            var headingGo = CreateUIObject("Heading", headingHolder.transform);
            StretchFull(headingGo);
            _headingText = headingGo.AddComponent<TextMeshProUGUI>();
            _headingText.text = "RESULTS";
            _headingText.fontSize = 64f;
            _headingText.color = COLOR_TEXT;
            _headingText.alignment = TextAlignmentOptions.Center;
            _headingText.fontStyle = FontStyles.Bold;
            _headingText.characterSpacing = 6f;

            // Underline bar (80% width, centered, animated in)
            var underlineGo = CreateUIObject("HeadingUnderline", card);
            underlineGo.AddComponent<LayoutElement>().preferredHeight = 4f;
            _headingUnderline = underlineGo.GetComponent<RectTransform>();
            _headingUnderline.pivot = new Vector2(0.5f, 0.5f);
            var ulImg = underlineGo.AddComponent<Image>();
            ulImg.color = COLOR_ACCENT;
            // Constrained inside layout to ~80% via a child that gets resized via animation.
            // Simpler: inset the image's rect via a child with fixed anchors.
            var ulInner = CreateUIObject("UnderlineFill", underlineGo.transform);
            ulInner.GetComponent<RectTransform>().anchorMin = new Vector2(0.1f, 0f);
            ulInner.GetComponent<RectTransform>().anchorMax = new Vector2(0.9f, 1f);
            ulInner.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            ulInner.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            // Hide the outer image (we'll animate the inner one)
            ulImg.color = new Color(0, 0, 0, 0);
            var ulFill = ulInner.AddComponent<Image>();
            ulFill.color = COLOR_ACCENT;
            // Re-point _headingUnderline to the inner fill so AnimateUnderline drives it
            _headingUnderline = ulInner.GetComponent<RectTransform>();

            AddLayoutSpacer(card, 4f);

            // Winner name
            var winnerGo = CreateUIObject("WinnerName", card);
            winnerGo.AddComponent<LayoutElement>().preferredHeight = 26f;
            _winnerText = winnerGo.AddComponent<TextMeshProUGUI>();
            _winnerText.text = "";
            _winnerText.fontSize = 18f;
            _winnerText.color = COLOR_DIM;
            _winnerText.alignment = TextAlignmentOptions.Center;

            // Match summary (arena · vehicles · time)
            var summaryGo = CreateUIObject("MatchSummary", card);
            summaryGo.AddComponent<LayoutElement>().preferredHeight = 20f;
            _summaryText = summaryGo.AddComponent<TextMeshProUGUI>();
            _summaryText.text = "";
            _summaryText.fontSize = 14f;
            _summaryText.color = COLOR_DIM;
            _summaryText.fontStyle = FontStyles.Italic;
            _summaryText.alignment = TextAlignmentOptions.Center;

            AddLayoutSpacer(card, 8f);

            // "Combat Statistics" heading (accent red)
            var shGo = CreateUIObject("StatsHeading", card);
            shGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var shTmp = shGo.AddComponent<TextMeshProUGUI>();
            shTmp.text = "Combat Statistics";
            shTmp.fontSize = 18f;
            shTmp.color = COLOR_ACCENT;
            shTmp.alignment = TextAlignmentOptions.Center;

            // Stats container
            var statsGo = CreateUIObject("StatsContent", card);
            var vlg = statsGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(0, 0, 2, 2);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            _tableContent = statsGo.transform;
        }

        private void BuildTrophy(Transform parent)
        {
            // Try a unicode trophy text first; many TMP fonts won't have it,
            // but we always also build the fallback yellow circle behind it.
            var fallback = CreateUIObject("TrophyFallback", parent);
            var fbRt = fallback.GetComponent<RectTransform>();
            fbRt.anchorMin = new Vector2(0.5f, 0.5f);
            fbRt.anchorMax = new Vector2(0.5f, 0.5f);
            fbRt.pivot = new Vector2(0.5f, 0.5f);
            fbRt.sizeDelta = new Vector2(60f, 60f);
            var fbImg = fallback.AddComponent<Image>();
            fbImg.sprite = CreateCircleSprite(64);
            fbImg.color = COLOR_YELLOW;

            var oneGo = CreateUIObject("TrophyOne", fallback.transform);
            StretchFull(oneGo);
            var oneTmp = oneGo.AddComponent<TextMeshProUGUI>();
            oneTmp.text = "1";
            oneTmp.fontSize = 36f;
            oneTmp.color = new Color(0.10f, 0.10f, 0.18f, 1f);
            oneTmp.alignment = TextAlignmentOptions.Center;
            oneTmp.fontStyle = FontStyles.Bold;
        }

        private static Sprite CreateCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float cx = (size - 1) * 0.5f;
            float cy = (size - 1) * 0.5f;
            float r = size * 0.5f - 1f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // =================================================================
        // Buttons
        // =================================================================

        private void BuildButtons()
        {
            AddLayoutSpacer(_cardRect, 16f);

            var btnRow = CreateUIObject("ButtonRow", _cardRect);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 64f;
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

        private IEnumerator AnimateUnderline(RectTransform fillRt)
        {
            // Slide in from left over 0.5s by animating localScale.x with pivot at left.
            fillRt.pivot = new Vector2(0f, 0.5f);
            float duration = 0.5f;
            float elapsed = 0f;
            fillRt.localScale = new Vector3(0f, 1f, 1f);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                fillRt.localScale = new Vector3(ease, 1f, 1f);
                yield return null;
            }
            fillRt.localScale = Vector3.one;
        }

        private IEnumerator PulseTrophy(RectTransform rt)
        {
            const float duration = 1.4f;
            while (rt != null && rt.gameObject.activeInHierarchy)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = elapsed / duration;
                    float s = 1f + 0.08f * Mathf.Sin(t * Mathf.PI * 2f);
                    rt.localScale = new Vector3(s, s, 1f);
                    yield return null;
                }
            }
        }

        private IEnumerator FadeInRow(CanvasGroup cg, float delay)
        {
            cg.alpha = 0f;
            float elapsed = 0f;
            while (elapsed < delay)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            elapsed = 0f;
            const float fade = 0.18f;
            while (elapsed < fade)
            {
                elapsed += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Clamp01(elapsed / fade);
                yield return null;
            }
            cg.alpha = 1f;
        }

        private IEnumerator AnimateBarFill(RectTransform fillRt, float targetWidthFraction, float delay)
        {
            float elapsed = 0f;
            while (elapsed < delay)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            elapsed = 0f;
            // Anchored: anchorMin.x=0, anchorMax.x is animated
            while (elapsed < BarFillDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / BarFillDuration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                fillRt.anchorMax = new Vector2(targetWidthFraction * ease, 1f);
                yield return null;
            }
            fillRt.anchorMax = new Vector2(targetWidthFraction, 1f);
        }

        private void Update()
        {
            // Escape during Results does nothing
        }

        // =================================================================
        // Stat row helpers
        // =================================================================

        private enum BarTier { DamageParts, Accuracy, TopSpeed }

        public void AddStatRow(string label, string value, Color? valueColor = null, bool bold = false)
        {
            var rowGo = CreateUIObject("Stat", _tableContent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 18f;
            var cg = rowGo.AddComponent<CanvasGroup>();

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

            float delay = _rowRevealIndex * RowStagger;
            _rowRevealIndex++;
            _pendingAnims.Add(StartCoroutine(FadeInRow(cg, delay)));
        }

        private void AddStatBar(string label, float value, float referenceMax, BarTier tier, string valueText)
        {
            float frac = Mathf.Clamp01(referenceMax <= 0f ? 0f : value / referenceMax);
            Color barColor = ResolveBarColor(frac, tier);

            var rowGo = CreateUIObject("StatBar", _tableContent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var cg = rowGo.AddComponent<CanvasGroup>();

            // Label (left 35%)
            var labelGo = CreateUIObject("Label", rowGo.transform);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(0.35f, 1f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = new Vector2(-6f, 0f);
            var lTmp = labelGo.AddComponent<TextMeshProUGUI>();
            lTmp.text = label;
            lTmp.fontSize = 13f;
            lTmp.color = COLOR_DIM;
            lTmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Bar holder (middle 40%)
            var barHolder = CreateUIObject("BarHolder", rowGo.transform);
            var bhRt = barHolder.GetComponent<RectTransform>();
            bhRt.anchorMin = new Vector2(0.35f, 0.2f);
            bhRt.anchorMax = new Vector2(0.75f, 0.8f);
            bhRt.offsetMin = new Vector2(0f, 0f);
            bhRt.offsetMax = new Vector2(-6f, 0f);

            // Background bar
            var bgGo = CreateUIObject("Bg", barHolder.transform);
            StretchFull(bgGo);
            bgGo.AddComponent<Image>().color = COLOR_BAR_BG;

            // Fill (anchored to left, anchorMax.x animated 0 → frac)
            var fillGo = CreateUIObject("Fill", barHolder.transform);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            fillGo.AddComponent<Image>().color = barColor;

            // Value (right 25%)
            var valGo = CreateUIObject("Value", rowGo.transform);
            var valRt = valGo.GetComponent<RectTransform>();
            valRt.anchorMin = new Vector2(0.75f, 0f);
            valRt.anchorMax = Vector2.one;
            valRt.offsetMin = Vector2.zero;
            valRt.offsetMax = Vector2.zero;
            var vTmp = valGo.AddComponent<TextMeshProUGUI>();
            vTmp.text = valueText;
            vTmp.fontSize = 13f;
            vTmp.color = COLOR_TEXT;
            vTmp.alignment = TextAlignmentOptions.MidlineRight;

            float rowDelay = _rowRevealIndex * RowStagger;
            _rowRevealIndex++;
            _pendingAnims.Add(StartCoroutine(FadeInRow(cg, rowDelay)));
            _pendingAnims.Add(StartCoroutine(AnimateBarFill(fillRt, frac, rowDelay + 0.05f)));
        }

        private static Color ResolveBarColor(float frac, BarTier tier)
        {
            switch (tier)
            {
                case BarTier.Accuracy:
                    if (frac < 0.25f) return COLOR_RED;
                    if (frac < 0.50f) return COLOR_YELLOW;
                    return COLOR_GREEN;
                case BarTier.TopSpeed:
                    if (frac < 0.25f) return COLOR_BLUEGRAY;
                    if (frac < 0.60f) return COLOR_YELLOW;
                    return COLOR_GREEN;
                default: // DamageParts
                    if (frac < 0.25f) return COLOR_RED;
                    if (frac < 0.60f) return COLOR_YELLOW;
                    return COLOR_GREEN;
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
            // Outer wrapper provides the hover-lift target rect (we don't move the button itself,
            // we move this wrapper's anchored Y).
            var wrap = CreateUIObject("Btn_" + label + "_Wrap", parent);
            wrap.AddComponent<LayoutElement>().preferredWidth = 160f;

            // Drop shadow (behind everything)
            var shadow = CreateUIObject("Shadow", wrap.transform);
            var sRt = shadow.GetComponent<RectTransform>();
            sRt.anchorMin = Vector2.zero;
            sRt.anchorMax = Vector2.one;
            sRt.offsetMin = new Vector2(0f, -4f);
            sRt.offsetMax = new Vector2(2f, -2f);
            var sImg = shadow.AddComponent<Image>();
            sImg.color = new Color(0f, 0f, 0f, 0.4f);
            sImg.raycastTarget = false;

            var go = CreateUIObject("Btn_" + label, wrap.transform);
            StretchFull(go);

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
            txt.fontSize = 20f;
            txt.color = COLOR_TEXT;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;

            // Hover lift on the wrapper RectTransform
            var hover = wrap.AddComponent<HoverLift>();
            hover.target = wrap.GetComponent<RectTransform>();
            hover.liftY = 3f;
            // EventTrigger wired by the HoverLift in Awake-equivalent (here, OnEnable)
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

        // =================================================================
        // Hover lift helper (button rect rises by liftY on pointer enter)
        // =================================================================

        private class HoverLift : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public RectTransform target;
            public float liftY = 3f;
            private Vector2 _baseAnchored;
            private bool _captured;

            public void OnPointerEnter(PointerEventData _)
            {
                if (target == null) return;
                if (!_captured) { _baseAnchored = target.anchoredPosition; _captured = true; }
                target.anchoredPosition = _baseAnchored + new Vector2(0f, liftY);
            }

            public void OnPointerExit(PointerEventData _)
            {
                if (target == null || !_captured) return;
                target.anchoredPosition = _baseAnchored;
            }
        }
    }
}
