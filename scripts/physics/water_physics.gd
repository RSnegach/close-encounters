## WaterPhysics (PhysicsBase)
##
## Physics controller for surface watercraft (boats, ships, hovercraft).
##
## Movement model:
##   - **Buoyancy**: an upward force proportional to how much hull volume is
##     submerged. Keeps the vehicle at the water surface.
##   - **Wave motion**: a sinusoidal offset added to the target waterline so
##     the boat bobs on waves. Purely cosmetic but adds life to the scene.
##   - **Thrust**: from marine propellers / jet drives. Propels the vessel
##     forward through the water.
##   - **Hull drag**: resists motion through the water, proportional to v^2.
##     Heavier / wider hulls have more drag.
##   - **Rudder turning**: yaw torque from steering input, scaled by speed
##     (you need forward motion to turn effectively).
##
## The water surface is assumed to be a flat plane at y = water_level. A real
## game would query a water shader or heightmap, but flat is fine for an
## arcade combat game.
class_name WaterPhysics
extends PhysicsBase


# ---------------------------------------------------------------------------
# Tuning constants
# ---------------------------------------------------------------------------

## Density of water (kg/m^3). Fresh water is ~1000; salt water ~1025.
## Used in the buoyancy calculation.
var water_density: float = 1000.0

## Y coordinate of the water surface in world space.
var water_level: float = 0.0

## Peak amplitude of wave motion (meters). Affects how much the boat bobs
## up and down. Set to 0 to disable waves.
var wave_amplitude: float = 0.5

## Wave oscillation frequency (Hz). Higher = choppier seas.
var wave_frequency: float = 0.3

## Internal time accumulator for the wave function.
var wave_time: float = 0.0

## Drag coefficient for hull resistance through water. Higher = slower top
## speed for the same thrust.
var hull_drag_coefficient: float = 0.05

## Rudder effectiveness: how much yaw torque per unit of steering input.
var rudder_strength: float = 4.0

## Spring constant that keeps the hull at the waterline. Prevents the boat
## from sinking below or flying above the surface.
var waterline_spring_k: float = 40.0

## Damping on the vertical spring to reduce oscillation / bouncing.
var waterline_damping: float = 5.0


# ---------------------------------------------------------------------------
# PhysicsBase overrides
# ---------------------------------------------------------------------------

## Apply water-surface forces every physics frame.
func apply_forces(vehicle: RigidBody3D, delta: float) -> void:
	wave_time += delta

	var hull_volume: float = _get_hull_volume(vehicle)
	var pos: Vector3 = vehicle.global_position
	var speed: float = vehicle.linear_velocity.length()

	# --- Wave motion ---
	# Compute a sinusoidal offset so the target waterline undulates.
	var wave_offset: float = _get_wave_offset(wave_time, pos.x)
	var target_y: float = water_level + wave_offset

	# --- Buoyancy (spring force toward the waterline) ---
	# The boat should float at the waterline. If it's below, push it up;
	# if above, let gravity pull it down (no downward spring force).
	var depth_below_surface: float = target_y - pos.y
	if depth_below_surface > 0.0:
		# Buoyancy = hull_volume * water_density * g (Archimedes' principle),
		# but we simplify to a spring for gameplay stability.
		# F_spring = k * displacement - damping * vertical_velocity.
		var vertical_vel: float = vehicle.linear_velocity.y
		var spring_force: float = waterline_spring_k * depth_below_surface
		var damping_force: float = waterline_damping * vertical_vel
		vehicle.apply_central_force(Vector3.UP * (spring_force - damping_force))
	else:
		# Above the waterline: only gravity acts (default RigidBody3D behaviour).
		# We still add a mild downward nudge to prevent hover.
		var above: float = pos.y - target_y
		if above > 0.5:
			vehicle.apply_central_force(Vector3.DOWN * above * 10.0)

	# --- Thrust ---
	var input: Dictionary = vehicle.get_input_vector()
	var throttle_input: float = input.get("forward", 0.0)

	var forward: Vector3 = vehicle.get_forward_direction()
	# Keep thrust horizontal (boats don't dive with the throttle).
	forward.y = 0.0
	forward = forward.normalized()

	if absf(throttle_input) > 0.01:
		var thrust: float = vehicle.total_thrust * throttle_input
		vehicle.apply_central_force(forward * thrust)

	# --- Hull drag ---
	# Drag opposes the velocity vector, proportional to speed^2.
	# F_drag = hull_drag_coefficient * hull_volume * speed^2.
	if speed > 0.1:
		var drag_area: float = maxf(hull_volume, 1.0)
		var drag_force: float = hull_drag_coefficient * drag_area * speed * speed
		var drag_dir: Vector3 = -vehicle.linear_velocity.normalized()
		vehicle.apply_central_force(drag_dir * drag_force)

	# --- Rudder turning ---
	var steer_input: float = input.get("strafe", 0.0)
	if absf(steer_input) > 0.01 and speed > 0.5:
		# Rudder is more effective at higher speeds.
		var speed_factor: float = clampf(speed * 0.1, 0.1, 1.0)
		var yaw_torque: float = -steer_input * rudder_strength * speed_factor * vehicle.total_mass
		vehicle.apply_torque(Vector3(0.0, yaw_torque, 0.0))

	# --- Level correction ---
	# Gently rotate the boat to stay upright (no capsizing in arcade mode).
	# Apply a small corrective torque on X and Z to align UP with world UP.
	var current_up: Vector3 = vehicle.global_transform.basis.y
	var correction: Vector3 = current_up.cross(Vector3.UP)
	vehicle.apply_torque(correction * 5.0 * vehicle.total_mass)


## Input handling is merged into apply_forces for water vehicles.
func handle_input(_vehicle: RigidBody3D, _delta: float) -> void:
	pass


## Theoretical max speed based on thrust vs hull drag.
## At terminal velocity: thrust = drag_coeff * hull_vol * v^2
## => v = sqrt(thrust / (drag_coeff * hull_vol)).
func get_max_speed(vehicle: RigidBody3D) -> float:
	var vol: float = maxf(_get_hull_volume(vehicle), 1.0)
	var denom: float = hull_drag_coefficient * vol
	if denom <= 0.0:
		return 0.0
	return sqrt(vehicle.total_thrust / denom)


## Return "water".
func get_domain() -> String:
	return "water"


## HUD data for watercraft.
func get_hud_data(vehicle: RigidBody3D) -> Dictionary:
	var speed: float = get_current_speed(vehicle)
	var fwd: Vector3 = vehicle.get_forward_direction()

	# Heading in degrees.
	var heading: float = rad_to_deg(atan2(fwd.x, fwd.z))
	if heading < 0.0:
		heading += 360.0

	# Hull integrity as a percentage of total HP still remaining.
	var total_hp: float = 0.0
	var current_hp: float = 0.0
	var seen: Dictionary = {}
	for cell: Vector3i in vehicle.parts:
		var pn: PartNode = vehicle.parts[cell]
		var nid: int = pn.get_instance_id()
		if seen.has(nid):
			continue
		seen[nid] = true
		total_hp += pn.part_data.hp
		current_hp += pn.current_hp

	var integrity: float = 100.0
	if total_hp > 0.0:
		integrity = (current_hp / total_hp) * 100.0

	return {
		"speed": speed,
		"max_speed": get_max_speed(vehicle),
		"heading": heading,
		"hull_integrity_percent": integrity,
	}


# ---------------------------------------------------------------------------
# Private helpers
# ---------------------------------------------------------------------------

## Sum up the hull volume from the vehicle's hull / keel parts.
## Volume is computed as the product of each hull part's grid size.
func _get_hull_volume(vehicle: RigidBody3D) -> float:
	var volume: float = 0.0
	var seen: Dictionary = {}

	for cell: Vector3i in vehicle.parts:
		var pn: PartNode = vehicle.parts[cell]
		var nid: int = pn.get_instance_id()
		if seen.has(nid):
			continue
		seen[nid] = true

		if pn.part_data == null:
			continue
		if pn.part_data.subcategory in ["hull", "keel", "pressurized_hull"]:
			volume += float(pn.part_data.size.x * pn.part_data.size.y * pn.part_data.size.z)

	return volume


## Compute the wave height offset at a given time and x-position.
## Uses a simple sine function. The x-position shifts the phase so
## different parts of the ocean surface are at different heights.
func _get_wave_offset(time: float, x_pos: float) -> float:
	return sin(time * wave_frequency * TAU + x_pos * 0.5) * wave_amplitude
