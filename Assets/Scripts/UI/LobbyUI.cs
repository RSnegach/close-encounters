using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using CloseEncounters.Core;

namespace CloseEncounters.UI
{
    /// <summary>
    /// Lobby screen matching the Godot version exactly:
    ///   Left panel:  Domain, Budget, Custom Budget, Arena (Ground/Water selectable),
    ///                AI Difficulty, AI Count
    ///   Right panel: Player Name, Player List, Ready/Unready, Start Match, Back
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        // ── Theme constants (matching Godot) ─────────────────────────────
        private static readonly Color COLOR_BG        = new Color32(26, 26, 46, 255);
        private static readonly Color COLOR_ACCENT    = new Color32(233, 69, 96, 255);
        private static readonly Color COLOR_SECONDARY = new Color32(15, 52, 96, 255);
        private static readonly Color COLOR_TEXT      = new Color32(238, 238, 238, 255);
        private static readonly Color COLOR_GREEN     = new Color32(78, 204, 163, 255);

        // ── Data ─────────────────────────────────────────────────────────
        private static readonly string[] Domains = { "Ground", "Air", "Water" };
        private static readonly string[] Difficulties = { "Easy", "Medium", "Hard" };

        private static readonly string[] BudgetLabels =
        {
            "Amateur $1,000", "Normal $3,000", "Pro $6,000",
            "Legend $10,000", "Unlimited", "Custom"
        };
        private static readonly int[] BudgetValues = { 1000, 3000, 6000, 10000, 0, -1 };
        // 0 = unlimited (no cap), -1 = custom placeholder

        private static readonly Dictionary<string, string[]> ArenaNames = new Dictionary<string, string[]>
        {
            { "Ground", new[] { "Albuquerque", "Fentchester", "Canada", "Florida", "Kyrgyzstan" } },
            { "Water",  new[] { "Archipelago", "Titan's Peak", "Frozen Strait", "Dragon Deez Nuts", "Corsair Bay" } },
            { "Air",    new[] { "Air Arena" } }
        };

        private static readonly Dictionary<string, string[]> ArenaKeys = new Dictionary<string, string[]>
        {
            { "Ground", new[] { "desert_flat", "town", "arctic", "volcanic", "highlands" } },
            { "Water",  new[] { "archipelago", "titans_peak", "frozen_strait", "kraken_lair", "corsair_bay" } },
            { "Air",    new[] { "air_arena" } }
        };

        // ── Widgets ──────────────────────────────────────────────────────
        private Canvas _canvas;
        private TMP_Dropdown _domainDropdown;
        private TMP_Dropdown _arenaDropdown;
        private GameObject _arenaLabel;         // "Arena" heading label (toggleable)
        private GameObject _arenaDropdownGo;    // The dropdown GO (toggleable)
        private TextMeshProUGUI _arenaAutoLabel; // Static "X Arena" for non-selectable domains
        private TMP_Dropdown _budgetDropdown;
        private TMP_InputField _customBudgetInput;
        private GameObject _customBudgetRow;    // The whole row (label + input, toggleable)
        private TMP_Dropdown _difficultyDropdown;
        private GameObject _difficultyRow;      // Difficulty label + dropdown (solo only)
        private Slider _aiCountSlider;
        private TextMeshProUGUI _aiCountLabel;
        private TMP_InputField _playerNameInput;
        private Transform _playerListContent;

        // ── Action buttons ───────────────────────────────────────────────
        private Button _readyBtn;
        private TextMeshProUGUI _readyBtnText;
        private Image _readyBtnImage;
        private Button _startBtn;
        private Button _backBtn;

        // ── State ────────────────────────────────────────────────────────
        private bool _isReady = false;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Start()
        {
            _canvas = GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                Debug.LogError("[LobbyUI] No Canvas found.");
                return;
            }

            BuildUI();
            ApplyDefaults();
            Debug.Log("[LobbyUI] Initialized.");
        }

        // ══════════════════════════════════════════════════════════════════
        // Full UI Build
        // ══════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            // Dark background
            var bg = CreateUIObject("Background", _canvas.transform);
            StretchFull(bg);
            bg.AddComponent<Image>().color = COLOR_BG;
            bg.transform.SetAsFirstSibling();

            BuildSettingsPanel();
            BuildPlayerPanel();
            BuildActionButtons();
        }

        // ── Left Panel: Settings ─────────────────────────────────────────

        private void BuildSettingsPanel()
        {
            var panel = CreateUIObject("SettingsPanel", _canvas.transform);
            Anchor(panel, new Vector2(0.03f, 0.12f), new Vector2(0.52f, 0.88f),
                Vector2.zero, Vector2.zero, new Vector2(0f, 1f));
            panel.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.18f, 0.9f);

            float y = -16f;
            const float rowH = 46f;
            const float labelW = 160f;
            const float fieldW = 240f;
            const float pad = 16f;

            // 1. Domain
            y = AddLabel(panel.transform, "Domain:", pad, y);
            _domainDropdown = AddDropdown(panel.transform, "DomainDD",
                pad + labelW, y, fieldW, new List<string>(Domains));
            _domainDropdown.onValueChanged.AddListener(OnDomainChanged);
            y -= rowH;

            // 2. Arena heading + auto label (for non-selectable domains)
            AddLabel(panel.transform, "Arena:", pad, y);
            _arenaAutoLabel = CreateLabelAt(panel.transform, "ArenaAutoLabel",
                pad + labelW, y, fieldW, "—");
            _arenaAutoLabel.color = COLOR_TEXT;

            // 3. Arena dropdown (visible for Ground/Water, hidden for Air)
            _arenaDropdown = AddDropdown(panel.transform, "ArenaDD",
                pad + labelW, y, fieldW, new List<string> { "---" });
            _arenaDropdown.onValueChanged.AddListener(OnArenaChanged);
            _arenaDropdownGo = _arenaDropdown.gameObject;
            y -= rowH;

            // 4. Budget
            y = AddLabel(panel.transform, "Budget Tier:", pad, y);
            _budgetDropdown = AddDropdown(panel.transform, "BudgetDD",
                pad + labelW, y, fieldW, new List<string>(BudgetLabels));
            _budgetDropdown.onValueChanged.AddListener(OnBudgetChanged);
            y -= rowH;

            // 5. Custom budget row (hidden unless "Custom" selected)
            _customBudgetRow = AddLabelObject(panel.transform, "Custom $:", pad, y);
            _customBudgetInput = AddInputField(panel.transform, "CustomBudgetInput",
                pad + labelW, y, 140f, "2000");
            _customBudgetInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            _customBudgetInput.characterLimit = 7;
            _customBudgetInput.onEndEdit.AddListener(OnCustomBudgetChanged);
            _customBudgetInput.gameObject.SetActive(false);
            _customBudgetRow.SetActive(false);
            y -= rowH;

            // 6. AI Difficulty (solo only)
            _difficultyRow = AddLabelObject(panel.transform, "AI Difficulty:", pad, y);
            _difficultyDropdown = AddDropdown(panel.transform, "DifficultyDD",
                pad + labelW, y, fieldW, new List<string>(Difficulties));
            _difficultyDropdown.onValueChanged.AddListener(OnDifficultyChanged);
            y -= rowH;

            // Show/hide difficulty based on mode
            bool isSolo = GameManager.Instance == null ||
                          GameManager.Instance.Settings.mode == "solo";
            _difficultyRow.SetActive(isSolo);
            _difficultyDropdown.gameObject.SetActive(isSolo);

            // 7. AI Count (slider 1-7)
            y = AddLabel(panel.transform, "Number of AIs:", pad, y);

            var sliderGo = CreateUIObject("AICountSlider", panel.transform);
            Anchor(sliderGo, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(pad + labelW, y), new Vector2(fieldW - 50f, 28f), new Vector2(0f, 1f));
            _aiCountSlider = BuildSlider(sliderGo, 1f, 7f, 1f);
            _aiCountSlider.onValueChanged.AddListener(OnAICountChanged);

            var countGo = CreateUIObject("AICountVal", panel.transform);
            Anchor(countGo, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(pad + labelW + fieldW - 40f, y), new Vector2(40f, 28f), new Vector2(0f, 1f));
            _aiCountLabel = countGo.AddComponent<TextMeshProUGUI>();
            _aiCountLabel.text = "1";
            _aiCountLabel.fontSize = 18f;
            _aiCountLabel.color = Color.white;
            _aiCountLabel.alignment = TextAlignmentOptions.Center;
            y -= rowH;

            // 8. Player Name
            y = AddLabel(panel.transform, "Your Name:", pad, y);
            _playerNameInput = AddInputField(panel.transform, "PlayerNameInput",
                pad + labelW, y, fieldW, "Player");
            _playerNameInput.characterLimit = 20;
            _playerNameInput.onEndEdit.AddListener(OnPlayerNameChanged);
        }

        // ── Right Panel: Player List ─────────────────────────────────────

        private void BuildPlayerPanel()
        {
            var panel = CreateUIObject("PlayerPanel", _canvas.transform);
            Anchor(panel, new Vector2(0.55f, 0.12f), new Vector2(0.97f, 0.88f),
                Vector2.zero, Vector2.zero, new Vector2(0f, 1f));
            panel.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.18f, 0.9f);

            // Header
            var header = CreateUIObject("PlayersHeader", panel.transform);
            Anchor(header, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -8f), new Vector2(0f, 36f), new Vector2(0.5f, 1f));
            var htxt = header.AddComponent<TextMeshProUGUI>();
            htxt.text = "PLAYERS";
            htxt.fontSize = 24f;
            htxt.color = Color.white;
            htxt.alignment = TextAlignmentOptions.Center;
            htxt.fontStyle = FontStyles.Bold;

            // Scroll view for player list
            var scrollGo = CreateUIObject("PlayerScroll", panel.transform);
            Anchor(scrollGo, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.90f),
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
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
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
            scroll.scrollSensitivity = 20f;

            _playerListContent = content.transform;
        }

        // ── Bottom: Ready + Start + Back ─────────────────────────────────

        private void BuildActionButtons()
        {
            // Ready button
            _readyBtn = CreateBottomButton("ReadyBtn", "Ready", 0.35f, OnReadyPressed);
            _readyBtnImage = _readyBtn.GetComponent<Image>();
            _readyBtnText = _readyBtn.GetComponentInChildren<TextMeshProUGUI>();

            // Start Match button (host/solo only)
            _startBtn = CreateBottomButton("StartBtn", "Start Match", 0.65f, OnStartPressed);
            var startImg = _startBtn.GetComponent<Image>();
            if (startImg != null) startImg.color = COLOR_ACCENT;

            bool isSolo = GameManager.Instance == null ||
                          GameManager.Instance.Settings.mode == "solo";
            bool isHost = GameManager.Instance != null &&
                          GameManager.Instance.Settings.mode == "host";
            _startBtn.gameObject.SetActive(isSolo || isHost);
            _startBtn.interactable = false; // Disabled until ready

            // Back button
            _backBtn = CreateBottomButton("BackBtn", "Back", 0.1f, OnBackPressed);
        }

        // ══════════════════════════════════════════════════════════════════
        // Event handlers
        // ══════════════════════════════════════════════════════════════════

        private void OnDomainChanged(int index)
        {
            if (GameManager.Instance == null) return;

            string domain = Domains[index];
            GameManager.Instance.Settings.domain = domain.ToLowerInvariant();

            RefreshArenas();
        }

        private void OnArenaChanged(int index)
        {
            if (GameManager.Instance == null) return;

            string domain = Domains[_domainDropdown.value];
            if (ArenaKeys.TryGetValue(domain, out string[] keys))
            {
                int clampedIndex = Mathf.Clamp(index, 0, keys.Length - 1);
                GameManager.Instance.Settings.arena = keys[clampedIndex];
            }
        }

        private void OnBudgetChanged(int index)
        {
            if (GameManager.Instance == null) return;

            bool isCustom = index == 5; // "Custom" is the last entry
            _customBudgetInput.gameObject.SetActive(isCustom);
            _customBudgetRow.SetActive(isCustom);

            if (isCustom)
            {
                if (int.TryParse(_customBudgetInput.text, out int val) && val > 0)
                    GameManager.Instance.Settings.budget = val;
                else
                    GameManager.Instance.Settings.budget = 2000;
            }
            else
            {
                int budgetVal = BudgetValues[index];
                // 0 = unlimited
                GameManager.Instance.Settings.budget = budgetVal == 0 ? 0 : budgetVal;

                switch (index)
                {
                    case 0: GameManager.Instance.Settings.SetBudgetTier(BudgetTier.Amateur); break;
                    case 1: GameManager.Instance.Settings.SetBudgetTier(BudgetTier.Normal); break;
                    case 2: GameManager.Instance.Settings.SetBudgetTier(BudgetTier.Pro); break;
                    case 3: GameManager.Instance.Settings.SetBudgetTier(BudgetTier.Legend); break;
                    case 4: GameManager.Instance.Settings.SetBudgetTier(BudgetTier.Unlimited); break;
                }
            }
        }

        private void OnCustomBudgetChanged(string text)
        {
            if (GameManager.Instance == null) return;
            if (int.TryParse(text, out int val) && val > 0)
                GameManager.Instance.Settings.budget = val;
        }

        private void OnDifficultyChanged(int index)
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.Settings.aiDifficulty = index;
        }

        private void OnAICountChanged(float value)
        {
            int count = Mathf.RoundToInt(value);
            _aiCountSlider.value = count;
            _aiCountLabel.text = count.ToString();

            if (GameManager.Instance == null) return;
            GameManager.Instance.Settings.aiCount = count;
            RefreshPlayerList();
        }

        private void OnPlayerNameChanged(string text)
        {
            if (GameManager.Instance == null) return;
            string name = string.IsNullOrWhiteSpace(text) ? "Player" : text;
            GameManager.Instance.SetPlayerName(1, name);
            RefreshPlayerList();
        }

        // ── Ready toggle ─────────────────────────────────────────────────

        private void OnReadyPressed()
        {
            _isReady = !_isReady;

            // Update button text and color
            if (_readyBtnText != null)
                _readyBtnText.text = _isReady ? "Unready" : "Ready";
            if (_readyBtnImage != null)
                _readyBtnImage.color = _isReady ? COLOR_GREEN : COLOR_SECONDARY;

            // In solo mode auto-enable start
            bool isSolo = GameManager.Instance == null ||
                          GameManager.Instance.Settings.mode == "solo";
            if (isSolo)
                _startBtn.interactable = _isReady;

            RefreshPlayerList();
        }

        // ── Start / Back ─────────────────────────────────────────────────

        private void OnStartPressed()
        {
            if (GameManager.Instance == null) return;

            // Commit player name
            string playerName = _playerNameInput != null ? _playerNameInput.text : "Player";
            if (string.IsNullOrWhiteSpace(playerName)) playerName = "Player";
            GameManager.Instance.SetPlayerName(1, playerName);

            // Set AI names
            int aiCount = Mathf.RoundToInt(_aiCountSlider.value);
            for (int i = 0; i < aiCount; i++)
            {
                string aiName = aiCount == 1 ? "Clanker" : $"Clanker {i + 1}";
                GameManager.Instance.SetPlayerName(i + 2, aiName);
            }

            GameManager.Instance.Settings.playerCount = 1 + aiCount;
            SceneManager.LoadScene("Builder");
        }

        private void OnBackPressed()
        {
            SceneManager.LoadScene("MainMenu");
        }

        // ══════════════════════════════════════════════════════════════════
        // Refresh helpers
        // ══════════════════════════════════════════════════════════════════

        private void RefreshArenas()
        {
            if (_arenaDropdown == null || _domainDropdown == null) return;

            string domain = Domains[_domainDropdown.value];

            bool hasSelector = domain == "Ground" || domain == "Water";

            // Show/hide arena selector
            _arenaDropdownGo.SetActive(hasSelector);
            _arenaAutoLabel.gameObject.SetActive(!hasSelector);

            if (hasSelector)
            {
                _arenaDropdown.ClearOptions();
                if (ArenaNames.TryGetValue(domain, out string[] names))
                    _arenaDropdown.AddOptions(new List<string>(names));
                else
                    _arenaDropdown.AddOptions(new List<string> { "Default Arena" });

                // Default to Fentchester (index 1) for Ground, index 0 for others
                int defaultIdx = domain == "Ground" ? Mathf.Min(1, _arenaDropdown.options.Count - 1) : 0;
                _arenaDropdown.value = defaultIdx;
                _arenaDropdown.RefreshShownValue();
                OnArenaChanged(defaultIdx);
            }
            else
            {
                // Static label for Air
                _arenaAutoLabel.text = domain + " Arena";
                if (ArenaKeys.TryGetValue(domain, out string[] keys) && keys.Length > 0)
                {
                    if (GameManager.Instance != null)
                        GameManager.Instance.Settings.arena = keys[0];
                }
            }
        }

        private void RefreshPlayerList()
        {
            if (_playerListContent == null) return;

            // Clear
            for (int i = _playerListContent.childCount - 1; i >= 0; i--)
                Destroy(_playerListContent.GetChild(i).gameObject);

            // Local player
            string localName = _playerNameInput != null ? _playerNameInput.text : "Player";
            if (string.IsNullOrWhiteSpace(localName)) localName = "Player";
            string readySuffix = _isReady ? " — ready" : "";
            AddPlayerRow($"{localName} (you){readySuffix}", true);

            // AI players
            int aiCount = _aiCountSlider != null ? Mathf.RoundToInt(_aiCountSlider.value) : 1;
            if (aiCount == 1)
            {
                AddPlayerRow("Computer (AI)", false);
            }
            else
            {
                for (int i = 0; i < aiCount; i++)
                    AddPlayerRow($"Computer {i + 1} (AI)", false);
            }
        }

        private void AddPlayerRow(string displayName, bool isLocal)
        {
            var row = CreateUIObject("PlayerRow", _playerListContent);
            row.AddComponent<LayoutElement>().preferredHeight = 34f;

            var rowBg = row.AddComponent<Image>();
            rowBg.color = isLocal
                ? new Color(0.15f, 0.28f, 0.15f, 0.8f)
                : new Color(0.15f, 0.15f, 0.22f, 0.8f);

            var txtGo = CreateUIObject("Name", row.transform);
            StretchFull(txtGo);
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text = $"  {displayName}";
            tmp.fontSize = 17f;
            tmp.color = isLocal ? new Color(0.5f, 1f, 0.5f) : new Color(0.75f, 0.75f, 0.85f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }

        // ══════════════════════════════════════════════════════════════════
        // Apply defaults — restores from GameManager.Settings if returning
        // from Builder/Combat, otherwise uses fresh defaults
        // ══════════════════════════════════════════════════════════════════

        private void ApplyDefaults()
        {
            var gm = GameManager.Instance;
            bool hasExisting = gm != null && !string.IsNullOrEmpty(gm.Settings.domain);

            // --- Domain ---
            int domainIdx = 0; // Ground
            if (hasExisting)
            {
                string saved = gm.Settings.domain.ToLowerInvariant();
                for (int i = 0; i < Domains.Length; i++)
                {
                    if (Domains[i].ToLowerInvariant() == saved) { domainIdx = i; break; }
                }
            }
            _domainDropdown.value = domainIdx;
            OnDomainChanged(domainIdx);

            // --- Arena (restore selection after domain refreshed the dropdown) ---
            if (hasExisting && !string.IsNullOrEmpty(gm.Settings.arena))
            {
                string savedArena = gm.Settings.arena;
                string domain = Domains[domainIdx];
                if (ArenaKeys.TryGetValue(domain, out string[] keys))
                {
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (string.Equals(keys[i], savedArena, System.StringComparison.OrdinalIgnoreCase))
                        {
                            _arenaDropdown.value = i;
                            _arenaDropdown.RefreshShownValue();
                            OnArenaChanged(i);
                            break;
                        }
                    }
                }
            }

            // --- Budget ---
            int budgetIdx = 4; // Unlimited
            if (hasExisting)
            {
                int savedBudget = gm.Settings.budget;
                // Try to match a preset tier
                for (int i = 0; i < BudgetValues.Length; i++)
                {
                    if (BudgetValues[i] == savedBudget) { budgetIdx = i; break; }
                }
                // If it was a custom value not in the presets, select Custom
                if (budgetIdx == 4 && savedBudget > 0 && savedBudget != 0)
                {
                    // Check it's not one of the named tiers
                    bool found = false;
                    for (int i = 0; i < BudgetValues.Length - 1; i++)
                    {
                        if (BudgetValues[i] == savedBudget) { found = true; break; }
                    }
                    if (!found)
                    {
                        budgetIdx = 5; // Custom
                    }
                }
            }
            _budgetDropdown.value = budgetIdx;
            OnBudgetChanged(budgetIdx);
            if (budgetIdx == 5 && hasExisting)
            {
                _customBudgetInput.text = gm.Settings.budget.ToString();
            }

            // --- Difficulty ---
            int diffIdx = hasExisting ? Mathf.Clamp(gm.Settings.aiDifficulty, 0, 2) : 1;
            _difficultyDropdown.value = diffIdx;
            OnDifficultyChanged(diffIdx);

            // --- AI count ---
            int aiCount = hasExisting ? Mathf.Clamp(gm.Settings.aiCount, 1, 7) : 1;
            _aiCountSlider.value = aiCount;
            OnAICountChanged(aiCount);

            // --- Player name ---
            string playerName = "Player";
            if (gm != null)
            {
                string saved = gm.GetPlayerName(1);
                if (!string.IsNullOrWhiteSpace(saved) && saved != "Player 1")
                    playerName = saved;
            }
            if (_playerNameInput != null)
            {
                _playerNameInput.text = playerName;
                OnPlayerNameChanged(playerName);
            }

            RefreshPlayerList();
        }

        // ══════════════════════════════════════════════════════════════════
        // UI factory helpers
        // ══════════════════════════════════════════════════════════════════

        private float AddLabel(Transform parent, string text, float x, float y)
        {
            AddLabelObject(parent, text, x, y);
            return y;
        }

        private GameObject AddLabelObject(Transform parent, string text, float x, float y)
        {
            var go = CreateUIObject("Lbl_" + text.Replace(":", ""), parent);
            Anchor(go, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(150f, 30f), new Vector2(0f, 1f));
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 17f;
            tmp.color = new Color(0.8f, 0.8f, 0.85f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            return go;
        }

        private TextMeshProUGUI CreateLabelAt(Transform parent, string name,
            float x, float y, float w, string text)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(w, 30f), new Vector2(0f, 1f));
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 16f;
            tmp.color = COLOR_TEXT;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            return tmp;
        }

        private TMP_Dropdown AddDropdown(Transform parent, string name,
            float x, float y, float w, List<string> options)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(w, 30f), new Vector2(0f, 1f));

            go.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.28f, 1f);

            var dd = go.AddComponent<TMP_Dropdown>();

            // Caption
            var captionGo = CreateUIObject("Label", go.transform);
            var captionRt = captionGo.GetComponent<RectTransform>();
            captionRt.anchorMin = Vector2.zero;
            captionRt.anchorMax = Vector2.one;
            captionRt.offsetMin = new Vector2(8f, 0f);
            captionRt.offsetMax = new Vector2(-28f, 0f);
            var captionTmp = captionGo.AddComponent<TextMeshProUGUI>();
            captionTmp.fontSize = 16f;
            captionTmp.color = Color.white;
            captionTmp.alignment = TextAlignmentOptions.MidlineLeft;
            dd.captionText = captionTmp;

            // Arrow
            var arrowGo = CreateUIObject("Arrow", go.transform);
            var arrowRt = arrowGo.GetComponent<RectTransform>();
            arrowRt.anchorMin = new Vector2(1f, 0f);
            arrowRt.anchorMax = new Vector2(1f, 1f);
            arrowRt.sizeDelta = new Vector2(24f, 0f);
            arrowRt.anchoredPosition = new Vector2(-14f, 0f);
            var arrowTmp = arrowGo.AddComponent<TextMeshProUGUI>();
            arrowTmp.text = "\u25BC";
            arrowTmp.fontSize = 12f;
            arrowTmp.color = new Color(0.7f, 0.7f, 0.8f);
            arrowTmp.alignment = TextAlignmentOptions.Center;

            // Template
            var template = CreateUIObject("Template", go.transform);
            var tRt = template.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 0f);
            tRt.anchorMax = new Vector2(1f, 0f);
            tRt.pivot = new Vector2(0.5f, 1f);
            tRt.anchoredPosition = Vector2.zero;
            tRt.sizeDelta = new Vector2(0f, 150f);

            template.AddComponent<Image>().color = new Color(0.16f, 0.16f, 0.22f, 1f);
            var scroll = template.AddComponent<ScrollRect>();

            var viewport = CreateUIObject("Viewport", template.transform);
            StretchFull(viewport);
            viewport.AddComponent<Image>().color = Color.white;
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = viewport.GetComponent<RectTransform>();

            var content = CreateUIObject("Content", viewport.transform);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 30f);
            scroll.content = contentRt;

            // Item template
            var item = CreateUIObject("Item", content.transform);
            var itemRt = item.GetComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0f, 0.5f);
            itemRt.anchorMax = new Vector2(1f, 0.5f);
            itemRt.sizeDelta = new Vector2(0f, 28f);
            var toggle = item.AddComponent<Toggle>();

            var itemBg = CreateUIObject("ItemBg", item.transform);
            StretchFull(itemBg);
            var itemBgImg = itemBg.AddComponent<Image>();
            itemBgImg.color = new Color(0.22f, 0.22f, 0.3f, 1f);
            toggle.targetGraphic = itemBgImg;

            var itemLabel = CreateUIObject("ItemLabel", item.transform);
            var itemLabelRt = itemLabel.GetComponent<RectTransform>();
            itemLabelRt.anchorMin = Vector2.zero;
            itemLabelRt.anchorMax = Vector2.one;
            itemLabelRt.offsetMin = new Vector2(8f, 0f);
            itemLabelRt.offsetMax = new Vector2(-8f, 0f);
            var itemTmp = itemLabel.AddComponent<TextMeshProUGUI>();
            itemTmp.fontSize = 15f;
            itemTmp.color = Color.white;
            itemTmp.alignment = TextAlignmentOptions.MidlineLeft;
            dd.itemText = itemTmp;

            if (template.GetComponent<CanvasGroup>() == null)
                template.AddComponent<CanvasGroup>();

            dd.template = tRt;
            template.SetActive(false);

            dd.ClearOptions();
            dd.AddOptions(options);

            return dd;
        }

        private TMP_InputField AddInputField(Transform parent, string name,
            float x, float y, float w, string placeholder)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(w, 30f), new Vector2(0f, 1f));

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
            phTmp.fontSize = 15f;
            phTmp.fontStyle = FontStyles.Italic;
            phTmp.color = new Color(0.5f, 0.5f, 0.55f);
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var txtGo = CreateUIObject("Text", textArea.transform);
            StretchFull(txtGo);
            var txtTmp = txtGo.AddComponent<TextMeshProUGUI>();
            txtTmp.fontSize = 15f;
            txtTmp.color = Color.white;
            txtTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = taRt;
            input.textComponent = txtTmp;
            input.placeholder = phTmp;
            input.text = placeholder;

            return input;
        }

        private Slider BuildSlider(GameObject go, float min, float max, float value)
        {
            var slider = go.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.value = value;

            var bg = CreateUIObject("SliderBg", go.transform);
            StretchFull(bg);
            bg.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.28f, 1f);
            slider.targetGraphic = bg.GetComponent<Image>();

            var fillArea = CreateUIObject("FillArea", go.transform);
            StretchFull(fillArea);
            var fill = CreateUIObject("Fill", fillArea.transform);
            var fillRt = StretchFull(fill);
            fill.AddComponent<Image>().color = new Color(0.3f, 0.55f, 0.85f, 1f);
            slider.fillRect = fillRt;

            var handleArea = CreateUIObject("HandleArea", go.transform);
            StretchFull(handleArea);
            var handle = CreateUIObject("Handle", handleArea.transform);
            Anchor(handle, new Vector2(0f, 0f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(12f, 0f), new Vector2(0.5f, 0.5f));
            handle.AddComponent<Image>().color = Color.white;
            slider.handleRect = handle.GetComponent<RectTransform>();

            return slider;
        }

        private Button CreateBottomButton(string name, string label, float anchorX,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateUIObject(name, _canvas.transform);
            Anchor(go, new Vector2(anchorX, 0.03f), new Vector2(anchorX, 0.03f),
                Vector2.zero, new Vector2(200f, 48f), new Vector2(0.5f, 0.5f));

            var img = go.AddComponent<Image>();
            img.color = COLOR_SECONDARY;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.3f, 0.3f, 0.42f, 1f);
            colors.pressedColor = new Color(0.45f, 0.4f, 0.2f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var txtGo = CreateUIObject("Label", go.transform);
            StretchFull(txtGo);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 20f;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;

            return btn;
        }

        // ══════════════════════════════════════════════════════════════════
        // Low-level helpers
        // ══════════════════════════════════════════════════════════════════

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static RectTransform Anchor(GameObject go,
            Vector2 anchorMin, Vector2 anchorMax,
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
