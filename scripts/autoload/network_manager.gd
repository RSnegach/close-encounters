## NetworkManager (Autoload Singleton)
##
## Handles LAN multiplayer networking using Godot's ENetMultiplayerPeer.
## Supports hosting and joining games on the local network. Uses a
## server-authoritative model where the host (peer ID 1) is the authority.
##
## Typical flow:
##   1. Host calls host_game() -- starts listening for connections.
##   2. Client calls join_game(address) -- connects to the host.
##   3. Both exchange vehicle data via send_vehicle_data().
##   4. Both set ready via set_player_ready(true).
##   5. When all players are ready, all_players_ready is emitted.
##   6. During combat, sync_combat_state() sends position updates.
extends Node


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## Default network port for LAN games.
const DEFAULT_PORT: int = 7777

## Maximum number of players in a LAN match (including host).
const MAX_PLAYERS: int = 4


# ---------------------------------------------------------------------------
# Signals
# ---------------------------------------------------------------------------

## A new player connected. [param peer_id] is their network peer ID.
signal player_connected(peer_id: int)

## A player disconnected. [param peer_id] is their network peer ID.
signal player_disconnected(peer_id: int)

## Emitted on the client when connection to the host fails.
signal connection_failed

## Emitted on clients when the server (host) disconnects unexpectedly.
signal server_disconnected

## Emitted when every connected player has reported ready.
signal all_players_ready


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## The ENet peer object. Null when not connected.
var peer: ENetMultiplayerPeer = null

## Dictionary of connected players. Keyed by peer_id (int), value is a
## dictionary with metadata (e.g. {"name": "Player1"}).
var players: Dictionary = {}

## Tracks the ready state of every player. Keyed by peer_id (int),
## value is bool.
var player_ready_states: Dictionary = {}

## Stores serialized vehicle data received from other players. Keyed by
## peer_id (int), value is the vehicle dictionary.
var vehicle_data_received: Dictionary = {}


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

## Connect to the SceneTree's multiplayer signals so we get notified about
## peer connect/disconnect events.
func _ready() -> void:
	# These signals live on the SceneTree's multiplayer object. We wire them
	# up to our own handler methods.
	multiplayer.peer_connected.connect(_on_peer_connected)
	multiplayer.peer_disconnected.connect(_on_peer_disconnected)
	multiplayer.connected_to_server.connect(_on_connected_to_server)
	multiplayer.connection_failed.connect(_on_connection_failed)
	multiplayer.server_disconnected.connect(_on_server_disconnected)


# ---------------------------------------------------------------------------
# Hosting & joining
# ---------------------------------------------------------------------------

## Create a server and start listening for incoming connections.
## [param port] defaults to [constant DEFAULT_PORT] (7777).
func host_game(port: int = DEFAULT_PORT) -> void:
	peer = ENetMultiplayerPeer.new()
	var error: int = peer.create_server(port, MAX_PLAYERS)
	if error != OK:
		push_error("[NetworkManager] Failed to create server on port %d. Error: %d" % [port, error])
		peer = null
		return

	# Tell the engine to use our peer for all multiplayer traffic.
	multiplayer.multiplayer_peer = peer

	# Register the host itself in the players dictionary.
	players[1] = {"name": "Host"}
	player_ready_states[1] = false

	print("[NetworkManager] Hosting game on port %d." % port)


## Connect to a server at [param address] on [param port].
## [param address] is an IP string like "192.168.1.10".
func join_game(address: String, port: int = DEFAULT_PORT) -> void:
	peer = ENetMultiplayerPeer.new()
	var error: int = peer.create_client(address, port)
	if error != OK:
		push_error("[NetworkManager] Failed to connect to %s:%d. Error: %d" % [address, port, error])
		peer = null
		return

	multiplayer.multiplayer_peer = peer
	print("[NetworkManager] Attempting to join %s:%d ..." % [address, port])


## Gracefully close the network connection and reset all tracking data.
func disconnect_game() -> void:
	if peer != null:
		peer.close()
		peer = null
	multiplayer.multiplayer_peer = null
	players.clear()
	player_ready_states.clear()
	vehicle_data_received.clear()
	print("[NetworkManager] Disconnected.")


# ---------------------------------------------------------------------------
# Utility getters
# ---------------------------------------------------------------------------

## Returns true if this machine is the server (host).
func is_host() -> bool:
	return multiplayer.is_server()


## Returns this machine's unique network peer ID.
## The host is always 1. Returns 0 if not connected.
func get_peer_id() -> int:
	return multiplayer.get_unique_id()


# ---------------------------------------------------------------------------
# Vehicle data exchange
# ---------------------------------------------------------------------------

## Send this player's serialized vehicle to all other peers.
## [param data] is a Dictionary describing the vehicle (part layout, etc.).
func send_vehicle_data(data: Dictionary) -> void:
	# Call the RPC on all peers (including self via server relay).
	receive_vehicle_data.rpc(data)


## RPC that receives vehicle data from a remote peer.
## "any_peer" means any connected peer can call this.
## "reliable" ensures the data arrives (TCP-like).
@rpc("any_peer", "reliable")
func receive_vehicle_data(data: Dictionary) -> void:
	var sender_id: int = multiplayer.get_remote_sender_id()
	vehicle_data_received[sender_id] = data
	print("[NetworkManager] Received vehicle data from peer %d." % sender_id)


# ---------------------------------------------------------------------------
# Ready state tracking
# ---------------------------------------------------------------------------

## Notify all peers that this player's ready state changed.
## Call this from the lobby UI when the player clicks "Ready" / "Not Ready".
func set_player_ready(ready: bool) -> void:
	var my_id: int = get_peer_id()
	# Update locally immediately.
	player_ready_states[my_id] = ready
	# Tell everyone else.
	_on_player_ready.rpc(my_id, ready)
	# Check if all are now ready.
	_check_all_ready()


## RPC that receives a player's ready-state update.
@rpc("any_peer", "reliable")
func _on_player_ready(sender_peer_id: int, ready: bool) -> void:
	player_ready_states[sender_peer_id] = ready
	print("[NetworkManager] Peer %d ready: %s" % [sender_peer_id, str(ready)])
	_check_all_ready()


## Internal helper -- checks whether every connected player is ready and
## emits [signal all_players_ready] if so.
func _check_all_ready() -> void:
	# Need at least one player.
	if player_ready_states.is_empty():
		return

	for peer_id: int in player_ready_states:
		if not player_ready_states[peer_id]:
			return  # Someone is not ready yet.

	# Everyone is ready!
	all_players_ready.emit()
	print("[NetworkManager] All players ready!")


# ---------------------------------------------------------------------------
# Combat state sync
# ---------------------------------------------------------------------------

## Send real-time combat state (position, rotation, actions) to all peers.
## Uses "unreliable" delivery for position/rotation since stale data is
## acceptable -- the next packet will correct it.
## [param position_data] - Vector3 world position
## [param rotation_data] - Vector3 euler rotation (degrees)
## [param actions] - Dictionary of action states (e.g. {"firing": true})
func sync_combat_state(position_data: Vector3, rotation_data: Vector3, actions: Dictionary) -> void:
	_receive_combat_state.rpc(position_data, rotation_data, actions)


## RPC that receives combat state from a remote peer.
## "unreliable" because we send these frequently and losing one is fine.
@rpc("any_peer", "unreliable")
func _receive_combat_state(position_data: Vector3, rotation_data: Vector3, actions: Dictionary) -> void:
	var sender_id: int = multiplayer.get_remote_sender_id()
	# Other systems (e.g. vehicle controller) should listen for this or poll
	# a shared data structure. For now we store it on the players dict.
	if players.has(sender_id):
		players[sender_id]["position"] = position_data
		players[sender_id]["rotation"] = rotation_data
		players[sender_id]["actions"] = actions


# ---------------------------------------------------------------------------
# Multiplayer signal handlers (private)
# ---------------------------------------------------------------------------

## Called when a new peer connects to the server.
func _on_peer_connected(peer_id: int) -> void:
	players[peer_id] = {"name": "Player_%d" % peer_id}
	player_ready_states[peer_id] = false
	player_connected.emit(peer_id)
	print("[NetworkManager] Peer %d connected. Total players: %d" % [peer_id, players.size()])


## Called when a peer disconnects from the server.
func _on_peer_disconnected(peer_id: int) -> void:
	players.erase(peer_id)
	player_ready_states.erase(peer_id)
	vehicle_data_received.erase(peer_id)
	player_disconnected.emit(peer_id)
	print("[NetworkManager] Peer %d disconnected. Total players: %d" % [peer_id, players.size()])


## Called on the client when it successfully connects to the server.
func _on_connected_to_server() -> void:
	var my_id: int = multiplayer.get_unique_id()
	players[my_id] = {"name": "Player_%d" % my_id}
	player_ready_states[my_id] = false
	print("[NetworkManager] Connected to server. My peer ID: %d" % my_id)


## Called on the client when the connection attempt fails.
func _on_connection_failed() -> void:
	push_warning("[NetworkManager] Connection to server failed.")
	connection_failed.emit()
	peer = null
	multiplayer.multiplayer_peer = null


## Called on the client when the server drops the connection.
func _on_server_disconnected() -> void:
	push_warning("[NetworkManager] Server disconnected.")
	server_disconnected.emit()
	players.clear()
	player_ready_states.clear()
	vehicle_data_received.clear()
	peer = null
	multiplayer.multiplayer_peer = null
