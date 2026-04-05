## StatusEffect -- Persistent damage-over-time (DOT) and debuff effects.
##
## Applied to vehicles by the DamageSystem. Each StatusEffect is a child node
## of the affected vehicle and ticks independently. When the duration expires,
## the effect removes itself.
##
## Supported effect types:
##   FIRE             - Deals damage to a random part each tick. Has a 20%
##                      chance to spread to an adjacent part. Common from fuel
##                      tank explosions.
##   FLOODING         - Increases the vehicle's effective mass each tick
##                      (simulates water filling the hull). Slows the vehicle
##                      down and eventually causes it to sink. Naval combat.
##   PRESSURE_BREACH  - Deals damage to ALL hull parts each tick. Submarine-
##                      only effect caused by depth charges or hull breaches.
##                      Very dangerous -- multiple breaches stack.
class_name StatusEffect
extends Node


# ---------------------------------------------------------------------------
# Enums
# ---------------------------------------------------------------------------

## The type of status effect. Determines tick behavior.
enum EffectType {
	FIRE,             ## Fire DOT -- damages random parts, can spread.
	FLOODING,         ## Water ingress -- increases mass over time.
	PRESSURE_BREACH,  ## Submarine hull breach -- damages all hull parts.
}


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## Which type of effect this is. Set via [method setup].
var effect_type: EffectType = EffectType.FIRE

## Total duration of the effect in seconds. After this time, the effect
## expires and removes itself.
var duration: float = 10.0

## Seconds between each damage/debuff tick.
var tick_rate: float = 1.0

## Time accumulated toward the next tick. Resets to 0.0 after each tick.
var tick_timer: float = 0.0

## Total time elapsed since the effect was applied. When this exceeds
## [member duration], the effect expires.
var elapsed: float = 0.0

## Damage dealt per tick. For FIRE and PRESSURE_BREACH, this is HP damage.
## For FLOODING, this is unused (flooding increases mass instead).
var damage_per_tick: int = 5

## Reference to the vehicle this effect is applied to. Stored so we can
## access its parts, mass, etc. without walking the tree every tick.
var vehicle: Node = null

## Master on/off flag. Set to false when the effect expires.
var is_active: bool = true


# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

## Initialize the status effect with its type, duration, and target.
##
## [param type]   - Which effect type (FIRE, FLOODING, PRESSURE_BREACH).
## [param dur]    - How long the effect lasts in seconds.
## [param dpt]    - Damage per tick (or mass increase rate for FLOODING).
## [param target] - The Vehicle this effect is applied to.
func setup(type: EffectType, dur: float, dpt: int, target: Node) -> void:
	effect_type = type
	duration = dur
	damage_per_tick = dpt
	vehicle = target
	is_active = true

	# Name the node descriptively for debugging in the scene tree.
	match effect_type:
		EffectType.FIRE:
			name = "StatusEffect_Fire"
		EffectType.FLOODING:
			name = "StatusEffect_Flooding"
		EffectType.PRESSURE_BREACH:
			name = "StatusEffect_PressureBreach"


# ---------------------------------------------------------------------------
# Processing
# ---------------------------------------------------------------------------

## Every frame, advance the tick timer and the total elapsed time.
## When the tick timer exceeds the tick rate, apply one tick of the effect.
## When the total elapsed time exceeds the duration, expire.
func _process(delta: float) -> void:
	if not is_active:
		return

	# If the vehicle is dead, the effect no longer matters.
	if vehicle == null or not vehicle.is_alive:
		_expire()
		return

	elapsed += delta
	tick_timer += delta

	# --- Tick check ---
	if tick_timer >= tick_rate:
		_apply_tick()
		tick_timer = 0.0

	# --- Duration check ---
	if elapsed >= duration:
		_expire()


# ---------------------------------------------------------------------------
# Tick logic
# ---------------------------------------------------------------------------

## Apply one tick of the status effect. The behavior depends on the type.
func _apply_tick() -> void:
	match effect_type:
		EffectType.FIRE:
			_tick_fire()
		EffectType.FLOODING:
			_tick_flooding()
		EffectType.PRESSURE_BREACH:
			_tick_pressure_breach()


## FIRE tick: damage a random alive part and potentially spread the fire.
func _tick_fire() -> void:
	var target_part: PartNode = _get_random_part()
	if target_part == null:
		return

	# Apply fire damage (bypasses armor -- fire doesn't care about plates).
	var damage_system: DamageSystem = _find_damage_system()
	if damage_system != null:
		damage_system.apply_damage(target_part, damage_per_tick, "fire", self)
	else:
		target_part.take_damage(damage_per_tick)

	# --- Fire spread ---
	# 20% chance each tick to spread fire to an adjacent part's cell,
	# causing a new, shorter fire effect on the same vehicle.
	if randf() < 0.20:
		var adjacent: PartNode = _get_adjacent_part(target_part)
		if adjacent != null and not adjacent.is_destroyed:
			# Spread creates a shorter-duration fire (half the remaining time).
			var remaining: float = duration - elapsed
			if remaining > 2.0 and damage_system != null:
				damage_system.apply_status_effect(vehicle, "fire", remaining * 0.5)
				print("[StatusEffect] Fire spread to %s!" % adjacent.name)


## FLOODING tick: increase the vehicle's mass to simulate water ingress.
## This makes the vehicle heavier, slower, and eventually unable to stay
## afloat (for water-domain vehicles).
func _tick_flooding() -> void:
	# Increase mass by 5% of the vehicle's base mass each tick.
	var mass_increase: float = vehicle.total_mass * 0.05
	vehicle.mass += mass_increase

	print("[StatusEffect] Flooding: vehicle mass increased by %.1f kg (now %.1f kg)." % [
		mass_increase, vehicle.mass
	])


## PRESSURE_BREACH tick: damage all hull/structural parts simultaneously.
## This is devastating -- a pressure breach on a submarine can rapidly
## destroy the entire hull.
func _tick_pressure_breach() -> void:
	# Find all alive parts and damage each one. In a real submarine, a hull
	# breach means pressure is crushing everything inside.
	var damage_system: DamageSystem = _find_damage_system()

	for cell_pos: Vector3i in vehicle.parts:
		var part: PartNode = vehicle.parts[cell_pos]
		if part.is_destroyed:
			continue

		# Only damage structural and hull parts (not weapons or utilities,
		# which would be protected inside the pressure hull).
		if part.part_data == null:
			continue
		var cat: String = part.part_data.category
		if cat == "chassis" or cat == "defense":
			if damage_system != null:
				damage_system.apply_damage(part, damage_per_tick, "pressure", self)
			else:
				part.take_damage(damage_per_tick)


# ---------------------------------------------------------------------------
# Expiration
# ---------------------------------------------------------------------------

## Deactivate the effect and remove it from the scene tree.
func _expire() -> void:
	is_active = false
	print("[StatusEffect] '%s' expired on vehicle." % name)
	queue_free()


# ---------------------------------------------------------------------------
# Utility -- Random part selection
# ---------------------------------------------------------------------------

## Pick a random alive (non-destroyed) part from the vehicle.
## Returns null if the vehicle has no alive parts.
func _get_random_part() -> PartNode:
	if vehicle == null:
		return null

	# Collect all unique, non-destroyed parts.
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

	# Pick one at random.
	return alive_parts[randi() % alive_parts.size()]


## Find a random alive part adjacent to the given part. Used for fire spread.
## Returns null if no adjacent alive parts exist.
func _get_adjacent_part(part: PartNode) -> PartNode:
	if vehicle == null or part == null:
		return null

	var pos: Vector3i = part.grid_position
	var neighbor_offsets: Array[Vector3i] = [
		Vector3i(1, 0, 0), Vector3i(-1, 0, 0),
		Vector3i(0, 1, 0), Vector3i(0, -1, 0),
		Vector3i(0, 0, 1), Vector3i(0, 0, -1),
	]

	# Shuffle the offsets so spread direction is random.
	neighbor_offsets.shuffle()

	for offset: Vector3i in neighbor_offsets:
		var neighbor_pos: Vector3i = pos + offset
		if vehicle.parts.has(neighbor_pos):
			var neighbor: PartNode = vehicle.parts[neighbor_pos]
			if not neighbor.is_destroyed:
				return neighbor

	return null


# ---------------------------------------------------------------------------
# Utility -- DamageSystem lookup
# ---------------------------------------------------------------------------

## Search the scene tree for the DamageSystem node.
func _find_damage_system() -> DamageSystem:
	var systems: Array[Node] = get_tree().get_nodes_in_group("damage_system")
	if not systems.is_empty():
		return systems[0] as DamageSystem

	var root: Node = get_tree().current_scene
	if root == null:
		return null
	for child: Node in root.get_children():
		if child is DamageSystem:
			return child as DamageSystem

	return null
