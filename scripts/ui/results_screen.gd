## results_screen.gd
## Post-match results screen.  Shows whether the player won, lost, or drew,
## along with match statistics (time, parts destroyed, damage dealt/received).
## Offers Rematch, Lobby, and Main Menu buttons.
##
## Usage (from the combat scene or GameManager):
##   var results = preload("res://scenes/results.tscn").instantiate()
##   add_child(results)
##   results.setup(winner_id, match_data)
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
const COLOR_YELLOW: Color   = Color("#f0c040")
const COLOR_RED: Color      = Color("#e94560")

# ─── UI nodes ────────────────────────────────────────────────────────────────
var vbox: VBoxContainer          ## Central column.
var result_label: Label          ## "VICTORY!" / "DEFEATED" / "DRAW".
var stats_container: GridContainer ## 2-column grid of match statistics.
var rematch_btn: Button          ## Jump straight back into building.
var lobby_btn: Button            ## Return to the lobby (same settings).
var menu_btn: Button             ## Disconnect and go to the main menu.


## Build the results UI, wire buttons, and tell GameManager about the state.
func _ready() -> void:
	_set_background(COLOR_BG)

	# ── Central column ──
	vbox = VBoxContainer.new()
	vbox.set_anchors_preset(Control.PRESET_CENTER)
	vbox.grow_horizontal = Control.GROW_DIRECTION_BOTH
	vbox.grow_vertical   = Control.GROW_DIRECTION_BOTH
	vbox.custom_minimum_size = Vector2(420, 0)
	vbox.add_theme_constant_override("separation", 16)
	add_child(vbox)

	# ── Result heading ──
	result_label = Label.new()
	result_label.text = "—"
	result_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	result_label.add_theme_font_size_override("font_size", 56)
	result_label.add_theme_color_override("font_color", COLOR_TEXT)
	vbox.add_child(result_label)

	# Spacer
	vbox.add_child(_spacer(12))

	# ── Stats heading ──
	var stats_heading: Label = Label.new()
	stats_heading.text = "Match Statistics"
	stats_heading.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	stats_heading.add_theme_font_size_override("font_size", 20)
	stats_heading.add_theme_color_override("font_color", COLOR_ACCENT)
	vbox.add_child(stats_heading)

	# ── Stats grid (2 columns: label | value) ──
	stats_container = GridContainer.new()
	stats_container.columns = 2
	stats_container.add_theme_constant_override("h_separation", 24)
	stats_container.add_theme_constant_override("v_separation", 8)
	vbox.add_child(stats_container)

	# Spacer
	vbox.add_child(_spacer(20))

	# ── Buttons ──
	var btn_row: HBoxContainer = HBoxContainer.new()
	btn_row.add_theme_constant_override("separation", 16)
	btn_row.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_child(btn_row)

	rematch_btn = _create_button("Rematch", COLOR_ACCENT)
	btn_row.add_child(rematch_btn)

	lobby_btn = _create_button("Lobby", COLOR_SECONDARY)
	btn_row.add_child(lobby_btn)

	menu_btn = _create_button("Main Menu", COLOR_SECONDARY.darkened(0.2))
	btn_row.add_child(menu_btn)

	# ── Signal connections ──
	rematch_btn.pressed.connect(_on_rematch_pressed)
	lobby_btn.pressed.connect(_on_lobby_pressed)
	menu_btn.pressed.connect(_on_menu_pressed)

	# Notify GameManager
	GameManager.change_state(GameManager.GameState.RESULTS)


## Populate the results screen with outcome and statistics.
##
## winner_id:  The peer ID of the winning player.
##             Use -1 for a draw.
##             In solo mode the player's own peer ID (or 1) means victory.
## match_data: Dictionary with optional keys:
##             match_time (float, seconds), parts_destroyed (int),
##             damage_dealt (float), damage_received (float),
##             plus any extras you want shown.
func setup(winner_id: int, match_data: Dictionary) -> void:
	# ── Determine outcome ──
	var my_id: int = multiplayer.get_unique_id() if multiplayer.has_multiplayer_peer() else 1
	var mode: String = GameManager.match_settings.get("mode", "solo")

	if winner_id == -1:
		# Draw
		result_label.text = "DRAW"
		result_label.add_theme_color_override("font_color", COLOR_YELLOW)
	elif winner_id == my_id or (mode == "solo" and winner_id == 1):
		# Victory
		result_label.text = "VICTORY!"
		result_label.add_theme_color_override("font_color", COLOR_GREEN)
	else:
		# Defeat
		result_label.text = "DEFEATED"
		result_label.add_theme_color_override("font_color", COLOR_RED)

	# ── Populate stat rows ──
	# Clear any existing rows first
	for child: Node in stats_container.get_children():
		child.queue_free()

	# Match time
	if match_data.has("match_time"):
		var secs: float = match_data["match_time"]
		var mins: int   = int(secs) / 60
		var rem: int    = int(secs) % 60
		_add_stat_row("Match Time", "%d:%02d" % [mins, rem])

	# Parts destroyed
	if match_data.has("parts_destroyed"):
		_add_stat_row("Parts Destroyed", str(match_data["parts_destroyed"]))

	# Damage dealt
	if match_data.has("damage_dealt"):
		_add_stat_row("Damage Dealt", "%d" % int(match_data["damage_dealt"]))

	# Damage received
	if match_data.has("damage_received"):
		_add_stat_row("Damage Received", "%d" % int(match_data["damage_received"]))

	# Any additional stats the caller wants to show
	for key: String in match_data.keys():
		if key in ["match_time", "parts_destroyed", "damage_dealt", "damage_received"]:
			continue
		_add_stat_row(key.capitalize().replace("_", " "), str(match_data[key]))


# ─── Button callbacks ────────────────────────────────────────────────────────

## Jump back to the builder for another round with the same settings.
func _on_rematch_pressed() -> void:
	GameManager.change_scene("res://scenes/builder.tscn")


## Return to the lobby to change settings before building again.
func _on_lobby_pressed() -> void:
	GameManager.change_scene("res://scenes/lobby.tscn")


## Disconnect from any network session and return to the title screen.
func _on_menu_pressed() -> void:
	NetworkManager.disconnect_game()
	GameManager.change_scene("res://scenes/main_menu.tscn")


# ─── Helpers ─────────────────────────────────────────────────────────────────

## Add a label-value pair to the stats GridContainer.
func _add_stat_row(label_text: String, value_text: String) -> void:
	# Left: stat name
	var name_lbl: Label = Label.new()
	name_lbl.text = label_text + ":"
	name_lbl.add_theme_font_size_override("font_size", 16)
	name_lbl.add_theme_color_override("font_color", Color("#999999"))
	stats_container.add_child(name_lbl)

	# Right: stat value
	var value_lbl: Label = Label.new()
	value_lbl.text = value_text
	value_lbl.add_theme_font_size_override("font_size", 16)
	value_lbl.add_theme_color_override("font_color", COLOR_TEXT)
	stats_container.add_child(value_lbl)


## Create a styled Button.
func _create_button(text: String, bg_color: Color) -> Button:
	var btn: Button = Button.new()
	btn.text = text
	btn.custom_minimum_size = Vector2(140, 48)
	btn.add_theme_font_size_override("font_size", 18)
	btn.add_theme_color_override("font_color", COLOR_TEXT)
	var sn: StyleBoxFlat = StyleBoxFlat.new()
	sn.bg_color = bg_color
	sn.corner_radius_top_left     = 6
	sn.corner_radius_top_right    = 6
	sn.corner_radius_bottom_left  = 6
	sn.corner_radius_bottom_right = 6
	sn.content_margin_top    = 8
	sn.content_margin_bottom = 8
	sn.content_margin_left   = 16
	sn.content_margin_right  = 16
	btn.add_theme_stylebox_override("normal", sn)
	var sh: StyleBoxFlat = sn.duplicate()
	sh.bg_color = bg_color.lightened(0.12)
	btn.add_theme_stylebox_override("hover", sh)
	var sp: StyleBoxFlat = sn.duplicate()
	sp.bg_color = bg_color.darkened(0.15)
	btn.add_theme_stylebox_override("pressed", sp)
	return btn


## Create a fixed-height spacer Control.
func _spacer(height: float) -> Control:
	var s: Control = Control.new()
	s.custom_minimum_size = Vector2(0, height)
	return s


## Fill the background with a solid colour.
func _set_background(color: Color) -> void:
	var rect: ColorRect = ColorRect.new()
	rect.color = color
	rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(rect)
	move_child(rect, 0)
