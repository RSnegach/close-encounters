## hud.gd
## In-combat heads-up display.  Shows health bar, speed, ammo, a crosshair,
## domain-specific info (altitude / depth / fuel …), a damage flash overlay,
## and a kill-feed in the top-right corner.
##
## Call setup(vehicle) after spawning so the HUD knows which Vehicle to track.
##
## Built entirely in code — the .tscn only needs a root Control with this
## script attached.
extends Control

# ─── Theme constants ─────────────────────────────────────────────────────────
const COLOR_BG: Color        = Color("#1a1a2e")
const COLOR_ACCENT: Color    = Color("#e94560")
const COLOR_TEXT: Color      = Color("#eeeeee")
const COLOR_GREEN: Color     = Color("#4ecca3")
const COLOR_RED: Color       = Color("#e94560")
const COLOR_YELLOW: Color    = Color("#f0c040")
const COLOR_BAR_BG: Color    = Color("#222244")
const COLOR_CROSSHAIR: Color = Color("#cccccc")
const KILL_FEED_TTL: float   = 5.0   ## Seconds before a kill-feed entry fades.

# ─── External reference ──────────────────────────────────────────────────────
var vehicle: Node = null       ## The player's Vehicle node.  Set via setup().

# ─── UI nodes ────────────────────────────────────────────────────────────────
var health_bar: ProgressBar    ## Top-centre health bar.
var health_label: Label        ## Numeric "HP: 340 / 500" overlay.
var speed_label: Label         ## Bottom-left speed readout.
var ammo_label: Label          ## Bottom-centre ammo readout.
var domain_info: VBoxContainer ## Bottom-right column for domain-specific data.
var crosshair: Control         ## Small centred cross.
var damage_flash: ColorRect    ## Full-screen red flash on taking damage.
var kill_feed: VBoxContainer   ## Top-right scrolling destruction messages.
var game_over_label: Label     ## Large centred "VICTORY" / "DEFEATED" text.
var pause_panel: PanelContainer ## Escape menu overlay.
var is_paused: bool = false     ## Whether the game is currently paused.

# ─── Internal state ──────────────────────────────────────────────────────────
var flash_timer: float = 0.0   ## Remaining seconds for damage flash.


## Build every HUD element programmatically.
func _ready() -> void:
	# Make sure the HUD itself doesn't eat mouse clicks meant for the 3D world.
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_anchors_preset(Control.PRESET_FULL_RECT)

	_create_health_bar()
	_create_bottom_bar()
	_create_crosshair()
	_create_damage_flash()
	_create_kill_feed()
	_create_game_over_label()
	_create_pause_menu()


## Bind the HUD to a specific Vehicle node.
## Call this once the vehicle is spawned and ready.
func setup(player_vehicle: Node) -> void:
	vehicle = player_vehicle
	# Connect vehicle signals if they exist
	if vehicle.has_signal("damage_taken"):
		vehicle.damage_taken.connect(func(_dmg: Variant = null) -> void: show_damage_flash())
	if vehicle.has_signal("vehicle_destroyed"):
		vehicle.vehicle_destroyed.connect(func() -> void: show_game_over(false))


## Per-frame update: refresh health, speed, ammo, and domain info.
func _process(delta: float) -> void:
	# Fade damage flash
	if flash_timer > 0.0:
		flash_timer -= delta
		damage_flash.modulate.a = clampf(flash_timer / 0.3, 0.0, 0.3)
		if flash_timer <= 0.0:
			damage_flash.modulate.a = 0.0

	if not vehicle or not vehicle.is_alive:
		return

	# ── Health ──
	_update_health()

	# ── Speed ──
	speed_label.text = "SPD: %.0f m/s" % vehicle.get_speed()

	# ── Ammo + active weapon ──
	_update_ammo()
	_update_weapon_indicator()

	# ── Domain-specific info ──
	# Physics controllers' get_hud_data() expects the vehicle as an argument.
	if vehicle.get("physics_controller") and vehicle.physics_controller.has_method("get_hud_data"):
		_update_domain_info(vehicle.physics_controller.get_hud_data(vehicle))


# ─── Pause menu ──────────────────────────────────────────────────────────────

## Toggle pause when Escape is pressed.
func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey:
		var key: InputEventKey = event as InputEventKey
		if key.pressed and key.physical_keycode == KEY_ESCAPE:
			_toggle_pause()
			get_viewport().set_input_as_handled()


## Pause or unpause the game.
func _toggle_pause() -> void:
	is_paused = not is_paused
	get_tree().paused = is_paused
	pause_panel.visible = is_paused
	if is_paused:
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
	else:
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)


func _on_resume_pressed() -> void:
	_toggle_pause()


func _on_restart_pressed() -> void:
	get_tree().paused = false
	is_paused = false
	GameManager.change_scene("res://scenes/builder.tscn")


func _on_quit_to_menu_pressed() -> void:
	get_tree().paused = false
	is_paused = false
	NetworkManager.disconnect_game()
	GameManager.change_scene("res://scenes/main_menu.tscn")


# ─── Health ──────────────────────────────────────────────────────────────────

## Sum current / max HP across all parts and update the bar + label.
func _update_health() -> void:
	var current_hp: float = 0.0
	var max_hp: float     = 0.0
	if vehicle.get("parts") and vehicle.parts is Dictionary:
		# vehicle.parts is Dict[Vector3i -> PartNode]. Iterate values and
		# deduplicate (multi-cell parts appear multiple times).
		var seen: Dictionary = {}
		for cell: Variant in vehicle.parts:
			var part_node: Variant = vehicle.parts[cell]
			if part_node == null:
				continue
			var nid: int = part_node.get_instance_id()
			if seen.has(nid):
				continue
			seen[nid] = true
			if part_node.get("current_hp") != null:
				current_hp += part_node.current_hp
			if part_node.get("part_data") != null and part_node.part_data.get("hp") != null:
				max_hp += part_node.part_data.hp
	else:
		# Fallback: vehicle exposes total values directly
		current_hp = vehicle.get("current_hp") if vehicle.get("current_hp") != null else 0.0
		max_hp     = vehicle.get("max_hp") if vehicle.get("max_hp") != null else 1.0

	health_bar.max_value = max_hp
	health_bar.value     = current_hp
	health_label.text    = "HP: %d / %d" % [int(current_hp), int(max_hp)]

	# Colour the bar
	var pct: float = current_hp / max_hp if max_hp > 0.0 else 0.0
	var bar_color: Color
	if pct > 0.5:
		bar_color = COLOR_GREEN
	elif pct > 0.25:
		bar_color = COLOR_YELLOW
	else:
		bar_color = COLOR_RED
	var fill: StyleBoxFlat = health_bar.get_theme_stylebox("fill") as StyleBoxFlat
	if fill:
		fill.bg_color = bar_color


# ─── Ammo ────────────────────────────────────────────────────────────────────

## Sum remaining ammo across all weapons on the vehicle.
func _update_ammo() -> void:
	var total_ammo: int = 0
	if vehicle.get("weapons"):
		for w: Variant in vehicle.weapons:
			# Per-weapon ammo tracked via metadata, falls back to stats max.
			if w.has_meta("_current_ammo"):
				total_ammo += int(w.get_meta("_current_ammo"))
			elif w.get("part_data") != null and w.part_data.stats.has("ammo"):
				total_ammo += int(w.part_data.stats["ammo"])
	ammo_label.text = "AMMO: %d" % total_ammo


## Show which weapon is currently active below the ammo count.
func _update_weapon_indicator() -> void:
	if not vehicle.get("weapons") or not vehicle.get("active_weapon_index") == null:
		return
	var idx: int = vehicle.active_weapon_index if vehicle.get("active_weapon_index") != null else -1
	var weapon_text: String = ""
	if idx == -1:
		weapon_text = "WPN: ALL"
	elif idx >= 0 and idx < vehicle.weapons.size():
		var w = vehicle.weapons[idx]
		if w.get("part_data") != null:
			weapon_text = "WPN: %s" % w.part_data.part_name
		else:
			weapon_text = "WPN: #%d" % (idx + 1)
	ammo_label.text += "  |  " + weapon_text


# ─── Domain info ─────────────────────────────────────────────────────────────

## Replace the domain_info column with key-value pairs from the vehicle's
## physics controller.  Expected keys depend on domain:
##   Air: altitude, throttle, stall_warning
##   Submarine: depth, ballast, pressure_warning
##   Space: fuel_pct, twr, altitude
##   Ground / Water: whatever the controller provides
func _update_domain_info(data: Dictionary) -> void:
	# Clear old labels
	for child: Node in domain_info.get_children():
		child.queue_free()

	for key: String in data.keys():
		if key == "speed":
			continue  # already shown in speed_label
		var lbl: Label = Label.new()
		var value: Variant = data[key]
		# Format the value nicely
		if value is float:
			lbl.text = "%s: %.1f" % [key.capitalize(), value]
		elif value is bool:
			lbl.text = "%s: %s" % [key.capitalize(), "YES" if value else "NO"]
			lbl.add_theme_color_override("font_color", COLOR_RED if key.ends_with("warning") and value else COLOR_TEXT)
		else:
			lbl.text = "%s: %s" % [key.capitalize(), str(value)]
		lbl.add_theme_font_size_override("font_size", 14)
		lbl.add_theme_color_override("font_color", COLOR_TEXT)
		lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		domain_info.add_child(lbl)


# ─── Damage flash ────────────────────────────────────────────────────────────

## Trigger a brief red screen flash to indicate the player took damage.
func show_damage_flash() -> void:
	damage_flash.modulate.a = 0.3
	flash_timer = 0.3


# ─── Kill feed ───────────────────────────────────────────────────────────────

## Add a line to the kill feed (e.g. "Enemy Tank destroyed!").
## The entry auto-removes after KILL_FEED_TTL seconds.
func add_kill_feed_entry(message: String) -> void:
	var lbl: Label = Label.new()
	lbl.text = message
	lbl.add_theme_font_size_override("font_size", 14)
	lbl.add_theme_color_override("font_color", COLOR_ACCENT)
	lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	kill_feed.add_child(lbl)

	# Auto-remove via a one-shot timer
	var timer: Timer = Timer.new()
	timer.wait_time = KILL_FEED_TTL
	timer.one_shot = true
	timer.autostart = true
	timer.timeout.connect(func() -> void:
		if is_instance_valid(lbl):
			lbl.queue_free()
		timer.queue_free()
	)
	add_child(timer)


# ─── Game over ───────────────────────────────────────────────────────────────

## Show a large centred label — "VICTORY!" or "DEFEATED".
func show_game_over(won: bool) -> void:
	game_over_label.visible = true
	if won:
		game_over_label.text = "VICTORY!"
		game_over_label.add_theme_color_override("font_color", COLOR_GREEN)
	else:
		game_over_label.text = "DEFEATED"
		game_over_label.add_theme_color_override("font_color", COLOR_RED)


# ═════════════════════════════════════════════════════════════════════════════
# Node creation helpers — each builds a section of the HUD
# ═════════════════════════════════════════════════════════════════════════════

## Health bar across the top centre of the screen.
func _create_health_bar() -> void:
	var container: MarginContainer = MarginContainer.new()
	container.set_anchors_preset(Control.PRESET_TOP_WIDE)
	container.add_theme_constant_override("margin_left", 200)
	container.add_theme_constant_override("margin_right", 200)
	container.add_theme_constant_override("margin_top", 16)
	container.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(container)

	var vbox: VBoxContainer = VBoxContainer.new()
	vbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	container.add_child(vbox)

	health_bar = ProgressBar.new()
	health_bar.custom_minimum_size = Vector2(0, 24)
	health_bar.max_value = 100
	health_bar.value     = 100
	health_bar.show_percentage = false
	health_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# Background
	var bg: StyleBoxFlat = StyleBoxFlat.new()
	bg.bg_color = COLOR_BAR_BG
	bg.corner_radius_top_left     = 4
	bg.corner_radius_top_right    = 4
	bg.corner_radius_bottom_left  = 4
	bg.corner_radius_bottom_right = 4
	health_bar.add_theme_stylebox_override("background", bg)
	# Fill
	var fill: StyleBoxFlat = StyleBoxFlat.new()
	fill.bg_color = COLOR_GREEN
	fill.corner_radius_top_left     = 4
	fill.corner_radius_top_right    = 4
	fill.corner_radius_bottom_left  = 4
	fill.corner_radius_bottom_right = 4
	health_bar.add_theme_stylebox_override("fill", fill)
	vbox.add_child(health_bar)

	health_label = Label.new()
	health_label.text = "HP: — / —"
	health_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	health_label.add_theme_font_size_override("font_size", 14)
	health_label.add_theme_color_override("font_color", COLOR_TEXT)
	health_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox.add_child(health_label)


## Bottom bar: speed on the left, ammo in the centre, domain info on the right.
func _create_bottom_bar() -> void:
	# Dark semi-transparent panel behind the bottom readouts.
	var panel: PanelContainer = PanelContainer.new()
	panel.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	panel.offset_top    = -80
	panel.offset_left   = 0
	panel.offset_right  = 0
	panel.offset_bottom = 0
	panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var panel_style: StyleBoxFlat = StyleBoxFlat.new()
	panel_style.bg_color = Color(0.0, 0.0, 0.0, 0.6)
	panel_style.content_margin_left   = 16
	panel_style.content_margin_right  = 16
	panel_style.content_margin_top    = 8
	panel_style.content_margin_bottom = 8
	panel.add_theme_stylebox_override("panel", panel_style)
	add_child(panel)

	var hbox: HBoxContainer = HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 20)
	hbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	panel.add_child(hbox)

	# Speed (left)
	speed_label = Label.new()
	speed_label.text = "SPD: 0 m/s"
	speed_label.add_theme_font_size_override("font_size", 18)
	speed_label.add_theme_color_override("font_color", COLOR_TEXT)
	speed_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	speed_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hbox.add_child(speed_label)

	# Ammo (centre)
	ammo_label = Label.new()
	ammo_label.text = "AMMO: 0"
	ammo_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	ammo_label.add_theme_font_size_override("font_size", 18)
	ammo_label.add_theme_color_override("font_color", COLOR_TEXT)
	ammo_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	ammo_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hbox.add_child(ammo_label)

	# Domain info (right column)
	domain_info = VBoxContainer.new()
	domain_info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	domain_info.add_theme_constant_override("separation", 2)
	domain_info.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hbox.add_child(domain_info)


## Centred crosshair made of thin ColorRects forming a + shape.
func _create_crosshair() -> void:
	crosshair = CenterContainer.new()
	crosshair.set_anchors_preset(Control.PRESET_CENTER)
	crosshair.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(crosshair)

	# Container for the cross lines
	var holder: Control = Control.new()
	holder.custom_minimum_size = Vector2(24, 24)
	holder.mouse_filter = Control.MOUSE_FILTER_IGNORE
	crosshair.add_child(holder)

	# Horizontal bar
	var h_bar: ColorRect = ColorRect.new()
	h_bar.color = COLOR_CROSSHAIR
	h_bar.size = Vector2(24, 2)
	h_bar.position = Vector2(0, 11)
	h_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	holder.add_child(h_bar)

	# Vertical bar
	var v_bar: ColorRect = ColorRect.new()
	v_bar.color = COLOR_CROSSHAIR
	v_bar.size = Vector2(2, 24)
	v_bar.position = Vector2(11, 0)
	v_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	holder.add_child(v_bar)


## Full-screen red overlay that flashes when the player takes damage.
## Starts fully transparent.
func _create_damage_flash() -> void:
	damage_flash = ColorRect.new()
	damage_flash.color = COLOR_RED
	damage_flash.modulate.a = 0.0
	damage_flash.set_anchors_preset(Control.PRESET_FULL_RECT)
	damage_flash.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(damage_flash)


## Top-right column for scrolling kill / destruction messages.
func _create_kill_feed() -> void:
	kill_feed = VBoxContainer.new()
	kill_feed.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	kill_feed.offset_left  = -300
	kill_feed.offset_right = -16
	kill_feed.offset_top   = 60
	kill_feed.add_theme_constant_override("separation", 4)
	kill_feed.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(kill_feed)


## Large centred label for the end-of-match result.  Hidden until show_game_over().
func _create_game_over_label() -> void:
	game_over_label = Label.new()
	game_over_label.set_anchors_preset(Control.PRESET_CENTER)
	game_over_label.grow_horizontal = Control.GROW_DIRECTION_BOTH
	game_over_label.grow_vertical   = Control.GROW_DIRECTION_BOTH
	game_over_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	game_over_label.vertical_alignment   = VERTICAL_ALIGNMENT_CENTER
	game_over_label.add_theme_font_size_override("font_size", 72)
	game_over_label.add_theme_color_override("font_color", COLOR_GREEN)
	game_over_label.text = ""
	game_over_label.visible = false
	game_over_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(game_over_label)


## Pause menu overlay — shown when Escape is pressed.
func _create_pause_menu() -> void:
	pause_panel = PanelContainer.new()
	pause_panel.set_anchors_preset(Control.PRESET_CENTER)
	pause_panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	pause_panel.grow_vertical   = Control.GROW_DIRECTION_BOTH
	pause_panel.custom_minimum_size = Vector2(300, 280)
	pause_panel.visible = false
	# This node must NOT be ignored by mouse so buttons work.
	pause_panel.mouse_filter = Control.MOUSE_FILTER_STOP
	# Must keep processing while paused so we can unpause.
	pause_panel.process_mode = Node.PROCESS_MODE_ALWAYS
	process_mode = Node.PROCESS_MODE_ALWAYS

	# Dark panel background
	var style: StyleBoxFlat = StyleBoxFlat.new()
	style.bg_color = Color(0.05, 0.05, 0.1, 0.9)
	style.border_color = Color("#e94560")
	style.border_width_top    = 2
	style.border_width_bottom = 2
	style.border_width_left   = 2
	style.border_width_right  = 2
	style.corner_radius_top_left     = 8
	style.corner_radius_top_right    = 8
	style.corner_radius_bottom_left  = 8
	style.corner_radius_bottom_right = 8
	style.content_margin_top    = 20
	style.content_margin_bottom = 20
	style.content_margin_left   = 20
	style.content_margin_right  = 20
	pause_panel.add_theme_stylebox_override("panel", style)

	var vbox: VBoxContainer = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 12)
	pause_panel.add_child(vbox)

	# Title
	var title: Label = Label.new()
	title.text = "PAUSED"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 32)
	title.add_theme_color_override("font_color", Color("#e94560"))
	vbox.add_child(title)

	# Spacer
	var spacer: Control = Control.new()
	spacer.custom_minimum_size = Vector2(0, 8)
	vbox.add_child(spacer)

	# Resume button
	var resume_btn: Button = _create_pause_button("Resume")
	resume_btn.pressed.connect(_on_resume_pressed)
	vbox.add_child(resume_btn)

	# Restart (back to builder)
	var restart_btn: Button = _create_pause_button("Restart (Builder)")
	restart_btn.pressed.connect(_on_restart_pressed)
	vbox.add_child(restart_btn)

	# Quit to menu
	var quit_btn: Button = _create_pause_button("Quit to Menu")
	quit_btn.pressed.connect(_on_quit_to_menu_pressed)
	vbox.add_child(quit_btn)

	add_child(pause_panel)


## Helper to create a styled button for the pause menu.
func _create_pause_button(text: String) -> Button:
	var btn: Button = Button.new()
	btn.text = text
	btn.custom_minimum_size = Vector2(250, 44)
	btn.add_theme_font_size_override("font_size", 18)
	btn.add_theme_color_override("font_color", Color("#eeeeee"))
	var sn: StyleBoxFlat = StyleBoxFlat.new()
	sn.bg_color = Color("#0f3460")
	sn.corner_radius_top_left     = 6
	sn.corner_radius_top_right    = 6
	sn.corner_radius_bottom_left  = 6
	sn.corner_radius_bottom_right = 6
	sn.content_margin_top    = 8
	sn.content_margin_bottom = 8
	btn.add_theme_stylebox_override("normal", sn)
	var sh: StyleBoxFlat = sn.duplicate()
	sh.bg_color = Color("#0f3460").lightened(0.15)
	btn.add_theme_stylebox_override("hover", sh)
	var sp: StyleBoxFlat = sn.duplicate()
	sp.bg_color = Color("#e94560")
	btn.add_theme_stylebox_override("pressed", sp)
	return btn
