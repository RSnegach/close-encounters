## VehicleSerializer (RefCounted)
##
## Handles saving and loading vehicle designs to/from JSON files in the user
## data directory (user://vehicles/).
##
## In Godot, "user://" maps to a platform-specific writeable folder:
##   - Windows: %APPDATA%/Godot/app_userdata/<Project Name>/
##   - Linux:   ~/.local/share/godot/app_userdata/<Project Name>/
##   - macOS:   ~/Library/Application Support/Godot/app_userdata/<Project Name>/
##
## All methods are static, so you never need to instantiate this class.
## Just call the methods directly on the class name:
##
##   VehicleSerializer.save_vehicle(data, "my_tank")
##   var data: Dictionary = VehicleSerializer.load_vehicle("my_tank")
##   var names: Array[String] = VehicleSerializer.list_saved_vehicles()
##   VehicleSerializer.delete_vehicle("my_tank")
##
## The JSON format stores the vehicle as a top-level object with at least:
##   {
##       "domain": "air",         # String -- which domain this vehicle is for
##       "parts": [ ... ],        # Array  -- list of placed parts
##       ...                      # any additional metadata the builder stores
##   }
class_name VehicleSerializer
extends RefCounted


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## Directory under user:// where all vehicle JSON files are stored.
## Created automatically on first save if it does not exist.
const VEHICLES_DIR: String = "user://vehicles/"


# ---------------------------------------------------------------------------
# Save
# ---------------------------------------------------------------------------

## Write a vehicle design dictionary to a JSON file.
##
## [param vehicle_data] is the dictionary produced by the builder or by
## Vehicle.serialize(). It should contain at least a "parts" array and a
## "domain" string.
##
## [param filename] is the file stem (no extension). The ".json" suffix is
## appended automatically, so pass "my_tank", not "my_tank.json".
##
## Returns [code]true[/code] on success, [code]false[/code] on any I/O error.
##
## Example:
##   var ok := VehicleSerializer.save_vehicle({"domain":"land","parts":[]}, "scout")
static func save_vehicle(vehicle_data: Dictionary, filename: String) -> bool:
	# --- Step 1: Ensure the vehicles directory exists ---
	# DirAccess.make_dir_recursive_absolute() creates the directory and any
	# missing parents. It returns OK if created, or ERR_ALREADY_EXISTS if
	# the directory was already there -- both are fine.
	var dir_err: int = DirAccess.make_dir_recursive_absolute(VEHICLES_DIR)
	if dir_err != OK and dir_err != ERR_ALREADY_EXISTS:
		push_error(
			"[VehicleSerializer] Could not create directory '%s'. Error code: %d"
			% [VEHICLES_DIR, dir_err]
		)
		return false

	# --- Step 2: Convert the dictionary to a pretty-printed JSON string ---
	var json_string: String = vehicle_data_to_json(vehicle_data)

	# --- Step 3: Open the file for writing ---
	# Build the full path: "user://vehicles/" + "my_tank" + ".json"
	var path: String = VEHICLES_DIR + filename + ".json"

	var file: FileAccess = FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		# FileAccess.open() returns null on failure. The static method
		# FileAccess.get_open_error() tells us what went wrong.
		push_error(
			"[VehicleSerializer] Failed to open '%s' for writing. Error: %s"
			% [path, FileAccess.get_open_error()]
		)
		return false

	# --- Step 4: Write the JSON string and close the file ---
	# store_string() writes the entire string in one call.
	file.store_string(json_string)
	# Closing flushes any buffered data to disk.
	file.close()

	print("[VehicleSerializer] Saved vehicle to '%s'." % path)
	return true


# ---------------------------------------------------------------------------
# Load
# ---------------------------------------------------------------------------

## Read a vehicle design from a JSON file and return it as a Dictionary.
##
## [param filename] is the file stem (no extension).
##
## Returns the parsed Dictionary on success. Returns an empty Dictionary if:
##   - the file does not exist,
##   - the file cannot be opened,
##   - the JSON is malformed, or
##   - the JSON is missing required keys ("parts" array, "domain" string).
##
## Example:
##   var data: Dictionary = VehicleSerializer.load_vehicle("scout")
##   if data.is_empty():
##       print("Failed to load!")
static func load_vehicle(filename: String) -> Dictionary:
	var path: String = VEHICLES_DIR + filename + ".json"

	# Check that the file exists before trying to open it. This gives a
	# cleaner warning than a raw open-error.
	if not FileAccess.file_exists(path):
		push_warning("[VehicleSerializer] File not found: '%s'." % path)
		return {}

	# Open for reading.
	var file: FileAccess = FileAccess.open(path, FileAccess.READ)
	if file == null:
		push_error(
			"[VehicleSerializer] Failed to open '%s' for reading. Error: %s"
			% [path, FileAccess.get_open_error()]
		)
		return {}

	# Read the entire file contents as a single string.
	var json_string: String = file.get_as_text()
	file.close()

	# Delegate parsing and validation to the shared helper.
	return json_to_vehicle_data(json_string)


# ---------------------------------------------------------------------------
# List
# ---------------------------------------------------------------------------

## Return an array of saved vehicle filenames (without the .json extension).
##
## The order is whatever the OS returns (typically alphabetical on most
## platforms). If the vehicles directory does not exist yet (no saves have
## been made), this returns an empty array.
##
## Example:
##   for name in VehicleSerializer.list_saved_vehicles():
##       print("Found vehicle: ", name)
static func list_saved_vehicles() -> Array[String]:
	var result: Array[String] = []

	# Try to open the vehicles directory. If it doesn't exist, DirAccess.open()
	# returns null -- that just means no vehicles have been saved yet.
	var dir: DirAccess = DirAccess.open(VEHICLES_DIR)
	if dir == null:
		return result

	# Iterate every entry in the directory.
	dir.list_dir_begin()
	var file_name: String = dir.get_next()

	while file_name != "":
		# Skip subdirectories; we only care about .json files.
		if not dir.current_is_dir() and file_name.ends_with(".json"):
			# get_basename() strips the extension: "my_tank.json" -> "my_tank"
			result.append(file_name.get_basename())
		file_name = dir.get_next()

	# Always call list_dir_end() when done iterating to free internal state.
	dir.list_dir_end()

	return result


# ---------------------------------------------------------------------------
# Delete
# ---------------------------------------------------------------------------

## Delete a saved vehicle file from disk.
##
## [param filename] is the file stem (no extension).
##
## Returns [code]true[/code] if the file was successfully deleted,
## [code]false[/code] if it didn't exist or could not be removed.
##
## Example:
##   if VehicleSerializer.delete_vehicle("old_design"):
##       print("Deleted!")
static func delete_vehicle(filename: String) -> bool:
	var path: String = VEHICLES_DIR + filename + ".json"

	# Check existence first for a clear warning message.
	if not FileAccess.file_exists(path):
		push_warning("[VehicleSerializer] Cannot delete -- file not found: '%s'." % path)
		return false

	# DirAccess.remove_absolute() deletes a single file by its full path.
	var err: int = DirAccess.remove_absolute(path)
	if err != OK:
		push_error(
			"[VehicleSerializer] Failed to delete '%s'. Error code: %d" % [path, err]
		)
		return false

	print("[VehicleSerializer] Deleted '%s'." % path)
	return true


# ---------------------------------------------------------------------------
# JSON conversion helpers
# ---------------------------------------------------------------------------

## Convert a vehicle data Dictionary to a pretty-printed JSON string.
##
## Uses tab indentation for readability when the player opens the file in
## a text editor (useful for modding or debugging).
##
## [param data] is the vehicle dictionary (domain, parts, etc.).
## Returns the JSON string.
static func vehicle_data_to_json(data: Dictionary) -> String:
	# JSON.stringify() with "\t" produces human-readable output:
	# {
	#     "domain": "air",
	#     "parts": [
	#         { "id": "cockpit_basic", ... },
	#         ...
	#     ]
	# }
	return JSON.stringify(data, "\t")


## Parse a JSON string into a vehicle data Dictionary.
##
## Performs structural validation:
##   1. The string must be valid JSON.
##   2. The top-level value must be a Dictionary (JSON object).
##   3. The dictionary must contain a "parts" key whose value is an Array.
##   4. The dictionary must contain a "domain" key whose value is a String.
##
## Returns the parsed Dictionary on success, or an empty Dictionary on any
## validation failure (with a warning/error pushed to the console).
##
## [param json_string] is the raw JSON text to parse.
static func json_to_vehicle_data(json_string: String) -> Dictionary:
	# JSON.parse_string() is the simplest Godot 4.x parser: it takes a
	# string and returns a Variant (or null on parse failure).
	var parsed = JSON.parse_string(json_string)

	# Check for parse failure. parse_string() returns null if the JSON
	# syntax is invalid.
	if parsed == null:
		push_error("[VehicleSerializer] JSON parse failed -- invalid syntax.")
		return {}

	# The top-level value must be a dictionary (JSON object), not an array
	# or primitive.
	if not parsed is Dictionary:
		push_error("[VehicleSerializer] Expected a JSON object at the top level.")
		return {}

	var dict: Dictionary = parsed as Dictionary

	# --- Validate required key: "parts" (Array) ---
	# Every vehicle file must have a parts array, even if it's empty.
	if not dict.has("parts"):
		push_warning("[VehicleSerializer] Vehicle data missing required 'parts' key.")
		return {}

	if not dict["parts"] is Array:
		push_warning("[VehicleSerializer] 'parts' must be an Array.")
		return {}

	# --- Validate required key: "domain" (String) ---
	# The domain tells the game which physics mode to use (land, air, etc.).
	if not dict.has("domain"):
		push_warning("[VehicleSerializer] Vehicle data missing required 'domain' key.")
		return {}

	if not dict["domain"] is String:
		push_warning("[VehicleSerializer] 'domain' must be a String.")
		return {}

	# All checks passed -- the data is structurally valid.
	return dict
