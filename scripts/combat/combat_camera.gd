## CombatCamera — Standalone third-person camera with mouse look.
##
## Uses CAPTURED mouse mode. If that doesn't work on your setup,
## the camera also works in VISIBLE mode (when paused).
class_name CombatCamera
extends Node3D


var target: Node3D = null       ## The vehicle to follow.
var pivot: Node3D = null        ## Rotation pivot (yaw + pitch).
var cam: Camera3D = null        ## The actual camera.
var yaw: float = 0.0           ## Horizontal angle (degrees).
var pitch: float = -15.0       ## Vertical angle (degrees).
var distance: float = 14.0     ## Distance behind the target.
var sensitivity: float = 0.2   ## Mouse sensitivity.

## The direction the camera center points — used for aiming.
var aim_direction: Vector3 = Vector3.FORWARD


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

	# Process even when paused so ESC menu camera still works.
	process_mode = Node.PROCESS_MODE_ALWAYS
	# Ensure we receive input.
	set_process_input(true)


## Bind to a target vehicle and hide the cursor.
func setup(target_node: Node3D) -> void:
	target = target_node
	yaw = rad_to_deg(target.global_rotation.y) + 180.0
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	print("[CombatCamera] Attached to %s." % target.name)


func _input(event: InputEvent) -> void:
	if target == null:
		return

	if event is InputEventMouseMotion:
		var motion: InputEventMouseMotion = event as InputEventMouseMotion
		yaw -= motion.relative.x * sensitivity
		pitch -= motion.relative.y * sensitivity
		pitch = clampf(pitch, -60.0, 20.0)


func _process(_delta: float) -> void:
	if target == null or pivot == null:
		return

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
