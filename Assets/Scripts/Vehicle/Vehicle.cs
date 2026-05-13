using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Core;
using CloseEncounters.Combat;

namespace CloseEncounters.Vehicle
{
    /// <summary>
    /// Core runtime vehicle component. Handles movement (ground/water/air),
    /// weapons, damage routing, boost, death, and combat stats.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Vehicle : MonoBehaviour
    {
        // ==================================================================
        // Data / identity
        // ==================================================================

        public string vehicleName;
        public string domain = "ground";
        public bool isPlayerControlled;
        public bool isAIControlled;
        public int peerId = -1;

        // ==================================================================
        // Parts
        // ==================================================================

        private readonly Dictionary<Vector3Int, PartNode> _parts = new Dictionary<Vector3Int, PartNode>();
        private PartNode _controlModule;

        public IReadOnlyDictionary<Vector3Int, PartNode> Parts => _parts;
        public PartNode ControlModule => _controlModule;

        // ==================================================================
        // Aggregate stats (recomputed from parts)
        // ==================================================================

        public float totalMass;
        public float totalThrust;
        public float totalDrag;
        public float totalFuel;
        public float maxFuel;
        public int totalHp;
        public int maxHp;

        // ==================================================================
        // Weapons
        // ==================================================================

        private readonly List<WeaponSlot> _weapons = new List<WeaponSlot>();
        public int activeWeaponIndex;
        public IReadOnlyList<WeaponSlot> Weapons => _weapons;

        // ==================================================================
        // State
        // ==================================================================

        public bool isAlive = true;
        public float forwardYaw;

        // ==================================================================
        // Boost
        // ==================================================================

        public float boostFuel;
        public float boostMaxFuel = 100f;
        public bool isBoosting;
        public float boostMultiplier = 2f;
        public float boostRegenRate = 8f;
        public float boostDrainRate = 25f;

        // ==================================================================
        // Camera / aiming (player only)
        // ==================================================================

        public Vector2 mouseDelta;
        public float cameraYaw;
        public float cameraPitch;
        public Vector3 aimDirection;
        public Vector2 reticleOffset;

        // ==================================================================
        // Combat stats
        // ==================================================================

        public CombatStats combatStats;

        // ==================================================================
        // Physics refs
        // ==================================================================

        private Rigidbody _rb;

        // ==================================================================
        // Movement tuning
        // ==================================================================

        private const float GroundRotateSpeed = 120f;
        private const float GroundMoveForce = 15f;
        private const float AirPitchSpeed = 80f;
        private const float AirRollSpeed = 100f;
        private const float AirYawSpeed = 40f;
        private const float AirThrustForce = 20f;
        private const float TerrainAlignSpeed = 5f;
        private const float MaxGroundSpeed = 30f;
        public const float MaxAirSpeed = 60f;
        private const float CameraSmoothing = 8f;
        private const float CellSize = 1f;

        // ==================================================================
        // Air flight tuning (vibe-jet style)
        // ==================================================================

        [Header("Air Flight")]
        [SerializeField] private float stallSpeed = 12f; // why: below this, wings stop producing meaningful lift
        [SerializeField] private float cruiseSpeed = 35f; // why: speed at which lift fully counteracts gravity
        [SerializeField] private float autoRollMaxAngle = 45f;
        [SerializeField] private float autoRollStrength = 4f;
        [SerializeField] private float autoRollReturnSpeed = 3f;
        [SerializeField] private float throttleChangeRate = 0.6f;
        [SerializeField] private float coordinatedTurnStrength = 0.5f;
        [SerializeField] private float mousePitchSensitivity = 1.2f;
        [SerializeField] private float stallPitchAuthority = 0.3f;
        [SerializeField] private float highSpeedPitchBoost = 1.15f; // why: punchy feel above 80% MaxAirSpeed

        public float airThrottle = 0.7f;
        public bool IsStalling { get; private set; }

        // ==================================================================
        // Events
        // ==================================================================

        public event Action<Vehicle> OnVehicleDied;
        public event Action<PartNode, int> OnPartDamaged;
        public event Action<PartNode> OnPartDestroyed;
        public event Action OnWeaponFired;

        // ==================================================================
        // Setup
        // ==================================================================

        /// <summary>
        /// Build the vehicle from saved VehicleData. Creates PartNodes, wires
        /// events, configures physics.
        /// </summary>
        public void SetupFromData(VehicleData data)
        {
            if (data == null)
            {
                Debug.LogError("[Vehicle] Null VehicleData.");
                return;
            }

            vehicleName = data.name;
            domain = data.domain;
            forwardYaw = data.forwardAngle;

            _rb = GetComponent<Rigidbody>();
            combatStats = new CombatStats();

            // Rotate the vehicle root to match forward direction
            transform.rotation = Quaternion.Euler(0f, forwardYaw, 0f);

            // Build parts
            ClearParts();

            for (int i = 0; i < data.parts.Count; i++)
            {
                PartEntry entry = data.parts[i];
                PartData partData = PartRegistry.Instance?.GetPart(entry.id);
                if (partData == null)
                {
                    Debug.LogWarning($"[Vehicle] Part not found in registry: {entry.id}");
                    continue;
                }

                Vector3Int gp = new Vector3Int(
                    entry.gridPosition.Length > 0 ? entry.gridPosition[0] : 0,
                    entry.gridPosition.Length > 1 ? entry.gridPosition[1] : 0,
                    entry.gridPosition.Length > 2 ? entry.gridPosition[2] : 0
                );

                SpawnPart(partData, gp);
            }

            RecalculateStats();
            ConfigureRigidbody();

            boostFuel = boostMaxFuel;
            isAlive = true;
        }

        private void SpawnPart(PartData data, Vector3Int gridPos)
        {
            // Occupy all cells for multi-cell parts; store under the origin cell
            for (int dx = 0; dx < data.size.x; dx++)
            for (int dy = 0; dy < data.size.y; dy++)
            for (int dz = 0; dz < data.size.z; dz++)
            {
                Vector3Int cell = gridPos + new Vector3Int(dx, dy, dz);
                if (_parts.ContainsKey(cell))
                {
                    Debug.LogWarning($"[Vehicle] Cell {cell} already occupied — skipping overlap for {data.id}");
                }
            }

            var go = new GameObject();
            go.transform.SetParent(transform, false);
            go.transform.localPosition = GridToLocal(gridPos);

            var node = go.AddComponent<PartNode>();
            node.Setup(data, gridPos);

            // Wire events
            node.OnPartDamaged += (dmg) => HandlePartDamaged(node, dmg);
            node.OnPartDestroyed += () => HandlePartDestroyed(node);

            _parts[gridPos] = node;

            // Track control module
            if (data.IsControlModule() && _controlModule == null)
                _controlModule = node;

            // Track weapons
            if (string.Equals(data.category, "weapon", StringComparison.OrdinalIgnoreCase))
            {
                _weapons.Add(new WeaponSlot(node));
            }
        }

        private void ClearParts()
        {
            foreach (var kv in _parts)
            {
                if (kv.Value != null && kv.Value.gameObject != null)
                    Destroy(kv.Value.gameObject);
            }
            _parts.Clear();
            _weapons.Clear();
            _controlModule = null;
        }

        // ==================================================================
        // Stats
        // ==================================================================

        public void RecalculateStats()
        {
            totalMass = 0f;
            totalThrust = 0f;
            totalDrag = 0f;
            totalFuel = 0f;
            totalHp = 0;
            maxHp = 0;

            foreach (var kv in _parts)
            {
                PartNode node = kv.Value;
                if (node == null) continue;

                PartData pd = node.partData;
                maxHp += pd.hp;

                if (!node.IsFunctional()) continue;

                totalMass += pd.massKg;
                totalDrag += pd.drag;
                totalThrust += pd.GetStat<float>("thrust", 0f);

                float fuel = pd.GetStat<float>("fuel", 0f);
                totalFuel += fuel;

                totalHp += node.currentHp;
            }

            maxFuel = totalFuel;

            // Boost fuel scales with fuel tanks
            if (totalFuel > 0f)
                boostMaxFuel = 50f + totalFuel * 0.5f;
        }

        private void ConfigureRigidbody()
        {
            if (_rb == null) return;

            _rb.mass = Mathf.Max(totalMass, 1f);
            _rb.linearDamping = Mathf.Clamp(totalDrag * 0.5f, 0f, 5f);
            _rb.angularDamping = 2f;
            _rb.useGravity = !IsSpaceDomain();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            // why: discrete CD tunnels through mountains/cliffs at boost speed
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (IsWaterDomain())
            {
                // WaterPhysics component handles buoyancy — just reduce gravity effect
                _rb.linearDamping = Mathf.Clamp(totalDrag * 2f, 0.5f, 8f);
            }

            ConfigureOutOfBoundsController();
        }

        private OutOfBoundsController _oobController;

        private void ConfigureOutOfBoundsController()
        {
            bool isAir = GetDomainType() == DomainType.Air;
            if (!isAir)
            {
                if (_oobController != null)
                {
                    _oobController.onOutOfBoundsExpired -= HandleOutOfBoundsExpired;
                    Destroy(_oobController);
                    _oobController = null;
                }
                return;
            }

            if (_oobController == null)
                _oobController = GetComponent<OutOfBoundsController>();
            if (_oobController == null)
                _oobController = gameObject.AddComponent<OutOfBoundsController>();

            // why: idempotent — re-subscribing without unsub would double-invoke Die
            _oobController.onOutOfBoundsExpired -= HandleOutOfBoundsExpired;
            _oobController.onOutOfBoundsExpired += HandleOutOfBoundsExpired;
        }

        /// <summary>Public accessor so HUD can bind to OOB state without touching internals.</summary>
        public OutOfBoundsController GetOutOfBoundsController() => _oobController;

        private void HandleOutOfBoundsExpired()
        {
            if (!isAlive) return;
            VFXManager.BigExplosion(transform.position, 2f);
            VFXManager.LargeFlames(transform.position, 1.5f);
            Die();
        }

        // ==================================================================
        // Update
        // ==================================================================

        private void Update()
        {
            if (!isAlive) return;
            if (!isPlayerControlled) return;

            GatherInput();
            UpdateBoost();
        }

        private void FixedUpdate()
        {
            if (!isAlive) return;
            if (_rb == null) return;

            DomainType dt = GetDomainType();

            if (isPlayerControlled)
            {
                switch (dt)
                {
                    case DomainType.Ground:
                        FixedUpdateGround();
                        break;
                    case DomainType.Water:
                        // WaterPhysics handles movement; we just apply thrust
                        FixedUpdateWater();
                        break;
                    case DomainType.Air:
                        FixedUpdateAir();
                        break;
                    // Space domain removed
                }
            }

            // Clamp velocity
            float maxSpeed = dt == DomainType.Air ? MaxAirSpeed : MaxGroundSpeed;
            if (isBoosting) maxSpeed *= boostMultiplier;
            if (_rb.linearVelocity.magnitude > maxSpeed)
                _rb.linearVelocity = _rb.linearVelocity.normalized * maxSpeed;
        }

        // ------------------------------------------------------------------
        // Input gathering
        // ------------------------------------------------------------------

        private float _inputH;
        private float _inputV;
        private bool _inputFire;
        private bool _inputBoost;
        private bool _inputSwitchWeapon;

        private void GatherInput()
        {
            _inputH = Input.GetAxis("Horizontal");
            _inputV = Input.GetAxis("Vertical");
            _inputFire = Input.GetButton("Fire1");
            _inputBoost = Input.GetKey(KeyCode.LeftShift);
            _inputSwitchWeapon = Input.GetKeyDown(KeyCode.Tab);

            mouseDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

            // Camera rotation
            cameraYaw += mouseDelta.x * CameraSmoothing;
            cameraPitch -= mouseDelta.y * CameraSmoothing;
            cameraPitch = Mathf.Clamp(cameraPitch, -80f, 80f);

            // Aim direction from camera
            aimDirection = Quaternion.Euler(cameraPitch, cameraYaw, 0f) * Vector3.forward;

            if (_inputSwitchWeapon && _weapons.Count > 0)
            {
                activeWeaponIndex = (activeWeaponIndex + 1) % _weapons.Count;
            }

            if (_inputFire)
            {
                FireWeapons();
            }
        }

        // ------------------------------------------------------------------
        // Boost
        // ------------------------------------------------------------------

        private void UpdateBoost()
        {
            if (_inputBoost && boostFuel > 0f && totalThrust > 0f)
            {
                isBoosting = true;
                boostFuel -= boostDrainRate * Time.deltaTime;
                if (boostFuel <= 0f)
                {
                    boostFuel = 0f;
                    isBoosting = false;
                }
            }
            else
            {
                isBoosting = false;
                // Regen boost fuel
                if (boostFuel < boostMaxFuel)
                {
                    boostFuel += boostRegenRate * Time.deltaTime;
                    if (boostFuel > boostMaxFuel)
                        boostFuel = boostMaxFuel;
                }
            }
        }

        // ------------------------------------------------------------------
        // Ground movement: A/D rotate, W/S forward, terrain raycast align
        // ------------------------------------------------------------------

        private static readonly RaycastHit[] _groundProbeHits = new RaycastHit[8];

        private void FixedUpdateGround()
        {
            float thrust = GetEffectiveThrust();
            float dt = Time.fixedDeltaTime;

            // Rotation
            float rotateInput = _inputH;
            if (Mathf.Abs(rotateInput) > 0.01f)
            {
                float rotAmount = rotateInput * GroundRotateSpeed * dt;
                Quaternion rot = Quaternion.Euler(0f, rotAmount, 0f);
                _rb.MoveRotation(_rb.rotation * rot);
            }

            // Forward/backward
            float moveInput = _inputV;
            if (Mathf.Abs(moveInput) > 0.01f)
            {
                Vector3 forward = transform.forward;
                float force = moveInput * thrust * GroundMoveForce * dt;
                if (isBoosting) force *= boostMultiplier;
                _rb.AddForce(forward * force, ForceMode.Force);
            }

            // Terrain alignment via raycast
            AlignToTerrain(dt);

            // Anti-tunnel sweep: stop short of rocks/cliffs before discrete step skips through
            CloseEncounters.VehiclePhysics.AntiPhaseCapsule.GateVelocity(
                _rb, transform, GetChassisRadius(), ~0, 1.5f);
        }

        private float GetChassisRadius()
        {
            // Conservative chassis sphere: half the max lateral extent from the grid
            float maxExtent = 1f;
            foreach (var kv in _parts)
            {
                Vector3Int gp = kv.Key;
                float r = Mathf.Max(Mathf.Abs(gp.x), Mathf.Abs(gp.z)) * CellSize + 0.5f;
                if (r > maxExtent) maxExtent = r;
            }
            return Mathf.Min(maxExtent, 3.5f);
        }

        private void AlignToTerrain(float dt)
        {
            // Origin raised above any part of the chassis so the cast cannot
            // start inside the vehicle's own colliders (previously used a flat
            // 2 m offset + ~0 mask which self-hit tall builds).
            Vector3 origin = transform.position + Vector3.up * (GetGroundOffset() + 2f);
            int count = Physics.RaycastNonAlloc(
                origin, Vector3.down, _groundProbeHits, 20f, ~0, QueryTriggerInteraction.Ignore);

            float bestDist = float.PositiveInfinity;
            int bestIdx = -1;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _groundProbeHits[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0) return;

            RaycastHit hit = _groundProbeHits[bestIdx];

            // Smoothly align the up vector to the surface normal
            Vector3 currentUp = transform.up;
            Vector3 targetUp = hit.normal;
            Vector3 smoothedUp = Vector3.Lerp(currentUp, targetUp, dt * TerrainAlignSpeed);

            Quaternion targetRot = Quaternion.FromToRotation(currentUp, smoothedUp) * _rb.rotation;
            _rb.MoveRotation(targetRot);

            // Keep the vehicle on the ground
            float desiredY = hit.point.y + GetGroundOffset();
            Vector3 pos = _rb.position;
            pos.y = Mathf.Lerp(pos.y, desiredY, dt * TerrainAlignSpeed);
            _rb.MovePosition(pos);
        }

        private float GetGroundOffset()
        {
            // Half the lowest part extent
            float minY = 0f;
            foreach (var kv in _parts)
            {
                if (kv.Value != null && kv.Value.IsFunctional())
                {
                    float partBottom = kv.Key.y * CellSize;
                    if (partBottom < minY) minY = partBottom;
                }
            }
            return -minY + 0.3f;
        }

        // ------------------------------------------------------------------
        // Water movement: basic thrust, WaterPhysics handles buoyancy
        // ------------------------------------------------------------------

        private void FixedUpdateWater()
        {
            float thrust = GetEffectiveThrust();
            float dt = Time.fixedDeltaTime;

            // Yaw rotation
            float rotateInput = _inputH;
            if (Mathf.Abs(rotateInput) > 0.01f)
            {
                float rotAmount = rotateInput * GroundRotateSpeed * 0.6f * dt;
                Quaternion rot = Quaternion.Euler(0f, rotAmount, 0f);
                _rb.MoveRotation(_rb.rotation * rot);
            }

            // Forward thrust
            float moveInput = _inputV;
            if (Mathf.Abs(moveInput) > 0.01f)
            {
                Vector3 forward = transform.forward;
                // Project forward onto the water plane
                forward.y = 0f;
                forward.Normalize();

                float force = moveInput * thrust * GroundMoveForce * 0.7f * dt;
                if (isBoosting) force *= boostMultiplier;
                _rb.AddForce(forward * force, ForceMode.Force);
            }
        }

        // ------------------------------------------------------------------
        // Air movement: camera-based flight controls
        // ------------------------------------------------------------------

        private void FixedUpdateAir()
        {
            float thrust = GetEffectiveThrust();
            float dt = Time.fixedDeltaTime;
            float speed = _rb.linearVelocity.magnitude;

            // Throttle state from W/S
            airThrottle = Mathf.Clamp01(airThrottle + _inputV * throttleChangeRate * dt);

            // why: pitch now comes from mouse Y (inverted, same convention as CombatCamera)
            float pitchInput = -Input.GetAxis("Mouse Y") * mousePitchSensitivity;
            pitchInput = Mathf.Clamp(pitchInput, -1f, 1f);

            // why: below stallSpeed authority collapses; above 0.8*MaxAirSpeed a small overshoot makes high-speed feel punchy
            float speedFrac = Mathf.Clamp01(speed / cruiseSpeed);
            float pitchAuthority = Mathf.Lerp(stallPitchAuthority, 1f, speedFrac);
            if (speed > MaxAirSpeed * 0.8f) pitchAuthority *= highSpeedPitchBoost;

            IsStalling = speed < stallSpeed;
            float yawAuthority = IsStalling ? stallPitchAuthority : 1f;

            float yawInput = _inputH;

            // Coordinated turn: banked yaw adds pitch so the plane carves instead of skidding
            float currentRollRad = transform.eulerAngles.z;
            if (currentRollRad > 180f) currentRollRad -= 360f;
            currentRollRad *= Mathf.Deg2Rad;
            float coordinatedPitch = -Mathf.Sin(currentRollRad) * Mathf.Abs(yawInput) * coordinatedTurnStrength;

            float effectivePitch = (pitchInput + coordinatedPitch) * pitchAuthority;
            if (Mathf.Abs(effectivePitch) > 0.01f)
            {
                Quaternion pitchRot = Quaternion.AngleAxis(-effectivePitch * AirPitchSpeed * dt, transform.right);
                _rb.MoveRotation(pitchRot * _rb.rotation);
            }

            if (Mathf.Abs(yawInput) > 0.01f)
            {
                Quaternion yawRot = Quaternion.AngleAxis(yawInput * AirYawSpeed * yawAuthority * dt, Vector3.up);
                _rb.MoveRotation(yawRot * _rb.rotation);
            }

            // Manual Q/E roll — takes priority over auto-roll when pressed
            float manualRoll = 0f;
            if (Input.GetKey(KeyCode.Q)) manualRoll = 1f;
            if (Input.GetKey(KeyCode.E)) manualRoll = -1f;

            if (Mathf.Abs(manualRoll) > 0.01f)
            {
                Quaternion rollRot = Quaternion.AngleAxis(manualRoll * AirRollSpeed * dt, transform.forward);
                _rb.MoveRotation(rollRot * _rb.rotation);
            }
            else
            {
                // Banked turn: auto-roll toward target bank angle proportional to yaw input
                float targetBank = -yawInput * autoRollMaxAngle;
                float currentBank = transform.eulerAngles.z;
                if (currentBank > 180f) currentBank -= 360f;
                float bankError = Mathf.DeltaAngle(currentBank, targetBank);
                float returnSpeed = Mathf.Abs(yawInput) > 0.01f ? autoRollStrength : autoRollReturnSpeed;
                float rollDelta = bankError * returnSpeed * dt;
                Quaternion rollRot = Quaternion.AngleAxis(rollDelta, transform.forward);
                _rb.MoveRotation(rollRot * _rb.rotation);
            }

            // Throttle-driven thrust; boost stacks on top
            float forceMultiplier = isBoosting ? boostMultiplier : 1f;
            Vector3 thrustForce = transform.forward * thrust * AirThrustForce * airThrottle * forceMultiplier * dt;
            _rb.AddForce(thrustForce, ForceMode.Force);

            // why: lift curve — below stall drops off fast (quadratic), above stall ramps to full support at cruiseSpeed
            float liftFactor;
            if (speed < stallSpeed)
            {
                float t = speed / stallSpeed;
                liftFactor = t * t * 0.25f;
            }
            else
            {
                liftFactor = Mathf.Clamp01((speed - stallSpeed) / Mathf.Max(cruiseSpeed - stallSpeed, 0.01f));
                liftFactor = 0.25f + liftFactor * 0.75f;
            }
            _rb.AddForce(Vector3.up * liftFactor * Physics.gravity.magnitude * _rb.mass, ForceMode.Force);
        }

        // ------------------------------------------------------------------
        // Space movement: 6DOF
        // ------------------------------------------------------------------

        private void FixedUpdateSpace()
        {
            float thrust = GetEffectiveThrust();
            float dt = Time.fixedDeltaTime;

            // Translation: WASD + QE for up/down
            Vector3 input = Vector3.zero;
            input.z = _inputV;
            input.x = _inputH;
            if (Input.GetKey(KeyCode.Q)) input.y = -1f;
            if (Input.GetKey(KeyCode.E)) input.y = 1f;

            if (input.sqrMagnitude > 0.01f)
            {
                input.Normalize();
                float forceMultiplier = isBoosting ? boostMultiplier : 1f;
                Vector3 worldForce = transform.TransformDirection(input) * thrust * AirThrustForce * forceMultiplier * dt;
                _rb.AddForce(worldForce, ForceMode.Force);
            }

            // Rotation from mouse
            float yawAmount = mouseDelta.x * AirYawSpeed * dt;
            float pitchAmount = -mouseDelta.y * AirPitchSpeed * dt;
            Quaternion rot = Quaternion.Euler(pitchAmount, yawAmount, 0f);
            _rb.MoveRotation(_rb.rotation * rot);
        }

        // ------------------------------------------------------------------
        // Weapons
        // ------------------------------------------------------------------

        public void FireWeapons()
        {
            if (!isAlive) return;
            if (_weapons.Count == 0) return;

            // Fire the active weapon if it has ammo and cooldown is ready
            WeaponSlot slot = _weapons[activeWeaponIndex];
            if (!slot.CanFire()) return;
            if (slot.partNode == null || !slot.partNode.IsFunctional()) return;

            slot.Fire();
            SpawnProjectile(slot);
            OnWeaponFired?.Invoke();
        }

        public void FireAllWeapons()
        {
            if (!isAlive) return;

            for (int i = 0; i < _weapons.Count; i++)
            {
                WeaponSlot slot = _weapons[i];
                if (!slot.CanFire()) continue;
                if (slot.partNode == null || !slot.partNode.IsFunctional()) continue;

                slot.Fire();
                SpawnProjectile(slot);
            }

            if (_weapons.Count > 0)
                OnWeaponFired?.Invoke();
        }

        private void SpawnProjectile(WeaponSlot slot)
        {
            PartData wpnData = slot.partNode.partData;
            string projectileType = wpnData.GetStat<string>("projectile_type", "ballistic");
            float damage = wpnData.GetStat<float>("damage", 10f);
            float range = wpnData.GetStat<float>("range", 80f);

            Vector3 origin = slot.partNode.transform.position;
            Vector3 direction = aimDirection.sqrMagnitude > 0.01f ? aimDirection.normalized : transform.forward;

            switch (projectileType)
            {
                case "hitscan":
                    FireHitscan(origin, direction, damage, range, slot);
                    break;

                case "ballistic":
                    FireBallistic(origin, direction, damage, range, slot);
                    break;

                case "guided":
                    FireGuided(origin, direction, damage, range, slot);
                    break;

                case "laser":
                    FireLaser(origin, direction, damage, range, slot);
                    break;

                case "railgun":
                    FireRailgun(origin, direction, damage, range, slot);
                    break;

                case "mine":
                    FireMine(origin, damage, slot);
                    break;

                default:
                    FireBallistic(origin, direction, damage, range, slot);
                    break;
            }
        }

        private void FireHitscan(Vector3 origin, Vector3 dir, float damage, float range, WeaponSlot slot)
        {
            if (Physics.Raycast(origin, dir, out RaycastHit hit, range))
            {
                Vehicle target = hit.collider.GetComponentInParent<Vehicle>();
                if (target != null && target != this)
                {
                    target.TakePositionalDamage(hit.point, Mathf.RoundToInt(damage));
                    combatStats.damageDealt += Mathf.RoundToInt(damage);
                    combatStats.shotsFired++;
                    combatStats.shotsHit++;
                    return;
                }
            }
            combatStats.shotsFired++;
        }

        private void FireBallistic(Vector3 origin, Vector3 dir, float damage, float range, WeaponSlot slot)
        {
            // Spawn a simple projectile sphere
            var projGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projGo.name = "Projectile_Ballistic";
            projGo.transform.position = origin + dir * 0.5f;
            projGo.transform.localScale = Vector3.one * 0.15f;

            var projRb = projGo.AddComponent<Rigidbody>();
            projRb.mass = 0.5f;
            projRb.useGravity = !IsSpaceDomain();
            projRb.linearVelocity = dir * 80f;

            var proj = projGo.AddComponent<Projectile>();
            proj.damage = Mathf.RoundToInt(damage);
            proj.range = range;
            proj.owner = this;
            proj.origin = origin;

            combatStats.shotsFired++;

            Destroy(projGo, range / 80f + 2f);
        }

        private void FireGuided(Vector3 origin, Vector3 dir, float damage, float range, WeaponSlot slot)
        {
            var projGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            projGo.name = "Projectile_Guided";
            projGo.transform.position = origin + dir * 0.5f;
            projGo.transform.localScale = new Vector3(0.1f, 0.2f, 0.1f);
            projGo.transform.rotation = Quaternion.LookRotation(dir);

            var projRb = projGo.AddComponent<Rigidbody>();
            projRb.mass = 1f;
            projRb.useGravity = false;
            projRb.linearVelocity = dir * 40f;

            var proj = projGo.AddComponent<Projectile>();
            proj.damage = Mathf.RoundToInt(damage);
            proj.range = range;
            proj.owner = this;
            proj.origin = origin;
            proj.isGuided = true;
            proj.targetDirection = dir;

            combatStats.shotsFired++;

            Destroy(projGo, range / 40f + 3f);
        }

        private void FireLaser(Vector3 origin, Vector3 dir, float damage, float range, WeaponSlot slot)
        {
            // Laser is a fast hitscan with visual trail
            if (Physics.Raycast(origin, dir, out RaycastHit hit, range))
            {
                Vehicle target = hit.collider.GetComponentInParent<Vehicle>();
                if (target != null && target != this)
                {
                    target.TakePositionalDamage(hit.point, Mathf.RoundToInt(damage));
                    combatStats.damageDealt += Mathf.RoundToInt(damage);
                    combatStats.shotsHit++;
                }
            }
            combatStats.shotsFired++;
        }

        private static readonly RaycastHit[] _railgunHits = new RaycastHit[32];
        private static readonly Comparison<RaycastHit> _railgunHitComparer =
            (a, b) => a.distance.CompareTo(b.distance);

        private void FireRailgun(Vector3 origin, Vector3 dir, float damage, float range, WeaponSlot slot)
        {
            // Railgun: penetrating hitscan — damages all vehicles along the ray
            int hitCount = Physics.RaycastNonAlloc(origin, dir, _railgunHits, range);
            Array.Sort(_railgunHits, 0, hitCount, Comparer<RaycastHit>.Create(_railgunHitComparer));

            float remainingDamage = damage;
            for (int i = 0; i < hitCount && remainingDamage > 0f; i++)
            {
                Vehicle target = _railgunHits[i].collider.GetComponentInParent<Vehicle>();
                if (target != null && target != this)
                {
                    int dmg = Mathf.RoundToInt(remainingDamage);
                    target.TakePositionalDamage(_railgunHits[i].point, dmg);
                    combatStats.damageDealt += dmg;
                    combatStats.shotsHit++;
                    remainingDamage *= 0.6f; // penetration falloff
                }
            }
            combatStats.shotsFired++;
        }

        private void FireMine(Vector3 origin, float damage, WeaponSlot slot)
        {
            var mineGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mineGo.name = "Mine";
            mineGo.transform.position = origin - transform.forward * 1.5f;
            mineGo.transform.localScale = Vector3.one * 0.3f;

            var mineRb = mineGo.AddComponent<Rigidbody>();
            mineRb.mass = 2f;
            mineRb.useGravity = !IsSpaceDomain();
            mineRb.linearVelocity = -transform.forward * 3f;
            mineRb.linearDamping = 3f;

            var proj = mineGo.AddComponent<Projectile>();
            proj.damage = Mathf.RoundToInt(damage);
            proj.range = 5f;
            proj.owner = this;
            proj.origin = origin;
            proj.isMine = true;

            combatStats.shotsFired++;

            // Mines last 30 seconds
            Destroy(mineGo, 30f);
        }

        // ------------------------------------------------------------------
        // Damage
        // ------------------------------------------------------------------

        /// <summary>
        /// Apply damage to the part nearest the world-space impact point.
        /// </summary>
        public void TakePositionalDamage(Vector3 worldPoint, int damage)
        {
            if (!isAlive) return;

            PartNode closest = FindNearestFunctionalPart(worldPoint);
            if (closest == null)
            {
                // No functional parts — vehicle is dead
                Die();
                return;
            }

            bool destroyed = closest.TakeDamage(damage);
            combatStats.damageTaken += damage;

            if (destroyed)
            {
                // Check if the control module was destroyed
                if (closest == _controlModule)
                {
                    Die();
                    return;
                }

                // Check if part explodes
                bool explodes = closest.partData.GetStat<bool>("explodes_on_destroy", false);
                if (explodes)
                {
                    ApplyExplosionDamage(closest.transform.position, damage / 2, 3f);
                }

                RecalculateStats();
                CheckAlive();
            }
        }

        /// <summary>
        /// Environmental damage (e.g., lava, out of bounds) — damages all parts.
        /// </summary>
        public void TakeEnvironmentalDamage(int damagePerPart)
        {
            if (!isAlive) return;

            var nodes = new List<PartNode>(_parts.Values);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null && nodes[i].IsFunctional())
                {
                    nodes[i].TakeDamage(damagePerPart);
                }
            }

            RecalculateStats();
            CheckAlive();
        }

        private void ApplyExplosionDamage(Vector3 center, int damage, float radius)
        {
            foreach (var kv in _parts)
            {
                PartNode node = kv.Value;
                if (node == null || !node.IsFunctional()) continue;

                float dist = Vector3.Distance(node.transform.position, center);
                if (dist < radius)
                {
                    float falloff = 1f - (dist / radius);
                    int scaledDmg = Mathf.RoundToInt(damage * falloff);
                    if (scaledDmg > 0)
                        node.TakeDamage(scaledDmg);
                }
            }
        }

        private PartNode FindNearestFunctionalPart(Vector3 worldPoint)
        {
            PartNode closest = null;
            float closestDist = float.MaxValue;

            foreach (var kv in _parts)
            {
                PartNode node = kv.Value;
                if (node == null || !node.IsFunctional()) continue;

                float dist = Vector3.Distance(node.transform.position, worldPoint);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = node;
                }
            }

            return closest;
        }

        private void CheckAlive()
        {
            if (!isAlive) return;

            // Dead if control module is destroyed
            if (_controlModule != null && !_controlModule.IsFunctional())
            {
                Die();
                return;
            }

            // Dead if no functional parts remain
            bool anyFunctional = false;
            foreach (var kv in _parts)
            {
                if (kv.Value != null && kv.Value.IsFunctional())
                {
                    anyFunctional = true;
                    break;
                }
            }

            if (!anyFunctional)
                Die();
        }

        // ------------------------------------------------------------------
        // Death
        // ------------------------------------------------------------------

        /// <summary>
        /// Kill the vehicle with cinematic part ejection.
        /// </summary>
        public void Die()
        {
            if (!isAlive) return;
            isAlive = false;

            Debug.Log($"[Vehicle] '{vehicleName}' destroyed!");

            // Cinematic part ejection: detach all parts and fling them outward
            StartCoroutine(CinematicDeath());

            OnVehicleDied?.Invoke(this);
        }

        private IEnumerator CinematicDeath()
        {
            // Disable main rigidbody
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.detectCollisions = false;
            }

            // Detach each part with physics
            var nodes = new List<PartNode>(_parts.Values);
            for (int i = 0; i < nodes.Count; i++)
            {
                PartNode node = nodes[i];
                if (node == null || node.gameObject == null) continue;

                // Re-enable destroyed renderers so debris is visible
                var renderers = node.GetComponentsInChildren<MeshRenderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                    renderers[r].enabled = true;

                // Detach from vehicle hierarchy
                node.transform.SetParent(null, true);

                // Add rigidbody for physics
                var partRb = node.gameObject.AddComponent<Rigidbody>();
                partRb.mass = Mathf.Max(node.partData.massKg * 0.1f, 0.5f);

                // Explosion force — outward + upward
                Vector3 direction = (node.transform.position - transform.position).normalized;
                if (direction.sqrMagnitude < 0.01f)
                    direction = UnityEngine.Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) + 0.5f;

                float force = UnityEngine.Random.Range(5f, 15f);
                partRb.AddForce(direction * force, ForceMode.Impulse);
                partRb.AddTorque(UnityEngine.Random.insideUnitSphere * 10f, ForceMode.Impulse);

                // Destroy debris after a delay
                Destroy(node.gameObject, UnityEngine.Random.Range(3f, 6f));
            }

            _parts.Clear();
            _weapons.Clear();

            yield return new WaitForSeconds(0.5f);

            // Destroy the vehicle root after debris settles
            Destroy(gameObject, 5f);
        }

        // ------------------------------------------------------------------
        // Part event handlers
        // ------------------------------------------------------------------

        private void HandlePartDamaged(PartNode node, int damage)
        {
            if (!isAlive) return;
            OnPartDamaged?.Invoke(node, damage);
        }

        private void HandlePartDestroyed(PartNode node)
        {
            if (!isAlive) return;
            OnPartDestroyed?.Invoke(node);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private float GetEffectiveThrust()
        {
            if (totalMass <= 0f) return 0f;
            return totalThrust / totalMass;
        }

        private Vector3 GridToLocal(Vector3Int gridPos)
        {
            return new Vector3(
                gridPos.x * CellSize,
                gridPos.y * CellSize,
                gridPos.z * CellSize
            );
        }

        private DomainType GetDomainType()
        {
            if (string.Equals(domain, "ground", StringComparison.OrdinalIgnoreCase)) return DomainType.Ground;
            if (string.Equals(domain, "water", StringComparison.OrdinalIgnoreCase)) return DomainType.Water;
            if (string.Equals(domain, "air", StringComparison.OrdinalIgnoreCase)) return DomainType.Air;
            return DomainType.Ground;
        }

        private bool IsWaterDomain()
        {
            var dt = GetDomainType();
            return dt == DomainType.Water;
        }

        private bool IsSpaceDomain()
        {
            return false;
        }

        // ==================================================================
        // Public queries
        // ==================================================================

        public int GetFunctionalPartCount()
        {
            int count = 0;
            foreach (var kv in _parts)
            {
                if (kv.Value != null && kv.Value.IsFunctional())
                    count++;
            }
            return count;
        }

        public int GetFunctionalWeaponCount()
        {
            int count = 0;
            for (int i = 0; i < _weapons.Count; i++)
            {
                if (_weapons[i].partNode != null && _weapons[i].partNode.IsFunctional())
                    count++;
            }
            return count;
        }

        public float GetHealthPercent()
        {
            if (maxHp <= 0) return 0f;
            return (float)totalHp / maxHp;
        }

        public float GetBoostPercent()
        {
            if (boostMaxFuel <= 0f) return 0f;
            return boostFuel / boostMaxFuel;
        }

        // ==================================================================
        // Nested types
        // ==================================================================

        private enum DomainType
        {
            Ground,
            Water,
            Air
        }
    }

    // ======================================================================
    // WeaponSlot — tracks per-weapon cooldown and ammo
    // ======================================================================

    [Serializable]
    public class WeaponSlot
    {
        public PartNode partNode;
        public int currentAmmo;
        public float lastFireTime;
        public float cooldownSeconds;

        public WeaponSlot(PartNode node)
        {
            partNode = node;
            currentAmmo = node.partData.GetStat<int>("ammo", 100);
            cooldownSeconds = 1f / Mathf.Max(node.partData.GetStat<float>("fire_rate", 1f), 0.01f);
            lastFireTime = -999f;
        }

        public bool CanFire()
        {
            if (currentAmmo <= 0) return false;
            if (Time.time - lastFireTime < cooldownSeconds) return false;
            return true;
        }

        public void Fire()
        {
            currentAmmo--;
            lastFireTime = Time.time;
        }
    }

    // ======================================================================
    // CombatStats — per-vehicle combat tracking
    // ======================================================================

    [Serializable]
    public class CombatStats
    {
        public int damageDealt;
        public int damageTaken;
        public int kills;
        public int deaths;
        public int shotsFired;
        public int shotsHit;
        public int partsDestroyed;

        public float Accuracy => shotsFired > 0 ? (float)shotsHit / shotsFired : 0f;
    }

    // ======================================================================
    // Projectile — simple projectile MonoBehaviour for ballistic/guided/mine
    // ======================================================================

    public class Projectile : MonoBehaviour
    {
        public int damage;
        public float range;
        public Vehicle owner;
        public Vector3 origin;
        public bool isGuided;
        public bool isMine;
        public Vector3 targetDirection;

        private Rigidbody _rb;
        private float _spawnTime;

        private static readonly Collider[] _mineOverlapBuffer = new Collider[32];

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _spawnTime = Time.time;
        }

        private void FixedUpdate()
        {
            // Self-destruct if beyond range
            float dist = Vector3.Distance(transform.position, origin);
            if (!isMine && dist > range)
            {
                Destroy(gameObject);
                return;
            }

            // Guided missiles track forward
            if (isGuided && _rb != null)
            {
                Vector3 desired = targetDirection.normalized * 40f;
                _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, desired, Time.fixedDeltaTime * 3f);
                if (_rb.linearVelocity.sqrMagnitude > 0.1f)
                    transform.rotation = Quaternion.LookRotation(_rb.linearVelocity);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            Vehicle target = collision.collider.GetComponentInParent<Vehicle>();
            if (target != null && target != owner)
            {
                target.TakePositionalDamage(collision.GetContact(0).point, damage);
                if (owner != null)
                {
                    owner.combatStats.damageDealt += damage;
                    owner.combatStats.shotsHit++;
                }
            }

            // Mine proximity detonation on contact
            if (isMine)
            {
                // Area damage
                int count = Physics.OverlapSphereNonAlloc(transform.position, 3f, _mineOverlapBuffer);
                for (int i = 0; i < count; i++)
                {
                    Vehicle v = _mineOverlapBuffer[i].GetComponentInParent<Vehicle>();
                    if (v != null && v != owner)
                    {
                        float dist = Vector3.Distance(transform.position, _mineOverlapBuffer[i].transform.position);
                        float falloff = 1f - Mathf.Clamp01(dist / 3f);
                        int scaledDmg = Mathf.RoundToInt(damage * falloff);
                        if (scaledDmg > 0)
                            v.TakePositionalDamage(_mineOverlapBuffer[i].transform.position, scaledDmg);
                    }
                }
            }

            Destroy(gameObject);
        }
    }
}
