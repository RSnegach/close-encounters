## CombatCamera — World of Tanks style camera.
##
## Camera rotates based on cursor offset from screen center.
## Cursor is hidden but confined — the HUD crosshair shows aim point.
## Pitch clamped so you can't look through the ground.
class_name CombatCamera
extends Node3D


var target: Node3D = null
var pivot: Node3D = null
var cam: Camera3D = null

var yaw: float = 0.0
var pitch: float = -10.0
var distance: float = 16.0

## Degrees per second at full screen-edge offset.
var turn_speed: float = 120.0

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
	# Hidden but confined — cursor position is still trackable.
	Input.set_mouse_mode(Input.MOUSE_MODE_CONFINED_HIDDEN)
	print("[CombatCamera] Attached to %s." % target.name)


func _process(delta: float) -> void:
	if target == null or pivot == null:
		return

	_screen_size = get_viewport().get_visible_rect().size
	_screen_center = _screen_size * 0.5

	if not get_tree().paused:
		if Input.get_mouse_mode() != Input.MOUSE_MODE_CONFINED_HIDDEN:
			Input.set_mouse_mode(Input.MOUSE_MODE_CONFINED_HIDDEN)

		var mouse_pos: Vector2 = get_viewport().get_mouse_position()
		var offset_x: float = (mouse_pos.x - _screen_center.x) / _screen_center.x
		var offset_y: float = (mouse_pos.y - _screen_center.y) / _screen_center.y

		# Dead zone near center.
		if absf(offset_x) < 0.03:
			offset_x = 0.0
		if absf(offset_y) < 0.03:
			offset_y = 0.0

		yaw -= offset_x * turn_speed * delta
		pitch -= offset_y * turn_speed * delta
		# Clamp pitch: -5 at most looking down (can't go underground),
		# +20 looking up.
		pitch = clampf(pitch, -25.0, 5.0)

	# Camera transform.
	global_position = target.global_position + Vector3(0, 4, 0)
	pivot.rotation_degrees = Vector3(pitch, yaw, 0)
	cam.position.z = distance

	aim_direction = -pivot.global_transform.basis.z

	if target.get("_camera_yaw") != null:
		target._camera_yaw = yaw
	if target.get("_camera_pitch") != null:
		target._camera_pitch = pitch
	if target.get("aim_direction") != null:
		target.aim_direction = aim_direction
