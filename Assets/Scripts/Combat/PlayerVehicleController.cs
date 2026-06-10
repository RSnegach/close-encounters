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
        // why: air mode steers off cursor POSITION (WoWP/GTA). Mouse delta (raw,
        // per-frame) * this accumulates the cursor; ~0.3 reaches full deflection over
        // a deliberate drag without being twitchy.
        public float airSensitivityX = 0.19f;
        public float airSensitivityY = 0.2f;

        // ── Aim ──────────────────────────────────────────────────────────
        [Header("Aim")]
        public float aimRayDistance = 500f;
        public LayerMask aimMask = ~0;

        // ── Public outputs ───────────────────────────────────────────────
        public Vector3 AimPoint { get; private set; }
        public Vector3 AimDirection { get; private set; }
        // Reticle stays fixed at screen center in all modes.
        public float ReticleOffsetX => 0f;
        public float ReticleOffsetY => 0f;
        public float Speed => _rb != null ? _rb.linearVelocity.magnitude : 0f;
        public bool IsBoosting => _isBoosting;
        public float BoostFuel => _boostFuel;
        public float MaxBoostFuel => _maxBoostFuel;

        // ── Air flight (mouse-steered, centered reticle) ───────────────
        [Header("Air Flight")]
        public float airCruiseSpeed = 40f;
        public float airBoostSpeed = 90f;
        public float airAcceleration = 20f;
        public float airPitchRate = 95f;       // deg/sec pitch at full cursor deflection
        public float airTurnRate = 75f;         // deg/sec heading change (yaw) at full deflection
        public float airBankAngle = 55f;        // deg of bank the plane rolls into a full turn
        public float airRollRate = 140f;        // Q/E hold-roll rate
        public float cursorReturnSpeed = 5f;     // how fast the cursor re-centres when you let go
        public float aimFollowSmooth = 6f;       // how snappily bank/level follows the demand
        // Barrel roll
        public float barrelRollSpeed = 540f;     // deg/sec during an automated 360 (double-tap)
        public float heldRollMultiplier = 2f;    // held Q/E rolls this much faster than airRollRate
        public float doubleTapWindow = 0.3f;     // max seconds between taps to trigger a preset
        // Aerobatics presets
        public float loopSpeed = 360f;           // deg/sec for Z loops (1s per 360)
        public float tailSlideDuration = 1.6f;   // seconds for the C tail slide
        public float flatSpinYawRate = 240f;     // deg/sec spin while holding F
        public float flatSpinDescent = 18f;      // m/s downward drift in a flat spin
        private float _airSpeed;
        private bool _isAirMode;
        private Vector2 _mouseAimOffset;          // normalized -1..1 aim demand
        private Vector3 _turbulenceForce;
        private float _turbulenceTimer;
        private float _bounceTimer;
        // Barrel roll bookkeeping
        private float _lastQTapTime = -10f;
        private float _lastETapTime = -10f;
        private float _barrelRollRemaining;       // degrees left in an automated roll
        private float _barrelRollDir;             // +1 right (E), -1 left (Q)
        private float _airRollInput;              // held Q/E roll, -1..1
        // Loop / tail slide / flat spin bookkeeping
        private float _lastZTapTime = -10f;
        private float _loopRemaining;             // degrees left in an automated loop
        private float _loopDir;                   // +1 forward (nose down/over), -1 backward (nose up/over)
        private Vector3 _loopAxis;                // fixed horizontal pitch axis captured at loop start
        private float _tailSlideTimer;            // seconds left in the tail slide
        private Vector3 _tailSlideBackDir;        // horizontal "behind me" dir captured at slide start
        private bool _flatSpinActive;             // true while F is held
        public float AirThrottle => _isBoosting ? 1f : 0.5f;
        public bool IsStalling => false;
        public bool IsAirMode => _isAirMode;
        public bool IsBarrelRolling => _barrelRollRemaining > 0f;
        public bool IsLooping => _loopRemaining > 0f;
        public bool IsTailSliding => _tailSlideTimer > 0f;
        public bool IsFlatSpinning => _flatSpinActive;

        public void ApplyTurbulence(Vector3 force, float duration)
        {
            _turbulenceForce = force;
            _turbulenceTimer = duration;
        }

        public void ApplyBounce(float duration)
        {
            _bounceTimer = duration;
        }

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

            // Detect air mode from GameManager
            var gm = CloseEncounters.Core.GameManager.Instance;
            _isAirMode = gm != null && gm.Settings != null && gm.Settings.domain == "air";

            if (_rb != null && _isAirMode)
            {
                _rb.useGravity = false;
                _rb.linearDamping = 0f;
                _rb.angularDamping = 0f;
                _rb.centerOfMass = Vector3.zero;
                _rb.constraints = RigidbodyConstraints.FreezeRotation;
                _airSpeed = airCruiseSpeed;
            }
            else if (_rb != null && !_isWaterMode)
            {
                _rb.useGravity = true;
                _rb.linearDamping = normalDrag;
                _rb.angularDamping = 5f;
                _rb.centerOfMass = new Vector3(0f, -1f, 0f);

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

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            SetupEngineAudio();

            Debug.Log($"[PlayerVehicleController] Started (mode={(_isAirMode ? "air" : _isWaterMode ? "water" : "ground")})");
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
            if (_isAirMode)
                HandleAirMovement();
            else if (!_isWaterMode)
                HandleMovement();
        }

        private void LateUpdate()
        {
            if (_engineAudio != null)
            {
                float spd = Speed;
                _engineAudio.pitch = Mathf.Lerp(0.7f, 1.6f, Mathf.Clamp01(spd / 60f));
                _engineAudio.volume = Mathf.Lerp(0.25f, 0.65f, Mathf.Clamp01(spd / 40f));
            }
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

            if (_isAirMode)
            {
                // Chase cam: behind + above, with slight lag for feel
                Vector3 idealPos = transform.position
                    - transform.forward * cameraDistance
                    + transform.up * cameraHeightOffset;

                _camPivot.transform.position = Vector3.Lerp(
                    _camPivot.transform.position, idealPos, 6f * dt);

                // Look at a point ahead of the plane
                Vector3 lookTarget = transform.position + transform.forward * 20f;
                Quaternion lookRot = Quaternion.LookRotation(lookTarget - _camPivot.transform.position);
                _camPivot.transform.rotation = Quaternion.Slerp(
                    _camPivot.transform.rotation, lookRot, 8f * dt);
            }
            else
            {
                Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
                Vector3 offset = rot * new Vector3(0f, 0f, -cameraDistance);
                offset.y += cameraHeightOffset;

                Vector3 desiredPos = transform.position + offset;
                _camPivot.transform.position = Vector3.Lerp(
                    _camPivot.transform.position, desiredPos, positionSmooth * dt);

                _camPivot.transform.rotation = rot;
            }
        }

        // =================================================================
        // Aim — raycast through the fixed center reticle
        // =================================================================

        private void UpdateAim()
        {
            if (_cam == null) return;

            // Raycast through the exact center of the screen, where the reticle sits.
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

            // ── Air aerobatics & roll ──
            if (_isAirMode)
            {
                bool maneuverBusy = IsBarrelRolling || IsLooping || IsTailSliding || IsFlatSpinning;

                // Roll: hold Q = roll right, hold E = roll left.
                // Double-tap either for a full automated 360 barrel roll that direction.
                if (Input.GetKeyDown(KeyCode.E))
                {
                    if (Time.time - _lastETapTime <= doubleTapWindow && !maneuverBusy)
                    {
                        _barrelRollRemaining = 360f;
                        _barrelRollDir = -1f;
                    }
                    _lastETapTime = Time.time;
                }
                if (Input.GetKeyDown(KeyCode.Q))
                {
                    if (Time.time - _lastQTapTime <= doubleTapWindow && !maneuverBusy)
                    {
                        _barrelRollRemaining = 360f;
                        _barrelRollDir = 1f;
                    }
                    _lastQTapTime = Time.time;
                }

                _airRollInput = 0f;
                if (Input.GetKey(KeyCode.Q)) _airRollInput += 1f;
                if (Input.GetKey(KeyCode.E)) _airRollInput -= 1f;

                // Loop: tap Z = backward loop (nose pulls up and over);
                //       double-tap Z = forward loop (nose pushes down and over).
                if (Input.GetKeyDown(KeyCode.Z) && !maneuverBusy)
                {
                    _loopRemaining = 360f;
                    _loopDir = (Time.time - _lastZTapTime <= doubleTapWindow) ? 1f : -1f;
                    // Capture a FIXED horizontal pitch axis now, so the loop keeps
                    // rotating the same way past vertical and goes fully inverted.
                    Vector3 axis = Vector3.Cross(Vector3.up, transform.forward);
                    _loopAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : transform.right;
                    _lastZTapTime = Time.time;
                }

                // Tail slide: tap C. Capture the current horizontal heading so the
                // slide drifts cleanly backward instead of following the rearing nose.
                if (Input.GetKeyDown(KeyCode.C) && !maneuverBusy)
                {
                    _tailSlideTimer = tailSlideDuration;
                    Vector3 flat = transform.forward;
                    flat.y = 0f;
                    _tailSlideBackDir = flat.sqrMagnitude > 0.0001f ? -flat.normalized : -transform.forward;
                    // Fixed horizontal axis so the rear-up arcs straight up and over.
                    Vector3 axis = Vector3.Cross(Vector3.up, transform.forward);
                    _loopAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : transform.right;
                }

                // Flat spin: held while F is down, but never while a one-shot
                // preset (barrel roll, loop, tail slide) is mid-run.
                _flatSpinActive = Input.GetKey(KeyCode.F)
                    && !(IsBarrelRolling || IsLooping || IsTailSliding);
            }

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

        // =================================================================
        // Air flight — WoWP/GTA style: mouse aims, plane chases the aim
        // =================================================================

        private void HandleAirMovement()
        {
            if (_rb == null) return;
            float dt = Time.fixedDeltaTime;

            // ── Speed: cruise normally, boost with shift ──
            float targetSpeed = _isBoosting ? airBoostSpeed : airCruiseSpeed;
            _airSpeed = Mathf.MoveTowards(_airSpeed, targetSpeed, airAcceleration * dt);

            // ── Mouse steering (WoWP/GTA): the cursor holds a POSITION (not a
            //    velocity). Drag accumulates it; it only re-centres when you let go.
            //    A held position gives a SUSTAINED turn, not just a momentary tilt. ──
            // Mouse delta is already a per-frame quantity — do NOT scale by dt (that
            // made it ~50x too weak). Accumulate raw delta * sensitivity into a held
            // cursor position; it only re-centres when the mouse stops.
            float mx = Input.GetAxisRaw("Mouse X");
            float my = Input.GetAxisRaw("Mouse Y");
            _mouseAimOffset.x += mx * airSensitivityX;
            _mouseAimOffset.y += my * airSensitivityY; // mouse up → demandPitch>0 → nose up

            // Re-centre only when that axis' mouse is still, so the turn holds while dragging.
            if (Mathf.Abs(mx) < 0.01f) _mouseAimOffset.x = Mathf.MoveTowards(_mouseAimOffset.x, 0f, cursorReturnSpeed * dt);
            if (Mathf.Abs(my) < 0.01f) _mouseAimOffset.y = Mathf.MoveTowards(_mouseAimOffset.y, 0f, cursorReturnSpeed * dt);
            _mouseAimOffset.x = Mathf.Clamp(_mouseAimOffset.x, -1f, 1f);
            _mouseAimOffset.y = Mathf.Clamp(_mouseAimOffset.y, -1f, 1f);

            // ── Automated loop (Z): trace a clean vertical loop. Pitch about the
            //    HORIZON-STABLE right axis (level wings) so it arcs, not corkscrews. ──
            if (_loopRemaining > 0f)
            {
                float step = Mathf.Min(loopSpeed * dt, _loopRemaining);
                _loopRemaining -= step;
                // Rotate about the FIXED axis captured at loop start (pre-multiply =
                // world-space axis) so it keeps going the same way over the top and
                // flips fully inverted. _loopDir +1 = forward, -1 = backward.
                transform.rotation = Quaternion.AngleAxis(step * _loopDir, _loopAxis) * transform.rotation;

                if (_bounceTimer > 0f) { _bounceTimer -= dt; return; }
                _rb.linearVelocity = transform.forward * _airSpeed;
                return;
            }

            // ── Tail slide (C): rear straight up, hang, slide backward+down. ──
            if (_tailSlideTimer > 0f)
            {
                _tailSlideTimer -= dt;
                // Pitch the nose up about the FIXED horizontal axis (pre-multiply =
                // world-space) so it rears straight up and over, not stalling at vertical.
                transform.rotation = Quaternion.AngleAxis(-80f * dt, _loopAxis) * transform.rotation;
                _airSpeed = Mathf.MoveTowards(_airSpeed, 0f, airAcceleration * 2f * dt);
                // Slide backward along the captured heading and fall.
                _rb.linearVelocity = _tailSlideBackDir * 5f + Vector3.down * 12f;
                return;
            }

            // ── Flat spin (hold F): stay level, spin about world up, and sink. ──
            if (_flatSpinActive)
            {
                Vector3 fwdFlat = transform.forward;
                fwdFlat.y = 0f;
                if (fwdFlat.sqrMagnitude > 0.0001f)
                {
                    Quaternion level = Quaternion.LookRotation(fwdFlat.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, level, 120f * dt);
                }
                transform.rotation = Quaternion.AngleAxis(flatSpinYawRate * dt, Vector3.up) * transform.rotation;

                _airSpeed = Mathf.MoveTowards(_airSpeed, airCruiseSpeed * 0.3f, airAcceleration * dt);
                _rb.linearVelocity = transform.forward * _airSpeed + Vector3.down * flatSpinDescent;
                return;
            }

            // ── Normal flight: cursor position → sustained turn + pitch + roll.
            //    Yaw and pitch ALWAYS apply (you can still steer up/down/left/right
            //    even mid barrel-roll); only the roll source changes. ──
            float demandYaw = _mouseAimOffset.x;   // +1 = turn right
            float demandPitch = _mouseAimOffset.y; // +1 = nose up

            // YAW = real heading change about WORLD up → the plane actually TURNS.
            float yawDelta = demandYaw * airTurnRate * dt;
            transform.rotation = Quaternion.AngleAxis(yawDelta, Vector3.up) * transform.rotation;

            // PITCH about the body right axis → climb/dive.
            // AngleAxis(+, right) pitches the nose DOWN, so use -demandPitch for nose-up.
            float pitchDelta = -demandPitch * airPitchRate * dt;
            transform.rotation = transform.rotation * Quaternion.AngleAxis(pitchDelta, Vector3.right);

            // ── Roll source (in priority order) ──
            if (_barrelRollRemaining > 0f)
            {
                // Automated 360 from a double-tap. Spins on top of live steering so
                // the player keeps full pitch/yaw control through the roll. The
                // auto-bank is skipped so it can't fight the spin.
                float step = Mathf.Min(barrelRollSpeed * dt, _barrelRollRemaining);
                _barrelRollRemaining -= step;
                transform.rotation = transform.rotation *
                    Quaternion.AngleAxis(step * _barrelRollDir, Vector3.forward);
            }
            else if (Mathf.Abs(_airRollInput) > 0.01f)
            {
                // Held roll, twice the base rate. Q = +1 (right), E = -1 (left).
                transform.rotation = transform.rotation *
                    Quaternion.AngleAxis(_airRollInput * airRollRate * heldRollMultiplier * dt, Vector3.forward);
            }
            else
            {
                // Cosmetic coordinated-turn bank, eased toward a target.
                // Tilt the opposite way from the turn input (negated).
                float currentRoll = transform.eulerAngles.z;
                if (currentRoll > 180f) currentRoll -= 360f;
                float targetBank = -demandYaw * airBankAngle;
                float bankDelta = (targetBank - currentRoll) * aimFollowSmooth * dt;
                transform.rotation = transform.rotation * Quaternion.AngleAxis(bankDelta, Vector3.forward);
            }

            // ── Bounce: let physics handle velocity briefly after mid-air collision ──
            if (_bounceTimer > 0f)
            {
                _bounceTimer -= dt;
                return;
            }

            // ── Velocity: always move forward at current air speed ──
            Vector3 velocity = transform.forward * _airSpeed;

            // Turbulence override (tornado, etc)
            if (_turbulenceTimer > 0f)
            {
                _turbulenceTimer -= dt;
                velocity += _turbulenceForce;
                float tumble = _turbulenceForce.magnitude * 2f * dt;
                transform.Rotate(
                    Random.Range(-tumble, tumble),
                    Random.Range(-tumble, tumble),
                    Random.Range(-tumble, tumble));
            }

            _rb.linearVelocity = velocity;
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
