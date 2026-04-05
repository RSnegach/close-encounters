## PartRegistry (Autoload Singleton)
##
## Loads every part definition from the JSON files inside res://data/parts/
## at startup and makes them available for lookup by ID, category, subcategory,
## or domain. Other systems (builder UI, factory, shop) query this singleton
## instead of reading files themselves.
##
## JSON file format expected -- each file is a JSON array of part objects:
##   [
##     {
##       "id": "chassis_light_01",
##       "name": "Light Frame",
##       "category": "chassis",
##       "subcategory": "frame",
##       "cost": 200,
##       "mass_kg": 50.0,
##       "hp": 100,
##       "size": [2, 1, 3],
##       "drag": 0.3,
##       "domains": ["ground", "water"],
##       "mount_points": ["top", "left", "right"],
##       "stats": { "speed_bonus": 5 },
##       "mesh": { "type": "box", "color": "#4488AA" }
##     },
##     ...
##   ]
extends Node


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## Directory where part JSON files live. Each .json file in this folder
## will be loaded automatically on startup.
const PARTS_DATA_DIR: String = "res://data/parts/"


# ---------------------------------------------------------------------------
# Private variables
# ---------------------------------------------------------------------------

## Master dictionary of all loaded parts. Keyed by part ID (String).
var _parts: Dictionary = {}

## Pre-built index of parts grouped by category. Keyed by category name
## (String), values are Array[PartData].
var _parts_by_category: Dictionary = {}

## Pre-built index of parts grouped by subcategory.
var _parts_by_subcategory: Dictionary = {}


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

## Called once at startup. Kicks off the loading process.
func _ready() -> void:
	_load_all_parts()


# ---------------------------------------------------------------------------
# Part loading (private)
# ---------------------------------------------------------------------------

## Scan the parts data directory for JSON files and load each one.
## After loading, the category and subcategory indices are built.
func _load_all_parts() -> void:
	# DirAccess.open returns null if the directory doesn't exist yet.
	var dir: DirAccess = DirAccess.open(PARTS_DATA_DIR)
	if dir == null:
		push_warning(
			"[PartRegistry] Parts directory '%s' not found. No parts loaded. "
			% PARTS_DATA_DIR
			+ "Create the folder and add JSON files to populate the registry."
		)
		return

	# Iterate over every file in the directory.
	dir.list_dir_begin()
	var file_name: String = dir.get_next()
	while file_name != "":
		# Only process .json files (skip .import, folders, etc.)
		if not dir.current_is_dir() and file_name.ends_with(".json"):
			var full_path: String = PARTS_DATA_DIR + file_name
			_load_part_file(full_path)
		file_name = dir.get_next()
	dir.list_dir_end()

	# Build the lookup indices after all files are loaded.
	_rebuild_indices()

	print("[PartRegistry] Loaded %d part(s) from '%s'." % [_parts.size(), PARTS_DATA_DIR])


## Open a single JSON file, parse it, and feed each entry to [method _parse_part].
## [param path] is the full resource path to the JSON file.
func _load_part_file(path: String) -> void:
	var file: FileAccess = FileAccess.open(path, FileAccess.READ)
	if file == null:
		push_error("[PartRegistry] Could not open '%s'." % path)
		return

	var text: String = file.get_as_text()
	file.close()

	# Parse the JSON text.
	var json: JSON = JSON.new()
	var error: int = json.parse(text)
	if error != OK:
		push_error(
			"[PartRegistry] JSON parse error in '%s' at line %d: %s"
			% [path, json.get_error_line(), json.get_error_message()]
		)
		return

	var data = json.data

	# Each file should contain an array of part dictionaries.
	if data is Array:
		for entry in data:
			if entry is Dictionary:
				var part: PartData = _parse_part(entry)
				if part != null:
					_parts[part.id] = part
			else:
				push_warning("[PartRegistry] Skipping non-dictionary entry in '%s'." % path)
	else:
		push_warning("[PartRegistry] Expected a JSON array in '%s', got %s." % [path, typeof(data)])


## Create a [PartData] resource from a raw dictionary parsed from JSON.
## Returns null if the dictionary is missing required fields.
func _parse_part(data: Dictionary) -> PartData:
	if not data.has("id"):
		push_warning("[PartRegistry] Part entry missing 'id' field. Skipping.")
		return null
	return PartData.from_dict(data)


## Rebuild the category and subcategory index dictionaries from the master
## _parts dictionary. Called once after all files are loaded.
func _rebuild_indices() -> void:
	_parts_by_category.clear()
	_parts_by_subcategory.clear()

	for part_id: String in _parts:
		var part: PartData = _parts[part_id]

		# Category index
		if not _parts_by_category.has(part.category):
			_parts_by_category[part.category] = []
		_parts_by_category[part.category].append(part)

		# Subcategory index
		if part.subcategory != "":
			if not _parts_by_subcategory.has(part.subcategory):
				_parts_by_subcategory[part.subcategory] = []
			_parts_by_subcategory[part.subcategory].append(part)


# ---------------------------------------------------------------------------
# Public lookups
# ---------------------------------------------------------------------------

## Return the [PartData] for the given [param id], or null if not found.
func get_part(id: String) -> PartData:
	if _parts.has(id):
		return _parts[id]
	push_warning("[PartRegistry] Part '%s' not found." % id)
	return null


## Return every loaded part as a flat array.
func get_all_parts() -> Array[PartData]:
	var result: Array[PartData] = []
	for part_id: String in _parts:
		result.append(_parts[part_id])
	return result


## Return all parts that belong to the given [param category]
## (e.g. "chassis", "weapon", "control"). Returns an empty array if the
## category has no parts.
func get_parts_by_category(category: String) -> Array[PartData]:
	if _parts_by_category.has(category):
		# Return a copy so callers can't accidentally modify our index.
		var result: Array[PartData] = []
		result.assign(_parts_by_category[category])
		return result
	return [] as Array[PartData]


## Return all parts whose [member PartData.domains] array includes the given
## [param domain] (e.g. "ground", "air", "water", "submarine", "space").
func get_parts_for_domain(domain: String) -> Array[PartData]:
	var result: Array[PartData] = []
	for part_id: String in _parts:
		var part: PartData = _parts[part_id]
		if part.is_valid_for_domain(domain):
			result.append(part)
	return result


## Return all parts that belong to the given [param subcategory]
## (e.g. "frame", "turret", "engine"). Returns an empty array if none match.
func get_parts_by_subcategory(subcategory: String) -> Array[PartData]:
	if _parts_by_subcategory.has(subcategory):
		var result: Array[PartData] = []
		result.assign(_parts_by_subcategory[subcategory])
		return result
	return [] as Array[PartData]
