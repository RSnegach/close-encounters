## CombatCamera — Standalone third-person camera with mouse look.
##
## Uses CONFINED mouse mode (cursor stays in window) and tracks mouse
## position delta each frame. This is more reliable than CAPTURED mode
## which doesn't work on all setups.
class_name CombatCamera
extends Node3D


var target: Node3D = null       ## The vehicle to follow.
var pivot: Node3D = null        ## Rotation pivot (yaw + pitch).
var cam: Camera3D = null        ## The actual camera.
var yaw: float = 0.0           ## Horizontal angle (degrees).
var pitch: float = -15.0       ## Vertical angle (degrees).
var distance: float = 14.0     ## Distance behind the target.
var sensitivity: float = 0.3   ## Mouse sensitivity.

## The direction the camera center points — used for aiming.
var aim_direction: Vector3 = Vector3.FORWARD

## Track previous mouse position to compute delta manually.
var _prev_mouse_pos: Vector2 = Vector2.ZERO
var _has_prev_pos: bool = false
var _screen_center: Vector2 = Vector2.ZERO


func _ready() -> void:
	# Build the camera rig.
	pivot = Node3D.new()
	pivot.name = "CamPivot"
	add_child(pivot)

	cam = Camera3D.new()
	cam.name = "CombatCam"
	cam.position = Vector3(0, 0, distance)
	pivot.add_child(cam)
	cam.current = true
	pivot.rotation_degrees.x = pitch

	# Process even when paused so ESC menu works.
	process_mode = Node.PROCESS_MODE_ALWAYS


## Bind to a target vehicle.
func setup(target_node: Node3D) -> void:
	target = target_node
	yaw = rad_to_deg(target.global_rotation.y) + 180.0
	# Use CONFINED mode — cursor stays in window, visible, generates motion.
	Input.set_mouse_mode(Input.MOUSE_MODE_CONFINED)
	_screen_center = get_viewport().get_visible_rect().size * 0.5
	print("[CombatCamera] Attached to %s. Mouse confined." % target.name)


func _input(event: InputEvent) -> void:
	if target == null:
		return

	# Handle mouse motion from both CONFINED and CAPTURED modes.
	if event is InputEventMouseMotion:
		var motion: InputEventMouseMotion = event as InputEventMouseMotion
		yaw -= motion.relative.x * sensitivity
		pitch -= motion.relative.y * sensitivity
		pitch = clampf(pitch, -60.0, 20.0)


func _process(_delta: float) -> void:
	if target == null or pivot == null:
		return

	# If the game is not paused, keep mouse confined and warp to center
	# each frame so we get infinite mouse movement (like CAPTURED mode
	# but more compatible).
	if not get_tree().paused:
		if Input.get_mouse_mode() != Input.MOUSE_MODE_CONFINED:
			Input.set_mouse_mode(Input.MOUSE_MODE_CONFINED)
		# Warp mouse back to center each frame so it never hits the edge.
		get_viewport().warp_mouse(_screen_center)

	# Follow the target.
	global_position = target.global_position + Vector3(0, 3, 0)
	pivot.rotation_degrees = Vector3(pitch, yaw, 0)
	cam.position.z = distance

	# Compute aim direction for weapons.
	aim_direction = -pivot.global_transform.basis.z

	# Push yaw/pitch/aim to the target vehicle so movement follows camera.
	if target.get("_camera_yaw") != null:
		target._camera_yaw = yaw
	if target.get("_camera_pitch") != null:
		target._camera_pitch = pitch
	if target.get("aim_direction") != null:
		target.aim_direction = aim_direction
