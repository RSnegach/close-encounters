## Projectile -- Base projectile that flies forward, detects hits, and deals damage.
##
## Supports two main modes:
##   - "ballistic" : Moves through the world every physics frame. Collision is
##                   detected via a RayCast3D that looks ahead one frame's
##                   worth of travel. This is the default for bullets and shells.
##   - "hitscan"   : Instantly traces a ray from the muzzle to max range, hits
##                   the first thing in line, and self-destructs. Used for
##                   laser-type weapons.
##
## Subclasses (GuidedMissile, Torpedo) extend this to add guidance and
## special warhead logic.
##
## Visual: A small elongated box with an emissive material (glowing trail
## effect). No external mesh assets needed -- everything is generated in code.
class_name Projectile
extends Node3D


# ---------------------------------------------------------------------------
# Signals
# ---------------------------------------------------------------------------

## Emitted when the projectile hits something (part, terrain, etc.).
## [param target]   - The Node that was hit (could be PartNode, StaticBody3D, etc.).
## [param position] - The world-space point of impact.
signal hit(target: Node, position: Vector3)

## Emitted when the projectile reaches max range without hitting anything.
signal expired


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## Travel speed in meters per second. Set during setup() from weapon stats.
var speed: float = 100.0

## Base damage dealt on impact. Armor reduction is applied by DamageSystem.
var damage: int = 10

## Maximum distance this projectile can travel before self-destructing.
var max_range: float = 200.0

## How far the projectile has traveled since spawning. When this exceeds
## max_range the projectile expires.
var distance_traveled: float = 0.0

## Reference to the Vehicle that fired this projectile. Used to prevent
## self-damage and for kill attribution.
var source_vehicle: Vehicle = null

## The behaviour mode of this projectile. See the class doc for details.
## Values: "hitscan", "ballistic", "guided", "area"
var projectile_type: String = "ballistic"

## World-space direction the projectile is traveling. Normalized at spawn,
## then modified each frame by gravity (if applicable).
var direction: Vector3 = Vector3.FORWARD

## Downward acceleration applied each frame. Simulates bullet drop for
## artillery shells. 0.0 means the projectile flies in a straight line.
var gravity_effect: float = 0.0

## Master on/off switch. Set to false when the projectile has hit something
## or expired, to prevent double-processing during the queue_free() frame.
var is_active: bool = true


# ---------------------------------------------------------------------------
# Private variables -- Visual
# ---------------------------------------------------------------------------

## The elongated box mesh that represents the bullet/shell in flight.
var _trail_mesh: MeshInstance3D = null

## The RayCast3D used to detect collisions ahead of the projectile.
## It casts forward by one frame's worth of movement so fast projectiles
## don't tunnel through thin objects.
var _raycast: RayCast3D = null


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

## Create the visual mesh and the forward-facing raycast when the projectile
## enters the scene tree.
func _ready() -> void:
	_create_visual()
	_setup_raycast()


## Move the projectile forward every physics frame. Handle gravity, check
## for raycast hits, and expire if max range is exceeded.
func _physics_process(delta: float) -> void:
	if not is_active:
		return

	# --- Apply gravity (bullet drop) ---
	# Gravity only affects the Y component of the direction vector.
	if gravity_effect > 0.0:
		direction.y -= gravity_effect * delta
		# Re-normalize so the projectile doesn't accelerate.
		direction = direction.normalized()

	# --- Move forward ---
	var movement: Vector3 = direction * speed * delta
	global_position += movement
	distance_traveled += speed * delta

	# --- Orient the mesh to face the travel direction ---
	if direction.length_squared() > 0.001:
		look_at(global_position + direction, Vector3.UP)

	# --- Update the raycast to look ahead by one frame ---
	# This prevents tunneling through thin walls at high speeds.
	if _raycast != null:
		_raycast.target_position = Vector3(0.0, 0.0, -speed * delta)
		_raycast.force_raycast_update()

		if _raycast.is_colliding():
			var hit_point: Vector3 = _raycast.get_collision_point()
			var collider: Node = _raycast.get_collider()
			_on_hit(hit_point, collider)
			return

	# --- Range check ---
	if distance_traveled >= max_range:
		_expire()


# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

## Configure the projectile from a weapon's stats dictionary.
##
## [param data]   - Dictionary of weapon stats. Expected keys:
##                  "projectile_speed" (float), "damage" (int),
##                  "range" (float), "projectile_type" (String),
##                  "gravity" (float, optional).
## [param source] - The Vehicle that fired this projectile.
## [param dir]    - The initial travel direction (should be normalized).
func setup(data: Dictionary, source: Vehicle, dir: Vector3) -> void:
	# Read stats from the weapon data, falling back to sensible defaults.
	speed = float(data.get("projectile_speed", 100.0))
	damage = int(data.get("damage", 10))
	max_range = float(data.get("range", 200.0))
	projectile_type = str(data.get("projectile_type", "ballistic"))
	gravity_effect = float(data.get("gravity", 0.0))

	source_vehicle = source
	direction = dir.normalized()

	# --- Hitscan mode ---
	# For hitscan projectiles, we skip the per-frame movement entirely.
	# Instead we immediately trace a ray from current position along the
	# direction, find the first hit, apply damage, show a brief flash, and
	# self-destruct.
	if projectile_type == "hitscan":
		_do_hitscan()


# ---------------------------------------------------------------------------
# Hit handling
# ---------------------------------------------------------------------------

## Called when the raycast detects a collision or a hitscan ray connects.
##
## [param point]    - World-space position of the impact.
## [param collider] - The physics body or area that was hit.
func _on_hit(point: Vector3, collider: Node) -> void:
	if not is_active:
		return
	is_active = false

	# --- Find the PartNode that was hit ---
	# The raycast may hit a CollisionShape3D, whose parent is a PartNode,
	# whose parent is a Vehicle. We walk up the tree to find either.
	var target_part: PartNode = _find_part_node(collider)

	if target_part != null:
		# Apply damage through the centralized DamageSystem.
		# The DamageSystem handles armor reduction and chain reactions.
		var damage_system: DamageSystem = _get_damage_system()
		if damage_system != null:
			damage_system.apply_damage(target_part, damage, "kinetic", self)
		else:
			# Fallback: apply damage directly if DamageSystem isn't found.
			push_warning("[Projectile] DamageSystem not found. Applying damage directly.")
			target_part.take_damage(damage)

	# --- Spawn a small hit effect ---
	_spawn_hit_effect(point)

	# --- Emit signal and clean up ---
	hit.emit(collider, point)
	queue_free()


## Called when the projectile exceeds its maximum range without hitting
## anything. Emits [signal expired] and removes itself from the scene.
func _expire() -> void:
	if not is_active:
		return
	is_active = false
	expired.emit()
	queue_free()


# ---------------------------------------------------------------------------
# Hitscan
# ---------------------------------------------------------------------------

## Perform an instant raycast from the current position along the direction.
## Used for "hitscan" type projectiles (lasers, railguns).
func _do_hitscan() -> void:
	# Create a temporary PhysicsRayQueryParameters3D for the space state query.
	var space_state: PhysicsDirectSpaceState3D = get_world_3d().direct_space_state
	if space_state == null:
		push_warning("[Projectile] Cannot perform hitscan: no physics space state.")
		queue_free()
		return

	var ray_end: Vector3 = global_position + direction * max_range

	var query: PhysicsRayQueryParameters3D = PhysicsRayQueryParameters3D.create(
		global_position, ray_end
	)

	# Exclude the source vehicle's collision shapes so we don't hit ourselves.
	if source_vehicle != null:
		query.exclude = [source_vehicle.get_rid()]

	var result: Dictionary = space_state.intersect_ray(query)

	if not result.is_empty():
		var hit_point: Vector3 = result["position"]
		var collider: Node = result["collider"]
		_on_hit(hit_point, collider)
	else:
		# Missed everything -- just expire.
		_expire()


# ---------------------------------------------------------------------------
# Visual creation
# ---------------------------------------------------------------------------

## Create a small, elongated box mesh with an emissive (glowing) material.
## This serves as the bullet/shell visual. No external assets required.
func _create_visual() -> void:
	_trail_mesh = MeshInstance3D.new()
	_trail_mesh.name = "TrailMesh"

	# Create a thin, elongated box to look like a bullet trail.
	var box: BoxMesh = BoxMesh.new()
	box.size = Vector3(0.05, 0.05, 0.3)  # Thin and long along Z axis.

	# Emissive material so the projectile glows without needing a light.
	var material: StandardMaterial3D = StandardMaterial3D.new()
	material.albedo_color = Color(1.0, 0.9, 0.3, 1.0)  # Bright yellow
	material.emission_enabled = true
	material.emission = Color(1.0, 0.8, 0.2)  # Warm glow
	material.emission_energy_multiplier = 3.0  # Bright enough to see easily
	box.material = material

	_trail_mesh.mesh = box
	add_child(_trail_mesh)


## Set up the RayCast3D used for collision detection. The ray points forward
## (-Z in local space) and its length is updated each frame to match one
## frame of travel distance. This prevents tunneling at high speeds.
func _setup_raycast() -> void:
	_raycast = RayCast3D.new()
	_raycast.name = "HitRay"
	# Point forward in local space (negative Z is forward in Godot).
	_raycast.target_position = Vector3(0.0, 0.0, -2.0)
	_raycast.enabled = true

	# Exclude the source vehicle so we don't hit our own hull.
	if source_vehicle != null:
		_raycast.add_exception(source_vehicle)

	add_child(_raycast)


# ---------------------------------------------------------------------------
# Hit effects
# ---------------------------------------------------------------------------

## Spawn a brief flash at the impact point. This is a simple visual indicator
## -- a small bright sphere that fades out quickly.
func _spawn_hit_effect(point: Vector3) -> void:
	# Create a tiny explosion / flash at the impact point.
	# We use the Explosion class if available; otherwise just print.
	var explosion_scene: Explosion = Explosion.new()
	explosion_scene.global_position = point
	# Small flash, no gameplay damage (damage was already applied above).
	explosion_scene.setup(0.5, 0, source_vehicle)

	# Add the effect to the scene tree root so it persists after we queue_free.
	if get_tree() != null and get_tree().current_scene != null:
		get_tree().current_scene.add_child(explosion_scene)


# ---------------------------------------------------------------------------
# Utility
# ---------------------------------------------------------------------------

## Walk up the node tree from a collider to find the nearest PartNode ancestor.
## Returns null if no PartNode is found.
func _find_part_node(collider: Node) -> PartNode:
	var current: Node = collider
	while current != null:
		if current is PartNode:
			return current as PartNode
		current = current.get_parent()
	return null


## Find the DamageSystem node in the current scene tree.
## It should be a child of the arena root and in the "damage_system" group,
## or we search by class name.
func _get_damage_system() -> DamageSystem:
	# First try the group lookup (fastest).
	var systems: Array[Node] = get_tree().get_nodes_in_group("damage_system")
	if not systems.is_empty():
		return systems[0] as DamageSystem

	# Fallback: search children of the scene root.
	var root: Node = get_tree().current_scene
	if root == null:
		return null
	for child: Node in root.get_children():
		if child is DamageSystem:
			return child as DamageSystem

	return null
