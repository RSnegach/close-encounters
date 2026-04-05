## DamageSystem -- Centralized damage processing system.
##
## Handles all damage application in the game: direct hits, area-of-effect
## blasts, armor reduction, chain reactions, and status effects. Every piece
## of damage in the game should flow through this node so that signals are
## emitted consistently and combat logic stays in one place.
##
## Add this as a child of the arena root (ArenaManager creates it
## automatically). Other systems (projectiles, explosions, hazards) call its
## public methods to deal damage.
##
## Why a centralized system?
##   - Single place to hook damage logging, hit markers, score tracking.
##   - Armor, shields, and resistances are calculated in one spot.
##   - Chain reactions (fuel fires, ammo cook-offs) are easy to manage.
class_name DamageSystem
extends Node


# ---------------------------------------------------------------------------
# Signals
# ---------------------------------------------------------------------------

## Emitted every time damage is applied to a part. Useful for hit markers,
## floating damage numbers, or combat logs.
## [param target_part] - the PartNode that was hit.
## [param damage] - the final damage amount after armor reduction.
## [param source] - the node that caused the damage (projectile, hazard, etc.).
signal damage_dealt(target_part: PartNode, damage: int, source: Node)

## Emitted when a vehicle's control module is destroyed, killing the vehicle.
## [param vehicle] - the Vehicle that just died.
signal vehicle_killed(vehicle: Node)

## Emitted when a destroyed part triggers a secondary explosion (fuel or ammo).
## [param position] - world-space center of the explosion.
## [param radius] - blast radius in meters.
## [param damage] - damage dealt at the epicenter.
signal chain_reaction(position: Vector3, radius: float, damage: int)


# ---------------------------------------------------------------------------
# Public methods -- Direct damage
# ---------------------------------------------------------------------------

## Apply damage to a specific part on a vehicle.
##
## This is the main entry point for all single-target damage. It:
##   1. Finds adjacent armor and reduces damage accordingly.
##   2. Applies the remaining damage to the target part.
##   3. Checks for chain reactions if the part was destroyed.
##   4. Emits [signal damage_dealt] for UI / logging.
##
## [param target_part]  - The PartNode to damage.
## [param damage]       - Base damage amount before armor reduction.
## [param damage_type]  - Category of damage. Currently "kinetic" (reduced by
##                         armor) or "fire" (ignores armor). More types can be
##                         added later.
## [param source]       - The node that caused the damage (for signal metadata).
func apply_damage(
	target_part: PartNode,
	damage: int,
	damage_type: String = "kinetic",
	source: Node = null
) -> void:
	if target_part == null:
		push_warning("[DamageSystem] apply_damage() called with null target_part.")
		return
	if target_part.is_destroyed:
		# Already dead -- no point processing further damage.
		return

	# --- Step 1: Find the vehicle that owns this part ---
	var vehicle: Node = _get_vehicle_from_part(target_part)

	# --- Step 2: Calculate armor reduction ---
	var effective_damage: int = damage
	if damage_type == "kinetic" and vehicle != null:
		# Armor only reduces kinetic damage; fire/pressure bypass it.
		var armor_reduction: float = _find_adjacent_armor(target_part, vehicle)
		effective_damage = maxi(1, damage - int(armor_reduction))
		# Always deal at least 1 damage so hits are never completely wasted.

	# --- Step 3: Apply the damage to the part ---
	var was_destroyed: bool = target_part.take_damage(effective_damage)

	# --- Step 4: Emit the signal so UI can react ---
	damage_dealt.emit(target_part, effective_damage, source)

	# --- Step 5: If the part was destroyed, check for chain reactions ---
	if was_destroyed:
		_check_chain_reaction(target_part)

		# Also check if this kill destroyed the vehicle's control module.
		if vehicle != null:
			if check_vehicle_death(vehicle):
				vehicle.die()
				vehicle_killed.emit(vehicle)


# ---------------------------------------------------------------------------
# Public methods -- Area damage
# ---------------------------------------------------------------------------

## Apply damage to all vehicles within a sphere. Damage falls off linearly
## with distance from the center.
##
## [param center]         - World-space position of the blast epicenter.
## [param radius]         - Blast radius in meters.
## [param damage]         - Damage at the epicenter (falls off to 0 at edge).
## [param source_vehicle] - The vehicle that caused this blast (immune to its
##                          own area damage to prevent self-kills from rockets).
func apply_area_damage(
	center: Vector3,
	radius: float,
	damage: int,
	source_vehicle: Node = null
) -> void:
	if radius <= 0.0:
		push_warning("[DamageSystem] apply_area_damage() called with radius <= 0.")
		return

	# Find every vehicle currently in the match.
	var all_vehicles: Array[Node] = get_tree().get_nodes_in_group("vehicles")

	for node: Node in all_vehicles:
		if not node is RigidBody3D:
			continue
		var vehicle: Node = node as Node

		# Skip the vehicle that caused the explosion (no self-damage).
		if vehicle == source_vehicle:
			continue
		if not vehicle.is_alive:
			continue

		# Find the closest alive part on this vehicle to the blast center.
		var closest_part: PartNode = _find_closest_part(vehicle, center)
		if closest_part == null:
			continue

		# Calculate distance and apply linear falloff.
		var dist: float = closest_part.global_position.distance_to(center)
		if dist > radius:
			continue  # Out of range.

		# Falloff: full damage at center, zero at the edge.
		var falloff: float = 1.0 - (dist / radius)
		var final_damage: int = maxi(1, int(damage * falloff))

		# Apply through the normal damage pipeline (armor, chain reaction, etc.).
		apply_damage(closest_part, final_damage, "kinetic", null)


# ---------------------------------------------------------------------------
# Public methods -- Status effects
# ---------------------------------------------------------------------------

## Create and attach a persistent status effect to a vehicle.
##
## Status effects are child nodes of the vehicle that tick over time,
## applying damage or other penalties each tick. See [StatusEffect] for
## the full list of supported types.
##
## [param vehicle]     - The vehicle to afflict.
## [param effect_type] - One of: "fire", "flooding", "pressure_breach".
## [param duration]    - How long the effect lasts in seconds.
func apply_status_effect(
	vehicle: Node,
	effect_type: String,
	duration: float
) -> void:
	if vehicle == null or not vehicle.is_alive:
		return

	# Create a new StatusEffect node and configure it.
	var effect: StatusEffect = StatusEffect.new()
	effect.name = "StatusEffect_" + effect_type

	# Map the string type to the enum value.
	var type_enum: StatusEffect.EffectType
	match effect_type:
		"fire":
			type_enum = StatusEffect.EffectType.FIRE
		"flooding":
			type_enum = StatusEffect.EffectType.FLOODING
		"pressure_breach":
			type_enum = StatusEffect.EffectType.PRESSURE_BREACH
		_:
			push_warning("[DamageSystem] Unknown status effect type '%s'." % effect_type)
			effect.queue_free()
			return

	# Determine damage-per-tick based on type.
	var dpt: int = 5
	match type_enum:
		StatusEffect.EffectType.FIRE:
			dpt = 5
		StatusEffect.EffectType.FLOODING:
			dpt = 3
		StatusEffect.EffectType.PRESSURE_BREACH:
			dpt = 8

	# Add the effect as a child of the vehicle so it processes alongside it.
	vehicle.add_child(effect)
	effect.setup(type_enum, duration, dpt, vehicle)

	print("[DamageSystem] Applied '%s' to vehicle for %.1fs." % [effect_type, duration])


# ---------------------------------------------------------------------------
# Public methods -- Death check
# ---------------------------------------------------------------------------

## Returns true if the vehicle should be considered dead.
## A vehicle dies when its control module (cockpit / bridge / guidance
## computer) is destroyed.
func check_vehicle_death(vehicle: Node) -> bool:
	if vehicle == null:
		return false
	if vehicle.control_module == null:
		# No control module assigned -- treat as dead.
		return true
	return vehicle.control_module.is_destroyed


# ---------------------------------------------------------------------------
# Private helpers -- Chain reactions
# ---------------------------------------------------------------------------

## Check whether a destroyed part should trigger a secondary explosion.
##
## Parts with a "fuel" stat cause a fire and a small explosion.
## Parts with an "ammo_bonus" stat cause a larger explosion (ammo cook-off).
func _check_chain_reaction(destroyed_part: PartNode) -> void:
	if destroyed_part.part_data == null:
		return

	var stats: Dictionary = destroyed_part.part_data.stats
	var part_pos: Vector3 = destroyed_part.global_position

	# --- Fuel explosion ---
	# Fuel parts catch fire and create a small blast. The explosion radius
	# scales with how much fuel was in the tank.
	if stats.has("fuel"):
		var fuel_amount: float = float(stats["fuel"])
		var explosion_radius: float = fuel_amount * 0.5
		var explosion_damage: int = int(fuel_amount)

		print("[DamageSystem] Fuel explosion at %s, radius=%.1f, damage=%d" % [
			part_pos, explosion_radius, explosion_damage
		])

		# Apply the blast to nearby vehicles.
		var vehicle: Node = _get_vehicle_from_part(destroyed_part)
		apply_area_damage(part_pos, explosion_radius, explosion_damage, vehicle)

		# Set the parent vehicle on fire.
		if vehicle != null:
			apply_status_effect(vehicle, "fire", 5.0)

		chain_reaction.emit(part_pos, explosion_radius, explosion_damage)

	# --- Ammo cook-off ---
	# Ammo parts detonate violently when destroyed.
	if stats.has("ammo_bonus"):
		var ammo_value: float = float(stats["ammo_bonus"])
		var explosion_radius: float = 3.0
		var explosion_damage: int = int(ammo_value * 2.0)

		print("[DamageSystem] Ammo explosion at %s, radius=%.1f, damage=%d" % [
			part_pos, explosion_radius, explosion_damage
		])

		var vehicle: Node = _get_vehicle_from_part(destroyed_part)
		apply_area_damage(part_pos, explosion_radius, explosion_damage, vehicle)

		chain_reaction.emit(part_pos, explosion_radius, explosion_damage)


# ---------------------------------------------------------------------------
# Private helpers -- Armor
# ---------------------------------------------------------------------------

## Find armor parts adjacent to the target part and sum their armor values.
##
## "Adjacent" means parts occupying one of the six orthogonal neighbor cells
## (up, down, left, right, front, back) relative to the target's grid
## position. Only parts in the "defense" category with an "armor_value" stat
## contribute to the reduction.
##
## Returns the total armor reduction as a float.
func _find_adjacent_armor(part: PartNode, vehicle: Node) -> float:
	var total_armor: float = 0.0
	var pos: Vector3i = part.grid_position

	# The six orthogonal directions in grid space.
	var neighbor_offsets: Array[Vector3i] = [
		Vector3i(1, 0, 0),   # right
		Vector3i(-1, 0, 0),  # left
		Vector3i(0, 1, 0),   # above
		Vector3i(0, -1, 0),  # below
		Vector3i(0, 0, 1),   # front
		Vector3i(0, 0, -1),  # back
	]

	for offset: Vector3i in neighbor_offsets:
		var neighbor_pos: Vector3i = pos + offset

		# Check if there is a part at this neighboring cell.
		if not vehicle.parts.has(neighbor_pos):
			continue

		var neighbor: PartNode = vehicle.parts[neighbor_pos]

		# Only count defense-category parts that have an armor_value stat.
		if neighbor.is_destroyed:
			continue
		if neighbor.part_data == null:
			continue
		if neighbor.part_data.category != "defense":
			continue
		if not neighbor.part_data.stats.has("armor_value"):
			continue

		total_armor += float(neighbor.part_data.stats["armor_value"])

	return total_armor


# ---------------------------------------------------------------------------
# Private helpers -- Vehicle lookup
# ---------------------------------------------------------------------------

## Walk up the scene tree from a PartNode to find its parent Vehicle.
## Returns null if the part is not a child of a Vehicle.
func _get_vehicle_from_part(part: PartNode) -> Node:
	var parent: Node = part.get_parent()
	while parent != null:
		if parent is RigidBody3D:
			return parent as Node
		parent = parent.get_parent()
	return null


## Find the closest alive (non-destroyed) part on a vehicle to a world
## position. Used by area damage to determine which part takes the hit.
func _find_closest_part(vehicle: Node, world_pos: Vector3) -> PartNode:
	var closest: PartNode = null
	var closest_dist: float = INF

	# Use a set to avoid checking multi-cell parts multiple times.
	var seen: Dictionary = {}

	for cell_pos: Vector3i in vehicle.parts:
		var part: PartNode = vehicle.parts[cell_pos]
		var nid: int = part.get_instance_id()
		if seen.has(nid):
			continue
		seen[nid] = true

		if part.is_destroyed:
			continue

		var dist: float = part.global_position.distance_to(world_pos)
		if dist < closest_dist:
			closest_dist = dist
			closest = part

	return closest
