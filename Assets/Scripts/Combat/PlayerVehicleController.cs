using UnityEngine;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// FPS-style free-look vehicle controller:
    ///
    /// - Mouse directly controls camera yaw/pitch (like right stick on a gamepad).
    /// - Cursor is locked to the center of the screen.
    /// - Aim = raycast from camera through screen center.
    /// - Left click fires toward the aim point.
    /// - WASD moves the vehicle (W/S forward/back, A/D turn hull).
    /// </summary>
    public class PlayerVehicleController : MonoBehaviour
    {
        // ── Movement ─────────────────────────────────────────────────────
        [Header("Movement")]
        public float moveSpeed = 22f;
        public float turnSpeed = 120f;
        public float normalDrag = 1f;

        // ── Boost ────────────────────────────────────────────────────────
        [Header("Boost")]
        public float boostMultiplier = 2f;
        public float boostRegenRate = 0.6f;
        private float _boostFuel;
        private float _maxBoostFuel = 3f;
        private bool _isBoosting;
        private bool _boostLocked;

        // ── Propulsion degradation ──────────────────────────────────────
        private int _initialPropulsionCount;
        private int _currentPropulsionCount;
        private float _baseMoveSpeed;

        // ── Fuel degradation ────────────────────────────────────────────
        private int _initialFuelCount;
        private int _currentFuelCount;
        private float _baseMaxBoostFuel;

        // ── Camera ───────────────────────────────────────────────────────
        [Header("Camera")]
        public float cameraDistance = 10f;
        public float cameraHeightOffset = 3f;
        public float pitchMin = -30f;
        public float pitchMax = 89f;
        public float positionSmooth = 10f;

        // ── Mouse Sensitivity ────────────────────────────────────────────
        [Header("Mouse Sensitivity")]
        public float sensitivityX = 2.5f;
        public float sensitivityY = 2.0f;

        // ── Aim ──────────────────────────────────────────────────────────
        [Header("Aim")]
        public float aimRayDistance = 500f;
        public LayerMask aimMask = ~0;

        // ── Public outputs ───────────────────────────────────────────────
        public Vector3 AimPoint { get; private set; }
        public Vector3 AimDirection { get; private set; }
        public float ReticleOffsetX => 0f;
        public float ReticleOffsetY => 0f;
        public float Speed => _rb != null ? _rb.linearVelocity.magnitude : 0f;
        public bool IsBoosting => _isBoosting;
        public float BoostFuel => _boostFuel;
        public float MaxBoostFuel => _maxBoostFuel;

        // ── Internal ─────────────────────────────────────────────────────
        private Rigidbody _rb;
        private Camera _cam;
        private GameObject _camPivot;
        private float _yaw;
        private float _pitch;
        private bool _paused;
        private float _inputForward;
        private float _inputTurn;
        private bool _isWaterMode; // WaterPhysics handles movement instead

        // =================================================================
        // Lifecycle
        // =================================================================

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _isWaterMode = GetComponent<CloseEncounters.VehiclePhysics.WaterPhysics>() != null;

            if (_rb != null && !_isWaterMode)
            {
                _rb.useGravity = true;
                _rb.linearDamping = normalDrag;
                _rb.angularDamping = 5f;
                // Low center of mass so gravity naturally keeps vehicle upright
                _rb.centerOfMass = new Vector3(0f, -1f, 0f);

                // Moderate friction so vehicles grip terrain but don't snag on small debris
                var vehicleMat = new PhysicsMaterial("VehicleGrip");
                vehicleMat.dynamicFriction = 0.4f;
                vehicleMat.staticFriction = 0.5f;
                vehicleMat.bounciness = 0f;
                vehicleMat.frictionCombine = PhysicsMaterialCombine.Average;
                vehicleMat.bounceCombine = PhysicsMaterialCombine.Minimum;
                foreach (var col in GetComponentsInChildren<Collider>())
                    col.material = vehicleMat;
            }

            SetupCamera();
            _yaw = transform.eulerAngles.y;

            // Lock cursor to center of screen (FPS style)
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            SetupEngineAudio();

            Debug.Log("[PlayerVehicleController] Started (FPS free-look camera)");
        }

        // ---- Engine audio (pitch scales with speed) ----
        private AudioSource _engineAudio;
        private void SetupEngineAudio()
        {
            var clip = Resources.Load<AudioClip>("Audio/Vehicle/EngineLoop");
            if (clip == null) return;
            var go = new GameObject("EngineAudio");
            go.transform.SetParent(transform, false);
            _engineAudio = go.AddComponent<AudioSource>();
            _engineAudio.clip = clip;
            _engineAudio.loop = true;
            _engineAudio.spatialBlend = 1f;
            _engineAudio.minDistance = 2f;
            _engineAudio.maxDistance = 60f;
            _engineAudio.volume = 0.5f;
            _engineAudio.playOnAwake = true;
            _engineAudio.Play();
        }

        private void LateUpdate()
        {
            if (_engineAudio != null)
            {
                float spd = Speed;
                _engineAudio.pitch = Mathf.Lerp(0.7f, 1.6f, Mathf.Clamp01(spd / 60f));
                _engineAudio.volume = Mathf.Lerp(0.25f, 0.65f, Mathf.Clamp01(spd / 40f));
            }
        }

        private void SetupCamera()
        {
            Camera existing = Camera.main;
            if (existing != null)
                Destroy(existing.gameObject);

            // Pivot object holds the camera — positioned by UpdateCamera()
            _camPivot = new GameObject("CamPivot");
            _camPivot.tag = "MainCamera";

            _cam = _camPivot.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.backgroundColor = new Color(0.4f, 0.6f, 0.9f); // fallback if no skybox
            _cam.fieldOfView = 60f;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 1000f;

            if (FindAnyObjectByType<AudioListener>() == null)
                _camPivot.AddComponent<AudioListener>();
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            ReadInput();
        }

        private void FixedUpdate()
        {
            if (Time.timeScale <= 0f) return;
            // In water mode, WaterPhysics handles thrust/steering/buoyancy
            if (!_isWaterMode)
                HandleMovement();
        }

        private void LateUpdate()
        {
            if (Time.timeScale <= 0f) return;
            HandleMouseLook();
            UpdateCamera();
            UpdateAim();
        }

        // =================================================================
        // Mouse look — FPS-style free look, cursor locked to center
        // =================================================================

        private void HandleMouseLook()
        {
            if (_paused) return;

            // Keep cursor locked during gameplay
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            float mx = Input.GetAxisRaw("Mouse X") * sensitivityX;
            float my = Input.GetAxisRaw("Mouse Y") * sensitivityY;

            _yaw += mx;
            _pitch = Mathf.Clamp(_pitch - my, pitchMin, pitchMax);
        }

        // =================================================================
        // Camera — orbits behind the vehicle at the mouse-controlled angle
        // =================================================================

        private void UpdateCamera()
        {
            if (_cam == null || _camPivot == null) return;
            if (transform == null) return;

            float dt = Time.deltaTime;

            // Camera rotation is fully driven by mouse yaw/pitch
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -cameraDistance);
            offset.y += cameraHeightOffset;

            Vector3 desiredPos = transform.position + offset;
            _camPivot.transform.position = Vector3.Lerp(
                _camPivot.transform.position, desiredPos, positionSmooth * dt);

            // Face the direction the mouse is pointing, not at the vehicle
            _camPivot.transform.rotation = rot;

            // FOV is managed by CombatCamera (right-click zoom + air speed FOV).
        }

        // =================================================================
        // Aim — raycast through screen center (where the crosshair is)
        // =================================================================

        private void UpdateAim()
        {
            if (_cam == null) return;

            // Raycast through the exact center of the screen
            Ray ray = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            AimDirection = ray.direction;

            if (Physics.Raycast(ray, out RaycastHit hit, aimRayDistance, aimMask,
                QueryTriggerInteraction.Ignore))
                AimPoint = hit.point;
            else
                AimPoint = ray.origin + ray.direction * aimRayDistance;
        }

        // =================================================================
        // Tank movement — W/S forward/back, A/D turn hull
        // =================================================================

        private void ReadInput()
        {
            _inputForward = 0f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) _inputForward += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) _inputForward -= 1f;

            _inputTurn = 0f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) _inputTurn += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) _inputTurn -= 1f;

            // Boost fuel (runs in Update for smooth UI feedback)
            // Stop boosting at 0 and require regen to 25% before allowing boost again
            bool wantsBoost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool canBoost = _boostFuel > 0f && _maxBoostFuel > 0f && !_boostLocked;

            if (wantsBoost && canBoost)
            {
                _isBoosting = true;
                _boostFuel -= Time.deltaTime;
                if (_boostFuel <= 0f)
                {
                    _boostFuel = 0f;
                    _boostLocked = true; // lock until regen reaches threshold
                }
            }
            else
            {
                _isBoosting = false;
                if (_boostFuel < _maxBoostFuel && _maxBoostFuel > 0f)
                    _boostFuel = Mathf.Min(_boostFuel + boostRegenRate * Time.deltaTime, _maxBoostFuel);
                // Unlock boost when regenerated to 25%
                if (_boostLocked && _maxBoostFuel > 0f && _boostFuel >= _maxBoostFuel * 0.25f)
                    _boostLocked = false;
            }
        }

        private void HandleMovement()
        {
            if (_rb == null) return;

            float dt = Time.fixedDeltaTime;

            // ── Ground check (only used for step-up and drag) ──
            bool grounded = Physics.Raycast(_rb.position, Vector3.down, 1.5f,
                ~0, QueryTriggerInteraction.Ignore);

            // ── Airborne: reduce drag for realistic falling ──
            if (!_isWaterMode)
            {
                _rb.linearDamping = grounded ? normalDrag : 0.05f;
            }

            // ── Turning: ALWAYS works ──
            if (Mathf.Abs(_inputTurn) > 0.01f)
            {
                float yawDelta = _inputTurn * turnSpeed * dt;
                _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, yawDelta, 0f));
            }
            // Damp angular velocity
            Vector3 av = _rb.angularVelocity;
            av.x *= 0.9f;
            av.z *= 0.9f;
            av.y = 0f;
            _rb.angularVelocity = av;

            float speed = _isBoosting ? moveSpeed * boostMultiplier : moveSpeed;

            // ── Forward/backward thrust: ALWAYS works ──
            if (Mathf.Abs(_inputForward) > 0.01f)
            {
                float thrust = _inputForward * speed * _rb.mass;
                _rb.AddForce(transform.forward * thrust, ForceMode.Force);
            }

            // ── Obstacle step-up: only when grounded ──
            if (!_isWaterMode && grounded && Mathf.Abs(_inputForward) > 0.01f
                && _rb.linearVelocity.y < 3f)
            {
                Vector3 moveDir = _inputForward > 0 ? transform.forward : -transform.forward;
                Vector3 footPos = _rb.position + Vector3.down * 0.2f;

                bool lowHit = Physics.Raycast(footPos, moveDir, 1.2f,
                    ~0, QueryTriggerInteraction.Ignore);

                if (lowHit && !Physics.Raycast(footPos + Vector3.up * 0.8f, moveDir,
                    1.2f, ~0, QueryTriggerInteraction.Ignore))
                {
                    _rb.AddForce(Vector3.up * _rb.mass * 1.5f, ForceMode.Force);
                }
            }

            // ── Speed caps ──
            Vector3 vel = _rb.linearVelocity;
            Vector3 hVel = new Vector3(vel.x, 0f, vel.z);
            float forwardSpeed = Vector3.Dot(hVel, transform.forward);

            // Forward cap: current speed (boosted or not)
            bool goingForward = _inputForward > 0f && forwardSpeed > 0f;
            if (goingForward && hVel.magnitude > speed)
            {
                Vector3 clamped = hVel.normalized * speed;
                hVel = Vector3.Lerp(hVel, clamped, 6f * dt);
                _rb.linearVelocity = new Vector3(hVel.x, vel.y, hVel.z);
            }

            // Reverse cap: half of unboosted max speed (full acceleration, hard speed limit)
            float reverseMax = moveSpeed * 0.5f;
            bool goingReverse = _inputForward < 0f && forwardSpeed < 0f;
            if (goingReverse && hVel.magnitude > reverseMax)
            {
                Vector3 clamped = hVel.normalized * reverseMax;
                _rb.linearVelocity = new Vector3(clamped.x, vel.y, clamped.z);
            }

            // ── Extra gravity when airborne for fast, realistic falling ──
            if (!_isWaterMode && !grounded)
            {
                _rb.AddForce(Vector3.down * 25f, ForceMode.Acceleration);
            }
        }

        /// <summary>
        /// Initialize boost fuel capacity from fuel tank count.
        /// Base 3s + 2s per fuel tank (matching Godot).
        /// </summary>
        public void InitBoost(int fuelTankCount)
        {
            _maxBoostFuel = fuelTankCount > 0 ? 3f + fuelTankCount * 2f : 0f;
            _boostFuel = _maxBoostFuel;
            _baseMaxBoostFuel = _maxBoostFuel;
        }

        /// <summary>
        /// Initialize propulsion degradation tracking.
        /// Call during vehicle setup with the number of propulsion parts.
        /// </summary>
        public void InitPropulsionTracking(int propulsionPartCount)
        {
            _initialPropulsionCount = propulsionPartCount;
            _currentPropulsionCount = propulsionPartCount;
            _baseMoveSpeed = moveSpeed;
        }

        /// <summary>
        /// Initialize fuel degradation tracking.
        /// Call during vehicle setup with the number of fuel parts.
        /// </summary>
        public void InitFuelTracking(int fuelPartCount)
        {
            _initialFuelCount = fuelPartCount;
            _currentFuelCount = fuelPartCount;
            // _baseMaxBoostFuel is set in InitBoost
        }

        /// <summary>
        /// Called when a propulsion part is destroyed.
        /// Proportionally reduces move speed (min 20% of base).
        /// </summary>
        public void OnPropulsionPartDestroyed()
        {
            _currentPropulsionCount = Mathf.Max(_currentPropulsionCount - 1, 0);
            float ratio = _initialPropulsionCount > 0
                ? (float)_currentPropulsionCount / _initialPropulsionCount
                : 0f;
            float speedFraction = Mathf.Lerp(0.2f, 1.0f, ratio);
            moveSpeed = _baseMoveSpeed * speedFraction;
        }

        /// <summary>
        /// Called when a fuel part is destroyed.
        /// Proportionally reduces max boost capacity (min 20% of base).
        /// Clamps current fuel to the new max.
        /// </summary>
        public void OnFuelPartDestroyed()
        {
            _currentFuelCount = Mathf.Max(_currentFuelCount - 1, 0);
            // All fuel destroyed → capacity drops to 0 (no boosting)
            // Otherwise proportional reduction
            float ratio = _initialFuelCount > 0
                ? (float)_currentFuelCount / _initialFuelCount
                : 0f;
            _maxBoostFuel = _baseMaxBoostFuel * ratio;
            if (_boostFuel > _maxBoostFuel)
                _boostFuel = _maxBoostFuel;
            if (_maxBoostFuel <= 0f)
                _boostLocked = true;
        }

        /// <summary>
        /// Reduce max boost capacity by a flat amount (legacy fallback).
        /// Clamps current fuel to the new max.
        /// </summary>
        public void ReduceBoostCapacity(float amount)
        {
            _maxBoostFuel = Mathf.Max(_maxBoostFuel - amount, 0f);
            if (_boostFuel > _maxBoostFuel)
                _boostFuel = _maxBoostFuel;
        }

        // =================================================================
        // Cursor management (pause menu)
        // =================================================================

        public void UnlockCursor()
        {
            _paused = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void RelockCursor()
        {
            _paused = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public Vector3 GetAimPoint() => AimPoint;
    }
}
