## GameManager (Autoload Singleton)
##
## Central manager for game state and scene transitions. This is registered as
## an autoload in project.godot so it persists across scene changes and is
## accessible globally via "GameManager" from any script.
##
## Responsibilities:
##   - Track which phase the game is in (menu, lobby, building, combat, results)
##   - Store match configuration (domain, budget, arena, mode, etc.)
##   - Store references to each player's vehicle data keyed by network peer ID
##   - Provide helpers for scene transitions and budget lookups
extends Node


# ---------------------------------------------------------------------------
# Enums
# ---------------------------------------------------------------------------

## All possible high-level game states. The game always starts in MAIN_MENU.
enum GameState {
	MAIN_MENU,  ## Title / main menu screen
	LOBBY,      ## Multiplayer lobby (host or join)
	BUILDING,   ## Vehicle builder phase
	COMBAT,     ## Active combat phase
	RESULTS,    ## Post-match results screen
}


# ---------------------------------------------------------------------------
# Signals
# ---------------------------------------------------------------------------

## Emitted whenever the game state changes. Listeners (e.g. UI) can react
## to transitions without polling.
signal state_changed(new_state: GameState)

## Emitted when the match officially begins (transition to COMBAT).
signal match_started

## Emitted when the match ends. [param winner_id] is the network peer ID of
## the winning player (1 for the host in single-player).
signal match_ended(winner_id: int)


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## Budget dollar amounts for each named tier. A value of 0 means unlimited.
const BUDGET_TIERS: Dictionary = {
	"scrapyard": 500,
	"garage": 1500,
	"factory": 3000,
	"arsenal": 5000,
	"unlimited": 0,
	# "custom" is handled separately -- uses match_settings.budget directly
}


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## The current phase of the game. Change this through [method change_state]
## so that the [signal state_changed] signal fires properly.
var current_state: GameState = GameState.MAIN_MENU

## Dictionary holding all settings for the current / upcoming match.
## Keys:
##   "domain"       : String  -- ground, air, water, submarine, or space
##   "budget"       : int     -- dollar amount (0 = unlimited)
##   "budget_tier"  : String  -- scrapyard / garage / factory / arsenal / unlimited / custom
##   "arena"        : String  -- scene file path (e.g. "res://scenes/arenas/desert.tscn")
##   "mode"         : String  -- solo, host, or join
##   "player_count" : int     -- number of players in the match
##   "ai_difficulty": String  -- easy, medium, or hard
var match_settings: Dictionary = {
	"domain": "ground",
	"budget": 1500,
	"budget_tier": "garage",
	"arena": "",
	"mode": "solo",
	"player_count": 1,
	"ai_difficulty": "medium",
}

## Stores serialized vehicle data for every player in the match.
## Keyed by the player's network peer ID (int). In single-player the host's
## peer ID is 1.
var player_vehicles: Dictionary = {}


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

## Called once when the autoload is added to the scene tree (on game launch).
## Sets the initial state to MAIN_MENU.
func _ready() -> void:
	# Ensure we start in the menu state every time the game launches.
	current_state = GameState.MAIN_MENU


# ---------------------------------------------------------------------------
# State management
# ---------------------------------------------------------------------------

## Transition to a new [param new_state]. Updates [member current_state] and
## emits [signal state_changed] so the rest of the game can react.
func change_state(new_state: GameState) -> void:
	var old_state: GameState = current_state
	current_state = new_state
	state_changed.emit(new_state)
	print("[GameManager] State changed: %s -> %s" % [
		GameState.keys()[old_state],
		GameState.keys()[new_state],
	])


# ---------------------------------------------------------------------------
# Scene transitions
# ---------------------------------------------------------------------------

## Convenience wrapper around the engine's scene-change API.
## [param scene_path] is the full resource path, e.g.
## "res://scenes/main_menu.tscn".
func change_scene(scene_path: String) -> void:
	var error: int = get_tree().change_scene_to_file(scene_path)
	if error != OK:
		push_error("[GameManager] Failed to change scene to '%s'. Error code: %d" % [scene_path, error])


# ---------------------------------------------------------------------------
# Match lifecycle
# ---------------------------------------------------------------------------

## Begin the match. Transitions to COMBAT and emits [signal match_started].
func start_match() -> void:
	change_state(GameState.COMBAT)
	match_started.emit()
	print("[GameManager] Match started.")


## End the match. [param winner_id] is the peer ID of the winning player.
## Transitions to RESULTS and emits [signal match_ended].
func end_match(winner_id: int) -> void:
	change_state(GameState.RESULTS)
	match_ended.emit(winner_id)
	print("[GameManager] Match ended. Winner peer ID: %d" % winner_id)


## Wipe all match data and return to the main menu. Useful after a match
## ends or if the player backs out of the lobby.
func reset_match() -> void:
	player_vehicles.clear()
	match_settings = {
		"domain": "ground",
		"budget": 1500,
		"budget_tier": "garage",
		"arena": "",
		"mode": "solo",
		"player_count": 1,
		"ai_difficulty": "medium",
	}
	change_state(GameState.MAIN_MENU)
	print("[GameManager] Match reset.")


# ---------------------------------------------------------------------------
# Budget helpers
# ---------------------------------------------------------------------------

## Return the dollar budget for the given [param tier] name.
## Recognized tiers: scrapyard, garage, factory, arsenal, unlimited, custom.
## "custom" returns whatever value is currently stored in
## [member match_settings]["budget"].
## Returns 0 (unlimited) if the tier name is not recognized.
func get_budget_for_tier(tier: String) -> int:
	if tier == "custom":
		# Custom uses whatever the player typed in.
		return match_settings.get("budget", 0)
	if BUDGET_TIERS.has(tier):
		return BUDGET_TIERS[tier]
	push_warning("[GameManager] Unknown budget tier '%s'. Returning 0 (unlimited)." % tier)
	return 0
