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
        private const float InputSmoothSpeed = 6f;

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
        private bool _cachedPhysicsChecked;

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

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _cachedWaterPhysics = GetComponent<CloseEncounters.VehiclePhysics.WaterPhysics>();
            _cachedGroundPhysics = GetComponent<CloseEncounters.VehiclePhysics.GroundPhysics>();
            _cachedPhysicsChecked = true;

            _preset = AIDifficultyPreset.ForLevel(difficultyLevel);
            _preset.ApplyVariance(personalityVariance);

            _lastStuckCheckPos = transform.position;
            _flankSide = UnityEngine.Random.value > 0.5f ? 1f : -1f;

            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;

            float dt = Time.deltaTime;

            // Reaction delay accumulator
            _reactionAccum += dt;
            if (_reactionAccum < _preset.reactionTime) return;
            _reactionAccum = 0f;

            UpdateTargetSelection();
            UpdateStuckDetection(dt);
            UpdateWeaponCycling(dt);
            UpdateStateMachine(dt);
            ProduceSmoothedInput(dt);
        }

        /// <summary>
        /// For ground AI without GroundPhysics: apply forces directly,
        /// same way PlayerVehicleController does.
        /// </summary>
        private void FixedUpdate()
        {
            if (!_initialized || _rb == null) return;
            if (_isAirDomain) return; // air uses different physics
            // Skip if WaterPhysics or GroundPhysics handle movement
            if (_cachedWaterPhysics != null) return;
            if (_cachedGroundPhysics != null) return;

            float dt = Time.fixedDeltaTime;
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
        //  External setters — vehicle health, weapon count
        // =====================================================================

        public void SetHealth(float current, float max)
        {
            _hp    = current;
            _maxHp = Mathf.Max(max, 1f);
        }

        public void SetWeaponCount(int count)
        {
            _weaponCount = Mathf.Max(count, 1);
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

                // Try to read HP from a component — fall back to 1
                var otherAI = go.GetComponent<AIController>();
                if (otherAI != null)
                    c.hpFraction = otherAI._maxHp > 0f ? otherAI._hp / otherAI._maxHp : 1f;
                else
                    c.hpFraction = 1f;

                c.threatScore = ComputeThreatScore(t, dist);
                c.persistenceBonus = (t == _lastTarget) ? TargetPersistenceBonus : 0f;
                c.hasLOS = CheckLineOfSight(myPos, t.position);

                // Composite score: lower is better
                float distScore   = dist;
                float hpScore     = c.hpFraction * 40f; // prefer low-HP
                float threatScore = -c.threatScore * 20f; // prefer high-threat
                float losBonus    = c.hasLOS ? -15f : 20f;
                float persist     = -c.persistenceBonus;

                c.totalScore = distScore + hpScore + threatScore + losBonus + persist;
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
            }

            CurrentTarget = best;
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
            CurrentInput = AIInput.Zero;
            if (CurrentTarget != null)
                TransitionTo(AIState.Seek);
        }

        // ----- Seek -----
        private void StateSeek(float dt, bool lowHP)
        {
            if (CurrentTarget == null) { TransitionTo(AIState.Idle); return; }
            if (lowHP) { TransitionTo(AIState.Retreat); return; }

            float dist = Vector3.Distance(transform.position, CurrentTarget.position);

            if (dist < _preset.engageRange && CheckLineOfSight(transform.position, CurrentTarget.position))
            {
                // Decide: flank or engage?
                if (UnityEngine.Random.value < _preset.flankProbability)
                    TransitionTo(AIState.Flank);
                else
                    TransitionTo(AIState.Engage);
                return;
            }

            // Drive toward target
            var input = AIInput.Zero;
            Vector3 desiredDir = NavigateToward(CurrentTarget.position, dt);
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

            // Random evade
            if (UnityEngine.Random.value < (1f - _preset.aggression) * 0.02f)
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

            float avoidMag   = avoidDir.magnitude;
            float hazardMag  = hazardDir.magnitude;
            float boundaryMag = boundaryDir.magnitude;

            float w = _preset.obstacleAvoidWeight;

            Vector3 result = desiredDir
                + avoidDir   * (w * 1.5f)
                + hazardDir  * (w * 1.2f)
                + boundaryDir * (w * 2.0f);

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

            // Tighter cone at higher accuracy
            float minDot = Mathf.Lerp(0.80f, 0.95f, _preset.accuracy);
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
            AIInput raw = CurrentInput;

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
                boost       = raw.boost,
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
