## part_catalog.gd
## Scrollable, filterable list of parts available for the current domain.
## Each entry shows the part name, cost, and mass.  The player clicks an
## entry to select it as the next piece to place in the builder.
##
## Usage:
##   var catalog = PartCatalog.new()
##   parent.add_child(catalog)
##   catalog.setup("Air")                    # populate with Air parts
##   catalog.part_selected.connect(_on_part) # listen for clicks
##
## Built entirely in code — no .tscn dependency.
extends VBoxContainer
class_name PartCatalog

# ─── Theme constants ─────────────────────────────────────────────────────────
const COLOR_BG: Color         = Color("#1a1a2e")
const COLOR_ACCENT: Color     = Color("#e94560")
const COLOR_SECONDARY: Color  = Color("#0f3460")
const COLOR_TEXT: Color       = Color("#eeeeee")
const COLOR_ITEM_BG: Color    = Color("#222244")
const COLOR_ITEM_HOVER: Color = Color("#2a2a55")
const COLOR_ITEM_SEL: Color   = Color("#0f3460")

# ─── Categories shown in the filter dropdown ─────────────────────────────────
const CATEGORIES: Array[String] = [
	"All",
	"Structural",
	"Propulsion",
	"Weapon",
	"Defense",
	"Utility",
	"Control",
]

# ─── Signals ─────────────────────────────────────────────────────────────────
## Emitted when the player clicks a part entry.  Carries the PartData resource.
signal part_selected(part_data: Variant)

# ─── Data ────────────────────────────────────────────────────────────────────
var all_parts: Array = []        ## Every part available for the current domain.
var filtered_parts: Array = []   ## Subset after category + search filters.
var current_domain: String = ""  ## Domain passed to setup().
var selected_part: Variant = null ## Currently highlighted PartData.

# ─── UI nodes ────────────────────────────────────────────────────────────────
var category_filter: OptionButton   ## "All / Structural / Propulsion / …"
var search_bar: LineEdit            ## Free-text search by part name.
var scroll: ScrollContainer         ## Scrollable wrapper for the list.
var part_list_container: VBoxContainer ## Holds individual PartListItem panels.
var _selected_item: PanelContainer = null ## Reference to the highlighted item.


## Create the filter controls, search bar, and scrollable part list.
func _ready() -> void:
	add_theme_constant_override("separation", 6)

	# ── Category filter ──
	category_filter = OptionButton.new()
	for cat: String in CATEGORIES:
		category_filter.add_item(cat)
	category_filter.custom_minimum_size = Vector2(0, 32)
	category_filter.add_theme_font_size_override("font_size", 14)
	category_filter.add_theme_color_override("font_color", COLOR_TEXT)
	category_filter.item_selected.connect(_on_category_changed)
	add_child(category_filter)

	# ── Search bar ──
	search_bar = LineEdit.new()
	search_bar.placeholder_text = "Search parts..."
	search_bar.custom_minimum_size = Vector2(0, 30)
	search_bar.add_theme_font_size_override("font_size", 14)
	search_bar.add_theme_color_override("font_color", COLOR_TEXT)
	search_bar.text_changed.connect(_on_search_changed)
	add_child(search_bar)

	# ── Scroll container with the part list ──
	scroll = ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)

	part_list_container = VBoxContainer.new()
	part_list_container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	part_list_container.add_theme_constant_override("separation", 4)
	scroll.add_child(part_list_container)


## Load parts for the given domain from PartRegistry and display them.
## Call this once after adding the catalog to the scene tree.
func setup(domain: String) -> void:
	current_domain = domain
	all_parts = PartRegistry.get_parts_for_domain(domain)
	_apply_filters()


## Filter the master part list by category and search text, then rebuild
## the visual list.
func _apply_filters() -> void:
	filtered_parts = all_parts.duplicate()

	# Category filter
	var cat_index: int = category_filter.selected if category_filter else 0
	var cat_name: String = CATEGORIES[cat_index] if cat_index >= 0 and cat_index < CATEGORIES.size() else "All"
	if cat_name != "All":
		var keep: Array = []
		for part: Variant in filtered_parts:
			if part.category.to_lower() == cat_name.to_lower():
				keep.append(part)
		filtered_parts = keep

	# Search text filter (case-insensitive substring match)
	var query: String = search_bar.text.strip_edges().to_lower() if search_bar else ""
	if query != "":
		var keep: Array = []
		for part: Variant in filtered_parts:
			if part.part_name.to_lower().find(query) != -1:
				keep.append(part)
		filtered_parts = keep

	_rebuild_list()


## Remove all existing items and create fresh ones for filtered_parts.
func _rebuild_list() -> void:
	# Clear existing children (skip index 0 if it's a background rect)
	for child: Node in part_list_container.get_children():
		child.queue_free()
	_selected_item = null

	for part: Variant in filtered_parts:
		var item: PanelContainer = _create_part_item(part)
		part_list_container.add_child(item)


## Respond to a change in the category dropdown.
func _on_category_changed(_index: int) -> void:
	_apply_filters()


## Respond to keystrokes in the search bar.
func _on_search_changed(_new_text: String) -> void:
	_apply_filters()


## Handle a click on an individual part list item.
## Highlights the item and emits part_selected.
func _on_part_item_clicked(event: InputEvent, part_data: Variant, item: PanelContainer) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		# Remove highlight from previously selected item
		if _selected_item and is_instance_valid(_selected_item):
			var old_bg: StyleBoxFlat = _selected_item.get_theme_stylebox("panel") as StyleBoxFlat
			if old_bg:
				old_bg.bg_color = COLOR_ITEM_BG

		# Highlight the new item
		var style: StyleBoxFlat = item.get_theme_stylebox("panel") as StyleBoxFlat
		if style:
			style.bg_color = COLOR_ITEM_SEL
		_selected_item = item

		selected_part = part_data
		part_selected.emit(part_data)


## Build a single visual row for a part.
## Layout: [Name]  [$Cost]  [Mass kg]
func _create_part_item(part: Variant) -> PanelContainer:
	var panel: PanelContainer = PanelContainer.new()
	panel.custom_minimum_size = Vector2(0, 40)

	# Background style
	var bg: StyleBoxFlat = StyleBoxFlat.new()
	bg.bg_color = COLOR_ITEM_BG
	bg.corner_radius_top_left     = 4
	bg.corner_radius_top_right    = 4
	bg.corner_radius_bottom_left  = 4
	bg.corner_radius_bottom_right = 4
	bg.content_margin_left   = 8
	bg.content_margin_right  = 8
	bg.content_margin_top    = 4
	bg.content_margin_bottom = 4
	panel.add_theme_stylebox_override("panel", bg)

	# Hover colour change via mouse enter/exit
	panel.mouse_entered.connect(func() -> void:
		if panel != _selected_item:
			(panel.get_theme_stylebox("panel") as StyleBoxFlat).bg_color = COLOR_ITEM_HOVER
	)
	panel.mouse_exited.connect(func() -> void:
		if panel != _selected_item:
			(panel.get_theme_stylebox("panel") as StyleBoxFlat).bg_color = COLOR_ITEM_BG
	)

	# Inner HBox: Name | Cost | Mass
	var hbox: HBoxContainer = HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 6)
	panel.add_child(hbox)

	# Part name (fills available space)
	var name_label: Label = Label.new()
	name_label.text = part.part_name
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.add_theme_font_size_override("font_size", 14)
	name_label.add_theme_color_override("font_color", COLOR_TEXT)
	name_label.clip_text = true
	hbox.add_child(name_label)

	# Cost
	var cost_label: Label = Label.new()
	cost_label.text = "$%d" % part.cost
	cost_label.add_theme_font_size_override("font_size", 13)
	cost_label.add_theme_color_override("font_color", Color("#aaddaa"))
	hbox.add_child(cost_label)

	# Mass
	var mass_label: Label = Label.new()
	mass_label.text = "%skg" % str(part.mass_kg)
	mass_label.add_theme_font_size_override("font_size", 13)
	mass_label.add_theme_color_override("font_color", Color("#aaaacc"))
	hbox.add_child(mass_label)

	# Click detection — forward to handler with the PartData and panel ref
	panel.gui_input.connect(func(event: InputEvent) -> void:
		_on_part_item_clicked(event, part, panel)
	)

	return panel
