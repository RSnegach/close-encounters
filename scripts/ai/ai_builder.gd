## AIBuilder -- Generates vehicle designs for AI opponents.
##
## When the player starts a solo match, the game needs to create an AI
## opponent with a reasonable vehicle. This class generates those designs
## procedurally based on the combat domain, budget, and difficulty level.
##
## How it works:
##   1. Select a template for the domain + difficulty combination.
##      Templates are priority-ordered lists of parts.
##   2. Walk the template in priority order, placing parts on a 3D grid.
##      Higher-priority parts (control module, engines) are placed first.
##   3. Stop when the budget runs out or all template parts are placed.
##   4. If budget remains, add random variation (extra weapons, armor).
##   5. Return a vehicle data dictionary in the same format as
##      Vehicle.setup_from_data() expects.
##
## This is a RefCounted class (no scene node needed). All methods are static
## so you can call AIBuilder.build_vehicle(...) directly.
class_name AIBuilder
extends RefCounted


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## The grid dimensions for AI vehicle placement. 7 wide, 5 tall, 7 deep.
## The control module goes at the center: (3, 2, 3).
const GRID_WIDTH: int = 7
const GRID_HEIGHT: int = 5
const GRID_DEPTH: int = 7

## Center of the grid. The control module always goes here.
const GRID_CENTER: Vector3i = Vector3i(3, 2, 3)

## Category placement priorities. Lower = placed first.
## This determines the general layout: control at center, structure outward,
## propulsion at the back/bottom, weapons on top/front, defense outside.
const CATEGORY_PRIORITY: Dictionary = {
	"control": 0,
	"chassis": 1,
	"propulsion": 2,
	"weapon": 3,
	"defense": 4,
	"utility": 5,
}


# ---------------------------------------------------------------------------
# Public static methods
# ---------------------------------------------------------------------------

## Generate a complete AI vehicle design.
##
## [param domain]     - Combat domain: "ground", "air", "water", "submarine",
##                      or "space".
## [param budget]     - Dollar budget to spend. 0 = unlimited.
## [param difficulty] - AI difficulty: "easy", "medium", or "hard".
##                      Harder AI gets better part selections.
##
## Returns a Dictionary in the same format as Node.setup_from_data():
##   { "parts": [ { "id": "...", "grid_position": [x, y, z] }, ... ],
##     "domain": "ground" }
static func build_vehicle(domain: String, budget: int, difficulty: String) -> Dictionary:
	# --- Step 1: Get the template for this domain + difficulty ---
	var template: Array = _get_template(domain, difficulty)
	if template.is_empty():
		push_warning("[AIBuilder] No template for domain='%s', difficulty='%s'." % [domain, difficulty])
		return {"parts": [], "domain": domain}

	# --- Step 2: Resolve template entries to actual part IDs ---
	# Each template entry has: {part_id, quantity, priority}
	# Sort by priority so important parts are placed first.
	template.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		return a.get("priority", 99) < b.get("priority", 99)
	)

	# --- Step 3: Place parts on the grid within budget ---
	var placed_parts: Array = _place_parts_on_grid(template, budget)

	# --- Step 4: Add random variation with leftover budget ---
	var spent: int = _calculate_cost(placed_parts)
	var remaining: int = budget - spent if budget > 0 else 0
	if remaining > 100:
		placed_parts = _add_random_variation(placed_parts, remaining, domain)

	print("[AIBuilder] Built %s vehicle (%s): %d parts, $%d spent." % [
		domain, difficulty, placed_parts.size(), _calculate_cost(placed_parts)
	])

	return {
		"parts": placed_parts,
		"domain": domain,
	}


# ---------------------------------------------------------------------------
# Template definitions
# ---------------------------------------------------------------------------

## Return the part template for a given domain and difficulty.
##
## Each entry is a Dictionary with:
##   "part_id"  : String  - The ID of the part in PartRegistry.
##   "quantity" : int     - How many of this part to try to place.
##   "priority" : int     - Placement priority (lower = placed first).
##
## Templates are deliberately overbuilt -- if the budget runs out, the
## lower-priority parts simply don't get placed.
static func _get_template(domain: String, difficulty: String) -> Array:
	var key: String = domain + "_" + difficulty

	# --- GROUND ---
	var templates: Dictionary = {
		"ground_easy": [
			{"part_id": "cockpit", "quantity": 1, "priority": 0},
			{"part_id": "light_frame", "quantity": 4, "priority": 1},
			{"part_id": "small_wheel", "quantity": 4, "priority": 2},
			{"part_id": "machine_gun", "quantity": 1, "priority": 3},
		],
		"ground_medium": [
			{"part_id": "armored_cockpit", "quantity": 1, "priority": 0},
			{"part_id": "medium_frame", "quantity": 4, "priority": 1},
			{"part_id": "large_wheel", "quantity": 2, "priority": 2},
			{"part_id": "tank_tracks", "quantity": 2, "priority": 2},
			{"part_id": "autocannon", "quantity": 1, "priority": 3},
			{"part_id": "machine_gun", "quantity": 1, "priority": 3},
			{"part_id": "light_armor", "quantity": 1, "priority": 4},
		],
		"ground_hard": [
			{"part_id": "armored_cockpit", "quantity": 1, "priority": 0},
			{"part_id": "heavy_frame", "quantity": 6, "priority": 1},
			{"part_id": "tank_tracks", "quantity": 2, "priority": 2},
			{"part_id": "heavy_cannon", "quantity": 1, "priority": 3},
			{"part_id": "missile_launcher", "quantity": 1, "priority": 3},
			{"part_id": "heavy_armor", "quantity": 2, "priority": 4},
			{"part_id": "radar", "quantity": 1, "priority": 5},
			{"part_id": "repair_kit", "quantity": 1, "priority": 5},
		],

		# --- AIR ---
		"air_easy": [
			{"part_id": "cockpit", "quantity": 1, "priority": 0},
			{"part_id": "aerodynamic_frame", "quantity": 3, "priority": 1},
			{"part_id": "propeller_air", "quantity": 1, "priority": 2},
			{"part_id": "machine_gun", "quantity": 1, "priority": 3},
		],
		"air_medium": [
			{"part_id": "cockpit", "quantity": 1, "priority": 0},
			{"part_id": "aerodynamic_frame", "quantity": 4, "priority": 1},
			{"part_id": "jet_engine", "quantity": 1, "priority": 2},
			{"part_id": "autocannon", "quantity": 1, "priority": 3},
			{"part_id": "missile_launcher", "quantity": 1, "priority": 3},
			{"part_id": "flare_dispenser", "quantity": 1, "priority": 5},
		],
		"air_hard": [
			{"part_id": "armored_cockpit", "quantity": 1, "priority": 0},
			{"part_id": "aerodynamic_frame", "quantity": 6, "priority": 1},
			{"part_id": "jet_engine", "quantity": 1, "priority": 2},
			{"part_id": "afterburner", "quantity": 1, "priority": 2},
			{"part_id": "railgun", "quantity": 1, "priority": 3},
			{"part_id": "missile_launcher", "quantity": 1, "priority": 3},
			{"part_id": "flare_dispenser", "quantity": 1, "priority": 5},
			{"part_id": "ecm_jammer", "quantity": 1, "priority": 5},
		],

		# --- WATER ---
		"water_easy": [
			{"part_id": "bridge", "quantity": 1, "priority": 0},
			{"part_id": "hull_plate", "quantity": 4, "priority": 1},
			{"part_id": "keel", "quantity": 1, "priority": 1},
			{"part_id": "marine_propeller", "quantity": 1, "priority": 2},
			{"part_id": "machine_gun", "quantity": 1, "priority": 3},
			{"part_id": "rudder", "quantity": 1, "priority": 2},
		],
		"water_medium": [
			{"part_id": "bridge", "quantity": 1, "priority": 0},
			{"part_id": "hull_plate", "quantity": 6, "priority": 1},
			{"part_id": "keel", "quantity": 1, "priority": 1},
			{"part_id": "marine_propeller", "quantity": 1, "priority": 2},
			{"part_id": "autocannon", "quantity": 1, "priority": 3},
			{"part_id": "torpedo_tube", "quantity": 1, "priority": 3},
			{"part_id": "rudder", "quantity": 1, "priority": 2},
			{"part_id": "light_armor", "quantity": 1, "priority": 4},
		],
		"water_hard": [
			{"part_id": "bridge", "quantity": 1, "priority": 0},
			{"part_id": "hull_plate", "quantity": 8, "priority": 1},
			{"part_id": "keel", "quantity": 2, "priority": 1},
			{"part_id": "marine_propeller", "quantity": 2, "priority": 2},
			{"part_id": "heavy_cannon", "quantity": 1, "priority": 3},
			{"part_id": "torpedo_tube", "quantity": 2, "priority": 3},
			{"part_id": "rudder", "quantity": 1, "priority": 2},
			{"part_id": "heavy_armor", "quantity": 2, "priority": 4},
			{"part_id": "sonar", "quantity": 1, "priority": 5},
			{"part_id": "point_defense", "quantity": 1, "priority": 5},
		],

		# --- SUBMARINE ---
		"submarine_easy": [
			{"part_id": "conning_tower", "quantity": 1, "priority": 0},
			{"part_id": "pressurized_hull", "quantity": 3, "priority": 1},
			{"part_id": "sub_propeller", "quantity": 1, "priority": 2},
			{"part_id": "torpedo_tube", "quantity": 1, "priority": 3},
			{"part_id": "ballast_tank", "quantity": 1, "priority": 2},
			{"part_id": "rudder", "quantity": 1, "priority": 2},
		],
		"submarine_medium": [
			{"part_id": "conning_tower", "quantity": 1, "priority": 0},
			{"part_id": "pressurized_hull", "quantity": 5, "priority": 1},
			{"part_id": "sub_propeller", "quantity": 1, "priority": 2},
			{"part_id": "torpedo_tube", "quantity": 2, "priority": 3},
			{"part_id": "ballast_tank", "quantity": 2, "priority": 2},
			{"part_id": "rudder", "quantity": 1, "priority": 2},
			{"part_id": "sonar", "quantity": 1, "priority": 5},
			{"part_id": "light_armor", "quantity": 1, "priority": 4},
		],
		"submarine_hard": [
			{"part_id": "conning_tower", "quantity": 1, "priority": 0},
			{"part_id": "pressurized_hull", "quantity": 7, "priority": 1},
			{"part_id": "sub_propeller", "quantity": 2, "priority": 2},
			{"part_id": "torpedo_tube", "quantity": 2, "priority": 3},
			{"part_id": "mine_layer", "quantity": 1, "priority": 3},
			{"part_id": "ballast_tank", "quantity": 3, "priority": 2},
			{"part_id": "rudder", "quantity": 1, "priority": 2},
			{"part_id": "sonar", "quantity": 1, "priority": 5},
			{"part_id": "heavy_armor", "quantity": 2, "priority": 4},
			{"part_id": "sonar_decoy", "quantity": 1, "priority": 5},
		],

		# --- SPACE ---
		"space_easy": [
			{"part_id": "guidance_computer", "quantity": 1, "priority": 0},
			{"part_id": "rocket_body", "quantity": 3, "priority": 1},
			{"part_id": "rocket_motor", "quantity": 1, "priority": 2},
			{"part_id": "fuel_tank", "quantity": 1, "priority": 1},
			{"part_id": "machine_gun", "quantity": 1, "priority": 3},
		],
		"space_medium": [
			{"part_id": "guidance_computer", "quantity": 1, "priority": 0},
			{"part_id": "rocket_body", "quantity": 4, "priority": 1},
			{"part_id": "rocket_motor", "quantity": 1, "priority": 2},
			{"part_id": "solid_booster", "quantity": 1, "priority": 2},
			{"part_id": "fuel_tank", "quantity": 2, "priority": 1},
			{"part_id": "missile_launcher", "quantity": 1, "priority": 3},
			{"part_id": "rcs_thruster", "quantity": 2, "priority": 2},
		],
		"space_hard": [
			{"part_id": "guidance_computer", "quantity": 1, "priority": 0},
			{"part_id": "rocket_body", "quantity": 6, "priority": 1},
			{"part_id": "rocket_motor", "quantity": 2, "priority": 2},
			{"part_id": "solid_booster", "quantity": 1, "priority": 2},
			{"part_id": "fuel_tank", "quantity": 3, "priority": 1},
			{"part_id": "railgun", "quantity": 1, "priority": 3},
			{"part_id": "missile_launcher", "quantity": 1, "priority": 3},
			{"part_id": "rcs_thruster", "quantity": 4, "priority": 2},
			{"part_id": "shield_generator", "quantity": 1, "priority": 4},
			{"part_id": "booster_fuel", "quantity": 1, "priority": 5},
		],
	}

	if templates.has(key):
		return templates[key]

	push_warning("[AIBuilder] No template for key '%s'." % key)
	return []


# ---------------------------------------------------------------------------
# Grid placement
# ---------------------------------------------------------------------------

## Place parts from the template onto the grid within the given budget.
##
## Algorithm:
##   1. Control module goes at GRID_CENTER.
##   2. Structural parts (chassis) expand outward from the center.
##   3. Propulsion goes at the bottom or back of the vehicle.
##   4. Weapons go at the top or front.
##   5. Defense (armor) wraps the exterior.
##   6. Utility fills remaining interior spaces.
##
## [param template_parts] - Priority-sorted Array of template entries.
## [param budget]         - Dollar budget (0 = unlimited).
##
## Returns Array of {id: String, grid_position: [int, int, int]} dicts.
static func _place_parts_on_grid(template_parts: Array, budget: int) -> Array:
	# Track which grid cells are occupied.
	var occupied: Dictionary = {}  # Vector3i -> true
	var result: Array = []
	var spent: int = 0

	for entry: Dictionary in template_parts:
		var part_id: String = entry.get("part_id", "")
		var quantity: int = entry.get("quantity", 1)

		# Look up the part definition to get its cost and category.
		var part_data: PartData = PartRegistry.get_part(part_id)
		if part_data == null:
			push_warning("[AIBuilder] Part '%s' not found in registry. Skipping." % part_id)
			continue

		for i: int in range(quantity):
			# Budget check (0 = unlimited).
			if budget > 0 and spent + part_data.cost > budget:
				# Can't afford any more of this part. Move to the next template
				# entry (which might be cheaper).
				break

			# Find a valid grid position for this part based on its category.
			var pos: Vector3i = _find_placement_position(
				part_data, occupied, result.is_empty()
			)

			if pos == Vector3i(-1, -1, -1):
				# No valid position found -- grid is full or no suitable spot.
				break

			# Place the part.
			# Mark all cells the part occupies as taken.
			for x: int in range(part_data.size.x):
				for y: int in range(part_data.size.y):
					for z: int in range(part_data.size.z):
						occupied[pos + Vector3i(x, y, z)] = true

			result.append({
				"id": part_id,
				"grid_position": [pos.x, pos.y, pos.z],
			})
			spent += part_data.cost

	return result


## Find a valid grid position for a part based on its category.
##
## The placement strategy depends on the part's role:
##   - Control: always at GRID_CENTER.
##   - Chassis/structure: expand outward from the center.
##   - Propulsion: bottom row or back face of the vehicle.
##   - Weapons: top row or front face.
##   - Defense: exterior cells (edges of the occupied volume).
##   - Utility: any remaining interior cell.
##
## [param part_data]  - The part definition (for category and size).
## [param occupied]   - Dictionary of currently occupied cells.
## [param is_first]   - True if this is the first part (always goes to center).
##
## Returns the Vector3i grid position, or (-1,-1,-1) if no spot is found.
static func _find_placement_position(
	part_data: PartData,
	occupied: Dictionary,
	is_first: bool
) -> Vector3i:
	# --- First part always goes to the center ---
	if is_first:
		if _can_place(GRID_CENTER, part_data.size, occupied):
			return GRID_CENTER

	# --- Category-based candidate generation ---
	var candidates: Array[Vector3i] = []

	match part_data.category:
		"control":
			# Control module near center.
			candidates = _get_candidates_near(GRID_CENTER, 2, part_data.size, occupied)
		"chassis":
			# Structural parts expand outward from whatever is already placed.
			candidates = _get_adjacent_candidates(occupied, part_data.size)
		"propulsion":
			# Propulsion at the bottom (low Y) or back (high Z) of the build.
			candidates = _get_candidates_in_region(
				Vector3i(0, 0, 4), Vector3i(GRID_WIDTH, 2, GRID_DEPTH),
				part_data.size, occupied
			)
			# Also try the back face.
			candidates.append_array(_get_candidates_in_region(
				Vector3i(0, 0, 5), Vector3i(GRID_WIDTH, GRID_HEIGHT, GRID_DEPTH),
				part_data.size, occupied
			))
		"weapon":
			# Weapons on top (high Y) or front (low Z).
			candidates = _get_candidates_in_region(
				Vector3i(0, 3, 0), Vector3i(GRID_WIDTH, GRID_HEIGHT, 4),
				part_data.size, occupied
			)
			candidates.append_array(_get_candidates_in_region(
				Vector3i(0, 0, 0), Vector3i(GRID_WIDTH, GRID_HEIGHT, 2),
				part_data.size, occupied
			))
		"defense":
			# Armor on the exterior -- cells adjacent to occupied but not
			# occupied themselves.
			candidates = _get_adjacent_candidates(occupied, part_data.size)
		"utility", _:
			# Utility parts fill any available space near the center.
			candidates = _get_candidates_near(GRID_CENTER, 4, part_data.size, occupied)

	# If category-specific placement failed, try anywhere on the grid.
	if candidates.is_empty():
		candidates = _get_adjacent_candidates(occupied, part_data.size)

	# If still empty (grid truly full), try any open cell.
	if candidates.is_empty():
		for x: int in range(GRID_WIDTH):
			for y: int in range(GRID_HEIGHT):
				for z: int in range(GRID_DEPTH):
					var pos: Vector3i = Vector3i(x, y, z)
					if _can_place(pos, part_data.size, occupied):
						candidates.append(pos)

	if candidates.is_empty():
		return Vector3i(-1, -1, -1)

	# Pick the candidate closest to the center (tightest build).
	candidates.sort_custom(func(a: Vector3i, b: Vector3i) -> bool:
		var da: float = Vector3(a).distance_to(Vector3(GRID_CENTER))
		var db: float = Vector3(b).distance_to(Vector3(GRID_CENTER))
		return da < db
	)

	return candidates[0]


## Check if a part of the given size can be placed at the given position
## without overlapping existing parts or exceeding grid bounds.
static func _can_place(pos: Vector3i, part_size: Vector3i, occupied: Dictionary) -> bool:
	for x: int in range(part_size.x):
		for y: int in range(part_size.y):
			for z: int in range(part_size.z):
				var cell: Vector3i = pos + Vector3i(x, y, z)
				# Bounds check.
				if cell.x < 0 or cell.x >= GRID_WIDTH:
					return false
				if cell.y < 0 or cell.y >= GRID_HEIGHT:
					return false
				if cell.z < 0 or cell.z >= GRID_DEPTH:
					return false
				# Overlap check.
				if occupied.has(cell):
					return false
	return true


## Get candidate positions that are adjacent to already-occupied cells.
## This ensures parts are connected to the existing structure.
static func _get_adjacent_candidates(
	occupied: Dictionary,
	part_size: Vector3i
) -> Array[Vector3i]:
	var candidates: Array[Vector3i] = []
	var checked: Dictionary = {}

	var offsets: Array[Vector3i] = [
		Vector3i(1, 0, 0), Vector3i(-1, 0, 0),
		Vector3i(0, 1, 0), Vector3i(0, -1, 0),
		Vector3i(0, 0, 1), Vector3i(0, 0, -1),
	]

	for cell: Vector3i in occupied:
		for offset: Vector3i in offsets:
			var candidate: Vector3i = cell + offset
			if checked.has(candidate):
				continue
			checked[candidate] = true

			if not occupied.has(candidate) and _can_place(candidate, part_size, occupied):
				candidates.append(candidate)

	return candidates


## Get candidate positions near a reference point within a given radius.
static func _get_candidates_near(
	center: Vector3i,
	radius: int,
	part_size: Vector3i,
	occupied: Dictionary
) -> Array[Vector3i]:
	var candidates: Array[Vector3i] = []

	for x: int in range(center.x - radius, center.x + radius + 1):
		for y: int in range(center.y - radius, center.y + radius + 1):
			for z: int in range(center.z - radius, center.z + radius + 1):
				var pos: Vector3i = Vector3i(x, y, z)
				if _can_place(pos, part_size, occupied):
					candidates.append(pos)

	return candidates


## Get candidate positions within a rectangular region of the grid.
static func _get_candidates_in_region(
	region_min: Vector3i,
	region_max: Vector3i,
	part_size: Vector3i,
	occupied: Dictionary
) -> Array[Vector3i]:
	var candidates: Array[Vector3i] = []

	# Clamp region to grid bounds.
	var x_start: int = maxi(region_min.x, 0)
	var y_start: int = maxi(region_min.y, 0)
	var z_start: int = maxi(region_min.z, 0)
	var x_end: int = mini(region_max.x, GRID_WIDTH)
	var y_end: int = mini(region_max.y, GRID_HEIGHT)
	var z_end: int = mini(region_max.z, GRID_DEPTH)

	for x: int in range(x_start, x_end):
		for y: int in range(y_start, y_end):
			for z: int in range(z_start, z_end):
				var pos: Vector3i = Vector3i(x, y, z)
				if _can_place(pos, part_size, occupied):
					candidates.append(pos)

	return candidates


# ---------------------------------------------------------------------------
# Random variation
# ---------------------------------------------------------------------------

## Add random extra parts if budget remains after the template is exhausted.
##
## This makes AI vehicles slightly different each time, even at the same
## difficulty level. Extra parts are biased toward weapons and armor.
##
## [param parts]            - Already-placed parts array.
## [param budget_remaining] - Dollars left to spend.
## [param domain]           - Combat domain (to filter part availability).
##
## Returns the modified parts array.
static func _add_random_variation(parts: Array, budget_remaining: int, domain: String) -> Array:
	# Get all parts available for this domain.
	var available: Array[PartData] = PartRegistry.get_parts_for_domain(domain)
	if available.is_empty():
		return parts

	# Filter to parts we can afford.
	var affordable: Array[PartData] = []
	for part: PartData in available:
		if part.cost <= budget_remaining and part.cost > 0:
			affordable.append(part)

	if affordable.is_empty():
		return parts

	# Bias toward useful categories: weapons and defense.
	var biased_pool: Array[PartData] = []
	for part: PartData in affordable:
		var weight: int = 1
		if part.category == "weapon":
			weight = 3  # Weapons are 3x more likely to be picked.
		elif part.category == "defense":
			weight = 2  # Armor is 2x more likely.
		for w: int in range(weight):
			biased_pool.append(part)

	if biased_pool.is_empty():
		return parts

	# Rebuild the occupied grid from existing parts.
	var occupied: Dictionary = {}
	for entry: Dictionary in parts:
		var pos_arr: Array = entry.get("grid_position", [0, 0, 0])
		var pos: Vector3i = Vector3i(int(pos_arr[0]), int(pos_arr[1]), int(pos_arr[2]))
		var part_data: PartData = PartRegistry.get_part(entry.get("id", ""))
		if part_data != null:
			for x: int in range(part_data.size.x):
				for y: int in range(part_data.size.y):
					for z: int in range(part_data.size.z):
						occupied[pos + Vector3i(x, y, z)] = true

	# Try to add up to 3 random parts.
	var attempts: int = 0
	var max_attempts: int = 10  # Prevent infinite loops if grid is full.
	var added: int = 0
	var remaining: int = budget_remaining

	while added < 3 and attempts < max_attempts and remaining > 0:
		attempts += 1

		# Pick a random part from the biased pool.
		var random_part: PartData = biased_pool[randi() % biased_pool.size()]
		if random_part.cost > remaining:
			continue

		# Try to find a placement position.
		var pos: Vector3i = _find_placement_position(random_part, occupied, false)
		if pos == Vector3i(-1, -1, -1):
			continue

		# Place it.
		for x: int in range(random_part.size.x):
			for y: int in range(random_part.size.y):
				for z: int in range(random_part.size.z):
					occupied[pos + Vector3i(x, y, z)] = true

		parts.append({
			"id": random_part.id,
			"grid_position": [pos.x, pos.y, pos.z],
		})
		remaining -= random_part.cost
		added += 1

	return parts


# ---------------------------------------------------------------------------
# Cost calculation
# ---------------------------------------------------------------------------

## Sum the total dollar cost of all parts in the given array.
static func _calculate_cost(parts: Array) -> int:
	var total: int = 0
	for entry: Dictionary in parts:
		var part_data: PartData = PartRegistry.get_part(entry.get("id", ""))
		if part_data != null:
			total += part_data.cost
	return total
