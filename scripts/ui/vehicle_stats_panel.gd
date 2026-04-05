## vehicle_stats_panel.gd
## Live read-out of computed vehicle statistics during the build phase.
## Shows mass, part count, HP, cost, thrust, TWR, weapons, and
## domain-specific stats (wing area, hull volume, fuel, etc.).
##
## Usage:
##   var panel = VehicleStatsPanel.new()
##   parent.add_child(panel)
##   panel.update_stats(my_vehicle_stats)
##   panel.update_domain_stats(my_vehicle_stats, "Air")
##
## Built entirely in code — no .tscn dependency.
extends VBoxContainer
class_name VehicleStatsPanel

# ─── Theme constants ─────────────────────────────────────────────────────────
const COLOR_TEXT: Color   = Color("#eeeeee")
const COLOR_ACCENT: Color = Color("#e94560")
const COLOR_GREEN: Color  = Color("#4ecca3")
const COLOR_RED: Color    = Color("#e94560")
const COLOR_DIM: Color    = Color("#999999")

# ─── Data ────────────────────────────────────────────────────────────────────
## Maps a stat key -> the Label node that shows its value.
## Populated by _create_stat_row().
var stat_labels: Dictionary = {}

## Container for domain-specific stat rows (cleared/rebuilt on domain change).
var domain_container: VBoxContainer


## Build all the default stat rows that appear for every domain.
func _ready() -> void:
	add_theme_constant_override("separation", 2)

	# Universal stats
	_create_stat_row("Parts",      "0")
	_create_stat_row("Mass",       "0.0 kg")
	_create_stat_row("HP",         "0")
	_create_stat_row("Cost",       "$0")
	_create_stat_row("Thrust",     "0 N")
	_create_stat_row("TWR",        "0.00")
	_create_stat_row("Est. Speed", "0.0 m/s")
	_create_stat_row("Weapons",    "0")
	_create_stat_row("Control",    "—")
	_create_stat_row("Propulsion", "—")

	# Separator before domain stats
	var sep: HSeparator = HSeparator.new()
	sep.add_theme_constant_override("separation", 6)
	add_child(sep)

	# Domain-specific container (filled by update_domain_stats)
	domain_container = VBoxContainer.new()
	domain_container.add_theme_constant_override("separation", 2)
	add_child(domain_container)


## Refresh every universal stat label from a VehicleStats object.
## Expected properties on `stats`: part_count, total_mass, total_hp,
## total_cost, total_thrust, thrust_to_weight, projected_max_speed,
## weapon_count, has_control, has_propulsion.
func update_stats(stats: Variant) -> void:
	_set_stat("Parts",      str(stats.part_count))
	_set_stat("Mass",       "%.1f kg" % stats.total_mass)
	_set_stat("HP",         str(stats.total_hp))
	_set_stat("Cost",       "$%d" % stats.total_cost)
	_set_stat("Thrust",     "%.0f N" % stats.total_thrust)

	# TWR — colour red when below 1 (insufficient thrust to fly/move in some domains)
	var twr_text: String = "%.2f" % stats.thrust_to_weight
	_set_stat("TWR", twr_text)
	var twr_label: Label = stat_labels.get("TWR") as Label
	if twr_label:
		twr_label.add_theme_color_override("font_color", COLOR_RED if stats.thrust_to_weight < 1.0 else COLOR_GREEN)

	_set_stat("Est. Speed", "%.1f m/s" % stats.projected_max_speed)
	_set_stat("Weapons",    str(stats.weapon_count))

	# Control indicator
	_set_bool_stat("Control", stats.has_control)

	# Propulsion indicator
	_set_bool_stat("Propulsion", stats.has_propulsion)


## Show domain-specific stats (wing area for Air, hull volume for Water, etc.).
## Clears previous domain rows before populating.
func update_domain_stats(stats: Variant, domain: String) -> void:
	# Remove old domain rows
	for child: Node in domain_container.get_children():
		child.queue_free()

	match domain:
		"Air":
			_create_domain_row("Wing Area", "%.1f m2" % stats.wing_area if stats.get("wing_area") != null else "—")
		"Water":
			_create_domain_row("Hull Volume", "%.1f m3" % stats.hull_volume if stats.get("hull_volume") != null else "—")
		"Submarine":
			_create_domain_row("Hull Volume", "%.1f m3" % stats.hull_volume if stats.get("hull_volume") != null else "—")
		"Space":
			_create_domain_row("Fuel", "%.0f L" % stats.fuel_capacity if stats.get("fuel_capacity") != null else "—")
		"Ground":
			pass  # no extra stats yet
		_:
			pass


# ─── Internal helpers ────────────────────────────────────────────────────────

## Add a row with a dim label on the left and a value label on the right.
## Stores the value Label in stat_labels[label_text] for later updates.
func _create_stat_row(label_text: String, initial_value: String) -> Label:
	var hbox: HBoxContainer = HBoxContainer.new()
	add_child(hbox)

	# Name (left)
	var name_lbl: Label = Label.new()
	name_lbl.text = label_text + ":"
	name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_lbl.add_theme_font_size_override("font_size", 13)
	name_lbl.add_theme_color_override("font_color", COLOR_DIM)
	hbox.add_child(name_lbl)

	# Value (right)
	var value_lbl: Label = Label.new()
	value_lbl.text = initial_value
	value_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	value_lbl.add_theme_font_size_override("font_size", 13)
	value_lbl.add_theme_color_override("font_color", COLOR_TEXT)
	hbox.add_child(value_lbl)

	stat_labels[label_text] = value_lbl
	return value_lbl


## Convenience: set the text of a previously created stat row.
func _set_stat(key: String, value: String) -> void:
	var lbl: Label = stat_labels.get(key) as Label
	if lbl:
		lbl.text = value


## Set a boolean stat to green "Yes" or red "MISSING".
func _set_bool_stat(key: String, has_it: bool) -> void:
	var lbl: Label = stat_labels.get(key) as Label
	if lbl:
		lbl.text = "Yes" if has_it else "MISSING"
		lbl.add_theme_color_override("font_color", COLOR_GREEN if has_it else COLOR_RED)


## Add a stat row to the domain-specific container (not the main one).
func _create_domain_row(label_text: String, value_text: String) -> void:
	var hbox: HBoxContainer = HBoxContainer.new()
	domain_container.add_child(hbox)

	var name_lbl: Label = Label.new()
	name_lbl.text = label_text + ":"
	name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_lbl.add_theme_font_size_override("font_size", 13)
	name_lbl.add_theme_color_override("font_color", COLOR_DIM)
	hbox.add_child(name_lbl)

	var value_lbl: Label = Label.new()
	value_lbl.text = value_text
	value_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	value_lbl.add_theme_font_size_override("font_size", 13)
	value_lbl.add_theme_color_override("font_color", COLOR_TEXT)
	hbox.add_child(value_lbl)
