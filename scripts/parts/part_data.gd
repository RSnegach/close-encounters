## PartData -- Data container for a single part definition.
##
## This is a [Resource] subclass that holds every property from the part JSON
## schema. It does NOT contain any scene/node logic -- it is pure data.
##
## Use [method from_dict] to create a PartData from a parsed JSON dictionary,
## and [method to_dict] to serialize it back. The [PartRegistry] autoload
## stores an instance of this for every part in the game.
##
## Example JSON entry this maps to:
##   {
##     "id": "chassis_light_01",
##     "name": "Light Frame",
##     "category": "chassis",
##     "subcategory": "frame",
##     "cost": 200,
##     "mass_kg": 50.0,
##     "hp": 100,
##     "size": [2, 1, 3],
##     "drag": 0.3,
##     "domains": ["ground", "water"],
##     "mount_points": ["top", "left", "right"],
##     "stats": { "speed_bonus": 5 },
##     "mesh": { "type": "box", "color": "#4488AA" }
##   }
class_name PartData
extends Resource


# ---------------------------------------------------------------------------
# Exported properties
# ---------------------------------------------------------------------------
# @export lets these show up in the Godot inspector if you ever want to
# edit a PartData resource by hand in the editor.

## Unique string identifier for this part (e.g. "chassis_light_01").
@export var id: String = ""

## Human-readable display name. Called "name" in the JSON; renamed here
## because "name" is already a property on Godot's Object class.
@export var part_name: String = ""

## Broad category this part belongs to (e.g. "chassis", "weapon", "control",
## "armor", "utility", "movement").
@export var category: String = ""

## More specific grouping within the category (e.g. "frame", "turret",
## "engine", "thruster").
@export var subcategory: String = ""

## Dollar cost used by the budget system in the builder.
@export var cost: int = 0

## Mass in kilograms. Affects vehicle physics (acceleration, top speed, etc.).
@export var mass_kg: float = 0.0

## Hit points. When reduced to zero the part is destroyed.
@export var hp: int = 50

## Grid footprint of the part in cells (width, height, depth).
## Vector3i because grid positions are always integers.
@export var size: Vector3i = Vector3i(1, 1, 1)

## Aerodynamic drag coefficient. Higher = more air resistance.
@export var drag: float = 0.5

## Which combat domains this part is allowed in. An empty array means it
## works everywhere. Possible values: "ground", "air", "water", "submarine",
## "space".
@export var domains: Array[String] = []

## Named attachment points on this part where other parts can connect.
## e.g. ["top", "bottom", "left", "right", "front", "back"].
@export var mount_points: Array[String] = []

## Free-form dictionary of part-specific stats. Contents vary by category.
## Examples: {"damage": 25, "fire_rate": 2.0} for a weapon,
## {"thrust": 500} for an engine, {"armor_rating": 3} for armor.
@export var stats: Dictionary = {}

## Mesh generation data. In the JSON this is the "mesh" key.
## Expected keys: "type" (String, e.g. "box"), "color" (String, e.g. "#FF0000").
## More complex mesh types can be added later (e.g. "model" with a scene path).
@export var mesh_data: Dictionary = {}


# ---------------------------------------------------------------------------
# Static factory method
# ---------------------------------------------------------------------------

## Create and populate a [PartData] from a dictionary (typically parsed from
## JSON). Missing keys fall back to their default values.
## Returns the new [PartData] instance.
static func from_dict(data: Dictionary) -> PartData:
	var part: PartData = PartData.new()

	part.id = data.get("id", "")
	# JSON uses "name"; we store it as part_name.
	part.part_name = data.get("name", "")
	part.category = data.get("category", "")
	part.subcategory = data.get("subcategory", "")
	part.cost = int(data.get("cost", 0))
	part.mass_kg = float(data.get("mass_kg", 0.0))
	part.hp = int(data.get("hp", 50))
	part.drag = float(data.get("drag", 0.5))

	# Size comes in as a JSON array of 3 ints: [width, height, depth].
	var size_arr: Array = data.get("size", [1, 1, 1])
	if size_arr.size() >= 3:
		part.size = Vector3i(int(size_arr[0]), int(size_arr[1]), int(size_arr[2]))
	else:
		push_warning("[PartData] Part '%s' has invalid size array. Using (1,1,1)." % part.id)
		part.size = Vector3i(1, 1, 1)

	# Domains array -- convert to typed Array[String].
	var raw_domains: Array = data.get("domains", [])
	part.domains = [] as Array[String]
	for d in raw_domains:
		part.domains.append(str(d))

	# Mount points array.
	var raw_mounts: Array = data.get("mount_points", [])
	part.mount_points = [] as Array[String]
	for m in raw_mounts:
		part.mount_points.append(str(m))

	# Free-form stats dictionary.
	part.stats = data.get("stats", {})

	# Mesh data -- JSON key is "mesh", we store as mesh_data.
	part.mesh_data = data.get("mesh", {})

	return part


# ---------------------------------------------------------------------------
# Serialization
# ---------------------------------------------------------------------------

## Serialize this PartData back into a plain Dictionary suitable for JSON
## export or network transfer.
func to_dict() -> Dictionary:
	return {
		"id": id,
		"name": part_name,
		"category": category,
		"subcategory": subcategory,
		"cost": cost,
		"mass_kg": mass_kg,
		"hp": hp,
		"size": [size.x, size.y, size.z],
		"drag": drag,
		"domains": domains,
		"mount_points": mount_points,
		"stats": stats,
		"mesh": mesh_data,
	}


# ---------------------------------------------------------------------------
# Query helpers
# ---------------------------------------------------------------------------

## Returns true if this part is allowed in the given combat [param domain].
## If the part's [member domains] array is empty, it is considered universal
## (valid in every domain).
func is_valid_for_domain(domain: String) -> bool:
	# Empty domains list means "works everywhere".
	if domains.is_empty():
		return true
	return domain in domains


## Retrieve a specific stat value by [param key] from the [member stats]
## dictionary. Returns [param default] if the key is not found.
func get_stat(key: String, default = null):
	return stats.get(key, default)


## Returns true if this part is a control module (the "brain" of the vehicle).
## Every vehicle must have exactly one control module to function.
func is_control_module() -> bool:
	return category == "control"


## Returns the grid footprint -- synonym for [member size], provided for
## clarity at call sites.
func get_footprint() -> Vector3i:
	return size
