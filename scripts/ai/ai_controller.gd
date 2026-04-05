## AIController -- Combat AI state machine for driving vehicles in battle.
##
## Attach this as a child of a Vehicle node. The AI will take over movement
## and weapon firing, making the vehicle behave like an autonomous opponent.
##
## The AI uses a simple finite state machine (FSM) with these states:
##   IDLE     - No target. Vehicle sits still or patrols randomly.
##   SEEK     - Has a target but it's far away. Move toward it.
##   APPROACH - Getting closer. Try to reach engagement range.
##   ENGAGE   - Within weapon range. Face the target and shoot.
##   EVADE    - HP is low. Dodge and weave to survive.
##   RETREAT  - Critically damaged. Flee to the arena edge.
##
## Difficulty levels (easy/medium/hard) affect reaction time, aim accuracy,
## engagement distance, and whether the AI uses advanced tactics like lead
## prediction and domain-specific maneuvers.
##
## The AI generates an input dictionary (same format as Vehicle.get_input_vector())
## that the vehicle's physics controller consumes. This means AI vehicles move
## using the exact same physics as player vehicles -- no cheating.
class_name AIController
extends Node


# ---------------------------------------------------------------------------
# Enums
# ---------------------------------------------------------------------------

## Finite state machine states for the combat AI.
enum AIState {
	IDLE,      ## No target -- idle or patrol.
	SEEK,      ## Target exists but is far away -- move toward it.
	APPROACH,  ## Closing distance to reach engagement range.
	ENGAGE,    ## In range -- face target and fire weapons.
	EVADE,     ## Low health -- dodge and weave.
	RETREAT,   ## Critically low health -- flee.
}


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## The Vehicle node this AI controls. Set automatically if the AIController
## is added as a child of a Vehicle; otherwise set via [method setup].
var vehicle: Vehicle = null

## The enemy Vehicle this AI is currently targeting.
var target: Vehicle = null

## Current AI state. Transitions are handled in [method _update_state].
var current_state: AIState = AIState.IDLE

## Difficulty level string: "easy", "medium", or "hard". Affects all
## AI parameters (reaction time, accuracy, range, tactics).
var difficulty: String = "medium"


# ---------------------------------------------------------------------------
# Tunable parameters (set by difficulty in setup)
# ---------------------------------------------------------------------------

## Distance at which the AI begins engaging (firing weapons). Closer than
## this = ENGAGE state. Affected by difficulty.
var engage_range: float = 80.0

## Distance at which the AI starts actively closing the gap. Further than
## this = SEEK; between this and engage_range = APPROACH.
var approach_range: float = 150.0

## HP percentage (0.0 - 1.0) below which the AI switches to EVADE.
var evade_health_threshold: float = 0.3

## HP percentage below which the AI switches to RETREAT (desperate flee).
var retreat_health_threshold: float = 0.1

## Seconds between AI decision updates. Higher = dumber (reacts slower).
var reaction_time: float = 0.5

## How accurately the AI aims. 0.0 = wildly inaccurate, 1.0 = perfect aim.
## Determines the random spread added to the aim direction.
var aim_accuracy: float = 0.7

## For air-domain AI: preferred cruising altitude in meters.
var preferred_altitude: float = 100.0

## For submarine-domain AI: preferred cruising depth in meters (negative Y).
var preferred_depth: float = -50.0


# ---------------------------------------------------------------------------
# Private variables
# ---------------------------------------------------------------------------

## Timer that accumulates delta until it reaches reaction_time, at which
## point the AI makes a new decision. This simulates "thinking speed".
var _reaction_timer: float = 0.0

## Timer for the current state. Used for time-limited behaviors like
## evasion maneuvers.
var _state_timer: float = 0.0

## Randomized strafe direction for ENGAGE state. Periodically flipped to
## make the AI harder to predict.
var _strafe_direction: float = 1.0

## Timer that controls how often the AI changes strafe direction.
var _strafe_change_timer: float = 0.0

## The last known position of the target. Used in SEEK state when the target
## might be behind cover.
var _last_known_target_pos: Vector3 = Vector3.ZERO

## Cached input dictionary generated each decision cycle. The vehicle reads
## this instead of player input.
var _ai_input: Dictionary = {}


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

## Find the parent Vehicle if we're a direct child of one.
func _ready() -> void:
	# Auto-detect the parent vehicle.
	if get_parent() is Vehicle:
		vehicle = get_parent() as Vehicle


## Run the AI every physics frame. The reaction timer ensures the AI only
## makes new decisions at a rate determined by the difficulty level.
func _physics_process(delta: float) -> void:
	if vehicle == null or not vehicle.is_alive:
		return

	# --- Accumulate time until the next decision ---
	_reaction_timer += delta
	_state_timer += delta
	_strafe_change_timer += delta

	# Only make a new decision when the reaction timer fires.
	if _reaction_timer >= reaction_time:
		_reaction_timer = 0.0
		_update_state()
		# Compensate for skipped frames: multiply the effective delta by
		# how many reaction intervals elapsed. This prevents the AI from
		# moving in stuttery bursts.
		var effective_delta: float = reaction_time
		_execute_state(effective_delta)

	# --- Apply the cached input to the vehicle ---
	# The vehicle's physics controller will read this each frame.
	_apply_input(delta)


# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

## Configure the AI with a target vehicle and difficulty level.
##
## [param target_vehicle] - The enemy Vehicle to fight.
## [param diff]           - Difficulty: "easy", "medium", or "hard".
func setup(target_vehicle: Vehicle, diff: String) -> void:
	# If vehicle wasn't auto-detected, try the parent again.
	if vehicle == null and get_parent() is Vehicle:
		vehicle = get_parent() as Vehicle

	target = target_vehicle
	difficulty = diff

	# --- Adjust parameters by difficulty ---
	match difficulty:
		"easy":
			reaction_time = 1.0           # Slow thinker.
			aim_accuracy = 0.3            # Poor aim -- lots of spread.
			engage_range = 60.0           # Gets close before shooting.
			approach_range = 120.0
			evade_health_threshold = 0.2  # Doesn't evade until very hurt.
			retreat_health_threshold = 0.05
		"medium":
			reaction_time = 0.5
			aim_accuracy = 0.6
			engage_range = 80.0
			approach_range = 150.0
			evade_health_threshold = 0.3
			retreat_health_threshold = 0.1
		"hard":
			reaction_time = 0.2           # Very fast reactions.
			aim_accuracy = 0.9            # Near-perfect aim.
			engage_range = 120.0          # Engages from further away.
			approach_range = 200.0
			evade_health_threshold = 0.4  # Evades earlier (smart).
			retreat_health_threshold = 0.15
		_:
			push_warning("[AIController] Unknown difficulty '%s'. Using medium." % diff)

	print("[AIController] Setup: difficulty='%s', reaction=%.2fs, accuracy=%.1f" % [
		difficulty, reaction_time, aim_accuracy
	])


# ---------------------------------------------------------------------------
# State machine -- Transition logic
# ---------------------------------------------------------------------------

## Evaluate the current situation and transition to the appropriate state.
## Called once per reaction cycle.
func _update_state() -> void:
	var previous_state: AIState = current_state

	# --- No target? ---
	if target == null or not target.is_alive:
		# Try to find a new target.
		target = _find_new_target()
		if target == null:
			current_state = AIState.IDLE
			return

	# --- Health check ---
	var hp_pct: float = _get_hp_percent()
	if hp_pct < retreat_health_threshold:
		current_state = AIState.RETREAT
	elif hp_pct < evade_health_threshold:
		current_state = AIState.EVADE
	else:
		# --- Distance check ---
		var dist: float = vehicle.global_position.distance_to(target.global_position)
		if dist > approach_range:
			current_state = AIState.SEEK
		elif dist > engage_range:
			current_state = AIState.APPROACH
		else:
			current_state = AIState.ENGAGE

	# Update the last known target position.
	if target != null and is_instance_valid(target):
		_last_known_target_pos = target.global_position

	# Reset the state timer on transitions.
	if current_state != previous_state:
		_state_timer = 0.0


# ---------------------------------------------------------------------------
# State machine -- Execution logic
# ---------------------------------------------------------------------------

## Execute the current state's behavior. Generates the AI input dictionary
## that the vehicle's physics controller will consume.
##
## [param delta] - Effective time step (compensated for reaction time).
func _execute_state(delta: float) -> void:
	# Reset the input dictionary each decision cycle.
	_ai_input = _get_neutral_input()

	match current_state:
		AIState.IDLE:
			_execute_idle(delta)
		AIState.SEEK:
			_execute_seek(delta)
		AIState.APPROACH:
			_execute_approach(delta)
		AIState.ENGAGE:
			_execute_engage(delta)
		AIState.EVADE:
			_execute_evade(delta)
		AIState.RETREAT:
			_execute_retreat(delta)

	# Apply domain-specific adjustments (altitude for air, depth for sub, etc.).
	_domain_specific_behavior(delta)


## IDLE: No target. Stop moving and look for enemies.
func _execute_idle(_delta: float) -> void:
	# Do nothing -- neutral input means the vehicle coasts to a stop.
	pass


## SEEK: Target is far away. Move directly toward it.
func _execute_seek(_delta: float) -> void:
	if target == null:
		return

	# Calculate the direction to the target in the vehicle's local frame.
	var to_target: Vector3 = (target.global_position - vehicle.global_position).normalized()
	var local_dir: Vector3 = vehicle.global_transform.basis.inverse() * to_target

	# Full forward throttle.
	_ai_input["forward"] = 1.0

	# Steer left/right to face the target.
	_ai_input["yaw"] = clampf(local_dir.x * 2.0, -1.0, 1.0)

	# Pitch up/down toward the target (relevant for air/space).
	_ai_input["pitch"] = clampf(-local_dir.y * 2.0, -1.0, 1.0)


## APPROACH: Getting closer to engagement range. Slow down slightly and
## start pre-aiming.
func _execute_approach(_delta: float) -> void:
	if target == null:
		return

	var to_target: Vector3 = (target.global_position - vehicle.global_position).normalized()
	var local_dir: Vector3 = vehicle.global_transform.basis.inverse() * to_target

	# Moderate forward speed -- don't overshoot.
	_ai_input["forward"] = 0.7

	# Steer toward target.
	_ai_input["yaw"] = clampf(local_dir.x * 2.0, -1.0, 1.0)
	_ai_input["pitch"] = clampf(-local_dir.y * 2.0, -1.0, 1.0)

	# Start firing if roughly facing the target (within ~30 degrees).
	if local_dir.z < -0.85:  # Facing forward = negative Z
		_ai_input["fire"] = true


## ENGAGE: In weapon range. Face the target, fire, and strafe to dodge.
func _execute_engage(_delta: float) -> void:
	if target == null:
		return

	# --- Aim at the target ---
	var aim_dir: Vector3 = _aim_at_target()
	var local_aim: Vector3 = vehicle.global_transform.basis.inverse() * aim_dir

	# Steer to face the aim direction.
	_ai_input["yaw"] = clampf(local_aim.x * 3.0, -1.0, 1.0)
	_ai_input["pitch"] = clampf(-local_aim.y * 3.0, -1.0, 1.0)

	# Fire weapons!
	_ai_input["fire"] = true

	# --- Strafing ---
	# Periodically change strafe direction to be harder to hit.
	if _strafe_change_timer > 2.0:
		_strafe_change_timer = 0.0
		_strafe_direction = -_strafe_direction

	_ai_input["strafe"] = _strafe_direction * 0.5

	# Light forward/backward movement to maintain distance.
	var dist: float = vehicle.global_position.distance_to(target.global_position)
	if dist < engage_range * 0.5:
		# Too close -- back off slightly.
		_ai_input["forward"] = -0.3
	elif dist > engage_range * 0.8:
		# Drifting out of range -- push forward.
		_ai_input["forward"] = 0.4


## EVADE: Low health. Dodge and weave while still trying to fight back.
func _execute_evade(_delta: float) -> void:
	if target == null:
		return

	var to_target: Vector3 = (target.global_position - vehicle.global_position).normalized()
	var local_dir: Vector3 = vehicle.global_transform.basis.inverse() * to_target

	# Move perpendicular to the target to dodge incoming fire.
	_ai_input["strafe"] = _strafe_direction
	_ai_input["forward"] = 0.5

	# Change direction more frequently when evading.
	if _strafe_change_timer > 1.0:
		_strafe_change_timer = 0.0
		_strafe_direction = -_strafe_direction

	# Still try to face and shoot (wounded but not helpless).
	_ai_input["yaw"] = clampf(local_dir.x * 2.0, -1.0, 1.0)
	_ai_input["fire"] = true


## RETREAT: Critically damaged. Run away from the target.
func _execute_retreat(_delta: float) -> void:
	if target == null:
		return

	# Move directly away from the target.
	var away_from_target: Vector3 = (vehicle.global_position - target.global_position).normalized()
	var local_dir: Vector3 = vehicle.global_transform.basis.inverse() * away_from_target

	# Full throttle away.
	_ai_input["forward"] = 1.0
	_ai_input["yaw"] = clampf(local_dir.x * 2.0, -1.0, 1.0)


# ---------------------------------------------------------------------------
# Domain-specific behavior
# ---------------------------------------------------------------------------

## Apply adjustments to the AI input based on the vehicle's combat domain.
## Each domain has unique movement constraints that the AI needs to respect.
##
## [param delta] - Effective time step.
func _domain_specific_behavior(_delta: float) -> void:
	if vehicle == null:
		return

	match vehicle.domain:
		"air":
			_air_behavior()
		"submarine":
			_submarine_behavior()
		"water":
			_water_behavior()
		"space":
			_space_behavior()
		# "ground" has no special behavior -- default movement is fine.


## Air domain: maintain a preferred altitude. Don't fly into the ground or
## above the ceiling. Use altitude advantage in combat.
func _air_behavior() -> void:
	var altitude: float = vehicle.global_position.y

	# --- Altitude maintenance ---
	if altitude < preferred_altitude * 0.7:
		# Too low -- pitch up to gain altitude.
		_ai_input["pitch"] = clampf(_ai_input.get("pitch", 0.0) + 0.5, -1.0, 1.0)
		# Also request throttle up to climb.
		_ai_input["throttle_up"] = true
	elif altitude > preferred_altitude * 1.3:
		# Too high -- pitch down.
		_ai_input["pitch"] = clampf(_ai_input.get("pitch", 0.0) - 0.3, -1.0, 1.0)

	# --- Stall prevention ---
	# If the vehicle is moving too slowly, it could stall. Pitch down and
	# throttle up to regain speed.
	if vehicle.get_speed() < 20.0:
		_ai_input["forward"] = 1.0
		_ai_input["pitch"] = clampf(_ai_input.get("pitch", 0.0) - 0.3, -1.0, 1.0)


## Submarine domain: stay at the preferred depth. Use depth to avoid
## detection, surface to use weapons.
func _submarine_behavior() -> void:
	var depth: float = vehicle.global_position.y

	# Dive to preferred depth during SEEK/APPROACH (stealth).
	if current_state == AIState.SEEK or current_state == AIState.APPROACH:
		if depth > preferred_depth:
			_ai_input["dive"] = true
		elif depth < preferred_depth * 1.2:
			_ai_input["surface"] = true

	# Surface slightly during ENGAGE to use weapons effectively.
	if current_state == AIState.ENGAGE:
		if depth < preferred_depth * 0.5:
			_ai_input["surface"] = true


## Water domain: try to present a broadside to the target (like historical
## naval combat) so more weapons can fire.
func _water_behavior() -> void:
	if target == null or current_state != AIState.ENGAGE:
		return

	# In ENGAGE, add some yaw to turn broadside to the target.
	# This is a simplified version -- real broadside combat is more nuanced.
	var to_target: Vector3 = (target.global_position - vehicle.global_position).normalized()
	var right: Vector3 = vehicle.global_transform.basis.x
	var dot: float = right.dot(to_target)

	# If the target is roughly perpendicular (broadside), maintain heading.
	# If not, turn to present the broadside.
	if absf(dot) < 0.7:
		_ai_input["yaw"] = signf(dot) * 0.3


## Space domain: use RCS strafing and maintain distance. No air resistance
## so movement is very different.
func _space_behavior() -> void:
	# In space, strafing is very effective because there's no friction.
	# Increase strafe intensity.
	if current_state == AIState.ENGAGE or current_state == AIState.EVADE:
		_ai_input["strafe"] = _ai_input.get("strafe", 0.0) * 1.5
		_ai_input["strafe"] = clampf(_ai_input["strafe"], -1.0, 1.0)


# ---------------------------------------------------------------------------
# Aiming
# ---------------------------------------------------------------------------

## Calculate the aim direction toward the target with accuracy-based spread.
##
## For hard difficulty, this includes lead prediction (aiming ahead of a
## moving target to compensate for projectile travel time).
##
## Returns a normalized world-space direction vector.
func _aim_at_target() -> Vector3:
	if target == null or not is_instance_valid(target):
		return vehicle.get_forward_direction()

	# Base aim direction: straight at the target.
	var aim_point: Vector3 = target.global_position
	var to_target: Vector3 = aim_point - vehicle.global_position

	# --- Lead prediction (hard difficulty only) ---
	# Estimate where the target will be when the projectile arrives.
	if difficulty == "hard" and target is Vehicle:
		var target_vehicle: Vehicle = target as Vehicle
		var dist: float = to_target.length()
		# Assume projectile speed of ~100 m/s for lead calculation.
		var projectile_speed: float = 100.0
		var travel_time: float = dist / projectile_speed
		# Add the target's velocity * travel time to predict its future position.
		var predicted_pos: Vector3 = aim_point + target_vehicle.linear_velocity * travel_time
		to_target = predicted_pos - vehicle.global_position

	var aim_dir: Vector3 = to_target.normalized()

	# --- Accuracy spread ---
	# Add a random offset scaled by (1 - aim_accuracy). At aim_accuracy = 1.0,
	# there is no spread. At 0.0, the spread is very large.
	var spread: float = (1.0 - aim_accuracy) * 0.15  # Max ~8.5 degrees at 0 accuracy.
	aim_dir.x += randf_range(-spread, spread)
	aim_dir.y += randf_range(-spread, spread)
	aim_dir.z += randf_range(-spread, spread)

	return aim_dir.normalized()


# ---------------------------------------------------------------------------
# Target finding
# ---------------------------------------------------------------------------

## Scan for a new target when the current one is dead or null.
## Returns the nearest alive enemy Vehicle, or null if none found.
func _find_new_target() -> Vehicle:
	var best: Vehicle = null
	var best_dist: float = INF

	var all_vehicles: Array[Node] = get_tree().get_nodes_in_group("vehicles")

	for node: Node in all_vehicles:
		if not node is Vehicle:
			continue
		var v: Vehicle = node as Vehicle

		# Skip self, dead vehicles, and allies (for future team modes).
		if v == vehicle:
			continue
		if not v.is_alive:
			continue

		var dist: float = vehicle.global_position.distance_to(v.global_position)
		if dist < best_dist:
			best_dist = dist
			best = v

	return best


# ---------------------------------------------------------------------------
# HP calculation
# ---------------------------------------------------------------------------

## Calculate the vehicle's current health as a percentage (0.0 - 1.0).
## Sums up current HP of all alive parts divided by max HP of all parts.
func _get_hp_percent() -> float:
	if vehicle == null:
		return 0.0

	var current_total: float = 0.0
	var max_total: float = 0.0
	var seen: Dictionary = {}

	for cell_pos: Vector3i in vehicle.parts:
		var part: PartNode = vehicle.parts[cell_pos]
		var nid: int = part.get_instance_id()
		if seen.has(nid):
			continue
		seen[nid] = true

		if part.part_data != null:
			max_total += float(part.part_data.hp)
			current_total += float(part.current_hp)

	if max_total <= 0.0:
		return 0.0
	return current_total / max_total


# ---------------------------------------------------------------------------
# Input generation
# ---------------------------------------------------------------------------

## Return a neutral (no-input) dictionary matching the format of
## Vehicle.get_input_vector(). All axes at 0, all buttons false.
func _get_neutral_input() -> Dictionary:
	return {
		"forward": 0.0,
		"strafe": 0.0,
		"pitch": 0.0,
		"yaw": 0.0,
		"roll": 0.0,
		"throttle_up": false,
		"throttle_down": false,
		"fire": false,
		"dive": false,
		"surface": false,
	}


## Apply the cached AI input to the vehicle. This is called every physics
## frame (not just on reaction ticks) so movement is smooth.
##
## [param delta] - Frame time in seconds.
func _apply_input(delta: float) -> void:
	if vehicle == null:
		return

	# --- Movement: feed the AI input to the physics controller ---
	if vehicle.physics_controller != null:
		# Override the vehicle's input source with our AI-generated input.
		vehicle.physics_controller.apply_forces_with_input(vehicle, _ai_input, delta)

	# --- Weapons: fire when the AI decides to ---
	if _ai_input.get("fire", false):
		vehicle.fire_weapons(delta)
