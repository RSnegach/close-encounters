using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using CloseEncounters.Core;
using CloseEncounters.Vehicle;

namespace CloseEncounters.UI
{
    /// <summary>
    /// Vehicle builder UI: part catalog, budget/stats, save/load dialogs.
    /// Fully wired to VehicleBuilder for all operations.
    /// Attach to the Canvas created by SceneBootstrapper; call Initialize().
    /// </summary>
    public class BuilderUI : MonoBehaviour
    {
        private Canvas _canvas;
        private VehicleBuilder _builder;

        // --- Part catalog (left panel) ---
        private TMP_Dropdown _categoryFilter;
        private TMP_InputField _searchInput;
        private Transform _catalogContent;
        private ScrollRect _catalogScroll;

        // --- Tooltip ---
        private GameObject _tooltipPanel;
        private TMP_Text _tooltipText;

        // --- Stats panel (right) ---
        private TMP_Text _budgetLabel;
        private Slider _budgetBar;
        private Image _budgetFill;
        private TMP_Text _statsText;
        private TMP_Text _layerLabel;
        private TMP_Text _forwardLabel;
        private TMP_Text _validationLabel;

        // --- Buttons ---
        private Button _validateBtn;
        private Button _saveBtn;
        private Button _loadBtn;
        private Button _clearBtn;
        private Button _readyBtn;
        private Button _backBtn;

        // --- Dialogs ---
        private GameObject _saveDialog;
        private TMP_InputField _saveNameInput;
        private GameObject _loadDialog;
        private Transform _loadListContent;

        // --- State ---
        private string _currentDomain = "ground";
        private string _selectedPartId;
        private float _validationTimer;

        private static readonly string[] Categories =
            { "All", "Control", "Structural", "Propulsion", "Weapon", "Defense", "Fuel", "Utility" };

        // ==================================================================
        // Initialization
        // ==================================================================

        public void Initialize()
        {
            _canvas = GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = GetComponentInParent<Canvas>();

            if (GameManager.Instance != null)
                _currentDomain = GameManager.Instance.Settings.domain;

            // Find VehicleBuilder in the scene
            _builder = FindAnyObjectByType<VehicleBuilder>();
            if (_builder == null)
            {
                Debug.LogError("[BuilderUI] No VehicleBuilder found in scene.");
                return;
            }

            // Subscribe to builder events
            _builder.OnBudgetChanged += HandleBudgetChanged;
            _builder.OnLayerChanged += HandleLayerChanged;
            _builder.OnForwardChanged += HandleForwardChanged;
            _builder.OnPartPlaced += HandlePartPlaced;
            _builder.OnPartRemoved += HandlePartRemoved;
            _builder.OnValidationError += HandleValidationError;

            BuildBackground();
            BuildCatalogPanel();
            BuildStatsPanel();
            BuildBottomBar();
            BuildSaveDialog();
            BuildLoadDialog();
            BuildTooltip();

            RefreshCatalog();
            RefreshBudget();
            RefreshLayerLabel();
            RefreshForwardLabel();
            RefreshStats();

            Debug.Log("[BuilderUI] Initialized.");
        }

        private void OnDestroy()
        {
            if (_builder != null)
            {
                _builder.OnBudgetChanged -= HandleBudgetChanged;
                _builder.OnLayerChanged -= HandleLayerChanged;
                _builder.OnForwardChanged -= HandleForwardChanged;
                _builder.OnPartPlaced -= HandlePartPlaced;
                _builder.OnPartRemoved -= HandlePartRemoved;
                _builder.OnValidationError -= HandleValidationError;
            }
        }

        // ==================================================================
        // Update loop
        // ==================================================================

        private void Update()
        {
            if (_builder == null) return;

            // Continuously refresh layer/forward labels from builder state
            // (keyboard input Q/E/F is handled in VehicleBuilder.Update)
            RefreshLayerLabel();
            RefreshForwardLabel();
            RefreshBudget();

            // Fade out validation message
            if (_validationTimer > 0f)
            {
                _validationTimer -= Time.deltaTime;
                if (_validationTimer <= 0f && _validationLabel != null)
                    _validationLabel.text = "";
            }

            // Hide tooltip if mouse not over catalog area
            UpdateTooltip();
        }

        // ==================================================================
        // Event handlers from VehicleBuilder
        // ==================================================================

        private void HandleBudgetChanged(int used, int total)
        {
            RefreshBudget();
            RefreshStats();
        }

        private void HandleLayerChanged(int layer)
        {
            RefreshLayerLabel();
        }

        private void HandleForwardChanged(float angle)
        {
            RefreshForwardLabel();
        }

        private void HandlePartPlaced(PartData part, Vector3Int origin)
        {
            RefreshStats();
        }

        private void HandlePartRemoved(PartData part, Vector3Int origin)
        {
            RefreshStats();
        }

        private void HandleValidationError(string message)
        {
            ShowValidationMessage(message, false);
        }

        // ==================================================================
        // Background
        // ==================================================================

        private void BuildBackground()
        {
            // No full-screen background — center stays transparent so the 3D viewport shows through.
            // Side panels have their own opaque backgrounds.
        }

        // ==================================================================
        // Left: Part catalog
        // ==================================================================

        private void BuildCatalogPanel()
        {
            var panel = CreateUIObject("CatalogPanel", _canvas.transform);
            Anchor(panel, new Vector2(0f, 0.08f), new Vector2(0.28f, 1f),
                Vector2.zero, Vector2.zero, new Vector2(0f, 1f));
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.08f, 0.14f, 0.92f);

            // Title
            var title = CreateUIObject("CatalogTitle", panel.transform);
            Anchor(title, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -4f), new Vector2(0f, 28f), new Vector2(0.5f, 1f));
            var titleTmp = title.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "PARTS CATALOG";
            titleTmp.fontSize = 18f;
            titleTmp.color = Color.white;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.fontStyle = FontStyles.Bold;

            // Category dropdown
            _categoryFilter = AddDropdown(panel.transform, "CategoryDD",
                8f, -36f, 0.55f, new List<string>(Categories));
            _categoryFilter.onValueChanged.AddListener((_) => RefreshCatalog());

            // Search field
            _searchInput = AddInputField(panel.transform, "SearchInput",
                0.58f, -36f, 0.40f, "Search...");
            _searchInput.onValueChanged.AddListener((_) => RefreshCatalog());

            // Scrollable list
            var scrollGo = CreateUIObject("CatalogScroll", panel.transform);
            Anchor(scrollGo, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.88f),
                Vector2.zero, Vector2.zero, new Vector2(0f, 1f));

            var viewport = CreateUIObject("Viewport", scrollGo.transform);
            StretchFull(viewport);
            var vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = CreateUIObject("Content", viewport.transform);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 3f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _catalogScroll = scrollGo.AddComponent<ScrollRect>();
            _catalogScroll.content = contentRt;
            _catalogScroll.viewport = viewport.GetComponent<RectTransform>();
            _catalogScroll.horizontal = false;
            _catalogScroll.vertical = true;
            _catalogScroll.scrollSensitivity = 30f;

            _catalogContent = content.transform;
        }

        // ==================================================================
        // Right: Stats panel
        // ==================================================================

        private void BuildStatsPanel()
        {
            // Narrower panel: 0.78 → 1.0 (was 0.72)
            var panel = CreateUIObject("StatsPanel", _canvas.transform);
            Anchor(panel, new Vector2(0.78f, 0.08f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, new Vector2(1f, 1f));
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.08f, 0.14f, 0.92f);

            float y = -6f;

            // Budget heading
            var budgetTitle = CreateUIObject("BudgetTitle", panel.transform);
            Anchor(budgetTitle, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, y), new Vector2(0f, 18f), new Vector2(0.5f, 1f));
            var btTmp = budgetTitle.AddComponent<TextMeshProUGUI>();
            btTmp.text = "BUDGET";
            btTmp.fontSize = 14f;
            btTmp.color = Color.white;
            btTmp.alignment = TextAlignmentOptions.Center;
            btTmp.fontStyle = FontStyles.Bold;
            y -= 20f;

            // Budget bar
            var barGo = CreateUIObject("BudgetBar", panel.transform);
            Anchor(barGo, new Vector2(0.05f, 1f), new Vector2(0.95f, 1f),
                new Vector2(0f, y), new Vector2(0f, 14f), new Vector2(0.5f, 1f));

            _budgetBar = barGo.AddComponent<Slider>();
            _budgetBar.minValue = 0f;
            _budgetBar.maxValue = 1f;
            _budgetBar.value = 0f;
            _budgetBar.interactable = false;
            _budgetBar.transition = Selectable.Transition.None;

            var barBg = CreateUIObject("BarBg", barGo.transform);
            StretchFull(barBg);
            barBg.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.2f, 1f);

            var fillArea = CreateUIObject("FillArea", barGo.transform);
            StretchFull(fillArea);
            var fill = CreateUIObject("Fill", fillArea.transform);
            var fillRt = StretchFull(fill);
            _budgetFill = fill.AddComponent<Image>();
            _budgetFill.color = new Color(0.3f, 0.7f, 0.3f, 1f);
            _budgetBar.fillRect = fillRt;
            y -= 16f;

            _budgetLabel = CreateLabel(panel.transform, "BudgetLabel", "$0 / $3,000", y, 13f);
            _budgetLabel.richText = true;
            y -= 20f;

            // Layer label
            _layerLabel = CreateLabel(panel.transform, "LayerLabel", "Layer: 0 (Q/E)", y, 13f);
            y -= 18f;

            // Forward label
            _forwardLabel = CreateLabel(panel.transform, "ForwardLabel", "Front: +Z (F)", y, 13f);
            y -= 22f;

            // Stats heading
            var statsTitle = CreateLabel(panel.transform, "StatsTitle", "VEHICLE STATS", y, 14f);
            statsTitle.fontStyle = FontStyles.Bold;
            y -= 18f;

            // Stats text block
            var statsGo = CreateUIObject("StatsText", panel.transform);
            Anchor(statsGo, new Vector2(0.05f, 1f), new Vector2(0.95f, 1f),
                new Vector2(0f, y), new Vector2(0f, 140f), new Vector2(0.5f, 1f));
            _statsText = statsGo.AddComponent<TextMeshProUGUI>();
            _statsText.text = "Parts: 0\nMass: 0 kg\nHP: 0\nThrust: 0 N\nTWR: 0.00\nEst. Speed: 0.0 m/s\nWeapons: 0\nControl: —\nPropulsion: —";
            _statsText.fontSize = 13f;
            _statsText.color = new Color(0.75f, 0.75f, 0.8f, 1f);
            _statsText.alignment = TextAlignmentOptions.TopLeft;
            _statsText.richText = true;

            // Validation message — anchored to BOTTOM of panel so it's always below everything
            var validGo = CreateUIObject("ValidationLabel", panel.transform);
            Anchor(validGo, new Vector2(0.05f, 0f), new Vector2(0.95f, 0f),
                new Vector2(0f, 6f), new Vector2(0f, 50f), new Vector2(0.5f, 0f));
            _validationLabel = validGo.AddComponent<TextMeshProUGUI>();
            _validationLabel.text = "";
            _validationLabel.fontSize = 13f;
            _validationLabel.color = new Color(1f, 0.4f, 0.4f, 1f);
            _validationLabel.alignment = TextAlignmentOptions.BottomLeft;
            _validationLabel.textWrappingMode = TextWrappingModes.Normal;
        }

        // ==================================================================
        // Bottom bar
        // ==================================================================

        private void BuildBottomBar()
        {
            var bar = CreateUIObject("BottomBar", _canvas.transform);
            Anchor(bar, new Vector2(0f, 0f), new Vector2(1f, 0.08f),
                Vector2.zero, Vector2.zero, new Vector2(0f, 0f));
            var barImg = bar.AddComponent<Image>();
            barImg.color = new Color(0.06f, 0.06f, 0.1f, 0.95f);

            float sp = 1f / 8f;
            _validateBtn = CreateBarButton(bar.transform, "ValidateBtn", "VALIDATE", sp * 0.5f, OnValidate);
            _saveBtn     = CreateBarButton(bar.transform, "SaveBtn", "SAVE", sp * 1.5f, OnSave);
            _loadBtn     = CreateBarButton(bar.transform, "LoadBtn", "LOAD", sp * 2.5f, OnLoad);
            _clearBtn    = CreateBarButton(bar.transform, "ClearBtn", "CLEAR", sp * 3.5f, OnClear);
            CreateBarButton(bar.transform, "PresetBtn", "TEST VEHICLE", sp * 4.5f, OnLoadPreset);
            _readyBtn    = CreateBarButton(bar.transform, "ReadyBtn", "READY >>", sp * 5.8f, OnReady);
            _backBtn     = CreateBarButton(bar.transform, "BackBtn", "BACK", sp * 7f, OnBack);

            // Color the ready button green-ish
            var readyImg = _readyBtn.GetComponent<Image>();
            if (readyImg != null) readyImg.color = new Color(0.15f, 0.28f, 0.15f, 0.95f);
        }

        // ==================================================================
        // Save dialog
        // ==================================================================

        private void BuildSaveDialog()
        {
            _saveDialog = CreateUIObject("SaveDialog", _canvas.transform);
            StretchFull(_saveDialog);
            var overlay = _saveDialog.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.65f);

            var box = CreateUIObject("SaveBox", _saveDialog.transform);
            Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(400f, 200f), new Vector2(0.5f, 0.5f));
            box.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 0.98f);

            var heading = CreateUIObject("Heading", box.transform);
            Anchor(heading, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -16f), new Vector2(360f, 32f), new Vector2(0.5f, 1f));
            var hTmp = heading.AddComponent<TextMeshProUGUI>();
            hTmp.text = "SAVE VEHICLE";
            hTmp.fontSize = 22f;
            hTmp.color = Color.white;
            hTmp.alignment = TextAlignmentOptions.Center;
            hTmp.fontStyle = FontStyles.Bold;

            _saveNameInput = AddCenteredInputField(box.transform, "SaveNameInput",
                new Vector2(0f, 10f), new Vector2(320f, 36f), "Vehicle Name");

            CreateDialogButton(box.transform, "SaveConfirm", "SAVE",
                new Vector2(-80f, -60f), () => ConfirmSave());
            CreateDialogButton(box.transform, "SaveCancel", "CANCEL",
                new Vector2(80f, -60f), () => _saveDialog.SetActive(false));

            _saveDialog.SetActive(false);
        }

        // ==================================================================
        // Load dialog
        // ==================================================================

        private void BuildLoadDialog()
        {
            _loadDialog = CreateUIObject("LoadDialog", _canvas.transform);
            StretchFull(_loadDialog);
            var overlay = _loadDialog.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.65f);

            var box = CreateUIObject("LoadBox", _loadDialog.transform);
            Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(440f, 400f), new Vector2(0.5f, 0.5f));
            box.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 0.98f);

            var heading = CreateUIObject("Heading", box.transform);
            Anchor(heading, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(400f, 30f), new Vector2(0.5f, 1f));
            var hTmp = heading.AddComponent<TextMeshProUGUI>();
            hTmp.text = "LOAD VEHICLE";
            hTmp.fontSize = 22f;
            hTmp.color = Color.white;
            hTmp.alignment = TextAlignmentOptions.Center;
            hTmp.fontStyle = FontStyles.Bold;

            // Scroll list
            var scrollGo = CreateUIObject("LoadScroll", box.transform);
            Anchor(scrollGo, new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.86f),
                Vector2.zero, Vector2.zero, new Vector2(0f, 1f));

            var viewport = CreateUIObject("Viewport", scrollGo.transform);
            StretchFull(viewport);
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = CreateUIObject("Content", viewport.transform);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 3f;
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 25f;

            _loadListContent = content.transform;

            // Close button
            CreateDialogButton(box.transform, "LoadCancel", "CLOSE",
                new Vector2(0f, 0f),
                () => _loadDialog.SetActive(false));
            // Re-position to bottom center
            var closeBtnRt = box.transform.Find("LoadCancel").GetComponent<RectTransform>();
            closeBtnRt.anchorMin = new Vector2(0.5f, 0f);
            closeBtnRt.anchorMax = new Vector2(0.5f, 0f);
            closeBtnRt.pivot = new Vector2(0.5f, 0f);
            closeBtnRt.anchoredPosition = new Vector2(0f, 10f);

            _loadDialog.SetActive(false);
        }

        // ==================================================================
        // Tooltip
        // ==================================================================

        private void BuildTooltip()
        {
            _tooltipPanel = CreateUIObject("Tooltip", _canvas.transform);
            var rt = _tooltipPanel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 0.5f); // anchor left-center
            rt.sizeDelta = new Vector2(300f, 210f);

            var bg = _tooltipPanel.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.14f, 0.96f);
            bg.raycastTarget = false;

            var textGo = CreateUIObject("Text", _tooltipPanel.transform);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10f, 8f);
            textRt.offsetMax = new Vector2(-10f, -8f);
            _tooltipText = textGo.AddComponent<TextMeshProUGUI>();
            _tooltipText.fontSize = 15f;
            _tooltipText.color = new Color(0.88f, 0.88f, 0.92f, 1f);
            _tooltipText.alignment = TextAlignmentOptions.TopLeft;
            _tooltipText.textWrappingMode = TextWrappingModes.Normal;
            _tooltipText.raycastTarget = false;

            _tooltipPanel.SetActive(false);
        }

        private string _hoveredPartId;
        private RectTransform _hoveredRowRect;
        private readonly Vector3[] _tooltipRowCorners = new Vector3[4];
        private readonly Vector3[] _tooltipSelfCorners = new Vector3[4];

        private void UpdateTooltip()
        {
            if (_tooltipPanel == null) return;

            if (string.IsNullOrEmpty(_hoveredPartId))
            {
                _tooltipPanel.SetActive(false);
                return;
            }

            _tooltipPanel.SetActive(true);
            var rt = _tooltipPanel.GetComponent<RectTransform>();

            // Position directly to the right of the hovered card
            if (_hoveredRowRect != null)
            {
                _hoveredRowRect.GetWorldCorners(_tooltipRowCorners);
                // rowCorners[2] = top-right, rowCorners[3] = top-left
                float rightEdge = _tooltipRowCorners[2].x;
                float centerY = (_tooltipRowCorners[0].y + _tooltipRowCorners[1].y) * 0.5f;
                rt.position = new Vector3(rightEdge + 4f, centerY, 0f);
            }
            else
            {
                rt.position = (Vector2)Input.mousePosition + new Vector2(16f, 0f);
            }

            // Clamp to screen
            rt.GetWorldCorners(_tooltipSelfCorners);
            if (_tooltipSelfCorners[2].x > Screen.width)
                rt.position -= new Vector3(_tooltipSelfCorners[2].x - Screen.width + 8f, 0f, 0f);
            if (_tooltipSelfCorners[0].y < 0f)
                rt.position -= new Vector3(0f, _tooltipSelfCorners[0].y - 8f, 0f);
            if (_tooltipSelfCorners[1].y > Screen.height)
                rt.position -= new Vector3(0f, _tooltipSelfCorners[1].y - Screen.height + 8f, 0f);
        }

        private void ShowPartTooltip(PartData part, RectTransform rowRect = null)
        {
            if (part == null) return;
            _hoveredPartId = part.id;
            _hoveredRowRect = rowRect;

            string name = string.IsNullOrEmpty(part.partName) ? part.id : part.partName;
            float damage = part.GetStat<float>("damage", 0f);
            float fireRate = part.GetStat<float>("fire_rate", part.GetStat<float>("fireRate", 0f));
            float range = part.GetStat<float>("range", 0f);

            string tip = $"<b><size=18>{name}</size></b>\n";
            tip += $"Cost: <color=#FFD700>${part.cost}</color>  |  Mass: {part.massKg:F1} kg\n";
            tip += $"HP: {part.hp}\n";
            if (damage > 0f) tip += $"Damage: {damage:F0}  |  Fire Rate: {fireRate:F1}/s\n";
            if (range > 0f) tip += $"Range: {range:F0}m\n";
            // Fallback: read range as int if float parse returned 0
            if (range <= 0f && part.stats != null && part.stats.ContainsKey("range"))
            {
                try { float r = System.Convert.ToSingle(part.stats["range"]); if (r > 0f) tip += $"Range: {r:F0}m\n"; }
                catch { }
            }

            tip += $"\n<size=16>{GetPartDescription(part.id)}</size>";

            if (_tooltipText != null)
                _tooltipText.text = tip;
        }

        private static string GetPartDescription(string id)
        {
            switch (id?.ToLowerInvariant())
            {
                // Weapons
                case "machine_gun":       return "Spray and pray. Works every time, 60% of the time.";
                case "autocannon":        return "Like a machine gun but angrier.";
                case "heavy_cannon":      return "Cannon in dire need of Ozempic.";
                case "missile_launcher":  return "Guided missile launcher. Fire and forget.";
                case "rocket_pod":        return "Launches unguided rockets in quick succession.";
                case "laser":             return "Not of the Jewish space variety.";
                case "railgun":           return "Deletes one thing per shot. Choose wisely.";
                case "mine_layer":        return "Now child-sized!";
                case "broadside_cannon":  return "Arr! Fire the cannons!";
                case "torpedo_launcher":  return "Launches guided torpedoes below the waterline.";
                case "deck_gun":          return "Top-mounted naval cannon for sustained fire.";
                case "swivel_cannon":     return "Rapid-fire light cannon on a swivel mount.";
                case "hull_gun":          return "Fixed hull-mounted gun for broadside attacks.";
                case "wing_cannon":       return "Wingtip dakka for air superiority.";
                case "side_missile":      return "Your new favorite sidechick.";
                case "milk_gun":          return "BEAM OF CUM";
                // Control
                case "cockpit":           return "I know where your mind went... pervert.";
                case "bridge":            return "Command center. Captain's chair not included.";
                case "conning_tower":     return "See everything. Be seen by everything.";
                // Structural
                case "light_frame":       return "Boogers and glue, but it'll do.";
                case "medium_frame":      return "The backbone of any respectable vehicle.";
                case "heavy_frame":       return "Frame in dire need of Ozempic.";
                case "hull_plate":        return "Keeps the water out. Usually.";
                case "reinforced_hull":   return "For when regular hull just won't cut it.";
                case "reinforced_frame":  return "Extra-reinforced structural frame.";
                // Propulsion
                case "small_wheel":       return "Size doesn't matter.";
                case "large_wheel":       return "Big wheel energy.";
                case "tank_tracks":       return "Continuous track system for all-terrain mobility.";
                case "rudder":            return "Rudder? I hard- you get the gist.";
                case "marine_propeller":  return "Propeller? I hardly know her.";
                case "paddle_wheel":      return "Side-mounted paddle wheel for river propulsion.";
                case "steam_engine":      return "Choo choo! But for boats.";
                case "turbine":           return "High-output gas turbine engine.";
                case "nuclear_reactor":   return "Iran has been days away from having one of these for the last 60 years.";
                case "jet_engine":        return "MAXIMUM THRUST.";
                case "afterburner":       return "Supplemental jet thrust booster.";
                case "propeller_air":     return "Front or rear-mounted air propeller.";
                case "rcs_thruster":      return "Small reaction control thruster for maneuvering.";
                // Defense
                case "light_armor":       return "Better than nothing.";
                case "heavy_armor":       return "Extra protection for your precious parts.";
                case "reactive_armor":    return "Reactive plating that detonates on impact.";
                // Utility
                case "fuel_tank":         return "Standard fuel storage tank.";
                case "booster_tank":      return "Additional fuel for boost capacity.";
                case "armored_fuel":      return "Won't explode when destroyed. More HP than standard fuel.";
                default:                  return "Vehicle component.";
            }
        }

        private void HidePartTooltip()
        {
            _hoveredPartId = null;
        }

        // ==================================================================
        // Button handlers
        // ==================================================================

        private void OnValidate()
        {
            if (_builder == null) return;

            bool valid = _builder.ValidateVehicle(out string error);

            if (valid)
            {
                ShowValidationMessage("Validation PASSED", true);
            }
            else
            {
                ShowValidationMessage($"Validation FAILED: {error}", false);
            }
        }

        private void ShowValidationMessage(string message, bool success)
        {
            if (_validationLabel != null)
            {
                _validationLabel.text = message;
                _validationLabel.color = success
                    ? new Color(0.3f, 0.9f, 0.3f, 1f)
                    : new Color(1f, 0.4f, 0.4f, 1f);
            }

            // Also flash the budget bar color
            if (_budgetFill != null)
            {
                _budgetFill.color = success
                    ? new Color(0.2f, 0.8f, 0.2f, 1f)
                    : new Color(0.9f, 0.2f, 0.2f, 1f);
            }

            _validationTimer = 6f;
        }

        private void OnSave()
        {
            _saveDialog.SetActive(true);
            if (_saveNameInput != null)
                _saveNameInput.text = "MyVehicle";
        }

        private void ConfirmSave()
        {
            if (_builder == null) return;

            string vehicleName = _saveNameInput != null ? _saveNameInput.text : "MyVehicle";
            if (string.IsNullOrWhiteSpace(vehicleName)) vehicleName = "MyVehicle";

            VehicleData data = _builder.ExportVehicleData(vehicleName);
            bool saved = VehicleSerializer.Save(data, vehicleName);

            _saveDialog.SetActive(false);

            if (saved)
                ShowValidationMessage($"Saved: {vehicleName}", true);
            else
                ShowValidationMessage("Save failed.", false);
        }

        private void OnLoad()
        {
            RefreshLoadList();
            _loadDialog.SetActive(true);
        }

        private void RefreshLoadList()
        {
            // Clear
            for (int i = _loadListContent.childCount - 1; i >= 0; i--)
                Destroy(_loadListContent.GetChild(i).gameObject);

            string[] saved = VehicleSerializer.ListSavedVehicles();

            foreach (string fileName in saved)
            {
                // Filter by domain: load header and check
                var data = VehicleSerializer.Load(fileName);
                if (data != null && !string.Equals(data.domain, _currentDomain, StringComparison.OrdinalIgnoreCase))
                    continue;

                string capture = fileName; // closure capture

                var row = CreateUIObject("LoadRow_" + fileName, _loadListContent);
                row.AddComponent<LayoutElement>().preferredHeight = 34f;
                row.AddComponent<Image>().color = new Color(0.16f, 0.16f, 0.24f, 0.9f);

                // Name label
                var nameGo = CreateUIObject("Name", row.transform);
                var nameRt = nameGo.GetComponent<RectTransform>();
                nameRt.anchorMin = new Vector2(0f, 0f);
                nameRt.anchorMax = new Vector2(0.65f, 1f);
                nameRt.offsetMin = new Vector2(8f, 0f);
                nameRt.offsetMax = Vector2.zero;
                var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
                nameTmp.text = data != null ? $"{data.name} ({data.parts.Count}p)" : fileName;
                nameTmp.fontSize = 15f;
                nameTmp.color = Color.white;
                nameTmp.alignment = TextAlignmentOptions.MidlineLeft;

                // Load button
                var loadBtnGo = CreateUIObject("LoadBtn", row.transform);
                var loadBtnRt = loadBtnGo.GetComponent<RectTransform>();
                loadBtnRt.anchorMin = new Vector2(0.66f, 0.1f);
                loadBtnRt.anchorMax = new Vector2(0.82f, 0.9f);
                loadBtnRt.offsetMin = Vector2.zero;
                loadBtnRt.offsetMax = Vector2.zero;
                loadBtnGo.AddComponent<Image>().color = new Color(0.2f, 0.45f, 0.2f, 1f);
                var loadBtn = loadBtnGo.AddComponent<Button>();
                loadBtn.onClick.AddListener(() =>
                {
                    LoadVehicle(capture);
                    _loadDialog.SetActive(false);
                });
                var loadTxt = CreateUIObject("T", loadBtnGo.transform);
                StretchFull(loadTxt);
                var ltmp = loadTxt.AddComponent<TextMeshProUGUI>();
                ltmp.text = "LOAD";
                ltmp.fontSize = 13f;
                ltmp.color = Color.white;
                ltmp.alignment = TextAlignmentOptions.Center;

                // Delete button
                var delBtnGo = CreateUIObject("DelBtn", row.transform);
                var delBtnRt = delBtnGo.GetComponent<RectTransform>();
                delBtnRt.anchorMin = new Vector2(0.84f, 0.1f);
                delBtnRt.anchorMax = new Vector2(0.98f, 0.9f);
                delBtnRt.offsetMin = Vector2.zero;
                delBtnRt.offsetMax = Vector2.zero;
                delBtnGo.AddComponent<Image>().color = new Color(0.55f, 0.15f, 0.15f, 1f);
                var delBtn = delBtnGo.AddComponent<Button>();
                delBtn.onClick.AddListener(() =>
                {
                    VehicleSerializer.Delete(capture);
                    RefreshLoadList();
                });
                var delTxt = CreateUIObject("T", delBtnGo.transform);
                StretchFull(delTxt);
                var dtmp = delTxt.AddComponent<TextMeshProUGUI>();
                dtmp.text = "DEL";
                dtmp.fontSize = 13f;
                dtmp.color = Color.white;
                dtmp.alignment = TextAlignmentOptions.Center;
            }
        }

        private void LoadVehicle(string fileName)
        {
            if (_builder == null) return;

            var data = VehicleSerializer.Load(fileName);
            if (data == null)
            {
                ShowValidationMessage($"Failed to load: {fileName}", false);
                return;
            }

            _builder.ImportVehicleData(data);
            RefreshStats();
            RefreshBudget();
            RefreshLayerLabel();
            RefreshForwardLabel();
            ShowValidationMessage($"Loaded: {data.name} ({data.parts.Count} parts)", true);
        }

        private void OnLoadPreset()
        {
            if (_builder == null) return;

            string domainLower = _currentDomain.ToLowerInvariant();
            string presetName = $"test_{domainLower}";

            // Try loading from Resources/Data/Presets/ first
            TextAsset presetAsset = Resources.Load<TextAsset>($"Data/Presets/{presetName}");
            if (presetAsset != null)
            {
                var data = VehicleSerializer.LoadFromJson(presetAsset.text);
                if (data != null)
                {
                    _builder.ImportVehicleData(data);
                    RefreshStats();
                    RefreshBudget();
                    ShowValidationMessage($"Loaded preset: {presetName} ({data.parts.Count} parts)", true);
                    return;
                }
            }

            // Fallback: try saved vehicles directory
            var saved = VehicleSerializer.Load(presetName);
            if (saved != null)
            {
                _builder.ImportVehicleData(saved);
                RefreshStats();
                RefreshBudget();
                ShowValidationMessage($"Loaded preset: {presetName}", true);
            }
            else
            {
                ShowValidationMessage($"No preset found for {domainLower}", false);
            }
        }

        private void OnClear()
        {
            if (_builder == null) return;

            _builder.ClearGrid();
            _selectedPartId = null;

            RefreshStats();
            RefreshBudget();
            RefreshLayerLabel();
            RefreshForwardLabel();
            ShowValidationMessage("Build cleared.", true);
        }

        private void OnReady()
        {
            if (_builder == null) return;

            // Validate first
            if (!_builder.ValidateVehicle(out string error))
            {
                ShowValidationMessage($"Cannot enter combat: {error}", false);
                return;
            }

            // Export vehicle data and store it for the combat scene
            VehicleData vehicleData = _builder.ExportVehicleData("Player Vehicle");

            // Save as a temp file so combat scene can load it
            VehicleSerializer.Save(vehicleData, "__combat_vehicle__");

            // Also store directly in MatchSettings so ArenaManager can use it
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Settings.playerVehicle = vehicleData;
                GameManager.Instance.StartMatch();
            }
            else
            {
                SceneManager.LoadScene("Combat");
            }
        }

        private void OnBack()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.ReturnToLobby();
            }
            else
            {
                SceneManager.LoadScene("Lobby");
            }
        }

        // ==================================================================
        // Catalog
        // ==================================================================

        private void RefreshCatalog()
        {
            // Clear existing rows
            for (int i = _catalogContent.childCount - 1; i >= 0; i--)
                Destroy(_catalogContent.GetChild(i).gameObject);

            if (PartRegistry.Instance == null) return;

            string catFilter = Categories[_categoryFilter.value].ToLowerInvariant();
            string search = _searchInput != null ? _searchInput.text.Trim().ToLowerInvariant() : "";

            List<PartData> parts = PartRegistry.Instance.GetPartsForDomain(_currentDomain);

            foreach (PartData part in parts)
            {
                // Category filter
                if (catFilter == "fuel")
                {
                    // Fuel tab: show utility parts with fuel-related subcategory
                    bool isFuel = string.Equals(part.category, "utility", StringComparison.OrdinalIgnoreCase)
                        && part.subcategory != null && part.subcategory.ToLowerInvariant().Contains("fuel");
                    if (!isFuel) continue;
                }
                else if (catFilter == "utility")
                {
                    // Utility tab: show utility parts that are NOT fuel
                    if (!string.Equals(part.category, "utility", StringComparison.OrdinalIgnoreCase)) continue;
                    bool isFuel = part.subcategory != null && part.subcategory.ToLowerInvariant().Contains("fuel");
                    if (isFuel) continue;
                }
                else if (catFilter != "all" && !string.Equals(part.category, catFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Search filter
                if (!string.IsNullOrEmpty(search))
                {
                    bool match = (part.partName != null && part.partName.ToLowerInvariant().Contains(search))
                              || (part.id != null && part.id.ToLowerInvariant().Contains(search))
                              || (part.category != null && part.category.ToLowerInvariant().Contains(search))
                              || (part.subcategory != null && part.subcategory.ToLowerInvariant().Contains(search));
                    if (!match) continue;
                }

                AddCatalogRow(part);
            }
        }

        private void AddCatalogRow(PartData part)
        {
            var row = CreateUIObject("Part_" + part.id, _catalogContent);
            row.AddComponent<LayoutElement>().preferredHeight = 48f;

            var rowImg = row.AddComponent<Image>();

            // Highlight if this part is currently selected
            bool isSelected = string.Equals(part.id, _selectedPartId, StringComparison.OrdinalIgnoreCase);
            rowImg.color = isSelected
                ? new Color(0.3f, 0.28f, 0.12f, 0.95f)
                : new Color(0.13f, 0.13f, 0.2f, 0.85f);

            // Make the row a button for selection
            var btn = row.AddComponent<Button>();
            btn.targetGraphic = rowImg;
            var colors = btn.colors;
            colors.normalColor = isSelected ? new Color(0.3f, 0.28f, 0.12f, 0.95f) : Color.white;
            colors.highlightedColor = new Color(0.22f, 0.22f, 0.35f, 1f);
            colors.pressedColor = new Color(0.35f, 0.3f, 0.15f, 1f);
            colors.selectedColor = new Color(0.3f, 0.28f, 0.12f, 0.95f);
            btn.colors = colors;

            string partId = part.id;
            PartData capturedPart = part;
            btn.onClick.AddListener(() => OnPartSelected(partId));

            // Hover events for tooltip + scroll forwarding to parent ScrollRect
            var fwd = row.AddComponent<ScrollForwarder>();
            var rowRt = row.GetComponent<RectTransform>();
            fwd.onPointerEnter = () => ShowPartTooltip(capturedPart, rowRt);
            fwd.onPointerExit  = () => HidePartTooltip();

            // Name
            var nameGo = CreateUIObject("Name", row.transform);
            var nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 0.5f);
            nameRt.anchorMax = new Vector2(0.7f, 1f);
            nameRt.offsetMin = new Vector2(8f, 0f);
            nameRt.offsetMax = Vector2.zero;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text = string.IsNullOrEmpty(part.partName) ? part.id : part.partName;
            nameTmp.fontSize = 14f;
            nameTmp.color = Color.white;
            nameTmp.alignment = TextAlignmentOptions.MidlineLeft;
            nameTmp.raycastTarget = false;

            // Cost
            var costGo = CreateUIObject("Cost", row.transform);
            var costRt = costGo.GetComponent<RectTransform>();
            costRt.anchorMin = new Vector2(0.7f, 0.5f);
            costRt.anchorMax = new Vector2(1f, 1f);
            costRt.offsetMin = Vector2.zero;
            costRt.offsetMax = new Vector2(-6f, 0f);
            var costTmp = costGo.AddComponent<TextMeshProUGUI>();
            costTmp.text = $"${part.cost}";
            costTmp.fontSize = 14f;
            costTmp.color = new Color(0.9f, 0.8f, 0.3f, 1f);
            costTmp.alignment = TextAlignmentOptions.MidlineRight;
            costTmp.raycastTarget = false;

            // Subtitle line (category, mass)
            var tipGo = CreateUIObject("Subtitle", row.transform);
            var tipRt = tipGo.GetComponent<RectTransform>();
            tipRt.anchorMin = new Vector2(0f, 0f);
            tipRt.anchorMax = new Vector2(1f, 0.5f);
            tipRt.offsetMin = new Vector2(8f, 0f);
            tipRt.offsetMax = new Vector2(-6f, 0f);
            var tipTmp = tipGo.AddComponent<TextMeshProUGUI>();
            tipTmp.text = $"{part.category}  |  {part.massKg:F1} kg  |  {part.hp} HP  |  {part.size.x}x{part.size.y}x{part.size.z}";
            tipTmp.fontSize = 11f;
            tipTmp.color = new Color(0.55f, 0.55f, 0.65f, 1f);
            tipTmp.alignment = TextAlignmentOptions.MidlineLeft;
            tipTmp.raycastTarget = false;
        }

        private void OnPartSelected(string partId)
        {
            if (_builder == null) return;

            // Toggle off if clicking the same part
            if (string.Equals(_selectedPartId, partId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedPartId = null;
                _builder.SelectPart(null);
                RefreshCatalog();
                return;
            }

            _selectedPartId = partId;

            PartData part = PartRegistry.Instance?.GetPart(partId);
            _builder.SelectPart(part);

            // Refresh catalog to show selection highlight
            RefreshCatalog();
        }

        // ==================================================================
        // Refresh helpers
        // ==================================================================

        private void RefreshBudget()
        {
            if (_builder == null || _budgetBar == null) return;

            int used = _builder.GetUsedBudget();
            int total = _builder.GetBudget();

            if (total <= 0)
            {
                // Unlimited budget
                _budgetBar.value = 0f;
                if (_validationTimer <= 0f)
                    _budgetFill.color = new Color(0.3f, 0.7f, 0.3f, 1f);
                _budgetLabel.text = "<color=#4ecca3>Unlimited</color>";
                return;
            }

            float ratio = (float)used / total;
            _budgetBar.value = Mathf.Clamp01(ratio);

            int remaining = total - used;
            float remainPct = (float)remaining / total;

            // Only reset bar color if not in the middle of a validation flash
            if (_validationTimer <= 0f)
            {
                if (remainPct > 0.5f)
                    _budgetFill.color = new Color(0.3f, 0.7f, 0.3f, 1f);    // Green
                else if (remainPct > 0.25f)
                    _budgetFill.color = new Color(0.94f, 0.75f, 0.25f, 1f);  // Yellow
                else
                    _budgetFill.color = new Color(0.9f, 0.2f, 0.2f, 1f);     // Red
            }

            _budgetLabel.text = $"${FormatNumber(remaining)} / ${FormatNumber(total)}";
        }

        private void RefreshLayerLabel()
        {
            if (_builder == null || _layerLabel == null) return;
            _layerLabel.text = $"Layer: {_builder.GetCurrentLayer()} (Q/E)";
        }

        private void RefreshForwardLabel()
        {
            if (_builder == null || _forwardLabel == null) return;

            float angle = _builder.GetForwardAngle();
            string dir;
            int rounded = Mathf.RoundToInt(angle) % 360;
            if (rounded < 0) rounded += 360;

            switch (rounded)
            {
                case 0:   dir = "+Z"; break;
                case 90:  dir = "+X"; break;
                case 180: dir = "-Z"; break;
                case 270: dir = "-X"; break;
                default:  dir = $"{rounded}\u00b0"; break;
            }

            _forwardLabel.text = $"Front: {dir} (F)";
        }

        private void RefreshStats()
        {
            if (_builder == null || _statsText == null) return;

            int partCount = _builder.GetPartCount();
            VehicleData data = _builder.ExportVehicleData("_stats_");

            float totalMass = data.CalculateTotalMass();
            float totalThrust = 0f;
            float totalDrag = 0f;
            int totalHp = 0;

            if (PartRegistry.Instance != null)
            {
                for (int i = 0; i < data.parts.Count; i++)
                {
                    PartData p = PartRegistry.Instance.GetPart(data.parts[i].id);
                    if (p == null) continue;
                    totalThrust += p.GetStat<float>("thrust", 0f);
                    totalDrag += p.drag;
                    totalHp += p.hp;
                }
            }

            // TWR (Thrust-to-Weight Ratio)
            float weight = totalMass * 9.81f;
            float twr = weight > 0f ? totalThrust / weight : 0f;
            string twrColor = twr >= 1f ? "<color=#4ecca3>" : "<color=#e94560>";

            // Estimated speed
            float estSpeed = totalDrag > 0.01f ? totalThrust / (totalDrag * 50f) : 0f;

            // Weapon count
            int weaponCount = 0;
            bool hasControl = false;
            bool hasPropulsion = false;
            if (PartRegistry.Instance != null)
            {
                for (int i = 0; i < data.parts.Count; i++)
                {
                    PartData pp = PartRegistry.Instance.GetPart(data.parts[i].id);
                    if (pp == null) continue;
                    if (string.Equals(pp.category, "weapons", System.StringComparison.OrdinalIgnoreCase))
                        weaponCount++;
                    if (string.Equals(pp.category, "control", System.StringComparison.OrdinalIgnoreCase))
                        hasControl = true;
                    if (string.Equals(pp.category, "propulsion", System.StringComparison.OrdinalIgnoreCase))
                        hasPropulsion = true;
                }
            }

            string ctrlStr = hasControl ? "<color=#4ecca3>Yes</color>" : "<color=#e94560>MISSING</color>";
            string propStr = hasPropulsion ? "<color=#4ecca3>Yes</color>" : "<color=#e94560>MISSING</color>";

            _statsText.text = $"Parts: {partCount}\n"
                + $"Mass: {totalMass:F1} kg\n"
                + $"HP: {totalHp}\n"
                + $"Thrust: {totalThrust:F0} N\n"
                + $"TWR: {twrColor}{twr:F2}</color>\n"
                + $"Est. Speed: {estSpeed:F1} m/s\n"
                + $"Weapons: {weaponCount}\n"
                + $"Control: {ctrlStr}\n"
                + $"Propulsion: {propStr}";
        }

        // ==================================================================
        // UI Helpers (shared)
        // ==================================================================

        private static string FormatNumber(int n)
        {
            return n.ToString("N0");
        }

        private TMP_Text CreateLabel(Transform parent, string name, string text, float y, float fontSize)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(0.05f, 1f), new Vector2(0.95f, 1f),
                new Vector2(0f, y), new Vector2(0f, 22f), new Vector2(0.5f, 1f));
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = new Color(0.8f, 0.8f, 0.85f, 1f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            return tmp;
        }

        private TMP_Dropdown AddDropdown(Transform parent, string name,
            float xPad, float y, float widthPct, List<string> options)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(xPad / 540f, 1f), new Vector2(xPad / 540f, 1f),
                new Vector2(0f, y), new Vector2(widthPct * 520f, 28f), new Vector2(0f, 1f));

            go.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.28f, 1f);

            var dd = go.AddComponent<TMP_Dropdown>();

            var captionGo = CreateUIObject("Label", go.transform);
            var captionRt = captionGo.GetComponent<RectTransform>();
            captionRt.anchorMin = Vector2.zero;
            captionRt.anchorMax = Vector2.one;
            captionRt.offsetMin = new Vector2(6f, 0f);
            captionRt.offsetMax = new Vector2(-20f, 0f);
            var capTmp = captionGo.AddComponent<TextMeshProUGUI>();
            capTmp.fontSize = 14f;
            capTmp.color = Color.white;
            capTmp.alignment = TextAlignmentOptions.MidlineLeft;
            dd.captionText = capTmp;

            // Minimal template
            var template = CreateUIObject("Template", go.transform);
            var tRt = template.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 0f);
            tRt.anchorMax = new Vector2(1f, 0f);
            tRt.pivot = new Vector2(0.5f, 1f);
            tRt.anchoredPosition = Vector2.zero;
            tRt.sizeDelta = new Vector2(0f, 150f);
            template.AddComponent<Image>().color = new Color(0.14f, 0.14f, 0.2f, 1f);
            var scroll = template.AddComponent<ScrollRect>();

            var vp = CreateUIObject("Viewport", template.transform);
            StretchFull(vp);
            vp.AddComponent<Image>().color = Color.white;
            vp.AddComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = vp.GetComponent<RectTransform>();

            var content = CreateUIObject("Content", vp.transform);
            var cRt = content.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 1f);
            cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot = new Vector2(0.5f, 1f);
            cRt.sizeDelta = new Vector2(0f, 28f);
            scroll.content = cRt;

            var item = CreateUIObject("Item", content.transform);
            var iRt = item.GetComponent<RectTransform>();
            iRt.anchorMin = new Vector2(0f, 0.5f);
            iRt.anchorMax = new Vector2(1f, 0.5f);
            iRt.sizeDelta = new Vector2(0f, 26f);
            var toggle = item.AddComponent<Toggle>();
            var iBg = CreateUIObject("IBg", item.transform);
            StretchFull(iBg);
            iBg.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.28f, 1f);
            toggle.targetGraphic = iBg.GetComponent<Image>();

            var iLbl = CreateUIObject("ILbl", item.transform);
            var ilRt = iLbl.GetComponent<RectTransform>();
            ilRt.anchorMin = Vector2.zero;
            ilRt.anchorMax = Vector2.one;
            ilRt.offsetMin = new Vector2(6f, 0f);
            ilRt.offsetMax = new Vector2(-6f, 0f);
            var ilTmp = iLbl.AddComponent<TextMeshProUGUI>();
            ilTmp.fontSize = 13f;
            ilTmp.color = Color.white;
            ilTmp.alignment = TextAlignmentOptions.MidlineLeft;
            dd.itemText = ilTmp;

            dd.template = tRt;
            template.SetActive(false);

            dd.ClearOptions();
            dd.AddOptions(options);
            return dd;
        }

        private TMP_InputField AddInputField(Transform parent, string name,
            float anchorMinX, float y, float anchorWidthPct, string placeholder)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(anchorMinX, 1f), new Vector2(anchorMinX + anchorWidthPct, 1f),
                new Vector2(0f, y), new Vector2(0f, 28f), new Vector2(0f, 1f));

            go.AddComponent<Image>().color = new Color(0.14f, 0.14f, 0.2f, 1f);

            var textArea = CreateUIObject("TextArea", go.transform);
            var taRt = textArea.GetComponent<RectTransform>();
            taRt.anchorMin = Vector2.zero;
            taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(6f, 2f);
            taRt.offsetMax = new Vector2(-6f, -2f);
            textArea.AddComponent<RectMask2D>();

            var phGo = CreateUIObject("Placeholder", textArea.transform);
            StretchFull(phGo);
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text = placeholder;
            phTmp.fontSize = 13f;
            phTmp.fontStyle = FontStyles.Italic;
            phTmp.color = new Color(0.45f, 0.45f, 0.5f, 1f);
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var txtGo = CreateUIObject("Text", textArea.transform);
            StretchFull(txtGo);
            var txtTmp = txtGo.AddComponent<TextMeshProUGUI>();
            txtTmp.fontSize = 13f;
            txtTmp.color = Color.white;
            txtTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = taRt;
            input.textComponent = txtTmp;
            input.placeholder = phTmp;
            return input;
        }

        private TMP_InputField AddCenteredInputField(Transform parent, string name,
            Vector2 pos, Vector2 size, string placeholder)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                pos, size, new Vector2(0.5f, 0.5f));

            go.AddComponent<Image>().color = new Color(0.14f, 0.14f, 0.2f, 1f);

            var textArea = CreateUIObject("TextArea", go.transform);
            var taRt = textArea.GetComponent<RectTransform>();
            taRt.anchorMin = Vector2.zero;
            taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(8f, 2f);
            taRt.offsetMax = new Vector2(-8f, -2f);
            textArea.AddComponent<RectMask2D>();

            var phGo = CreateUIObject("Placeholder", textArea.transform);
            StretchFull(phGo);
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text = placeholder;
            phTmp.fontSize = 16f;
            phTmp.fontStyle = FontStyles.Italic;
            phTmp.color = new Color(0.5f, 0.5f, 0.55f, 1f);
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var txtGo = CreateUIObject("Text", textArea.transform);
            StretchFull(txtGo);
            var txtTmp = txtGo.AddComponent<TextMeshProUGUI>();
            txtTmp.fontSize = 16f;
            txtTmp.color = Color.white;
            txtTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = taRt;
            input.textComponent = txtTmp;
            input.placeholder = phTmp;
            input.characterLimit = 30;
            return input;
        }

        private Button CreateBarButton(Transform parent, string name, string label,
            float anchorX, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(anchorX, 0.5f), new Vector2(anchorX, 0.5f),
                Vector2.zero, new Vector2(120f, 40f), new Vector2(0.5f, 0.5f));

            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.18f, 0.26f, 0.95f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.highlightedColor = new Color(0.3f, 0.3f, 0.42f, 1f);
            c.pressedColor = new Color(0.45f, 0.4f, 0.2f, 1f);
            btn.colors = c;
            btn.onClick.AddListener(onClick);

            var txtGo = CreateUIObject("Label", go.transform);
            StretchFull(txtGo);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 18f;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;

            return btn;
        }

        private void CreateDialogButton(Transform parent, string name, string label,
            Vector2 offset, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                offset, new Vector2(120f, 38f), new Vector2(0.5f, 0.5f));

            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.3f, 0.95f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var txtGo = CreateUIObject("Label", go.transform);
            StretchFull(txtGo);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 16f;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;
        }

        // ==================================================================
        // Shared helpers
        // ==================================================================

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
