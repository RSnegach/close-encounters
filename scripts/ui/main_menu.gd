## main_menu.gd
## Main menu screen for Close Encounters.
##
## Three primary buttons:
##   - **Solo Match** — fight AI, no networking.
##   - **Host Game** — start a server and go to lobby.
##   - **Join Game** — opens a server browser that auto-discovers games
##     on LAN and internet. No IP entry needed — just click a game.
##
## The server browser refreshes automatically every few seconds and shows
## every game found via LAN broadcast or the online lobby server.
extends Control

# ─── Theme constants ─────────────────────────────────────────────────────────
const COLOR_BG: Color       = Color("#1a1a2e")
const COLOR_ACCENT: Color   = Color("#e94560")
const COLOR_SECONDARY: Color = Color("#0f3460")
const COLOR_TEXT: Color     = Color("#eeeeee")
const COLOR_GREEN: Color    = Color("#4ecca3")
const COLOR_DIM: Color      = Color("#888888")

# ─── Node references ─────────────────────────────────────────────────────────
var vbox: VBoxContainer              ## Central column for menu buttons.
var title_label: Label               ## Game title.
var solo_btn: Button                 ## Solo match button.
var host_btn: Button                 ## Host game button.
var join_btn: Button                 ## Open server browser button.
var quit_btn: Button                 ## Quit button.

# Server browser (shown when Join is clicked)
var browser_panel: PanelContainer    ## The popup panel containing the browser.
var browser_vbox: VBoxContainer      ## Layout inside the browser panel.
var game_list: VBoxContainer         ## Scrollable list of discovered games.
var scroll: ScrollContainer          ## Scroll wrapper for game_list.
var status_label: Label              ## "Searching..." / "No games found" etc.
var refresh_btn: Button              ## Manual refresh.
var back_from_browser_btn: Button    ## Close browser, back to menu.
var manual_ip_btn: Button            ## Fallback: enter IP manually.
var ip_dialog: ConfirmationDialog    ## Manual IP entry dialog.
var ip_input: LineEdit               ## IP text field inside the dialog.

## Auto-refresh timer for the server browser.
var _refresh_timer: float = 0.0
const REFRESH_INTERVAL: float = 3.0

## Whether the server browser is currently open.
var _browser_open: bool = false


func _ready() -> void:
	_set_background(COLOR_BG)

	# ── Central VBoxContainer (the main menu buttons) ──
	vbox = VBoxContainer.new()
	vbox.set_anchors_preset(Control.PRESET_CENTER)
	vbox.grow_horizontal = Control.GROW_DIRECTION_BOTH
	vbox.grow_vertical   = Control.GROW_DIRECTION_BOTH
	vbox.custom_minimum_size = Vector2(320, 0)
	vbox.add_theme_constant_override("separation", 16)
	add_child(vbox)

	# Title
	title_label = Label.new()
	title_label.text = "CLOSE ENCOUNTERS"
	title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title_label.add_theme_font_size_override("font_size", 48)
	title_label.add_theme_color_override("font_color", COLOR_ACCENT)
	vbox.add_child(title_label)

	# Spacer
	var spacer: Control = Control.new()
	spacer.custom_minimum_size = Vector2(0, 24)
	vbox.add_child(spacer)

	# Buttons
	solo_btn = _create_menu_button("Solo Match")
	host_btn = _create_menu_button("Host Game")
	join_btn = _create_menu_button("Join Game")
	quit_btn = _create_menu_button("Quit")

	# Signal connections
	solo_btn.pressed.connect(_on_solo_pressed)
	host_btn.pressed.connect(_on_host_pressed)
	join_btn.pressed.connect(_on_join_pressed)
	quit_btn.pressed.connect(_on_quit_pressed)

	# ── Server browser panel (hidden by default) ──
	_build_server_browser()

	# ── Manual IP fallback dialog ──
	ip_dialog = ConfirmationDialog.new()
	ip_dialog.title = "Connect by IP"
	ip_dialog.min_size = Vector2(360, 140)
	var dialog_vbox: VBoxContainer = VBoxContainer.new()
	dialog_vbox.add_theme_constant_override("separation", 8)
	var ip_label: Label = Label.new()
	ip_label.text = "Server IP Address:"
	ip_label.add_theme_color_override("font_color", COLOR_TEXT)
	dialog_vbox.add_child(ip_label)
	ip_input = LineEdit.new()
	ip_input.placeholder_text = "e.g. 73.42.100.5 or 192.168.1.10"
	ip_input.custom_minimum_size = Vector2(300, 0)
	dialog_vbox.add_child(ip_input)
	ip_dialog.add_child(dialog_vbox)
	ip_dialog.confirmed.connect(_on_manual_ip_confirmed)
	add_child(ip_dialog)

	# Listen for discovery results from NetworkManager.
	NetworkManager.games_discovered.connect(_on_games_discovered)
	NetworkManager.connection_failed.connect(_on_connection_failed)

	# Set game state
	GameManager.change_state(GameManager.GameState.MAIN_MENU)


func _process(delta: float) -> void:
	# Auto-refresh the server browser while it's open.
	if _browser_open:
		_refresh_timer += delta
		if _refresh_timer >= REFRESH_INTERVAL:
			_refresh_timer = 0.0
			NetworkManager.refresh_online_games()


# ─── Menu button handlers ────────────────────────────────────────────────────

func _on_solo_pressed() -> void:
	GameManager.match_settings["mode"] = "solo"
	GameManager.change_scene("res://scenes/lobby.tscn")


func _on_host_pressed() -> void:
	GameManager.match_settings["mode"] = "host"
	NetworkManager.host_game()
	GameManager.change_scene("res://scenes/lobby.tscn")


## Open the server browser and start auto-discovering games.
func _on_join_pressed() -> void:
	vbox.visible = false
	browser_panel.visible = true
	_browser_open = true
	_refresh_timer = 0.0
	status_label.text = "Searching for games..."
	_clear_game_list()
	NetworkManager.start_discovery()


func _on_quit_pressed() -> void:
	get_tree().quit()


# ─── Server browser ──────────────────────────────────────────────────────────

## Build the server browser panel (hidden until Join is clicked).
func _build_server_browser() -> void:
	browser_panel = PanelContainer.new()
	browser_panel.set_anchors_preset(Control.PRESET_CENTER)
	browser_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	browser_panel.grow_vertical   = Control.GROW_DIRECTION_BOTH
	browser_panel.custom_minimum_size = Vector2(600, 450)
	browser_panel.visible = false

	# Dark styled panel background
	var panel_style: StyleBoxFlat = StyleBoxFlat.new()
	panel_style.bg_color = COLOR_BG.lightened(0.05)
	panel_style.border_color = COLOR_SECONDARY
	panel_style.border_width_top    = 2
	panel_style.border_width_bottom = 2
	panel_style.border_width_left   = 2
	panel_style.border_width_right  = 2
	panel_style.corner_radius_top_left     = 8
	panel_style.corner_radius_top_right    = 8
	panel_style.corner_radius_bottom_left  = 8
	panel_style.corner_radius_bottom_right = 8
	panel_style.content_margin_top    = 16
	panel_style.content_margin_bottom = 16
	panel_style.content_margin_left   = 16
	panel_style.content_margin_right  = 16
	browser_panel.add_theme_stylebox_override("panel", panel_style)

	browser_vbox = VBoxContainer.new()
	browser_vbox.add_theme_constant_override("separation", 10)
	browser_panel.add_child(browser_vbox)

	# Title
	var browser_title: Label = Label.new()
	browser_title.text = "FIND GAME"
	browser_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	browser_title.add_theme_font_size_override("font_size", 28)
	browser_title.add_theme_color_override("font_color", COLOR_ACCENT)
	browser_vbox.add_child(browser_title)

	# Status
	status_label = Label.new()
	status_label.text = "Searching for games..."
	status_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	status_label.add_theme_font_size_override("font_size", 14)
	status_label.add_theme_color_override("font_color", COLOR_DIM)
	browser_vbox.add_child(status_label)

	# Scrollable game list
	scroll = ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.custom_minimum_size = Vector2(0, 250)
	browser_vbox.add_child(scroll)

	game_list = VBoxContainer.new()
	game_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	game_list.add_theme_constant_override("separation", 4)
	scroll.add_child(game_list)

	# Button row at bottom
	var btn_row: HBoxContainer = HBoxContainer.new()
	btn_row.add_theme_constant_override("separation", 8)
	btn_row.alignment = BoxContainer.ALIGNMENT_CENTER
	browser_vbox.add_child(btn_row)

	refresh_btn = _create_small_button("Refresh", COLOR_SECONDARY)
	refresh_btn.pressed.connect(_on_refresh_pressed)
	btn_row.add_child(refresh_btn)

	manual_ip_btn = _create_small_button("Enter IP", COLOR_SECONDARY.darkened(0.1))
	manual_ip_btn.pressed.connect(_on_manual_ip_pressed)
	btn_row.add_child(manual_ip_btn)

	back_from_browser_btn = _create_small_button("Back", COLOR_SECONDARY.darkened(0.2))
	back_from_browser_btn.pressed.connect(_on_browser_back_pressed)
	btn_row.add_child(back_from_browser_btn)

	add_child(browser_panel)


## Called by NetworkManager when discovered games change.
func _on_games_discovered(games: Array) -> void:
	if not _browser_open:
		return

	_clear_game_list()

	if games.is_empty():
		status_label.text = "No games found. Make sure the host is running."
		return

	status_label.text = "%d game(s) found" % games.size()

	for game_info: Dictionary in games:
		_add_game_entry(game_info)


## Build a clickable row for one discovered game.
func _add_game_entry(info: Dictionary) -> void:
	var row: PanelContainer = PanelContainer.new()
	var row_style: StyleBoxFlat = StyleBoxFlat.new()
	row_style.bg_color = Color("#252540")
	row_style.corner_radius_top_left     = 4
	row_style.corner_radius_top_right    = 4
	row_style.corner_radius_bottom_left  = 4
	row_style.corner_radius_bottom_right = 4
	row_style.content_margin_top    = 8
	row_style.content_margin_bottom = 8
	row_style.content_margin_left   = 12
	row_style.content_margin_right  = 12
	row.add_theme_stylebox_override("panel", row_style)

	var hbox: HBoxContainer = HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 12)
	row.add_child(hbox)

	# Source icon (LAN / Online)
	var source_label: Label = Label.new()
	var source_text: String = "[LAN]" if info.get("source", "lan") == "lan" else "[NET]"
	var source_color: Color = COLOR_GREEN if info.get("source") == "lan" else Color("#66aaff")
	source_label.text = source_text
	source_label.add_theme_font_size_override("font_size", 12)
	source_label.add_theme_color_override("font_color", source_color)
	source_label.custom_minimum_size = Vector2(40, 0)
	hbox.add_child(source_label)

	# Game name
	var name_label: Label = Label.new()
	name_label.text = info.get("name", "Unknown")
	name_label.add_theme_font_size_override("font_size", 16)
	name_label.add_theme_color_override("font_color", COLOR_TEXT)
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(name_label)

	# Domain
	var domain_label: Label = Label.new()
	domain_label.text = info.get("domain", "")
	domain_label.add_theme_font_size_override("font_size", 14)
	domain_label.add_theme_color_override("font_color", COLOR_DIM)
	domain_label.custom_minimum_size = Vector2(80, 0)
	hbox.add_child(domain_label)

	# Player count
	var players_label: Label = Label.new()
	players_label.text = "%d/%d" % [info.get("players", 0), info.get("max_players", 4)]
	players_label.add_theme_font_size_override("font_size", 14)
	players_label.add_theme_color_override("font_color", COLOR_DIM)
	players_label.custom_minimum_size = Vector2(40, 0)
	hbox.add_child(players_label)

	# Join button
	var join_game_btn: Button = _create_small_button("Join", COLOR_ACCENT)
	join_game_btn.custom_minimum_size = Vector2(70, 32)
	# Capture info in the lambda so we know which game to join.
	var address: String = info.get("address", "")
	var port: int = int(info.get("port", NetworkManager.DEFAULT_PORT))
	join_game_btn.pressed.connect(_on_join_game.bind(address, port))
	hbox.add_child(join_game_btn)

	game_list.add_child(row)


## Player clicked Join on a specific game entry.
func _on_join_game(address: String, port: int) -> void:
	_browser_open = false
	GameManager.match_settings["mode"] = "join"
	NetworkManager.join_game(address, port)
	GameManager.change_scene("res://scenes/lobby.tscn")


## Clear all game entries from the list.
func _clear_game_list() -> void:
	for child: Node in game_list.get_children():
		child.queue_free()


func _on_refresh_pressed() -> void:
	status_label.text = "Refreshing..."
	_clear_game_list()
	NetworkManager.start_discovery()


func _on_manual_ip_pressed() -> void:
	ip_input.text = ""
	ip_dialog.popup_centered()


func _on_manual_ip_confirmed() -> void:
	var ip: String = ip_input.text.strip_edges()
	if ip.is_empty():
		return
	_browser_open = false
	GameManager.match_settings["mode"] = "join"
	NetworkManager.join_game(ip)
	GameManager.change_scene("res://scenes/lobby.tscn")


func _on_browser_back_pressed() -> void:
	_browser_open = false
	browser_panel.visible = false
	vbox.visible = true
	NetworkManager.stop_discovery()


func _on_connection_failed() -> void:
	# If we were in the browser and a join failed, reopen browser.
	if not _browser_open:
		# TODO: show error toast
		pass


# ─── Helpers ─────────────────────────────────────────────────────────────────

## Create a styled main-menu button and add it to the VBox.
func _create_menu_button(text: String) -> Button:
	var btn: Button = Button.new()
	btn.text = text
	btn.custom_minimum_size = Vector2(280, 48)
	btn.add_theme_font_size_override("font_size", 22)
	btn.add_theme_color_override("font_color", COLOR_TEXT)
	var style_normal: StyleBoxFlat = StyleBoxFlat.new()
	style_normal.bg_color = COLOR_SECONDARY
	style_normal.corner_radius_top_left     = 6
	style_normal.corner_radius_top_right    = 6
	style_normal.corner_radius_bottom_left  = 6
	style_normal.corner_radius_bottom_right = 6
	style_normal.content_margin_top    = 8
	style_normal.content_margin_bottom = 8
	btn.add_theme_stylebox_override("normal", style_normal)
	var style_hover: StyleBoxFlat = style_normal.duplicate()
	style_hover.bg_color = COLOR_SECONDARY.lightened(0.15)
	btn.add_theme_stylebox_override("hover", style_hover)
	var style_pressed: StyleBoxFlat = style_normal.duplicate()
	style_pressed.bg_color = COLOR_ACCENT
	btn.add_theme_stylebox_override("pressed", style_pressed)
	vbox.add_child(btn)
	return btn


## Create a smaller styled button (for browser actions).
func _create_small_button(text: String, bg_color: Color) -> Button:
	var btn: Button = Button.new()
	btn.text = text
	btn.custom_minimum_size = Vector2(90, 36)
	btn.add_theme_font_size_override("font_size", 14)
	btn.add_theme_color_override("font_color", COLOR_TEXT)
	var style: StyleBoxFlat = StyleBoxFlat.new()
	style.bg_color = bg_color
	style.corner_radius_top_left     = 4
	style.corner_radius_top_right    = 4
	style.corner_radius_bottom_left  = 4
	style.corner_radius_bottom_right = 4
	style.content_margin_top    = 4
	style.content_margin_bottom = 4
	style.content_margin_left   = 8
	style.content_margin_right  = 8
	btn.add_theme_stylebox_override("normal", style)
	var style_hover: StyleBoxFlat = style.duplicate()
	style_hover.bg_color = bg_color.lightened(0.15)
	btn.add_theme_stylebox_override("hover", style_hover)
	return btn


## Solid background colour.
func _set_background(color: Color) -> void:
	var rect: ColorRect = ColorRect.new()
	rect.color = color
	rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(rect)
	move_child(rect, 0)
