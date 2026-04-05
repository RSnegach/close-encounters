## Explosion -- Visual and gameplay explosion effect.
##
## When spawned, an Explosion immediately applies area damage (if configured),
## then plays a visual effect: an expanding, fading sphere that looks like a
## fireball. After the animation completes, it removes itself from the scene.
##
## Usage:
##   1. Create an Explosion instance: var boom = Explosion.new()
##   2. Add it to the scene tree and set its position.
##   3. Call setup() with the desired radius, damage, and source vehicle.
##   4. The explosion handles everything else automatically.
##
## The visual is a simple SphereMesh with an emissive orange material. It
## starts small and scales up to the full radius over the duration, while
## the material fades from bright to transparent.
class_name Explosion
extends Node3D


# ---------------------------------------------------------------------------
# Public variables
# ---------------------------------------------------------------------------

## Blast radius in meters. Determines both the visual size and the gameplay
## area-of-effect sphere.
var radius: float = 5.0

## Damage dealt at the epicenter. Damage falls off linearly to zero at the
## edge of the radius. Set to 0 for visual-only explosions (hit effects).
var damage: int = 50

## How long the visual effect lasts in seconds. After this time, the node
## is removed from the scene.
var duration: float = 0.5

## Elapsed time since the explosion was spawned. Drives the expand/fade
## animation.
var timer: float = 0.0

## Whether area damage has already been applied. Prevents double-damage if
## _process runs before setup() finishes or if the node is re-entered.
var has_applied_damage: bool = false


# ---------------------------------------------------------------------------
# Private variables
# ---------------------------------------------------------------------------

## The MeshInstance3D that displays the expanding fireball sphere.
var _flash_mesh: MeshInstance3D = null

## The material applied to the sphere mesh. We tween its albedo alpha and
## emission energy to create the fade-out effect.
var _material: StandardMaterial3D = null

## The initial scale factor. The sphere starts at this fraction of the full
## size and expands to 1.0 over the duration.
var _start_scale: float = 0.1


# ---------------------------------------------------------------------------
# Engine callbacks
# ---------------------------------------------------------------------------

## Create the visual sphere when the explosion enters the scene.
func _ready() -> void:
	_create_visual()


## Every frame, expand the sphere and fade the material. Remove the node
## once the duration has elapsed.
func _process(delta: float) -> void:
	timer += delta

	# --- Expand the sphere ---
	# Linear interpolation from the start scale to full radius.
	var progress: float = clampf(timer / duration, 0.0, 1.0)
	var current_scale: float = lerpf(_start_scale, radius, progress)
	_flash_mesh.scale = Vector3.ONE * current_scale

	# --- Fade out ---
	# Start fading after 30% of the duration has passed, reaching full
	# transparency by the end.
	if _material != null:
		var fade_start: float = 0.3
		if progress > fade_start:
			var fade_progress: float = (progress - fade_start) / (1.0 - fade_start)
			_material.albedo_color.a = 1.0 - fade_progress
			# Also reduce emission energy so the glow dims.
			_material.emission_energy_multiplier = lerpf(5.0, 0.0, fade_progress)

	# --- Clean up when done ---
	if timer >= duration:
		queue_free()


# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

## Configure and activate the explosion.
##
## [param explosion_radius] - The blast radius in meters.
## [param explosion_damage] - Damage at the epicenter (0 = visual only).
## [param source]           - The vehicle that caused this explosion. It
##                            will be excluded from its own area damage.
func setup(explosion_radius: float, explosion_damage: int, source: Vehicle = null) -> void:
	radius = explosion_radius
	damage = explosion_damage

	# Scale the visual duration with the radius so bigger explosions last
	# longer and feel more impactful.
	duration = clampf(radius * 0.1, 0.3, 2.0)

	# --- Apply area damage (once only) ---
	if not has_applied_damage and damage > 0:
		has_applied_damage = true
		_apply_blast_damage(source)


# ---------------------------------------------------------------------------
# Visual creation
# ---------------------------------------------------------------------------

## Build the expanding fireball mesh. Uses a SphereMesh with an emissive
## orange/yellow material that supports transparency for the fade effect.
func _create_visual() -> void:
	_flash_mesh = MeshInstance3D.new()
	_flash_mesh.name = "FlashSphere"

	# Create a unit sphere (radius 0.5). We scale the MeshInstance3D to
	# achieve the desired explosion radius.
	var sphere: SphereMesh = SphereMesh.new()
	sphere.radius = 0.5
	sphere.height = 1.0
	# Reduce polygon count -- explosions are fast and don't need to be smooth.
	sphere.radial_segments = 16
	sphere.rings = 8

	# --- Emissive material ---
	_material = StandardMaterial3D.new()
	# Bright orange/yellow fireball color.
	_material.albedo_color = Color(1.0, 0.6, 0.1, 1.0)
	# Enable transparency for the fade-out effect.
	_material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	# Emissive glow so the explosion lights up the scene.
	_material.emission_enabled = true
	_material.emission = Color(1.0, 0.4, 0.05)
	_material.emission_energy_multiplier = 5.0
	# Disable backface culling so the sphere looks solid from inside.
	_material.cull_mode = BaseMaterial3D.CULL_DISABLED

	sphere.material = _material

	_flash_mesh.mesh = sphere
	# Start at a small scale; _process will expand it.
	_flash_mesh.scale = Vector3.ONE * _start_scale

	add_child(_flash_mesh)


# ---------------------------------------------------------------------------
# Damage application
# ---------------------------------------------------------------------------

## Apply the explosion's area damage through the DamageSystem. Called once
## during setup().
##
## [param source] - The vehicle that caused the explosion (excluded from
##                  self-damage).
func _apply_blast_damage(source: Vehicle) -> void:
	# Look up the DamageSystem in the scene tree.
	var damage_system: DamageSystem = _find_damage_system()
	if damage_system != null:
		damage_system.apply_area_damage(global_position, radius, damage, source)
	else:
		# DamageSystem not found -- this can happen in test scenes.
		push_warning("[Explosion] DamageSystem not found. Blast damage skipped.")


## Search the scene tree for a DamageSystem node.
func _find_damage_system() -> DamageSystem:
	# Try the group first.
	var systems: Array[Node] = get_tree().get_nodes_in_group("damage_system")
	if not systems.is_empty():
		return systems[0] as DamageSystem

	# Fallback: check children of the scene root.
	var root: Node = get_tree().current_scene
	if root == null:
		return null
	for child: Node in root.get_children():
		if child is DamageSystem:
			return child as DamageSystem

	return null
