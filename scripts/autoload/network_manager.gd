## NetworkManager (Autoload Singleton)
##
## Handles multiplayer networking with automatic game discovery.
## Three discovery mechanisms, all transparent to the player:
##
##   1. **LAN broadcast** — host sends periodic UDP broadcasts on port 7776,
##      clients listen and auto-discover games on the same local network.
##
##   2. **Online lobby server** — host registers with an HTTP matchmaking
##      server, clients query it to find internet-hosted games. Configure
##      the lobby URL via LOBBY_SERVER_URL (default: localhost for testing).
##
##   3. **UPnP** — when hosting, automatically asks the router to forward
##      the game port so internet players can connect without manual
##      port-forwarding.
##
## Joining a game is as simple as calling discover_games() and then
## join_game() on one of the returned results. The main menu's server
## browser does this automatically.
##
## Server-authoritative model: host (peer ID 1) is the authority.
extends Node


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## Default game port for ENet connections.
const DEFAULT_PORT: int = 7777

## Port used for LAN broadcast discovery (separate from game traffic).
const BROADCAST_PORT: int = 7776

## Maximum players in a match (including host).
const MAX_PLAYERS: int = 4

## How often the host broadcasts its presence on LAN (seconds).
const BROADCAST_INTERVAL: float = 2.0

## How often clients refresh the LAN listener for new broadcasts (seconds).
const LISTEN_INTERVAL: float = 1.0

## After this many seconds without a broadcast, a LAN game is considered gone.
const LAN_GAME_TIMEOUT: float = 8.0

## URL of the online matchmaking lobby server.
## Change this to your deployed server's URL for internet play.
## Example: "https://close-encounters-lobby.onrender.com"
const LOBBY_SERVER_URL: String = "http://localhost:8080"

## Broadcast magic bytes so we only respond to our own game's packets.
const BROADCAST_MAGIC: String = "CLOSE_ENCOUNTERS_v1"


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

## Emitted when the available games list changes (LAN or online discovery).
## [param games] is an Array of Dictionaries, each with keys:
##   "name": String, "address": String, "port": int,
##   "players": int, "domain": String, "source": "lan"|"online"
signal games_discovered(games: Array)

## Emitted when UPnP setup completes. [param success] indicates whether the
## port was successfully forwarded.
signal upnp_completed(success: bool)


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## The ENet peer object. Null when not connected.
var peer: ENetMultiplayerPeer = null

## Dictionary of connected players. Keyed by peer_id (int), value is a
## dictionary with metadata (e.g. {"name": "Player1"}).
var players: Dictionary = {}

## Tracks the ready state of every player. Keyed by peer_id, value is bool.
var player_ready_states: Dictionary = {}

## Stores serialized vehicle data received from other players.
var vehicle_data_received: Dictionary = {}

## Combined list of discovered games from LAN + online. Each entry is a dict
## with: name, address, port, players, domain, source.
var discovered_games: Array = []

## The host's public IP address, discovered via UPnP.
var public_ip: String = ""

## Name for this hosted game (shown in server browser).
var game_name: String = "Close Encounters Game"


# ---------------------------------------------------------------------------
# Private variables
# ---------------------------------------------------------------------------

## UDP peer for sending LAN broadcast announcements (host only).
var _broadcast_peer: PacketPeerUDP = null

## UDP peer for listening for LAN broadcasts (client/discovery only).
var _listen_peer: PacketPeerUDP = null

## Timer tracking for periodic broadcast sending.
var _broadcast_timer: float = 0.0

## Timer tracking for periodic listen polling.
var _listen_timer: float = 0.0

## LAN games discovered, keyed by "ip:port" string. Value is the game dict
## plus a "_last_seen" timestamp for timeout tracking.
var _lan_games: Dictionary = {}

## Whether we are currently broadcasting (hosting).
var _is_broadcasting: bool = false

## Whether we are currently listening for broadcasts.
var _is_listening: bool = false

## UPnP object used for automatic port forwarding.
var _upnp: UPNP = null

## Whether UPnP successfully mapped our port.
var _upnp_mapped: bool = false

## HTTPRequest node for online lobby communication.
var _http_request: HTTPRequest = null

## What the current HTTP request is doing: "register", "unregister",
## "discover", or "" if idle.
var _http_action: String = ""

## The host's port (stored so we can unregister on shutdown).
var _hosted_port: int = 0


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

func _ready() -> void:
	# Wire up multiplayer signals.
	multiplayer.peer_connected.connect(_on_peer_connected)
	multiplayer.peer_disconnected.connect(_on_peer_disconnected)
	multiplayer.connected_to_server.connect(_on_connected_to_server)
	multiplayer.connection_failed.connect(_on_connection_failed)
	multiplayer.server_disconnected.connect(_on_server_disconnected)

	# Create the HTTPRequest node for lobby server communication.
	_http_request = HTTPRequest.new()
	_http_request.request_completed.connect(_on_http_request_completed)
	add_child(_http_request)


func _process(delta: float) -> void:
	# Host: send periodic LAN broadcasts.
	if _is_broadcasting:
		_broadcast_timer += delta
		if _broadcast_timer >= BROADCAST_INTERVAL:
			_broadcast_timer = 0.0
			_send_broadcast()

	# Client/browser: poll for incoming LAN broadcasts.
	if _is_listening:
		_listen_timer += delta
		if _listen_timer >= LISTEN_INTERVAL:
			_listen_timer = 0.0
			_poll_broadcasts()
			_expire_stale_lan_games()


# ---------------------------------------------------------------------------
# Hosting
# ---------------------------------------------------------------------------

## Host a game. This:
##   1. Creates the ENet server on [param port].
##   2. Starts LAN broadcasting so local players can discover you.
##   3. Starts UPnP port-forwarding (in a thread) for internet play.
##   4. Registers with the online lobby server.
func host_game(port: int = DEFAULT_PORT) -> void:
	# --- ENet server ---
	peer = ENetMultiplayerPeer.new()
	var error: int = peer.create_server(port, MAX_PLAYERS)
	if error != OK:
		push_error("[NetworkManager] Failed to create server on port %d. Error: %d" % [port, error])
		peer = null
		return

	multiplayer.multiplayer_peer = peer
	players[1] = {"name": "Host"}
	player_ready_states[1] = false
	_hosted_port = port

	# --- LAN broadcast ---
	_start_broadcasting(port)

	# --- UPnP (threaded so it doesn't block) ---
	_setup_upnp(port)

	print("[NetworkManager] Hosting on port %d. LAN broadcast active." % port)


## Stop hosting — close everything down cleanly.
func stop_hosting() -> void:
	_stop_broadcasting()
	_unregister_from_lobby()
	_cleanup_upnp()


# ---------------------------------------------------------------------------
# Joining
# ---------------------------------------------------------------------------

## Connect to a discovered game. [param address] is the IP, [param port]
## is the game port. You typically get these from discovered_games entries.
func join_game(address: String, port: int = DEFAULT_PORT) -> void:
	# Stop listening when we join (we found our game).
	stop_discovery()

	peer = ENetMultiplayerPeer.new()
	var error: int = peer.create_client(address, port)
	if error != OK:
		push_error("[NetworkManager] Failed to connect to %s:%d. Error: %d" % [address, port, error])
		peer = null
		return

	multiplayer.multiplayer_peer = peer
	print("[NetworkManager] Connecting to %s:%d ..." % [address, port])


## Disconnect from the current game and clean up all state.
func disconnect_game() -> void:
	stop_hosting()
	stop_discovery()

	if peer != null:
		peer.close()
		peer = null
	multiplayer.multiplayer_peer = null
	players.clear()
	player_ready_states.clear()
	vehicle_data_received.clear()
	discovered_games.clear()
	_lan_games.clear()
	print("[NetworkManager] Disconnected.")


# ---------------------------------------------------------------------------
# Game discovery (called by the server browser UI)
# ---------------------------------------------------------------------------

## Start discovering available games on both LAN and internet.
## Results arrive via the games_discovered signal.
func start_discovery() -> void:
	discovered_games.clear()
	_lan_games.clear()
	_start_listening()
	_query_lobby_server()


## Stop listening for game broadcasts.
func stop_discovery() -> void:
	_stop_listening()


## Manually refresh the online lobby list.
func refresh_online_games() -> void:
	_query_lobby_server()


# ---------------------------------------------------------------------------
# Utility getters
# ---------------------------------------------------------------------------

## Returns true if this machine is the server (host).
func is_host() -> bool:
	if peer == null:
		return false
	return multiplayer.is_server()


## Returns this machine's unique network peer ID (host = 1, 0 = not connected).
func get_peer_id() -> int:
	if peer == null:
		return 0
	return multiplayer.get_unique_id()


# ---------------------------------------------------------------------------
# Vehicle data exchange
# ---------------------------------------------------------------------------

## Send this player's vehicle data to all peers.
func send_vehicle_data(data: Dictionary) -> void:
	receive_vehicle_data.rpc(data)


## RPC receiving vehicle data from a remote peer.
@rpc("any_peer", "reliable")
func receive_vehicle_data(data: Dictionary) -> void:
	var sender_id: int = multiplayer.get_remote_sender_id()
	vehicle_data_received[sender_id] = data
	print("[NetworkManager] Received vehicle data from peer %d." % sender_id)


# ---------------------------------------------------------------------------
# Ready state tracking
# ---------------------------------------------------------------------------

## Set this player's ready state and notify all peers.
func set_player_ready(ready: bool) -> void:
	var my_id: int = get_peer_id()
	player_ready_states[my_id] = ready
	_on_player_ready.rpc(my_id, ready)
	_check_all_ready()


@rpc("any_peer", "reliable")
func _on_player_ready(sender_peer_id: int, ready: bool) -> void:
	player_ready_states[sender_peer_id] = ready
	print("[NetworkManager] Peer %d ready: %s" % [sender_peer_id, str(ready)])
	_check_all_ready()


func _check_all_ready() -> void:
	if player_ready_states.is_empty():
		return
	for pid: int in player_ready_states:
		if not player_ready_states[pid]:
			return
	all_players_ready.emit()
	print("[NetworkManager] All players ready!")


# ---------------------------------------------------------------------------
# Combat state sync
# ---------------------------------------------------------------------------

## Send position/rotation/actions to all peers (unreliable = fast, lossy ok).
func sync_combat_state(position_data: Vector3, rotation_data: Vector3, actions: Dictionary) -> void:
	_receive_combat_state.rpc(position_data, rotation_data, actions)


@rpc("any_peer", "unreliable")
func _receive_combat_state(position_data: Vector3, rotation_data: Vector3, actions: Dictionary) -> void:
	var sender_id: int = multiplayer.get_remote_sender_id()
	if players.has(sender_id):
		players[sender_id]["position"] = position_data
		players[sender_id]["rotation"] = rotation_data
		players[sender_id]["actions"] = actions


# ---------------------------------------------------------------------------
# LAN Broadcast — Host side
# ---------------------------------------------------------------------------

## Start broadcasting this game's presence on the LAN via UDP.
func _start_broadcasting(port: int) -> void:
	_broadcast_peer = PacketPeerUDP.new()
	# Enable broadcast mode so the packet goes to 255.255.255.255.
	_broadcast_peer.set_broadcast_enabled(true)
	# Bind to any available port for sending (we don't receive on this).
	var err := _broadcast_peer.bind(0)
	if err != OK:
		push_warning("[NetworkManager] Could not bind broadcast sender: %d" % err)
		_broadcast_peer = null
		return
	_broadcast_peer.set_dest_address("255.255.255.255", BROADCAST_PORT)
	_is_broadcasting = true
	_broadcast_timer = 0.0
	print("[NetworkManager] LAN broadcast started on port %d." % BROADCAST_PORT)


## Send one broadcast packet containing game info as JSON.
func _send_broadcast() -> void:
	if _broadcast_peer == null:
		return
	var domain: String = GameManager.match_settings.get("domain", "Ground")
	var info: Dictionary = {
		"magic": BROADCAST_MAGIC,
		"name": game_name,
		"port": _hosted_port,
		"players": players.size(),
		"max_players": MAX_PLAYERS,
		"domain": domain,
	}
	var json: String = JSON.stringify(info)
	_broadcast_peer.put_packet(json.to_utf8_buffer())


## Stop LAN broadcasting.
func _stop_broadcasting() -> void:
	if _broadcast_peer != null:
		_broadcast_peer.close()
		_broadcast_peer = null
	_is_broadcasting = false


# ---------------------------------------------------------------------------
# LAN Broadcast — Client / listener side
# ---------------------------------------------------------------------------

## Start listening for LAN broadcast packets.
func _start_listening() -> void:
	_listen_peer = PacketPeerUDP.new()
	var err := _listen_peer.bind(BROADCAST_PORT)
	if err != OK:
		push_warning("[NetworkManager] Could not bind LAN listener on port %d: %d" % [BROADCAST_PORT, err])
		_listen_peer = null
		return
	_is_listening = true
	_listen_timer = 0.0
	print("[NetworkManager] LAN discovery listener started.")


## Poll for incoming broadcast packets and update _lan_games.
func _poll_broadcasts() -> void:
	if _listen_peer == null:
		return

	# Drain all available packets.
	while _listen_peer.get_available_packet_count() > 0:
		var packet: PackedByteArray = _listen_peer.get_packet()
		var sender_ip: String = _listen_peer.get_packet_ip()
		var json_str: String = packet.get_string_from_utf8()

		var parsed = JSON.parse_string(json_str)
		if parsed == null or not parsed is Dictionary:
			continue

		var data: Dictionary = parsed
		if data.get("magic", "") != BROADCAST_MAGIC:
			continue  # Not our game.

		var port: int = int(data.get("port", DEFAULT_PORT))
		var key: String = "%s:%d" % [sender_ip, port]

		var game_entry: Dictionary = {
			"name": data.get("name", "Unknown"),
			"address": sender_ip,
			"port": port,
			"players": int(data.get("players", 1)),
			"max_players": int(data.get("max_players", MAX_PLAYERS)),
			"domain": data.get("domain", "Ground"),
			"source": "lan",
			"_last_seen": Time.get_ticks_msec(),
		}
		_lan_games[key] = game_entry

	_rebuild_discovered_list()


## Remove LAN games that haven't broadcast recently.
func _expire_stale_lan_games() -> void:
	var now: int = Time.get_ticks_msec()
	var timeout_ms: int = int(LAN_GAME_TIMEOUT * 1000.0)
	var stale_keys: Array = []
	for key: String in _lan_games:
		if now - _lan_games[key].get("_last_seen", 0) > timeout_ms:
			stale_keys.append(key)
	for key: String in stale_keys:
		_lan_games.erase(key)
	if stale_keys.size() > 0:
		_rebuild_discovered_list()


## Stop listening for LAN broadcasts.
func _stop_listening() -> void:
	if _listen_peer != null:
		_listen_peer.close()
		_listen_peer = null
	_is_listening = false


# ---------------------------------------------------------------------------
# Online lobby server
# ---------------------------------------------------------------------------

## Register this game with the online lobby server so internet players
## can discover it.  Called automatically after UPnP completes.
func _register_with_lobby() -> void:
	if LOBBY_SERVER_URL.is_empty():
		return
	var domain: String = GameManager.match_settings.get("domain", "Ground")
	var body: Dictionary = {
		"name": game_name,
		"port": _hosted_port,
		"ip": public_ip,
		"players": players.size(),
		"max_players": MAX_PLAYERS,
		"domain": domain,
	}
	var json: String = JSON.stringify(body)
	var headers: PackedStringArray = ["Content-Type: application/json"]
	_http_action = "register"
	var err := _http_request.request(LOBBY_SERVER_URL + "/games", headers, HTTPClient.METHOD_POST, json)
	if err != OK:
		push_warning("[NetworkManager] Failed to register with lobby server: %d" % err)
		_http_action = ""


## Remove this game from the online lobby server.
func _unregister_from_lobby() -> void:
	if LOBBY_SERVER_URL.is_empty() or public_ip.is_empty():
		return
	_http_action = "unregister"
	var url: String = "%s/games/%s/%d" % [LOBBY_SERVER_URL, public_ip, _hosted_port]
	var err := _http_request.request(url, [], HTTPClient.METHOD_DELETE)
	if err != OK:
		push_warning("[NetworkManager] Failed to unregister from lobby: %d" % err)
		_http_action = ""


## Query the lobby server for available games.
func _query_lobby_server() -> void:
	if LOBBY_SERVER_URL.is_empty():
		return
	_http_action = "discover"
	var err := _http_request.request(LOBBY_SERVER_URL + "/games", [], HTTPClient.METHOD_GET)
	if err != OK:
		push_warning("[NetworkManager] Failed to query lobby server: %d" % err)
		_http_action = ""


## Handle HTTP responses from the lobby server.
func _on_http_request_completed(result: int, response_code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	var action: String = _http_action
	_http_action = ""

	if result != HTTPRequest.RESULT_SUCCESS:
		# Server unreachable — not fatal, LAN still works.
		if action == "discover":
			_rebuild_discovered_list()  # Emit what we have from LAN.
		return

	match action:
		"register":
			if response_code == 200 or response_code == 201:
				print("[NetworkManager] Registered with lobby server.")
			else:
				push_warning("[NetworkManager] Lobby register returned %d." % response_code)

		"unregister":
			print("[NetworkManager] Unregistered from lobby server.")

		"discover":
			if response_code == 200:
				var json_str: String = body.get_string_from_utf8()
				var parsed = JSON.parse_string(json_str)
				if parsed is Array:
					_merge_online_games(parsed)
			else:
				push_warning("[NetworkManager] Lobby query returned %d." % response_code)
				_rebuild_discovered_list()


## Merge games from the online lobby into discovered_games.
func _merge_online_games(online_list: Array) -> void:
	# Build online entries.
	var online_games: Array = []
	for entry in online_list:
		if not entry is Dictionary:
			continue
		online_games.append({
			"name": entry.get("name", "Online Game"),
			"address": entry.get("ip", ""),
			"port": int(entry.get("port", DEFAULT_PORT)),
			"players": int(entry.get("players", 1)),
			"max_players": int(entry.get("max_players", MAX_PLAYERS)),
			"domain": entry.get("domain", "Ground"),
			"source": "online",
		})

	# Combine LAN + online, de-duplicate by address:port.
	var seen: Dictionary = {}
	discovered_games.clear()

	# LAN games first (they are faster to connect to).
	for key: String in _lan_games:
		var g: Dictionary = _lan_games[key].duplicate()
		g.erase("_last_seen")
		discovered_games.append(g)
		seen[key] = true

	# Online games (skip if already found on LAN).
	for g: Dictionary in online_games:
		var key: String = "%s:%d" % [g.get("address", ""), g.get("port", 0)]
		if not seen.has(key) and g.get("address", "") != "":
			discovered_games.append(g)

	games_discovered.emit(discovered_games)


## Rebuild the discovered list from LAN games only (used when lobby is
## unreachable) and emit the signal.
func _rebuild_discovered_list() -> void:
	discovered_games.clear()
	for key: String in _lan_games:
		var g: Dictionary = _lan_games[key].duplicate()
		g.erase("_last_seen")
		discovered_games.append(g)
	games_discovered.emit(discovered_games)


# ---------------------------------------------------------------------------
# UPnP — automatic port forwarding for internet play
# ---------------------------------------------------------------------------

## Discover UPnP devices and forward the game port. Runs in a thread to
## avoid blocking the main thread (UPnP discovery can take 2-5 seconds).
func _setup_upnp(port: int) -> void:
	# Run in a background thread because UPNP.discover() blocks.
	var thread := Thread.new()
	thread.start(_upnp_thread.bind(port, thread))


## Thread function that performs UPnP discovery and port mapping.
## [param port] is the game port to forward.
## [param thread] is the Thread reference so we can clean up.
func _upnp_thread(port: int, thread: Thread) -> void:
	_upnp = UPNP.new()

	# Step 1: discover UPnP devices on the network (routers, gateways).
	var discover_result: int = _upnp.discover()
	if discover_result != UPNP.UPNP_RESULT_SUCCESS:
		call_deferred("_on_upnp_done", false, "", thread)
		return

	# Step 2: find a valid Internet Gateway Device.
	var gateway: UPNPDevice = null
	for i in range(_upnp.get_device_count()):
		var dev: UPNPDevice = _upnp.get_device(i)
		if dev.is_valid_gateway():
			gateway = dev
			break

	if gateway == null:
		call_deferred("_on_upnp_done", false, "", thread)
		return

	# Step 3: map the port (UDP for ENet).
	var map_result: int = gateway.add_port_mapping(port, port, "CloseEncounters", "UDP")
	if map_result != UPNP.UPNP_RESULT_SUCCESS:
		# Try TCP+UDP just in case.
		map_result = gateway.add_port_mapping(port, port, "CloseEncounters", "TCP")

	# Step 4: get our public IP from the gateway.
	var ext_ip: String = gateway.query_external_address()

	var success: bool = (map_result == UPNP.UPNP_RESULT_SUCCESS) or ext_ip != ""
	call_deferred("_on_upnp_done", success, ext_ip, thread)


## Called on the main thread after UPnP completes.
func _on_upnp_done(success: bool, ip: String, thread: Thread) -> void:
	# Wait for the thread to finish (should already be done).
	if thread != null and thread.is_started():
		thread.wait_to_finish()

	_upnp_mapped = success
	if ip != "":
		public_ip = ip

	if success:
		print("[NetworkManager] UPnP port forwarded. Public IP: %s" % public_ip)
		# Now register with the online lobby server.
		_register_with_lobby()
	else:
		push_warning("[NetworkManager] UPnP failed. LAN play still works. For internet play, manually forward port %d." % _hosted_port)

	upnp_completed.emit(success)


## Remove UPnP port mapping and clean up.
func _cleanup_upnp() -> void:
	if _upnp != null and _upnp_mapped:
		# Try to remove the mapping (best effort).
		for i in range(_upnp.get_device_count()):
			var dev: UPNPDevice = _upnp.get_device(i)
			if dev.is_valid_gateway():
				dev.delete_port_mapping(_hosted_port, "UDP")
				dev.delete_port_mapping(_hosted_port, "TCP")
				break
		_upnp_mapped = false
	_upnp = null


# ---------------------------------------------------------------------------
# Multiplayer signal handlers (private)
# ---------------------------------------------------------------------------

func _on_peer_connected(peer_id: int) -> void:
	players[peer_id] = {"name": "Player_%d" % peer_id}
	player_ready_states[peer_id] = false
	player_connected.emit(peer_id)
	print("[NetworkManager] Peer %d connected. Total: %d" % [peer_id, players.size()])


func _on_peer_disconnected(peer_id: int) -> void:
	players.erase(peer_id)
	player_ready_states.erase(peer_id)
	vehicle_data_received.erase(peer_id)
	player_disconnected.emit(peer_id)
	print("[NetworkManager] Peer %d disconnected. Total: %d" % [peer_id, players.size()])


func _on_connected_to_server() -> void:
	var my_id: int = multiplayer.get_unique_id()
	players[my_id] = {"name": "Player_%d" % my_id}
	player_ready_states[my_id] = false
	print("[NetworkManager] Connected to server. My peer ID: %d" % my_id)


func _on_connection_failed() -> void:
	push_warning("[NetworkManager] Connection to server failed.")
	connection_failed.emit()
	peer = null
	multiplayer.multiplayer_peer = null


func _on_server_disconnected() -> void:
	push_warning("[NetworkManager] Server disconnected.")
	server_disconnected.emit()
	players.clear()
	player_ready_states.clear()
	vehicle_data_received.clear()
	peer = null
	multiplayer.multiplayer_peer = null
