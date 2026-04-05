## AudioManager (Autoload Singleton)
##
## Manages all sound playback in the game: background music and sound effects.
##
## Music:   Played through a single [AudioStreamPlayer]. Supports crossfading
##          between tracks.
## SFX:     Played through a pool of [AudioStreamPlayer] nodes. When a sound
##          is requested, the first idle player in the pool is used. If all are
##          busy, the oldest one is recycled.
##
## Volume controls map a 0.0-1.0 linear range to decibels. A value of 0.0
## means -80 dB (effectively silent); 1.0 means 0 dB (full volume).
##
## All methods gracefully handle null [AudioStream] arguments so the game
## won't crash before audio assets are added.
extends Node


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## Number of SFX AudioStreamPlayers to keep in the pool. Increase this if
## you hear sound effects cutting each other off.
const SFX_POOL_SIZE: int = 5

## Minimum dB value (treated as silent).
const MIN_DB: float = -80.0

## The audio bus names. These must match the buses defined in Godot's
## Audio Bus Layout (default_bus_layout.tres). Godot always has a "Master"
## bus; you'll need to create "Music" and "SFX" buses in the editor.
const BUS_MASTER: StringName = &"Master"
const BUS_MUSIC: StringName = &"Music"
const BUS_SFX: StringName = &"SFX"


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## The currently playing music stream, or null if nothing is playing.
var current_music: AudioStream = null


# ---------------------------------------------------------------------------
# Private variables
# ---------------------------------------------------------------------------

## Dedicated player for background music.
var _music_player: AudioStreamPlayer = null

## Array of AudioStreamPlayers used for short sound effects.
var _sfx_pool: Array[AudioStreamPlayer] = []

## Tween used for music fade-in / fade-out. Keeping a reference lets us
## kill an in-progress fade when a new one starts.
var _music_tween: Tween = null


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

## Create the audio players as child nodes so they persist with this autoload.
func _ready() -> void:
	_setup_music_player()
	_setup_sfx_pool()


# ---------------------------------------------------------------------------
# Setup helpers (private)
# ---------------------------------------------------------------------------

## Create the dedicated music AudioStreamPlayer.
func _setup_music_player() -> void:
	_music_player = AudioStreamPlayer.new()
	_music_player.name = "MusicPlayer"
	# Route to the "Music" bus if it exists; otherwise falls back to "Master".
	_music_player.bus = BUS_MUSIC
	add_child(_music_player)


## Create a pool of AudioStreamPlayers for SFX playback.
func _setup_sfx_pool() -> void:
	for i: int in range(SFX_POOL_SIZE):
		var player: AudioStreamPlayer = AudioStreamPlayer.new()
		player.name = "SFXPlayer_%d" % i
		player.bus = BUS_SFX
		add_child(player)
		_sfx_pool.append(player)


# ---------------------------------------------------------------------------
# Music playback
# ---------------------------------------------------------------------------

## Start playing [param stream] as background music. If music is already
## playing, it will crossfade over [param fade_in] seconds.
## Pass null to stop music instead.
func play_music(stream: AudioStream, fade_in: float = 0.5) -> void:
	if stream == null:
		push_warning("[AudioManager] play_music() called with null stream. Stopping music.")
		stop_music(fade_in)
		return

	# If the same track is already playing, do nothing.
	if stream == current_music and _music_player.playing:
		return

	# Kill any in-progress fade.
	if _music_tween != null and _music_tween.is_valid():
		_music_tween.kill()

	# If something is already playing, fade it out first, then start new.
	if _music_player.playing:
		_music_tween = create_tween()
		_music_tween.tween_property(_music_player, "volume_db", MIN_DB, fade_in * 0.5)
		_music_tween.tween_callback(_start_new_music.bind(stream, fade_in))
	else:
		_start_new_music(stream, fade_in)


## Internal: set the stream and fade it in.
func _start_new_music(stream: AudioStream, fade_in: float) -> void:
	current_music = stream
	_music_player.stream = stream
	_music_player.volume_db = MIN_DB
	_music_player.play()

	# Fade in from silence to 0 dB.
	_music_tween = create_tween()
	_music_tween.tween_property(_music_player, "volume_db", 0.0, fade_in)


## Fade out and stop the current music over [param fade_out] seconds.
func stop_music(fade_out: float = 0.5) -> void:
	if not _music_player.playing:
		return

	# Kill any in-progress fade.
	if _music_tween != null and _music_tween.is_valid():
		_music_tween.kill()

	_music_tween = create_tween()
	_music_tween.tween_property(_music_player, "volume_db", MIN_DB, fade_out)
	_music_tween.tween_callback(_music_player.stop)
	# Clear the reference after stopping.
	_music_tween.tween_callback(func() -> void: current_music = null)


# ---------------------------------------------------------------------------
# SFX playback
# ---------------------------------------------------------------------------

## Play a sound effect. Uses the first idle player in the pool; if all are
## busy, recycles the first one (oldest sound gets cut).
## [param stream] - the AudioStream to play.
## [param position] - unused for 2D playback; reserved for future 3D support.
func play_sfx(stream: AudioStream, _position: Vector3 = Vector3.ZERO) -> void:
	if stream == null:
		push_warning("[AudioManager] play_sfx() called with null stream. Ignoring.")
		return

	var player: AudioStreamPlayer = _get_available_sfx_player()
	player.stream = stream
	player.play()


## Play a sound effect attached to a [Node3D] for positional audio.
## NOTE: This currently uses non-positional players. For true 3D audio,
## swap the pool to AudioStreamPlayer3D nodes.
func play_sfx_at(stream: AudioStream, _node: Node3D) -> void:
	if stream == null:
		push_warning("[AudioManager] play_sfx_at() called with null stream. Ignoring.")
		return

	# For now, just play through the regular pool. A future improvement would
	# parent an AudioStreamPlayer3D to the node for spatialized sound.
	play_sfx(stream)


## Find the first idle AudioStreamPlayer in the pool. If all are busy,
## return the first one (it will be interrupted).
func _get_available_sfx_player() -> AudioStreamPlayer:
	for player: AudioStreamPlayer in _sfx_pool:
		if not player.playing:
			return player
	# All busy -- recycle the first (oldest).
	return _sfx_pool[0]


# ---------------------------------------------------------------------------
# Volume controls
# ---------------------------------------------------------------------------

## Set the master volume. [param volume] is linear 0.0 (silent) to 1.0 (full).
func set_master_volume(volume: float) -> void:
	_set_bus_volume(BUS_MASTER, volume)


## Set the music volume. [param volume] is linear 0.0 to 1.0.
func set_music_volume(volume: float) -> void:
	_set_bus_volume(BUS_MUSIC, volume)


## Set the SFX volume. [param volume] is linear 0.0 to 1.0.
func set_sfx_volume(volume: float) -> void:
	_set_bus_volume(BUS_SFX, volume)


## Internal: map a linear 0.0-1.0 value to decibels and apply it to the
## named audio bus. If the bus doesn't exist yet, prints a warning.
func _set_bus_volume(bus_name: StringName, volume: float) -> void:
	var bus_index: int = AudioServer.get_bus_index(bus_name)
	if bus_index == -1:
		push_warning(
			"[AudioManager] Audio bus '%s' not found. Create it in the "
			% bus_name
			+ "Audio Bus Layout (Project > Audio Bus Layout)."
		)
		return

	# Clamp to [0, 1] then convert to dB.
	volume = clampf(volume, 0.0, 1.0)
	var db: float
	if volume <= 0.0:
		db = MIN_DB
	else:
		db = linear_to_db(volume)

	AudioServer.set_bus_volume_db(bus_index, db)
