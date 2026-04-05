## PartNode -- 3D representation of a placed part in the game world.
##
## Each part on a vehicle gets one PartNode as a child of the vehicle's root
## Node3D. The PartNode creates its own mesh and collision shape based on the
## [PartData] it is set up with.
##
## During combat, PartNodes track their own health and emit signals when
## damaged or destroyed, so the vehicle can react (e.g. lose functionality
## when the engine is blown off).
class_name PartNode
extends Node3D


# ---------------------------------------------------------------------------
# Signals
# ---------------------------------------------------------------------------

## Emitted when this part takes damage. [param damage] is the amount dealt.
signal part_damaged(damage: int)

## Emitted when this part's HP reaches zero and it is destroyed.
signal part_destroyed


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## Reference to the immutable part definition (stats, cost, size, etc.).
## Set once via [method setup]; do not change at runtime.
var part_data: PartData = null

## Current hit points. Starts at [member PartData.hp] and decreases as
## the part takes damage.
var current_hp: int = 0

## The integer grid position where this part was placed in the builder.
## (0, 0, 0) is the vehicle's origin cell.
var grid_position: Vector3i = Vector3i.ZERO

## The MeshInstance3D child that provides the part's visual appearance.
var mesh_instance: MeshInstance3D = null

## The CollisionShape3D that defines the part's physics volume.
## This is added as a child of PartNode; the parent vehicle's physics body
## will pick it up.
var collision_shape: CollisionShape3D = null

## Whether this part has been destroyed (HP <= 0). Destroyed parts remain
## in the scene (as wreckage) but stop functioning.
var is_destroyed: bool = false


# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

## Initialize this PartNode with a [PartData] definition and a grid position.
## Creates the mesh and collision shape as child nodes.
## Call this once right after instantiating the node (before adding to tree
## is fine, but after is also okay).
func setup(data: PartData, grid_pos: Vector3i) -> void:
	if data == null:
		push_error("[PartNode] setup() called with null PartData.")
		return

	part_data = data
	grid_position = grid_pos
	current_hp = part_data.hp
	is_destroyed = false

	# Name the node after the part for easier debugging in the scene tree.
	name = part_data.id + "_" + str(grid_pos)

	# Position the node in world space based on its grid cell.
	# Each grid cell is 1 meter on a side, so the conversion is direct.
	position = Vector3(grid_pos.x, grid_pos.y, grid_pos.z)

	# Build visual and collision children.
	_create_mesh()
	_create_collision()


# ---------------------------------------------------------------------------
# Mesh creation (private)
# ---------------------------------------------------------------------------

## Create a [MeshInstance3D] child representing this part visually.
## Uses [member PartData.mesh_data] to determine the shape and color.
## Falls back to a white box if mesh_data is missing or incomplete.
func _create_mesh() -> void:
	mesh_instance = MeshInstance3D.new()
	mesh_instance.name = "Mesh"

	# Determine the color from mesh_data, defaulting to white.
	var color_hex: String = part_data.mesh_data.get("color", "#FFFFFF")
	var color: Color = Color.WHITE
	if color_hex != "":
		color = Color.from_string(color_hex, Color.WHITE)

	# Create a BoxMesh sized to the part's grid footprint.
	# Each grid unit = 1 meter, so a size of (2,1,3) = 2m x 1m x 3m box.
	var box: BoxMesh = BoxMesh.new()
	box.size = Vector3(part_data.size.x, part_data.size.y, part_data.size.z)

	# Apply a simple colored material.
	var material: StandardMaterial3D = StandardMaterial3D.new()
	material.albedo_color = color
	box.material = material

	mesh_instance.mesh = box
	add_child(mesh_instance)


# ---------------------------------------------------------------------------
# Collision creation (private)
# ---------------------------------------------------------------------------

## Create a [CollisionShape3D] child matching the part's size.
## The shape is a [BoxShape3D] with the same dimensions as the visual mesh.
func _create_collision() -> void:
	collision_shape = CollisionShape3D.new()
	collision_shape.name = "Collision"

	var box_shape: BoxShape3D = BoxShape3D.new()
	# BoxShape3D.size is full extents (same convention as BoxMesh.size).
	box_shape.size = Vector3(part_data.size.x, part_data.size.y, part_data.size.z)
	collision_shape.shape = box_shape

	add_child(collision_shape)


# ---------------------------------------------------------------------------
# Damage system
# ---------------------------------------------------------------------------

## Apply [param damage] hit points of damage to this part.
## Returns true if this hit destroyed the part (HP reached 0), false otherwise.
func take_damage(damage: int) -> bool:
	if is_destroyed:
		# Already destroyed -- ignore further damage.
		return false

	current_hp -= damage

	# Clamp to zero (don't go negative).
	if current_hp < 0:
		current_hp = 0

	part_damaged.emit(damage)

	# Check if the part just died.
	if current_hp <= 0:
		destroy()
		return true

	return false


## Mark this part as destroyed. Applies a visual darkening effect and
## emits [signal part_destroyed].
func destroy() -> void:
	is_destroyed = true
	current_hp = 0

	# Visual feedback: darken the mesh to show wreckage.
	if mesh_instance != null and mesh_instance.mesh != null:
		var mat: Material = mesh_instance.mesh.surface_get_material(0)
		if mat is StandardMaterial3D:
			# Duplicate so we don't affect other parts sharing the same mesh.
			var dark_mat: StandardMaterial3D = mat.duplicate() as StandardMaterial3D
			# Darken to ~20% brightness to look like burnt wreckage.
			dark_mat.albedo_color = dark_mat.albedo_color.darkened(0.8)
			mesh_instance.mesh.surface_set_material(0, dark_mat)

	part_destroyed.emit()


## Restore [param amount] hit points, up to the part's maximum HP.
## Only works if the part is not destroyed. If you want to repair a
## destroyed part, set [member is_destroyed] to false first.
func repair(amount: int) -> void:
	if is_destroyed:
		push_warning("[PartNode] Cannot repair destroyed part '%s'. Un-destroy it first." % name)
		return

	current_hp = mini(current_hp + amount, part_data.hp)


# ---------------------------------------------------------------------------
# Query helpers
# ---------------------------------------------------------------------------

## Return the axis-aligned bounding box of this part in world space.
## Useful for overlap checks, camera framing, etc.
func get_world_bounds() -> AABB:
	if mesh_instance != null:
		return mesh_instance.get_aabb()
	# Fallback: construct an AABB from the part's grid size.
	var half: Vector3 = Vector3(part_data.size) * 0.5
	return AABB(global_position - half, Vector3(part_data.size))


## Returns true if this part is still functional (not destroyed).
## Destroyed parts stay in the scene as wreckage but don't contribute
## to the vehicle's capabilities.
func is_functional() -> bool:
	return not is_destroyed
