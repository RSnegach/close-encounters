using System;
using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Core;
using CloseEncounters.Arena;

namespace CloseEncounters.AI
{
    // =========================================================================
    //  AIInput — the output produced each frame by AIController.
    //  Whatever drives the vehicle reads these values instead of player input.
    // =========================================================================
    [Serializable]
    public struct AIInput
    {
        /// <summary>Forward/backward throttle. -1 = full reverse, +1 = full forward.</summary>
        public float forward;
        /// <summary>Left/right strafe. -1 = left, +1 = right.</summary>
        public float strafe;
        /// <summary>Yaw rotation request. -1 = left, +1 = right.</summary>
        public float yaw;
        /// <summary>True when the AI wants to fire.</summary>
        public bool fire;
        /// <summary>True when the AI requests a boost.</summary>
        public bool boost;
        /// <summary>Index of the weapon group the AI wants active.</summary>
        public int weaponIndex;

        public static AIInput Zero => new AIInput
        {
            forward = 0f, strafe = 0f, yaw = 0f,
            fire = false, boost = false, weaponIndex = 0
        };
    }

    // =========================================================================
    //  Difficulty preset — tunable knobs that shape AI behaviour.
    // =========================================================================
    public enum AIDifficultyLevel { Easy, Medium, Hard }

    [Serializable]
    public class AIDifficultyPreset
    {
        public float reactionTime;
        public float accuracy;
        public float aggression;
        public float awarenessRadius;
        public float engageRange;
        public float retreatHpFraction;
        public float leadPredictionFactor;
        public float obstacleAvoidWeight;
        public float flankProbability;
        public float stuckRecoverTime;
        public float weaponCyclePeriod;
        public float boostUseProbability;

        public static AIDifficultyPreset Easy => new AIDifficultyPreset
        {
            reactionTime        = 0.7f,
            accuracy            = 0.45f,
            aggression          = 0.3f,
            awarenessRadius     = 80f,
            engageRange         = 50f,
            retreatHpFraction   = 0.35f,
            leadPredictionFactor= 0.3f,
            obstacleAvoidWeight = 1.2f,
            flankProbability    = 0.10f,
            stuckRecoverTime    = 2.5f,
            weaponCyclePeriod   = 8f,
            boostUseProbability = 0.05f,
        };

        public static AIDifficultyPreset Medium => new AIDifficultyPreset
        {
            reactionTime        = 0.40f,
            accuracy            = 0.65f,
            aggression          = 0.55f,
            awarenessRadius     = 120f,
            engageRange         = 70f,
            retreatHpFraction   = 0.25f,
            leadPredictionFactor= 0.6f,
            obstacleAvoidWeight = 1.0f,
            flankProbability    = 0.30f,
            stuckRecoverTime    = 1.8f,
            weaponCyclePeriod   = 5f,
            boostUseProbability = 0.20f,
        };

        public static AIDifficultyPreset Hard => new AIDifficultyPreset
        {
            reactionTime        = 0.15f,
            accuracy            = 0.88f,
            aggression          = 0.80f,
            awarenessRadius     = 180f,
            engageRange         = 100f,
            retreatHpFraction   = 0.15f,
            leadPredictionFactor= 0.90f,
            obstacleAvoidWeight = 0.8f,
            flankProbability    = 0.55f,
            stuckRecoverTime    = 1.0f,
            weaponCyclePeriod   = 3f,
            boostUseProbability = 0.40f,
        };

        public static AIDifficultyPreset ForLevel(AIDifficultyLevel level)
        {
            switch (level)
            {
                case AIDifficultyLevel.Easy:   return Easy;
                case AIDifficultyLevel.Medium: return Medium;
                case AIDifficultyLevel.Hard:   return Hard;
                default:                       return Medium;
            }
        }

        /// <summary>
        /// Apply random personality variance to each tunable within +-pct (0-1).
        /// </summary>
        public void ApplyVariance(float pct)
        {
            reactionTime         *= Variance(pct);
            accuracy             *= Variance(pct);
            aggression           *= Variance(pct);
            awarenessRadius      *= Variance(pct);
            engageRange          *= Variance(pct);
            retreatHpFraction    *= Variance(pct);
            leadPredictionFactor *= Variance(pct);
            obstacleAvoidWeight  *= Variance(pct);
            flankProbability     *= Variance(pct);
            stuckRecoverTime     *= Variance(pct);
            weaponCyclePeriod    *= Variance(pct);
            boostUseProbability  *= Variance(pct);

            // Clamp probability fields
            accuracy            = Mathf.Clamp01(accuracy);
            aggression          = Mathf.Clamp01(aggression);
            flankProbability    = Mathf.Clamp01(flankProbability);
            boostUseProbability = Mathf.Clamp01(boostUseProbability);
            retreatHpFraction   = Mathf.Clamp01(retreatHpFraction);
        }

        private static float Variance(float pct)
        {
            return 1f + UnityEngine.Random.Range(-pct, pct);
        }
    }

    // =========================================================================
    //  FSM states
    // =========================================================================
    public enum AIState
    {
        Idle,
        Seek,
        Flank,
        Engage,
        Evade,
        Retreat,
        StuckRecover,
    }

    // =========================================================================
    //  Hazard zone — axis-aligned bounding box the AI should avoid.
    //  These are registered externally (e.g. by the ArenaManager).
    // =========================================================================
    [Serializable]
    public struct HazardZone
    {
        public Vector3 center;
        public Vector3 halfExtents;

        public bool Contains(Vector3 point)
        {
            return Mathf.Abs(point.x - center.x) <= halfExtents.x
                && Mathf.Abs(point.y - center.y) <= halfExtents.y
                && Mathf.Abs(point.z - center.z) <= halfExtents.z;
        }

        public Vector3 ClosestPointOnSurface(Vector3 point)
        {
            Vector3 clamped;
            clamped.x = Mathf.Clamp(point.x, center.x - halfExtents.x, center.x + halfExtents.x);
            clamped.y = Mathf.Clamp(point.y, center.y - halfExtents.y, center.y + halfExtents.y);
            clamped.z = Mathf.Clamp(point.z, center.z - halfExtents.z, center.z + halfExtents.z);
            return clamped;
        }
    }

    // =========================================================================
    //  Cached target info for scoring
    // =========================================================================
    internal struct TargetCandidate
    {
        public Transform transform;
        public float distance;
        public float hpFraction;
        public float threatScore;
        public float persistenceBonus;
        public float totalScore;
        public bool hasLOS;
    }

    // =========================================================================
    //  AIController — the brain that goes on every AI-driven vehicle.
    // =========================================================================
    [DisallowMultipleComponent]
    public class AIController : MonoBehaviour
    {
        // ----- public configuration -----
        [Header("Difficulty")]
        public AIDifficultyLevel difficultyLevel = AIDifficultyLevel.Medium;
        [Range(0f, 1f)]
        public float personalityVariance = 0.15f;

        [Header("Arena")]
        public Vector3 arenaCentre  = Vector3.zero;
        public Vector3 arenaHalfSize = new Vector3(200f, 100f, 200f);
        public float arenaBoundaryMargin = 20f;

        [Header("Obstacle Avoidance")]
        public float rayLength = 18f;
        public LayerMask obstacleMask = ~0;

        [Header("Vehicle Interface")]
        [Tooltip("Tag applied to all potential enemy vehicles.")]
        public string enemyTag = "Vehicle";

        // ----- public readable state -----
        public AIInput CurrentInput  { get; private set; }
        public AIState CurrentState  { get; private set; } = AIState.Idle;
        public Transform CurrentTarget { get; private set; }

        // ----- internal preset (built at Start) -----
        private AIDifficultyPreset _preset;

        // ----- target tracking -----
        private readonly List<TargetCandidate> _candidates = new List<TargetCandidate>(16);
        private Transform _lastTarget;
        private float _targetPersistenceTimer;
        private const float TargetPersistenceBonus = 12f;
        private const float TargetSwitchCooldown   = 1.5f;
        private float _targetSwitchTimer;

        // ----- obstacle avoidance rays -----
        private readonly Vector3[] _rayDirs = new Vector3[5];
        private readonly float[] _rayHits  = new float[5];
        private const int RayCount = 5;
        // Fan angles (degrees from forward): 0, +-30, +-60
        private static readonly float[] RayAngles = { 0f, -30f, 30f, -60f, 60f };

        // ----- hazard zones (cached list, set externally) -----
        private static readonly List<HazardZone> _hazardZones = new List<HazardZone>(8);
        public static void RegisterHazardZone(HazardZone zone) { _hazardZones.Add(zone); }
        public static void ClearHazardZones() { _hazardZones.Clear(); }
        public static IReadOnlyList<HazardZone> HazardZones => _hazardZones;

        // ----- stuck detection -----
        private Vector3 _lastStuckCheckPos;
        private float _stuckTimer;
        private float _stuckRecoverTimer;
        private const float StuckCheckInterval = 0.5f;
        private const float StuckDistanceThreshold = 0.6f;
        private float _stuckCheckAccum;

        // ----- weapon cycling -----
        private int _currentWeaponIndex;
        private float _weaponCycleTimer;
        private int _weaponCount = 1;

        // ----- reaction delay -----
        private float _reactionAccum;

        // ----- smoothed outputs -----
        private float _smoothForward;
        private float _smoothStrafe;
        private float _smoothYaw;
        private const float InputSmoothSpeed = 9f;

        // ----- flank state -----
        private float _flankSide; // -1 or +1
        private float _flankTimer;
        private const float FlankDuration = 3f;

        // ----- evade state -----
        private float _evadeTimer;
        private Vector3 _evadeDirection;
        private const float EvadeDuration = 1.5f;

        // ----- retreat state -----
        private float _retreatTimer;
        private const float RetreatDuration = 4f;

        // ----- misc -----
        private float _hp    = 1f; // 0..1, set externally
        private float _maxHp = 1f;
        private Rigidbody _rb;
        private bool _initialized;
        private bool _isAirDomain;
        private CloseEncounters.VehiclePhysics.WaterPhysics _cachedWaterPhysics;
        private CloseEncounters.VehiclePhysics.GroundPhysics _cachedGroundPhysics;

        // ----- propulsion degradation -----
        private int _initialPropulsionCount;
        private int _currentPropulsionCount;
        private float _baseMoveSpeed = 22f;
        private float _currentMoveSpeed = 22f;

        // ----- fuel/boost degradation -----
        private int _initialFuelCount;
        private int _currentFuelCount;
        private float _baseMaxBoostFuel;
        private float _maxBoostFuel;
        private float _boostFuel;
        private bool _boostLocked;

        // ----- decoupled decision/motion (decisions tick at reaction rate,
        //        smoothing/aim run every frame so bots aren't jerky) -----
        private AIInput _decided = AIInput.Zero;

        // ----- weapon-aware engagement (set by AICombat) -----
        private float _weaponRange;        // max effective range of equipped weapons
        private bool _hasAimedWeapon;       // has a turret/aimed weapon that can bear off-hull

        // ----- idle patrol (ground/water) -----
        private float _patrolTimer;
        private Vector3 _patrolPoint;

        // ----- anti-dogpile focus tracker (shared across all bots) -----
        private static readonly Dictionary<EntityId, int> _focusCounts = new Dictionary<EntityId, int>();
        private EntityId _focusedTargetId;

        // ----- perception / awareness (set by AICombat / damage events) -----
        private int _playerId = -1;                 // own FFA id, to ignore our own shots

        // Target memory: where we last SAW the current target, so we search the last
        // known spot when LOS breaks instead of psychically tracking through walls.
        private Vector3 _lastKnownTargetPos;
        private bool _hasLastKnown;

        // Retaliation: remember whoever last damaged us and prioritise them briefly,
        // so a bot whips around on an off-angle attacker like a real player would.
        private EntityId _recentAttackerId;
        private float _recentAttackerTimer;
        private const float RetaliationMemory = 4f;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _cachedWaterPhysics = GetComponent<CloseEncounters.VehiclePhysics.WaterPhysics>();
            _cachedGroundPhysics = GetComponent<CloseEncounters.VehiclePhysics.GroundPhysics>();

            _preset = AIDifficultyPreset.ForLevel(difficultyLevel);
            _preset.ApplyVariance(personalityVariance);

            // AICombat set the weapon range before Start ran (it rebuilds _preset),
            // so re-apply the weapon-range-derived engage distance here.
            if (_weaponRange > 0f) SetWeaponRange(_weaponRange);

            _lastStuckCheckPos = transform.position;
            _patrolPoint = transform.position;
            _flankSide = UnityEngine.Random.value > 0.5f ? 1f : -1f;

            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;

            float dt = Time.deltaTime;

            // These run EVERY frame so bots are responsive (the old code gated the
            // whole brain behind reactionTime, which made movement/aim jerky/laggy).
            UpdateStuckDetection(dt);   // self-gates internally
            UpdateWeaponCycling(dt);

            // Heavy DECISIONS (target pick + state machine) run at reaction cadence —
            // that's the deliberate skill latency. Between decisions we still keep the
            // target valid (drop corpses immediately) so bots don't shoot wreckage.
            _reactionAccum += dt;
            if (_reactionAccum >= _preset.reactionTime)
            {
                _reactionAccum = 0f;
                UpdateTargetSelection();
                UpdateStateMachine(dt);
                _decided = CurrentInput; // capture the raw decision to smooth toward
            }
            else
            {
                ValidateTarget();
            }

            // Motion smoothing, aim and boost-fuel tick run EVERY frame for fluidity.
            if (_recentAttackerTimer > 0f) _recentAttackerTimer -= dt;
            TickBoost(dt);
            ProduceSmoothedInput(dt);
        }

        /// <summary>Drop a target that died / despawned so the bot re-acquires fast
        /// instead of pursuing and shooting a wreck until the next decision tick.</summary>
        private void ValidateTarget()
        {
            if (CurrentTarget == null) return;
            var vr = CurrentTarget.GetComponent<CloseEncounters.Arena.VehicleRuntime>();
            if (vr == null || !vr.IsAlive || !CurrentTarget.gameObject.activeInHierarchy)
            {
                ReleaseFocus();
                CurrentTarget = null;
                _lastTarget = null;
                if (CurrentState == AIState.Engage || CurrentState == AIState.Flank)
                    TransitionTo(AIState.Seek);
            }
        }

        /// <summary>Boost fuel accounting (parity with the player): burn while
        /// boosting, regen + 25% unlock when not. Prevents infinite-boost bots.</summary>
        private void TickBoost(float dt)
        {
            bool wantBoost = _decided.boost;
            if (wantBoost && CanBoost())
            {
                _boostFuel -= dt;
                if (_boostFuel <= 0f) { _boostFuel = 0f; _boostLocked = true; }
            }
            else
            {
                if (_boostFuel < _maxBoostFuel)
                    _boostFuel = Mathf.Min(_boostFuel + dt * 0.6f, _maxBoostFuel);
                if (_boostLocked && _maxBoostFuel > 0f && _boostFuel >= _maxBoostFuel * 0.25f)
                    _boostLocked = false;
            }
        }

        private bool CanBoost()
        {
            return _maxBoostFuel > 0f && _boostFuel > 0f && !_boostLocked;
        }

        private void OnEnable()
        {
            CloseEncounters.Combat.DamageSystem.OnVehicleDamaged += OnDamaged;
        }

        private void OnDisable()
        {
            CloseEncounters.Combat.DamageSystem.OnVehicleDamaged -= OnDamaged;
            ReleaseFocus();
        }

        /// <summary>Damage hook: remember who just hit us so target selection can
        /// prioritise retaliation (scaled by aggression — passive bots shrug it off).</summary>
        private void OnDamaged(CloseEncounters.Arena.VehicleRuntime victim,
                               CloseEncounters.Arena.VehicleRuntime attacker, int amount)
        {
            if (victim == null || victim.gameObject != gameObject) return;
            if (attacker == null || attacker.gameObject == gameObject) return;
            _recentAttackerId = attacker.transform.GetEntityId();
            _recentAttackerTimer = RetaliationMemory;
        }

        /// <summary>
        /// For ground AI without GroundPhysics: apply forces directly,
        /// same way PlayerVehicleController does.
        /// </summary>
        private void FixedUpdate()
        {
            if (!_initialized || _rb == null) return;

            float dt = Time.fixedDeltaTime;

            // Air: kinematic flight (mirrors the player's air model — no air physics
            // component exists, so the brain flies the plane itself).
            if (_isAirDomain) { HandleAirFlight(dt); return; }

            // Skip if WaterPhysics or GroundPhysics handle movement
            if (_cachedWaterPhysics != null) return;
            if (_cachedGroundPhysics != null) return;

            AIInput inp = CurrentInput;

            // Turning — same as PlayerVehicleController
            if (Mathf.Abs(inp.yaw) > 0.01f)
            {
                float yawDelta = inp.yaw * 120f * dt;
                _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, yawDelta, 0f));
            }

            // Damp angular velocity
            Vector3 av = _rb.angularVelocity;
            av.x *= 0.9f;
            av.z *= 0.9f;
            av.y = 0f;
            _rb.angularVelocity = av;

            // Forward thrust — same as PlayerVehicleController
            float moveSpeed = _currentMoveSpeed;
            float speed = inp.boost ? moveSpeed * 2f : moveSpeed;

            if (Mathf.Abs(inp.forward) > 0.01f)
            {
                float thrust = inp.forward * speed * _rb.mass;
                if (inp.forward < 0f) thrust *= 0.5f;
                _rb.AddForce(transform.forward * thrust, ForceMode.Force);
            }

            // Soft speed cap
            Vector3 vel = _rb.linearVelocity;
            Vector3 hVel = new Vector3(vel.x, 0f, vel.z);
            if (hVel.magnitude > speed)
            {
                Vector3 clamped = hVel.normalized * speed;
                hVel = Vector3.Lerp(hVel, clamped, 6f * dt);
                _rb.linearVelocity = new Vector3(hVel.x, vel.y, hVel.z);
            }
        }

        // =====================================================================
        //  Air flight — kinematic pursuit for AI planes (no air physics exists).
        //  Drives the rigidbody like the player's air model: rotate the nose toward
        //  the pursuit direction at a bank-limited rate, fly forward at airspeed.
        // =====================================================================
        private void HandleAirFlight(float dt)
        {
            Vector3 pos = transform.position;

            // Desired heading: chase the lead point if we have a target, otherwise
            // patrol back toward the arena centre (and don't loiter at the edge).
            Vector3 desired;
            if (CurrentTarget != null)
            {
                desired = GetAimPoint() - pos;
            }
            else
            {
                Vector3 toCentre = arenaCentre - pos;
                Vector3 flat = new Vector3(toCentre.x, 0f, toCentre.z);
                desired = flat.magnitude > arenaHalfSize.x * 0.7f ? toCentre : transform.forward;
            }
            if (desired.sqrMagnitude < 0.01f) desired = transform.forward;
            desired.Normalize();

            // Keep planes from clumping/colliding: push away from nearby bots.
            desired += ComputeAirSeparation(pos) * 0.8f;

            // Juke out of the path of incoming fire (skill-scaled; no-op for low skill).
            desired += ComputeIncomingThreatEvasion(pos);

            // Always steer back toward the play area, even while chasing — otherwise a
            // bot follows a target out of bounds and the OOB timer kills it.
            desired += ComputeBoundaryAvoidance() * 1.5f;

            // Altitude floor: bias upward only when low AND descending (avoids the
            // up/down porpoising the constant bias used to cause). Soft cap at 0.5.
            const float minAlt = 55f;
            if (pos.y < minAlt && _rb.linearVelocity.y < 2f)
            {
                float t = Mathf.Clamp01((minAlt - pos.y) / minAlt) * 0.5f;
                desired = Vector3.Slerp(desired.normalized, Vector3.up, t);
            }
            if (desired.sqrMagnitude < 0.01f) desired = transform.forward;
            desired.Normalize();

            // Turn toward the desired heading — harder bots turn (and fight) tighter.
            float turnRate = 70f + 70f * _preset.aggression; // deg/sec
            Quaternion targetRot = Quaternion.LookRotation(desired, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnRate * dt);

            // Airspeed: cruise, faster on boost. Scaled by propulsion health.
            float speedScale = _baseMoveSpeed > 0f ? Mathf.Clamp(_currentMoveSpeed / _baseMoveSpeed, 0.35f, 1f) : 1f;
            float cruise = 48f, boostSpeed = 95f;
            float airspeed = (CurrentInput.boost ? boostSpeed : cruise) * speedScale;
            _rb.linearVelocity = transform.forward * airspeed;
        }

        /// <summary>3D separation steer away from nearby flying bots (boid-style),
        /// so air bots chasing the same target don't clump and collide.</summary>
        private Vector3 ComputeAirSeparation(Vector3 pos)
        {
            const float radius = 32f;
            Vector3 push = Vector3.zero;
            var all = CloseEncounters.Arena.VehicleRuntime.LiveInstances;
            if (all == null) return push;
            for (int i = 0; i < all.Count; i++)
            {
                var other = all[i];
                if (other == null || other.gameObject == gameObject || !other.IsAlive) continue;
                Vector3 delta = pos - other.transform.position;
                float d = delta.magnitude;
                if (d > 0.01f && d < radius)
                    push += delta / d * (1f - d / radius);
            }
            return push;
        }

        /// <summary>
        /// Perceive in-flight enemy projectiles and return a world-space steer to
        /// juke out of their path. Only shots on a near-collision course count, so
        /// bots jink only when actually shot at. Gated/scaled by skill (accuracy):
        /// low-skill bots don't dodge, high-skill bots dodge hard — a real
        /// difficulty differentiator no other system provides.
        /// </summary>
        private Vector3 ComputeIncomingThreatEvasion(Vector3 pos)
        {
            float skill = _preset.accuracy;
            if (skill < 0.25f) return Vector3.zero; // unskilled bots are oblivious

            var projs = CloseEncounters.Combat.Projectile.Active;
            if (projs == null || projs.Count == 0) return Vector3.zero;

            const float threatRange = 70f;   // only react to nearby shots
            const float missRadius  = 6f;    // "would hit me" perpendicular tolerance
            Vector3 evade = Vector3.zero;

            for (int i = 0; i < projs.Count; i++)
            {
                var p = projs[i];
                if (p == null) continue;
                if (p.ownerPlayerId < 0 || p.ownerPlayerId == _playerId) continue; // ours / neutral

                Vector3 pp = p.transform.position;
                Vector3 toMe = pos - pp;
                // Cheap squared-distance cull first (skip sqrt on far shots).
                float sqr = toMe.sqrMagnitude;
                if (sqr > threatRange * threatRange || sqr < 0.25f) continue;
                float dist = Mathf.Sqrt(sqr);

                Vector3 vel = p.Velocity;
                float vmag = vel.magnitude;
                if (vmag < 1f) continue;
                Vector3 vdir = vel / vmag;

                // Closest approach of the shot's line to us; skip shots already past
                // or not actually aimed near us.
                float along = Vector3.Dot(toMe, vdir);
                if (along < 0f) continue;
                Vector3 miss = pos - (pp + vdir * along);
                float missDist = miss.magnitude;
                if (missDist > missRadius) continue;

                // Steer perpendicular to the incoming line, away from the impact point.
                Vector3 dodge = miss.sqrMagnitude > 0.01f
                    ? miss.normalized
                    : Vector3.Cross(Vector3.up, vdir).normalized;
                float urgency = (1f - dist / threatRange) * (1f - missDist / missRadius);
                evade += dodge * urgency;
            }

            return evade * Mathf.Lerp(0.5f, 2f, skill);
        }

        // =====================================================================
        //  External setters — vehicle health, weapon count
        // =====================================================================

        public void SetHealth(float current, float max)
        {
            _hp    = current;
            _maxHp = Mathf.Max(max, 1f);
        }

        /// <summary>
        /// World-space lead-predicted aim point for the current target. Consumed by
        /// AICombat to aim weapons (and to point cosmetic turrets).
        /// </summary>
        public Vector3 GetAimPoint()
        {
            return ComputeLeadPosition(CurrentTarget);
        }

        public void SetWeaponCount(int count)
        {
            _weaponCount = Mathf.Max(count, 1);
        }

        /// <summary>
        /// Tell the brain its weapons' effective range so engage distance matches the
        /// loadout (snipers keep standoff, brawlers close in) instead of a flat preset.
        /// </summary>
        public void SetWeaponRange(float range)
        {
            if (range <= 0f) return;
            _weaponRange = range;
            // Higher-skill bots use more of their range; clamp to a sane band.
            float skill = Mathf.Lerp(0.55f, 0.9f, _preset.accuracy);
            _preset.engageRange = Mathf.Clamp(range * skill, 15f, 350f);
            // Detection must comfortably exceed engage range or bots never close in.
            _preset.awarenessRadius = Mathf.Max(_preset.awarenessRadius, _preset.engageRange * 1.7f);
        }

        /// <summary>True when the bot has a turret/aimed weapon that can bear without
        /// the hull facing the target — relaxes the fire-arc gate so it doesn't hold fire.</summary>
        public void SetHasAimedWeapon(bool value)
        {
            _hasAimedWeapon = value;
        }

        /// <summary>Tell the brain its own FFA player id so incoming-fire dodging can
        /// ignore the bot's own projectiles.</summary>
        public void SetPlayerId(int id)
        {
            _playerId = id;
        }

        // ----- shared anti-dogpile focus accounting -----
        private static int GetFocus(EntityId id)
        {
            return _focusCounts.TryGetValue(id, out int n) ? n : 0;
        }
        private void AcquireFocus(EntityId id)
        {
            if (id == default(EntityId)) return;
            _focusCounts[id] = GetFocus(id) + 1;
            _focusedTargetId = id;
        }
        private void ReleaseFocus()
        {
            if (_focusedTargetId == default(EntityId)) return;
            int n = GetFocus(_focusedTargetId) - 1;
            if (n <= 0) _focusCounts.Remove(_focusedTargetId);
            else _focusCounts[_focusedTargetId] = n;
            _focusedTargetId = default(EntityId);
        }

        public void SetDifficulty(AIDifficultyLevel level, float variance = 0.15f)
        {
            difficultyLevel    = level;
            personalityVariance = variance;
            _preset = AIDifficultyPreset.ForLevel(level);
            _preset.ApplyVariance(variance);
        }

        /// <summary>Set whether this AI is in air domain (can fly) or ground/water (stays on surface).</summary>
        public void SetDomain(string domain)
        {
            _isAirDomain = string.Equals(domain, "air", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Initialize propulsion degradation tracking.
        /// Call during vehicle setup with the number of propulsion parts.
        /// </summary>
        public void InitPropulsionTracking(int propulsionPartCount)
        {
            _initialPropulsionCount = propulsionPartCount;
            _currentPropulsionCount = propulsionPartCount;
            _baseMoveSpeed = _currentMoveSpeed;
        }

        /// <summary>
        /// Initialize boost fuel capacity from fuel tank count, and set up fuel degradation tracking.
        /// Base 3s + 2s per fuel tank (matching player vehicle formula).
        /// </summary>
        public void InitBoost(int fuelTankCount)
        {
            _maxBoostFuel = fuelTankCount > 0 ? 3f + fuelTankCount * 2f : 0f;
            _boostFuel = _maxBoostFuel;
            _baseMaxBoostFuel = _maxBoostFuel;
            _initialFuelCount = fuelTankCount;
            _currentFuelCount = fuelTankCount;
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
            _currentMoveSpeed = _baseMoveSpeed * speedFraction;
        }

        /// <summary>
        /// Called when a fuel part is destroyed.
        /// Proportionally reduces max boost capacity (min 20% of base).
        /// Clamps current fuel to the new max.
        /// </summary>
        public void OnFuelPartDestroyed()
        {
            _currentFuelCount = Mathf.Max(_currentFuelCount - 1, 0);
            float ratio = _initialFuelCount > 0
                ? (float)_currentFuelCount / _initialFuelCount
                : 0f;
            float boostFraction = Mathf.Lerp(0.2f, 1.0f, ratio);
            _maxBoostFuel = _baseMaxBoostFuel * boostFraction;
            if (_boostFuel > _maxBoostFuel)
                _boostFuel = _maxBoostFuel;
        }

        // =====================================================================
        //  Target selection and scoring
        // =====================================================================

        private void UpdateTargetSelection()
        {
            _targetSwitchTimer -= Time.deltaTime;
            _targetPersistenceTimer += Time.deltaTime;

            _candidates.Clear();

            // Read from VehicleRuntime's static registry (no scene scan per tick).
            var allVehicles = CloseEncounters.Arena.VehicleRuntime.LiveInstances;
            if (allVehicles == null || allVehicles.Count == 0)
            {
                CurrentTarget = null;
                return;
            }

            Vector3 myPos = transform.position;

            for (int i = 0; i < allVehicles.Count; i++)
            {
                GameObject go = allVehicles[i].gameObject;
                if (go == null || go == gameObject) continue;
                if (!allVehicles[i].IsAlive) continue;
                if (!go.activeInHierarchy) continue;

                Transform t = go.transform;
                float dist = Vector3.Distance(myPos, t.position);
                if (dist > _preset.awarenessRadius) continue;

                TargetCandidate c;
                c.transform = t;
                c.distance = dist;

                // Read live HP straight from the runtime (works for the player too,
                // who has no AIController) so bots can focus-fire wounded enemies.
                var vr = allVehicles[i];
                c.hpFraction = vr.MaxHP > 0 ? (float)vr.TotalHP / vr.MaxHP : 1f;

                c.threatScore = ComputeThreatScore(t, dist);
                c.persistenceBonus = (t == _lastTarget) ? TargetPersistenceBonus : 0f;
                c.hasLOS = CheckLineOfSight(myPos, t.position);

                // Anti-dogpile: how many OTHER bots already focus this target.
                int otherFocus = GetFocus(t.GetEntityId());
                if (t.GetEntityId() == _focusedTargetId && otherFocus > 0) otherFocus--; // don't count self

                // Composite score: lower is better
                float distScore   = dist;
                float hpScore     = c.hpFraction * 40f;      // prefer low-HP
                float threatScore = -c.threatScore * 20f;     // prefer high-threat
                float losBonus    = c.hasLOS ? -15f : 20f;
                float persist     = -c.persistenceBonus;
                float crowdPenalty = otherFocus * 22f;        // spread fire, no swarming

                // Retaliation: strongly prefer whoever just shot us (scaled by
                // aggression, so timid bots barely react and brawlers turn hard).
                float retaliation = (_recentAttackerTimer > 0f
                    && t.GetEntityId() == _recentAttackerId)
                    ? -Mathf.Lerp(20f, 70f, _preset.aggression) : 0f;

                c.totalScore = distScore + hpScore + threatScore + losBonus + persist
                    + crowdPenalty + retaliation;
                _candidates.Add(c);
            }

            if (_candidates.Count == 0)
            {
                CurrentTarget = null;
                return;
            }

            // Sort ascending — lowest totalScore is best
            _candidates.Sort((a, b) => a.totalScore.CompareTo(b.totalScore));

            Transform best = _candidates[0].transform;

            // Respect switch cooldown unless target is dead/gone
            if (_targetSwitchTimer > 0f && _lastTarget != null && _lastTarget && _lastTarget.gameObject.activeInHierarchy)
                best = _lastTarget;

            if (best != _lastTarget)
            {
                _lastTarget = best;
                _targetSwitchTimer = TargetSwitchCooldown;
                _targetPersistenceTimer = 0f;
                // Update the shared focus registry so other bots avoid this target.
                ReleaseFocus();
                if (best != null) AcquireFocus(best.GetEntityId());
            }

            CurrentTarget = best;

            // Target memory: refresh last-known position only while we can actually
            // see the target, so a broken LOS leaves us searching the last sighting.
            if (CurrentTarget != null && CheckLineOfSight(transform.position, CurrentTarget.position))
            {
                _lastKnownTargetPos = CurrentTarget.position;
                _hasLastKnown = true;
            }
        }

        private float ComputeThreatScore(Transform target, float distance)
        {
            // Simple threat: inversely proportional to distance, boosted if target
            // is facing us (dot product of their forward vs direction to us).
            if (distance < 0.01f) return 1f;
            float proximityThreat = 1f - Mathf.Clamp01(distance / _preset.awarenessRadius);

            Vector3 toMe = (transform.position - target.position).normalized;
            float facingDot = Mathf.Max(0f, Vector3.Dot(target.forward, toMe));

            return proximityThreat * 0.6f + facingDot * 0.4f;
        }

        private bool CheckLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.1f) return true;
            return !Physics.Raycast(from + Vector3.up * 1f, dir / dist, dist, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        // =====================================================================
        //  Stuck detection
        // =====================================================================

        private void UpdateStuckDetection(float dt)
        {
            _stuckCheckAccum += dt;
            if (_stuckCheckAccum < StuckCheckInterval) return;
            _stuckCheckAccum = 0f;

            float movedDist = Vector3.Distance(transform.position, _lastStuckCheckPos);
            _lastStuckCheckPos = transform.position;

            if (movedDist < StuckDistanceThreshold && CurrentState != AIState.Idle && CurrentState != AIState.StuckRecover)
            {
                _stuckTimer += StuckCheckInterval;
                if (_stuckTimer >= _preset.stuckRecoverTime)
                {
                    TransitionTo(AIState.StuckRecover);
                    _stuckTimer = 0f;
                }
            }
            else
            {
                _stuckTimer = 0f;
            }
        }

        // =====================================================================
        //  Weapon cycling
        // =====================================================================

        private void UpdateWeaponCycling(float dt)
        {
            if (_weaponCount <= 1) return;

            _weaponCycleTimer += dt;
            if (_weaponCycleTimer >= _preset.weaponCyclePeriod)
            {
                _weaponCycleTimer = 0f;
                _currentWeaponIndex = (_currentWeaponIndex + 1) % _weaponCount;
            }
        }

        // =====================================================================
        //  FSM — state machine
        // =====================================================================

        private void TransitionTo(AIState newState)
        {
            if (newState == CurrentState) return;

            // Exit logic
            switch (CurrentState)
            {
                case AIState.Flank:
                    _flankTimer = 0f;
                    break;
                case AIState.Evade:
                    _evadeTimer = 0f;
                    break;
                case AIState.Retreat:
                    _retreatTimer = 0f;
                    break;
                case AIState.StuckRecover:
                    _stuckRecoverTimer = 0f;
                    break;
            }

            // Enter logic
            switch (newState)
            {
                case AIState.Flank:
                    _flankSide = UnityEngine.Random.value > 0.5f ? 1f : -1f;
                    _flankTimer = 0f;
                    break;
                case AIState.Evade:
                    _evadeTimer = 0f;
                    _evadeDirection = PickEvadeDirection();
                    break;
                case AIState.Retreat:
                    _retreatTimer = 0f;
                    break;
                case AIState.StuckRecover:
                    _stuckRecoverTimer = 0f;
                    break;
            }

            CurrentState = newState;
        }

        private void UpdateStateMachine(float dt)
        {
            // Pre-check: should we retreat?
            float hpFrac = _maxHp > 0f ? _hp / _maxHp : 1f;
            bool lowHP = hpFrac < _preset.retreatHpFraction;

            switch (CurrentState)
            {
                case AIState.Idle:
                    StateIdle(dt);
                    break;
                case AIState.Seek:
                    StateSeek(dt, lowHP);
                    break;
                case AIState.Flank:
                    StateFlank(dt, lowHP);
                    break;
                case AIState.Engage:
                    StateEngage(dt, lowHP);
                    break;
                case AIState.Evade:
                    StateEvade(dt);
                    break;
                case AIState.Retreat:
                    StateRetreat(dt);
                    break;
                case AIState.StuckRecover:
                    StateStuckRecover(dt);
                    break;
            }
        }

        // ----- Idle -----
        private void StateIdle(float dt)
        {
            if (CurrentTarget != null)
            {
                TransitionTo(AIState.Seek);
                return;
            }

            // Air bots patrol in HandleAirFlight; ground/water bots roam toward random
            // points so they aren't motionless "training cones" and drift into contact.
            if (_isAirDomain)
            {
                CurrentInput = AIInput.Zero;
                return;
            }

            _patrolTimer -= dt;
            if (_patrolTimer <= 0f || (transform.position - _patrolPoint).sqrMagnitude < 144f)
            {
                _patrolTimer = UnityEngine.Random.Range(4f, 9f);
                Vector2 r = UnityEngine.Random.insideUnitCircle * (arenaHalfSize.x * 0.5f);
                _patrolPoint = arenaCentre + new Vector3(r.x, 0f, r.y);
            }

            var input = AIInput.Zero;
            Vector3 dir = NavigateToward(_patrolPoint, dt);
            ApplySteeringToInput(dir, ref input);
            input.forward = 0.45f;
            input.weaponIndex = _currentWeaponIndex;
            CurrentInput = input;
        }

        // ----- Seek -----
        private void StateSeek(float dt, bool lowHP)
        {
            if (CurrentTarget == null) { TransitionTo(AIState.Idle); return; }
            if (lowHP) { TransitionTo(AIState.Retreat); return; }

            float dist = Vector3.Distance(transform.position, CurrentTarget.position);
            bool seeTarget = CheckLineOfSight(transform.position, CurrentTarget.position);

            if (dist < _preset.engageRange && seeTarget)
            {
                // Decide: flank or engage?
                if (UnityEngine.Random.value < _preset.flankProbability)
                    TransitionTo(AIState.Flank);
                else
                    TransitionTo(AIState.Engage);
                return;
            }

            // Drive toward the target if we can see it; otherwise head to where we
            // last saw it (search the last sighting rather than tracking through walls).
            Vector3 navTarget = (!seeTarget && _hasLastKnown) ? _lastKnownTargetPos : CurrentTarget.position;
            var input = AIInput.Zero;
            Vector3 desiredDir = NavigateToward(navTarget, dt);
            ApplySteeringToInput(desiredDir, ref input);
            input.forward = 1f;
            input.boost = ShouldBoost(dist);
            input.weaponIndex = _currentWeaponIndex;
            CurrentInput = input;
        }

        // ----- Flank -----
        private void StateFlank(float dt, bool lowHP)
        {
            if (CurrentTarget == null) { TransitionTo(AIState.Idle); return; }
            if (lowHP) { TransitionTo(AIState.Retreat); return; }

            _flankTimer += dt;
            if (_flankTimer >= FlankDuration)
            {
                TransitionTo(AIState.Engage);
                return;
            }

            float dist = Vector3.Distance(transform.position, CurrentTarget.position);

            Vector3 toTarget = (CurrentTarget.position - transform.position).normalized;
            Vector3 flankDir = Vector3.Cross(Vector3.up, toTarget) * _flankSide;
            Vector3 combined = (toTarget * 0.4f + flankDir * 0.6f).normalized;

            Vector3 desiredDir = BlendWithObstacleAvoidance(combined, dt);

            var input = AIInput.Zero;
            ApplySteeringToInput(desiredDir, ref input);
            input.forward = 0.8f;
            input.strafe  = _flankSide * 0.5f;
            input.boost   = ShouldBoost(dist);

            // Fire opportunistically if we have LOS
            if (dist < _preset.engageRange && CheckLineOfSight(transform.position, CurrentTarget.position))
            {
                input.fire = ShouldFire(dist);
            }

            input.weaponIndex = _currentWeaponIndex;
            CurrentInput = input;
        }

        // ----- Engage -----
        private void StateEngage(float dt, bool lowHP)
        {
            if (CurrentTarget == null) { TransitionTo(AIState.Idle); return; }
            if (lowHP) { TransitionTo(AIState.Retreat); return; }

            float dist = Vector3.Distance(transform.position, CurrentTarget.position);

            // Lost range or LOS — seek again
            if (dist > _preset.engageRange * 1.3f || !CheckLineOfSight(transform.position, CurrentTarget.position))
            {
                TransitionTo(AIState.Seek);
                return;
            }

            // Random evade — bumped from 0.02 so Easy/Medium bots visibly jink mid-fight
            // instead of standing still (they evaded ~1% of decisions before).
            if (UnityEngine.Random.value < (1f - _preset.aggression) * 0.05f)
            {
                TransitionTo(AIState.Evade);
                return;
            }

            Vector3 aimPoint = ComputeLeadPosition(CurrentTarget);
            Vector3 toAim = (aimPoint - transform.position).normalized;
            Vector3 desiredDir = BlendWithObstacleAvoidance(toAim, dt);

            var input = AIInput.Zero;
            ApplySteeringToInput(desiredDir, ref input);

            // Maintain comfortable distance
            float idealDist = _preset.engageRange * 0.6f;
            if (dist > idealDist + 5f)
                input.forward = 0.6f;
            else if (dist < idealDist - 5f)
                input.forward = -0.4f;
            else
                input.forward = 0.1f;

            input.fire        = ShouldFire(dist);
            input.boost       = false;
            // Circle-strafe while engaging (matches the Flank pattern) so bots orbit their
            // target instead of just driving straight in and out — far less turret-like.
            input.strafe      = _flankSide * 0.45f;
            input.weaponIndex = _currentWeaponIndex;
            CurrentInput = input;
        }

        // ----- Evade -----
        private void StateEvade(float dt)
        {
            _evadeTimer += dt;
            if (_evadeTimer >= EvadeDuration)
            {
                TransitionTo(CurrentTarget != null ? AIState.Engage : AIState.Idle);
                return;
            }

            Vector3 desiredDir = BlendWithObstacleAvoidance(_evadeDirection, dt);
            var input = AIInput.Zero;
            ApplySteeringToInput(desiredDir, ref input);
            input.forward = 0.9f;
            input.strafe  = _flankSide * 0.7f;
            input.boost   = true;
            input.weaponIndex = _currentWeaponIndex;
            CurrentInput = input;
        }

        // ----- Retreat -----
        private void StateRetreat(float dt)
        {
            _retreatTimer += dt;
            float hpFrac = _maxHp > 0f ? _hp / _maxHp : 1f;

            if (hpFrac > _preset.retreatHpFraction + 0.1f || _retreatTimer > RetreatDuration)
            {
                TransitionTo(CurrentTarget != null ? AIState.Seek : AIState.Idle);
                return;
            }

            // Run away from target, toward arena centre
            Vector3 awayDir;
            if (CurrentTarget != null)
                awayDir = (transform.position - CurrentTarget.position).normalized;
            else
                awayDir = (arenaCentre - transform.position).normalized;

            // Blend toward arena centre to avoid cornering
            Vector3 toCentre = (arenaCentre - transform.position).normalized;
            Vector3 combined = (awayDir * 0.6f + toCentre * 0.4f).normalized;
            Vector3 desiredDir = BlendWithObstacleAvoidance(combined, dt);

            var input = AIInput.Zero;
            ApplySteeringToInput(desiredDir, ref input);
            input.forward = 1f;
            input.boost   = true;
            input.weaponIndex = _currentWeaponIndex;
            CurrentInput = input;
        }

        // ----- StuckRecover -----
        private void StateStuckRecover(float dt)
        {
            _stuckRecoverTimer += dt;
            if (_stuckRecoverTimer > 2.0f)
            {
                TransitionTo(CurrentTarget != null ? AIState.Seek : AIState.Idle);
                return;
            }

            var input = AIInput.Zero;

            // Phase 1: reverse
            if (_stuckRecoverTimer < 1.0f)
            {
                input.forward = -1f;
                input.yaw = _flankSide * 0.6f;
            }
            // Phase 2: turn and go
            else
            {
                input.forward = 1f;
                input.yaw = -_flankSide * 0.8f;
            }

            input.boost = true;
            input.weaponIndex = _currentWeaponIndex;
            CurrentInput = input;
        }

        // =====================================================================
        //  Navigation helpers
        // =====================================================================

        /// <summary>
        /// Returns a world-space direction to move toward 'target', accounting
        /// for obstacle avoidance, hazard zones, and arena boundaries.
        /// </summary>
        private Vector3 NavigateToward(Vector3 target, float dt)
        {
            Vector3 toTarget = (target - transform.position).normalized;
            return BlendWithObstacleAvoidance(toTarget, dt);
        }

        /// <summary>
        /// Given a desired direction, blend in obstacle avoidance, hazard
        /// avoidance, and arena boundary corrections.
        /// </summary>
        private Vector3 BlendWithObstacleAvoidance(Vector3 desiredDir, float dt)
        {
            Vector3 avoidDir = ComputeObstacleAvoidance();
            Vector3 hazardDir = ComputeHazardAvoidance();
            Vector3 boundaryDir = ComputeBoundaryAvoidance();
            Vector3 evadeDir = ComputeIncomingThreatEvasion(transform.position);

            float w = _preset.obstacleAvoidWeight;

            Vector3 result = desiredDir
                + avoidDir   * (w * 1.5f)
                + hazardDir  * (w * 1.2f)
                + boundaryDir * (w * 2.0f)
                + evadeDir;          // already skill-scaled; juke out of incoming fire

            if (result.sqrMagnitude < 0.001f)
                result = desiredDir;

            return result.normalized;
        }

        // =====================================================================
        //  5-ray obstacle avoidance
        // =====================================================================

        private Vector3 ComputeObstacleAvoidance()
        {
            Vector3 origin = transform.position + Vector3.up * 1.0f;
            Vector3 fwd = transform.forward;

            Vector3 steerAway = Vector3.zero;

            for (int i = 0; i < RayCount; i++)
            {
                // Rotate forward by the ray angle around up
                Quaternion rot = Quaternion.AngleAxis(RayAngles[i], Vector3.up);
                _rayDirs[i] = rot * fwd;

                if (Physics.Raycast(origin, _rayDirs[i], out RaycastHit hit, rayLength, obstacleMask, QueryTriggerInteraction.Ignore))
                {
                    _rayHits[i] = hit.distance;
                    float urgency = 1f - (hit.distance / rayLength);
                    // Steer perpendicular to the ray that hit
                    Vector3 perp = Vector3.Cross(Vector3.up, _rayDirs[i]).normalized;
                    // Choose the side that points more away from the hit normal
                    if (Vector3.Dot(perp, hit.normal) < 0f)
                        perp = -perp;
                    steerAway += perp * urgency;
                }
                else
                {
                    _rayHits[i] = rayLength;
                }
            }

            return steerAway;
        }

        // =====================================================================
        //  Hazard zone AABB avoidance
        // =====================================================================

        private Vector3 ComputeHazardAvoidance()
        {
            if (_hazardZones.Count == 0) return Vector3.zero;

            Vector3 pos = transform.position;
            Vector3 steer = Vector3.zero;

            for (int i = 0; i < _hazardZones.Count; i++)
            {
                HazardZone hz = _hazardZones[i];
                Vector3 closest = hz.ClosestPointOnSurface(pos);
                float dist = Vector3.Distance(pos, closest);

                // Only care if we are inside or very close
                float dangerRadius = Mathf.Max(hz.halfExtents.x, hz.halfExtents.z) * 0.3f;
                if (dist > dangerRadius && !hz.Contains(pos)) continue;

                Vector3 away;
                if (hz.Contains(pos))
                {
                    // Push outward from centre
                    away = (pos - hz.center).normalized;
                    if (away.sqrMagnitude < 0.001f) away = transform.right;
                    steer += away * 2f;
                }
                else
                {
                    away = (pos - closest).normalized;
                    float urgency = 1f - Mathf.Clamp01(dist / dangerRadius);
                    steer += away * urgency;
                }
            }

            return steer;
        }

        // =====================================================================
        //  Arena boundary avoidance
        // =====================================================================

        private Vector3 ComputeBoundaryAvoidance()
        {
            Vector3 pos = transform.position;
            Vector3 steer = Vector3.zero;

            // Check each axis
            float margin = arenaBoundaryMargin;

            // X boundaries
            float xMin = arenaCentre.x - arenaHalfSize.x + margin;
            float xMax = arenaCentre.x + arenaHalfSize.x - margin;
            if (pos.x < xMin) steer.x += (xMin - pos.x) / margin;
            if (pos.x > xMax) steer.x += (xMax - pos.x) / margin;

            // Z boundaries
            float zMin = arenaCentre.z - arenaHalfSize.z + margin;
            float zMax = arenaCentre.z + arenaHalfSize.z - margin;
            if (pos.z < zMin) steer.z += (zMin - pos.z) / margin;
            if (pos.z > zMax) steer.z += (zMax - pos.z) / margin;

            // Y boundaries (only for air domain)
            if (_isAirDomain)
            {
                float yMin = arenaCentre.y - arenaHalfSize.y + margin;
                float yMax = arenaCentre.y + arenaHalfSize.y - margin;
                if (pos.y < yMin) steer.y += (yMin - pos.y) / margin;
                if (pos.y > yMax) steer.y += (yMax - pos.y) / margin;
            }

            return steer;
        }

        // =====================================================================
        //  Steering -> AIInput
        // =====================================================================

        private void ApplySteeringToInput(Vector3 desiredWorldDir, ref AIInput input)
        {
            if (desiredWorldDir.sqrMagnitude < 0.001f) return;

            // Flatten to horizontal for ground/water vehicles (prevents flying)
            if (!_isAirDomain)
            {
                desiredWorldDir.y = 0f;
                if (desiredWorldDir.sqrMagnitude < 0.001f) return;
                desiredWorldDir.Normalize();
            }

            Vector3 localDir = transform.InverseTransformDirection(desiredWorldDir);

            // Yaw: steer toward the desired direction
            float yawAngle = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            input.yaw = Mathf.Clamp(yawAngle / 45f, -1f, 1f);

            // Forward: go forward if roughly facing the right way
            float forwardDot = Vector3.Dot(transform.forward, desiredWorldDir);
            if (input.forward == 0f)
                input.forward = Mathf.Clamp(forwardDot, -0.5f, 1f);
        }

        // =====================================================================
        //  Lead prediction
        // =====================================================================

        private Vector3 ComputeLeadPosition(Transform target)
        {
            if (target == null) return transform.position + transform.forward * 20f;

            Rigidbody targetRb = target.GetComponent<Rigidbody>();
            Vector3 targetPos = target.position;

            if (targetRb == null || _preset.leadPredictionFactor < 0.01f)
                return targetPos;

            Vector3 targetVel = targetRb.linearVelocity;
            float dist = Vector3.Distance(transform.position, targetPos);

            // Rough projectile speed estimate
            float projectileSpeed = 60f;
            float tof = dist / projectileSpeed;

            Vector3 predicted = targetPos + targetVel * tof * _preset.leadPredictionFactor;

            // Apply accuracy jitter
            float jitter = (1f - _preset.accuracy) * dist * 0.05f;
            predicted += UnityEngine.Random.insideUnitSphere * jitter;

            return predicted;
        }

        // =====================================================================
        //  Fire decision
        // =====================================================================

        private bool ShouldFire(float distToTarget)
        {
            if (CurrentTarget == null) return false;

            // Check if roughly facing target
            Vector3 toTarget = (CurrentTarget.position - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, toTarget);

            // Turret/aimed weapons can bear without the hull facing the enemy, so only
            // require the target be in the front hemisphere. Fixed-weapon bots still
            // need to point their hull (tighter cone scales with accuracy).
            float minDot = _hasAimedWeapon ? 0.1f : Mathf.Lerp(0.80f, 0.95f, _preset.accuracy);
            if (dot < minDot) return false;

            // Range check
            if (distToTarget > _preset.engageRange * 1.2f) return false;

            // LOS check
            if (!CheckLineOfSight(transform.position, CurrentTarget.position)) return false;

            // Accuracy-based random skip
            if (UnityEngine.Random.value > _preset.accuracy) return false;

            return true;
        }

        // =====================================================================
        //  Boost decision
        // =====================================================================

        private bool ShouldBoost(float distToTarget)
        {
            if (distToTarget < _preset.engageRange * 0.5f) return false;
            return UnityEngine.Random.value < _preset.boostUseProbability;
        }

        // =====================================================================
        //  Evade direction picker
        // =====================================================================

        private Vector3 PickEvadeDirection()
        {
            // Pick a mostly-perpendicular direction, away from the target if possible
            Vector3 perpendicular = Vector3.Cross(Vector3.up, transform.forward) * _flankSide;

            if (CurrentTarget != null)
            {
                Vector3 away = (transform.position - CurrentTarget.position).normalized;
                perpendicular = (perpendicular * 0.6f + away * 0.4f).normalized;
            }

            return perpendicular;
        }

        // =====================================================================
        //  Input smoothing — prevents jerky vehicle motion
        // =====================================================================

        private void ProduceSmoothedInput(float dt)
        {
            // Smooth toward the LAST decided input (held between reaction ticks) so
            // motion stays fluid every frame regardless of decision cadence.
            AIInput raw = _decided;

            float speed = InputSmoothSpeed * dt;
            _smoothForward = Mathf.MoveTowards(_smoothForward, raw.forward, speed);
            _smoothStrafe  = Mathf.MoveTowards(_smoothStrafe,  raw.strafe,  speed);
            _smoothYaw     = Mathf.MoveTowards(_smoothYaw,     raw.yaw,     speed);

            CurrentInput = new AIInput
            {
                forward     = _smoothForward,
                strafe      = _smoothStrafe,
                yaw         = _smoothYaw,
                fire        = raw.fire,
                boost       = raw.boost && CanBoost(),
                weaponIndex = raw.weaponIndex,
            };
        }

        // =====================================================================
        //  Debug gizmos
        // =====================================================================

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Obstacle rays
            Vector3 origin = transform.position + Vector3.up * 1f;
            Vector3 fwd = transform.forward;
            for (int i = 0; i < RayCount; i++)
            {
                Quaternion rot = Quaternion.AngleAxis(RayAngles[i], Vector3.up);
                Vector3 dir = rot * fwd;
                float hitDist = (Application.isPlaying && i < _rayHits.Length) ? _rayHits[i] : rayLength;
                Gizmos.color = hitDist < rayLength ? Color.red : Color.green;
                Gizmos.DrawRay(origin, dir * hitDist);
            }

            // Arena boundary box
            Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
            Gizmos.DrawWireCube(arenaCentre, arenaHalfSize * 2f);

            // Target line
            if (CurrentTarget != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(transform.position, CurrentTarget.position);
            }

            // Hazard zones
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.25f);
            for (int i = 0; i < _hazardZones.Count; i++)
            {
                Gizmos.DrawWireCube(_hazardZones[i].center, _hazardZones[i].halfExtents * 2f);
            }
        }
#endif
    }
}
