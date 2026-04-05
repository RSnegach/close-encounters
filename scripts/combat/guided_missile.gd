## GuidedMissile -- Seeking missile that tracks a target.
##
## Extends [Projectile] with guidance logic: the missile steers toward a
## locked target each frame, detonating on proximity or direct impact.
##
## The missile has an arming distance -- it won't detonate until it has
## traveled a minimum distance from the launcher. This prevents the player
## from blowing themselves up at point-blank range.
##
## Guidance behavior:
##   1. Launch straight (like a regular projectile) for the first few meters.
##   2. Once armed, begin steering toward the target.
##   3. On proximity (within half the explosion radius) or direct hit, detonate.
##   4. Detonation applies area damage via the DamageSystem.
class_name GuidedMissile
extends Projectile


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## The target node this missile is tracking. Can be any Node3D -- typically
## the enemy vehicle's control module or the vehicle root itself.
var target: Node3D = null

## Maximum steering speed in radians per second. Higher values make the
## missile more agile but look less realistic.
var turn_rate: float = 2.0

## Maximum distance at which the missile can acquire (lock onto) a target.
var lock_range: float = 200.0

## Minimum distance traveled before the warhead becomes active. Prevents
## self-damage from point-blank launches.
var arm_distance: float = 10.0

## Blast radius for the warhead detonation. Everything within this sphere
## takes area damage (with falloff).
var explosion_radius: float = 5.0

## Whether the warhead is active. Set to true once distance_traveled
## exceeds arm_distance.
var is_armed: bool = false


# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

## Configure the guided missile from weapon stats and assign a target.
##
## [param data]        - Weapon stats dictionary. In addition to the base
##                       Projectile keys, expects: "turn_rate" (float),
##                       "lock_range" (float), "arm_distance" (float),
##                       "explosion_radius" (float).
## [param source]      - The Vehicle that launched this missile.
## [param target_node] - The Node3D to track. Null means the missile flies
##                       straight (dumb-fire mode).
func setup_guided(data: Dictionary, source: Vehicle, target_node: Node3D) -> void:
	# Configure the base Projectile fields first.
	# Direction is toward the target if we have one; otherwise straight ahead.
	var dir: Vector3
	if target_node != null and is_instance_valid(target_node):
		dir = (target_node.global_position - global_position).normalized()
	else:
		dir = -source.global_transform.basis.z  # Vehicle's forward direction.

	setup(data, source, dir)

	# Override base speed to something slower than bullets -- missiles are
	# typically slower but more powerful.
	speed = float(data.get("projectile_speed", 60.0))

	# Store the target reference.
	target = target_node

	# Read guidance parameters from weapon stats.
	turn_rate = float(data.get("turn_rate", 2.0))
	lock_range = float(data.get("lock_range", 200.0))
	arm_distance = float(data.get("arm_distance", 10.0))
	explosion_radius = float(data.get("explosion_radius", 5.0))


# ---------------------------------------------------------------------------
# Physics processing
# ---------------------------------------------------------------------------

## Override the base Projectile physics to add guidance logic.
func _physics_process(delta: float) -> void:
	if not is_active:
		return

	# --- Arm check ---
	# The warhead activates once the missile has traveled far enough from
	# the launcher. This is a safety measure.
	if not is_armed and distance_traveled >= arm_distance:
		is_armed = true

	# --- Guidance: steer toward the target ---
	if target != null and is_instance_valid(target):
		var to_target: Vector3 = (target.global_position - global_position)
		var target_dir: Vector3 = to_target.normalized()

		# Smoothly rotate the travel direction toward the target.
		# The lerp rate is controlled by turn_rate * delta, capped at 1.0.
		var steer_amount: float = minf(turn_rate * delta, 1.0)
		direction = direction.lerp(target_dir, steer_amount).normalized()

		# --- Proximity fuse ---
		# If armed and within half the explosion radius, detonate immediately.
		# This handles cases where the missile passes very close but doesn't
		# score a direct hit.
		if is_armed:
			var dist_to_target: float = to_target.length()
			if dist_to_target < explosion_radius * 0.5:
				detonate()
				return

	# --- Movement and base collision/range checks ---
	# Apply gravity (if any).
	if gravity_effect > 0.0:
		direction.y -= gravity_effect * delta
		direction = direction.normalized()

	# Move forward.
	var movement: Vector3 = direction * speed * delta
	global_position += movement
	distance_traveled += speed * delta

	# Orient the mesh to face the travel direction.
	if direction.length_squared() > 0.001:
		look_at(global_position + direction, Vector3.UP)

	# Check raycast for direct hits.
	if _raycast != null:
		_raycast.target_position = Vector3(0.0, 0.0, -speed * delta)
		_raycast.force_raycast_update()

		if _raycast.is_colliding():
			var hit_point: Vector3 = _raycast.get_collision_point()
			var collider: Node = _raycast.get_collider()
			_on_hit(hit_point, collider)
			return

	# Range check.
	if distance_traveled >= max_range:
		_expire()


# ---------------------------------------------------------------------------
# Hit override
# ---------------------------------------------------------------------------

## Override the base hit handler. If the warhead is armed, detonate (area
## damage). If not armed, just apply direct impact damage like a bullet.
func _on_hit(point: Vector3, collider: Node) -> void:
	if not is_active:
		return

	if is_armed:
		# Move to the hit point before detonating for accurate blast center.
		global_position = point
		detonate()
	else:
		# Not armed yet -- just do regular impact damage (no explosion).
		super._on_hit(point, collider)


# ---------------------------------------------------------------------------
# Detonation
# ---------------------------------------------------------------------------

## Detonate the missile warhead: apply area damage, spawn an explosion
## visual, and remove the missile from the scene.
func detonate() -> void:
	if not is_active:
		return
	is_active = false

	print("[GuidedMissile] Detonating at %s, radius=%.1f, damage=%d" % [
		global_position, explosion_radius, damage
	])

	# --- Apply area damage via the DamageSystem ---
	var damage_system: DamageSystem = _get_damage_system()
	if damage_system != null:
		damage_system.apply_area_damage(
			global_position,
			explosion_radius,
			damage,
			source_vehicle
		)
	else:
		push_warning("[GuidedMissile] DamageSystem not found. Area damage skipped.")

	# --- Spawn explosion visual ---
	var explosion: Explosion = Explosion.new()
	if get_tree() != null and get_tree().current_scene != null:
		get_tree().current_scene.add_child(explosion)
		explosion.global_position = global_position
		explosion.setup(explosion_radius, damage, source_vehicle)

	# --- Clean up ---
	hit.emit(null, global_position)
	queue_free()


# ---------------------------------------------------------------------------
# Target acquisition
# ---------------------------------------------------------------------------

## Scan for the nearest enemy vehicle and return its control module (or the
## vehicle root if no control module exists). Called by the weapon system
## when no target is pre-assigned.
##
## [param firing_vehicle] - The vehicle doing the scanning. Enemies only.
## Returns null if no valid target is found within lock_range.
func find_target(firing_vehicle: Vehicle) -> Node3D:
	var best_target: Node3D = null
	var best_dist: float = lock_range

	var all_vehicles: Array[Node] = get_tree().get_nodes_in_group("vehicles")

	for node: Node in all_vehicles:
		if not node is Vehicle:
			continue
		var vehicle: Vehicle = node as Vehicle

		# Skip self and dead vehicles.
		if vehicle == firing_vehicle:
			continue
		if not vehicle.is_alive:
			continue

		var dist: float = firing_vehicle.global_position.distance_to(vehicle.global_position)
		if dist < best_dist:
			best_dist = dist
			# Prefer targeting the control module (kill shot).
			if vehicle.control_module != null and not vehicle.control_module.is_destroyed:
				best_target = vehicle.control_module
			else:
				best_target = vehicle

	return best_target
