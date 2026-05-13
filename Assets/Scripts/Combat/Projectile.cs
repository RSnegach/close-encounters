using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Arena;
using CloseEncounters.VehiclePhysics;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// The five projectile flight behaviors.
    /// </summary>
    public enum FlightType
    {
        Straight,
        Ballistic,
        Guided,
        Laser,
        Mine
    }

    /// <summary>
    /// Projectile MonoBehaviour handling movement, collision, trail effects,
    /// and static factory spawning for all weapon types.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        // --- Config ---
        public FlightType flightType;
        public int damage;
        public float speed;
        public float lifetime;
        public float triggerRadius;
        public int ownerPlayerId = -1;

        // --- Ballistic ---
        public float gravity = 9.81f;

        // --- Guided ---
        public Vector3 aimTarget;
        public float guidanceLerp = 3f;

        // --- Laser ---
        public float laserDuration = 0.08f;

        // --- Mine ---
        public float armDelay = 1f;

        // --- Internal ---
        private Vector3 _velocity;
        private Vector3 _prevPosition;
        private float _timer;
        private bool _armed = true;
        private bool _hasDetonated;
        private TrailRenderer _trail;
        private ParticleSystem _trailParticles;
        private LineRenderer _laserLine;
        private SphereCollider _triggerCollider;
        private GameObject _modelInstance;
        private GameObject _mineLight;
        private string _weaponId;

        // --- Mine buoyancy ---
        private bool _mineOnWater;
        private float _mineBobPhase;

        // =====================================================================
        // Pool (static) — per-shot GC was multi-KB/frame at MG fire rates.
        // Keyed by weaponId so each weapon's cached model/collider is reused.
        // =====================================================================
        private static readonly Dictionary<string, Stack<Projectile>> s_pool
            = new Dictionary<string, Stack<Projectile>>();
        private static readonly Dictionary<string, GameObject> s_prefabCache
            = new Dictionary<string, GameObject>();
        private static Material s_particleUnlitMat;
        private static Material s_urpLitMat;
        private static Shader s_particleUnlitShader;
        private static Shader s_urpLitShader;

        private static Shader GetParticleUnlitShader()
        {
            if (s_particleUnlitShader == null)
                s_particleUnlitShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            return s_particleUnlitShader;
        }

        private static Shader GetUrpLitShader()
        {
            if (s_urpLitShader == null)
                s_urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
            return s_urpLitShader;
        }

        private static GameObject LoadPrefabCached(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!s_prefabCache.TryGetValue(path, out var prefab))
            {
                prefab = Resources.Load<GameObject>(path);
                s_prefabCache[path] = prefab;
            }
            return prefab;
        }

        // =====================================================================
        // Setup
        // =====================================================================

        /// <summary>
        /// Called after instantiation to initialize the projectile's flight.
        /// </summary>
        public void Init(Vector3 direction, Vector3 startPos)
        {
            transform.position = startPos;
            _prevPosition = startPos;
            _velocity = direction.normalized * speed;
            _timer = 0f;
            _hasDetonated = false;

            switch (flightType)
            {
                case FlightType.Straight:
                case FlightType.Ballistic:
                case FlightType.Guided:
                    SetupPhysicalProjectile();
                    break;
                case FlightType.Laser:
                    SetupLaser(startPos, direction);
                    break;
                case FlightType.Mine:
                    SetupMine();
                    break;
            }
        }

        private void EnsureTriggerCollider(float radius)
        {
            if (_triggerCollider == null)
                _triggerCollider = gameObject.AddComponent<SphereCollider>();
            _triggerCollider.isTrigger = true;
            _triggerCollider.radius = radius;
            _triggerCollider.enabled = true;
        }

        private void SetupPhysicalProjectile()
        {
            // Trigger collider for hit detection
            EnsureTriggerCollider(0.3f);

            // Visual: try weapon-specific prefab first, fall back to sphere
            string missilePrefabPath = GetMissilePrefabPath();
            bool prefabLoaded = false;

            if (missilePrefabPath != null)
            {
                var prefab = LoadPrefabCached(missilePrefabPath);
                if (prefab != null)
                {
                    var instance = Object.Instantiate(prefab, transform);
                    instance.name = "ProjectileModel";
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localScale = Vector3.one * GetProjectileModelScale();
                    instance.transform.localRotation = Quaternion.identity;
                    _modelInstance = instance;

                    // Remove colliders from prefab
                    var prefabCols = instance.GetComponentsInChildren<Collider>();
                    for (int c = 0; c < prefabCols.Length; c++)
                        Object.DestroyImmediate(prefabCols[c]);

                    // For bullets: shrink particle effects to near-invisible
                    bool isBullet = (_weaponId == "machine_gun" || _weaponId == "autocannon"
                        || _weaponId == "swivel_cannon" || _weaponId == "wing_cannon");
                    if (isBullet)
                    {
                        // Remove all particle/trail effects — just the solid bullet
                        var particles = instance.GetComponentsInChildren<ParticleSystem>();
                        for (int p = 0; p < particles.Length; p++)
                            particles[p].gameObject.SetActive(false);
                        var trails = instance.GetComponentsInChildren<TrailRenderer>();
                        for (int t = 0; t < trails.Length; t++)
                            trails[t].enabled = false;
                    }

                    // Fix URP materials
                    FixURPMaterials(instance);
                    prefabLoaded = true;
                }
            }

            if (!prefabLoaded)
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.name = "ProjectileVisual";
                visual.transform.SetParent(transform, false);
                _modelInstance = visual;
                // Small tracer round, not a big silver ball
                bool isBullet = (_weaponId == "machine_gun" || _weaponId == "autocannon"
                    || _weaponId == "swivel_cannon" || _weaponId == "wing_cannon");
                visual.transform.localScale = Vector3.one * (isBullet ? 0.15f : 0.4f);
                SetProjectileColor(visual, GetProjectileColor());
                Object.DestroyImmediate(visual.GetComponent<Collider>());
            }

            // Trail
            CreateTrailParticles();

            // VFX: attach rocket/smoke trail to missiles, torpedoes, and rockets
            if (flightType == FlightType.Guided
                || _weaponId == "rocket" || _weaponId == "rocket_pod")
            {
                VFXManager.AttachRocketTrail(transform, 0.5f);
            }
        }

        private void SetupLaser(Vector3 origin, Vector3 direction)
        {
            _armed = true;
            lifetime = laserDuration;

            // Raycast for hit
            RaycastHit hit = default;
            float maxRange = 500f;
            Vector3 endPoint;

            // Raycast, skipping self (loop through hits to find first non-self)
            RaycastHit[] hits = Physics.RaycastAll(origin, direction.normalized, maxRange);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            bool didHit = false;
            for (int h = 0; h < hits.Length; h++)
            {
                // Skip owner vehicle
                var hitVr = hits[h].collider.GetComponentInParent<VehicleRuntime>();
                if (hitVr != null && hitVr.PlayerId == ownerPlayerId) continue;

                hit = hits[h];
                didHit = true;
                break;
            }

            if (didHit)
            {
                endPoint = hit.point;
                HandleHit(hit.collider, hit.point);

                // VFX: hitscan impact effect -- only for slow-firing weapons
                if (_weaponId == "railgun")
                    VFXManager.PlasmaExplosion(endPoint);
                // Skip impact VFX for rapid-fire beams (laser, milk_gun) to prevent silver sphere pile-up
            }
            else
            {
                endPoint = origin + direction.normalized * maxRange;
            }

            // Line renderer for visual
            if (_laserLine == null)
                _laserLine = gameObject.AddComponent<LineRenderer>();
            _laserLine.enabled = true;
            _laserLine.positionCount = 2;
            _laserLine.SetPosition(0, origin);
            _laserLine.SetPosition(1, endPoint);
            _laserLine.startWidth = 0.15f;
            _laserLine.endWidth = 0.08f;
            if (s_particleUnlitMat == null)
                s_particleUnlitMat = new Material(GetParticleUnlitShader());
            _laserLine.sharedMaterial = s_particleUnlitMat;
            _laserLine.startColor = new Color(1f, 0.3f, 0.1f, 1f);
            _laserLine.endColor = new Color(1f, 0.1f, 0.0f, 0.5f);

            // Cum Gun: small white beam with Water_Beam VFX (Cum Beam)
            if (_weaponId == "milk_gun")
            {
                var beamPrefab = LoadPrefabCached("VFX/MilkBeam");
                if (beamPrefab != null)
                {
                    var beam = Instantiate(beamPrefab);
                    beam.name = "CumBeamVFX";
                    beam.transform.position = origin;
                    // Point horizontally toward the endpoint
                    Vector3 beamDir = (endPoint - origin);
                    beamDir.y = 0f; // force horizontal
                    if (beamDir.sqrMagnitude > 0.01f)
                        beam.transform.rotation = Quaternion.LookRotation(beamDir.normalized);
                    else
                        beam.transform.rotation = Quaternion.LookRotation(direction.normalized);
                    float beamLen = Vector3.Distance(origin, endPoint);
                    beam.transform.localScale = Vector3.one * 0.8f; // visible beam

                    // Recolor white
                    foreach (var ps in beam.GetComponentsInChildren<ParticleSystem>())
                    {
                        var main = ps.main;
                        main.startColor = Color.white;
                        main.startSize = new ParticleSystem.MinMaxCurve(main.startSize.constant * 0.3f);
                    }
                    foreach (var rend in beam.GetComponentsInChildren<Renderer>())
                    {
                        foreach (var mat in rend.materials)
                            if (mat != null) mat.color = Color.white;
                    }

                    Destroy(beam, 0.3f);
                }

                // Small white impact
                var impactPrefab = LoadPrefabCached("VFX/MilkImpact");
                if (impactPrefab != null && hit.collider != null)
                {
                    var impact = Instantiate(impactPrefab, endPoint, Quaternion.identity);
                    impact.transform.localScale = Vector3.one * 0.3f;
                    foreach (var ps in impact.GetComponentsInChildren<ParticleSystem>())
                    {
                        var main = ps.main;
                        main.startColor = Color.white;
                    }
                    Destroy(impact, 1f);
                }

                // Wide white beam
                _laserLine.startColor = new Color(1f, 1f, 1f, 0.95f);
                _laserLine.endColor = new Color(0.9f, 0.95f, 1f, 0.5f);
                _laserLine.startWidth = 0.4f;
                _laserLine.endWidth = 0.2f;
            }
        }

        private void SetupMine()
        {
            _armed = false;

            // Visual: use SpikeBall model from Ball Pack, fall back to dark sphere
            var spikePrefab = LoadPrefabCached("Models/Missiles/Missile_SpikeBall");
            if (spikePrefab != null)
            {
                var instance = Object.Instantiate(spikePrefab, transform);
                instance.name = "MineModel";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localScale = Vector3.one * 0.6f;
                instance.transform.localRotation = Quaternion.identity;
                _modelInstance = instance;

                // Remove colliders from the prefab
                var prefabCols = instance.GetComponentsInChildren<Collider>();
                for (int c = 0; c < prefabCols.Length; c++)
                    Object.DestroyImmediate(prefabCols[c]);

                FixURPMaterials(instance);
            }
            else
            {
                // Fallback: dark sphere
                var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.name = "MineVisual";
                visual.transform.SetParent(transform, false);
                visual.transform.localScale = Vector3.one * 0.8f;
                _modelInstance = visual;
                SetProjectileColor(visual, new Color(0.3f, 0.3f, 0.3f));
                Object.DestroyImmediate(visual.GetComponent<Collider>());
            }

            // Blinking light
            var light = new GameObject("MineLight");
            light.transform.SetParent(transform, false);
            light.transform.localPosition = Vector3.up * 0.5f;
            var lt = light.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = Color.red;
            lt.intensity = 2f;
            lt.range = 3f;
            _mineLight = light;

            // Trigger collider
            EnsureTriggerCollider(triggerRadius);

            // Determine whether we are on water or ground
            _mineOnWater = (WaveManager.Instance != null);
            _mineBobPhase = Random.Range(0f, Mathf.PI * 2f); // random phase so mines don't bob in sync

            if (_mineOnWater)
            {
                // Place mine at water surface height
                float waterY = WaveManager.Instance.GetWaterHeight(transform.position);
                transform.position = new Vector3(transform.position.x, waterY, transform.position.z);
            }

            // Stop all movement
            _velocity = Vector3.zero;
        }

        // =====================================================================
        // Update
        // =====================================================================

        private void Update()
        {
            _timer += Time.deltaTime;

            switch (flightType)
            {
                case FlightType.Straight:
                    UpdateStraight();
                    break;
                case FlightType.Ballistic:
                    UpdateBallistic();
                    break;
                case FlightType.Guided:
                    UpdateGuided();
                    break;
                case FlightType.Laser:
                    UpdateLaser();
                    break;
                case FlightType.Mine:
                    UpdateMine();
                    break;
            }

            // Lifetime expiry
            if (_timer >= lifetime && !_hasDetonated)
            {
                if (flightType == FlightType.Mine)
                {
                    Detonate();
                }
                else
                {
                    _hasDetonated = true;
                    ReturnToPool();
                }
            }
        }

        private void UpdateStraight()
        {
            _prevPosition = transform.position;
            transform.position += _velocity * Time.deltaTime;
            if (_velocity.sqrMagnitude > 0.01f)
                transform.forward = _velocity.normalized;
            RaycastSweep();
        }

        private void UpdateBallistic()
        {
            _prevPosition = transform.position;
            _velocity += Vector3.down * gravity * Time.deltaTime;
            transform.position += _velocity * Time.deltaTime;
            if (_velocity.sqrMagnitude > 0.01f)
                transform.forward = _velocity.normalized;
            RaycastSweep();
        }

        private void UpdateGuided()
        {
            _prevPosition = transform.position;
            Vector3 toTarget = (aimTarget - transform.position).normalized;
            _velocity = Vector3.Lerp(_velocity.normalized, toTarget, guidanceLerp * Time.deltaTime).normalized * speed;
            transform.position += _velocity * Time.deltaTime;
            if (_velocity.sqrMagnitude > 0.01f)
                transform.forward = _velocity.normalized;
            RaycastSweep();
        }

        /// <summary>
        /// Raycast from previous position to current position to catch fast
        /// projectiles that teleport through colliders in a single frame.
        /// </summary>
        private void RaycastSweep()
        {
            if (_hasDetonated) return;

            Vector3 delta = transform.position - _prevPosition;
            float dist = delta.magnitude;
            if (dist < 0.01f) return;

            if (Physics.Raycast(_prevPosition, delta.normalized, out RaycastHit hit, dist,
                ~0, QueryTriggerInteraction.Ignore))
            {
                // Don't hit self (owner vehicle) in the first 0.5s
                var vr = hit.collider.GetComponentInParent<VehicleRuntime>();
                if (vr != null && vr.PlayerId == ownerPlayerId && _timer < 0.5f)
                    return;

                _hasDetonated = true;
                transform.position = hit.point;
                HandleHit(hit.collider, hit.point);
                ReturnToPool();
            }
        }

        private void UpdateLaser()
        {
            // Fade line renderer
            if (_laserLine != null)
            {
                float alpha = 1f - (_timer / lifetime);
                Color c = _laserLine.startColor;
                c.a = alpha;
                _laserLine.startColor = c;
                c = _laserLine.endColor;
                c.a = alpha * 0.5f;
                _laserLine.endColor = c;
                _laserLine.startWidth = 0.15f * alpha;
                _laserLine.endWidth = 0.08f * alpha;
            }
        }

        private void UpdateMine()
        {
            // Arm after delay
            if (!_armed && _timer >= armDelay)
            {
                _armed = true;
            }

            // Blink mine light
            var light = GetComponentInChildren<Light>();
            if (light != null)
            {
                light.enabled = (Mathf.FloorToInt(_timer * 4f) % 2 == 0);
            }

            // Water buoyancy: bob on the wave surface
            if (_mineOnWater && WaveManager.Instance != null)
            {
                float waterY = WaveManager.Instance.GetWaterHeight(transform.position);

                // Add a small sinusoidal bob so the mine looks naturally adrift
                float bob = Mathf.Sin(_timer * 1.5f + _mineBobPhase) * 0.15f;
                transform.position = new Vector3(transform.position.x, waterY + bob, transform.position.z);

                // Tilt the mine to match the wave surface normal
                Vector3 normal = WaveManager.Instance.GetSurfaceNormal(
                    transform.position.x, transform.position.z);
                Quaternion targetRot = Quaternion.FromToRotation(Vector3.up, normal);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 3f * Time.deltaTime);
            }
        }

        // =====================================================================
        // Collision
        // =====================================================================

        private void OnTriggerEnter(Collider other)
        {
            if (_hasDetonated) return;

            // Mines only trigger when armed
            if (flightType == FlightType.Mine && !_armed) return;

            // Don't hit self
            var vr = other.GetComponentInParent<VehicleRuntime>();
            if (vr != null && vr.PlayerId == ownerPlayerId && _timer < 0.5f) return;

            if (flightType == FlightType.Mine)
            {
                HandleHit(other, transform.position);
                Detonate();
            }
            else
            {
                _hasDetonated = true;
                HandleHit(other, transform.position);
                ReturnToPool();
            }
        }

        private VehicleRuntime FindOwnerVehicle()
        {
            if (ArenaManager.Instance == null) return null;
            var vehicles = ArenaManager.Instance.GetVehicles();
            for (int i = 0; i < vehicles.Count; i++)
                if (vehicles[i] != null && vehicles[i].PlayerId == ownerPlayerId)
                    return vehicles[i];
            return null;
        }

        private void HandleHit(Collider hitCollider, Vector3 hitPoint)
        {
            if (hitCollider == null) return;

            // Damage vehicle — no spark VFX on impact
            var vehicle = hitCollider.GetComponentInParent<VehicleRuntime>();
            if (vehicle != null && vehicle.IsAlive)
            {
                var attacker = FindOwnerVehicle();
                if (attacker != null)
                    attacker.ShotsHit++;
                DamageSystem.DealDamageToVehicle(vehicle, damage, hitPoint, attacker);
                return;
            }

            // Damage iceberg
            var iceberg = hitCollider.GetComponent<Iceberg>();
            if (iceberg == null) iceberg = hitCollider.GetComponentInParent<Iceberg>();
            if (iceberg != null)
            {
                iceberg.TakeDamage(damage);
                return;
            }

            // Break props on impact
            var prop = hitCollider.GetComponent<BreakableProp>();
            if (prop == null) prop = hitCollider.GetComponentInParent<BreakableProp>();
            if (prop != null)
            {
                Vector3 force = _velocity.normalized * damage * 0.3f;
                force.y = Mathf.Abs(force.y) + 2f;
                prop.BreakFree(force);
                return;
            }

            // Damage dragon boss
            var dragonHP = hitCollider.GetComponent<DragonHealth>();
            if (dragonHP == null) dragonHP = hitCollider.GetComponentInParent<DragonHealth>();
            if (dragonHP != null)
            {
                dragonHP.TakeDamage(damage);
                return;
            }

            // Damage arena mobs (horses, etc.)
            var mob = hitCollider.GetComponent<Mob>();
            if (mob == null) mob = hitCollider.GetComponentInParent<Mob>();
            if (mob != null)
            {
                mob.TakeDamage(damage, hitPoint);
                return;
            }

            // Damage chain-triggered base installations + hostile units
            var ammo = hitCollider.GetComponentInParent<AmmoDumpDetonator>();
            if (ammo != null) { ammo.TakeDamage(damage); return; }
            var fuel = hitCollider.GetComponentInParent<FuelDepotDetonator>();
            if (fuel != null) { fuel.TakeDamage(damage); return; }
            var mine = hitCollider.GetComponentInParent<SeaMine>();
            if (mine != null) { mine.TakeDamage(damage, hitPoint); return; }
            var turret = hitCollider.GetComponentInParent<ShoreTurret>();
            if (turret != null) { turret.TakeDamage(damage, hitPoint); return; }
            var warship = hitCollider.GetComponentInParent<Warship>();
            if (warship != null) { warship.TakeDamage(damage, hitPoint); return; }
        }

        private void Detonate()
        {
            if (_hasDetonated) return;
            _hasDetonated = true;

            DamageSystem.DealAreaDamage(transform.position, triggerRadius, damage);
            DamageSystem.SpawnExplosionFX(transform.position, 2f);

            // VFX: ParticlePack explosion for mine detonation
            VFXManager.SmallExplosion(transform.position, 2f);

            // Also damage icebergs in radius
            DamageSystem.DamageIcebergAt(transform.position, damage, triggerRadius);

            ReturnToPool();
        }

        // =====================================================================
        // Pool return
        // =====================================================================

        private void ReturnToPool()
        {
            string key = _weaponId ?? "";

            // Reset transient state
            _timer = 0f;
            _hasDetonated = false;
            _velocity = Vector3.zero;
            _armed = true;
            _mineOnWater = false;
            aimTarget = Vector3.zero;
            ownerPlayerId = -1;

            // Zero the config so Spawn re-applies it (two weapons may share a prefab/flightType
            // with different speed/damage/lifetime).
            damage = 0;
            speed = 0f;
            lifetime = 0f;
            triggerRadius = 0f;
            gravity = 9.81f;
            guidanceLerp = 3f;
            laserDuration = 0.08f;
            armDelay = 1f;

            // Visual/component cleanup
            if (_laserLine != null)
            {
                _laserLine.enabled = false;
                _laserLine.positionCount = 0;
            }

            if (_trailParticles != null)
            {
                _trailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _trailParticles.Clear(true);
            }

            if (_modelInstance != null)
            {
                Object.Destroy(_modelInstance);
                _modelInstance = null;
            }

            if (_mineLight != null)
            {
                Object.Destroy(_mineLight);
                _mineLight = null;
            }

            // Disable and requeue
            gameObject.SetActive(false);

            if (!s_pool.TryGetValue(key, out var stack))
            {
                stack = new Stack<Projectile>();
                s_pool[key] = stack;
            }
            stack.Push(this);
        }

        private void OnDisable()
        {
            // Stop trail emission without destroying the particle GO
            if (_trailParticles != null)
                _trailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // =====================================================================
        // VFX Helpers
        // =====================================================================

        private void CreateTrailParticles()
        {
            if (_trailParticles == null)
            {
                var trailObj = new GameObject("Trail");
                trailObj.transform.SetParent(transform, false);
                _trailParticles = trailObj.AddComponent<ParticleSystem>();
            }
            else
            {
                _trailParticles.gameObject.SetActive(true);
                _trailParticles.transform.localPosition = Vector3.zero;
            }

            var main = _trailParticles.main;
            main.startLifetime = 0.4f;
            main.startSpeed = 0f;
            main.startSize = 0.2f;
            main.startColor = GetProjectileColor() * 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 50;

            var emission = _trailParticles.emission;
            emission.rateOverTime = 30f;
            emission.enabled = true;

            var sol = _trailParticles.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var col = _trailParticles.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            Color projColor = GetProjectileColor();
            g.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(projColor, 0f),
                    new GradientColorKey(projColor * 0.5f, 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0.8f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            col.color = g;

            var renderer = _trailParticles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                if (s_particleUnlitMat == null)
                    s_particleUnlitMat = new Material(GetParticleUnlitShader());
                renderer.sharedMaterial = s_particleUnlitMat;
            }

            _trailParticles.Clear(true);
            _trailParticles.Play(true);
        }

        private Color GetProjectileColor()
        {
            switch (flightType)
            {
                case FlightType.Straight:  return new Color(1f, 0.9f, 0.3f);   // yellow tracer
                case FlightType.Ballistic: return new Color(0.9f, 0.5f, 0.1f);  // orange shell
                case FlightType.Guided:    return new Color(1f, 0.4f, 0.2f);    // red-orange rocket
                case FlightType.Laser:     return new Color(1f, 0.1f, 0.0f);    // red laser
                case FlightType.Mine:      return new Color(0.3f, 0.3f, 0.3f);  // dark mine
                default:                   return Color.white;
            }
        }

        private static void SetProjectileColor(GameObject obj, Color color)
        {
            var rend = obj.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                rend.material = new Material(GetUrpLitShader());
                rend.material.color = color;
                rend.material.EnableKeyword("_EMISSION");
                rend.material.SetColor("_EmissionColor", color * 0.5f);
            }
        }

        private static void SpawnImpactSpark(Vector3 point)
        {
            VFXManager.Sparks(point, 0.5f);
        }

        /// <summary>Map weapon ID to a missile model prefab path in Resources.</summary>
        private string GetMissilePrefabPath()
        {
            switch (_weaponId)
            {
                case "machine_gun":
                case "autocannon":
                case "swivel_cannon":
                case "wing_cannon":
                    return null; // use default sphere — no prefab

                case "missile":
                case "missile_launcher":
                    return "Models/Missiles/Missile_Guided";

                case "rocket":
                case "rocket_pod":
                    return "Models/Missiles/Missile_Rocket";

                case "torpedo_launcher":
                    return "Models/Missiles/Missile_Torpedo";

                case "side_missile":
                    return "Models/Missiles/Missile_Side";

                case "heavy_cannon":
                case "deck_gun":
                    return "Models/Missiles/Missile_Shell";

                case "broadside_cannon":
                case "hull_gun":
                    return "Models/Missiles/Missile_BombBall";

                default:
                    return null; // use default sphere
            }
        }

        /// <summary>Scale for each projectile model type.</summary>
        private float GetProjectileModelScale()
        {
            switch (_weaponId)
            {
                case "machine_gun":
                case "autocannon":
                case "swivel_cannon":
                case "wing_cannon":
                    return 0.6f; // bullet

                case "missile":
                case "missile_launcher":
                    return 0.3f; // guided missile

                case "rocket":
                case "rocket_pod":
                    return 0.3f; // unguided rocket

                case "torpedo_launcher":
                    return 0.35f; // torpedo

                case "side_missile":
                    return 0.25f; // side missile

                case "heavy_cannon":
                case "deck_gun":
                    return 0.3f; // shell

                case "broadside_cannon":
                case "hull_gun":
                    return 0.35f; // bomb ball

                default:
                    return 0.3f;
            }
        }

        /// <summary>Fix non-URP materials on an instantiated prefab.</summary>
        private static void FixURPMaterials(GameObject instance)
        {
            var urpShader = GetUrpLitShader();
            if (urpShader == null) return;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && !mats[m].shader.name.Contains("Universal"))
                    {
                        Color c = mats[m].HasProperty("_Color") ? mats[m].color : Color.gray;
                        Texture tex = mats[m].HasProperty("_MainTex") ? mats[m].mainTexture : null;
                        mats[m] = new Material(urpShader);
                        mats[m].color = c;
                        if (tex != null) mats[m].mainTexture = tex;
                    }
                }
                renderers[i].materials = mats;
            }
        }

        // =====================================================================
        // Static Factory: Spawn()
        // =====================================================================

        /// <summary>
        /// Factory method that creates and configures a Projectile for a given weapon ID.
        /// Returns the initialized Projectile component.
        /// </summary>
        /// <param name="weaponId">Weapon type identifier string.</param>
        /// <param name="origin">Spawn position.</param>
        /// <param name="direction">Initial fire direction.</param>
        /// <param name="aimPoint">Target point for guided weapons.</param>
        /// <param name="ownerId">Player ID of the shooter (-1 = neutral).</param>
        public static Projectile Spawn(string weaponId, Vector3 origin, Vector3 direction,
            Vector3 aimPoint = default, int ownerId = -1)
        {
            string wid = weaponId != null ? weaponId.ToLowerInvariant() : "";

            // Pool first: full GameObject construction (+SphereCollider +LineRenderer
            // +trail ParticleSystem +Shader.Find +new Material) was multi-KB GC per shot
            // at machine-gun fire rates. Reuse the same instance per weaponId.
            Projectile proj = null;
            if (s_pool.TryGetValue(wid, out var stack) && stack.Count > 0)
            {
                proj = stack.Pop();
                if (proj == null)
                {
                    // Scene unload destroyed the pooled instance; fall through to fresh build.
                }
                else
                {
                    proj.gameObject.SetActive(true);
                    proj.gameObject.name = $"Projectile_{wid}";
                }
            }

            if (proj == null)
            {
                var obj = new GameObject($"Projectile_{wid}");
                proj = obj.AddComponent<Projectile>();
            }

            proj.ownerPlayerId = ownerId;
            proj._weaponId = wid;

            switch (wid)
            {
                case "machine_gun":
                    proj.flightType = FlightType.Straight;
                    proj.damage = 10;
                    proj.speed = 200f;
                    proj.lifetime = 2f;
                    proj.triggerRadius = 0.3f;
                    break;

                case "autocannon":
                    proj.flightType = FlightType.Straight;
                    proj.damage = 25;
                    proj.speed = 150f;
                    proj.lifetime = 3f;
                    proj.triggerRadius = 0.5f;
                    break;

                case "heavy_cannon":
                    proj.flightType = FlightType.Ballistic;
                    proj.damage = 60;
                    proj.speed = 80f;
                    proj.lifetime = 5f;
                    proj.gravity = 9.81f;
                    proj.triggerRadius = 1f;
                    break;

                case "missile":
                case "missile_launcher":
                    proj.flightType = FlightType.Guided;
                    proj.damage = 80;
                    proj.speed = 60f;
                    proj.lifetime = 6f;
                    proj.aimTarget = aimPoint;
                    proj.guidanceLerp = 3f;
                    proj.triggerRadius = 1.5f;
                    break;

                case "rocket":
                case "rocket_pod":
                    proj.flightType = FlightType.Straight;
                    proj.damage = 40;
                    proj.speed = 100f;
                    proj.lifetime = 4f;
                    proj.triggerRadius = 1f;
                    break;

                case "laser":
                    proj.flightType = FlightType.Laser;
                    proj.damage = 8;
                    proj.speed = 0f;
                    proj.lifetime = 0.05f;
                    proj.laserDuration = 0.05f;
                    proj.triggerRadius = 0f;
                    break;

                case "railgun":
                    proj.flightType = FlightType.Laser;
                    proj.damage = 100;
                    proj.speed = 0f;
                    proj.lifetime = 0.12f;
                    proj.laserDuration = 0.12f;
                    proj.triggerRadius = 0f;
                    break;

                case "mine":
                case "mine_layer":
                    proj.flightType = FlightType.Mine;
                    proj.damage = 200;
                    proj.speed = 0f;
                    proj.lifetime = 30f;
                    proj.armDelay = 1f;
                    proj.triggerRadius = 3f;
                    break;

                case "broadside_cannon":
                case "deck_gun":
                case "hull_gun":
                    proj.flightType = FlightType.Straight;
                    proj.damage = 30;
                    proj.speed = 120f;
                    proj.lifetime = 3f;
                    proj.triggerRadius = 0.5f;
                    break;

                case "swivel_cannon":
                case "wing_cannon":
                    proj.flightType = FlightType.Straight;
                    proj.damage = 20;
                    proj.speed = 180f;
                    proj.lifetime = 2f;
                    proj.triggerRadius = 0.3f;
                    break;

                case "side_missile":
                    proj.flightType = FlightType.Guided;
                    proj.damage = 60;
                    proj.speed = 55f;
                    proj.lifetime = 6f;
                    proj.aimTarget = aimPoint;
                    proj.guidanceLerp = 3f;
                    proj.triggerRadius = 1f;
                    break;

                case "torpedo_launcher":
                    proj.flightType = FlightType.Guided;
                    proj.damage = 90;
                    proj.speed = 40f;
                    proj.lifetime = 8f;
                    proj.aimTarget = aimPoint;
                    proj.guidanceLerp = 2f;
                    proj.triggerRadius = 1.5f;
                    break;

                case "milk_gun":
                    proj.flightType = FlightType.Laser; // hitscan beam
                    proj.damage = 10;
                    proj.speed = 0f;
                    proj.lifetime = 0.06f;
                    proj.laserDuration = 0.06f;
                    proj.triggerRadius = 0f;
                    break;

                default:
                    Debug.LogWarning($"[Projectile] Unknown weapon ID: {weaponId}, using machine_gun defaults");
                    proj.flightType = FlightType.Straight;
                    proj.damage = 10;
                    proj.speed = 200f;
                    proj.lifetime = 2f;
                    proj.triggerRadius = 0.3f;
                    break;
            }

            proj.Init(direction, origin);
            return proj;
        }
    }
}
