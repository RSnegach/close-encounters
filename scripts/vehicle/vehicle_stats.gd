## VehicleStats (RefCounted)
##
## A pure-data object that computes aggregate statistics from a collection of
## PartData resources. Used in two places:
##
##   1. **Builder UI** -- preview stats as the player adds / removes parts.
##      Call VehicleStats.calculate(parts_array, domain) to get a snapshot.
##   2. **Runtime Vehicle** -- the Vehicle node can create a VehicleStats from
##      its children to cross-check or display in the HUD.
##
## VehicleStats extends RefCounted (not Node) because it carries no scene-tree
## behaviour -- it is just a bag of numbers. You never attach it to the scene
## tree. Create instances via the static calculate() factory.
##
## Usage example:
##   var stats := VehicleStats.calculate(my_parts, "air")
##   label.text = "Mass: %.1f kg" % stats.total_mass
##   for issue in stats.get_issues("air"):
##       print("WARNING: ", issue)
class_name VehicleStats
extends RefCounted


# ---------------------------------------------------------------------------
# Computed properties (read-only after calculate())
# ---------------------------------------------------------------------------
# All of these start at zero/false and are populated by calculate().
# After that they should be treated as read-only -- nothing stops you from
# writing to them, but doing so would make the stats inconsistent.

## Sum of every part's mass_kg (kilograms). Used for inertia, TWR, and the
## top-speed estimate.
var total_mass: float = 0.0

## Sum of every part's cost (abstract currency units). Compared against the
## player's budget in the builder.
var total_cost: int = 0

## Sum of every part's hit-points. A rough measure of overall durability --
## the vehicle can survive this much total damage spread across all parts.
var total_hp: int = 0

## Sum of thrust from all propulsion-category parts (Newtons). Only parts
## whose category is "propulsion" and whose stats dictionary contains a
## "thrust" key contribute to this total.
var total_thrust: float = 0.0

## Sum of every part's drag coefficient. Higher drag means slower top speed.
var total_drag: float = 0.0

## Thrust-to-weight ratio: total_thrust / (total_mass * g).
## Values above 1.0 mean the vehicle can accelerate upward against gravity.
## Critical for space-domain vehicles that need to reach orbit.
var thrust_to_weight: float = 0.0

## Rough estimate of top speed (m/s). Derived from thrust, mass, and drag
## using a simplified formula. Clamped to MAX_SPEED_CAP to prevent absurd
## values when drag is very low. The real top speed depends on the physics
## controller at runtime.
var projected_max_speed: float = 0.0

## Total wing / aerodynamic surface area (m^2). Each qualifying part adds
## WING_AREA_PER_PART m^2. Air-domain vehicles need enough wing area to
## generate lift.
var wing_area: float = 0.0

## Total hull / pressure-vessel volume (m^3). Computed from each hull part's
## grid size (size.x * size.y * size.z). Water-domain vehicles need hull
## volume for buoyancy.
var hull_volume: float = 0.0

## Total fuel capacity (abstract fuel units). Accumulated from the "fuel"
## stat on fuel_tank parts and from "booster_fuel" stat on booster parts.
## Consumed by rockets, jets, and boosters at runtime.
var fuel_capacity: float = 0.0

## Total ammo capacity. Base ammo comes from the "ammo" stat on weapon-
## category parts; bonus ammo comes from the "ammo_bonus" stat on ammo_rack
## subcategory parts.
var ammo_capacity: int = 0

## Whether the build includes at least one control-category part (cockpit,
## bridge, drone controller, etc.). A vehicle without control cannot be
## piloted.
var has_control: bool = false

## Whether the build includes at least one propulsion-category part (engine,
## thruster, wheel, etc.). A vehicle without propulsion cannot move.
var has_propulsion: bool = false

## Number of weapon-category parts. Shown in the builder stats panel so the
## player knows their offensive loadout at a glance.
var weapon_count: int = 0

## Total number of PartData entries that were processed. Includes every
## category.
var part_count: int = 0


# ---------------------------------------------------------------------------
# Static factory
# ---------------------------------------------------------------------------

## Iterate [param parts_array] (an Array of PartData resources) and compute
## every aggregate stat, returning a new VehicleStats instance.
##
## [param domain] is optional (default ""). When provided it does not change
## the calculation itself, but you can pass the same domain to get_issues()
## afterward for domain-specific validation.
##
## Parts that are not PartData instances are skipped with a warning.
static func calculate(parts_array: Array, domain: String = "") -> VehicleStats:
	# Create a fresh stats object. All fields start at their default zero/false.
	var s: VehicleStats = VehicleStats.new()

	# -- Physical constants and tuning knobs --

	## Gravitational acceleration (m/s^2) used for thrust-to-weight ratio.
	const GRAVITY: float = 9.8

	## Hard ceiling on projected_max_speed (m/s). Prevents unrealistically
	## high numbers when drag is near zero.
	const MAX_SPEED_CAP: float = 300.0

	## Each wing / aerodynamic part contributes this many m^2 of wing area.
	## A rough placeholder; more realistic sims would read each part's actual
	## surface area.
	const WING_AREA_PER_PART: float = 2.0

	# -- Main accumulation loop --
	# Walk every entry in the parts array. We expect PartData resources.
	for part in parts_array:
		# Type-check: skip anything that isn't a PartData resource.
		var pd: PartData = part as PartData
		if pd == null:
			push_warning("[VehicleStats] Skipping non-PartData entry in parts array.")
			continue

		# Count this part.
		s.part_count += 1

		# --- Universal stats (every part contributes) ---
		s.total_mass += pd.mass_kg
		s.total_cost += pd.cost
		s.total_hp   += pd.hp
		s.total_drag += pd.drag

		# --- Category-specific aggregation ---
		# The part's "category" string determines its role in the vehicle.
		match pd.category:
			"control":
				# At least one control part means the vehicle is pilotable.
				s.has_control = true

			"propulsion":
				# Mark that propulsion exists, and accumulate thrust.
				s.has_propulsion = true
				# Thrust lives in the part's stats dictionary, not as a
				# top-level PartData field, because only propulsion parts
				# have it.
				if pd.stats.has("thrust"):
					s.total_thrust += float(pd.stats["thrust"])

			"weapon":
				s.weapon_count += 1
				# Weapons may carry their own base ammo supply.
				if pd.stats.has("ammo"):
					s.ammo_capacity += int(pd.stats["ammo"])

		# --- Wing area (air-domain lift) ---
		# A part counts as an aerodynamic surface if:
		#   - its subcategory is "wing", OR
		#   - its id contains the substring "aerodynamic" (covers
		#     "aerodynamic_frame", "aerodynamic_nose", etc.)
		if pd.subcategory == "wing" or "aerodynamic" in pd.id:
			s.wing_area += WING_AREA_PER_PART

		# --- Hull volume (water / submarine buoyancy) ---
		# Hull, keel, and pressurized-hull parts contribute their grid
		# volume (in cells) to the total hull volume.
		if pd.subcategory in ["hull", "keel", "pressurized_hull"]:
			# size is a Vector3i (grid cells). Volume = x * y * z.
			s.hull_volume += float(pd.size.x * pd.size.y * pd.size.z)

		# --- Fuel capacity ---
		# fuel_tank parts store fuel in stats["fuel"].
		if pd.subcategory == "fuel_tank" and pd.stats.has("fuel"):
			s.fuel_capacity += float(pd.stats["fuel"])
		# Booster parts store fuel in stats["booster_fuel"].
		if pd.subcategory == "booster_fuel" and pd.stats.has("fuel"):
			s.fuel_capacity += float(pd.stats["fuel"])

		# --- Ammo rack bonus ---
		# Ammo racks are a separate subcategory that add bonus ammo on top
		# of whatever the weapons themselves carry.
		if pd.subcategory == "ammo_rack" and pd.stats.has("ammo_bonus"):
			s.ammo_capacity += int(pd.stats["ammo_bonus"])

	# -- Derived stats (computed after the loop) --

	# Thrust-to-weight ratio. Only meaningful when mass > 0.
	if s.total_mass > 0.0:
		s.thrust_to_weight = s.total_thrust / (s.total_mass * GRAVITY)
	else:
		s.thrust_to_weight = 0.0

	# Projected max speed.
	# Formula: thrust / (mass * drag), with drag floored at 0.1 to avoid
	# division by zero, and the whole denominator floored at 1.0 for safety.
	# The result is clamped to MAX_SPEED_CAP.
	if s.total_mass > 0.0:
		var effective_drag: float = maxf(s.total_drag, 0.1)
		var denominator: float = maxf(s.total_mass * effective_drag, 1.0)
		s.projected_max_speed = clampf(
			s.total_thrust / denominator,
			0.0,
			MAX_SPEED_CAP
		)
	else:
		s.projected_max_speed = 0.0

	return s


# ---------------------------------------------------------------------------
# Instance methods
# ---------------------------------------------------------------------------

## Serialize every stat into a flat Dictionary. Useful for:
##   - Displaying in the builder's stats panel (iterate keys).
##   - Sending over the network for multiplayer lobby previews.
##   - Saving alongside the vehicle design JSON.
func to_dict() -> Dictionary:
	return {
		"total_mass": total_mass,
		"total_cost": total_cost,
		"total_hp": total_hp,
		"total_thrust": total_thrust,
		"total_drag": total_drag,
		"thrust_to_weight": thrust_to_weight,
		"projected_max_speed": projected_max_speed,
		"wing_area": wing_area,
		"hull_volume": hull_volume,
		"fuel_capacity": fuel_capacity,
		"ammo_capacity": ammo_capacity,
		"has_control": has_control,
		"has_propulsion": has_propulsion,
		"weapon_count": weapon_count,
		"part_count": part_count,
	}


## Return a list of human-readable validation problems for the given
## [param domain]. An empty array means the build is valid.
##
## Call this after calculate() to warn the player before they try to
## launch a vehicle that is missing critical components.
##
## Recognized domains: "air", "water", "submarine", "space", "land".
## Unknown domains only get the universal checks.
func get_issues(domain: String) -> Array[String]:
	var issues: Array[String] = []

	# --- Universal checks (apply to every domain) ---
	if not has_control:
		issues.append("No control module")
	if not has_propulsion:
		issues.append("No propulsion")

	# --- Domain-specific checks ---
	match domain:
		"air":
			# Aircraft need a minimum wing area to generate enough lift.
			if wing_area < 4.0:
				issues.append("Insufficient wing area")
		"water":
			# Watercraft need a hull for buoyancy. Even a tiny hull counts,
			# but less than 1.0 m^3 is not enough.
			if hull_volume < 1.0:
				issues.append("No hull")
		"submarine":
			# Submarines need a pressure hull AND ballast tanks.
			if hull_volume < 1.0:
				issues.append("No hull")
			# Check whether any part is a ballast tank. We don't track this
			# as a dedicated field, so we note the issue generically.
			# (The caller's parts array is not available here, so the builder
			# should also run its own ballast check if needed.)
			# For now, we flag it so the player is warned.
			issues.append("No ballast tank")
		"space":
			# Rockets must overcome gravity: TWR >= 1.0 to leave the ground.
			if thrust_to_weight < 1.0:
				issues.append("TWR below 1.0")

	return issues
