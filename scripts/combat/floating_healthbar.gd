## FloatingHealthbar — A 3D healthbar that hovers above a vehicle.
##
## Shows a colored bar (green → yellow → red) with HP text.
## Semi-transparent background. Attaches to any vehicle node.
## Can be toggled on/off globally.
class_name FloatingHealthbar
extends Node3D


var target: Node3D = null      ## The vehicle this bar follows.
var bar_bg: MeshInstance3D     ## Dark background bar.
var bar_fill: MeshInstance3D   ## Colored fill bar.
var label_3d: Label3D          ## HP text.

var max_hp: float = 100.0
var current_hp: float = 100.0
var bar_width: float = 3.0     ## Width in world units.
var bar_height: float = 0.3
var y_offset: float = 4.0      ## How high above the vehicle center.



func _ready() -> void:
	add_to_group("floating_healthbar")
	# Background bar (dark semi-transparent).
	bar_bg = MeshInstance3D.new()
	var bg_mesh: BoxMesh = BoxMesh.new()
	bg_mesh.size = Vector3(bar_width, bar_height, 0.05)
	bar_bg.mesh = bg_mesh
	var bg_mat: StandardMaterial3D = StandardMaterial3D.new()
	bg_mat.albedo_color = Color(0.1, 0.1, 0.1, 0.5)
	bg_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	bg_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	bg_mat.no_depth_test = true
	bg_mat.render_priority = 10
	bar_bg.material_override = bg_mat
	bar_bg.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(bar_bg)

	# Fill bar (colored, overlaps the background).
	bar_fill = MeshInstance3D.new()
	var fill_mesh: BoxMesh = BoxMesh.new()
	fill_mesh.size = Vector3(bar_width, bar_height, 0.06)
	bar_fill.mesh = fill_mesh
	var fill_mat: StandardMaterial3D = StandardMaterial3D.new()
	fill_mat.albedo_color = Color(0.3, 0.9, 0.3, 0.7)
	fill_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	fill_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	fill_mat.no_depth_test = true
	fill_mat.render_priority = 11
	bar_fill.material_override = fill_mat
	bar_fill.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(bar_fill)

	# HP text label.
	label_3d = Label3D.new()
	label_3d.text = ""
	label_3d.font_size = 32
	label_3d.pixel_size = 0.01
	label_3d.position = Vector3(0, bar_height * 0.5 + 0.15, 0)
	label_3d.no_depth_test = true
	label_3d.render_priority = 12
	label_3d.modulate = Color(1, 1, 1, 0.9)
	label_3d.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	add_child(label_3d)

	# Billboard: always face the camera.
	# We handle this manually in _process.


func setup(vehicle_node: Node3D) -> void:
	target = vehicle_node
	# Calculate max HP from parts.
	_recalculate_max_hp()


func _process(_delta: float) -> void:
	if target == null:
		queue_free()
		return

	if not visible:
		return

	# Follow target position.
	global_position = target.global_position + Vector3(0, y_offset, 0)

	# Billboard: face the active camera.
	var cam: Camera3D = get_viewport().get_camera_3d()
	if cam:
		look_at(cam.global_position, Vector3.UP)

	# Update HP.
	_update_hp()


func _recalculate_max_hp() -> void:
	max_hp = 0.0
	current_hp = 0.0
	if target == null or target.get("parts") == null:
		return
	var seen: Dictionary = {}
	for cell in target.parts:
		var part = target.parts[cell]
		if part == null:
			continue
		var nid: int = part.get_instance_id()
		if seen.has(nid):
			continue
		seen[nid] = true
		if part.get("part_data") != null:
			max_hp += float(part.part_data.hp)
		if part.get("current_hp") != null:
			current_hp += float(part.current_hp)
	if max_hp <= 0:
		max_hp = 1.0


func _update_hp() -> void:
	# Sum current HP.
	current_hp = 0.0
	if target.get("parts") == null:
		return
	var seen: Dictionary = {}
	for cell in target.parts:
		var part = target.parts[cell]
		if part == null:
			continue
		var nid: int = part.get_instance_id()
		if seen.has(nid):
			continue
		seen[nid] = true
		if part.get("current_hp") != null:
			current_hp += float(part.current_hp)

	var pct: float = clampf(current_hp / max_hp, 0.0, 1.0)

	# Resize fill bar.
	var fill_mesh: BoxMesh = bar_fill.mesh as BoxMesh
	fill_mesh.size.x = bar_width * pct
	# Offset fill bar so it drains from right to left.
	bar_fill.position.x = -bar_width * (1.0 - pct) * 0.5

	# Color: green → yellow → red.
	var color: Color
	if pct > 0.5:
		color = Color(0.3, 0.9, 0.3, 0.7)  # Green.
	elif pct > 0.25:
		color = Color(0.9, 0.8, 0.2, 0.7)  # Yellow.
	else:
		color = Color(0.9, 0.2, 0.2, 0.7)  # Red.
	(bar_fill.material_override as StandardMaterial3D).albedo_color = color

	# Update text.
	label_3d.text = "%d / %d" % [int(current_hp), int(max_hp)]

	# Hide if dead.
	if target.get("is_alive") != null and not target.is_alive:
		visible = false
