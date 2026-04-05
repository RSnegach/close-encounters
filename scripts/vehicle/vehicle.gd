## Vehicle (RigidBody3D)
##
## The core gameplay entity: a modular vehicle assembled from PartNode children,
## driven by a domain-specific PhysicsBase controller. A vehicle can be
## player-controlled, AI-controlled, or network-replicated.
##
## Lifecycle:
##   1. Instantiate a Vehicle scene / node.
##   2. Call setup_from_data() with saved build data + domain string.
##   3. The vehicle creates its PartNode children, selects the right physics
##      controller, and is ready for combat.
##   4. Every physics frame, the controller applies domain-specific forces.
##   5. When the control module is destroyed the vehicle dies.
##
## This class owns the parts dictionary but does NOT own the physics controller
## resource -- it simply holds a reference.
class_name Vehicle
extends RigidBody3D


# ---------------------------------------------------------------------------
# Signals
# ---------------------------------------------------------------------------

## Emitted when the vehicle's HP reaches zero or the control module is lost.
signal vehicle_destroyed

## Emitted when an individual part is destroyed and removed from the vehicle.
signal part_lost(part: PartNode)

## Emitted after recalculate_stats() finishes so UI / HUD can update.
signal stats_changed


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## Each grid cell is 1 meter cubed. Parts are positioned at
## grid_position * CELL_SIZE in local space.
const CELL_SIZE: float = 1.0

## Weapon cooldown tracking key stored on each weapon PartNode's metadata.
const META_COOLDOWN: String = "_fire_cooldown"


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## Maps grid positions (Vector3i) to the PartNode placed there.
## Multi-cell parts occupy multiple keys that all point to the same PartNode.
var parts: Dictionary = {}

## Domain-specific physics implementation. Assigned in _assign_physics_controller().
var physics_controller: PhysicsBase = null

## The combat domain this vehicle was built for.
## One of: "ground", "air", "water", "submarine", "space".
var domain: String = "ground"

## True when this vehicle should read local player input.
var is_player_controlled: bool = false

## True when an AI agent drives this vehicle.
var is_ai_controlled: bool = false

## Multiplayer peer ID that owns this vehicle. 1 = host / single-player.
var peer_id: int = 0

## Aggregate stats recalculated whenever parts change. --
var total_mass: float = 0.0
var total_thrust: float = 0.0
var total_drag: float = 0.0

## The cockpit / bridge / command module. If this part is destroyed the
## vehicle is considered dead.
var control_module: PartNode = null

## Whether the vehicle is still functional and participating in combat.
var is_alive: bool = true

## Cached references to weapon parts for quick iteration each frame.
var weapons: Array[PartNode] = []

## Cached references to propulsion parts (engines, wheels, thrusters, etc.).
var propulsion_parts: Array[PartNode] = []


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

## Called when the node enters the scene tree. Configures RigidBody3D gravity
## based on the current domain so the right default behaviour applies before
## the physics controller takes over.
func _ready() -> void:
	# --- Collision layers ---
	# Layer 1 = vehicles. Mask all physics layers so we collide with everything.
	collision_layer = 1
	collision_mask = 0xFFFFFFFF  # Collide with all layers.

	# --- Contact monitoring for damage detection ---
	contact_monitor = true
	max_contacts_reported = 4

	# --- Gravity per domain ---
	match domain:
		"ground":
			gravity_scale = 1.0
		"air":
			gravity_scale = 1.0
		"water":
			gravity_scale = 1.0
		"submarine":
			gravity_scale = 0.0
		"space":
			gravity_scale = 0.0
		_:
			gravity_scale = 1.0


## Called every physics tick (~60 Hz by default). Delegates force application
## to the physics controller and processes player input if applicable.
func _physics_process(delta: float) -> void:
	if not is_alive:
		return

	# Let the domain controller apply thrust, drag, lift, buoyancy, etc.
	if physics_controller != null:
		# If the player is driving, let the controller translate input to forces.
		if is_player_controlled:
			physics_controller.handle_input(self, delta)
		physics_controller.apply_forces(self, delta)

	# Fire weapons when the fire action is held.
	if is_player_controlled and Input.is_action_pressed("fire_primary"):
		fire_weapons(delta)


# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

## Build the vehicle from a saved data dictionary and the target combat domain.
##
## [param vehicle_data] is a Dictionary with the structure:
##   { "parts": [ { "id": "...", "grid_position": [x,y,z] }, ... ],
##     "domain": "ground" }
## [param target_domain] overrides the domain stored in the data dict.
##
## This method:
##   1. Creates PartNode children via PartFactory.
##   2. Positions each part on the local grid.
##   3. Assigns the physics controller for the domain.
##   4. Caches weapon / propulsion / control references.
##   5. Recalculates aggregate stats (mass, thrust, drag).
func setup_from_data(vehicle_data: Dictionary, target_domain: String) -> void:
	domain = target_domain

	# Clear any previous build.
	_clear_existing_parts()

	# Retrieve the parts list from the data.
	var parts_list: Array = vehicle_data.get("parts", [])
	if parts_list.is_empty():
		push_warning("[Vehicle] setup_from_data called with no parts.")
		return

	for part_entry: Dictionary in parts_list:
		var part_id: String = part_entry.get("id", "")
		if part_id == "":
			push_warning("[Vehicle] Part entry missing 'id'. Skipping.")
			continue

		# Look up the part definition from the registry.
		var part_data: PartData = PartRegistry.get_part(part_id)
		if part_data == null:
			push_warning("[Vehicle] PartData not found for id '%s'. Skipping." % part_id)
			continue

		# Parse the grid position array [x, y, z] -> Vector3i.
		var pos_array: Array = part_entry.get("grid_position", [0, 0, 0])
		var grid_pos := Vector3i(
			int(pos_array[0]),
			int(pos_array[1]),
			int(pos_array[2])
		)

		# Create the PartNode via the factory and add it as a child.
		var part_node: PartNode = PartFactory.create_part(part_data, grid_pos)
		if part_node == null:
			push_error("[Vehicle] PartFactory returned null for '%s'. Skipping." % part_id)
			continue

		add_child(part_node)

		# Position the part in local space based on its grid cell.
		part_node.position = Vector3(grid_pos) * CELL_SIZE

		# Restore HP if provided (e.g. from a mid-match save).
		if part_entry.has("current_hp"):
			part_node.current_hp = int(part_entry["current_hp"])

		# Register the part in the grid dictionary. Multi-cell parts occupy
		# every cell their size covers.
		_register_part_cells(part_node, grid_pos, part_data.size)

		# Connect the part's destroyed signal so we can react to losses.
		if part_node.has_signal("part_destroyed"):
			part_node.part_destroyed.connect(on_part_destroyed.bind(part_node))

		# Cache references by category for fast lookup.
		_cache_part_reference(part_node, part_data)

	# Pick the right physics implementation for this domain.
	_assign_physics_controller(domain)

	# Compute aggregate mass / thrust / drag.
	recalculate_stats()

	# Set up the RigidBody3D mass from parts.
	mass = maxf(total_mass, 1.0)

	print("[Vehicle] Setup complete. %d part(s), domain='%s', mass=%.1f kg." % [
		_get_unique_parts().size(), domain, total_mass
	])


## Attach a third-person follow camera to this vehicle. Call after
## setup_from_data() and only for the local player's vehicle.
func attach_follow_camera() -> void:
	# Pivot follows the vehicle (child node, so it moves with us).
	var pivot: Node3D = Node3D.new()
	pivot.name = "CameraPivot"
	add_child(pivot)

	var cam: Camera3D = Camera3D.new()
	cam.name = "FollowCamera"
	# Offset: behind and above the vehicle, looking down at it.
	cam.position = Vector3(0, 6, 12)
	pivot.add_child(cam)
	cam.look_at(global_position, Vector3.UP)
	cam.current = true

	print("[Vehicle] Follow camera attached.")


# ---------------------------------------------------------------------------
# Physics controller assignment
# ---------------------------------------------------------------------------

## Instantiate the correct PhysicsBase subclass for the given [param p_domain].
## Called once during setup_from_data().
func _assign_physics_controller(p_domain: String) -> void:
	match p_domain:
		"ground":
			physics_controller = GroundPhysics.new()
		"air":
			physics_controller = AirPhysics.new()
		"water":
			physics_controller = WaterPhysics.new()
		"submarine":
			physics_controller = SubmarinePhysics.new()
		"space":
			physics_controller = RocketPhysics.new()
			# Rocket physics needs to know the fuel load.
			(physics_controller as RocketPhysics).setup_fuel(self)
		_:
			push_error("[Vehicle] No physics controller for domain '%s'." % p_domain)
			physics_controller = null


# ---------------------------------------------------------------------------
# Stats
# ---------------------------------------------------------------------------

## Iterate all unique parts and recompute total_mass, total_thrust, and
## total_drag. Emits stats_changed when done.
func recalculate_stats() -> void:
	total_mass = 0.0
	total_thrust = 0.0
	total_drag = 0.0

	for part_node: PartNode in _get_unique_parts():
		if part_node.part_data == null:
			continue
		total_mass += part_node.part_data.mass_kg
		total_drag += part_node.part_data.drag

		# Propulsion parts store their thrust in the stats dictionary.
		if part_node.part_data.stats.has("thrust"):
			total_thrust += float(part_node.part_data.stats["thrust"])

	# Update the RigidBody3D mass to match so Godot's physics solver is accurate.
	mass = maxf(total_mass, 1.0)

	stats_changed.emit()


## Return the vehicle's current speed in m/s (magnitude of velocity vector).
func get_speed() -> float:
	return linear_velocity.length()


## Return the vehicle's forward direction as a unit vector.
## By convention, negative-Z is forward in Godot.
func get_forward_direction() -> Vector3:
	return -global_transform.basis.z


## Compute a centre-of-mass offset from part positions and masses.
## Useful for flight stability and HUD indicators.
func get_center_of_mass_offset() -> Vector3:
	var weighted_sum := Vector3.ZERO
	var mass_sum: float = 0.0

	for part_node: PartNode in _get_unique_parts():
		if part_node.part_data == null:
			continue
		var m: float = part_node.part_data.mass_kg
		weighted_sum += part_node.position * m
		mass_sum += m

	if mass_sum <= 0.0:
		return Vector3.ZERO
	return weighted_sum / mass_sum


# ---------------------------------------------------------------------------
# Input helpers
# ---------------------------------------------------------------------------

## Sample the current frame's input actions and return a dictionary describing
## the player's intent. Physics controllers consume this dict.
##
## Returned keys:
##   forward  : float  -1 to 1 (W / S)
##   strafe   : float  -1 to 1 (A / D)
##   pitch    : float  -1 to 1
##   yaw      : float  -1 to 1
##   roll     : float  -1 to 1
##   throttle_up   : bool
##   throttle_down : bool
##   fire     : bool
##   dive     : bool
##   surface  : bool
func get_input_vector() -> Dictionary:
	return {
		"forward": Input.get_axis("move_backward", "move_forward"),
		"strafe": Input.get_axis("move_left", "move_right"),
		"pitch": Input.get_axis("pitch_down", "pitch_up"),
		"yaw": Input.get_axis("move_left", "move_right"),
		"roll": Input.get_axis("roll_left", "roll_right"),
		"throttle_up": Input.is_action_pressed("throttle_up"),
		"throttle_down": Input.is_action_pressed("throttle_down"),
		"fire": Input.is_action_pressed("fire_primary"),
		"dive": Input.is_action_pressed("dive"),
		"surface": Input.is_action_pressed("surface"),
	}


# ---------------------------------------------------------------------------
# Weapons
# ---------------------------------------------------------------------------

## Iterate cached weapon parts and fire any whose cooldown has elapsed.
## Each weapon stores a "_fire_cooldown" metadata float that counts down.
func fire_weapons(delta: float) -> void:
	for weapon: PartNode in weapons:
		if not weapon.is_functional():
			continue

		# Read or initialize the per-weapon cooldown timer.
		var cooldown: float = weapon.get_meta(META_COOLDOWN, 0.0)
		cooldown -= delta

		if cooldown <= 0.0:
			# Determine fire rate from the weapon's stats (shots per second).
			var fire_rate: float = float(weapon.part_data.stats.get("fire_rate", 1.0))
			cooldown = 1.0 / maxf(fire_rate, 0.1)

			_spawn_projectile(weapon)

		weapon.set_meta(META_COOLDOWN, cooldown)


## Create and launch a projectile from the given weapon part.
## The projectile scene is determined by the weapon's stats.projectile field.
func _spawn_projectile(weapon: PartNode) -> void:
	var projectile_id: String = str(weapon.part_data.stats.get("projectile", "bullet_default"))

	# TODO: ProjectileFactory.create(projectile_id, weapon.global_position,
	#        get_forward_direction(), peer_id)
	# For now, print a placeholder message.
	print("[Vehicle] FIRE projectile '%s' from weapon '%s'." % [
		projectile_id, weapon.part_data.part_name
	])


# ---------------------------------------------------------------------------
# Damage / destruction
# ---------------------------------------------------------------------------

## Called when a child PartNode's HP reaches zero and it signals destruction.
## Removes the part from caches, recalculates stats, and checks for vehicle
## death or chain-reaction explosions.
func on_part_destroyed(part: PartNode) -> void:
	if part == null:
		return

	# Emit the loss signal so HUD / combat log can react.
	part_lost.emit(part)

	# Remove from weapon / propulsion caches.
	weapons.erase(part)
	propulsion_parts.erase(part)

	# Remove all grid cells that pointed to this part.
	var keys_to_remove: Array[Vector3i] = []
	for cell_pos: Vector3i in parts:
		if parts[cell_pos] == part:
			keys_to_remove.append(cell_pos)
	for key: Vector3i in keys_to_remove:
		parts.erase(key)

	# Check for chain-reaction: ammo racks and fuel tanks explode.
	if part.part_data != null:
		var stats: Dictionary = part.part_data.stats
		if stats.has("ammo_rack") or stats.has("fuel_tank"):
			_trigger_explosion(part)

	# Recalculate aggregate stats after the loss.
	recalculate_stats()

	# If the control module was destroyed, the vehicle dies.
	if part == control_module:
		print("[Vehicle] Control module destroyed -- vehicle is dead.")
		die()


## Trigger an explosion at a part's location. Damages adjacent parts.
## Used for chain-reaction mechanics (ammo / fuel).
func _trigger_explosion(source_part: PartNode) -> void:
	var explosion_damage: float = float(source_part.part_data.stats.get("explosion_damage", 50.0))
	var explosion_pos: Vector3 = source_part.position

	print("[Vehicle] Explosion at %s dealing %.0f damage!" % [explosion_pos, explosion_damage])

	# Damage all parts within 2 cells of the explosion source.
	for part_node: PartNode in _get_unique_parts():
		if part_node == source_part:
			continue
		var dist: float = part_node.position.distance_to(explosion_pos)
		if dist <= 2.0 * CELL_SIZE:
			# Damage falls off linearly with distance.
			var falloff: float = 1.0 - (dist / (2.0 * CELL_SIZE))
			var dmg: float = explosion_damage * falloff
			part_node.take_damage(dmg)


## Kill the vehicle. Freezes physics and emits vehicle_destroyed.
func die() -> void:
	is_alive = false
	# Freeze the RigidBody3D so it stops simulating.
	freeze = true
	vehicle_destroyed.emit()
	print("[Vehicle] Vehicle destroyed.")


# ---------------------------------------------------------------------------
# Serialization
# ---------------------------------------------------------------------------

## Serialize the vehicle's current state to a dictionary suitable for saving
## or sending over the network.
func serialize() -> Dictionary:
	var parts_array: Array = []

	# Use a set to avoid serializing multi-cell parts more than once.
	var seen: Dictionary = {}
	for cell_pos: Vector3i in parts:
		var part_node: PartNode = parts[cell_pos]
		var node_id: int = part_node.get_instance_id()
		if seen.has(node_id):
			continue
		seen[node_id] = true

		parts_array.append({
			"id": part_node.part_data.id,
			"grid_position": [
				part_node.grid_position.x,
				part_node.grid_position.y,
				part_node.grid_position.z,
			],
			"current_hp": part_node.current_hp,
		})

	return {
		"parts": parts_array,
		"domain": domain,
	}


## Return only the weapon parts that are still functional (HP > 0).
func get_weapon_parts() -> Array[PartNode]:
	return weapons.filter(func(p: PartNode) -> bool: return p.is_functional())


# ---------------------------------------------------------------------------
# Private helpers
# ---------------------------------------------------------------------------

## Register a part in the grid dictionary for every cell its size covers.
## A part at grid_pos with size (2,1,3) occupies 6 cells.
func _register_part_cells(part_node: PartNode, grid_pos: Vector3i, part_size: Vector3i) -> void:
	for x: int in range(part_size.x):
		for y: int in range(part_size.y):
			for z: int in range(part_size.z):
				var cell: Vector3i = grid_pos + Vector3i(x, y, z)
				parts[cell] = part_node


## Cache a part reference in the appropriate fast-lookup array based on its
## category.
func _cache_part_reference(part_node: PartNode, part_data: PartData) -> void:
	match part_data.category:
		"weapon":
			weapons.append(part_node)
			# Initialize the fire cooldown metadata.
			part_node.set_meta(META_COOLDOWN, 0.0)
		"propulsion":
			propulsion_parts.append(part_node)
		"control":
			# The first control part found becomes THE control module.
			if control_module == null:
				control_module = part_node


## Return an array of unique PartNode references (no duplicates from
## multi-cell parts).
func _get_unique_parts() -> Array[PartNode]:
	var unique: Array[PartNode] = []
	var seen_ids: Dictionary = {}
	for cell_pos: Vector3i in parts:
		var node: PartNode = parts[cell_pos]
		var nid: int = node.get_instance_id()
		if not seen_ids.has(nid):
			seen_ids[nid] = true
			unique.append(node)
	return unique


## Remove and free all existing PartNode children and clear caches.
func _clear_existing_parts() -> void:
	for part_node: PartNode in _get_unique_parts():
		part_node.queue_free()
	parts.clear()
	weapons.clear()
	propulsion_parts.clear()
	control_module = null
