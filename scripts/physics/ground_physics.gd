## GroundPhysics (PhysicsBase)
##
## Physics controller for ground vehicles (cars, trucks, tanks, walkers).
##
## Movement model:
##   - Thrust from wheels / tracks creates a forward force multiplied by a
##     surface friction coefficient.
##   - Turning applies yaw torque. Turn rate is inversely proportional to
##     speed (realistic-ish: you turn slower at highway speed).
##   - Braking applies a counter-force when no throttle input is given,
##     simulating rolling friction and engine braking.
##   - Maximum speed is limited to thrust / (mass * friction).
##
## All forces use SI units (Newtons, kg, m/s).
class_name GroundPhysics
extends PhysicsBase


# ---------------------------------------------------------------------------
# Tuning constants
# ---------------------------------------------------------------------------

## Surface friction multiplier. Higher = more grip = higher top speed.
## Typical: 0.7 for asphalt, 0.4 for dirt, 0.2 for ice.
var friction_coefficient: float = 0.7

## Base turn speed in radians/second at low velocity. Actual turn rate is
## scaled by (turn_speed / max(speed, 1)).
var turn_speed: float = 2.0

## Deceleration force applied when the player isn't pressing forward/backward.
## Acts like engine braking + rolling resistance (N per kg of mass).
var brake_force: float = 5.0

## Whether the vehicle has tracks instead of wheels. Tracks provide better
## traction and allow zero-radius turns.
var has_tracks: bool = false


# ---------------------------------------------------------------------------
# PhysicsBase overrides
# ---------------------------------------------------------------------------

## Apply ground-vehicle forces each physics frame.
##
## Force model:
##   drive_force   = total_thrust * input_forward * friction_coefficient
##   brake_force   = -velocity_direction * brake_force * mass   (when idle)
##   turn_torque   = input_strafe * turn_speed / max(speed, 1)
##   max_speed     = total_thrust / max(mass, 1) * friction_coefficient
func apply_forces(vehicle: Vehicle, _delta: float) -> void:
	# Only read keyboard input for player-controlled vehicles.
	# AI vehicles get their forces from AIController instead.
	if not vehicle.is_player_controlled:
		return

	var input: Dictionary = vehicle.get_input_vector()
	var forward_input: float = input.get("forward", 0.0)
	var turn_input: float = input.get("strafe", 0.0)

	var forward_dir: Vector3 = vehicle.get_forward_direction()
	var speed: float = vehicle.linear_velocity.length()
	var max_spd: float = get_max_speed(vehicle)

	# --- Drive force ---
	if absf(forward_input) > 0.01 and speed < max_spd:
		# Use impulse (instant velocity change) for reliable movement.
		# Impulse = force * delta gives frame-rate-independent acceleration.
		var drive_magnitude: float = vehicle.total_thrust * forward_input * friction_coefficient * _delta
		vehicle.apply_central_impulse(forward_dir * drive_magnitude)

	# --- Braking / rolling resistance ---
	if absf(forward_input) < 0.01 and speed > 0.5:
		var brake_dir: Vector3 = -vehicle.linear_velocity.normalized()
		vehicle.apply_central_impulse(brake_dir * brake_force * _delta)

	# --- Turning ---
	if absf(turn_input) > 0.01:
		# Turn rate decreases with speed so the vehicle handles differently at
		# high vs low speed. Tracks can pivot in place (no speed penalty).
		var speed_factor: float
		if has_tracks:
			speed_factor = 1.0
		else:
			# At higher speeds, turning is more gradual.
			speed_factor = turn_speed / maxf(speed, 1.0)

		# Torque is applied around the Y axis (vertical).
		var traction: float = _get_traction(vehicle)
		var yaw_torque: float = -turn_input * speed_factor * traction * vehicle.total_mass
		vehicle.apply_torque(Vector3(0.0, yaw_torque, 0.0))


## Read player input and directly drive the vehicle. For ground vehicles,
## handle_input is merged into apply_forces since all input maps to forces.
func handle_input(_vehicle: Vehicle, _delta: float) -> void:
	# Input is read inside apply_forces via vehicle.get_input_vector().
	pass


## Theoretical top speed (m/s) at full throttle on a flat surface.
## Formula: thrust / mass * friction. This is a simplified model.
func get_max_speed(vehicle: Vehicle) -> float:
	var m: float = maxf(vehicle.total_mass, 1.0)
	return vehicle.total_thrust / m * friction_coefficient


## Return "ground".
func get_domain() -> String:
	return "ground"


## HUD data for ground vehicles: speed, heading, and a pseudo gear number.
func get_hud_data(vehicle: Vehicle) -> Dictionary:
	var speed: float = get_current_speed(vehicle)
	var max_spd: float = get_max_speed(vehicle)

	# Pseudo gear: divide the speed range into 5 gears.
	var gear: int = 0
	if max_spd > 0.0:
		gear = clampi(int(speed / max_spd * 5.0) + 1, 1, 5)

	# Heading in degrees (0 = north / +Z).
	var fwd: Vector3 = vehicle.get_forward_direction()
	var heading: float = rad_to_deg(atan2(fwd.x, fwd.z))
	if heading < 0.0:
		heading += 360.0

	return {
		"speed": speed,
		"max_speed": max_spd,
		"heading": heading,
		"gear": gear,
	}


# ---------------------------------------------------------------------------
# Private helpers
# ---------------------------------------------------------------------------

## Count how many propulsion parts are wheel or track type. Used for traction
## calculations.
func _count_wheels(vehicle: Vehicle) -> int:
	var count: int = 0
	for part: PartNode in vehicle.propulsion_parts:
		if part.part_data == null:
			continue
		# Wheels and tracks are identified by subcategory.
		if part.part_data.subcategory in ["wheel", "track"]:
			count += 1
	return count


## Compute a traction multiplier based on wheel / track count and type.
## More wheels = better grip = tighter turns.
## Returns a value roughly in the range 0.5 .. 2.0.
func _get_traction(vehicle: Vehicle) -> float:
	var wheel_count: int = _count_wheels(vehicle)
	if wheel_count <= 0:
		return 0.5  # No wheels -- very poor traction (hovercraft-like)

	# Check if any propulsion part is a track.
	for part: PartNode in vehicle.propulsion_parts:
		if part.part_data != null and part.part_data.subcategory == "track":
			has_tracks = true
			break

	# Tracks give a flat 1.5 bonus. Wheels scale with count.
	if has_tracks:
		return 1.5
	else:
		# Each wheel adds 0.25 traction, base 0.5, capped at 2.0.
		return clampf(0.5 + wheel_count * 0.25, 0.5, 2.0)
