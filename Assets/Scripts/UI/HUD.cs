using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CloseEncounters.Core;

namespace CloseEncounters.UI
{
    /// <summary>
    /// In-game heads-up display built programmatically on a Canvas.
    /// Call Setup(vehicle) after spawning the player vehicle to bind live data.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        // --- Bound vehicle ---
        private MonoBehaviour _vehicle;

        // --- Widgets ---
        private Slider _healthBar;
        private Image _healthFill;
        private TMP_Text _healthLabel;       // "HP: X / X" overlay on health bar
        private TMP_Text _speedLabel;
        private TMP_Text _ammoLabel;
        private Image _boostRingBg;
        private Image _boostRingFill;
        private TMP_Text _boostLabel;
        private RawImage _vehiclePreview;
        private Image _crosshairH;
        private Image _crosshairV;
        private Image _damageFlash;
        private TMP_Text _gameOverLabel;
        private GameObject _pausePanel;
        private TMP_Text _pauseTitle;
        private Transform _killFeedContent;  // Top-right kill feed
        private Transform _domainInfoContent; // Bottom-right domain info column
        private bool _healthbarsOn = true;

        // --- Canvas ref (set by bootstrapper or self-created) ---
        private Canvas _canvas;
        private RectTransform _canvasRect;

        // --- Crosshair offset driven externally ---
        public float reticleOffsetX;
        public float reticleOffsetY;

        // --- State ---
        private bool _isPaused;
#pragma warning disable CS0414
        private bool _isGameOver;
#pragma warning restore CS0414
        private float _flashTimer;
        private const float FlashDuration = 0.25f;
        private CloseEncounters.Combat.PlayerVehicleController _cachedPlayerCtrl;
        private CloseEncounters.Vehicle.Vehicle _cachedPlayerVehicle;
        private CloseEncounters.Combat.OutOfBoundsController _cachedPlayerOob;

        // --- OOB banner ---
        private GameObject _oobBannerGo;
        private Image _oobBannerBg;
        private TMP_Text _oobBannerLabel;
        private readonly System.Text.StringBuilder _oobSb = new System.Text.StringBuilder(64);

        private CloseEncounters.Combat.PlayerVehicleController GetPlayerCtrl()
        {
            if (_cachedPlayerCtrl == null)
                _cachedPlayerCtrl = FindAnyObjectByType<CloseEncounters.Combat.PlayerVehicleController>();
            return _cachedPlayerCtrl;
        }

        private CloseEncounters.Combat.OutOfBoundsController GetPlayerOob()
        {
            var ctrl = GetPlayerCtrl();
            if (ctrl == null) { _cachedPlayerVehicle = null; _cachedPlayerOob = null; return null; }

            if (_cachedPlayerVehicle == null || _cachedPlayerVehicle.gameObject != ctrl.gameObject)
            {
                _cachedPlayerVehicle = ctrl.GetComponent<CloseEncounters.Vehicle.Vehicle>();
                _cachedPlayerOob = null;
            }
            if (_cachedPlayerVehicle == null) return null;

            if (_cachedPlayerOob == null)
                _cachedPlayerOob = _cachedPlayerVehicle.GetOutOfBoundsController();
            return _cachedPlayerOob;
        }

        // === Programmatic build =========================================

        private void Start()
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
                _canvas = GetComponent<Canvas>();

            if (_canvas == null)
            {
                var canvasObj = new GameObject("HUD_Canvas");
                _canvas = canvasObj.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 10;
                var scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                canvasObj.AddComponent<GraphicRaycaster>();
                transform.SetParent(canvasObj.transform, false);
            }

            _canvasRect = _canvas.GetComponent<RectTransform>();

            BuildHealthBar();
            BuildBottomBar();
            BuildBoostBar();
            BuildCrosshair();
            BuildDamageFlash();
            BuildKillFeed();
            BuildGameOverLabel();
            BuildOOBBanner();
            BuildPauseMenu();

            CloseEncounters.Combat.DamageSystem.OnVehicleKilled += HandleVehicleKilled;
        }

        private void OnDestroy()
        {
            CloseEncounters.Combat.DamageSystem.OnVehicleKilled -= HandleVehicleKilled;
        }

        private void HandleVehicleKilled(CloseEncounters.Arena.VehicleRuntime victim, CloseEncounters.Arena.VehicleRuntime attacker)
        {
            string victimName = victim != null ? NameForVehicle(victim) : "?";
            string attackerName = attacker != null ? NameForVehicle(attacker) : "Environment";
            AddKillFeedEntry($"{attackerName}  \u2192  {victimName}");
        }

        private static string NameForVehicle(CloseEncounters.Arena.VehicleRuntime vr)
        {
            if (vr == null || vr.gameObject == null) return "?";
            var gm = CloseEncounters.Core.GameManager.Instance;
            if (gm != null) return gm.GetPlayerName(vr.PlayerId);
            return vr.IsAI ? $"AI {vr.PlayerId}" : $"Player {vr.PlayerId}";
        }

        // --- Out-of-bounds banner (top-center, prominent) ---

        private void BuildOOBBanner()
        {
            _oobBannerGo = CreateUIObject("OOBBanner", _canvas.transform);
            // why: top-center under the health bar; 900x70 is visible without covering crosshair
            Anchor(_oobBannerGo, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -120f), new Vector2(900f, 70f), new Vector2(0.5f, 1f));

            _oobBannerBg = _oobBannerGo.AddComponent<Image>();
            _oobBannerBg.color = new Color(0.75f, 0.05f, 0.05f, 0.85f);
            _oobBannerBg.raycastTarget = false;

            var labelGo = CreateUIObject("OOBLabel", _oobBannerGo.transform);
            StretchFull(labelGo);
            _oobBannerLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _oobBannerLabel.text = "OUT OF BOUNDS - 10.0 SECONDS TO RETURN TO COMBAT";
            _oobBannerLabel.fontSize = 28f;
            _oobBannerLabel.color = Color.white;
            _oobBannerLabel.fontStyle = FontStyles.Bold;
            _oobBannerLabel.alignment = TextAlignmentOptions.Center;
            _oobBannerLabel.raycastTarget = false;

            _oobBannerGo.SetActive(false);
        }

        private void UpdateOOBBanner()
        {
            if (_oobBannerGo == null) return;

            var ctrl = GetPlayerCtrl();
            if (ctrl == null || (_cachedPlayerVehicle != null && !_cachedPlayerVehicle.isAlive))
            {
                if (_oobBannerGo.activeSelf) _oobBannerGo.SetActive(false);
                return;
            }

            var oob = GetPlayerOob();
            if (oob == null || !oob.IsOutOfBounds)
            {
                if (_oobBannerGo.activeSelf) _oobBannerGo.SetActive(false);
                return;
            }

            if (!_oobBannerGo.activeSelf) _oobBannerGo.SetActive(true);

            float remaining = Mathf.Max(0f, oob.TimeRemaining);
            int whole = (int)remaining;
            int tenths = Mathf.Clamp((int)((remaining - whole) * 10f), 0, 9);

            _oobSb.Length = 0;
            _oobSb.Append("OUT OF BOUNDS - ");
            _oobSb.Append(whole);
            _oobSb.Append('.');
            _oobSb.Append(tenths);
            _oobSb.Append(" SECONDS TO RETURN TO COMBAT");
            _oobBannerLabel.SetText(_oobSb);
        }

        // --- Health bar (top-left) ---

        private void BuildHealthBar()
        {
            // Compact bar below the timer area (top-center, 400px wide, 20px tall)
            var go = CreateUIObject("HealthBar", _canvas.transform);
            Anchor(go, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -50f), new Vector2(400f, 20f), new Vector2(0.5f, 1f));

            _healthBar = go.AddComponent<Slider>();
            _healthBar.minValue = 0f;
            _healthBar.maxValue = 1f;
            _healthBar.value = 1f;
            _healthBar.interactable = false;
            _healthBar.transition = Selectable.Transition.None;

            // Background
            var bg = CreateUIObject("Background", go.transform);
            StretchFull(bg);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.13f, 0.13f, 0.27f, 0.9f); // #222244
            _healthBar.targetGraphic = bgImg;

            // Fill area
            var fillArea = CreateUIObject("FillArea", go.transform);
            StretchFull(fillArea);
            var fill = CreateUIObject("Fill", fillArea.transform);
            var fillRt = StretchFull(fill);
            _healthFill = fill.AddComponent<Image>();
            _healthFill.color = new Color(0.3f, 0.8f, 0.64f, 1f); // green
            _healthBar.fillRect = fillRt;

            // HP label overlay "HP: X / X"
            var label = CreateUIObject("HealthLabel", go.transform);
            StretchFull(label);
            _healthLabel = label.AddComponent<TextMeshProUGUI>();
            _healthLabel.text = "HP: — / —";
            _healthLabel.fontSize = 14f;
            _healthLabel.color = Color.white;
            _healthLabel.alignment = TextAlignmentOptions.Center;
        }

        // --- Bottom bar: dark panel with speed(left), ammo(center), domain info(right) ---

        private void BuildBottomBar()
        {
            // Dark semi-transparent panel at the bottom
            var panel = CreateUIObject("BottomBar", _canvas.transform);
            Anchor(panel, new Vector2(0f, 0f), new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, 80f), new Vector2(0f, 0f));
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.6f);
            panelImg.raycastTarget = false;

            // Speed (left)
            var speedGo = CreateUIObject("SpeedLabel", panel.transform);
            Anchor(speedGo, new Vector2(0f, 0f), new Vector2(0.33f, 1f),
                Vector2.zero, Vector2.zero, new Vector2(0f, 0f));
            var speedRt = speedGo.GetComponent<RectTransform>();
            speedRt.offsetMin = new Vector2(16f, 0f);
            speedRt.offsetMax = Vector2.zero;
            _speedLabel = speedGo.AddComponent<TextMeshProUGUI>();
            _speedLabel.text = "SPD: 0 m/s";
            _speedLabel.fontSize = 18f;
            _speedLabel.color = Color.white;
            _speedLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _speedLabel.raycastTarget = false;

            // Ammo (center)
            var ammoGo = CreateUIObject("AmmoLabel", panel.transform);
            Anchor(ammoGo, new Vector2(0.33f, 0f), new Vector2(0.66f, 1f),
                Vector2.zero, Vector2.zero, new Vector2(0f, 0f));
            _ammoLabel = ammoGo.AddComponent<TextMeshProUGUI>();
            _ammoLabel.text = "AMMO: 0";
            _ammoLabel.fontSize = 18f;
            _ammoLabel.color = Color.white;
            _ammoLabel.alignment = TextAlignmentOptions.Center;
            _ammoLabel.raycastTarget = false;

            // Domain info (right column)
            var domainGo = CreateUIObject("DomainInfo", panel.transform);
            Anchor(domainGo, new Vector2(0.66f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, new Vector2(0f, 0f));
            var domainRt = domainGo.GetComponent<RectTransform>();
            domainRt.offsetMax = new Vector2(-16f, 0f);

            var domainVlg = domainGo.AddComponent<VerticalLayoutGroup>();
            domainVlg.spacing = 2f;
            domainVlg.childAlignment = TextAnchor.MiddleRight;
            domainVlg.childForceExpandWidth = true;
            domainVlg.childForceExpandHeight = false;
            domainVlg.childControlWidth = true;
            domainVlg.childControlHeight = false;
            domainVlg.padding = new RectOffset(0, 0, 8, 8);

            _domainInfoContent = domainGo.transform;
        }

        // --- Boost circle (bottom-right, ring around miniature vehicle) ---

        private static Sprite _circleSprite;

        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size * 0.5f;
            float radius = center - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center, dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f); // anti-aliased edge
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }

        private void BuildBoostBar()
        {
            float ringSize = 120f;
            Sprite circle = GetCircleSprite();

            // Container anchored to bottom-right
            var container = CreateUIObject("BoostCircle", _canvas.transform);
            Anchor(container, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-80f, 90f), new Vector2(ringSize, ringSize), new Vector2(0.5f, 0.5f));

            // Background ring (dark circle)
            var bgGo = CreateUIObject("RingBg", container.transform);
            StretchFull(bgGo);
            _boostRingBg = bgGo.AddComponent<Image>();
            _boostRingBg.sprite = circle;
            _boostRingBg.color = new Color(0.12f, 0.12f, 0.18f, 0.7f);
            _boostRingBg.type = Image.Type.Filled;
            _boostRingBg.fillMethod = Image.FillMethod.Radial360;
            _boostRingBg.fillOrigin = (int)Image.Origin360.Top;
            _boostRingBg.fillClockwise = true;
            _boostRingBg.fillAmount = 1f;
            _boostRingBg.raycastTarget = false;

            // Fill ring (colored circular, drawn on top)
            var fillGo = CreateUIObject("RingFill", container.transform);
            StretchFull(fillGo);
            _boostRingFill = fillGo.AddComponent<Image>();
            _boostRingFill.sprite = circle;
            _boostRingFill.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            _boostRingFill.type = Image.Type.Filled;
            _boostRingFill.fillMethod = Image.FillMethod.Radial360;
            _boostRingFill.fillOrigin = (int)Image.Origin360.Top;
            _boostRingFill.fillClockwise = true;
            _boostRingFill.fillAmount = 1f;
            _boostRingFill.raycastTarget = false;

            // Inner dark circle (creates the ring cutout)
            float innerRatio = 0.72f;
            var innerGo = CreateUIObject("Inner", container.transform);
            Anchor(innerGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(ringSize * innerRatio, ringSize * innerRatio),
                new Vector2(0.5f, 0.5f));
            var innerImg = innerGo.AddComponent<Image>();
            innerImg.sprite = circle;
            innerImg.color = new Color(0.08f, 0.08f, 0.14f, 0.9f);
            innerImg.raycastTarget = false;

            // Vehicle preview (RenderTexture from a secondary camera)
            var previewGo = CreateUIObject("VehiclePreview", innerGo.transform);
            Anchor(previewGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(ringSize * innerRatio * 0.85f, ringSize * innerRatio * 0.85f),
                new Vector2(0.5f, 0.5f));
            _vehiclePreview = previewGo.AddComponent<RawImage>();
            _vehiclePreview.color = Color.white;
            _vehiclePreview.raycastTarget = false;

            // Label below the ring
            var labelGo = CreateUIObject("BoostLabel", container.transform);
            Anchor(labelGo, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, -14f), new Vector2(100f, 20f), new Vector2(0.5f, 1f));
            _boostLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _boostLabel.text = "BOOST";
            _boostLabel.fontSize = 13f;
            _boostLabel.color = new Color(0.5f, 0.7f, 1f);
            _boostLabel.alignment = TextAlignmentOptions.Center;
            _boostLabel.raycastTarget = false;
        }

        /// <summary>
        /// Set up the vehicle preview render texture. Call after the player
        /// vehicle is spawned so we can create a secondary camera for it.
        /// </summary>
        public void SetupVehiclePreview(Transform vehicleTransform)
        {
            if (_vehiclePreview == null || vehicleTransform == null) return;

            var rt = new RenderTexture(128, 128, 16);
            rt.antiAliasing = 2;

            var camObj = new GameObject("BoostPreviewCam");
            var cam = camObj.AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.14f, 0f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.depth = -10; // render before main camera

            // Parent to vehicle and position above/behind looking down
            camObj.transform.SetParent(vehicleTransform, false);
            camObj.transform.localPosition = new Vector3(0f, 8f, -6f);
            camObj.transform.LookAt(vehicleTransform.position + Vector3.up * 0.5f);

            _vehiclePreview.texture = rt;
        }

        // --- Crosshair (center, offset by reticleOffsetX/Y) ---

        private void BuildCrosshair()
        {
            // Horizontal line
            var hGo = CreateUIObject("CrosshairH", _canvas.transform);
            Anchor(hGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(32f, 2f), new Vector2(0.5f, 0.5f));
            _crosshairH = hGo.AddComponent<Image>();
            _crosshairH.color = new Color(1f, 1f, 1f, 0.85f);

            // Vertical line
            var vGo = CreateUIObject("CrosshairV", _canvas.transform);
            Anchor(vGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(2f, 32f), new Vector2(0.5f, 0.5f));
            _crosshairV = vGo.AddComponent<Image>();
            _crosshairV.color = new Color(1f, 1f, 1f, 0.85f);
        }

        // --- Damage flash (fullscreen red overlay) ---

        private void BuildDamageFlash()
        {
            var go = CreateUIObject("DamageFlash", _canvas.transform);
            StretchFull(go);
            _damageFlash = go.AddComponent<Image>();
            _damageFlash.color = new Color(0.8f, 0f, 0f, 0f);
            _damageFlash.raycastTarget = false;
        }

        // --- Kill feed (top-right) ---

        private void BuildKillFeed()
        {
            var go = CreateUIObject("KillFeed", _canvas.transform);
            Anchor(go, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -60f), new Vector2(300f, 200f), new Vector2(1f, 1f));

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperRight;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;

            _killFeedContent = go.transform;
        }

        // --- Game over label (center, hidden) ---

        private void BuildGameOverLabel()
        {
            var go = CreateUIObject("GameOverLabel", _canvas.transform);
            Anchor(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 60f), new Vector2(600f, 80f), new Vector2(0.5f, 0.5f));

            _gameOverLabel = go.AddComponent<TextMeshProUGUI>();
            _gameOverLabel.text = "GAME OVER";
            _gameOverLabel.fontSize = 56f;
            _gameOverLabel.color = new Color(1f, 0.2f, 0.2f, 1f);
            _gameOverLabel.alignment = TextAlignmentOptions.Center;
            _gameOverLabel.fontStyle = FontStyles.Bold;
            go.SetActive(false);
        }

        // --- Pause menu (Escape toggle) ---

        private void BuildPauseMenu()
        {
            _pausePanel = CreateUIObject("PauseMenu", _canvas.transform);
            StretchFull(_pausePanel);
            var overlay = _pausePanel.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.6f);

            // Title
            var titleGo = CreateUIObject("PauseTitle", _pausePanel.transform);
            Anchor(titleGo, new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.7f),
                Vector2.zero, new Vector2(400f, 60f), new Vector2(0.5f, 0.5f));
            _pauseTitle = titleGo.AddComponent<TextMeshProUGUI>();
            _pauseTitle.text = "PAUSED";
            _pauseTitle.fontSize = 48f;
            _pauseTitle.color = Color.white;
            _pauseTitle.alignment = TextAlignmentOptions.Center;
            _pauseTitle.fontStyle = FontStyles.Bold;

            // Resume button
            CreateButton("ResumeBtn", _pausePanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(250f, 44f),
                "RESUME", () => TogglePause());

            // Restart (Builder) button
            CreateButton("RestartBtn", _pausePanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(250f, 44f),
                "RESTART (BUILDER)", () =>
                {
                    Time.timeScale = 1f;
                    _isPaused = false;
                    if (GameManager.Instance != null)
                        GameManager.Instance.GoToBuilder();
                    else
                        UnityEngine.SceneManagement.SceneManager.LoadScene("Builder");
                });

            // Quit to menu button
            CreateButton("QuitBtn", _pausePanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -50f), new Vector2(250f, 44f),
                "QUIT TO MENU", () =>
                {
                    Time.timeScale = 1f;
                    _isPaused = false;
                    if (GameManager.Instance != null)
                        GameManager.Instance.ReturnToMainMenu();
                });

            // Healthbars toggle button
            var hbBtn = CreateButton("HealthbarToggle", _pausePanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(250f, 44f),
                "HEALTHBARS: ON", null);
            var hbText = hbBtn.GetComponentInChildren<TMP_Text>();
            hbBtn.onClick.AddListener(() =>
            {
                _healthbarsOn = !_healthbarsOn;
                if (hbText != null)
                    hbText.text = _healthbarsOn ? "HEALTHBARS: ON" : "HEALTHBARS: OFF";
                // Toggle all floating healthbar objects in the scene
                var allHealthbars = FindObjectsByType<FloatingHealthbar>(FindObjectsSortMode.None);
                foreach (var hb in allHealthbars)
                    hb.gameObject.SetActive(_healthbarsOn);
            });

            _pausePanel.SetActive(false);
        }

        // === Public API ==================================================

        /// <summary>
        /// Bind to a vehicle MonoBehaviour that exposes health, speed, etc.
        /// </summary>
        public void Setup(MonoBehaviour vehicle)
        {
            _vehicle = vehicle;
        }

        /// <summary>Trigger damage flash overlay.</summary>
        public void ShowDamageFlash()
        {
            _flashTimer = FlashDuration;
        }

        /// <summary>Show the game over label.</summary>
        public void ShowGameOver(string message = "GAME OVER")
        {
            _isGameOver = true;
            _gameOverLabel.text = message;
            _gameOverLabel.fontSize = 72f;
            _gameOverLabel.gameObject.SetActive(true);
        }

        /// <summary>Set health bar value (0..1), with green/yellow/red coloring.</summary>
        public void SetHealth(float normalized)
        {
            if (_healthBar != null)
            {
                _healthBar.value = Mathf.Clamp01(normalized);
                // Green > 50%, Yellow 25-50%, Red < 25%
                if (normalized > 0.5f)
                    _healthFill.color = new Color(0.3f, 0.8f, 0.64f, 1f);   // green
                else if (normalized > 0.25f)
                    _healthFill.color = new Color(0.94f, 0.75f, 0.25f, 1f);  // yellow
                else
                    _healthFill.color = new Color(0.91f, 0.27f, 0.38f, 1f);  // red
            }
        }

        /// <summary>Set health bar with numeric display: "HP: current / max".</summary>
        public void SetHealth(float normalized, int currentHp, int maxHp)
        {
            SetHealth(normalized);
            if (_healthLabel != null)
                _healthLabel.text = $"HP: {currentHp} / {maxHp}";
        }

        /// <summary>Set speed readout.</summary>
        public void SetSpeed(float metersPerSecond)
        {
            if (_speedLabel != null)
                _speedLabel.text = $"SPD: {metersPerSecond:F0} m/s";
        }

        /// <summary>Set ammo / weapon readout.</summary>
        public void SetAmmo(string weaponName, int current, int max)
        {
            if (_ammoLabel == null) return;

            if (string.IsNullOrEmpty(weaponName))
                _ammoLabel.text = $"AMMO: {current}";
            else if (max > 0)
                _ammoLabel.text = $"{weaponName}  {current} / {max}";
            else
                _ammoLabel.text = $"AMMO: {current}  |  WPN: {weaponName}";
        }

        /// <summary>Set boost ring fill (0..1). Color: blue > 0.5, orange > 0.2, red below.</summary>
        public void SetBoost(float normalized)
        {
            if (_boostRingFill != null)
            {
                _boostRingFill.fillAmount = Mathf.Clamp01(normalized);
                if (normalized > 0.5f)
                    _boostRingFill.color = new Color(0.2f, 0.6f, 1f, 0.9f);   // blue
                else if (normalized > 0.2f)
                    _boostRingFill.color = new Color(1f, 0.6f, 0.1f, 0.9f);   // orange
                else
                    _boostRingFill.color = new Color(0.9f, 0.15f, 0.15f, 0.9f); // red
            }
        }

        /// <summary>Add a kill-feed entry (auto-removes after 5 seconds).</summary>
        public void AddKillFeedEntry(string message)
        {
            if (_killFeedContent == null) return;

            var go = CreateUIObject("KillEntry", _killFeedContent);
            go.AddComponent<LayoutElement>().preferredHeight = 20f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = message;
            tmp.fontSize = 14f;
            tmp.color = new Color(0.91f, 0.27f, 0.38f, 1f); // accent red
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            tmp.raycastTarget = false;

            // Auto-destroy after 5 seconds
            Destroy(go, 5f);
        }

        /// <summary>Update domain-specific info labels (altitude, depth, etc.).</summary>
        public void SetDomainInfo(Dictionary<string, string> data)
        {
            if (_domainInfoContent == null) return;

            // Clear old labels
            for (int i = _domainInfoContent.childCount - 1; i >= 0; i--)
                Destroy(_domainInfoContent.GetChild(i).gameObject);

            if (data == null) return;

            foreach (var kv in data)
            {
                var go = CreateUIObject("DI_" + kv.Key, _domainInfoContent);
                go.AddComponent<LayoutElement>().preferredHeight = 18f;
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.text = $"{kv.Key}: {kv.Value}";
                tmp.fontSize = 14f;
                tmp.color = Color.white;
                tmp.alignment = TextAlignmentOptions.MidlineRight;
                tmp.raycastTarget = false;
            }
        }

        /// <summary>Set boost label text ("BOOST" or "BOOSTING").</summary>
        public void SetBoostLabel(string text)
        {
            if (_boostLabel != null)
                _boostLabel.text = text;
        }

        /// <summary>
        /// Switch HUD to spectator mode: hide player-specific elements,
        /// show spectating text with the name of who we're watching.
        /// </summary>
        public void EnterSpectatorMode(string spectatingName)
        {
            // Hide player HUD elements
            if (_healthBar != null) _healthBar.gameObject.SetActive(false);
            if (_healthLabel != null) _healthLabel.gameObject.SetActive(false);
            if (_speedLabel != null) _speedLabel.gameObject.SetActive(false);
            if (_ammoLabel != null) _ammoLabel.gameObject.SetActive(false);
            if (_boostRingBg != null) _boostRingBg.gameObject.SetActive(false);
            if (_boostRingFill != null) _boostRingFill.gameObject.SetActive(false);
            if (_boostLabel != null) _boostLabel.gameObject.SetActive(false);
            if (_vehiclePreview != null) _vehiclePreview.gameObject.SetActive(false);
            if (_crosshairH != null) _crosshairH.gameObject.SetActive(false);
            if (_crosshairV != null) _crosshairV.gameObject.SetActive(false);

            // Show spectating info using the game-over label
            if (_gameOverLabel != null)
            {
                _gameOverLabel.text = $"Spectating - click to spectate {spectatingName}";
                _gameOverLabel.fontSize = 24f;
                _gameOverLabel.gameObject.SetActive(true);
            }
        }

        /// <summary>Update the spectating name shown on screen.</summary>
        public void SetSpectatingTarget(string name)
        {
            if (_gameOverLabel != null && _gameOverLabel.gameObject.activeSelf)
                _gameOverLabel.text = $"Spectating - click to spectate {name}";
        }

        // === Update ======================================================

        private void Update()
        {
            // Move crosshair to match reticle offset from PlayerVehicleController
            if (_crosshairH != null)
            {
                var ctrl = GetPlayerCtrl();
                if (ctrl != null)
                {
                    float ox = ctrl.ReticleOffsetX;
                    float oy = ctrl.ReticleOffsetY;
                    _crosshairH.rectTransform.anchoredPosition = new Vector2(ox, oy);
                    _crosshairV.rectTransform.anchoredPosition = new Vector2(ox, oy);
                }
            }

            // Damage flash fade
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.unscaledDeltaTime;
                float alpha = Mathf.Clamp01(_flashTimer / FlashDuration) * 0.4f;
                _damageFlash.color = new Color(0.8f, 0f, 0f, alpha);
            }

            UpdateOOBBanner();

            // Pause toggle (Escape). Skip only during Results screen.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (GameManager.Instance != null &&
                    GameManager.Instance.CurrentState == GameState.Results)
                    return;

                TogglePause();
            }
        }

        private void TogglePause()
        {
            _isPaused = !_isPaused;
            _pausePanel.SetActive(_isPaused);
            Time.timeScale = _isPaused ? 0f : 1f;

            // Unlock/relock cursor for pause menu interaction
            Cursor.lockState = _isPaused ? CursorLockMode.None : CursorLockMode.None;
            Cursor.visible = true;
            var ctrl = GetPlayerCtrl();
            if (ctrl != null)
            {
                if (_isPaused) ctrl.UnlockCursor();
                else ctrl.RelockCursor();
            }
        }

        // === UI helpers ==================================================

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

        private static Button CreateButton(string name, Transform parent,
            Vector2 anchor, Vector2 pos, Vector2 size, string label,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, anchor, anchor, pos, size, new Vector2(0.5f, 0.5f));

            var img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.22f, 0.22f, 0.9f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var txtGo = CreateUIObject("Text", go.transform);
            StretchFull(txtGo);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 20f;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;

            return btn;
        }
    }
}
