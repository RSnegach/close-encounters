using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CloseEncounters.Core;

namespace CloseEncounters.UI
{
    /// <summary>
    /// Main menu screen matching the Godot version:
    ///   - Title "CLOSE ENCOUNTERS" in accent red
    ///   - Solo Match / Host Game / Join Game / Quit buttons
    ///   - Server browser panel (shown when Join Game is clicked)
    ///   - Manual IP entry dialog
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        // ── Theme constants (matching Godot) ─────────────────────────────
        private static readonly Color COLOR_BG        = new Color32(26, 26, 46, 255);    // #1a1a2e
        private static readonly Color COLOR_ACCENT    = new Color32(233, 69, 96, 255);   // #e94560
        private static readonly Color COLOR_SECONDARY = new Color32(15, 52, 96, 255);    // #0f3460
        private static readonly Color COLOR_TEXT      = new Color32(238, 238, 238, 255);  // #eeeeee
        private static readonly Color COLOR_GREEN     = new Color32(78, 204, 163, 255);  // #4ecca3
        private static readonly Color COLOR_DIM       = new Color32(136, 136, 136, 255); // #888888

        private Canvas _canvas;

        // ── Main menu widgets ────────────────────────────────────────────
        private GameObject _menuVBox;
        private Button _soloBtn;
        private Button _hostBtn;
        private Button _joinBtn;
        private Button _quitBtn;

        // ── Server browser ───────────────────────────────────────────────
        private GameObject _browserPanel;
        private TextMeshProUGUI _browserStatus;
        private Transform _gameListContent;
        private Button _refreshBtn;
        private Button _enterIpBtn;
        private Button _browserBackBtn;

        // ── Manual IP dialog ─────────────────────────────────────────────
        private GameObject _ipDialog;
        private TMP_InputField _ipInput;

        public void Initialize()
        {
            _canvas = GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = GetComponentInParent<Canvas>();

            BuildBackground();
            BuildMenuButtons();
            BuildServerBrowser();
            BuildIpDialog();

            Debug.Log("[MainMenuUI] Initialized.");
        }

        // =================================================================
        // Background
        // =================================================================

        private void BuildBackground()
        {
            var go = CreateUIObject("Background", _canvas.transform);
            StretchFull(go);
            var img = go.AddComponent<Image>();
            img.color = COLOR_BG;
            go.transform.SetAsFirstSibling();
        }

        // =================================================================
        // Central menu buttons
        // =================================================================

        private void BuildMenuButtons()
        {
            // VBox centered on screen
            _menuVBox = CreateUIObject("MenuVBox", _canvas.transform);
            Anchor(_menuVBox, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 40f), new Vector2(320f, 360f), new Vector2(0.5f, 0.5f));

            var vlg = _menuVBox.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 16f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;

            // Title
            var titleGo = CreateUIObject("Title", _menuVBox.transform);
            titleGo.AddComponent<LayoutElement>().preferredHeight = 60f;
            var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "CLOSE ENCOUNTERS";
            titleTmp.fontSize = 48f;
            titleTmp.color = COLOR_ACCENT;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.fontStyle = FontStyles.Bold;

            // Spacer
            var spacer = CreateUIObject("Spacer", _menuVBox.transform);
            spacer.AddComponent<LayoutElement>().preferredHeight = 24f;

            // Buttons
            _soloBtn = CreateMenuButton("Solo Match");
            _hostBtn = CreateMenuButton("Host Game");
            _joinBtn = CreateMenuButton("Join Game");
            _quitBtn = CreateMenuButton("Quit");

            _soloBtn.onClick.AddListener(OnSolo);
            _hostBtn.onClick.AddListener(OnHost);
            _joinBtn.onClick.AddListener(OnJoin);
            _quitBtn.onClick.AddListener(OnQuit);
        }

        // =================================================================
        // Server browser panel
        // =================================================================

        private void BuildServerBrowser()
        {
            _browserPanel = CreateUIObject("BrowserPanel", _canvas.transform);
            Anchor(_browserPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(600f, 450f), new Vector2(0.5f, 0.5f));

            // Panel background
            var panelImg = _browserPanel.AddComponent<Image>();
            panelImg.color = Lighten(COLOR_BG, 0.05f);

            // Border via Outline
            var outline = _browserPanel.AddComponent<Outline>();
            outline.effectColor = COLOR_SECONDARY;
            outline.effectDistance = new Vector2(2f, -2f);

            // Inner layout
            var innerGo = CreateUIObject("Inner", _browserPanel.transform);
            var innerRt = innerGo.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(16f, 16f);
            innerRt.offsetMax = new Vector2(-16f, -16f);
            var innerVlg = innerGo.AddComponent<VerticalLayoutGroup>();
            innerVlg.spacing = 10f;
            innerVlg.childForceExpandWidth = true;
            innerVlg.childForceExpandHeight = false;
            innerVlg.childControlWidth = true;
            innerVlg.childControlHeight = false;

            // Title
            var titleGo = CreateUIObject("BrowserTitle", innerGo.transform);
            titleGo.AddComponent<LayoutElement>().preferredHeight = 36f;
            var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "FIND GAME";
            titleTmp.fontSize = 28f;
            titleTmp.color = COLOR_ACCENT;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.fontStyle = FontStyles.Bold;

            // Status
            var statusGo = CreateUIObject("BrowserStatus", innerGo.transform);
            statusGo.AddComponent<LayoutElement>().preferredHeight = 20f;
            _browserStatus = statusGo.AddComponent<TextMeshProUGUI>();
            _browserStatus.text = "Searching for games...";
            _browserStatus.fontSize = 14f;
            _browserStatus.color = COLOR_DIM;
            _browserStatus.alignment = TextAlignmentOptions.Center;

            // Scrollable game list
            BuildGameListScroll(innerGo.transform);

            // Button row at bottom
            var btnRow = CreateUIObject("BtnRow", innerGo.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 40f;
            var btnHlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            btnHlg.spacing = 8f;
            btnHlg.childAlignment = TextAnchor.MiddleCenter;
            btnHlg.childForceExpandWidth = false;
            btnHlg.childForceExpandHeight = false;
            btnHlg.childControlWidth = false;
            btnHlg.childControlHeight = false;

            _refreshBtn = CreateSmallButton("Refresh", COLOR_SECONDARY, btnRow.transform);
            _enterIpBtn = CreateSmallButton("Enter IP", Darken(COLOR_SECONDARY, 0.1f), btnRow.transform);
            _browserBackBtn = CreateSmallButton("Back", Darken(COLOR_SECONDARY, 0.2f), btnRow.transform);

            _refreshBtn.onClick.AddListener(OnRefresh);
            _enterIpBtn.onClick.AddListener(OnEnterIp);
            _browserBackBtn.onClick.AddListener(OnBrowserBack);

            _browserPanel.SetActive(false);
        }

        private void BuildGameListScroll(Transform parent)
        {
            var scrollGo = CreateUIObject("GameScroll", parent);
            scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1f;

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

            _gameListContent = content.transform;
        }

        // =================================================================
        // Manual IP dialog
        // =================================================================

        private void BuildIpDialog()
        {
            _ipDialog = CreateUIObject("IpDialog", _canvas.transform);
            StretchFull(_ipDialog);
            var overlay = _ipDialog.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.65f);

            var box = CreateUIObject("IpBox", _ipDialog.transform);
            Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(360f, 160f), new Vector2(0.5f, 0.5f));
            box.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.16f, 0.98f);

            // Title
            var heading = CreateUIObject("Heading", box.transform);
            Anchor(heading, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), new Vector2(320f, 28f), new Vector2(0.5f, 1f));
            var hTmp = heading.AddComponent<TextMeshProUGUI>();
            hTmp.text = "Connect by IP";
            hTmp.fontSize = 20f;
            hTmp.color = Color.white;
            hTmp.alignment = TextAlignmentOptions.Center;
            hTmp.fontStyle = FontStyles.Bold;

            // Label
            var labelGo = CreateUIObject("IpLabel", box.transform);
            Anchor(labelGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 22f), new Vector2(300f, 22f), new Vector2(0.5f, 0.5f));
            var lTmp = labelGo.AddComponent<TextMeshProUGUI>();
            lTmp.text = "Server IP Address:";
            lTmp.fontSize = 15f;
            lTmp.color = COLOR_TEXT;
            lTmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Input field
            _ipInput = AddCenteredInputField(box.transform, "IpInput",
                new Vector2(0f, -6f), new Vector2(300f, 32f), "e.g. 73.42.100.5 or 192.168.1.10");

            // OK / Cancel buttons
            CreateDialogButton(box.transform, "IpOk", "OK",
                new Vector2(-60f, -50f), OnIpConfirmed);
            CreateDialogButton(box.transform, "IpCancel", "Cancel",
                new Vector2(60f, -50f), () => _ipDialog.SetActive(false));

            _ipDialog.SetActive(false);
        }

        // =================================================================
        // Button handlers
        // =================================================================

        private void OnSolo()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Settings.mode = "solo";
                GameManager.Instance.TransitionToState(GameState.Lobby);
            }
        }

        private void OnHost()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Settings.mode = "host";
                GameManager.Instance.TransitionToState(GameState.Lobby);
            }
        }

        private void OnJoin()
        {
            // Hide main menu buttons, show server browser
            _menuVBox.SetActive(false);
            _browserPanel.SetActive(true);
            _browserStatus.text = "Searching for games...";

            // Clear game list
            ClearGameList();

            // TODO: NetworkManager.start_discovery() equivalent
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ── Server browser handlers ──────────────────────────────────────

        private void OnRefresh()
        {
            _browserStatus.text = "Refreshing...";
            ClearGameList();
            // TODO: NetworkManager.start_discovery()
        }

        private void OnEnterIp()
        {
            if (_ipInput != null) _ipInput.text = "";
            _ipDialog.SetActive(true);
        }

        private void OnBrowserBack()
        {
            _browserPanel.SetActive(false);
            _menuVBox.SetActive(true);
            // TODO: NetworkManager.stop_discovery()
        }

        private void OnIpConfirmed()
        {
            string ip = _ipInput != null ? _ipInput.text.Trim() : "";
            if (string.IsNullOrEmpty(ip)) return;

            _ipDialog.SetActive(false);
            _browserPanel.SetActive(false);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.Settings.mode = "join";
                GameManager.Instance.TransitionToState(GameState.Lobby);
            }
        }

        private void ClearGameList()
        {
            if (_gameListContent == null) return;
            for (int i = _gameListContent.childCount - 1; i >= 0; i--)
                Destroy(_gameListContent.GetChild(i).gameObject);
        }

        /// <summary>
        /// Called externally when games are discovered on the network.
        /// Pass a list of game info dicts.
        /// </summary>
        public void OnGamesDiscovered(GameInfo[] games)
        {
            ClearGameList();

            if (games == null || games.Length == 0)
            {
                _browserStatus.text = "No games found. Make sure the host is running.";
                return;
            }

            _browserStatus.text = $"{games.Length} game(s) found";

            foreach (var info in games)
                AddGameEntry(info);
        }

        private void AddGameEntry(GameInfo info)
        {
            var row = CreateUIObject("GameEntry", _gameListContent);
            row.AddComponent<LayoutElement>().preferredHeight = 44f;
            var rowImg = row.AddComponent<Image>();
            rowImg.color = new Color32(37, 37, 64, 255); // #252540

            // Source tag [LAN] or [NET]
            var sourceGo = CreateUIObject("Source", row.transform);
            var sourceRt = sourceGo.GetComponent<RectTransform>();
            sourceRt.anchorMin = new Vector2(0f, 0f);
            sourceRt.anchorMax = new Vector2(0.08f, 1f);
            sourceRt.offsetMin = new Vector2(8f, 0f);
            sourceRt.offsetMax = Vector2.zero;
            var sourceTmp = sourceGo.AddComponent<TextMeshProUGUI>();
            sourceTmp.text = info.isLan ? "[LAN]" : "[NET]";
            sourceTmp.fontSize = 12f;
            sourceTmp.color = info.isLan ? COLOR_GREEN : new Color32(102, 170, 255, 255);
            sourceTmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Game name
            var nameGo = CreateUIObject("Name", row.transform);
            var nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0.09f, 0f);
            nameRt.anchorMax = new Vector2(0.55f, 1f);
            nameRt.offsetMin = Vector2.zero;
            nameRt.offsetMax = Vector2.zero;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text = info.gameName;
            nameTmp.fontSize = 16f;
            nameTmp.color = COLOR_TEXT;
            nameTmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Domain
            var domainGo = CreateUIObject("Domain", row.transform);
            var domainRt = domainGo.GetComponent<RectTransform>();
            domainRt.anchorMin = new Vector2(0.56f, 0f);
            domainRt.anchorMax = new Vector2(0.72f, 1f);
            domainRt.offsetMin = Vector2.zero;
            domainRt.offsetMax = Vector2.zero;
            var domainTmp = domainGo.AddComponent<TextMeshProUGUI>();
            domainTmp.text = info.domain;
            domainTmp.fontSize = 14f;
            domainTmp.color = COLOR_DIM;
            domainTmp.alignment = TextAlignmentOptions.Center;

            // Player count
            var playersGo = CreateUIObject("Players", row.transform);
            var playersRt = playersGo.GetComponent<RectTransform>();
            playersRt.anchorMin = new Vector2(0.73f, 0f);
            playersRt.anchorMax = new Vector2(0.82f, 1f);
            playersRt.offsetMin = Vector2.zero;
            playersRt.offsetMax = Vector2.zero;
            var playersTmp = playersGo.AddComponent<TextMeshProUGUI>();
            playersTmp.text = $"{info.currentPlayers}/{info.maxPlayers}";
            playersTmp.fontSize = 14f;
            playersTmp.color = COLOR_DIM;
            playersTmp.alignment = TextAlignmentOptions.Center;

            // Join button
            var joinGo = CreateUIObject("JoinBtn", row.transform);
            var joinRt = joinGo.GetComponent<RectTransform>();
            joinRt.anchorMin = new Vector2(0.84f, 0.15f);
            joinRt.anchorMax = new Vector2(0.98f, 0.85f);
            joinRt.offsetMin = Vector2.zero;
            joinRt.offsetMax = Vector2.zero;
            joinGo.AddComponent<Image>().color = COLOR_ACCENT;
            var joinBtn = joinGo.AddComponent<Button>();
            string capturedAddr = info.address;
            int capturedPort = info.port;
            joinBtn.onClick.AddListener(() => OnJoinGame(capturedAddr, capturedPort));
            var joinTxt = CreateUIObject("T", joinGo.transform);
            StretchFull(joinTxt);
            var jtmp = joinTxt.AddComponent<TextMeshProUGUI>();
            jtmp.text = "Join";
            jtmp.fontSize = 14f;
            jtmp.color = COLOR_TEXT;
            jtmp.alignment = TextAlignmentOptions.Center;
        }

        private void OnJoinGame(string address, int port)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Settings.mode = "join";
                GameManager.Instance.TransitionToState(GameState.Lobby);
            }
        }

        // =================================================================
        // UI factories
        // =================================================================

        private Button CreateMenuButton(string label)
        {
            var go = CreateUIObject("Btn_" + label, _menuVBox.transform);
            go.AddComponent<LayoutElement>().preferredHeight = 48f;

            var img = go.AddComponent<Image>();
            img.color = COLOR_SECONDARY;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Lighten(COLOR_SECONDARY, 0.15f);
            colors.pressedColor = COLOR_ACCENT;
            btn.colors = colors;

            var txtGo = CreateUIObject("Label", go.transform);
            StretchFull(txtGo);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 22f;
            txt.color = COLOR_TEXT;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;

            return btn;
        }

        private Button CreateSmallButton(string label, Color bgColor, Transform parent)
        {
            var go = CreateUIObject("Btn_" + label, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(90f, 36f);

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = Lighten(bgColor, 0.15f);
            btn.colors = colors;

            var txtGo = CreateUIObject("Label", go.transform);
            StretchFull(txtGo);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 14f;
            txt.color = COLOR_TEXT;
            txt.alignment = TextAlignmentOptions.Center;

            return btn;
        }

        private TMP_InputField AddCenteredInputField(Transform parent, string name,
            Vector2 pos, Vector2 size, string placeholder)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                pos, size, new Vector2(0.5f, 0.5f));

            go.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.1f, 1f);

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
            phTmp.fontSize = 14f;
            phTmp.fontStyle = FontStyles.Italic;
            phTmp.color = new Color(0.5f, 0.5f, 0.55f);
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var txtGo = CreateUIObject("Text", textArea.transform);
            StretchFull(txtGo);
            var txtTmp = txtGo.AddComponent<TextMeshProUGUI>();
            txtTmp.fontSize = 14f;
            txtTmp.color = COLOR_TEXT;
            txtTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = taRt;
            input.textComponent = txtTmp;
            input.placeholder = phTmp;
            input.text = "";

            return input;
        }

        private void CreateDialogButton(Transform parent, string name, string label,
            Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateUIObject(name, parent);
            Anchor(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                pos, new Vector2(100f, 36f), new Vector2(0.5f, 0.5f));

            var img = go.AddComponent<Image>();
            img.color = COLOR_SECONDARY;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var txtGo = CreateUIObject("T", go.transform);
            StretchFull(txtGo);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 16f;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static Color Lighten(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r + amount),
                Mathf.Clamp01(c.g + amount),
                Mathf.Clamp01(c.b + amount),
                c.a);
        }

        private static Color Darken(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r - amount),
                Mathf.Clamp01(c.g - amount),
                Mathf.Clamp01(c.b - amount),
                c.a);
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

    /// <summary>
    /// Data structure for a discovered game entry in the server browser.
    /// </summary>
    [System.Serializable]
    public class GameInfo
    {
        public string gameName = "Unknown";
        public string domain = "";
        public string address = "";
        public int port = 7777;
        public int currentPlayers = 0;
        public int maxPlayers = 4;
        public bool isLan = true;
    }
}
