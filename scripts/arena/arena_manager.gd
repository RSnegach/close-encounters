## ArenaManager -- Manages a combat arena instance.
##
## This is the root node of a combat scene. It handles:
##   - Spawning player and AI vehicles at designated spawn points.
##   - Tracking which vehicles are alive and detecting win conditions.
##   - Managing the match timer (matches have a time limit).
##   - Creating and owning the centralized DamageSystem.
##   - Coordinating match start/end with the GameManager autoload.
##
## Arena scene setup:
##   The arena scene should have this script on its root Node3D.
##   Add child Node3D markers named "SpawnPoint0", "SpawnPoint1", etc.
##   to define where vehicles appear. The ArenaManager finds these
##   automatically in _ready().
##
## Match flow:
##   1. Scene loads. _ready() finds spawn points and creates the DamageSystem.
##   2. External code calls setup_match() with vehicle data for all players.
##   3. External code calls start_match() to begin combat.
##   4. ArenaManager checks win conditions every physics frame.
##   5. When one vehicle remains (or time runs out), _end_match() is called.
class_name ArenaManager
extends Node3D


# ---------------------------------------------------------------------------
# Signals
# ---------------------------------------------------------------------------

## Emitted when the match officially begins.
signal match_started

## Emitted when the match ends. [param winner_id] is the winning player's
## network peer ID, or -1 for a draw.
signal match_ended(winner_id: int)

## Emitted when a vehicle is eliminated during the match. Useful for
## kill-feed UI, announcements, etc.
signal vehicle_eliminated(vehicle: Vehicle)


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## Scene paths for each domain's arena. These are loaded by the main menu
## or lobby when transitioning to combat.
const ARENA_SCENES: Dictionary = {
	"ground": "res://scenes/arenas/arena_ground.tscn",
	"air": "res://scenes/arenas/arena_air.tscn",
	"water": "res://scenes/arenas/arena_water.tscn",
	"submarine": "res://scenes/arenas/arena_submarine.tscn",
	"space": "res://scenes/arenas/arena_space.tscn",
}


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## Positions where vehicles spawn at the start of a match. Auto-populated
## from child nodes named "SpawnPoint*".
var spawn_points: Array[Vector3] = []

## All vehicles currently in the match (alive and dead).
var vehicles: Array[Vehicle] = []

## Whether the match is currently active. False before start_match() and
## after _end_match().
var is_match_active: bool = false

## Elapsed time since the match started, in seconds.
var match_timer: float = 0.0

## Maximum match duration in seconds. When this is reached, the match ends
## and the winner is determined by remaining HP.
var max_match_time: float = 300.0  # 5 minutes.

## The combat domain for this match.
var domain: String = ""

## The centralized damage system. Created in _ready() and used by all
## combat entities (projectiles, explosions, hazards, etc.).
var damage_system: DamageSystem = null

## Optional arena hazards manager. Created during setup based on domain.
var hazards: ArenaHazards = null


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

## Find spawn point markers, create the DamageSystem, and connect to
## GameManager signals.
func _ready() -> void:
	# --- Find spawn points ---
	# Scan children for nodes whose names start with "SpawnPoint".
	for child: Node in get_children():
		if child.name.begins_with("SpawnPoint") and child is Node3D:
			spawn_points.append((child as Node3D).global_position)

	# If no spawn points were found, create default ones.
	if spawn_points.is_empty():
		push_warning(
			"[ArenaManager] No SpawnPoint* children found. "
			+ "Using default spawn positions."
		)
		spawn_points.append(Vector3(-20.0, 0.0, 0.0))
		spawn_points.append(Vector3(20.0, 0.0, 0.0))

	# --- Create the DamageSystem ---
	damage_system = DamageSystem.new()
	damage_system.name = "DamageSystem"
	# Add to a group so projectiles/explosions can find it easily.
	damage_system.add_to_group("damage_system")
	add_child(damage_system)

	# Connect DamageSystem signals.
	damage_system.vehicle_killed.connect(_on_vehicle_killed)

	print("[ArenaManager] Ready. %d spawn point(s) found." % spawn_points.size())

	# --- Auto-initialize from GameManager if we have match data ---
	# This lets the combat scene work without external setup calls.
	call_deferred("_auto_initialize")


## Automatically load the arena, spawn vehicles, and start the match
## using data from GameManager.match_settings. Deferred so the full
## scene tree is ready first.
func _auto_initialize() -> void:
	var settings: Dictionary = GameManager.match_settings
	var match_domain: String = settings.get("domain", "Ground").to_lower()

	# --- Load the arena scene into the ArenaContainer sibling ---
	var arena_path: String = ARENA_SCENES.get(match_domain, "")
	if arena_path != "" and ResourceLoader.exists(arena_path):
		var arena_scene: PackedScene = load(arena_path)
		if arena_scene:
			var arena_instance: Node3D = arena_scene.instantiate()
			# Find ArenaContainer sibling in the combat scene.
			var container: Node = get_parent().find_child("ArenaContainer", false, false)
			if container:
				container.add_child(arena_instance)
			else:
				get_parent().add_child(arena_instance)

			# Grab spawn points from the loaded arena.
			spawn_points.clear()
			for child: Node in arena_instance.get_children():
				if child.name.begins_with("SpawnPoint") and child is Node3D:
					spawn_points.append((child as Node3D).global_position)
			if spawn_points.is_empty():
				spawn_points.append(Vector3(-20.0, 1.0, 0.0))
				spawn_points.append(Vector3(20.0, 1.0, 0.0))

			print("[ArenaManager] Loaded arena: %s (%d spawn points)" % [arena_path, spawn_points.size()])

	# --- Build vehicle data list ---
	var vehicle_data_list: Array = []

	# Player vehicle from builder.
	var player_vehicle: Variant = settings.get("player_vehicle", null)
	if player_vehicle is Dictionary:
		var player_data: Dictionary = player_vehicle.duplicate()
		player_data["peer_id"] = 1
		player_data["is_ai"] = false
		vehicle_data_list.append(player_data)
	else:
		# No vehicle data — create a minimal fallback so there's something to see.
		push_warning("[ArenaManager] No player vehicle data found. Using fallback.")
		vehicle_data_list.append({
			"parts": [
				{"id": "cockpit", "grid_position": [3, 0, 3]},
				{"id": "light_frame", "grid_position": [3, 0, 2]},
				{"id": "light_frame", "grid_position": [3, 0, 4]},
				{"id": "small_wheel", "grid_position": [2, 0, 2]},
				{"id": "small_wheel", "grid_position": [4, 0, 2]},
				{"id": "small_wheel", "grid_position": [2, 0, 4]},
				{"id": "small_wheel", "grid_position": [4, 0, 4]},
				{"id": "machine_gun", "grid_position": [3, 1, 3]},
			],
			"domain": match_domain,
			"peer_id": 1,
			"is_ai": false,
		})

	# Solo mode: add an AI opponent.
	var mode: String = settings.get("mode", "solo")
	if mode == "solo":
		var difficulty: String = settings.get("ai_difficulty", "medium")
		var ai_budget: int = settings.get("budget", 1500)
		if ai_budget <= 0:
			ai_budget = 3000
		var ai_vehicle_data: Dictionary = AIBuilder.build_vehicle(match_domain, ai_budget, difficulty)
		ai_vehicle_data["peer_id"] = 2
		ai_vehicle_data["is_ai"] = true
		vehicle_data_list.append(ai_vehicle_data)

	# --- Setup and start ---
	setup_match(match_domain, vehicle_data_list)
	start_match()

	# --- Hook up the HUD to the player vehicle ---
	if vehicles.size() > 0:
		var player_v: Vehicle = null
		for v: Vehicle in vehicles:
			if v.is_player_controlled:
				player_v = v
				break
		if player_v == null:
			player_v = vehicles[0]

		# Find the HUD in the UI layer.
		var hud_node: Node = get_parent().find_child("HUD", true, false)
		if hud_node and hud_node.has_method("setup"):
			hud_node.setup(player_v)


## Every physics frame, advance the match timer and check for win conditions.
func _physics_process(delta: float) -> void:
	if not is_match_active:
		return

	match_timer += delta

	# --- Time limit check ---
	if match_timer >= max_match_time:
		_time_up()
		return

	# --- Win condition check ---
	_check_win_condition()


# ---------------------------------------------------------------------------
# Match setup
# ---------------------------------------------------------------------------

## Set up the match by spawning vehicles from the provided data.
##
## [param match_domain]     - The combat domain ("ground", "air", etc.).
## [param vehicle_data_list] - Array of Dictionaries, one per player/AI.
##                             Each dict has the format expected by
##                             Vehicle.setup_from_data():
##                             { "parts": [...], "domain": "ground",
##                               "peer_id": 1, "is_ai": false }
func setup_match(match_domain: String, vehicle_data_list: Array) -> void:
	domain = match_domain

	# --- Create arena hazards for this domain ---
	hazards = ArenaHazards.new()
	hazards.name = "ArenaHazards"
	add_child(hazards)
	hazards.setup(domain)

	# --- Spawn vehicles ---
	for i: int in range(vehicle_data_list.size()):
		var data: Dictionary = vehicle_data_list[i]

		# Create a new Vehicle node.
		var vehicle: Vehicle = Vehicle.new()
		vehicle.name = "Vehicle_%d" % i

		# Assign peer ID if present (for multiplayer attribution).
		vehicle.peer_id = int(data.get("peer_id", i + 1))

		# Add to scene tree before setup (some setup steps need the tree).
		add_child(vehicle)

		# Build the vehicle from the part data.
		vehicle.setup_from_data(data, domain)

		# Add the vehicle to the "vehicles" group for easy lookup.
		vehicle.add_to_group("vehicles")

		# --- Position at spawn point ---
		var spawn_index: int = i % spawn_points.size()
		vehicle.global_position = spawn_points[spawn_index]

		# Face spawn points toward the center of the arena.
		var look_target: Vector3 = Vector3.ZERO
		vehicle.look_at(look_target, Vector3.UP)

		# --- Set up AI if needed ---
		var is_ai: bool = data.get("is_ai", false)
		if is_ai:
			vehicle.is_ai_controlled = true
			vehicle.is_player_controlled = false

			var ai: AIController = AIController.new()
			ai.name = "AIController"
			vehicle.add_child(ai)
			# The AI target will be assigned in start_match() once all
			# vehicles exist.
		else:
			vehicle.is_player_controlled = true
			vehicle.is_ai_controlled = false

		# Connect the destruction signal.
		vehicle.vehicle_destroyed.connect(_on_vehicle_destroyed.bind(vehicle))

		vehicles.append(vehicle)

	print("[ArenaManager] Match setup: %d vehicle(s) in '%s' domain." % [
		vehicles.size(), domain
	])


# ---------------------------------------------------------------------------
# Match lifecycle
# ---------------------------------------------------------------------------

## Start the match. Enables all vehicle physics and assigns AI targets.
func start_match() -> void:
	is_match_active = true
	match_timer = 0.0

	# --- Assign AI targets ---
	# Each AI targets the nearest non-AI vehicle. In a 1v1 this is simple;
	# in larger matches the AI picks the closest enemy.
	for vehicle: Vehicle in vehicles:
		if not vehicle.is_ai_controlled:
			continue

		# Find the AIController child.
		for child: Node in vehicle.get_children():
			if child is AIController:
				var ai: AIController = child as AIController
				var ai_target: Vehicle = _find_target_for(vehicle)
				var diff: String = GameManager.match_settings.get("ai_difficulty", "medium")
				ai.setup(ai_target, diff)
				break

	# --- Unfreeze all vehicles ---
	for vehicle: Vehicle in vehicles:
		vehicle.freeze = false

	match_started.emit()
	GameManager.start_match()

	print("[ArenaManager] Match started! %d vehicle(s) in combat." % vehicles.size())


## End the match with a winner.
##
## [param winner] - The winning Vehicle, or null for a draw.
func _end_match(winner: Vehicle) -> void:
	is_match_active = false

	var winner_id: int = -1
	if winner != null:
		winner_id = winner.peer_id

	# --- Freeze all remaining vehicles ---
	for vehicle: Vehicle in vehicles:
		vehicle.freeze = true

	match_ended.emit(winner_id)
	GameManager.end_match(winner_id)

	print("[ArenaManager] Match ended. Winner: peer %d." % winner_id)


# ---------------------------------------------------------------------------
# Win conditions
# ---------------------------------------------------------------------------

## Check if the match should end. A match ends when zero or one vehicle
## remains alive.
func _check_win_condition() -> void:
	var alive_vehicles: Array[Vehicle] = []
	for vehicle: Vehicle in vehicles:
		if vehicle.is_alive:
			alive_vehicles.append(vehicle)

	if alive_vehicles.size() <= 1:
		var winner: Vehicle = alive_vehicles[0] if alive_vehicles.size() == 1 else null
		_end_match(winner)


## Called when the match timer reaches max_match_time. The winner is the
## vehicle with the most remaining HP (as a percentage of max HP).
func _time_up() -> void:
	print("[ArenaManager] Time's up! Determining winner by HP...")

	var best_vehicle: Vehicle = null
	var best_hp_pct: float = -1.0

	for vehicle: Vehicle in vehicles:
		if not vehicle.is_alive:
			continue

		var hp_pct: float = _get_vehicle_hp_percent(vehicle)
		if hp_pct > best_hp_pct:
			best_hp_pct = hp_pct
			best_vehicle = vehicle

	_end_match(best_vehicle)


# ---------------------------------------------------------------------------
# Signal handlers
# ---------------------------------------------------------------------------

## Called when a vehicle is destroyed (via vehicle.vehicle_destroyed signal).
func _on_vehicle_destroyed(vehicle: Vehicle) -> void:
	vehicle_eliminated.emit(vehicle)
	print("[ArenaManager] Vehicle '%s' (peer %d) eliminated!" % [vehicle.name, vehicle.peer_id])


## Called when the DamageSystem reports a vehicle kill.
func _on_vehicle_killed(vehicle: Vehicle) -> void:
	# The DamageSystem already called vehicle.die(). We just emit our signal.
	vehicle_eliminated.emit(vehicle)


# ---------------------------------------------------------------------------
# Utility
# ---------------------------------------------------------------------------

## Calculate a vehicle's current HP as a percentage (0.0 - 1.0).
## Used for time-up tiebreaker.
func _get_vehicle_hp_percent(vehicle: Vehicle) -> float:
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


## Find the best target for an AI vehicle. Returns the nearest alive
## non-ally vehicle.
func _find_target_for(ai_vehicle: Vehicle) -> Vehicle:
	var best: Vehicle = null
	var best_dist: float = INF

	for vehicle: Vehicle in vehicles:
		if vehicle == ai_vehicle:
			continue
		if not vehicle.is_alive:
			continue

		var dist: float = ai_vehicle.global_position.distance_to(vehicle.global_position)
		if dist < best_dist:
			best_dist = dist
			best = vehicle

	return best


## Get the arena scene path for a given domain. Used by the lobby or main
## menu to load the correct arena scene.
static func get_arena_scene_path(arena_domain: String) -> String:
	if ARENA_SCENES.has(arena_domain):
		return ARENA_SCENES[arena_domain]
	push_warning("[ArenaManager] No arena scene for domain '%s'." % arena_domain)
	return ""
