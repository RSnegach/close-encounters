## CombatCamera — World of Tanks style camera.
##
## The mouse cursor is visible and acts as the crosshair/reticle.
## Camera rotates based on cursor offset from screen center:
##   - Cursor right of center → camera turns right
##   - Further from center → faster rotation
##   - Cursor at center → no rotation
##
## This matches how World of Tanks handles mouse look.
class_name CombatCamera
extends Node3D


var target: Node3D = null
var pivot: Node3D = null
var cam: Camera3D = null

var yaw: float = 0.0
var pitch: float = -10.0
var distance: float = 16.0

## How fast the camera rotates when cursor is at the screen edge.
## Degrees per second at full offset.
var turn_speed: float = 90.0

var aim_direction: Vector3 = Vector3.FORWARD
var _screen_center: Vector2 = Vector2.ZERO
var _screen_size: Vector2 = Vector2.ZERO


func _ready() -> void:
	pivot = Node3D.new()
	pivot.name = "CamPivot"
	add_child(pivot)

	cam = Camera3D.new()
	cam.name = "CombatCam"
	cam.position = Vector3(0, 0, distance)
	pivot.add_child(cam)
	cam.current = true

	process_mode = Node.PROCESS_MODE_ALWAYS


func setup(target_node: Node3D) -> void:
	target = target_node
	yaw = 0.0
	pitch = -10.0
	_screen_size = get_viewport().get_visible_rect().size
	_screen_center = _screen_size * 0.5
	# Cursor stays visible — it IS the crosshair.
	Input.set_mouse_mode(Input.MOUSE_MODE_CONFINED)
	print("[CombatCamera] WoT-style camera attached to %s." % target.name)


func _process(delta: float) -> void:
	if target == null or pivot == null:
		return

	# Update screen size in case window was resized.
	_screen_size = get_viewport().get_visible_rect().size
	_screen_center = _screen_size * 0.5

	# --- Mouse-driven camera rotation (only when not paused) ---
	if not get_tree().paused:
		# Keep cursor confined during gameplay.
		if Input.get_mouse_mode() != Input.MOUSE_MODE_CONFINED:
			Input.set_mouse_mode(Input.MOUSE_MODE_CONFINED)

		var mouse_pos: Vector2 = get_viewport().get_mouse_position()

		# Offset from center, normalized to -1..+1 range.
		var offset_x: float = (mouse_pos.x - _screen_center.x) / _screen_center.x
		var offset_y: float = (mouse_pos.y - _screen_center.y) / _screen_center.y

		# Dead zone: ignore tiny movements near center.
		if absf(offset_x) < 0.05:
			offset_x = 0.0
		if absf(offset_y) < 0.05:
			offset_y = 0.0

		# Apply rotation. Further from center = faster turn.
		yaw -= offset_x * turn_speed * delta
		pitch -= offset_y * turn_speed * delta
		pitch = clampf(pitch, -60.0, 20.0)

	# --- Camera transform ---
	global_position = target.global_position + Vector3(0, 4, 0)
	pivot.rotation_degrees = Vector3(pitch, yaw, 0)
	cam.position.z = distance

	# --- Aim direction ---
	aim_direction = -pivot.global_transform.basis.z

	# --- Push to vehicle ---
	if target.get("_camera_yaw") != null:
		target._camera_yaw = yaw
	if target.get("_camera_pitch") != null:
		target._camera_pitch = pitch
	if target.get("aim_direction") != null:
		target.aim_direction = aim_direction
