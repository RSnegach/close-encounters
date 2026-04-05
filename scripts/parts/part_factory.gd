## PartFactory -- Static factory for creating PartNode instances and preview meshes.
##
## This is a utility class (RefCounted, not a Node) with only static methods.
## You never instantiate it; just call PartFactory.create_part(...) etc.
##
## Responsibilities:
##   - Spawn a fully-configured [PartNode] from a [PartData] and grid position.
##   - Create semi-transparent "ghost" preview meshes for the builder UI.
##   - Provide color and mesh helpers used by the above.
class_name PartFactory
extends RefCounted


# ---------------------------------------------------------------------------
# Part creation
# ---------------------------------------------------------------------------

## Create a new [PartNode] from a [PartData] definition and place it at
## [param grid_pos] on the build grid. The returned node is ready to be
## added to a vehicle's scene tree.
##
## Usage:
##   var node: PartNode = PartFactory.create_part(my_part_data, Vector3i(2, 0, 1))
##   vehicle_root.add_child(node)
static func create_part(data: PartData, grid_pos: Vector3i) -> PartNode:
	if data == null:
		push_error("[PartFactory] create_part() called with null PartData.")
		return null

	var node: PartNode = PartNode.new()
	node.setup(data, grid_pos)
	return node


# ---------------------------------------------------------------------------
# Preview mesh (for builder ghost)
# ---------------------------------------------------------------------------

## Create a semi-transparent mesh used by the builder to show where a part
## will be placed before the player confirms. The mesh has the same size
## and color as the real part but is translucent (alpha = 0.4).
##
## Returns a standalone [MeshInstance3D] that can be added anywhere in the
## scene tree (it is NOT a PartNode).
static func create_preview_mesh(data: PartData) -> MeshInstance3D:
	if data == null:
		push_error("[PartFactory] create_preview_mesh() called with null PartData.")
		return null

	# Determine color from mesh_data, defaulting to a light blue.
	var color_hex: String = data.mesh_data.get("color", "#6699CC")
	var color: Color = color_from_hex(color_hex)

	# Make the color semi-transparent for the ghost effect.
	color.a = 0.4

	# Build the box mesh with the translucent color.
	var size: Vector3 = Vector3(data.size.x, data.size.y, data.size.z)
	var mesh: BoxMesh = create_box_mesh(size, color)

	# The material must have transparency enabled for the alpha to work.
	var mat: StandardMaterial3D = mesh.material as StandardMaterial3D
	if mat != null:
		mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA

	# Wrap in a MeshInstance3D.
	var instance: MeshInstance3D = MeshInstance3D.new()
	instance.name = "PreviewMesh_%s" % data.id
	instance.mesh = mesh
	return instance


# ---------------------------------------------------------------------------
# Color utilities
# ---------------------------------------------------------------------------

## Parse a hex color string (e.g. "#FF0000" or "FF0000") into a Godot
## [Color]. Returns [Color.WHITE] if the string is invalid.
static func color_from_hex(hex: String) -> Color:
	if hex.is_empty():
		return Color.WHITE

	# Color.from_string handles both "#RRGGBB" and "RRGGBB" formats.
	# The second argument is the fallback if parsing fails.
	return Color.from_string(hex, Color.WHITE)


# ---------------------------------------------------------------------------
# Mesh utilities
# ---------------------------------------------------------------------------

## Create a [BoxMesh] with the given [param size] (in meters) and apply a
## [StandardMaterial3D] with the given [param color] as the albedo.
##
## The material is set directly on the mesh resource (not on the
## MeshInstance3D), so every MeshInstance3D sharing this mesh will have the
## same color. If you need per-instance color, duplicate the material on the
## MeshInstance3D instead.
static func create_box_mesh(size: Vector3, color: Color) -> BoxMesh:
	var box: BoxMesh = BoxMesh.new()
	box.size = size

	# Create a simple unlit-looking material with the given color.
	var material: StandardMaterial3D = StandardMaterial3D.new()
	material.albedo_color = color
	box.material = material

	return box
