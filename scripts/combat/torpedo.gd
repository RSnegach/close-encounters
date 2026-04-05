## Torpedo -- Underwater guided projectile for submarine and naval combat.
##
## Slower than missiles but packs a much larger warhead. Torpedoes behave
## differently above and below the waterline:
##
##   - Above water (y > 0): Affected by gravity. This handles the case where
##     a surface ship drops a torpedo from a launcher above the waterline --
##     it falls until it enters the water.
##   - Underwater (y <= 0): Self-propelled at [member swim_speed]. Steers
##     toward the target using a slower turn rate than missiles (torpedoes
##     are heavy and less maneuverable).
##
## Torpedoes also have a "noise level" stat that could be detected by sonar
## in a future stealth system.
class_name Torpedo
extends Projectile


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## The target node this torpedo is tracking. Typically the enemy vehicle's
## hull or control module.
var target: Node3D = null

## Maximum steering speed in radians per second. Torpedoes steer slower than
## missiles because they are heavier and move through a denser medium.
var turn_rate: float = 1.0

## Blast radius for the warhead detonation. Underwater explosions have a
## larger effective radius than surface explosions because water transmits
## shockwaves more efficiently.
var explosion_radius: float = 8.0

## Maximum operational depth. If the torpedo goes deeper than this, it
## implodes (self-destructs with no damage to enemies).
var max_depth: float = -400.0

## Cruising speed underwater in meters per second. Much slower than a bullet
## or missile, but the warhead compensates.
var swim_speed: float = 20.0

## How loud the torpedo is. Higher values make it easier for enemy sonar to
## detect. (Not yet used in gameplay but stored for future expansion.)
var noise_level: float = 50.0

## Minimum distance traveled before the warhead becomes active. Prevents
## self-damage from close-range launches.
var arm_distance: float = 15.0

## Whether the warhead is active.
var is_armed: bool = false


# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

## Configure the torpedo from weapon stats and assign a target.
##
## [param data]        - Weapon stats dictionary. Expected keys:
##                       "swim_speed" (float), "turn_rate" (float),
##                       "explosion_radius" (float), "max_depth" (float),
##                       "noise_level" (float), "arm_distance" (float).
## [param source]      - The Vehicle that launched this torpedo.
## [param target_node] - The Node3D to track (enemy vehicle or part).
func setup_torpedo(data: Dictionary, source: Node, target_node: Node3D) -> void:
	# Set up the base projectile (speed, damage, range, etc.).
	var dir: Vector3
	if target_node != null and is_instance_valid(target_node):
		dir = (target_node.global_position - global_position).normalized()
	else:
		dir = -source.global_transform.basis.z

	setup(data, source, dir)

	# Override speed to the slower swim speed.
	swim_speed = float(data.get("swim_speed", 20.0))
	speed = swim_speed

	# Store target and read torpedo-specific stats.
	target = target_node
	turn_rate = float(data.get("turn_rate", 1.0))
	explosion_radius = float(data.get("explosion_radius", 8.0))
	max_depth = float(data.get("max_depth", -400.0))
	noise_level = float(data.get("noise_level", 50.0))
	arm_distance = float(data.get("arm_distance", 15.0))


# ---------------------------------------------------------------------------
# Physics processing
# ---------------------------------------------------------------------------

## Override the base physics to handle water entry, underwater guidance,
## depth limits, and proximity detonation.
func _physics_process(delta: float) -> void:
	if not is_active:
		return

	# --- Arm check ---
	if not is_armed and distance_traveled >= arm_distance:
		is_armed = true

	# --- Above water: torpedo is in free-fall ---
	# When a torpedo is launched from a surface ship's deck tubes, it starts
	# above the waterline and needs to fall into the water before propulsion
	# kicks in.
	if global_position.y > 0.0:
		# Apply gravity to simulate the torpedo dropping into the water.
		direction.y -= 9.8 * delta
		direction = direction.normalized()

		# Move at a slower speed while falling (no propulsion yet).
		var fall_speed: float = speed * 0.5
		global_position += direction * fall_speed * delta
		distance_traveled += fall_speed * delta

	# --- Underwater: self-propelled with guidance ---
	else:
		# Steer toward the target if we have one.
		_steer_toward_target(delta)

		# Move forward at swim speed.
		global_position += direction * swim_speed * delta
		distance_traveled += swim_speed * delta

		# --- Depth limit check ---
		# Real torpedoes have a crush depth. If this torpedo goes too deep,
		# it implodes harmlessly.
		if global_position.y < max_depth:
			print("[Torpedo] Exceeded max depth (%.1f). Imploding." % global_position.y)
			_expire()
			return

	# --- Orient the mesh to face the travel direction ---
	if direction.length_squared() > 0.001:
		look_at(global_position + direction, Vector3.UP)

	# --- Proximity fuse (underwater only) ---
	if is_armed and target != null and is_instance_valid(target):
		var dist_to_target: float = global_position.distance_to(target.global_position)
		if dist_to_target < explosion_radius * 0.5:
			detonate()
			return

	# --- Raycast collision check ---
	if _raycast != null:
		_raycast.target_position = Vector3(0.0, 0.0, -swim_speed * delta)
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
# Hit override
# ---------------------------------------------------------------------------

## On hit, detonate if armed; otherwise just do direct impact damage.
func _on_hit(point: Vector3, collider: Node) -> void:
	if not is_active:
		return

	if is_armed:
		global_position = point
		detonate()
	else:
		super._on_hit(point, collider)


# ---------------------------------------------------------------------------
# Detonation
# ---------------------------------------------------------------------------

## Detonate the torpedo warhead. Underwater explosions have a larger effective
## radius because the shockwave propagates through water more efficiently.
func detonate() -> void:
	if not is_active:
		return
	is_active = false

	# Underwater explosions are 1.5x larger than the nominal radius.
	var effective_radius: float = explosion_radius
	if global_position.y <= 0.0:
		effective_radius *= 1.5

	print("[Torpedo] Detonating at %s, radius=%.1f (effective=%.1f), damage=%d" % [
		global_position, explosion_radius, effective_radius, damage
	])

	# Apply area damage.
	var damage_system: DamageSystem = _get_damage_system()
	if damage_system != null:
		damage_system.apply_area_damage(
			global_position,
			effective_radius,
			damage,
			source_vehicle
		)
	else:
		push_warning("[Torpedo] DamageSystem not found. Area damage skipped.")

	# Spawn explosion visual.
	var explosion: Explosion = Explosion.new()
	if get_tree() != null and get_tree().current_scene != null:
		get_tree().current_scene.add_child(explosion)
		explosion.global_position = global_position
		explosion.setup(effective_radius, damage, source_vehicle)

	# Clean up.
	hit.emit(null, global_position)
	queue_free()


# ---------------------------------------------------------------------------
# Guidance
# ---------------------------------------------------------------------------

## Gradually steer the torpedo toward its target. Uses a simple lerp on the
## direction vector, limited by the turn rate.
##
## [param delta] - Frame time in seconds.
func _steer_toward_target(delta: float) -> void:
	if target == null or not is_instance_valid(target):
		# No target -- continue on current heading.
		return

	var to_target: Vector3 = (target.global_position - global_position).normalized()

	# Lerp the current direction toward the target direction.
	# The turn rate limits how fast the torpedo can turn per second.
	var steer_amount: float = minf(turn_rate * delta, 1.0)
	direction = direction.lerp(to_target, steer_amount).normalized()
