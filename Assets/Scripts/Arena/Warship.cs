using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Combat;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Patrolling warship with pursuit mode, procedural hull detail,
    /// rotating radar dishes, running lights, wake, searchlights,
    /// dual-barrel AA suppression, damage states, and sinking on death.
    /// </summary>
    public class Warship : MonoBehaviour
    {
        // Contract fields (arena sets)
        public Transform[] patrolWaypoints;
        public float patrolSpeed = 8f;
        public float detectionRange = 100f;
        public int damagePerShot = 25;
        public float fireInterval = 4f;
        public Transform barrelTransform;

        // New tuning
        public int maxHealth = 1500;
        public float pursueBreakoffTime = 20f;
        public float aaFireInterval = 2f;
        public int aaDamagePerShot = 5;
        public float hornMinInterval = 30f;
        public float hornMaxInterval = 60f;
        public bool leadTargets = true;

        // State
        private int _wpIndex;
        private float _scanTimer, _fireTimer, _aaTimer, _hornTimer, _pursueTimer;
        private Transform _target;
        private Rigidbody _targetRb;
        private int _currentHealth;
        private bool _dead, _sinking;
        private float _sinkTimer;
        private bool _dmg75, _dmg50, _dmg25;
        private float _speedMultiplier = 1f;
        private bool _aaCapable = true;
        private bool _pursuing;

        // Visual refs
        private Transform _radarA, _radarB;
        private Transform _sternAnchor;
        private Transform _aaMountFwd, _aaMountAft;
        private Transform _searchlightAPivot, _searchlightBPivot;
        private float _searchlightPhase;
        private ParticleSystem _wake;
        private float _baseY;
        private float _bobPhase;
        private AudioSource _audio;
        private AudioClip _hornClip;

        // Shared resources
        private static Material _tracerMat;
        private static Material _casingMat;
        private static readonly Queue<GameObject> s_casingPool = new Queue<GameObject>();
        private const int CasingPoolCap = 16;

        private void Awake()
        {
            EnsureHullDetail();
            _currentHealth = maxHealth;
            _baseY = transform.position.y;
            _bobPhase = Random.Range(0f, Mathf.PI * 2f);
            _hornTimer = Random.Range(hornMinInterval, hornMaxInterval);
            _searchlightPhase = Random.Range(0f, 10f);
            BuildAudio();
        }

        private void Start()
        {
            if (patrolWaypoints == null || patrolWaypoints.Length == 0)
            {
                Debug.LogWarning("[Warship] no waypoints — disabling");
                enabled = false;
                return;
            }
            // snap to first waypoint
            Vector3 p = GetWaypointPos(0);
            if (p != Vector3.zero) transform.position = new Vector3(p.x, _baseY, p.z);
        }

        private void Update()
        {
            if (_dead) { if (_sinking) UpdateSink(); return; }
            float dt = Time.deltaTime;

            _scanTimer -= dt;
            if (_scanTimer <= 0f) { _scanTimer = 0.5f; AcquireTarget(); }

            if (_pursuing) UpdatePursue(dt);
            else Patrol(dt);

            _fireTimer -= dt;
            if (_target != null && _fireTimer <= 0f)
            {
                FireMainGun();
                _fireTimer = _pursuing ? fireInterval * 0.6f : fireInterval;
            }

            if (_aaCapable)
            {
                _aaTimer -= dt;
                if (_aaTimer <= 0f) { _aaTimer = aaFireInterval; DoAABurst(); }
            }

            UpdateRadar(dt);
            UpdateSearchlights(dt);
            UpdateBob();
            UpdateHorn(dt);
            UpdateWakeEmission();
        }

        // ---- patrol + pursuit ----
        private void Patrol(float dt)
        {
            if (patrolWaypoints.Length == 0) return;
            Vector3 wpPos = GetWaypointPos(_wpIndex);
            Vector3 flatTo = new Vector3(wpPos.x - transform.position.x, 0f, wpPos.z - transform.position.z);
            if (flatTo.sqrMagnitude < 9f)
            {
                _wpIndex = (_wpIndex + 1) % patrolWaypoints.Length;
                return;
            }
            Vector3 dir = flatTo.normalized;
            transform.position += dir * patrolSpeed * _speedMultiplier * dt;
            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, dt);
        }

        private void UpdatePursue(float dt)
        {
            _pursueTimer += dt;
            if (_target == null || !_target.gameObject.activeInHierarchy || _pursueTimer > pursueBreakoffTime)
            {
                _pursuing = false;
                _pursueTimer = 0f;
                _wpIndex = FindNearestWaypointIndex();
                return;
            }
            Vector3 chase = _target.position;
            if (leadTargets && _targetRb != null) chase += _targetRb.linearVelocity.normalized * 50f;
            chase.y = _baseY;
            Vector3 flatTo = new Vector3(chase.x - transform.position.x, 0f, chase.z - transform.position.z);
            if (flatTo.sqrMagnitude < 0.01f) return;
            Vector3 dir = flatTo.normalized;
            transform.position += dir * patrolSpeed * 1.6f * _speedMultiplier * dt;
            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, dt);
            if (Vector3.Distance(transform.position, _target.position) > detectionRange * 2f)
            {
                _pursuing = false; _pursueTimer = 0f;
                _wpIndex = FindNearestWaypointIndex();
            }
        }

        private int FindNearestWaypointIndex()
        {
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < patrolWaypoints.Length; i++)
            {
                Vector3 p = GetWaypointPos(i);
                if (p == Vector3.zero) continue;
                float d = (p - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private Vector3 GetWaypointPos(int i)
        {
            if (patrolWaypoints == null || i < 0 || i >= patrolWaypoints.Length) return Vector3.zero;
            var wp = patrolWaypoints[i];
            return wp != null ? wp.position : Vector3.zero;
        }

        // ---- target + firing ----
        private void AcquireTarget()
        {
            Transform best = null; float bestSqr = detectionRange * detectionRange;
            Vector3 pos = transform.position;
            var all = VehicleRuntime.LiveInstances;
            for (int i = 0; i < all.Count; i++)
            {
                var vr = all[i];
                if (vr == null || !vr.IsAlive) continue;
                float d = (vr.transform.position - pos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = vr.transform; }
            }
            _target = best;
            _targetRb = best != null ? best.GetComponent<Rigidbody>() : null;
            if (_target != null && !_pursuing) { _pursuing = true; _pursueTimer = 0f; }
        }

        private void FireMainGun()
        {
            Vector3 origin = barrelTransform != null ? barrelTransform.position : transform.position + Vector3.up * 3f;
            Vector3 aim = _target.position;
            if (leadTargets && _targetRb != null) aim += _targetRb.linearVelocity * 0.3f;
            Vector3 dir = (aim - origin).normalized;

            VFXManager.MuzzleFlash(origin, dir, 1.8f);
            SpawnTracer(origin, origin + dir * detectionRange * 1.2f, new Color(1.5f, 0.6f, 0.1f, 1f));
            EjectCasing(origin + Vector3.right * 1f);

            if (Physics.Raycast(origin, dir, out RaycastHit hit, detectionRange * 1.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(transform))
                {
                    var vr = hit.collider.GetComponentInParent<VehicleRuntime>();
                    if (vr != null) DamageSystem.DealDamageToVehicle(vr, damagePerShot, hit.point, skipControlParts: true);
                    VFXManager.BigExplosion(hit.point, 1.2f);
                    VFXManager.LargeFlames(hit.point, 0.9f);
                    VFXManager.SmallExplosion(hit.point + Vector3.up * 0.5f, 0.8f);
                }
            }
        }

        private void DoAABurst()
        {
            // find nearest air target
            Transform air = null; float bestSqr = detectionRange * 1.2f * detectionRange * 1.2f;
            var all = VehicleRuntime.LiveInstances;
            for (int i = 0; i < all.Count; i++)
            {
                var vr = all[i];
                if (vr == null || !vr.IsAlive) continue;
                var veh = vr.GetComponent<CloseEncounters.Vehicle.Vehicle>();
                if (veh == null || veh.domain != "air") continue;
                float d = (vr.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; air = vr.transform; }
            }
            if (air == null) return;

            for (int shot = 0; shot < 3; shot++)
            {
                // fire both mounts, burst spaced
                FireAAShotFrom(_aaMountFwd, air);
                FireAAShotFrom(_aaMountAft, air);
            }
        }

        private void FireAAShotFrom(Transform mount, Transform target)
        {
            if (mount == null || target == null) return;
            Vector3 origin = mount.position;
            Vector3 spread = Random.insideUnitSphere * 1.2f;
            Vector3 dir = (target.position + spread - origin).normalized;
            VFXManager.MuzzleFlash(origin, dir, 0.6f);
            SpawnTracer(origin, origin + dir * detectionRange * 1.2f, new Color(1.5f, 1.3f, 0.3f, 1f));
            if (Physics.Raycast(origin, dir, out RaycastHit hit, detectionRange * 1.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(transform))
                {
                    var vr = hit.collider.GetComponentInParent<VehicleRuntime>();
                    if (vr != null) DamageSystem.DealDamageToVehicle(vr, aaDamagePerShot, hit.point, skipControlParts: true);
                    VFXManager.SmallExplosion(hit.point, 0.6f);
                }
            }
        }

        // ---- health ----
        public void TakeDamage(int amount, Vector3 hitPoint)
        {
            if (_dead) return;
            _currentHealth = Mathf.Max(0, _currentHealth - amount);
            float f = (float)_currentHealth / maxHealth;
            if (!_dmg75 && f <= 0.75f) { _dmg75 = true; AttachVfx(VFXManager.GroundFog, transform.position + Vector3.up * 4f, 1.5f); }
            if (!_dmg50 && f <= 0.5f) { _dmg50 = true; _speedMultiplier = 0.8f; AttachVfx(VFXManager.LargeFlames, transform.position + Vector3.up * 4f, 1.5f); }
            if (!_dmg25 && f <= 0.25f) { _dmg25 = true; _aaCapable = false; AttachVfx(VFXManager.LargeFlames, transform.position + Vector3.up * 1f, 1.8f); }
            if (_currentHealth == 0) Die();
        }

        private void AttachVfx(System.Func<Vector3, float, GameObject> fn, Vector3 pos, float scale)
        {
            var go = fn(pos, scale);
            if (go != null) go.transform.SetParent(transform, true);
        }

        private void Die()
        {
            _dead = true;
            _sinking = true;
            VFXManager.BigExplosion(transform.position + Vector3.up * 3f, 3f);
            VFXManager.LargeFlames(transform.position + Vector3.up * 2f, 2.5f);
            VFXManager.SmallExplosion(transform.position + Vector3.up * 4f, 2f);
            DamageSystem.DealAreaDamage(transform.position, 15f, 150);
            Destroy(gameObject, 8f);
        }

        private void UpdateSink()
        {
            _sinkTimer += Time.deltaTime;
            transform.position += Vector3.down * 0.5f * Time.deltaTime;
            transform.Rotate(0.5f * Time.deltaTime, 0f, 1.2f * Time.deltaTime, Space.Self);
        }

        // ---- cosmetic updates ----
        private void UpdateRadar(float dt)
        {
            float mul = _target != null ? 2f : 1f;
            if (_radarA != null) _radarA.Rotate(0f, 30f * mul * dt, 0f, Space.Self);
            if (_radarB != null) _radarB.Rotate(90f * mul * dt, 0f, 0f, Space.Self);
        }

        private void UpdateSearchlights(float dt)
        {
            if (_searchlightAPivot != null)
                _searchlightAPivot.localRotation = Quaternion.Euler(15f, Mathf.Sin((Time.time + _searchlightPhase) * 0.8f) * 60f, 0f);
            if (_searchlightBPivot != null)
                _searchlightBPivot.localRotation = Quaternion.Euler(15f, Mathf.Sin((Time.time + _searchlightPhase + 3f) * 0.8f) * 60f, 0f);
        }

        private void UpdateBob()
        {
            var p = transform.position;
            p.y = _baseY + Mathf.Sin(Time.time * 0.5f + _bobPhase) * 0.2f;
            transform.position = p;
        }

        private void UpdateHorn(float dt)
        {
            if (_hornClip == null || _audio == null || _target != null) return;
            _hornTimer -= dt;
            if (_hornTimer <= 0f)
            {
                _audio.PlayOneShot(_hornClip, 0.7f);
                _hornTimer = Random.Range(hornMinInterval, hornMaxInterval);
            }
        }

        private void UpdateWakeEmission()
        {
            if (_wake == null) return;
            var em = _wake.emission;
            em.rateOverTime = _pursuing ? 110f : 40f;
        }

        // ---- audio ----
        private void BuildAudio()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f;
            _audio.minDistance = 20f;
            _audio.maxDistance = 600f;
            _hornClip = Resources.Load<AudioClip>("Audio/Ambient/ShipHorn");
        }

        // ---- hull detail ----
        private void EnsureHullDetail()
        {
            // Find a sensible "stern" point first (based on existing bounds)
            Bounds b = ComputeRootBounds();
            Vector3 sternLocal = new Vector3(0f, 0f, -b.extents.z);

            _sternAnchor = new GameObject("Stern").transform;
            _sternAnchor.SetParent(transform, false);
            _sternAnchor.localPosition = sternLocal;

            // Bridge superstructure on top of hull (if this is a minimal arena-built ship, add detail)
            if (CountRenderers(transform) < 5)
            {
                BuildFullDetail(b);
            }
            else
            {
                BuildMinimalDetail(b);
            }
            BuildWake();
        }

        private Bounds ComputeRootBounds()
        {
            var rends = GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(transform.position, new Vector3(6f, 3f, 18f));
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            // world → local
            Vector3 c = transform.InverseTransformPoint(b.center);
            Vector3 e = new Vector3(Mathf.Abs(b.extents.x), Mathf.Abs(b.extents.y), Mathf.Abs(b.extents.z));
            return new Bounds(c, e * 2f);
        }

        private static int CountRenderers(Transform t)
        {
            return t.GetComponentsInChildren<MeshRenderer>(true).Length;
        }

        private void BuildFullDetail(Bounds b)
        {
            // Bridge on top amidships
            var bridge = CreatePrim(PrimitiveType.Cube, transform, "Bridge",
                new Vector3(0f, b.extents.y + 1f, b.extents.z * 0.15f),
                new Vector3(2.4f, 1.5f, 3.5f),
                new Color(0.4f, 0.42f, 0.45f));
            var window = CreatePrim(PrimitiveType.Cube, bridge.transform, "Windows",
                new Vector3(0f, 0.3f, 1.7f),
                new Vector3(2.5f, 0.4f, 0.05f),
                new Color(0.15f, 0.35f, 0.5f));
            var wMat = window.GetComponent<MeshRenderer>().material;
            wMat.EnableKeyword("_EMISSION");
            wMat.SetColor("_EmissionColor", new Color(0.6f, 0.85f, 1f) * 2.5f);

            // Radar mast
            var mast = CreatePrim(PrimitiveType.Cylinder, bridge.transform, "Mast",
                new Vector3(0f, 1.2f, -0.5f),
                new Vector3(0.2f, 1.8f, 0.2f),
                new Color(0.3f, 0.3f, 0.3f));
            _radarA = CreatePrim(PrimitiveType.Cube, mast.transform, "RadarA",
                new Vector3(0f, 1.6f, 0f),
                new Vector3(3f, 0.25f, 0.8f),
                new Color(0.6f, 0.6f, 0.65f)).transform;
            _radarB = CreatePrim(PrimitiveType.Sphere, mast.transform, "RadarB",
                new Vector3(0f, 2.2f, 0f),
                new Vector3(0.8f, 0.25f, 2f),
                new Color(0.5f, 0.52f, 0.55f)).transform;

            // Main deck gun (assign as barrelTransform if not set)
            var turret = CreatePrim(PrimitiveType.Cylinder, transform, "MainGun",
                new Vector3(0f, b.extents.y + 0.6f, b.extents.z * 0.7f),
                new Vector3(1.2f, 0.4f, 1.2f),
                new Color(0.35f, 0.38f, 0.4f));
            var barrel = CreatePrim(PrimitiveType.Cylinder, turret.transform, "Barrel",
                new Vector3(0f, 0f, 1.5f),
                new Vector3(0.18f, 0.9f, 0.18f),
                new Color(0.2f, 0.2f, 0.22f));
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            if (barrelTransform == null) barrelTransform = barrel.transform;

            // AA mounts
            _aaMountFwd = CreatePrim(PrimitiveType.Cylinder, transform, "AAMountFwd",
                new Vector3(0f, b.extents.y + 0.5f, b.extents.z * 0.35f),
                new Vector3(0.8f, 0.3f, 0.8f),
                new Color(0.35f, 0.35f, 0.35f)).transform;
            _aaMountAft = CreatePrim(PrimitiveType.Cylinder, transform, "AAMountAft",
                new Vector3(0f, b.extents.y + 0.5f, -b.extents.z * 0.4f),
                new Vector3(0.8f, 0.3f, 0.8f),
                new Color(0.35f, 0.35f, 0.35f)).transform;

            // Running lights
            CreateEmissiveLight(transform, new Vector3(-b.extents.x, b.extents.y + 1.5f, 0f), new Color(1f, 0.05f, 0.05f), "PortLight");
            CreateEmissiveLight(transform, new Vector3(b.extents.x, b.extents.y + 1.5f, 0f), new Color(0.05f, 1f, 0.2f), "StbdLight");
            CreateEmissiveLight(bridge.transform, new Vector3(0f, 2f, 0f), new Color(1f, 1f, 1f), "MastLight");
            CreateEmissiveLight(transform, new Vector3(0f, b.extents.y + 0.4f, -b.extents.z * 0.95f), new Color(1f, 1f, 0.9f), "SternLight");

            // Searchlights
            _searchlightAPivot = BuildSearchlight(bridge.transform, new Vector3(-1.0f, 0.7f, 0f));
            _searchlightBPivot = BuildSearchlight(bridge.transform, new Vector3(1.0f, 0.7f, 0f));

            // Smokestack
            var stack = CreatePrim(PrimitiveType.Cylinder, transform, "Smokestack",
                new Vector3(0f, b.extents.y + 1.2f, -b.extents.z * 0.1f),
                new Vector3(0.9f, 0.8f, 0.9f),
                new Color(0.22f, 0.22f, 0.22f));
            var smokeFx = VFXManager.GroundFog(stack.transform.position + Vector3.up * 1.5f, 1f);
            if (smokeFx != null) smokeFx.transform.SetParent(stack.transform, true);
        }

        private void BuildMinimalDetail(Bounds b)
        {
            // Just radar + a couple running lights on prefab-based hulls
            var mast = CreatePrim(PrimitiveType.Cylinder, transform, "Mast",
                new Vector3(0f, b.extents.y + 1.5f, 0f),
                new Vector3(0.2f, 1.2f, 0.2f),
                new Color(0.3f, 0.3f, 0.3f));
            _radarA = CreatePrim(PrimitiveType.Cube, mast.transform, "RadarA",
                new Vector3(0f, 1.2f, 0f),
                new Vector3(2.5f, 0.2f, 0.6f),
                new Color(0.6f, 0.6f, 0.65f)).transform;
            _radarB = CreatePrim(PrimitiveType.Sphere, mast.transform, "RadarB",
                new Vector3(0f, 1.8f, 0f),
                new Vector3(0.6f, 0.2f, 1.6f),
                new Color(0.5f, 0.52f, 0.55f)).transform;
            CreateEmissiveLight(transform, new Vector3(-b.extents.x, b.extents.y + 1f, 0f), new Color(1f, 0.05f, 0.05f), "PortLight");
            CreateEmissiveLight(transform, new Vector3(b.extents.x, b.extents.y + 1f, 0f), new Color(0.05f, 1f, 0.2f), "StbdLight");
        }

        private Transform BuildSearchlight(Transform parent, Vector3 localPos)
        {
            var pivot = new GameObject("Searchlight").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = localPos;
            CreatePrim(PrimitiveType.Cube, pivot, "Head",
                new Vector3(0f, 0f, 0.3f),
                new Vector3(0.3f, 0.3f, 0.4f),
                new Color(0.9f, 0.9f, 0.85f));
            var lens = CreatePrim(PrimitiveType.Sphere, pivot, "Lens",
                new Vector3(0f, 0f, 0.55f),
                new Vector3(0.25f, 0.25f, 0.15f),
                new Color(1f, 1f, 0.95f));
            var lm = lens.GetComponent<MeshRenderer>().material;
            lm.EnableKeyword("_EMISSION");
            lm.SetColor("_EmissionColor", Color.white * 5f);
            var light = pivot.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.intensity = 6f;
            light.range = 60f;
            light.spotAngle = 25f;
            light.shadows = LightShadows.None;
            return pivot;
        }

        private void BuildWake()
        {
            if (_sternAnchor == null) return;
            var go = new GameObject("Wake");
            go.transform.SetParent(_sternAnchor, false);
            _wake = go.AddComponent<ParticleSystem>();
            var main = _wake.main;
            main.duration = 5f; main.loop = true;
            main.startLifetime = 2.5f;
            main.startSpeed = 1.5f;
            main.startSize = 1.2f;
            main.startColor = new Color(0.9f, 0.95f, 1f, 0.8f);
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = _wake.emission; em.rateOverTime = 40f;
            var shape = _wake.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 20f; shape.radius = 2f;
            var vel = _wake.velocityOverLifetime; vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.z = new ParticleSystem.MinMaxCurve(-7f, -4f);
            var color = _wake.colorOverLifetime; color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(grad);
            var size = _wake.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 2.5f));
            var rend = _wake.GetComponent<ParticleSystemRenderer>();
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.color = new Color(1f, 1f, 1f, 0.8f);
            rend.sharedMaterial = m;
            _wake.Play();
        }

        // ---- helpers ----
        private static GameObject CreatePrim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c;
            m.SetFloat("_Smoothness", 0.35f);
            m.SetFloat("_Metallic", 0.35f);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void CreateEmissiveLight(Transform parent, Vector3 localPos, Color c, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.2f;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 4f);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        private void SpawnTracer(Vector3 a, Vector3 b, Color color)
        {
            var go = new GameObject("WarshipTracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2; lr.useWorldSpace = true;
            lr.startWidth = 0.2f; lr.endWidth = 0.05f;
            if (_tracerMat == null) { _tracerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); _tracerMat.color = Color.white; }
            lr.sharedMaterial = _tracerMat;
            lr.startColor = color; lr.endColor = new Color(color.r, color.g, color.b, 0.1f);
            lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Destroy(go, 0.15f);
        }

        private void EjectCasing(Vector3 origin)
        {
            GameObject c;
            if (s_casingPool.Count >= CasingPoolCap) { c = s_casingPool.Dequeue(); if (c == null) c = MakeCasing(); else c.SetActive(true); }
            else c = MakeCasing();
            c.transform.position = origin;
            c.transform.rotation = Random.rotation;
            var rb = c.GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.right * Random.Range(2f, 4f) + Vector3.up * Random.Range(1.5f, 2.5f); rb.angularVelocity = Random.insideUnitSphere * 10f; }
            s_casingPool.Enqueue(c);
            Destroy(c, 4f);
        }

        private GameObject MakeCasing()
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = "ShellCasing";
            c.transform.localScale = new Vector3(0.1f, 0.15f, 0.1f);
            Object.DestroyImmediate(c.GetComponent<Collider>());
            if (_casingMat == null)
            {
                _casingMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _casingMat.color = new Color(0.85f, 0.65f, 0.15f);
                _casingMat.SetFloat("_Metallic", 0.9f);
                _casingMat.SetFloat("_Smoothness", 0.8f);
            }
            c.GetComponent<MeshRenderer>().sharedMaterial = _casingMat;
            var rb = c.AddComponent<Rigidbody>();
            rb.mass = 0.3f; rb.useGravity = true;
            var sc = c.AddComponent<SphereCollider>(); sc.radius = 0.08f;
            return c;
        }
    }
}
