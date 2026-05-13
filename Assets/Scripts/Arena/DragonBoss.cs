using UnityEngine;
using CloseEncounters.Combat;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Dragon boss AI with a full state machine. Loads the RedDragon prefab at
    /// runtime, patrols the arena, hunts vehicles, performs cycling attack
    /// patterns, rests on an island, and plays a death sequence when HP reaches
    /// zero.  Designed as a drop-in arena hazard spawned by the arena builder.
    /// </summary>
    public class DragonBoss : MonoBehaviour
    {
        // =================================================================
        // Public config
        // =================================================================

        [Header("Flight")]
        public float flyHeight = 20f;
        public float patrolRadius = 120f;
        public float patrolSpeed = 15f;
        public float huntSpeed = 20f;
        public float takeOffRiseHeight = 15f;
        public float takeOffDuration = 2f;
        public float landDuration = 2f;

        [Header("Combat")]
        public float attackRange = 20f;
        public float detectionRange = 80f;
        public float scanInterval = 2f;
        public int maxHP = 500;

        [Header("Rest")]
        public float restDurationMin = 4f;
        public float restDurationMax = 8f;

        [Header("Damage")]
        public int drakarisDamage = 160;
        public float drakarisRadius = 15f;
        public int biteDamage = 400;
        public float biteRadius = 12f;
        public float biteKnockback = 30f;
        public int flyingAttackDamage = 80;
        public float flyingAttackRadius = 12f;

        [Header("World")]
        public Vector3 islandCenter = Vector3.zero;
        public float islandReturnThreshold = 10f;

        // =================================================================
        // State machine
        // =================================================================

        private enum DragonState
        {
            Resting,
            TakeOff,
            Flying,
            Hunting,
            Attacking,
            Returning,
            Landing,
            Dead
        }

        private DragonState _state = DragonState.Resting;

        // =================================================================
        // Runtime fields
        // =================================================================

        private GameObject _model;
        private Animator _animator;
        private int _currentHP;
        private float _stateTimer;
        private float _restDuration;
        private float _patrolAngle;
        private float _scanTimer;
        private Transform _currentTarget;
        private int _attackIndex;
        private string _currentAnim = "";
        private float _attackDuration;
        private int _attacksSinceRest;

        // TakeOff / Landing interpolation
        private Vector3 _takeOffStart;
        private Vector3 _takeOffEnd;
        private Vector3 _landStart;
        private Vector3 _landEnd;

        // Fire VFX handle so we can destroy it if the dragon dies mid-breath
        private GameObject _activeFireVFX;

        // =================================================================
        // Lifecycle
        // =================================================================

        private void Start()
        {
            LoadModel();
            _currentHP = maxHP;
            _restDuration = Random.Range(restDurationMin, restDurationMax);
            EnterState(DragonState.Resting);
        }

        private void Update()
        {
            if (_state == DragonState.Dead)
            {
                UpdateDead();
                return;
            }

            switch (_state)
            {
                case DragonState.Resting:   UpdateResting();   break;
                case DragonState.TakeOff:   UpdateTakeOff();   break;
                case DragonState.Flying:    UpdateFlying();     break;
                case DragonState.Hunting:   UpdateHunting();    break;
                case DragonState.Attacking: UpdateAttacking();  break;
                case DragonState.Returning: UpdateReturning();  break;
                case DragonState.Landing:   UpdateLanding();    break;
            }
        }

        // =================================================================
        // Model loading
        // =================================================================

        private void LoadModel()
        {
            // The dragon model is already instantiated by SpawnDragon() on this GameObject.
            // Don't load another copy -- just find the existing Animator.
            _model = gameObject;
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                Debug.LogWarning("[DragonBoss] No Animator found on dragon. Animations won't play.");
            }
            else
            {
                Debug.Log($"[DragonBoss] Animator found: {_animator.runtimeAnimatorController?.name ?? "NO CONTROLLER"}");
            }
        }

        // =================================================================
        // Animation helper
        // =================================================================

        /// <summary>
        /// Switches the active animator bool. Clears the previous parameter
        /// before enabling the new one so that exactly one bool is true at a
        /// time, as required by the embedded controller.
        /// </summary>
        private void SetAnimation(string paramName)
        {
            if (_animator == null) return;
            if (_currentAnim == paramName) return;

            if (!string.IsNullOrEmpty(_currentAnim))
                _animator.SetBool(_currentAnim, false);

            _animator.SetBool(paramName, true);
            _currentAnim = paramName;

            // Force play the animation directly as backup
            // (some Animator Controllers don't transition on bool alone)
            _animator.Play(paramName, 0, 0f);
        }

        // =================================================================
        // State entry
        // =================================================================

        private void EnterState(DragonState newState)
        {
            _state = newState;
            _stateTimer = 0f;

            switch (newState)
            {
                case DragonState.Resting:
                    _restDuration = Random.Range(restDurationMin, restDurationMax);
                    _attacksSinceRest = 0;
                    SetAnimation("IdleSimple");
                    break;

                case DragonState.TakeOff:
                    SetAnimation("TakeOff");
                    _takeOffStart = transform.position;
                    _takeOffEnd = transform.position + Vector3.up * takeOffRiseHeight;
                    break;

                case DragonState.Flying:
                    SetAnimation("FlyingFWD");
                    // Seed patrol angle from current XZ position relative to island
                    Vector3 offset = transform.position - islandCenter;
                    _patrolAngle = Mathf.Atan2(offset.z, offset.x);
                    _scanTimer = 0f;
                    break;

                case DragonState.Hunting:
                    SetAnimation("FlyingFWD");
                    break;

                case DragonState.Attacking:
                    PerformAttack();
                    break;

                case DragonState.Returning:
                    SetAnimation("FlyingFWD");
                    break;

                case DragonState.Landing:
                    SetAnimation("Lands");
                    _landStart = transform.position;
                    _landEnd = new Vector3(islandCenter.x, islandCenter.y, islandCenter.z);
                    break;

                case DragonState.Dead:
                    SetAnimation("Die");
                    CleanupFireVFX();
                    Debug.Log("[DragonBoss] Dragon has been slain!");
                    break;
            }
        }

        // =================================================================
        // Resting
        // =================================================================

        private void UpdateResting()
        {
            _stateTimer += Time.deltaTime;

            // Cycle through ALL ground idle animations
            float segment = _restDuration / 5f;
            float t = _stateTimer;
            if (t < segment)
                SetAnimation("IdleSimple");
            else if (t < segment * 2f)
                SetAnimation("Walk");          // pace around the island
            else if (t < segment * 3f)
                SetAnimation("IdleAgressive");
            else if (t < segment * 4f)
                SetAnimation("BattleStance");  // ready to fight
            else
                SetAnimation("IdleRestless");  // agitated, about to take off

            if (_stateTimer >= _restDuration)
            {
                EnterState(DragonState.TakeOff);
            }
        }

        // =================================================================
        // TakeOff
        // =================================================================

        private void UpdateTakeOff()
        {
            _stateTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_stateTimer / takeOffDuration);

            // Smooth rise using ease-out curve
            float eased = 1f - (1f - t) * (1f - t);
            transform.position = Vector3.Lerp(_takeOffStart, _takeOffEnd, eased);

            if (_stateTimer >= takeOffDuration)
            {
                EnterState(DragonState.Flying);
            }
        }

        // =================================================================
        // Flying (patrol)
        // =================================================================

        private void UpdateFlying()
        {
            _stateTimer += Time.deltaTime;

            // Circle the arena
            _patrolAngle += (patrolSpeed / patrolRadius) * Time.deltaTime;
            Vector3 target = new Vector3(
                islandCenter.x + Mathf.Cos(_patrolAngle) * patrolRadius,
                islandCenter.y + flyHeight,
                islandCenter.z + Mathf.Sin(_patrolAngle) * patrolRadius
            );

            MoveAndFace(target, patrolSpeed);

            // Scan for vehicles periodically
            _scanTimer += Time.deltaTime;
            if (_scanTimer >= scanInterval)
            {
                _scanTimer = 0f;
                Transform nearest = FindNearestAliveVehicle();
                if (nearest != null)
                {
                    float dist = Vector3.Distance(transform.position, nearest.position);
                    if (dist <= detectionRange)
                    {
                        _currentTarget = nearest;
                        EnterState(DragonState.Hunting);
                    }
                }
            }
        }

        // =================================================================
        // Hunting (chase)
        // =================================================================

        private void UpdateHunting()
        {
            _stateTimer += Time.deltaTime;

            // If target died or was destroyed, go back to patrol
            if (_currentTarget == null || !IsTargetAlive(_currentTarget))
            {
                _currentTarget = null;
                EnterState(DragonState.Flying);
                return;
            }

            // Fly toward target at hunt height
            Vector3 targetPos = _currentTarget.position;
            targetPos.y = islandCenter.y + flyHeight * 0.75f;
            MoveAndFace(targetPos, huntSpeed);

            float horizontalDist = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(_currentTarget.position.x, 0f, _currentTarget.position.z)
            );

            // Switch to Hover when close to target (menacing pause before attack)
            if (horizontalDist <= attackRange * 1.5f && horizontalDist > attackRange)
                SetAnimation("Hover");
            else
                SetAnimation("FlyingFWD");

            if (horizontalDist <= attackRange)
            {
                EnterState(DragonState.Attacking);
            }
        }

        // =================================================================
        // Attacking
        // =================================================================

        private void PerformAttack()
        {
            int attackType = _attackIndex % 3;
            _attackIndex++;
            _attacksSinceRest++;

            switch (attackType)
            {
                case 0: // Drakaris (fire breath)
                    SetAnimation("Drakaris");
                    _attackDuration = 3f;
                    PerformDrakaris();
                    break;

                case 1: // Bite
                    SetAnimation("Bite");
                    _attackDuration = 2f;
                    PerformBite();
                    break;

                case 2: // Flying attack (swoop)
                    SetAnimation("FlyingAttack");
                    _attackDuration = 2.5f;
                    PerformFlyingAttack();
                    break;
            }
        }

        private void PerformDrakaris()
        {
            // Find nearest target to aim at
            Vector3 firePos = transform.position + transform.forward * 5f;
            Vector3 targetPos = firePos + transform.forward * 80f; // default: straight ahead

            if (_currentTarget != null)
                targetPos = _currentTarget.position;
            else if (ArenaManager.Instance != null)
            {
                var vehicles = ArenaManager.Instance.GetVehicles();
                float closest = float.MaxValue;
                for (int i = 0; i < vehicles.Count; i++)
                {
                    if (vehicles[i] == null || !vehicles[i].IsAlive) continue;
                    float d = Vector3.Distance(transform.position, vehicles[i].transform.position);
                    if (d < closest) { closest = d; targetPos = vehicles[i].transform.position; }
                }
            }

            Vector3 direction = (targetPos - firePos).normalized;

            // 1. Fire breath stream (like laser cannon -- instant hitscan line of fire)
            float breathRange = 60f;
            RaycastHit hit;
            Vector3 endPoint;
            if (Physics.Raycast(firePos, direction, out hit, breathRange, ~0, QueryTriggerInteraction.Ignore))
            {
                endPoint = hit.point;
                // Damage anything hit
                var vr = hit.collider.GetComponentInParent<VehicleRuntime>();
                if (vr != null && vr.IsAlive)
                    DamageSystem.DealDamageToVehicle(vr, drakarisDamage, hit.point, skipControlParts: true);
                // Explosion at impact
                VFXManager.BigExplosion(hit.point, 1.5f);
                VFXManager.LargeFlames(hit.point, 1f);
            }
            else
            {
                endPoint = firePos + direction * breathRange;
            }

            // Visual fire stream (LineRenderer like laser but orange/red)
            var streamObj = new GameObject("FireBreathStream");
            var lr = streamObj.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, firePos);
            lr.SetPosition(1, endPoint);
            lr.startWidth = 1.5f;
            lr.endWidth = 0.5f;
            var streamMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            streamMat.color = new Color(1f, 0.4f, 0.05f, 0.9f);
            lr.material = streamMat;
            lr.startColor = new Color(1f, 0.6f, 0.1f, 1f);
            lr.endColor = new Color(1f, 0.2f, 0.0f, 0.5f);
            UnityEngine.Object.Destroy(streamObj, 0.3f);
            UnityEngine.Object.Destroy(streamMat, 0.3f);

            // Fire VFX along the stream
            VFXManager.LargeFlames(firePos, 1.5f);
            float streamLen = Vector3.Distance(firePos, endPoint);
            for (float t = 0.25f; t < 1f; t += 0.25f)
            {
                Vector3 midPoint = Vector3.Lerp(firePos, endPoint, t);
                VFXManager.TinyFlames(midPoint, 0.8f);
            }

            // 2. Also launch a straight fireball projectile for visual punch
            var fireball = new GameObject("DragonFireball");
            fireball.transform.position = firePos;
            var fb = fireball.AddComponent<DragonFireball>();
            fb.direction = direction;
            fb.damage = drakarisDamage;
            fb.speed = 25f;
            fb.lifetime = 4f;
            fb.explosionRadius = drakarisRadius;

            Debug.Log("[DragonBoss] Drakaris! Fire breath + fireball.");
        }

        private void PerformBite()
        {
            Vector3 bitePos = transform.position + transform.forward * 4f + Vector3.down * 3f;

            // Bite targets the nearest vehicle within bite range.
            // Use horizontal distance so the dragon's high altitude doesn't
            // make every bite miss (dragon at y=15-20, vehicles at y=0-2).
            if (ArenaManager.Instance != null)
            {
                var vehicles = ArenaManager.Instance.GetVehicles();
                VehicleRuntime closest = null;
                float closestDist = float.MaxValue;

                for (int i = 0; i < vehicles.Count; i++)
                {
                    if (vehicles[i] == null || !vehicles[i].IsAlive) continue;
                    Vector3 vPos = vehicles[i].transform.position;
                    float dist = Vector2.Distance(
                        new Vector2(bitePos.x, bitePos.z),
                        new Vector2(vPos.x, vPos.z));
                    if (dist < biteRadius && dist < closestDist)
                    {
                        closestDist = dist;
                        closest = vehicles[i];
                    }
                }

                if (closest != null)
                {
                    // Spread bite damage across multiple parts to avoid one-shotting control modules
                    int hits = 4;
                    int dmgPerHit = biteDamage / hits;
                    for (int h = 0; h < hits; h++)
                    {
                        Vector3 spreadPos = bitePos + Random.insideUnitSphere * 3f;
                        DamageSystem.DealDamageToVehicle(closest, dmgPerHit, spreadPos, skipControlParts: true);
                    }
                    VFXManager.BigExplosion(closest.transform.position, 1f);

                    // Fling the vehicle away from the dragon
                    var rb = closest.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        Vector3 flingDir = (closest.transform.position - transform.position).normalized;
                        flingDir.y = 0.6f; // launch upward
                        flingDir = flingDir.normalized;
                        rb.AddForce(flingDir * biteKnockback, ForceMode.VelocityChange);
                        rb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.VelocityChange);
                    }

                    Debug.Log($"[DragonBoss] Bite hit Vehicle {closest.PlayerId} for {biteDamage} damage!");
                }
                else
                {
                    Debug.Log("[DragonBoss] Bite missed -- no vehicle in range.");
                }
            }
        }

        // Swoop state tracked across UpdateAttacking frames
        private float _swoopTargetY;
        private float _swoopStartY;
        private bool _swoopDamageDealt;

        private void PerformFlyingAttack()
        {
            // Record starting height; will lerp down during UpdateAttacking
            _swoopStartY = transform.position.y;
            _swoopDamageDealt = false;

            if (_currentTarget != null)
                _swoopTargetY = _currentTarget.position.y + 3f; // just above vehicles
            else
                _swoopTargetY = 3f;

            Debug.Log("[DragonBoss] Flying attack -- swooping down!");
        }

        private void UpdateAttacking()
        {
            _stateTimer += Time.deltaTime;

            // During Drakaris, keep the fire VFX tracking the dragon's mouth
            if (_currentAnim == "Drakaris" && _activeFireVFX != null)
            {
                Vector3 firePos = transform.position + transform.forward * 5f;
                _activeFireVFX.transform.position = firePos;
            }

            // During FlyingAttack, physically swoop the dragon down then back up
            if (_currentAnim == "FlyingAttack")
            {
                float halfDuration = _attackDuration * 0.5f;

                if (_stateTimer < halfDuration)
                {
                    // Descend phase: lerp Y down toward target
                    float t = _stateTimer / halfDuration;
                    float newY = Mathf.Lerp(_swoopStartY, _swoopTargetY, t);
                    transform.position = new Vector3(transform.position.x, newY, transform.position.z);

                    // Also move forward toward the target
                    if (_currentTarget != null)
                        MoveAndFace(_currentTarget.position, huntSpeed);
                }
                else
                {
                    // Deal damage once at the bottom of the swoop
                    if (!_swoopDamageDealt)
                    {
                        _swoopDamageDealt = true;
                        Vector3 swoopCenter = transform.position + transform.forward * 6f;
                        swoopCenter.y = 0f; // ground level where vehicles are
                        DamageSystem.DealAreaDamage(swoopCenter, flyingAttackRadius, flyingAttackDamage);
                        VFXManager.DustExplosion(swoopCenter, 2f);
                        Debug.Log("[DragonBoss] Flying attack swoop hit!");
                    }

                    // Ascend phase: lerp Y back up to fly height
                    float t = (_stateTimer - halfDuration) / halfDuration;
                    float newY = Mathf.Lerp(_swoopTargetY, islandCenter.y + flyHeight, t);
                    transform.position = new Vector3(transform.position.x, newY, transform.position.z);
                }
            }

            if (_stateTimer >= _attackDuration)
            {
                CleanupFireVFX();

                // After several attacks, return to island to rest
                if (_attacksSinceRest >= 8)
                {
                    EnterState(DragonState.Returning);
                }
                else
                {
                    // Check if target is still alive to continue hunting
                    if (_currentTarget != null && IsTargetAlive(_currentTarget))
                    {
                        EnterState(DragonState.Hunting);
                    }
                    else
                    {
                        EnterState(DragonState.Flying);
                    }
                }
            }
        }

        // =================================================================
        // Returning
        // =================================================================

        private void UpdateReturning()
        {
            _stateTimer += Time.deltaTime;

            Vector3 returnTarget = new Vector3(
                islandCenter.x,
                islandCenter.y + flyHeight,
                islandCenter.z
            );

            MoveAndFace(returnTarget, patrolSpeed);

            float dist = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(islandCenter.x, 0f, islandCenter.z)
            );

            if (dist <= islandReturnThreshold)
            {
                EnterState(DragonState.Landing);
            }
        }

        // =================================================================
        // Landing
        // =================================================================

        private void UpdateLanding()
        {
            _stateTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_stateTimer / landDuration);

            // Smooth descent using ease-in curve
            float eased = t * t;
            transform.position = Vector3.Lerp(_landStart, _landEnd, eased);

            if (_stateTimer >= landDuration)
            {
                transform.position = _landEnd;
                EnterState(DragonState.Resting);
            }
        }

        // =================================================================
        // Dead
        // =================================================================

        private void UpdateDead()
        {
            _stateTimer += Time.deltaTime;

            // Accelerating gravity fall
            float fallSpeed = 9.81f * _stateTimer * _stateTimer * 0.5f;
            transform.position += Vector3.down * fallSpeed * Time.deltaTime;

            // Corkscrew spiral rotation as it falls
            float spinSpeed = 60f + _stateTimer * 40f; // accelerating spin
            transform.Rotate(
                15f * Time.deltaTime,         // slight pitch tumble
                spinSpeed * Time.deltaTime,   // fast yaw spin (corkscrew)
                45f * Time.deltaTime,         // roll
                Space.Self);

            if (_stateTimer >= 10f)
            {
                Debug.Log("[DragonBoss] Dragon corpse destroyed.");
                Destroy(gameObject);
            }
        }

        // =================================================================
        // Public damage API
        // =================================================================

        /// <summary>
        /// Deal damage to the dragon. Call from projectile hits, area effects,
        /// or any other damage source. Triggers death when HP reaches zero.
        /// </summary>
        public void TakeDamage(int amount)
        {
            if (_state == DragonState.Dead) return;
            if (amount <= 0) return;

            _currentHP -= amount;
            _currentHP = Mathf.Max(_currentHP, 0);

            Debug.Log($"[DragonBoss] Took {amount} damage. HP: {_currentHP}/{maxHP}");

            // Visual feedback
            VFXManager.Sparks(transform.position, 0.6f);

            if (_currentHP <= 0)
            {
                EnterState(DragonState.Dead);
            }
        }

        /// <summary>Current HP (read-only).</summary>
        public int CurrentHP => _currentHP;

        /// <summary>Whether the dragon is still alive.</summary>
        public bool IsAlive => _state != DragonState.Dead;

        /// <summary>Current state name for debugging / UI.</summary>
        public string CurrentStateName => _state.ToString();

        // =================================================================
        // Movement helper
        // =================================================================

        /// <summary>
        /// Move toward a world position at the given speed and smoothly
        /// rotate to face the movement direction.
        /// Avoids phasing through terrain: sphere-cast ahead and, if blocked,
        /// deflect upward + lateral around the obstacle. Also enforces a
        /// minimum altitude above whatever is directly below.
        /// </summary>
        private void MoveAndFace(Vector3 targetPos, float speed)
        {
            Vector3 direction = (targetPos - transform.position).normalized;
            float dt = Time.deltaTime;

            // why: sphere radius must be close to dragon body half-size
            const float avoidRadius = 6f;
            const float avoidLookAhead = 24f;
            const float minGroundClearance = 12f;

            Vector3 move = direction * speed * dt;

            // Three-probe avoidance: forward, forward-left, forward-right.
            // Pick the clearest direction; if all blocked, climb hard.
            Quaternion leftQ = Quaternion.AngleAxis(-35f, Vector3.up);
            Quaternion rightQ = Quaternion.AngleAxis(35f, Vector3.up);
            Vector3 dirL = leftQ * direction;
            Vector3 dirR = rightQ * direction;
            float fwdClearance = ProbeClearance(transform.position, direction, avoidRadius, avoidLookAhead);
            float leftClearance = ProbeClearance(transform.position, dirL, avoidRadius, avoidLookAhead);
            float rightClearance = ProbeClearance(transform.position, dirR, avoidRadius, avoidLookAhead);

            if (fwdClearance < avoidLookAhead)
            {
                // Forward blocked — pick best sideways alternative
                Vector3 climb = Vector3.up * (speed * dt * 1.2f);
                if (leftClearance >= rightClearance && leftClearance > fwdClearance)
                    move = dirL * speed * dt * 0.7f + climb;
                else if (rightClearance > fwdClearance)
                    move = dirR * speed * dt * 0.7f + climb;
                else
                    // All blocked — climb hard to escape vertical
                    move = Vector3.up * (speed * dt * 2.2f);
            }

            transform.position += move;

            // Altitude floor: keep the dragon above any terrain underneath it.
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit groundHit,
                    200f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!IsSelfCollider(groundHit.collider))
                {
                    float desiredY = groundHit.point.y + minGroundClearance;
                    if (transform.position.y < desiredY)
                    {
                        var p = transform.position;
                        p.y = Mathf.Lerp(p.y, desiredY, 6f * dt);
                        transform.position = p;
                    }
                }
            }

            // Face movement direction (only on XZ plane for natural flight)
            if (direction.sqrMagnitude > 0.001f)
            {
                Vector3 flatDir = new Vector3(direction.x, 0f, direction.z);
                if (flatDir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(flatDir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 5f * dt);
                }
            }
        }

        private bool IsSelfCollider(Collider c)
        {
            if (c == null) return true;
            return c.transform == transform || c.transform.IsChildOf(transform);
        }

        // Returns distance to the nearest non-self obstacle along a direction, or maxDist if clear.
        private float ProbeClearance(Vector3 origin, Vector3 dir, float radius, float maxDist)
        {
            if (Physics.SphereCast(origin, radius, dir, out RaycastHit hit, maxDist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!IsSelfCollider(hit.collider)) return hit.distance;
            }
            return maxDist;
        }

        // =================================================================
        // Target finding
        // =================================================================

        /// <summary>
        /// Finds the nearest alive vehicle using ArenaManager. Returns null
        /// if no vehicles exist or ArenaManager is unavailable.
        /// </summary>
        private Transform FindNearestAliveVehicle()
        {
            if (ArenaManager.Instance == null) return null;

            var vehicles = ArenaManager.Instance.GetVehicles();
            Transform closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < vehicles.Count; i++)
            {
                if (vehicles[i] == null || !vehicles[i].IsAlive) continue;

                float dist = Vector3.Distance(transform.position, vehicles[i].transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = vehicles[i].transform;
                }
            }

            return closest;
        }

        /// <summary>
        /// Checks whether a tracked target transform still belongs to an
        /// alive vehicle.
        /// </summary>
        private bool IsTargetAlive(Transform target)
        {
            if (target == null) return false;

            var runtime = target.GetComponent<VehicleRuntime>();
            if (runtime != null) return runtime.IsAlive;

            // Fallback: if VehicleRuntime is on a parent
            runtime = target.GetComponentInParent<VehicleRuntime>();
            return runtime != null && runtime.IsAlive;
        }

        // =================================================================
        // Cleanup
        // =================================================================

        private void CleanupFireVFX()
        {
            if (_activeFireVFX != null)
            {
                Destroy(_activeFireVFX);
                _activeFireVFX = null;
            }
        }

        private void OnDestroy()
        {
            CleanupFireVFX();
        }
    }
}
