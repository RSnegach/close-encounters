using System.Collections;
using UnityEngine;
using CloseEncounters.Combat;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Base class for all arena mobs (animals, NPCs, etc.).
    /// Handles HP, physics, vehicle/projectile interaction, death effects,
    /// AI roaming, and recovery from ragdoll states.
    /// Subclasses or per-instance config define animation clip names.
    /// </summary>
    public class Mob : MonoBehaviour
    {
        // =================================================================
        // Configuration
        // =================================================================

        [Header("Health")]
        public int maxHP = 100;

        [Header("AI Roaming")]
        public float roamRadius = 30f;
        public float walkSpeed = 3f;
        public float runSpeed = 8f;

        [Header("Animation Clips")]
        public string idleAnim = "Horse_001_idle";
        public string walkAnim = "Horse_001_walk";
        public string runAnim  = "Horse_001_run";
        public string eatAnim  = "Horse_001_eat";

        [Header("Hit Cooldown")]
        [Tooltip("Minimum seconds between registering hits from the same source.")]
        public float hitCooldown = 0.05f;

        // =================================================================
        // Runtime State
        // =================================================================

        private int _currentHP;
        private bool _isAlive = true;
        private bool _isRagdoll;
        private bool _isFleeing;
        private float _lastHitTime = -1f;

        // Roaming
        private Vector3 _spawnPosition;
        private Vector3 _roamTarget;
        private float _roamTimer;
        private bool _isRunning;

        // Components
        protected Animator _animator;
        private Rigidbody _rb;
        private Collider _collider;

        // Flee state
        private Vector3 _fleeDirection;
        private float _fleeTimer;

        // Recovery coroutine handle
        private Coroutine _recoveryRoutine;

        /// <summary>True while the mob has HP remaining.</summary>
        public bool IsAlive => _isAlive;

        /// <summary>Current HP for external reads (healthbar, etc.).</summary>
        public int CurrentHP => _currentHP;

        // =================================================================
        // Lifecycle
        // =================================================================

        protected virtual void Start()
        {
            _currentHP = maxHP;
            _spawnPosition = transform.position;
            _roamTarget = transform.position;
            _roamTimer = Random.Range(0f, 5f);

            // Animator
            _animator = GetComponentInChildren<Animator>();

            // Ensure collider
            EnsureCollider();

            // Ensure rigidbody
            EnsureRigidbody();

            // Start idle
            PlayAnim(idleAnim);
        }

        protected virtual void Update()
        {
            if (!_isAlive) return;

            // Vehicle proximity fling detection (replaces unreliable OnCollisionEnter)
            if (_isAlive && !_isRagdoll)
            {
                if (ArenaManager.Instance != null)
                {
                    var vehicles = ArenaManager.Instance.GetVehicles();
                    for (int v = 0; v < vehicles.Count; v++)
                    {
                        if (vehicles[v] == null || !vehicles[v].IsAlive) continue;
                        float dist = Vector3.Distance(transform.position, vehicles[v].transform.position);
                        if (dist < 4f && Time.time - _lastHitTime > hitCooldown)
                        {
                            var vehRb = vehicles[v].GetComponent<Rigidbody>();
                            float vehicleSpeed = vehRb != null ? vehRb.linearVelocity.magnitude : 10f;
                            if (vehicleSpeed < 2f) continue; // ignore stationary vehicles

                            float momentum = vehicleSpeed * (vehRb != null ? vehRb.mass : 100f);
                            int damage = Mathf.CeilToInt(vehicleSpeed * 5f);

                            _lastHitTime = Time.time;
                            TakeDamage(damage, transform.position);

                            if (_isAlive)
                            {
                                Vector3 flingDir = (transform.position - vehicles[v].transform.position).normalized;
                                flingDir.y = 0.7f;
                                flingDir = flingDir.normalized;
                                float flingForce = Mathf.Clamp(momentum * 0.04f, 10f, 80f);

                                _isRagdoll = false;
                                EnterRagdoll(flingDir * flingForce);
                            }
                            break;
                        }
                    }
                }
            }

            if (_isRagdoll) return;

            if (_isFleeing)
            {
                UpdateFlee();
            }
            else
            {
                UpdateRoam();
            }
        }

        // =================================================================
        // Collider Setup
        // =================================================================

        private void EnsureCollider()
        {
            _collider = GetComponent<Collider>();
            if (_collider != null) return;

            // Use MeshColliders on every child mesh for pixel-perfect hitbox
            var meshFilters = GetComponentsInChildren<MeshFilter>();
            if (meshFilters != null && meshFilters.Length > 0)
            {
                for (int i = 0; i < meshFilters.Length; i++)
                {
                    var mf = meshFilters[i];
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (mf.GetComponent<Collider>() != null) continue;

                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true; // convex required for Rigidbody interaction
                }
                _collider = GetComponentInChildren<Collider>();
            }

            // Fallback to BoxCollider if no meshes found
            if (_collider == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                var renderers = GetComponentsInChildren<Renderer>();
                if (renderers != null && renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        if (renderers[i] != null) b.Encapsulate(renderers[i].bounds);
                    box.center = transform.InverseTransformPoint(b.center);
                    var ls = transform.lossyScale;
                    box.size = new Vector3(
                        b.size.x / Mathf.Max(ls.x, 0.01f),
                        b.size.y / Mathf.Max(ls.y, 0.01f),
                        b.size.z / Mathf.Max(ls.z, 0.01f)) * 0.85f;
                }
                _collider = box;
            }
        }

        private void EnsureRigidbody()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
            {
                _rb = gameObject.AddComponent<Rigidbody>();
            }
            // Kinematic when roaming, non-kinematic when ragdolling
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.mass = 15f;
            _rb.interpolation = RigidbodyInterpolation.None;
        }

        // =================================================================
        // Health System
        // =================================================================

        /// <summary>
        /// Apply damage to the mob. Spawns hit VFX at the impact point.
        /// When HP reaches zero, triggers the death sequence.
        /// </summary>
        /// <param name="amount">Damage amount (positive integer).</param>
        /// <param name="hitPoint">World-space point of impact for VFX.</param>
        public void TakeDamage(int amount, Vector3 hitPoint)
        {
            if (!_isAlive) return;
            if (amount <= 0) return;

            _currentHP -= amount;
            if (_currentHP < 0) _currentHP = 0;

            // Hit VFX
            SpawnHitVFX(hitPoint);

            Debug.Log($"[Mob] {gameObject.name} took {amount} damage. HP: {_currentHP}/{maxHP}");

            if (_currentHP <= 0)
            {
                DieWithRedMist();
            }
            else
            {
                // Knock the mob back from the hit point (projectiles and all damage)
                Vector3 knockDir = (transform.position - hitPoint).normalized;
                knockDir.y = 0.4f;
                knockDir = knockDir.normalized;
                float knockForce = Mathf.Clamp(amount * 1.5f, 8f, 50f);

                _isRagdoll = false; // reset so EnterRagdoll can fire
                EnterRagdoll(knockDir * knockForce);
            }
        }

        /// <summary>Overload without a hit point -- uses the mob's center.</summary>
        public void TakeDamage(int amount)
        {
            TakeDamage(amount, transform.position);
        }

        private void SpawnHitVFX(Vector3 position)
        {
            // No physical VFX on rapid hits -- prevents silver sphere pile-up
        }

        // =================================================================
        // Death Sequence
        // =================================================================

        private void DieWithRedMist()
        {
            _isAlive = false;

            // Stop any recovery in progress
            if (_recoveryRoutine != null)
            {
                StopCoroutine(_recoveryRoutine);
                _recoveryRoutine = null;
            }

            Vector3 center = transform.position + Vector3.up;

            // 8 brain chunk bursts with small particles
            var chunkPrefab = Resources.Load<GameObject>("VFX/Blood/BrainChunks");
            if (chunkPrefab != null)
            {
                for (int c = 0; c < 10; c++)
                {
                    float angle = (360f / 10) * c * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), Random.Range(0.2f, 1f), Mathf.Sin(angle)) * 0.5f;
                    var chunks = Object.Instantiate(chunkPrefab, center + offset, Random.rotation);
                    chunks.transform.localScale = Vector3.one * 2f;
                    // Color bright red
                    foreach (var ps in chunks.GetComponentsInChildren<ParticleSystem>())
                    {
                        var main = ps.main;
                        main.startColor = new Color(0.9f, 0.05f, 0.05f, 1f);
                    }
                    foreach (var rend in chunks.GetComponentsInChildren<Renderer>())
                    {
                        foreach (var mat in rend.materials)
                            if (mat != null) mat.color = new Color(0.9f, 0.05f, 0.05f, 1f);
                    }
                    CityPrefabHelper.FixURPMaterials(chunks.transform);
                    Object.Destroy(chunks, 1f);
                }
            }

            // Cinematic explosion for impact feel
            VFXManager.SmallExplosion(center, 1f);

            // Destroy the mob instantly
            Object.Destroy(gameObject, 0.05f);
        }

        // =================================================================
        // Vehicle Collision
        // =================================================================

        private void OnCollisionEnter(Collision collision)
        {
            if (!_isAlive) return;
            if (collision == null) return;

            // Debounce rapid multi-hits
            if (Time.time - _lastHitTime < hitCooldown) return;

            // Check for projectile hit (solid collider fallback)
            var proj = collision.collider != null
                ? collision.collider.GetComponent<Projectile>()
                : null;
            if (proj == null && collision.collider != null)
                proj = collision.collider.GetComponentInParent<Projectile>();

            if (proj != null)
            {
                _lastHitTime = Time.time;
                Vector3 contactPoint = collision.contactCount > 0
                    ? collision.GetContact(0).point
                    : transform.position;
                TakeDamage(proj.damage, contactPoint);
                Object.Destroy(proj.gameObject);
            }
        }

        // =================================================================
        // Projectile Trigger Detection
        // =================================================================

        private void OnTriggerEnter(Collider other)
        {
            if (!_isAlive) return;
            if (other == null) return;

            // Debounce rapid multi-hits
            if (Time.time - _lastHitTime < hitCooldown) return;

            Projectile proj = other.GetComponent<Projectile>();
            if (proj == null) proj = other.GetComponentInParent<Projectile>();
            if (proj == null) return;

            _lastHitTime = Time.time;

            Vector3 hitPoint = other.ClosestPoint(transform.position);
            TakeDamage(proj.damage, hitPoint);

            // Destroy the projectile on hit so it doesn't pass through
            Object.Destroy(proj.gameObject);
        }

        // =================================================================
        // Ragdoll / Recovery System
        // =================================================================

        /// <summary>
        /// Enter ragdoll state: disable AI, enable physics, apply impulse.
        /// After 3 seconds, attempt recovery if still alive.
        /// </summary>
        private void EnterRagdoll(Vector3 impulse)
        {
            if (_isRagdoll) return;
            _isRagdoll = true;
            _isFleeing = false;

            // Stop any active recovery
            if (_recoveryRoutine != null)
            {
                StopCoroutine(_recoveryRoutine);
                _recoveryRoutine = null;
            }

            // Enable physics
            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
                _rb.linearDamping = 0.5f;
                _rb.angularDamping = 0.5f;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.AddForce(impulse, ForceMode.VelocityChange);
            }

            // Start recovery timer
            _recoveryRoutine = StartCoroutine(RecoveryRoutine());
        }

        /// <summary>
        /// After 3 seconds of ragdoll, smoothly rotate upright over 1 second
        /// and resume roaming AI.
        /// </summary>
        private IEnumerator RecoveryRoutine()
        {
            // Wait for ragdoll duration
            yield return new WaitForSeconds(3f);

            if (!_isAlive)
            {
                _recoveryRoutine = null;
                yield break;
            }

            // Clamp angular velocity to near-zero to stop wild spinning
            if (_rb != null)
            {
                _rb.angularVelocity *= 0.1f;
            }

            // Wait until close to the ground before uprighting
            float groundWaitTimeout = 5f;
            float groundWaitElapsed = 0f;
            while (groundWaitElapsed < groundWaitTimeout)
            {
                if (!_isAlive)
                {
                    _recoveryRoutine = null;
                    yield break;
                }

                // Raycast down to check if near ground
                if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit groundHit, 2f))
                {
                    break; // close enough to the ground
                }

                groundWaitElapsed += Time.deltaTime;
                yield return null;
            }

            // Stop physics movement
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }

            // Smoothly rotate back to upright over 1 second
            float elapsed = 0f;
            float duration = 1f;
            Quaternion startRot = transform.rotation;
            // Keep the Y rotation (facing direction) but remove X and Z tilt
            float yAngle = transform.eulerAngles.y;
            Quaternion targetRot = Quaternion.Euler(0f, yAngle, 0f);

            while (elapsed < duration)
            {
                if (!_isAlive)
                {
                    _recoveryRoutine = null;
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
                yield return null;
            }

            transform.rotation = targetRot;

            // Resume roaming
            _isRagdoll = false;
            _isFleeing = false;
            _roamTimer = Random.Range(1f, 3f);
            PlayAnim(idleAnim);

            _recoveryRoutine = null;
        }

        // =================================================================
        // AI Roaming
        // =================================================================

        private void UpdateRoam()
        {
            _roamTimer -= Time.deltaTime;

            // Pick new target periodically
            if (_roamTimer <= 0f)
            {
                PickNewRoamTarget();
            }

            // Move toward target
            Vector3 toTarget = _roamTarget - transform.position;
            toTarget.y = 0f;

            if (toTarget.magnitude > 1f)
            {
                float speed = _isRunning ? runSpeed : walkSpeed;
                Vector3 dir = toTarget.normalized;
                transform.position += dir * speed * Time.deltaTime;

                // Face movement direction
                Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, look, 3f * Time.deltaTime);
            }
            else
            {
                // Arrived at target, idle or eat
                if (_isRunning)
                {
                    _isRunning = false;
                    PlayAnim(Random.value > 0.5f ? idleAnim : eatAnim);
                }
            }
        }

        private void PickNewRoamTarget()
        {
            Vector2 rng = Random.insideUnitCircle * roamRadius;
            _roamTarget = _spawnPosition + new Vector3(rng.x, 0f, rng.y);
            _roamTarget.y = transform.position.y;
            _roamTimer = Random.Range(4f, 12f);
            _isRunning = Random.value > 0.6f;

            // Switch animation
            if (_isRunning)
                PlayAnim(runAnim);
            else if (Random.value > 0.3f)
                PlayAnim(walkAnim);
            else
                PlayAnim(eatAnim);
        }

        // =================================================================
        // Flee Behavior
        // =================================================================

        /// <summary>
        /// Flee away from the damage source for 5 seconds at run speed.
        /// </summary>
        private void StartFlee(Vector3 damageSource)
        {
            if (_isRagdoll) return;

            _isFleeing = true;
            _fleeTimer = 5f;

            // Run away from damage source
            _fleeDirection = transform.position - damageSource;
            _fleeDirection.y = 0f;
            if (_fleeDirection.sqrMagnitude < 0.01f)
                _fleeDirection = Random.insideUnitSphere;
            _fleeDirection = _fleeDirection.normalized;

            PlayAnim(runAnim);
        }

        private void UpdateFlee()
        {
            _fleeTimer -= Time.deltaTime;

            if (_fleeTimer <= 0f)
            {
                // Stop fleeing, resume roaming
                _isFleeing = false;
                _roamTimer = Random.Range(1f, 3f);
                PlayAnim(idleAnim);
                return;
            }

            // Run in flee direction
            transform.position += _fleeDirection * runSpeed * Time.deltaTime;

            // Face flee direction
            if (_fleeDirection.sqrMagnitude > 0.01f)
            {
                Quaternion look = Quaternion.LookRotation(_fleeDirection, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, look, 5f * Time.deltaTime);
            }
        }

        // =================================================================
        // Animation
        // =================================================================

        /// <summary>
        /// Play an animation clip by name on the mob's Animator.
        /// Safe to call with null animator or empty clip name.
        /// </summary>
        protected void PlayAnim(string clipName)
        {
            if (_animator == null) return;
            if (string.IsNullOrEmpty(clipName)) return;

            _animator.Play(clipName, 0, 0f);
        }
    }
}
