using UnityEngine;
using CloseEncounters.AI;

namespace CloseEncounters.VehiclePhysics
{
    // =========================================================================
    //  GroundPhysics — FixedUpdate physics for ground-domain vehicles.
    //
    //  Handles yaw steering, forward/reverse thrust, terrain-following via
    //  raycast + surface normal alignment, heavy gravity, angular damping,
    //  and boost.
    //
    //  Reads input from player axes or an AIController on the same GameObject.
    // =========================================================================
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class GroundPhysics : MonoBehaviour
    {
        // ----- thrust -----
        [Header("Thrust")]
        [Tooltip("Forward thrust force at full throttle.")]
        public float thrustForce = 1200f;
        [Tooltip("Reverse thrust is this fraction of forward thrust.")]
        public float reverseFraction = 0.5f;

        // ----- steering -----
        [Header("Steering")]
        [Tooltip("Yaw torque applied by A/D or AI yaw input.")]
        public float yawTorque = 600f;
        [Tooltip("Speed at which yaw torque reaches full authority (units/s).")]
        public float yawFullSpeedRef = 15f;
        [Tooltip("Minimum yaw authority fraction even when stationary.")]
        public float yawMinAuthority = 0.35f;

        // ----- terrain following -----
        [Header("Terrain Following")]
        [Tooltip("How far below the vehicle to raycast for the ground surface.")]
        public float groundRayLength = 5f;
        [Tooltip("Desired hover height above the ground surface.")]
        public float hoverHeight = 0.5f;
        [Tooltip("Spring force per unit of height error.")]
        public float hoverSpring = 80f;
        [Tooltip("Damping on the hover spring.")]
        public float hoverDamping = 12f;
        [Tooltip("How quickly the vehicle aligns to the ground normal.")]
        public float alignSpeed = 8f;
        public LayerMask groundMask = ~0;

        // ----- gravity -----
        [Header("Gravity")]
        [Tooltip("Extra downward force multiplier (on top of default gravity).")]
        public float gravityMultiplier = 4f;

        // ----- angular damping -----
        [Header("Angular Damping")]
        [Tooltip("Additional angular drag applied each FixedUpdate to prevent spinning.")]
        public float angularDampingTorque = 20f;

        // ----- boost -----
        [Header("Boost")]
        public float boostMultiplier = 2.0f;
        public float boostDuration   = 2.5f;
        public float boostCooldown   = 6f;

        // ----- drag -----
        [Header("Linear Drag")]
        [Tooltip("Simple linear drag coefficient applied to lateral velocity.")]
        public float lateralDrag = 3f;

        // ----- internal -----
        private Rigidbody _rb;
        private AIController _ai;
        private bool _isAI;

        // boost state
        private bool  _boosting;
        private float _boostTimer;
        private float _boostCooldownTimer;

        // ----- propulsion degradation -----
        private int _initialPropulsionCount;
        private int _currentPropulsionCount;
        private float _baseThrustForce;

        // grounded state
        private bool    _grounded;
        private Vector3 _groundNormal = Vector3.up;
        private float   _groundDistance;

        // input cache
        private float _inputForward;
        private float _inputYaw;
        private bool  _inputBoost;

        // anti-tunnel sweep
        [Header("Anti-Tunnel")]
        [Tooltip("Chassis sweep radius used to stop the vehicle short of walls/rocks.")]
        public float sweepRadius = 1.25f;
        [Tooltip("Multiplier on velocity*dt for the sweep distance.")]
        public float sweepSafetyFactor = 1.5f;

        // non-alloc probe buffer (ground raycast, self-filter)
        private static readonly RaycastHit[] _probeHits = new RaycastHit[8];

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = true;
            // why: discrete CD tunnels through cliffs at boost speed
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private void Start()
        {
            _ai = GetComponent<AIController>();
            _isAI = _ai != null;
            Debug.Log($"[GroundPhysics] Started on '{gameObject.name}', isAI={_isAI}, rb.mass={_rb.mass}");
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            ReadInput();
            ProbeGround();
            ApplyHeavyGravity();
            ApplyTerrainFollow(dt);
            ApplyThrust(dt);
            ApplyYawSteering(dt);
            ApplyAngularDamping();
            ApplyLateralDrag();
            UpdateBoost(dt);

            // why: clamp velocity if a wall is within reach this step
            AntiPhaseCapsule.GateVelocity(_rb, transform, sweepRadius, groundMask, sweepSafetyFactor);
        }

        // =====================================================================
        //  Input
        // =====================================================================

        private void ReadInput()
        {
            if (_isAI && _ai != null)
            {
                AIInput ai = _ai.CurrentInput;
                _inputForward = ai.forward;
                _inputYaw     = ai.yaw;
                _inputBoost   = ai.boost;
            }
            else
            {
                _inputForward = Input.GetAxis("Vertical");
                _inputYaw     = Input.GetAxis("Horizontal");
                _inputBoost   = Input.GetKey(KeyCode.LeftShift);
            }
        }

        // =====================================================================
        //  Ground probe — raycast downward, get normal + distance
        // =====================================================================

        private void ProbeGround()
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;

            int count = UnityEngine.Physics.RaycastNonAlloc(origin, Vector3.down, _probeHits,
                groundRayLength + 0.5f, groundMask, QueryTriggerInteraction.Ignore);

            float bestDist = float.PositiveInfinity;
            int bestIdx = -1;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _probeHits[i];
                if (h.collider == null) continue;
                // why: mask may be ~0 so chassis colliders must be filtered out
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
            {
                _grounded = true;
                _groundNormal = _probeHits[bestIdx].normal;
                _groundDistance = bestDist - 0.5f; // account for origin offset
            }
            else
            {
                _grounded = false;
                _groundNormal = Vector3.up;
                _groundDistance = groundRayLength;
            }
        }

        // =====================================================================
        //  Heavy gravity — extra downward force
        // =====================================================================

        private void ApplyHeavyGravity()
        {
            // Additional gravity beyond what Rigidbody.useGravity provides
            float extra = (gravityMultiplier - 1f) * _rb.mass * UnityEngine.Physics.gravity.magnitude;
            if (extra > 0f)
                _rb.AddForce(Vector3.down * extra, ForceMode.Force);
        }

        // =====================================================================
        //  Terrain following — hover spring + normal alignment
        // =====================================================================

        private void ApplyTerrainFollow(float dt)
        {
            if (!_grounded) return;

            // Hover spring: push up/down to maintain hoverHeight above ground
            float heightError = hoverHeight - _groundDistance;
            float springForce = hoverSpring * _rb.mass * heightError;
            float dampForce   = hoverDamping * _rb.mass * _rb.linearVelocity.y;

            _rb.AddForce(Vector3.up * (springForce - dampForce), ForceMode.Force);

            // Align to ground normal via cross-product torque
            Vector3 currentUp = transform.up;
            Vector3 alignTorque = Vector3.Cross(currentUp, _groundNormal);
            _rb.AddTorque(alignTorque * alignSpeed * _rb.mass, ForceMode.Force);
        }

        // =====================================================================
        //  Thrust — forward/reverse along transform.forward
        // =====================================================================

        private void ApplyThrust(float dt)
        {
            float boost = GetBoostMultiplier();
            float input = _inputForward;
            float force;

            // Scale thrust with mass so heavier vehicles still move well
            // Reduced thrust when airborne
            float groundMult = _grounded ? 1f : 0.3f;
            float massScaledThrust = thrustForce * Mathf.Max(1f, _rb.mass * 0.1f) * groundMult;

            if (input >= 0f)
                force = input * massScaledThrust * boost;
            else
                force = input * massScaledThrust * reverseFraction;

            // Project thrust along the ground plane
            Vector3 thrustDir = Vector3.ProjectOnPlane(transform.forward, _groundNormal).normalized;
            _rb.AddForce(thrustDir * force, ForceMode.Force);
        }

        // =====================================================================
        //  Yaw steering — torque around up axis
        // =====================================================================

        private void ApplyYawSteering(float dt)
        {
            float speed = _rb.linearVelocity.magnitude;
            float authority = Mathf.Lerp(yawMinAuthority, 1f,
                Mathf.Clamp01(speed / yawFullSpeedRef));

            // Scale torque with mass so heavy vehicles can still turn
            float massScaledTorque = yawTorque * Mathf.Max(1f, _rb.mass * 0.05f);
            float torque = _inputYaw * massScaledTorque * authority;

            // Apply around world up (keeps vehicle from flipping)
            _rb.AddTorque(Vector3.up * torque, ForceMode.Force);
        }

        // =====================================================================
        //  Angular damping — prevent continuous spinning
        // =====================================================================

        private void ApplyAngularDamping()
        {
            Vector3 angVel = _rb.angularVelocity;
            if (angVel.sqrMagnitude < 0.001f) return;

            _rb.AddTorque(-angVel * angularDampingTorque * _rb.mass, ForceMode.Force);
        }

        // =====================================================================
        //  Lateral drag — resist sideways sliding
        // =====================================================================

        private void ApplyLateralDrag()
        {
            Vector3 vel = _rb.linearVelocity;
            Vector3 lateralVel = Vector3.Project(vel, transform.right);
            _rb.AddForce(-lateralVel * lateralDrag * _rb.mass, ForceMode.Force);
        }

        // =====================================================================
        //  Boost
        // =====================================================================

        private void UpdateBoost(float dt)
        {
            _boostCooldownTimer -= dt;

            if (_boosting)
            {
                _boostTimer -= dt;
                if (_boostTimer <= 0f)
                {
                    _boosting = false;
                    _boostCooldownTimer = boostCooldown;
                }
            }
            else if (_inputBoost && _boostCooldownTimer <= 0f)
            {
                _boosting = true;
                _boostTimer = boostDuration;
            }
        }

        private float GetBoostMultiplier()
        {
            return _boosting ? boostMultiplier : 1f;
        }

        // =====================================================================
        //  Public API
        // =====================================================================

        /// <summary>
        /// Initialize propulsion degradation tracking.
        /// Call during vehicle setup with the number of propulsion parts.
        /// </summary>
        public void InitPropulsionTracking(int propulsionPartCount)
        {
            _initialPropulsionCount = propulsionPartCount;
            _currentPropulsionCount = propulsionPartCount;
            _baseThrustForce = thrustForce;
        }

        /// <summary>
        /// Called when a propulsion part is destroyed.
        /// Proportionally reduces thrust force (min 20% of base).
        /// </summary>
        public void OnPropulsionPartDestroyed()
        {
            _currentPropulsionCount = Mathf.Max(_currentPropulsionCount - 1, 0);
            float ratio = _initialPropulsionCount > 0
                ? (float)_currentPropulsionCount / _initialPropulsionCount
                : 0f;
            float thrustFraction = Mathf.Lerp(0.2f, 1.0f, ratio);
            thrustForce = _baseThrustForce * thrustFraction;
        }

        /// <summary>True when the vehicle is touching the ground.</summary>
        public bool IsGrounded => _grounded;

        /// <summary>True when the vehicle is currently boosting.</summary>
        public bool IsBoosting => _boosting;

        /// <summary>Remaining boost time (0 if not boosting).</summary>
        public float BoostTimeRemaining => _boosting ? _boostTimer : 0f;

        /// <summary>Remaining cooldown (0 if ready).</summary>
        public float BoostCooldownRemaining => Mathf.Max(_boostCooldownTimer, 0f);

        /// <summary>Current speed in units/s.</summary>
        public float Speed => _rb != null ? _rb.linearVelocity.magnitude : 0f;

        /// <summary>Ground surface normal under the vehicle (Vector3.up if airborne).</summary>
        public Vector3 GroundNormal => _groundNormal;

        // =====================================================================
        //  Debug gizmos
        // =====================================================================

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Gizmos.color = _grounded ? Color.green : Color.red;
            Gizmos.DrawRay(origin, Vector3.down * (groundRayLength + 0.5f));

            if (_grounded)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(transform.position, _groundNormal * 2f);
            }
        }
#endif
    }
}
