## VehicleBuilder (Node3D)
##
## The build-mode controller. Handles the 3D grid where the player places and
## removes parts to design a vehicle before combat.
##
## Features:
##   - 3D grid visualization (wireframe lines)
##   - Mouse ray-casting to determine which grid cell the cursor is over
##   - Ghost mesh preview of the selected part (green = valid, red = blocked)
##   - Placement validation (bounds, overlap, budget, domain)
##   - Part removal (right-click or delete mode)
##   - Orbit camera for inspecting the build from any angle
##   - Connectivity check (BFS flood-fill) to ensure all parts are connected
##   - Domain-specific build validation (wings for air, hull for water, etc.)
##
## Typical lifecycle:
##   1. Add this scene to the build screen.
##   2. Call setup(domain, budget) to configure the grid.
##   3. External UI sets selected_part_data when the player picks from a catalog.
##   4. Player clicks to place / right-clicks to remove.
##   5. Call validate_build() before transitioning to combat.
##   6. Call get_vehicle_data() to serialize the build.
class_name VehicleBuilder
extends Node3D


# ---------------------------------------------------------------------------
# Signals
# ---------------------------------------------------------------------------

## Emitted when a part is successfully placed on the grid.
signal part_placed(part_data: PartData, grid_pos: Vector3i)

## Emitted when a part is removed from the grid.
signal part_removed(grid_pos: Vector3i)

## Emitted when the remaining budget changes (placement or removal).
signal budget_changed(remaining: int)

## Emitted after validate_build() completes. [param is_valid] is true when the
## build passes all checks. [param issues] lists human-readable problem strings.
signal build_validated(is_valid: bool, issues: Array[String])


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

## Color for the grid lines.
const GRID_LINE_COLOR: Color = Color(0.4, 0.4, 0.5, 0.3)

## Ghost mesh tint when placement is allowed.
const GHOST_VALID_COLOR: Color = Color(0.2, 0.9, 0.2, 0.4)

## Ghost mesh tint when placement is blocked.
const GHOST_INVALID_COLOR: Color = Color(0.9, 0.2, 0.2, 0.4)

## Camera orbit sensitivity (degrees per pixel of mouse motion).
const ORBIT_SENSITIVITY: float = 0.3

## Camera zoom step per scroll tick (meters).
const ZOOM_STEP: float = 1.0

## Minimum / maximum camera distance from the pivot.
const ZOOM_MIN: float = 3.0
const ZOOM_MAX: float = 30.0


# ---------------------------------------------------------------------------
# Exported / configurable variables
# ---------------------------------------------------------------------------

## Number of cells along each axis.
var grid_size: Vector3i = Vector3i(7, 5, 7)

## Meters per grid cell. Matches Vehicle.CELL_SIZE.
var cell_size: float = 1.0


# ---------------------------------------------------------------------------
# Internal state
# ---------------------------------------------------------------------------

## Dictionary mapping Vector3i grid positions to the PartNode placed there.
## Multi-cell parts occupy multiple keys.
var placed_parts: Dictionary = {}

## The PartData the player currently has selected in the catalog UI.
## null means nothing is selected (freehand / delete mode).
var selected_part_data: PartData = null

## Transparent preview mesh shown under the cursor while a part is selected.
var ghost_mesh: MeshInstance3D = null

## Root node that holds the visual grid line meshes.
var grid_visual: Node3D = null

## Orbit camera pivot sits at the center of the grid.
var camera_pivot: Node3D = null

## Actual Camera3D, parented to camera_pivot and offset along -Z.
var camera: Camera3D = null

## How much money the player has left to spend.
var budget_remaining: int = 0

## The domain for this build session. Controls which parts are allowed.
var build_domain: String = "ground"

## When true, left-click removes parts instead of placing them.
var is_delete_mode: bool = false

## An invisible horizontal plane used for mouse raycasting to find the grid
## cell under the cursor.
var raycast_plane: StaticBody3D = null

## The grid cell the mouse is currently hovering over. Updated every frame.
var _hovered_cell: Vector3i = Vector3i.ZERO

## Whether the player is currently dragging to orbit the camera.
var _is_orbiting: bool = false

## Ghost material instance (reused and recolored each frame).
var _ghost_material: StandardMaterial3D = null


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

## Initialize the grid visual, camera rig, raycast plane, and ghost mesh.
func _ready() -> void:
	_setup_orbit_camera()
	_create_grid_visual()
	_create_raycast_plane()
	_setup_ghost_mesh()


## Every frame: update the ghost preview position and tint.
func _process(_delta: float) -> void:
	_update_ghost()


## Handle mouse clicks, keyboard shortcuts, and camera controls.
func _unhandled_input(event: InputEvent) -> void:
	# --- Mouse button events ---
	if event is InputEventMouseButton:
		var mb: InputEventMouseButton = event as InputEventMouseButton

		# Left click: place or delete
		if mb.button_index == MOUSE_BUTTON_LEFT and mb.pressed:
			if is_delete_mode:
				remove_part(_hovered_cell)
			else:
				if selected_part_data != null:
					place_part(selected_part_data, _hovered_cell)

		# Right click: always remove the part under the cursor
		if mb.button_index == MOUSE_BUTTON_RIGHT and mb.pressed:
			remove_part(_hovered_cell)

		# Middle mouse: begin / end orbit drag
		if mb.button_index == MOUSE_BUTTON_MIDDLE:
			_is_orbiting = mb.pressed

		# Scroll wheel: zoom camera
		if mb.button_index == MOUSE_BUTTON_WHEEL_UP and mb.pressed:
			_zoom_camera(-1.0)
		if mb.button_index == MOUSE_BUTTON_WHEEL_DOWN and mb.pressed:
			_zoom_camera(1.0)

	# --- Mouse motion: orbit camera or update hover cell ---
	if event is InputEventMouseMotion:
		var mm: InputEventMouseMotion = event as InputEventMouseMotion
		if _is_orbiting:
			_orbit_camera(mm)
		else:
			_hovered_cell = _get_grid_position_from_mouse()

	# --- Keyboard: toggle delete mode ---
	if event is InputEventKey:
		var key: InputEventKey = event as InputEventKey
		if key.pressed and key.physical_keycode == KEY_X:
			is_delete_mode = not is_delete_mode
			print("[Builder] Delete mode: %s" % ("ON" if is_delete_mode else "OFF"))


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

## Configure the builder for a new build session.
## [param p_domain] is one of the five combat domains.
## [param p_budget] is the starting dollar amount.
func setup(p_domain: String, p_budget: int) -> void:
	build_domain = p_domain
	# Budget of 0 means "Unlimited" — use -1 internally to bypass checks.
	budget_remaining = -1 if p_budget <= 0 else p_budget
	clear_build()
	budget_changed.emit(budget_remaining)


## Attempt to place [param data] at [param pos].
## Returns true if the placement succeeded.
func place_part(data: PartData, pos: Vector3i) -> bool:
	if not _can_place_part(data, pos):
		return false

	# Create the PartNode through the factory.
	var part_node: PartNode = PartFactory.create_part(data, pos)
	if part_node == null:
		push_error("[Builder] PartFactory returned null for '%s'." % data.id)
		return false

	add_child(part_node)
	part_node.position = Vector3(pos) * cell_size

	# Register every cell the part occupies.
	for x: int in range(data.size.x):
		for y: int in range(data.size.y):
			for z: int in range(data.size.z):
				placed_parts[pos + Vector3i(x, y, z)] = part_node

	# Deduct cost.
	budget_remaining -= data.cost

	part_placed.emit(data, pos)
	budget_changed.emit(budget_remaining)
	return true


## Remove the part at [param pos] (if any). Multi-cell parts are fully removed
## regardless of which cell is clicked. Refunds the cost.
## Returns true if a part was removed.
func remove_part(pos: Vector3i) -> bool:
	if not placed_parts.has(pos):
		return false

	var part_node: PartNode = placed_parts[pos]
	var part_data: PartData = part_node.part_data

	# Find and erase every cell that references this same node.
	var keys_to_erase: Array[Vector3i] = []
	for cell: Vector3i in placed_parts:
		if placed_parts[cell] == part_node:
			keys_to_erase.append(cell)
	for key: Vector3i in keys_to_erase:
		placed_parts.erase(key)

	# Refund the cost.
	budget_remaining += part_data.cost

	# Remove the node from the scene tree.
	part_node.queue_free()

	part_removed.emit(pos)
	budget_changed.emit(budget_remaining)
	return true


## Run all validation checks on the current build.
## Returns a dictionary: { "valid": bool, "issues": Array[String] }.
func validate_build() -> Dictionary:
	var issues: Array[String] = []

	# --- Required part checks ---
	var has_control: bool = false
	var has_propulsion: bool = false
	var has_hull: bool = false
	var has_wings: bool = false
	var has_ballast: bool = false
	var total_thrust: float = 0.0
	var total_mass: float = 0.0

	var unique_parts: Array[PartNode] = _get_unique_parts()

	for part_node: PartNode in unique_parts:
		var pd: PartData = part_node.part_data
		if pd == null:
			continue
		match pd.category:
			"control":
				has_control = true
			"propulsion":
				has_propulsion = true

		# Domain-specific part detection via subcategory / stats.
		if pd.subcategory in ["hull", "keel"]:
			has_hull = true
		if pd.subcategory in ["wing", "aerodynamic_frame"]:
			has_wings = true
		if pd.stats.has("ballast"):
			has_ballast = true
		if pd.stats.has("thrust"):
			total_thrust += float(pd.stats["thrust"])
		total_mass += pd.mass_kg

	if not has_control:
		issues.append("Vehicle needs a control module (cockpit / bridge).")
	if not has_propulsion:
		issues.append("Vehicle needs at least one propulsion part.")

	# --- Domain-specific checks ---
	match build_domain:
		"air":
			if not has_wings:
				issues.append("Aircraft need wings or aerodynamic frames for lift.")
		"water":
			if not has_hull:
				issues.append("Watercraft need a hull or keel to float.")
		"submarine":
			if not has_hull:
				issues.append("Submarines need a pressure hull.")
			if not has_ballast:
				issues.append("Submarines need ballast tanks to dive / surface.")
		"space":
			# Thrust-to-weight ratio must exceed 1.0 to lift off.
			var twr: float = 0.0
			if total_mass > 0.0:
				twr = total_thrust / (total_mass * 9.8)
			if twr < 1.0:
				issues.append(
					"Rocket TWR is %.2f — must be > 1.0 to launch. Add more thrust or reduce mass." % twr
				)

	# --- Connectivity check ---
	if unique_parts.size() > 1 and not _check_connectivity():
		issues.append("Not all parts are connected. Every part must be reachable via adjacent cells.")

	var is_valid: bool = issues.is_empty()
	build_validated.emit(is_valid, issues)
	return {"valid": is_valid, "issues": issues}


## Serialize the current build to a dictionary for saving / combat loading.
func get_vehicle_data() -> Dictionary:
	var parts_array: Array = []
	var seen: Dictionary = {}

	for cell: Vector3i in placed_parts:
		var part_node: PartNode = placed_parts[cell]
		var nid: int = part_node.get_instance_id()
		if seen.has(nid):
			continue
		seen[nid] = true
		parts_array.append({
			"id": part_node.part_data.id,
			"grid_position": [
				part_node.grid_position.x,
				part_node.grid_position.y,
				part_node.grid_position.z,
			],
		})

	return {
		"parts": parts_array,
		"domain": build_domain,
	}


## Load a previously saved vehicle design and place all its parts.
func load_vehicle_data(data: Dictionary) -> void:
	clear_build()

	build_domain = data.get("domain", build_domain)
	var parts_list: Array = data.get("parts", [])

	for entry: Dictionary in parts_list:
		var part_id: String = entry.get("id", "")
		var pd: PartData = PartRegistry.get_part(part_id)
		if pd == null:
			push_warning("[Builder] Unknown part '%s' in saved data. Skipping." % part_id)
			continue

		var pos_arr: Array = entry.get("grid_position", [0, 0, 0])
		var gpos := Vector3i(int(pos_arr[0]), int(pos_arr[1]), int(pos_arr[2]))
		place_part(pd, gpos)


## Remove every placed part and reset the budget.
func clear_build() -> void:
	for part_node: PartNode in _get_unique_parts():
		part_node.queue_free()
	placed_parts.clear()
	# Reset budget to whatever was configured by setup().
	# (budget_remaining is set externally via setup().)


## Return the total cost of all placed parts.
func get_total_cost() -> int:
	var cost: int = 0
	for part_node: PartNode in _get_unique_parts():
		cost += part_node.part_data.cost
	return cost


## Return the total mass (kg) of all placed parts.
func get_total_mass() -> float:
	var mass_sum: float = 0.0
	for part_node: PartNode in _get_unique_parts():
		mass_sum += part_node.part_data.mass_kg
	return mass_sum


# ---------------------------------------------------------------------------
# Placement validation
# ---------------------------------------------------------------------------

## Check whether [param data] can legally be placed at [param pos].
## Considers grid bounds, cell overlap, budget, and domain compatibility.
func _can_place_part(data: PartData, pos: Vector3i) -> bool:
	# Budget check. A budget_remaining of -1 means unlimited (skip the check).
	if budget_remaining >= 0 and data.cost > budget_remaining:
		return false

	# Domain check: the part must list the current build domain in its domains.
	if not data.is_valid_for_domain(build_domain):
		return false

	# Bounds and overlap check for every cell the part would occupy.
	for x: int in range(data.size.x):
		for y: int in range(data.size.y):
			for z: int in range(data.size.z):
				var cell: Vector3i = pos + Vector3i(x, y, z)

				# Grid bounds: each axis goes from 0 to grid_size - 1.
				if cell.x < 0 or cell.x >= grid_size.x:
					return false
				if cell.y < 0 or cell.y >= grid_size.y:
					return false
				if cell.z < 0 or cell.z >= grid_size.z:
					return false

				# Overlap: cell already occupied.
				if placed_parts.has(cell):
					return false

	return true


# ---------------------------------------------------------------------------
# Connectivity (BFS flood-fill)
# ---------------------------------------------------------------------------

## BFS from an arbitrary part. If the flood-fill visits every unique part,
## the build is fully connected via orthogonal adjacency.
func _check_connectivity() -> bool:
	var unique: Array[PartNode] = _get_unique_parts()
	if unique.size() <= 1:
		return true

	# Build a set of all occupied cells for quick lookup.
	var occupied: Dictionary = {}
	for cell: Vector3i in placed_parts:
		occupied[cell] = true

	# Start BFS from the first occupied cell.
	var start_cell: Vector3i = placed_parts.keys()[0]
	var visited: Dictionary = {}
	var queue: Array[Vector3i] = [start_cell]
	visited[start_cell] = true

	# Six orthogonal directions.
	var directions: Array[Vector3i] = [
		Vector3i(1, 0, 0), Vector3i(-1, 0, 0),
		Vector3i(0, 1, 0), Vector3i(0, -1, 0),
		Vector3i(0, 0, 1), Vector3i(0, 0, -1),
	]

	while queue.size() > 0:
		var current: Vector3i = queue.pop_front()
		for dir: Vector3i in directions:
			var neighbor: Vector3i = current + dir
			if occupied.has(neighbor) and not visited.has(neighbor):
				visited[neighbor] = true
				queue.append(neighbor)

	# Every occupied cell should have been visited.
	return visited.size() == occupied.size()


# ---------------------------------------------------------------------------
# Ghost preview
# ---------------------------------------------------------------------------

## Create the ghost mesh node (a translucent box) used as a placement preview.
func _setup_ghost_mesh() -> void:
	ghost_mesh = MeshInstance3D.new()
	ghost_mesh.mesh = BoxMesh.new()

	_ghost_material = StandardMaterial3D.new()
	_ghost_material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	_ghost_material.albedo_color = GHOST_VALID_COLOR
	# Disable shadow casting so the ghost doesn't look weird.
	ghost_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	ghost_mesh.material_override = _ghost_material

	ghost_mesh.visible = false
	add_child(ghost_mesh)


## Move and recolor the ghost mesh to match the hovered cell and the current
## placement validity.
func _update_ghost() -> void:
	if selected_part_data == null or is_delete_mode:
		ghost_mesh.visible = false
		return

	ghost_mesh.visible = true

	# Resize the ghost to match the selected part's cell dimensions.
	var box: BoxMesh = ghost_mesh.mesh as BoxMesh
	box.size = Vector3(selected_part_data.size) * cell_size

	# Position the ghost so its origin aligns with the hovered cell corner.
	# Offset by half the part size so the mesh visually covers all cells.
	var offset: Vector3 = Vector3(selected_part_data.size) * cell_size * 0.5
	ghost_mesh.position = Vector3(_hovered_cell) * cell_size + offset - Vector3(0.5, 0.5, 0.5) * cell_size

	# Tint green or red based on placement validity.
	if _can_place_part(selected_part_data, _hovered_cell):
		_ghost_material.albedo_color = GHOST_VALID_COLOR
	else:
		_ghost_material.albedo_color = GHOST_INVALID_COLOR


# ---------------------------------------------------------------------------
# Grid visual
# ---------------------------------------------------------------------------

## Draw a wireframe grid using an ImmediateMesh so the player can see the
## build volume.
func _create_grid_visual() -> void:
	if grid_visual != null:
		grid_visual.queue_free()

	grid_visual = Node3D.new()
	grid_visual.name = "GridVisual"
	add_child(grid_visual)

	var im := ImmediateMesh.new()
	var mesh_instance := MeshInstance3D.new()
	mesh_instance.mesh = im
	mesh_instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF

	# Unshaded, semi-transparent line material.
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_color = GRID_LINE_COLOR
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mesh_instance.material_override = mat

	grid_visual.add_child(mesh_instance)

	# Draw lines along each axis.
	im.surface_begin(Mesh.PRIMITIVE_LINES)

	var sx: float = grid_size.x * cell_size
	var sy: float = grid_size.y * cell_size
	var sz: float = grid_size.z * cell_size

	# Lines parallel to X (along the Z-Y plane grid intersections).
	for y: int in range(grid_size.y + 1):
		for z: int in range(grid_size.z + 1):
			im.surface_add_vertex(Vector3(0, y * cell_size, z * cell_size))
			im.surface_add_vertex(Vector3(sx, y * cell_size, z * cell_size))

	# Lines parallel to Y.
	for x: int in range(grid_size.x + 1):
		for z: int in range(grid_size.z + 1):
			im.surface_add_vertex(Vector3(x * cell_size, 0, z * cell_size))
			im.surface_add_vertex(Vector3(x * cell_size, sy, z * cell_size))

	# Lines parallel to Z.
	for x: int in range(grid_size.x + 1):
		for y: int in range(grid_size.y + 1):
			im.surface_add_vertex(Vector3(x * cell_size, y, 0))
			im.surface_add_vertex(Vector3(x * cell_size, y, sz))

	im.surface_end()


# ---------------------------------------------------------------------------
# Raycasting
# ---------------------------------------------------------------------------

## Create an invisible horizontal plane that the mouse raycast can hit.
## The plane sits at y = 0 and covers the full grid footprint.
func _create_raycast_plane() -> void:
	raycast_plane = StaticBody3D.new()
	raycast_plane.name = "RaycastPlane"

	var shape := WorldBoundaryShape3D.new()
	# Default plane is horizontal (Y-up normal), which is what we want.
	var col := CollisionShape3D.new()
	col.shape = shape
	raycast_plane.add_child(col)

	# Put the plane on a dedicated layer so it doesn't interfere with gameplay.
	raycast_plane.collision_layer = 0
	raycast_plane.collision_mask = 0
	add_child(raycast_plane)


## Cast a ray from the camera through the current mouse position and determine
## which grid cell it hits. Returns Vector3i.ZERO if nothing is hit.
func _get_grid_position_from_mouse() -> Vector3i:
	if camera == null:
		return Vector3i.ZERO

	var viewport: Viewport = get_viewport()
	if viewport == null:
		return Vector3i.ZERO

	var mouse_pos: Vector2 = viewport.get_mouse_position()

	# Build a ray from the camera.
	var ray_origin: Vector3 = camera.project_ray_origin(mouse_pos)
	var ray_dir: Vector3 = camera.project_ray_normal(mouse_pos)

	# Intersect with the horizontal plane at the currently viewed Y layer.
	# For simplicity, we always use y = 0.
	# Plane: y = 0 -> t = -origin.y / dir.y (avoid division by zero).
	if absf(ray_dir.y) < 0.0001:
		return Vector3i.ZERO

	var t: float = -ray_origin.y / ray_dir.y
	if t < 0.0:
		return Vector3i.ZERO

	var hit_point: Vector3 = ray_origin + ray_dir * t

	# Snap the hit point to the nearest grid cell.
	var gx: int = int(floorf(hit_point.x / cell_size))
	var gy: int = 0  # ground layer
	var gz: int = int(floorf(hit_point.z / cell_size))

	# Clamp to grid bounds.
	gx = clampi(gx, 0, grid_size.x - 1)
	gz = clampi(gz, 0, grid_size.z - 1)

	return Vector3i(gx, gy, gz)


# ---------------------------------------------------------------------------
# Camera
# ---------------------------------------------------------------------------

## Create the orbit camera rig: a pivot at the grid center with the camera
## offset along negative-Z.
func _setup_orbit_camera() -> void:
	camera_pivot = Node3D.new()
	camera_pivot.name = "CameraPivot"
	# Center the pivot on the grid.
	camera_pivot.position = Vector3(grid_size) * cell_size * 0.5
	add_child(camera_pivot)

	camera = Camera3D.new()
	camera.name = "BuildCamera"
	# Offset the camera so it looks at the grid center from a comfortable distance.
	camera.position = Vector3(0, 5, 12)
	camera_pivot.add_child(camera)
	# look_at must be called after adding to the tree.
	camera.look_at(camera_pivot.global_position)


## Rotate the orbit pivot based on mouse drag delta.
func _orbit_camera(event: InputEventMouseMotion) -> void:
	if camera_pivot == null:
		return
	# Horizontal mouse movement rotates around Y (yaw).
	camera_pivot.rotate_y(deg_to_rad(-event.relative.x * ORBIT_SENSITIVITY))
	# Vertical mouse movement rotates around local X (pitch), clamped to avoid flipping.
	var pitch_change: float = deg_to_rad(-event.relative.y * ORBIT_SENSITIVITY)
	var current_pitch: float = camera_pivot.rotation.x
	camera_pivot.rotation.x = clampf(current_pitch + pitch_change, deg_to_rad(-80), deg_to_rad(80))


## Move the camera closer or further from the pivot.
## [param direction] is positive to zoom out, negative to zoom in.
func _zoom_camera(direction: float) -> void:
	if camera == null:
		return
	var new_z: float = camera.position.z + direction * ZOOM_STEP
	camera.position.z = clampf(new_z, ZOOM_MIN, ZOOM_MAX)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

## Return an array of unique PartNode references from placed_parts.
func _get_unique_parts() -> Array[PartNode]:
	var unique: Array[PartNode] = []
	var seen: Dictionary = {}
	for cell: Vector3i in placed_parts:
		var node: PartNode = placed_parts[cell]
		var nid: int = node.get_instance_id()
		if not seen.has(nid):
			seen[nid] = true
			unique.append(node)
	return unique
