using UnityEngine;
using CloseEncounters.AI;

namespace CloseEncounters.VehiclePhysics
{
    // =========================================================================
    //  WaterPhysics — FixedUpdate physics for water-domain vehicles.
    //
    //  Handles buoyancy, wave motion, thrust, hull drag, rudder steering,
    //  anti-capsize torque, sail passive thrust, and boost.
    //
    //  Reads input from either player Input axes or an AIController on the
    //  same GameObject.
    // =========================================================================
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class WaterPhysics : MonoBehaviour
    {
        private static readonly Vector3[] _fallbackCenterProbe = { Vector3.zero };

        // ----- buoyancy -----
        [Header("Buoyancy")]
        [Tooltip("Resting water surface Y position (used when WaveManager is absent).")]
        public float waterSurfaceY = 6f;
        [Tooltip("Spring constant per kg of mass (force = springPerKg * mass * displacement).")]
        public float springPerKg = 45.0f;
        [Tooltip("Damping constant per kg of mass.")]
        public float dampingPerKg = 4.0f;
        [Tooltip("Depth below surface at which buoyancy is fully applied (smooth ramp).")]
        public float depthBeforeSubmerged = 1.0f;

        // ----- multi-point buoyancy (adapted from FloatCube concept) -----
        [Header("Multi-Point Buoyancy")]
        [Tooltip("Local-space offsets for buoyancy probe points. If empty, uses single center probe.")]
        public Vector3[] buoyancyProbes = new Vector3[]
        {
            new Vector3( 0f,   -0.8f,  3.5f),  // bow center
            new Vector3( 0f,   -0.8f, -3.5f),  // stern center
            new Vector3( 2.5f, -0.8f,  0f),    // port amidships
            new Vector3(-2.5f, -0.8f,  0f),    // starboard amidships
            new Vector3( 2.0f, -0.8f,  3.0f),  // bow-port corner
            new Vector3(-2.0f, -0.8f,  3.0f),  // bow-starboard corner
        };

        // ----- waves (fallback when WaveManager is absent) -----
        [Header("Waves (Fallback)")]
        public float waveAmplitude = 0.8f;
        public float waveFrequency = 0.6f;
        public float waveSpeed     = 2.0f;

        // ----- thrust -----
        [Header("Thrust")]
        [Tooltip("Forward thrust force applied per unit of throttle input.")]
        public float thrustForce = 3200f;
        [Tooltip("Lateral strafe thrust (fraction of main thrust).")]
        public float strafeFraction = 0.3f;

        // ----- hull drag (3-component model from BoatForces) -----
        [Header("Hull Drag")]
        [Tooltip("Wetted surface area in m² for frictional resistance.")]
        public float wettedSurface = 6f;
        [Tooltip("Friction coefficient Cf (typically 0.003-0.005 for hulls).")]
        public float frictionCoeff = 0.004f;
        [Tooltip("Residual resistance coefficient Cr (wave-making drag).")]
        public float residualCoeff = 0.01f;
        [Tooltip("Lateral (sideways) drag coefficient — prevents sliding broadside.")]
        public float lateralDragCoeff = 0.25f;
        [Tooltip("Water density in kg/m³ (saltwater ~1030, fresh ~1000).")]
        public float waterDensity = 1030f;

        // ----- rudder / steering -----
        [Header("Rudder")]
        [Tooltip("Maximum yaw torque at full speed.")]
        public float rudderTorque = 800f;
        [Tooltip("How much speed amplifies rudder authority (0 = none, 1 = linear).")]
        [Range(0f, 1f)]
        public float rudderSpeedScale = 0.7f;
        [Tooltip("Minimum rudder authority even when stationary.")]
        public float rudderMinAuthority = 0.2f;

        // ----- anti-capsize -----
        [Header("Anti-Capsize")]
        [Tooltip("Multiplier on mass for the uprighting torque.")]
        public float anticapsizeMultiplier = 3f;

        // ----- sail -----
        [Header("Sail (Passive Thrust)")]
        [Tooltip("Passive forward force applied regardless of throttle (simulates sails).")]
        public float sailThrust = 40f;
        [Tooltip("If false, sail thrust is not applied.")]
        public bool hasSail = false;

        // ----- boost -----
        [Header("Boost")]
        public float boostMultiplier = 3f;
        public float boostDuration   = 3f;
        public float boostCooldown   = 8f;

        // ----- anti-tunnel sweep -----
        [Header("Anti-Tunnel")]
        [Tooltip("Hull sweep radius used to stop short of island shorelines.")]
        public float sweepRadius = 2.5f;
        [Tooltip("Layers treated as solid shoreline/obstacles. Exclude water volumes & vehicle self.")]
        public LayerMask shoreMask = ~0;
        [Tooltip("Multiplier on velocity*dt for the sweep distance.")]
        public float sweepSafetyFactor = 1.75f;

        // ----- input source -----
        private Rigidbody _rb;
        private AIController _ai;
        private CloseEncounters.Combat.PlayerVehicleController _playerCtrl;
        private bool _isAI;

        // ----- boost state -----
        private bool  _boosting;
        private float _boostTimer;
        private float _boostCooldownTimer;

        // ----- propulsion degradation -----
        private int _initialPropulsionCount;
        private int _currentPropulsionCount;
        private float _baseThrustForce;

        // ----- input cache -----
        private float _inputForward;
        private float _inputStrafe;
        private float _inputYaw;
        private bool  _inputBoost;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false; // buoyancy replaces gravity
            // Heavier boats resist rocking more — scale angular damping with mass
            _rb.angularDamping = Mathf.Lerp(2f, 8f, Mathf.Clamp01(_rb.mass / 500f));
            // why: boats at full thrust tunnel into island shorelines
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // why: lateral drift-clip + depenetration runs in its own FixedUpdate
            if (GetComponent<HullContactGuard>() == null)
                gameObject.AddComponent<HullContactGuard>();
        }

        private void Start()
        {
            _ai = GetComponent<AIController>();
            _isAI = _ai != null;
            _playerCtrl = GetComponent<CloseEncounters.Combat.PlayerVehicleController>();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            ReadInput();
            ApplyBuoyancy(dt);
            ApplyThrust(dt);
            ApplyHullDrag();
            ApplyRudder(dt);
            ApplyAnticapsize();
            ApplySailThrust();
            UpdateBoost(dt);

            // why: only gate horizontal thrust into shorelines; buoyancy loop above is untouched
            AntiPhaseCapsule.GateVelocity(_rb, transform, sweepRadius, shoreMask, sweepSafetyFactor);
        }

        // =====================================================================
        //  Input reading
        // =====================================================================

        private void ReadInput()
        {
            if (_isAI && _ai != null)
            {
                AIInput ai = _ai.CurrentInput;
                _inputForward = ai.forward;
                _inputStrafe  = ai.strafe;
                _inputYaw     = ai.yaw;
                _inputBoost   = ai.boost;
            }
            else
            {
                // Direct key polling — W/S for forward/back, A/D for yaw
                _inputForward = 0f;
                if (Input.GetKey(KeyCode.W)) _inputForward += 1f;
                if (Input.GetKey(KeyCode.S)) _inputForward -= 1f;

                _inputStrafe = 0f; // strafe removed

                _inputYaw = 0f;
                if (Input.GetKey(KeyCode.D)) _inputYaw += 1f;
                if (Input.GetKey(KeyCode.A)) _inputYaw -= 1f;

                _inputBoost = Input.GetKey(KeyCode.LeftShift);
            }
        }

        // =====================================================================
        //  Buoyancy — multi-point probes with WaveManager support
        //  Each probe independently pushes up based on its submersion depth,
        //  which naturally produces pitch and roll from waves.
        //  Adapted from FloatCube buoyancy concept + WaweManager integration.
        // =====================================================================

        private float GetSurfaceY(Vector3 worldPos)
        {
            if (WaveManager.Instance != null)
                return WaveManager.Instance.GetWaterHeight(worldPos);

            // Fallback: inline sine wave (use fixedTime — called from FixedUpdate path)
            float waveOffset = waveAmplitude * Mathf.Sin(
                waveFrequency * Time.fixedTime + waveSpeed * worldPos.x * 0.1f
                + worldPos.z * 0.07f);
            return waterSurfaceY + waveOffset;
        }

        private void ApplyBuoyancy(float dt)
        {
            float mass = _rb.mass;

            // Gravity (applied once, buoyancy probes counteract it)
            _rb.AddForce(Vector3.down * mass * UnityEngine.Physics.gravity.magnitude, ForceMode.Force);

            // Use probes if defined, otherwise single center probe
            Vector3[] probes = (buoyancyProbes != null && buoyancyProbes.Length > 0)
                ? buoyancyProbes
                : _fallbackCenterProbe;

            float forcePerProbe = mass / probes.Length;

            for (int i = 0; i < probes.Length; i++)
            {
                Vector3 worldProbe = transform.TransformPoint(probes[i]);
                float surfaceY = GetSurfaceY(worldProbe);

                // How far below the surface this probe is (positive = submerged)
                float displacement = surfaceY - worldProbe.y;

                if (displacement <= 0f)
                    continue; // Above water — no buoyancy

                // Smooth ramp: 0 at surface, 1 when depthBeforeSubmerged reached
                float submersionRatio = Mathf.Clamp01(displacement / depthBeforeSubmerged);

                // Spring + damping per probe
                float springForce  = springPerKg * forcePerProbe * submersionRatio * displacement;
                float dampingForce = dampingPerKg * forcePerProbe * _rb.GetPointVelocity(worldProbe).y;

                float netForce = springForce - dampingForce;

                // Apply at the probe's world position (creates torque for pitch/roll)
                _rb.AddForceAtPosition(Vector3.up * netForce, worldProbe, ForceMode.Force);
            }
        }

        // =====================================================================
        //  Thrust — along -transform.forward (Unity convention: forward = +Z)
        // =====================================================================

        private void ApplyThrust(float dt)
        {
            float boost = GetBoostMultiplier();

            // Flatten forward direction to horizontal plane so thrust
            // doesn't pitch the vehicle into the water
            Vector3 flatForward = transform.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.01f) flatForward = Vector3.forward;
            flatForward.Normalize();

            float fwd = _inputForward * thrustForce * boost;
            _rb.AddForce(flatForward * fwd, ForceMode.Force);
        }

        // =====================================================================
        //  Hull drag — 3-component model adapted from BoatForces.cs
        //    1. Frictional resistance (wetted surface)
        //    2. Residual / wave-making resistance
        //    3. Lateral (sideways) hull drag — prevents broadside sliding
        // =====================================================================

        private void ApplyHullDrag()
        {
            Vector3 vel = _rb.linearVelocity;
            float speed = vel.magnitude;
            if (speed < 0.01f) return;

            float halfRhoV2 = 0.5f * waterDensity * speed * speed;

            // 1. Frictional resistance: Ffr = 0.5 * rho * V² * S * Cf
            float frictionDrag = halfRhoV2 * wettedSurface * frictionCoeff;

            // 2. Residual (wave-making) resistance: Fr = 0.5 * rho * V² * Cr
            float residualDrag = halfRhoV2 * residualCoeff;

            // Total forward drag opposes velocity direction
            // Reduce drag when boosting so speed actually increases
            float totalDrag = frictionDrag + residualDrag;
            if (GetBoostMultiplier() > 1f)
                totalDrag *= 0.3f;

            // Cap to prevent instability at high dt
            float maxDrag = _rb.mass * speed / Time.fixedDeltaTime * 0.5f;
            totalDrag = Mathf.Min(totalDrag, maxDrag);

            _rb.AddForce(-vel.normalized * totalDrag, ForceMode.Force);

            // 3. Lateral drag — resist sideways motion (hull acts like a keel)
            Vector3 localVel = transform.InverseTransformDirection(vel);
            float lateralSpeed = localVel.x;
            if (Mathf.Abs(lateralSpeed) > 0.01f)
            {
                float lateralDrag = 0.5f * waterDensity * lateralSpeed * Mathf.Abs(lateralSpeed)
                                    * wettedSurface * lateralDragCoeff;
                lateralDrag = Mathf.Clamp(lateralDrag,
                    -_rb.mass * Mathf.Abs(lateralSpeed) / Time.fixedDeltaTime * 0.5f,
                     _rb.mass * Mathf.Abs(lateralSpeed) / Time.fixedDeltaTime * 0.5f);

                _rb.AddForce(-transform.right * lateralDrag, ForceMode.Force);
            }
        }

        // =====================================================================
        //  Rudder — yaw torque that scales with speed
        // =====================================================================

        private void ApplyRudder(float dt)
        {
            if (Mathf.Abs(_inputYaw) < 0.01f) return;

            // Consistent turning: blend between direct rotation and torque
            // based on speed, with no hard cutoff
            float speed = _rb.linearVelocity.magnitude;
            float targetYawRate = _inputYaw * 70f * Mathf.Deg2Rad; // 70 deg/s base

            // At higher speeds, rudder is more effective (more water flow over it)
            float speedBoost = 1f + Mathf.Clamp01(speed / 15f) * 0.5f;
            targetYawRate *= speedBoost;

            // Smoothly set angular velocity toward target (no torque fighting damping)
            Vector3 av = _rb.angularVelocity;
            av.y = Mathf.Lerp(av.y, targetYawRate, 8f * dt);
            _rb.angularVelocity = av;
        }

        // =====================================================================
        //  Anti-capsize — strong uprighting torque
        // =====================================================================

        private void ApplyAnticapsize()
        {
            Vector3 currentUp = transform.up;
            Vector3 correction = Vector3.Cross(currentUp, Vector3.up);
            float tiltAngle = Vector3.Angle(currentUp, Vector3.up);

            // Progressive: gentle for small tilts (allows wave bobbing),
            // very strong for severe tilts (prevents capsizing)
            float mult = anticapsizeMultiplier;
            if (tiltAngle > 30f)
                mult *= 5f; // emergency uprighting
            else if (tiltAngle > 15f)
                mult *= 2f;

            float strength = mult * _rb.mass;
            _rb.AddTorque(correction * strength, ForceMode.Force);
        }

        // =====================================================================
        //  Sail — passive forward thrust
        // =====================================================================

        private void ApplySailThrust()
        {
            if (!hasSail) return;

            // Passive forward push (always in the "ship forward" direction)
            _rb.AddForce(-transform.forward * sailThrust, ForceMode.Force);
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
            // Player: use fuel-tank boost from PlayerVehicleController (HUD-connected)
            if (_playerCtrl != null)
                return _playerCtrl.IsBoosting ? boostMultiplier : 1f;
            // AI: use internal cooldown-based boost
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

        /// <summary>True when the vehicle is currently boosting.</summary>
        public bool IsBoosting => _boosting;

        /// <summary>Remaining boost time in seconds (0 if not boosting).</summary>
        public float BoostTimeRemaining => _boosting ? _boostTimer : 0f;

        /// <summary>Remaining cooldown in seconds (0 if ready).</summary>
        public float BoostCooldownRemaining => Mathf.Max(_boostCooldownTimer, 0f);

        /// <summary>Current speed in world units per second.</summary>
        public float Speed => _rb != null ? _rb.linearVelocity.magnitude : 0f;

        /// <summary>
        /// Override the water surface Y at runtime (e.g. for rising water level events).
        /// Also updates WaveManager if present.
        /// </summary>
        public void SetWaterSurface(float y)
        {
            waterSurfaceY = y;
            if (WaveManager.Instance != null)
                WaveManager.Instance.baseWaterY = y;
        }

        /// <summary>
        /// Get the water surface Y at the vehicle's current XZ position.
        /// </summary>
        public float GetCurrentWaterHeight()
        {
            return GetSurfaceY(transform.position);
        }
    }
}
