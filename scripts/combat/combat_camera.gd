## CombatCamera — Third-person camera, World of Tanks style.
##
## Mouse look via position polling. Each frame:
##   1. Read mouse position
##   2. Compute offset from screen center
##   3. Apply to yaw/pitch (scaled down)
##   4. Warp mouse back to center
##
## The warp-then-read issue is avoided by skipping one frame after
## each warp so the warp's fake motion is ignored.
class_name CombatCamera
extends Node3D


var target: Node3D = null
var pivot: Node3D = null
var cam: Camera3D = null

var yaw: float = 0.0
var pitch: float = -10.0
var distance: float = 16.0
var sensitivity: float = 0.08

var aim_direction: Vector3 = Vector3.FORWARD

var _screen_center: Vector2 = Vector2.ZERO
var _skip_frame: bool = false  ## Skip one frame after warping.


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
	_screen_center = get_viewport().get_visible_rect().size * 0.5
	Input.set_mouse_mode(Input.MOUSE_MODE_HIDDEN)
	get_viewport().warp_mouse(_screen_center)
	_skip_frame = true
	print("[CombatCamera] Attached to %s." % target.name)


func _process(_delta: float) -> void:
	if target == null or pivot == null:
		return

	# --- Mouse look (only when not paused) ---
	if not get_tree().paused:
		if Input.get_mouse_mode() != Input.MOUSE_MODE_HIDDEN:
			Input.set_mouse_mode(Input.MOUSE_MODE_HIDDEN)

		if _skip_frame:
			# Ignore this frame's mouse position (it's from the warp).
			_skip_frame = false
		else:
			var mouse_pos: Vector2 = get_viewport().get_mouse_position()
			var dx: float = mouse_pos.x - _screen_center.x
			var dy: float = mouse_pos.y - _screen_center.y

			if absf(dx) > 1.0 or absf(dy) > 1.0:
				yaw -= dx * sensitivity
				pitch -= dy * sensitivity
				pitch = clampf(pitch, -60.0, 20.0)

			# Warp back to center and skip next frame's read.
			get_viewport().warp_mouse(_screen_center)
			_skip_frame = true

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
