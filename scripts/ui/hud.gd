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

	# ── Ammo ──
	_update_ammo()

	# ── Domain-specific info ──
	# Physics controllers' get_hud_data() expects the vehicle as an argument.
	if vehicle.get("physics_controller") and vehicle.physics_controller.has_method("get_hud_data"):
		_update_domain_info(vehicle.physics_controller.get_hud_data(vehicle))


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
			if w.get("current_ammo") != null:
				total_ammo += int(w.current_ammo)
			elif w.get("ammo") != null:
				total_ammo += int(w.ammo)
	ammo_label.text = "AMMO: %d" % total_ammo


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
	var hbox: HBoxContainer = HBoxContainer.new()
	hbox.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	hbox.offset_top    = -80
	hbox.offset_left   = 16
	hbox.offset_right  = -16
	hbox.offset_bottom = -16
	hbox.add_theme_constant_override("separation", 20)
	hbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(hbox)

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
