## SubmarinePhysics (PhysicsBase)
##
## Physics controller for submarines. Extends the water model with:
##
##   - **Ballast tanks**: the player adjusts ballast_level (0.0 = surface,
##     1.0 = maximum dive rate) to control depth. Net buoyancy is the
##     difference between hull displacement and ballast weight.
##   - **3D movement**: unlike surface ships, subs pitch and dive through
##     the full water volume.
##   - **Crush depth**: below a configurable depth, the hull takes pressure
##     damage each second until it implodes.
##   - **Noise level**: faster movement and certain propulsion types generate
##     noise, which could feed into a detection / stealth system.
##
## Depth is measured as the Y coordinate (negative = deeper). The water
## surface is at y = water_level (default 0).
class_name SubmarinePhysics
extends PhysicsBase


# ---------------------------------------------------------------------------
# Tuning constants
# ---------------------------------------------------------------------------

## Y coordinate of the water surface in world space.
var water_level: float = 0.0

## Depth (negative Y) at which the hull starts taking pressure damage.
## For example, -500 means 500 meters below the surface.
var crush_depth: float = -500.0

## Depth at which HUD warnings begin. Gives the player time to ascend before
## crush depth is reached.
var danger_depth: float = -300.0

## Current depth (cached each frame). Negative = below surface.
var current_depth: float = 0.0

## Ballast level: 0.0 = fully blown (surface), 1.0 = fully flooded (dive).
## Intermediate values hover at a depth proportional to the range.
var ballast_level: float = 0.5

## Noise level (arbitrary 0..100 scale). Increases with speed and thrust.
## A stealth / sonar system can read this value.
var noise_level: float = 0.0

## HP per second of damage dealt to random hull parts when below crush depth.
var pressure_damage_rate: float = 5.0

## How quickly ballast responds to player input (units per second).
var ballast_rate: float = 0.3

## Water resistance coefficient. Submarine drag is generally higher than
## surface craft because the entire hull is submerged.
var water_resistance: float = 0.08

## Torque multipliers for pitch and yaw control.
var pitch_torque: float = 6.0
var yaw_torque: float = 6.0


# ---------------------------------------------------------------------------
# PhysicsBase overrides
# ---------------------------------------------------------------------------

## Apply submarine forces every physics frame.
func apply_forces(vehicle: RigidBody3D, delta: float) -> void:
	var input: Dictionary = vehicle.get_input_vector()
	current_depth = vehicle.global_position.y

	# --- Ballast control ---
	# Dive (F key) floods the tanks -> ballast_level increases -> sub sinks.
	# Surface (R key) blows the tanks -> ballast_level decreases -> sub rises.
	if input.get("dive", false):
		ballast_level = clampf(ballast_level + ballast_rate * delta, 0.0, 1.0)
	elif input.get("surface", false):
		ballast_level = clampf(ballast_level - ballast_rate * delta, 0.0, 1.0)

	# --- Net buoyancy ---
	# At ballast_level 0.0 the sub is positively buoyant (rises to surface).
	# At ballast_level 0.5 the sub is neutrally buoyant (hovers).
	# At ballast_level 1.0 the sub is negatively buoyant (sinks).
	#
	# net_buoyancy ranges from +1 (rise) through 0 (hover) to -1 (sink).
	var net_buoyancy: float = 1.0 - (ballast_level * 2.0)

	# The force magnitude scales with mass and a gravity-like constant.
	# Positive net_buoyancy -> upward force.
	var buoyancy_force: float = net_buoyancy * vehicle.total_mass * 9.8
	vehicle.apply_central_force(Vector3.UP * buoyancy_force)

	# --- Surface clamp ---
	# Prevent the sub from flying above the waterline when ballast is blown.
	if current_depth > water_level and net_buoyancy > 0.0:
		var over: float = current_depth - water_level
		vehicle.apply_central_force(Vector3.DOWN * over * 30.0 * vehicle.total_mass)

	# --- Thrust ---
	var throttle_input: float = input.get("forward", 0.0)
	var forward: Vector3 = vehicle.get_forward_direction()

	if absf(throttle_input) > 0.01:
		var thrust: float = vehicle.total_thrust * throttle_input
		vehicle.apply_central_force(forward * thrust)

	# --- Water resistance (drag) ---
	var speed: float = vehicle.linear_velocity.length()
	if speed > 0.1:
		var drag_force: float = water_resistance * speed * speed * vehicle.total_mass * 0.01
		vehicle.apply_central_force(-vehicle.linear_velocity.normalized() * drag_force)

	# --- Pitch (dive angle) ---
	# W = nose down, S = nose up. Affects the direction the sub travels.
	var pitch_input: float = input.get("pitch", 0.0)
	if absf(pitch_input) > 0.01:
		var local_x: Vector3 = vehicle.global_transform.basis.x
		vehicle.apply_torque(local_x * pitch_input * pitch_torque * vehicle.total_mass)

	# --- Yaw (left/right) ---
	var yaw_input: float = input.get("yaw", 0.0)
	if absf(yaw_input) > 0.01:
		vehicle.apply_torque(Vector3.UP * -yaw_input * yaw_torque * vehicle.total_mass)

	# --- Level correction (roll only) ---
	# Subs shouldn't roll uncontrollably; gently correct roll to zero.
	var current_up: Vector3 = vehicle.global_transform.basis.y
	var roll_correction: Vector3 = Vector3(
		0.0, 0.0, current_up.cross(Vector3.UP).z
	)
	vehicle.apply_torque(roll_correction * 3.0 * vehicle.total_mass)

	# --- Pressure damage ---
	if current_depth < crush_depth:
		_apply_pressure_damage(vehicle, delta)

	# --- Noise calculation ---
	# Noise is a function of speed and throttle. Fast subs are loud.
	noise_level = clampf(speed * 2.0 + absf(throttle_input) * 20.0, 0.0, 100.0)


## Input is handled inside apply_forces for submarines.
func handle_input(_vehicle: RigidBody3D, _delta: float) -> void:
	pass


## Return "submarine".
func get_domain() -> String:
	return "submarine"


## Max speed for submarines: limited by thrust vs water resistance.
func get_max_speed(vehicle: RigidBody3D) -> float:
	var resist: float = maxf(water_resistance * vehicle.total_mass * 0.01, 0.01)
	return sqrt(vehicle.total_thrust / resist)


## HUD data for submarines.
func get_hud_data(vehicle: RigidBody3D) -> Dictionary:
	var speed: float = get_current_speed(vehicle)

	# Depth as a positive number (meters below surface) for display.
	var display_depth: float = maxf(water_level - current_depth, 0.0)

	# Pressure warning level:
	#   "safe"    = above danger depth
	#   "warning" = between danger and crush
	#   "critical" = below crush depth
	var pressure_warning: String = "safe"
	if current_depth < crush_depth:
		pressure_warning = "critical"
	elif current_depth < danger_depth:
		pressure_warning = "warning"

	return {
		"speed": speed,
		"max_speed": get_max_speed(vehicle),
		"depth": display_depth,
		"ballast_level": ballast_level,
		"noise_level": noise_level,
		"pressure_warning": pressure_warning,
	}


# ---------------------------------------------------------------------------
# Private helpers
# ---------------------------------------------------------------------------

## When below crush depth, deal pressure damage to random hull parts every
## frame. The damage rate is pressure_damage_rate HP per second.
func _apply_pressure_damage(vehicle: RigidBody3D, delta: float) -> void:
	# How much depth below crush (positive number = how far past the limit).
	var over_depth: float = crush_depth - current_depth  # both are negative, so this is positive
	if over_depth <= 0.0:
		return

	# Scale damage with how far past crush depth we are.
	var damage_this_frame: float = pressure_damage_rate * delta * (1.0 + over_depth * 0.01)

	# Collect hull parts that can take damage.
	var hull_parts: Array[PartNode] = []
	var seen: Dictionary = {}
	for cell: Vector3i in vehicle.parts:
		var pn: PartNode = vehicle.parts[cell]
		var nid: int = pn.get_instance_id()
		if seen.has(nid):
			continue
		seen[nid] = true
		if pn.part_data != null and pn.is_functional():
			if pn.part_data.subcategory in ["hull", "keel", "pressurized_hull"]:
				hull_parts.append(pn)

	# If no hull parts remain, damage a random surviving part instead.
	if hull_parts.is_empty():
		var all_parts: Array[PartNode] = []
		seen.clear()
		for cell: Vector3i in vehicle.parts:
			var pn: PartNode = vehicle.parts[cell]
			var nid: int = pn.get_instance_id()
			if not seen.has(nid) and pn.is_functional():
				seen[nid] = true
				all_parts.append(pn)
		if all_parts.is_empty():
			return
		hull_parts = all_parts

	# Pick a random part and apply the damage.
	# take_damage() expects an int, so round the float up (ceili) to ensure
	# at least 1 HP of damage per tick even at low rates.
	var target: PartNode = hull_parts[randi() % hull_parts.size()]
	target.take_damage(ceili(damage_this_frame))
