## RocketPhysics (PhysicsBase)
##
## Physics controller for rocket / space vehicles. Two distinct flight phases:
##
## **Launch phase** (altitude < space_altitude):
##   - Gravity pulls the vehicle down at 9.8 m/s^2. TWR must exceed 1.0 to
##     leave the ground.
##   - Atmospheric drag is proportional to speed^2 and decreases with altitude
##     (thinner air = less drag).
##   - Fuel is consumed at a rate proportional to thrust output.
##
## **Space phase** (altitude >= space_altitude):
##   - No gravity, no drag, no terminal velocity (Newtonian mechanics).
##   - RCS (Reaction Control System) thrusters provide rotation and small
##     translational forces.
##   - Fuel is still consumed when thrusting.
##
## Fuel model:
##   fuel_remaining decreases by (thrust * burn_rate * delta) each frame.
##   When fuel hits zero, the main engine cuts out. The vehicle may still have
##   RCS fuel (not modeled separately -- assumed unlimited for gameplay).
class_name RocketPhysics
extends PhysicsBase


# ---------------------------------------------------------------------------
# Tuning constants
# ---------------------------------------------------------------------------

## Whether the vehicle has passed the atmosphere threshold.
var is_in_space: bool = false

## Altitude (meters) at which "space" begins. Above this: no gravity, no drag.
var space_altitude: float = 200.0

## Atmospheric drag coefficient at sea level. Decreases linearly with altitude
## until it reaches zero at space_altitude.
var atmospheric_drag_coefficient: float = 0.01

## Current fuel remaining (abstract fuel units). Starts at max_fuel, consumed
## as the engine fires.
var fuel_remaining: float = 0.0

## Maximum fuel capacity, calculated from the vehicle's fuel tank parts in
## setup_fuel().
var max_fuel: float = 0.0

## Fuel consumed per unit of thrust per second.
## Consumption = applied_thrust * burn_rate * delta.
var burn_rate: float = 0.1

## Current throttle level: 0.0 (off) to 1.0 (full power).
var throttle: float = 0.0


# ---------------------------------------------------------------------------
# Constants (internal)
# ---------------------------------------------------------------------------

## How quickly the throttle ramps up / down per second.
const THROTTLE_RATE: float = 0.8

## Gravitational acceleration (m/s^2) applied during the launch phase.
const GRAVITY: float = 9.8

## Torque magnitude for pitch / yaw / roll control (atmospheric flight).
const ATMO_TORQUE: float = 8.0

## Torque magnitude for RCS thrusters in space (typically weaker than
## aerodynamic control surfaces, but more precise).
const RCS_TORQUE: float = 3.0

## Small translational force for RCS lateral / vertical manoeuvring in space.
const RCS_TRANSLATION_FORCE: float = 500.0


# ---------------------------------------------------------------------------
# PhysicsBase overrides
# ---------------------------------------------------------------------------

## Apply rocket forces every physics frame.
func apply_forces(vehicle: Vehicle, delta: float) -> void:
	var input: Dictionary = vehicle.get_input_vector()
	var altitude: float = vehicle.global_position.y
	var speed: float = vehicle.linear_velocity.length()

	# --- Update space flag ---
	is_in_space = altitude >= space_altitude

	# --- Throttle ---
	if input.get("throttle_up", false):
		throttle = clampf(throttle + THROTTLE_RATE * delta, 0.0, 1.0)
	elif input.get("throttle_down", false):
		throttle = clampf(throttle - THROTTLE_RATE * delta, 0.0, 1.0)

	# --- Main engine thrust ---
	# Only fire if there is fuel remaining.
	var applied_thrust: float = 0.0
	if fuel_remaining > 0.0 and throttle > 0.0:
		applied_thrust = vehicle.total_thrust * throttle

		# Consume fuel: consumption = thrust * burn_rate * delta.
		var fuel_used: float = applied_thrust * burn_rate * delta
		fuel_remaining = maxf(fuel_remaining - fuel_used, 0.0)

		# Apply thrust in the vehicle's "up" direction (rockets point up).
		# By convention the thrust nozzles point down, so thrust pushes up.
		var thrust_dir: Vector3 = vehicle.global_transform.basis.y
		vehicle.apply_central_force(thrust_dir * applied_thrust)
	elif fuel_remaining <= 0.0 and throttle > 0.0:
		# Out of fuel -- cut the throttle indicator so the HUD reflects reality.
		throttle = 0.0

	# --- Phase-specific forces ---
	if not is_in_space:
		_apply_atmospheric_forces(vehicle, delta, altitude, speed)
	else:
		_apply_space_forces(vehicle, delta, input)


## Translate input into rotation torques. Delegates to atmospheric or space
## handling depending on phase.
func handle_input(vehicle: Vehicle, _delta: float) -> void:
	var input: Dictionary = vehicle.get_input_vector()

	var pitch_input: float = input.get("pitch", 0.0)
	var yaw_input: float = input.get("yaw", 0.0)
	var roll_input: float = input.get("roll", 0.0)

	if is_in_space:
		# In space: RCS thrusters for rotation. Less torque, more precise.
		var local_x: Vector3 = vehicle.global_transform.basis.x
		var local_y: Vector3 = vehicle.global_transform.basis.y
		var local_z: Vector3 = vehicle.global_transform.basis.z

		vehicle.apply_torque(local_x * pitch_input * RCS_TORQUE * vehicle.total_mass)
		vehicle.apply_torque(local_y * -yaw_input * RCS_TORQUE * vehicle.total_mass)
		vehicle.apply_torque(local_z * -roll_input * RCS_TORQUE * vehicle.total_mass)
	else:
		# In atmosphere: aerodynamic control surfaces, stronger torque.
		var local_x: Vector3 = vehicle.global_transform.basis.x
		var local_y: Vector3 = vehicle.global_transform.basis.y
		var local_z: Vector3 = vehicle.global_transform.basis.z

		vehicle.apply_torque(local_x * pitch_input * ATMO_TORQUE * vehicle.total_mass)
		vehicle.apply_torque(local_y * -yaw_input * ATMO_TORQUE * vehicle.total_mass)
		vehicle.apply_torque(local_z * -roll_input * ATMO_TORQUE * vehicle.total_mass)


## Return "space".
func get_domain() -> String:
	return "space"


## Max speed depends on phase. In atmosphere: terminal velocity from
## thrust vs drag. In space: effectively infinite (Newtonian).
func get_max_speed(vehicle: Vehicle) -> float:
	if is_in_space:
		# No max speed in space. Return current speed + 1 as a placeholder.
		return vehicle.linear_velocity.length() + 1.0
	else:
		# Terminal velocity in atmosphere: v = sqrt(thrust / drag).
		var drag: float = maxf(atmospheric_drag_coefficient * vehicle.total_mass, 0.01)
		return sqrt(vehicle.total_thrust / drag)


## HUD data for rockets.
func get_hud_data(vehicle: Vehicle) -> Dictionary:
	var speed: float = get_current_speed(vehicle)
	var altitude: float = vehicle.global_position.y

	# Fuel percentage for the gauge.
	var fuel_percent: float = 0.0
	if max_fuel > 0.0:
		fuel_percent = (fuel_remaining / max_fuel) * 100.0

	# Current TWR at the current throttle level.
	var twr: float = _get_twr(vehicle)

	# Very rough apoapsis estimate for the HUD (assumes straight-up flight).
	# In reality you'd integrate the trajectory, but for an arcade game:
	# apoapsis ~ altitude + (vertical_speed^2) / (2 * g)
	var vert_speed: float = vehicle.linear_velocity.y
	var apoapsis: float = altitude
	if vert_speed > 0.0 and not is_in_space:
		apoapsis += (vert_speed * vert_speed) / (2.0 * GRAVITY)

	return {
		"speed": speed,
		"altitude": altitude,
		"fuel_percent": fuel_percent,
		"throttle": throttle,
		"twr": twr,
		"is_in_space": is_in_space,
		"apoapsis_estimate": apoapsis,
	}


# ---------------------------------------------------------------------------
# Fuel setup
# ---------------------------------------------------------------------------

## Calculate max_fuel from the vehicle's fuel tank and booster parts.
## Called once during Vehicle.setup_from_data() after parts are created.
func setup_fuel(vehicle: Vehicle) -> void:
	max_fuel = 0.0

	var seen: Dictionary = {}
	for cell: Vector3i in vehicle.parts:
		var pn: PartNode = vehicle.parts[cell]
		var nid: int = pn.get_instance_id()
		if seen.has(nid):
			continue
		seen[nid] = true

		if pn.part_data == null:
			continue

		# Accumulate fuel from fuel_tank and booster parts.
		if pn.part_data.stats.has("fuel"):
			max_fuel += float(pn.part_data.stats["fuel"])
		if pn.part_data.stats.has("booster_fuel"):
			max_fuel += float(pn.part_data.stats["booster_fuel"])

	fuel_remaining = max_fuel
	print("[RocketPhysics] Fuel loaded: %.1f units." % max_fuel)


# ---------------------------------------------------------------------------
# Private helpers
# ---------------------------------------------------------------------------

## Apply gravity and atmospheric drag during the launch phase.
func _apply_atmospheric_forces(
	vehicle: Vehicle, _delta: float, altitude: float, speed: float
) -> void:
	# --- Gravity ---
	# F = m * g, directed downward. Godot's built-in gravity is disabled for
	# rockets (gravity_scale = 0 on the Vehicle), so we apply it manually to
	# have full control over the transition to space.
	var gravity_force: float = vehicle.total_mass * GRAVITY
	vehicle.apply_central_force(Vector3.DOWN * gravity_force)

	# --- Atmospheric drag ---
	# Drag decreases linearly with altitude until it vanishes at space_altitude.
	# drag_factor = 1.0 at sea level, 0.0 at space_altitude.
	var drag_factor: float = clampf(1.0 - (altitude / space_altitude), 0.0, 1.0)

	if speed > 0.1 and drag_factor > 0.0:
		# F_drag = drag_coeff * mass * v^2 * drag_factor.
		var drag_force: float = (
			atmospheric_drag_coefficient * vehicle.total_mass * speed * speed * drag_factor
		)
		vehicle.apply_central_force(-vehicle.linear_velocity.normalized() * drag_force)


## Apply space-specific forces (RCS translation).
func _apply_space_forces(
	vehicle: Vehicle, _delta: float, input: Dictionary
) -> void:
	# In space, WASD can also apply small RCS translation forces for docking
	# and fine manoeuvring (in addition to the rotation handled in handle_input).
	var forward_input: float = input.get("forward", 0.0)
	var strafe_input: float = input.get("strafe", 0.0)

	if absf(forward_input) > 0.01:
		var fwd: Vector3 = vehicle.get_forward_direction()
		vehicle.apply_central_force(fwd * forward_input * RCS_TRANSLATION_FORCE)

	if absf(strafe_input) > 0.01:
		var right: Vector3 = vehicle.global_transform.basis.x
		vehicle.apply_central_force(right * strafe_input * RCS_TRANSLATION_FORCE)


## Current thrust-to-weight ratio at the current throttle level.
## TWR = (total_thrust * throttle) / (mass * g).
func _get_twr(vehicle: Vehicle) -> float:
	var weight: float = vehicle.total_mass * GRAVITY
	if weight <= 0.0:
		return 0.0
	return (vehicle.total_thrust * throttle) / weight
