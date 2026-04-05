## PhysicsBase (RefCounted)
##
## Abstract base class for domain-specific vehicle physics. Each combat domain
## (ground, air, water, submarine, space) has a subclass that overrides
## apply_forces() to implement its own movement model.
##
## Why RefCounted instead of Node?
##   The physics controller holds no children or scene-tree state. It's a
##   lightweight strategy object owned by the Vehicle. RefCounted lets the
##   engine garbage-collect it automatically when the Vehicle drops its
##   reference.
##
## Subclass contract:
##   - Override apply_forces()  to push the vehicle around each physics tick.
##   - Override handle_input()  to translate player actions into forces.
##   - Override get_domain()    to return the domain string.
##   - Override get_max_speed() to return the theoretical top speed.
##   - Override get_hud_data()  to return a dictionary of HUD-relevant info.
class_name PhysicsBase
extends RefCounted


# ---------------------------------------------------------------------------
# Virtual methods -- override in subclasses
# ---------------------------------------------------------------------------

## Apply domain-specific forces (thrust, drag, gravity, lift, buoyancy, etc.)
## to [param vehicle] during this physics tick.
##
## This is the heart of the physics controller. Called every _physics_process
## frame by the Vehicle. [param delta] is the frame timestep in seconds.
##
## The base implementation does nothing. Every subclass MUST override this.
func apply_forces(_vehicle: RigidBody3D, _delta: float) -> void:
	pass


## Return the theoretical maximum speed (m/s) this vehicle can reach under
## full power with its current stats. Used for HUD gauges and AI planning.
func get_max_speed(_vehicle: RigidBody3D) -> float:
	return 0.0


## Return the vehicle's current scalar speed (m/s).
## The default implementation uses the RigidBody3D's linear_velocity magnitude.
func get_current_speed(vehicle: RigidBody3D) -> float:
	return vehicle.linear_velocity.length()


## Return the domain string this controller handles.
## One of: "ground", "air", "water", "submarine", "space".
func get_domain() -> String:
	return ""


## Read player input and translate it into forces / torques on the vehicle.
## Called every physics frame when the vehicle is player-controlled.
##
## The base implementation does nothing. Override per domain.
func handle_input(_vehicle: RigidBody3D, _delta: float) -> void:
	pass


## Return a Dictionary of domain-specific data for the HUD to display.
## At minimum, every domain should include "speed". Subclasses add things
## like altitude, fuel, depth, throttle, etc.
func get_hud_data(vehicle: RigidBody3D) -> Dictionary:
	return {
		"speed": get_current_speed(vehicle),
	}
