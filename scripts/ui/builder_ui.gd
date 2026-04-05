## builder_ui.gd
## HUD overlay for the vehicle-builder scene.  Sits on top of the 3D viewport
## and provides a part catalog on the left, stats / budget / action buttons on
## the right, and leaves the centre transparent so the 3D builder shows through.
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
const COLOR_RED: Color      = Color("#e94560")
const COLOR_YELLOW: Color   = Color("#f0c040")
const PANEL_WIDTH: float    = 220.0

# ─── External references ─────────────────────────────────────────────────────
var builder: Node = null          ## The VehicleBuilder node in the parent scene.
var current_domain: String = ""   ## Domain chosen in the lobby (Ground, Air, …).

# ─── UI nodes ────────────────────────────────────────────────────────────────
var left_panel: VBoxContainer           ## Part catalog column.
var right_panel: VBoxContainer          ## Stats / actions column.
var part_catalog: Node = null           ## PartCatalog instance (see part_catalog.gd).
var budget_bar: Node = null             ## BudgetBar instance (see budget_bar.gd).
var stats_panel: Node = null            ## VehicleStatsPanel instance.
var validate_btn: Button                ## Run build validation.
var validation_label: Label             ## Shows validation results.
var save_btn: Button                    ## Save current build.
var load_btn: Button                    ## Load a saved build.
var clear_btn: Button                   ## Wipe the build area.
var ready_btn: Button                   ## Confirm build and proceed to combat.
var back_btn: Button                    ## Return to the lobby.
var save_dialog: ConfirmationDialog     ## Popup with filename LineEdit.
var save_name_input: LineEdit           ## Filename field inside the save dialog.
var load_dialog: ConfirmationDialog     ## Popup listing saved vehicles.
var load_list: ItemList                 ## List of saved vehicle filenames.


## Set up the three-column layout, wire builder signals, and populate
## the catalog and budget display for the current domain.
func _ready() -> void:
	# Let mouse events pass through the transparent centre
	mouse_filter = Control.MOUSE_FILTER_IGNORE

	# Grab external references
	current_domain = GameManager.match_settings.get("domain", "Ground")
	# The builder node should be a sibling or child in the scene tree —
	# search by class or group.  Adjust the path to your scene layout.
	builder = _find_builder()

	# ── Root HBoxContainer spanning the full screen with margins ──
	var hbox: HBoxContainer = HBoxContainer.new()
	hbox.set_anchors_preset(Control.PRESET_FULL_RECT)
	hbox.offset_right = -8   # Small right margin so panel stays on screen
	hbox.add_theme_constant_override("separation", 0)
	hbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(hbox)

	# ── Left panel — part catalog ──
	left_panel = _create_side_panel()
	left_panel.custom_minimum_size = Vector2(PANEL_WIDTH, 0)
	hbox.add_child(left_panel)

	left_panel.add_child(_make_heading("Parts"))

	# Instantiate the PartCatalog scene / class
	part_catalog = PartCatalog.new()
	part_catalog.size_flags_vertical = Control.SIZE_EXPAND_FILL
	left_panel.add_child(part_catalog)
	part_catalog.setup(current_domain)
	part_catalog.part_selected.connect(_on_part_selected)

	# ── Centre spacer (transparent — 3D shows through) ──
	var centre: Control = Control.new()
	centre.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	centre.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hbox.add_child(centre)

	# ── Right panel — budget, stats, actions ──
	right_panel = _create_side_panel()
	right_panel.custom_minimum_size = Vector2(PANEL_WIDTH, 0)
	hbox.add_child(right_panel)

	# Budget bar
	right_panel.add_child(_make_heading("Budget"))
	budget_bar = BudgetBar.new()
	right_panel.add_child(budget_bar)
	var total_budget: int = GameManager.match_settings.get("budget", 3000)
	budget_bar.setup(total_budget)

	# Vehicle stats panel
	right_panel.add_child(_make_heading("Vehicle Stats"))
	stats_panel = VehicleStatsPanel.new()
	stats_panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	right_panel.add_child(stats_panel)

	# ── Action buttons ──
	right_panel.add_child(_separator())

	validate_btn = _make_button("Validate Build", COLOR_SECONDARY)
	right_panel.add_child(validate_btn)

	validation_label = Label.new()
	validation_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	validation_label.custom_minimum_size = Vector2(0, 40)
	validation_label.add_theme_font_size_override("font_size", 13)
	validation_label.add_theme_color_override("font_color", COLOR_TEXT)
	right_panel.add_child(validation_label)

	right_panel.add_child(_separator())

	save_btn = _make_button("Save", COLOR_SECONDARY)
	right_panel.add_child(save_btn)

	load_btn = _make_button("Load", COLOR_SECONDARY)
	right_panel.add_child(load_btn)

	clear_btn = _make_button("Clear", COLOR_SECONDARY.darkened(0.2))
	right_panel.add_child(clear_btn)

	right_panel.add_child(_separator())

	ready_btn = _make_button("Ready  >>  Combat", COLOR_ACCENT)
	right_panel.add_child(ready_btn)

	back_btn = _make_button("Back to Lobby", COLOR_SECONDARY.darkened(0.3))
	right_panel.add_child(back_btn)

	# ── Save dialog ──
	save_dialog = ConfirmationDialog.new()
	save_dialog.title = "Save Vehicle"
	save_dialog.min_size = Vector2(340, 120)
	var save_vbox: VBoxContainer = VBoxContainer.new()
	var save_lbl: Label = Label.new()
	save_lbl.text = "Vehicle name:"
	save_lbl.add_theme_color_override("font_color", COLOR_TEXT)
	save_vbox.add_child(save_lbl)
	save_name_input = LineEdit.new()
	save_name_input.placeholder_text = "my_tank"
	save_vbox.add_child(save_name_input)
	save_dialog.add_child(save_vbox)
	add_child(save_dialog)

	# ── Load dialog ──
	load_dialog = ConfirmationDialog.new()
	load_dialog.title = "Load Vehicle"
	load_dialog.min_size = Vector2(340, 260)
	load_list = ItemList.new()
	load_list.custom_minimum_size = Vector2(300, 180)
	load_dialog.add_child(load_list)
	add_child(load_dialog)

	# ── Signal wiring ──
	validate_btn.pressed.connect(_on_validate_pressed)
	save_btn.pressed.connect(_on_save_pressed)
	load_btn.pressed.connect(_on_load_pressed)
	clear_btn.pressed.connect(_on_clear_pressed)
	ready_btn.pressed.connect(_on_ready_pressed)
	back_btn.pressed.connect(_on_back_pressed)
	save_dialog.confirmed.connect(_on_save_confirmed)
	load_dialog.confirmed.connect(_on_load_confirmed)

	# Listen to builder events so budget & stats stay up-to-date
	if builder:
		if builder.has_signal("part_placed"):
			builder.part_placed.connect(_on_build_changed)
		if builder.has_signal("part_removed"):
			builder.part_removed.connect(_on_build_changed)
		if builder.has_signal("budget_changed"):
			builder.budget_changed.connect(_on_budget_changed)


# ─── Builder signal handlers ─────────────────────────────────────────────────

## A part was placed or removed — recalculate stats.
func _on_build_changed(_part_data: Variant = null) -> void:
	_update_stats()


## Budget value changed in the builder — update the bar.
func _on_budget_changed(new_remaining: int) -> void:
	var total_budget: int = GameManager.match_settings.get("budget", 3000)
	budget_bar.update_spent(total_budget - new_remaining)
	_update_stats()


# ─── Part catalog callback ───────────────────────────────────────────────────

## The player clicked a part in the catalog — tell the builder to use it as
## the next piece to place.
func _on_part_selected(part_data: Variant) -> void:
	if builder:
		builder.selected_part_data = part_data


# ─── Action-button callbacks ────────────────────────────────────────────────

## Run the builder's validation and display the results.
func _on_validate_pressed() -> void:
	if not builder:
		return
	var result: Variant = builder.validate_build()
	# result is expected to be a Dictionary with "valid" (bool) and "issues" (Array[String])
	if result is Dictionary:
		if result.get("valid", false):
			validation_label.text = "Build is valid!"
			validation_label.add_theme_color_override("font_color", COLOR_GREEN)
		else:
			var issues: Array = result.get("issues", [])
			validation_label.text = "\n".join(issues)
			validation_label.add_theme_color_override("font_color", COLOR_RED)
	else:
		# Fallback if validate_build returns a bool
		if result:
			validation_label.text = "Build is valid!"
			validation_label.add_theme_color_override("font_color", COLOR_GREEN)
		else:
			validation_label.text = "Build has issues."
			validation_label.add_theme_color_override("font_color", COLOR_RED)


## Show the save dialog so the player can name their build.
func _on_save_pressed() -> void:
	save_name_input.text = ""
	save_dialog.popup_centered()


## Player confirmed the save — write to disk.
func _on_save_confirmed() -> void:
	var filename: String = save_name_input.text.strip_edges()
	if filename.is_empty():
		return
	if builder:
		VehicleSerializer.save_vehicle(builder.get_vehicle_data(), filename)


## Show the load dialog with a list of previously saved vehicles.
func _on_load_pressed() -> void:
	load_list.clear()
	var saves: Array = VehicleSerializer.list_saved_vehicles()
	for s: String in saves:
		load_list.add_item(s)
	load_dialog.popup_centered()


## Player selected a saved vehicle and confirmed — load it into the builder.
func _on_load_confirmed() -> void:
	var selected_items: PackedInt32Array = load_list.get_selected_items()
	if selected_items.is_empty():
		return
	var filename: String = load_list.get_item_text(selected_items[0])
	if builder and not filename.is_empty():
		var data: Variant = VehicleSerializer.load_vehicle(filename)
		if data:
			builder.load_vehicle_data(data)
			_update_stats()


## Wipe the builder workspace.
func _on_clear_pressed() -> void:
	if builder:
		builder.clear_build()
		_update_stats()
		validation_label.text = ""


## Validate the build, then transition to combat if everything checks out.
func _on_ready_pressed() -> void:
	if not builder:
		return
	var result: Variant = builder.validate_build()
	var is_valid: bool = false
	if result is Dictionary:
		is_valid = result.get("valid", false)
		if not is_valid:
			validation_label.text = "\n".join(result.get("issues", ["Build invalid"]))
			validation_label.add_theme_color_override("font_color", COLOR_RED)
			return
	elif result is bool:
		is_valid = result
	if not is_valid:
		validation_label.text = "Build has issues — fix before proceeding."
		validation_label.add_theme_color_override("font_color", COLOR_RED)
		return

	# Store vehicle data for the combat scene to read
	var vehicle_data: Variant = builder.get_vehicle_data()
	GameManager.match_settings["player_vehicle"] = vehicle_data

	# If multiplayer, send data to peers
	var mode: String = GameManager.match_settings.get("mode", "solo")
	if mode == "host" or mode == "join":
		NetworkManager.send_vehicle_data(vehicle_data)

	GameManager.change_scene("res://scenes/combat.tscn")


## Go back to the lobby without saving.
func _on_back_pressed() -> void:
	GameManager.change_scene("res://scenes/lobby.tscn")


# ─── Stats refresh ───────────────────────────────────────────────────────────

## Recalculate and display the vehicle's aggregate statistics.
func _update_stats() -> void:
	if not builder:
		return
	var parts: Array = builder.placed_parts if builder.get("placed_parts") else []
	var stats: Variant = VehicleStats.calculate(parts, current_domain)
	if stats_panel and stats:
		stats_panel.update_stats(stats)
		stats_panel.update_domain_stats(stats, current_domain)

	# Also refresh budget bar from builder's own tracking
	if builder.get("budget_remaining") != null:
		var total_budget: int = GameManager.match_settings.get("budget", 3000)
		budget_bar.update_spent(total_budget - int(builder.budget_remaining))


# ─── Utility: find builder node ─────────────────────────────────────────────

## Walk up to the scene root and look for a node in the "builder" group,
## or one named "VehicleBuilder".  Returns null if nothing found.
func _find_builder() -> Node:
	var builders: Array[Node] = get_tree().get_nodes_in_group("builder")
	if builders.size() > 0:
		return builders[0]
	# Fallback: look for a node by name in the parent
	var parent: Node = get_parent()
	if parent:
		var found: Node = parent.find_child("VehicleBuilder", true, false)
		if found:
			return found
	return null


# ─── Helpers ─────────────────────────────────────────────────────────────────

## Create a VBoxContainer with a dark semi-transparent background.
func _create_side_panel() -> VBoxContainer:
	var panel: PanelContainer = PanelContainer.new()
	var bg: StyleBoxFlat = StyleBoxFlat.new()
	bg.bg_color = Color(COLOR_BG, 0.92)
	bg.content_margin_left   = 10
	bg.content_margin_right  = 10
	bg.content_margin_top    = 10
	bg.content_margin_bottom = 10
	panel.add_theme_stylebox_override("panel", bg)

	# We need a VBox inside the panel; return the VBox but add the panel
	# to the caller's parent automatically (we will actually just use VBox
	# with its own background for simplicity).
	var vbox: VBoxContainer = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)

	# Give the VBox its own background using a Panel parent trick:
	# Actually, just paint via a ColorRect child.
	var bg_rect: ColorRect = ColorRect.new()
	bg_rect.color = Color(COLOR_BG, 0.92)
	bg_rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bg_rect.show_behind_parent = true
	vbox.add_child(bg_rect)

	return vbox


## Create a small heading label.
func _make_heading(text: String) -> Label:
	var lbl: Label = Label.new()
	lbl.text = text
	lbl.add_theme_font_size_override("font_size", 17)
	lbl.add_theme_color_override("font_color", COLOR_ACCENT)
	return lbl


## Create a themed Button.
func _make_button(text: String, bg_color: Color) -> Button:
	var btn: Button = Button.new()
	btn.text = text
	btn.custom_minimum_size = Vector2(0, 38)
	btn.add_theme_font_size_override("font_size", 15)
	btn.add_theme_color_override("font_color", COLOR_TEXT)
	var sn: StyleBoxFlat = StyleBoxFlat.new()
	sn.bg_color = bg_color
	sn.corner_radius_top_left     = 5
	sn.corner_radius_top_right    = 5
	sn.corner_radius_bottom_left  = 5
	sn.corner_radius_bottom_right = 5
	sn.content_margin_top    = 4
	sn.content_margin_bottom = 4
	sn.content_margin_left   = 8
	sn.content_margin_right  = 8
	btn.add_theme_stylebox_override("normal", sn)
	var sh: StyleBoxFlat = sn.duplicate()
	sh.bg_color = bg_color.lightened(0.12)
	btn.add_theme_stylebox_override("hover", sh)
	var sp: StyleBoxFlat = sn.duplicate()
	sp.bg_color = bg_color.darkened(0.15)
	btn.add_theme_stylebox_override("pressed", sp)
	return btn


## Thin horizontal separator line.
func _separator() -> HSeparator:
	var sep: HSeparator = HSeparator.new()
	sep.add_theme_constant_override("separation", 8)
	return sep
