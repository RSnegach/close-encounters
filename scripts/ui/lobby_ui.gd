## lobby_ui.gd
## Pre-match lobby where players choose domain, budget tier, arena, and
## difficulty (solo-only).  In multiplayer the host controls the settings
## and they are synced to clients.  Everyone must mark themselves "ready"
## before the host can start the match.
##
## Built entirely in code — the .tscn only needs a root Control with this
## script attached.
extends Control

# ─── Theme constants ─────────────────────────────────────────────────────────
const COLOR_BG: Color       = Color("#1a1a2e")
const COLOR_ACCENT: Color   = Color("#e94560")
const COLOR_SECONDARY: Color = Color("#0f3460")
const COLOR_TEXT: Color     = Color("#eeeeee")
const COLOR_GREEN: Color    = Color("#4ecca3")

# ─── Domain / budget data ────────────────────────────────────────────────────
## Domains the player can pick from.
const DOMAINS: Array[String] = ["Ground", "Air", "Water", "Submarine", "Space"]

## Budget tier names mapped to their dollar values (0 means unlimited).
const BUDGET_TIERS: Dictionary = {
	"Scrapyard $500":  500,
	"Garage $1,500":   1500,
	"Factory $3,000":  3000,
	"Arsenal $5,000":  5000,
	"Unlimited":       0,
	"Custom":         -1,        # -1 = use the SpinBox value
}

## Maps a domain name to an arena scene path.  Extend this dictionary as
## new arenas are added.
const ARENA_MAP: Dictionary = {
	"Ground":     "res://scenes/arenas/arena_ground.tscn",
	"Air":        "res://scenes/arenas/arena_air.tscn",
	"Water":      "res://scenes/arenas/arena_water.tscn",
	"Submarine":  "res://scenes/arenas/arena_submarine.tscn",
	"Space":      "res://scenes/arenas/arena_space.tscn",
}

## AI difficulty labels.
const DIFFICULTY_OPTIONS: Array[String] = ["Easy", "Medium", "Hard"]

# ─── Node references ─────────────────────────────────────────────────────────
var hsplit: HSplitContainer         ## Top-level horizontal split.
var domain_selector: OptionButton   ## Domain pick-list.
var budget_selector: OptionButton   ## Budget tier pick-list.
var custom_budget_input: SpinBox    ## Visible only when "Custom" is chosen.
var arena_label: Label              ## Auto-selected arena display.
var difficulty_label: Label         ## "AI Difficulty" heading (solo only).
var difficulty_selector: OptionButton ## Easy / Medium / Hard (solo only).
var player_list: ItemList           ## Shows connected players + ready flags.
var ready_btn: Button               ## Toggle local player's ready state.
var start_btn: Button               ## Host-only: begin match when all ready.
var back_btn: Button                ## Return to main menu.
var is_ready: bool = false          ## Whether local player has toggled ready.


## Construct the full lobby UI, populate selectors, and hook up signals.
func _ready() -> void:
	_set_background(COLOR_BG)

	# ── Main split ──
	hsplit = HSplitContainer.new()
	hsplit.set_anchors_preset(Control.PRESET_FULL_RECT)
	hsplit.add_theme_constant_override("separation", 12)
	# Outer margins so nothing sticks to screen edges
	hsplit.offset_left   = 24
	hsplit.offset_top    = 24
	hsplit.offset_right  = -24
	hsplit.offset_bottom = -24
	add_child(hsplit)

	# ── Left panel — settings ──
	var left_panel: VBoxContainer = _create_panel()
	left_panel.custom_minimum_size = Vector2(340, 0)
	hsplit.add_child(left_panel)

	# Domain
	left_panel.add_child(_make_heading("Domain"))
	domain_selector = OptionButton.new()
	for d: String in DOMAINS:
		domain_selector.add_item(d)
	_style_option_button(domain_selector)
	left_panel.add_child(domain_selector)

	# Budget
	left_panel.add_child(_make_heading("Budget Tier"))
	budget_selector = OptionButton.new()
	for tier_name: String in BUDGET_TIERS.keys():
		budget_selector.add_item(tier_name)
	_style_option_button(budget_selector)
	left_panel.add_child(budget_selector)

	# Custom budget spin box (hidden by default)
	custom_budget_input = SpinBox.new()
	custom_budget_input.min_value = 100
	custom_budget_input.max_value = 99999
	custom_budget_input.step      = 100
	custom_budget_input.value     = 2000
	custom_budget_input.visible   = false
	custom_budget_input.prefix    = "$"
	left_panel.add_child(custom_budget_input)

	# Arena (read-only, auto-picked)
	left_panel.add_child(_make_heading("Arena"))
	arena_label = Label.new()
	arena_label.text = "—"
	arena_label.add_theme_color_override("font_color", COLOR_TEXT)
	arena_label.add_theme_font_size_override("font_size", 16)
	left_panel.add_child(arena_label)

	# Difficulty (solo only)
	difficulty_label = _make_heading("AI Difficulty")
	left_panel.add_child(difficulty_label)
	difficulty_selector = OptionButton.new()
	for d: String in DIFFICULTY_OPTIONS:
		difficulty_selector.add_item(d)
	_style_option_button(difficulty_selector)
	difficulty_selector.selected = 1   # default "Medium"
	left_panel.add_child(difficulty_selector)

	# Show / hide difficulty based on game mode
	var is_solo: bool = GameManager.match_settings.get("mode", "solo") == "solo"
	difficulty_label.visible    = is_solo
	difficulty_selector.visible = is_solo

	# ── Right panel — player list & actions ──
	var right_panel: VBoxContainer = _create_panel()
	right_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hsplit.add_child(right_panel)

	right_panel.add_child(_make_heading("Players"))

	player_list = ItemList.new()
	player_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	player_list.custom_minimum_size = Vector2(0, 200)
	player_list.add_theme_color_override("font_color", COLOR_TEXT)
	var list_bg: StyleBoxFlat = StyleBoxFlat.new()
	list_bg.bg_color = COLOR_BG.lightened(0.06)
	list_bg.corner_radius_top_left     = 4
	list_bg.corner_radius_top_right    = 4
	list_bg.corner_radius_bottom_left  = 4
	list_bg.corner_radius_bottom_right = 4
	player_list.add_theme_stylebox_override("panel", list_bg)
	right_panel.add_child(player_list)

	# Spacer
	var sp: Control = Control.new()
	sp.custom_minimum_size = Vector2(0, 8)
	right_panel.add_child(sp)

	ready_btn = _create_action_button("Ready", COLOR_SECONDARY)
	right_panel.add_child(ready_btn)

	start_btn = _create_action_button("Start Match", COLOR_ACCENT)
	right_panel.add_child(start_btn)
	# Only the host (or solo player) can start
	var is_host_or_solo: bool = is_solo or GameManager.match_settings.get("mode", "") == "host"
	start_btn.visible = is_host_or_solo
	start_btn.disabled = true   # enabled once everyone is ready

	back_btn = _create_action_button("Back", COLOR_SECONDARY.darkened(0.2))
	right_panel.add_child(back_btn)

	# ── Signal connections ──
	domain_selector.item_selected.connect(_on_domain_changed)
	budget_selector.item_selected.connect(_on_budget_changed)
	custom_budget_input.value_changed.connect(_on_custom_budget_changed)
	difficulty_selector.item_selected.connect(_on_difficulty_changed)
	ready_btn.pressed.connect(_on_ready_pressed)
	start_btn.pressed.connect(_on_start_pressed)
	back_btn.pressed.connect(_on_back_pressed)

	# Network signals (safe if NetworkManager doesn't exist yet — check first)
	if NetworkManager.has_signal("player_connected"):
		NetworkManager.player_connected.connect(_on_player_connected)
	if NetworkManager.has_signal("player_disconnected"):
		NetworkManager.player_disconnected.connect(_on_player_disconnected)
	if NetworkManager.has_signal("all_players_ready"):
		NetworkManager.all_players_ready.connect(_on_all_players_ready)

	# Apply initial defaults so match_settings is consistent
	_on_domain_changed(0)
	_on_budget_changed(0)
	_update_player_list()


# ─── Setting callbacks ───────────────────────────────────────────────────────

## Called when the player picks a different domain.
## Updates match_settings and auto-selects the matching arena.
func _on_domain_changed(index: int) -> void:
	var domain: String = DOMAINS[index]
	GameManager.match_settings["domain"] = domain
	GameManager.match_settings["arena"]  = _get_arena_for_domain(domain)
	arena_label.text = domain + " Arena"


## Called when the player picks a different budget tier.
## Shows/hides the custom SpinBox and writes the dollar amount into settings.
func _on_budget_changed(index: int) -> void:
	var tier_name: String = budget_selector.get_item_text(index)
	var value: int = BUDGET_TIERS[tier_name]

	GameManager.match_settings["budget_tier"] = tier_name

	if value == -1:
		# Custom — use SpinBox value
		custom_budget_input.visible = true
		GameManager.match_settings["budget"] = int(custom_budget_input.value)
	else:
		custom_budget_input.visible = false
		GameManager.match_settings["budget"] = value


## Called when the custom-budget SpinBox value changes.
func _on_custom_budget_changed(value: float) -> void:
	GameManager.match_settings["budget"] = int(value)


## Called when the AI difficulty selector changes (solo mode only).
func _on_difficulty_changed(index: int) -> void:
	GameManager.match_settings["ai_difficulty"] = DIFFICULTY_OPTIONS[index]


## Toggle the local player's ready status.
func _on_ready_pressed() -> void:
	is_ready = !is_ready
	ready_btn.text = "Unready" if is_ready else "Ready"

	# Style swap to give visual feedback
	var col: Color = COLOR_GREEN if is_ready else COLOR_SECONDARY
	var style: StyleBoxFlat = (ready_btn.get_theme_stylebox("normal") as StyleBoxFlat).duplicate()
	style.bg_color = col
	ready_btn.add_theme_stylebox_override("normal", style)

	NetworkManager.set_player_ready(is_ready)

	# In solo mode auto-enable start
	if GameManager.match_settings.get("mode", "") == "solo":
		start_btn.disabled = !is_ready


## Host clicks Start — transition everyone to the builder scene.
func _on_start_pressed() -> void:
	GameManager.start_match()
	GameManager.change_scene("res://scenes/builder.tscn")


## Return to the main menu, cleaning up any network state.
func _on_back_pressed() -> void:
	NetworkManager.disconnect_game()
	GameManager.change_scene("res://scenes/main_menu.tscn")


# ─── Network event handlers ─────────────────────────────────────────────────

## A new player joined — refresh the list.
func _on_player_connected(_id: int) -> void:
	_update_player_list()


## A player left — refresh the list.
func _on_player_disconnected(_id: int) -> void:
	_update_player_list()


## Every connected player is now ready — host can start.
func _on_all_players_ready() -> void:
	start_btn.disabled = false


# ─── Helpers ─────────────────────────────────────────────────────────────────

## Rebuild the player ItemList from the current multiplayer peer list.
## In solo mode it just shows "Player (you)".
func _update_player_list() -> void:
	player_list.clear()
	if GameManager.match_settings.get("mode", "solo") == "solo":
		player_list.add_item("Player (you) — ready" if is_ready else "Player (you)")
		return
	# Multiplayer — iterate connected peers.  Peer 1 is the host.
	var peers: PackedInt32Array = multiplayer.get_peers()
	var my_id: int = multiplayer.get_unique_id()
	for pid: int in peers:
		var suffix: String = " (host)" if pid == 1 else ""
		if pid == my_id:
			suffix += " (you)"
		player_list.add_item("Player %d%s" % [pid, suffix])


## Return the arena scene path that matches the given domain name.
func _get_arena_for_domain(domain: String) -> String:
	return ARENA_MAP.get(domain, "res://scenes/arenas/arena_ground.tscn")


## Create a VBoxContainer styled as a panel section.
func _create_panel() -> VBoxContainer:
	var panel: VBoxContainer = VBoxContainer.new()
	panel.add_theme_constant_override("separation", 8)
	return panel


## Create a heading Label.
func _make_heading(text: String) -> Label:
	var lbl: Label = Label.new()
	lbl.text = text
	lbl.add_theme_font_size_override("font_size", 18)
	lbl.add_theme_color_override("font_color", COLOR_ACCENT)
	return lbl


## Apply consistent styling to an OptionButton.
func _style_option_button(btn: OptionButton) -> void:
	btn.custom_minimum_size = Vector2(0, 36)
	btn.add_theme_font_size_override("font_size", 16)
	btn.add_theme_color_override("font_color", COLOR_TEXT)


## Create a styled action Button (Ready, Start, Back, etc.).
func _create_action_button(text: String, bg_color: Color) -> Button:
	var btn: Button = Button.new()
	btn.text = text
	btn.custom_minimum_size = Vector2(0, 44)
	btn.add_theme_font_size_override("font_size", 18)
	btn.add_theme_color_override("font_color", COLOR_TEXT)
	var style_normal: StyleBoxFlat = StyleBoxFlat.new()
	style_normal.bg_color = bg_color
	style_normal.corner_radius_top_left     = 6
	style_normal.corner_radius_top_right    = 6
	style_normal.corner_radius_bottom_left  = 6
	style_normal.corner_radius_bottom_right = 6
	style_normal.content_margin_top    = 6
	style_normal.content_margin_bottom = 6
	style_normal.content_margin_left   = 12
	style_normal.content_margin_right  = 12
	btn.add_theme_stylebox_override("normal", style_normal)
	var style_hover: StyleBoxFlat = style_normal.duplicate()
	style_hover.bg_color = bg_color.lightened(0.12)
	btn.add_theme_stylebox_override("hover", style_hover)
	var style_pressed: StyleBoxFlat = style_normal.duplicate()
	style_pressed.bg_color = bg_color.darkened(0.15)
	btn.add_theme_stylebox_override("pressed", style_pressed)
	return btn


## Fill the root Control with a solid background colour.
func _set_background(color: Color) -> void:
	var rect: ColorRect = ColorRect.new()
	rect.color = color
	rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(rect)
	move_child(rect, 0)
