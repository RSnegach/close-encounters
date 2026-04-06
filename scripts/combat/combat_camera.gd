## CombatCamera — Standalone third-person camera with mouse look.
##
## Uses position-delta polling instead of InputEventMouseMotion.
## Works with VISIBLE mouse mode (no CAPTURED needed).
## The cursor IS the crosshair — it stays centered on screen.
class_name CombatCamera
extends Node3D


var target: Node3D = null
var pivot: Node3D = null
var cam: Camera3D = null
var yaw: float = 0.0
var pitch: float = -15.0
var distance: float = 14.0
var sensitivity: float = 0.3

var aim_direction: Vector3 = Vector3.FORWARD

## Screen center for mouse warping.
var _screen_center: Vector2 = Vector2.ZERO
## Whether to apply mouse look (disabled when paused).
var _mouse_look_active: bool = false


func _ready() -> void:
	pivot = Node3D.new()
	pivot.name = "CamPivot"
	add_child(pivot)

	cam = Camera3D.new()
	cam.name = "CombatCam"
	cam.position = Vector3(0, 0, distance)
	pivot.add_child(cam)
	cam.current = true
	pivot.rotation_degrees.x = pitch

	process_mode = Node.PROCESS_MODE_ALWAYS
	_screen_center = Vector2(640, 360)  # Will be updated on first frame.


func setup(target_node: Node3D) -> void:
	target = target_node
	yaw = rad_to_deg(target.global_rotation.y) + 180.0
	_mouse_look_active = true
	# Use HIDDEN so cursor is invisible but position is trackable.
	Input.set_mouse_mode(Input.MOUSE_MODE_HIDDEN)
	# Get actual screen center.
	_screen_center = get_viewport().get_visible_rect().size * 0.5
	# Warp mouse to center to start clean.
	get_viewport().warp_mouse(_screen_center)
	print("[CombatCamera] Attached to %s." % target.name)


func _process(_delta: float) -> void:
	if target == null or pivot == null:
		return

	# --- Mouse look via position polling (not _input events) ---
	if _mouse_look_active and not get_tree().paused:
		var mouse_pos: Vector2 = get_viewport().get_mouse_position()
		var delta_mouse: Vector2 = mouse_pos - _screen_center

		# Only apply if mouse actually moved (ignore tiny jitter).
		if delta_mouse.length() > 0.5:
			yaw -= delta_mouse.x * sensitivity
			pitch -= delta_mouse.y * sensitivity
			pitch = clampf(pitch, -60.0, 20.0)

		# Warp back to center AFTER reading the delta.
		# This is safe because we poll position, not events.
		get_viewport().warp_mouse(_screen_center)

		# Keep cursor hidden during gameplay.
		if Input.get_mouse_mode() != Input.MOUSE_MODE_HIDDEN:
			Input.set_mouse_mode(Input.MOUSE_MODE_HIDDEN)

	# Follow the target.
	global_position = target.global_position + Vector3(0, 3, 0)
	pivot.rotation_degrees = Vector3(pitch, yaw, 0)
	cam.position.z = distance

	# Compute aim direction.
	aim_direction = -pivot.global_transform.basis.z

	# Push to the vehicle for movement and weapon aiming.
	if target.get("_camera_yaw") != null:
		target._camera_yaw = yaw
	if target.get("_camera_pitch") != null:
		target._camera_pitch = pitch
	if target.get("aim_direction") != null:
		target.aim_direction = aim_direction
