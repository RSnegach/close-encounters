## ArenaHazards -- Environmental hazards for combat arenas.
##
## Each combat domain has its own set of environmental dangers that affect
## all vehicles in the arena:
##
##   Ground    : No environmental hazards (buildings are just physics
##               obstacles handled by the scene geometry).
##   Air       : Altitude ceiling (too high = thin air damage) and ground
##               collision (slamming into terrain = heavy damage).
##   Water     : Shallow water / rocks near the edges (running aground).
##   Submarine : Crush depth (too deep = hull collapse) and underwater
##               cave walls.
##   Space     : Asteroid collisions and solar radiation at arena edges.
##
## ArenaHazards is created as a child of ArenaManager and checks all vehicles
## every physics frame for hazard conditions. When a hazard triggers, it
## applies damage through the DamageSystem and can optionally display a
## warning message (via signal) to the affected player.
class_name ArenaHazards
extends Node3D


# ---------------------------------------------------------------------------
# Signals
# ---------------------------------------------------------------------------

## Emitted when a vehicle enters a hazard zone. UI can display a warning.
## [param vehicle] - The affected vehicle.
## [param message] - Human-readable warning string.
signal hazard_warning(vehicle: Node, message: String)


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## Hazard boundary values for each domain. These define the danger zones.

## Air domain: maximum safe altitude in meters.
const AIR_CEILING: float = 2000.0

## Air domain: altitude above which damage starts (gradual).
const AIR_CEILING_WARN: float = 1800.0

## Air domain: minimum safe altitude before ground collision.
const AIR_FLOOR: float = 2.0

## Submarine domain: crush depth in meters (negative Y).
const SUB_CRUSH_DEPTH: float = -500.0

## Submarine domain: depth at which warnings start.
const SUB_CRUSH_WARN: float = -400.0

## Space domain: arena boundary radius. Beyond this, solar radiation damages
## the vehicle.
const SPACE_BOUNDARY_RADIUS: float = 500.0

## Space domain: radius at which warnings start.
const SPACE_BOUNDARY_WARN: float = 400.0

## Water domain: shallow water boundary (distance from arena center).
const WATER_SHALLOW_RADIUS: float = 300.0

## Damage per second for each hazard type. Tunable for balance.
const HAZARD_DPS: Dictionary = {
	"ceiling": 20,         # Air ceiling: moderate DOT.
	"ground_collision": 200, # Ground slam: very high (probably fatal).
	"crush_depth": 30,     # Sub crush: heavy pressure damage.
	"shallow_water": 15,   # Running aground.
	"space_radiation": 10, # Solar radiation: slow DOT.
	"asteroid": 50,        # Asteroid hit: sharp burst.
}


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## The combat domain this hazard manager is configured for.
var domain: String = ""

## Elapsed time since setup. Used for periodic hazard events (e.g. asteroid
## spawning).
var hazard_timer: float = 0.0

## Tracks active hazard states per vehicle to avoid spamming warnings every
## frame. Key: vehicle instance ID, value: Set of active hazard names.
var _active_hazard_states: Dictionary = {}


# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

## Configure the hazard manager for the given arena domain.
##
## [param arena_domain] - One of: "ground", "air", "water", "submarine",
##                        "space".
func setup(arena_domain: String) -> void:
	domain = arena_domain
	_init_hazards()
	print("[ArenaHazards] Initialized for '%s' domain." % domain)


## Domain-specific initialization. Creates hazard zone markers or static
## obstacles as needed.
func _init_hazards() -> void:
	match domain:
		"ground":
			# Ground arenas have no environmental hazards.
			# Buildings and terrain are just physics bodies in the scene.
			pass
		"air":
			# Air hazards are checked dynamically (altitude bounds).
			pass
		"water":
			# Water hazards are checked dynamically (boundary distance).
			pass
		"submarine":
			# Submarine hazards are checked dynamically (depth).
			pass
		"space":
			# Space hazards are checked dynamically (boundary + asteroids).
			pass


# ---------------------------------------------------------------------------
# Physics processing
# ---------------------------------------------------------------------------

## Every physics frame, check all vehicles for hazard conditions and apply
## damage as needed.
func _physics_process(delta: float) -> void:
	hazard_timer += delta

	# Find all vehicles in the match.
	var all_vehicles: Array[Node] = get_tree().get_nodes_in_group("vehicles")

	for node: Node in all_vehicles:
		if not node is RigidBody3D:
			continue
		var vehicle: Node = node as Node
		if not vehicle.is_alive:
			continue

		_check_hazards(vehicle, delta)


# ---------------------------------------------------------------------------
# Hazard checks
# ---------------------------------------------------------------------------

## Check all relevant hazards for a specific vehicle based on the domain.
##
## [param vehicle] - The vehicle to check.
## [param delta]   - Frame time in seconds (for DPS calculation).
func _check_hazards(vehicle: Node, delta: float) -> void:
	match domain:
		"air":
			_check_air_hazards(vehicle, delta)
		"water":
			_check_water_hazards(vehicle, delta)
		"submarine":
			_check_submarine_hazards(vehicle, delta)
		"space":
			_check_space_hazards(vehicle, delta)
		# "ground" has no environmental hazards.


## Air domain hazards: altitude ceiling and ground collision.
func _check_air_hazards(vehicle: Node, delta: float) -> void:
	var altitude: float = vehicle.global_position.y

	# --- Altitude ceiling ---
	# Above the warning threshold, damage scales linearly with height.
	if altitude > AIR_CEILING_WARN:
		var severity: float = (altitude - AIR_CEILING_WARN) / (AIR_CEILING - AIR_CEILING_WARN)
		severity = clampf(severity, 0.0, 2.0)  # Cap at 2x damage above ceiling.
		var dps: int = int(HAZARD_DPS["ceiling"] * severity)
		var tick_damage: int = maxi(1, int(dps * delta))

		_apply_hazard_damage(vehicle, tick_damage, "Too high! Thin atmosphere damaging vehicle.")

		# Above the hard ceiling, apply extra damage.
		if altitude > AIR_CEILING:
			_apply_hazard_damage(vehicle, tick_damage * 2, "ABOVE CEILING! Critical damage!")

	# --- Ground collision ---
	# Very close to the ground at high speed = heavy damage.
	if altitude < AIR_FLOOR:
		var speed: float = vehicle.get_speed()
		# Scale damage by speed -- a slow descent is less damaging.
		var speed_factor: float = clampf(speed / 50.0, 0.2, 3.0)
		var collision_damage: int = int(HAZARD_DPS["ground_collision"] * speed_factor)

		_apply_hazard_damage(vehicle, collision_damage, "GROUND COLLISION!")


## Water domain hazards: shallow water near arena edges.
func _check_water_hazards(vehicle: Node, delta: float) -> void:
	# Calculate horizontal distance from arena center (ignore Y).
	var horizontal_pos: Vector2 = Vector2(vehicle.global_position.x, vehicle.global_position.z)
	var dist_from_center: float = horizontal_pos.length()

	if dist_from_center > WATER_SHALLOW_RADIUS:
		var severity: float = (dist_from_center - WATER_SHALLOW_RADIUS) / 50.0
		severity = clampf(severity, 0.0, 3.0)
		var dps: int = int(HAZARD_DPS["shallow_water"] * severity)
		var tick_damage: int = maxi(1, int(dps * delta))

		_apply_hazard_damage(vehicle, tick_damage, "Shallow water! Vehicle taking damage.")


## Submarine domain hazards: crush depth.
func _check_submarine_hazards(vehicle: Node, delta: float) -> void:
	var depth: float = vehicle.global_position.y  # Negative = deeper.

	# --- Crush depth ---
	if depth < SUB_CRUSH_WARN:
		var severity: float = (SUB_CRUSH_WARN - depth) / (SUB_CRUSH_WARN - SUB_CRUSH_DEPTH)
		severity = clampf(severity, 0.0, 3.0)
		var dps: int = int(HAZARD_DPS["crush_depth"] * severity)
		var tick_damage: int = maxi(1, int(dps * delta))

		_apply_hazard_damage(vehicle, tick_damage, "CRUSH DEPTH WARNING! Hull buckling.")

		# Below crush depth: apply a pressure breach status effect (once).
		if depth < SUB_CRUSH_DEPTH:
			var vid: int = vehicle.get_instance_id()
			if not _has_active_hazard(vid, "pressure_breach"):
				_set_active_hazard(vid, "pressure_breach")
				var damage_sys: DamageSystem = _find_damage_system()
				if damage_sys != null:
					damage_sys.apply_status_effect(vehicle, "pressure_breach", 10.0)


## Space domain hazards: arena boundary radiation and asteroids.
func _check_space_hazards(vehicle: Node, delta: float) -> void:
	# --- Boundary radiation ---
	var dist_from_center: float = vehicle.global_position.length()

	if dist_from_center > SPACE_BOUNDARY_WARN:
		var severity: float = (dist_from_center - SPACE_BOUNDARY_WARN) / (SPACE_BOUNDARY_RADIUS - SPACE_BOUNDARY_WARN)
		severity = clampf(severity, 0.0, 3.0)
		var dps: int = int(HAZARD_DPS["space_radiation"] * severity)
		var tick_damage: int = maxi(1, int(dps * delta))

		_apply_hazard_damage(vehicle, tick_damage, "Solar radiation! Return to arena.")

		# Beyond the hard boundary, damage ramps dramatically.
		if dist_from_center > SPACE_BOUNDARY_RADIUS:
			_apply_hazard_damage(vehicle, tick_damage * 3, "OUTSIDE ARENA! Critical radiation!")


# ---------------------------------------------------------------------------
# Damage application
# ---------------------------------------------------------------------------

## Apply hazard damage to a random alive part on the vehicle.
##
## [param vehicle] - The vehicle taking damage.
## [param damage]  - Amount of damage to apply.
## [param msg]     - Warning message for the UI.
func _apply_hazard_damage(vehicle: Node, damage: int, msg: String) -> void:
	if vehicle == null or not vehicle.is_alive:
		return
	if damage <= 0:
		return

	# Find a random alive part to damage.
	var target_part: PartNode = _get_random_alive_part(vehicle)
	if target_part == null:
		return

	# Apply damage through the DamageSystem for consistency.
	var damage_sys: DamageSystem = _find_damage_system()
	if damage_sys != null:
		# Hazard damage bypasses armor (environment doesn't care about plates).
		damage_sys.apply_damage(target_part, damage, "environment", self)
	else:
		target_part.take_damage(damage)

	# Emit a warning (rate-limited per vehicle to avoid spam).
	var vid: int = vehicle.get_instance_id()
	if not _has_active_hazard(vid, msg):
		_set_active_hazard(vid, msg)
		hazard_warning.emit(vehicle, msg)
		print("[ArenaHazards] %s (vehicle: %s)" % [msg, vehicle.name])

		# Clear the warning state after a short delay so it can fire again.
		_clear_hazard_after_delay(vid, msg, 2.0)


# ---------------------------------------------------------------------------
# Utility
# ---------------------------------------------------------------------------

## Pick a random alive (non-destroyed) part from a vehicle.
func _get_random_alive_part(vehicle: Node) -> PartNode:
	var alive_parts: Array[PartNode] = []
	var seen: Dictionary = {}

	for cell_pos: Vector3i in vehicle.parts:
		var part: PartNode = vehicle.parts[cell_pos]
		var nid: int = part.get_instance_id()
		if seen.has(nid):
			continue
		seen[nid] = true
		if not part.is_destroyed:
			alive_parts.append(part)

	if alive_parts.is_empty():
		return null
	return alive_parts[randi() % alive_parts.size()]


## Find the DamageSystem in the scene tree.
func _find_damage_system() -> DamageSystem:
	var systems: Array[Node] = get_tree().get_nodes_in_group("damage_system")
	if not systems.is_empty():
		return systems[0] as DamageSystem
	return null


# ---------------------------------------------------------------------------
# Hazard state tracking (rate limiting)
# ---------------------------------------------------------------------------

## Check if a hazard is currently active for a vehicle.
func _has_active_hazard(vehicle_id: int, hazard_name: String) -> bool:
	if not _active_hazard_states.has(vehicle_id):
		return false
	return _active_hazard_states[vehicle_id].has(hazard_name)


## Mark a hazard as active for a vehicle.
func _set_active_hazard(vehicle_id: int, hazard_name: String) -> void:
	if not _active_hazard_states.has(vehicle_id):
		_active_hazard_states[vehicle_id] = {}
	_active_hazard_states[vehicle_id][hazard_name] = true


## Clear a hazard state after a delay. Uses a one-shot timer.
func _clear_hazard_after_delay(vehicle_id: int, hazard_name: String, delay: float) -> void:
	# Create a one-shot timer to clear the hazard state.
	var timer: Timer = Timer.new()
	timer.wait_time = delay
	timer.one_shot = true
	timer.autostart = true
	add_child(timer)

	timer.timeout.connect(func() -> void:
		if _active_hazard_states.has(vehicle_id):
			_active_hazard_states[vehicle_id].erase(hazard_name)
		timer.queue_free()
	)
