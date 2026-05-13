using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Combat;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Shore-mounted anti-vehicle turret. Builds its own detailed visual on Awake
    /// if empty. Tracks nearest VehicleRuntime, telegraphs fire with a red laser-sight
    /// + charge-up glow, then fires alternating dual barrels with recoil + shell
    /// casing ejection. Health-based damage states; dies by toppling.
    /// </summary>
    public class ShoreTurret : MonoBehaviour
    {
        public float detectionRange = 80f;
        public float fireInterval = 3.5f;
        public int damagePerShot = 30;
        public Transform barrelTransform;
        public float barrelPitchLimit = 45f;

        // new tuning
        public int maxHealth = 400;
        public float chargeUpTime = 0.65f;
        public float aimSlerpTime = 0.35f;
        public float leadTime = 0.25f;
        public int casingPoolCap = 8;
        public float casingLifetime = 4f;

        private const float ScanInterval = 0.5f;
        private const float TracerLifetime = 0.15f;

        private Transform _yawPivot;        // rotates 360° horizontally
        private Transform _pitchPivot;      // rotates barrels up/down
        private Transform _barrelL, _barrelR;
        private Transform _chargeGlow;      // emissive sphere at barrel tip
        private Transform _antennaLight;    // blinks during charge
        private Transform _ejectPort;       // where casings pop out
        private Transform _housing;         // top structure (for smoke/fire attachment)

        private LineRenderer _laserSight;
        private Material _chargeGlowMat;
        private Material _antennaLightMat;
        private Material _housingMat;

        private Transform _currentTarget;
        private Rigidbody _targetRb;
        private float _nextScanAt;
        private float _nextFireAt;
        private float _chargeUntil; // set when a shot is scheduled; during [now, chargeUntil) the laser is visible
        private int _nextBarrelIdx;
        private int _currentHealth;
        private bool _dead;
        private bool _topplingStarted;
        private float _toppleT;
        private Quaternion _topplingTargetRot;
        private bool _heavyDamage;
        private bool _lightDamage;
        private float _idleSweepPhase;

        // damage-state FX
        private GameObject _dmgSmokeVfx;
        private GameObject _dmgFireVfx;

        // casing pool
        private readonly Queue<GameObject> _casingPool = new Queue<GameObject>();

        // shared materials
        private static Material _tracerMat;
        private static Material _laserMat;
        private static Material _casingMat;
        private static readonly RaycastHit[] _hitBuf = new RaycastHit[8];

        private void Awake()
        {
            EnsureVisuals();
            _currentHealth = maxHealth;
            BuildLaserSight();
            // why: stagger so multi-turret bases don't all fire/scan in sync
            _nextFireAt = Time.time + Random.Range(0f, fireInterval);
            _nextScanAt = Time.time + Random.Range(0f, ScanInterval);
            _idleSweepPhase = Random.Range(0f, 10f);
        }

        private void Update()
        {
            if (_dead)
            {
                if (_topplingStarted) UpdateToppling();
                return;
            }
            float now = Time.time;

            if (now >= _nextScanAt)
            {
                _nextScanAt = now + ScanInterval;
                AcquireTarget();
            }
            if (_currentTarget != null && !_currentTarget.gameObject.activeInHierarchy)
                _currentTarget = null;

            if (_currentTarget != null) UpdateAim(now);
            else UpdateIdleSweep();

            UpdateLaserSight(now);

            if (_currentTarget != null && now >= _nextFireAt && _chargeUntil <= 0f)
            {
                _chargeUntil = now + (_heavyDamage ? chargeUpTime * 1.25f : chargeUpTime);
            }
            if (_chargeUntil > 0f && now >= _chargeUntil)
            {
                if (_currentTarget != null && HasLineOfSight()) FireShot();
                _chargeUntil = 0f;
                float interval = _heavyDamage ? fireInterval * 1.3f : fireInterval;
                _nextFireAt = now + interval;
            }

            UpdateChargeGlow(now);
            UpdateAntennaBlink(now);
        }

        // ---- target acquisition ----
        private void AcquireTarget()
        {
            Transform best = null;
            float bestSqr = detectionRange * detectionRange;
            var pos = transform.position;
            var all = VehicleRuntime.LiveInstances;
            for (int i = 0; i < all.Count; i++)
            {
                var vr = all[i];
                if (vr == null || !vr.IsAlive) continue;
                var t = vr.transform;
                float d = (t.position - pos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = t; }
            }
            _currentTarget = best;
            _targetRb = best != null ? best.GetComponent<Rigidbody>() : null;
        }

        // ---- aim + idle sweep ----
        private void UpdateAim(float now)
        {
            Vector3 aimPoint = _currentTarget.position;
            if (_targetRb != null) aimPoint += _targetRb.linearVelocity * leadTime;

            Vector3 to = aimPoint - _yawPivot.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude < 0.01f) return;

            Quaternion yawTarget = Quaternion.LookRotation(flat.normalized, Vector3.up);
            float slerp = Mathf.Min(1f, Time.deltaTime / (_heavyDamage ? aimSlerpTime * 2f : aimSlerpTime));
            _yawPivot.rotation = Quaternion.Slerp(_yawPivot.rotation, yawTarget, slerp);

            float horiz = flat.magnitude;
            float pitch = Mathf.Atan2(to.y, horiz) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, -barrelPitchLimit, barrelPitchLimit);
            Quaternion pitchTarget = Quaternion.Euler(-pitch, 0f, 0f);
            _pitchPivot.localRotation = Quaternion.Slerp(_pitchPivot.localRotation, pitchTarget, slerp);
        }

        private void UpdateIdleSweep()
        {
            // slow scanning sweep when no target
            float yaw = Mathf.Sin((Time.time + _idleSweepPhase) * 0.3f) * 30f;
            Quaternion target = Quaternion.Euler(0f, yaw, 0f);
            _yawPivot.localRotation = Quaternion.Slerp(_yawPivot.localRotation, target, Time.deltaTime / 2f);
            _pitchPivot.localRotation = Quaternion.Slerp(_pitchPivot.localRotation, Quaternion.Euler(-5f, 0f, 0f), Time.deltaTime);
        }

        // ---- charge + fire ----
        private void UpdateLaserSight(float now)
        {
            bool charging = _chargeUntil > 0f && _currentTarget != null;
            if (_laserSight == null) return;
            if (!charging) { _laserSight.enabled = false; return; }
            _laserSight.enabled = true;
            Vector3 start = GetBarrelTipPosition(_nextBarrelIdx);
            Vector3 end = _currentTarget.position;
            _laserSight.SetPosition(0, start);
            _laserSight.SetPosition(1, end);
        }

        private void UpdateChargeGlow(float now)
        {
            if (_chargeGlow == null || _chargeGlowMat == null) return;
            float scale = 0f;
            if (_chargeUntil > 0f && _currentTarget != null)
            {
                float t = Mathf.Clamp01(1f - (_chargeUntil - now) / (_heavyDamage ? chargeUpTime * 1.25f : chargeUpTime));
                scale = Mathf.Lerp(0.05f, 0.5f, t);
            }
            _chargeGlow.localScale = Vector3.one * scale;
            float emit = _chargeUntil > 0f ? 6f : 0.1f;
            _chargeGlowMat.SetColor("_EmissionColor", new Color(1.5f, 0.55f, 0.08f) * emit * Mathf.Max(scale, 0.1f));
        }

        private void UpdateAntennaBlink(float now)
        {
            if (_antennaLightMat == null) return;
            float blink = _chargeUntil > 0f ? Mathf.PingPong(now * 10f, 1f) : (_currentTarget != null ? 0.6f : 0.2f);
            _antennaLightMat.SetColor("_EmissionColor", new Color(1.2f, 0.1f, 0.05f) * blink * 2f);
        }

        private bool HasLineOfSight()
        {
            if (_currentTarget == null) return false;
            Vector3 origin = GetBarrelTipPosition(_nextBarrelIdx);
            Vector3 dir = (_currentTarget.position - origin).normalized;
            int count = Physics.RaycastNonAlloc(origin, dir, _hitBuf, detectionRange * 1.2f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var h = _hitBuf[i];
                if (h.transform.IsChildOf(transform)) continue;
                var vr = h.collider.GetComponentInParent<VehicleRuntime>();
                if (vr != null) return true;
                return false; // first non-self hit wasn't a vehicle — blocked
            }
            return true;
        }

        private void FireShot()
        {
            Vector3 origin = GetBarrelTipPosition(_nextBarrelIdx);
            Vector3 dir = _currentTarget != null
                ? ((_currentTarget.position + (_targetRb != null ? _targetRb.linearVelocity * leadTime : Vector3.zero)) - origin).normalized
                : _pitchPivot.forward;

            VFXManager.MuzzleFlash(origin, dir, 1.5f);
            SpawnTracer(origin, origin + dir * detectionRange * 1.2f);
            StartBarrelRecoil(_nextBarrelIdx);
            EjectCasing();

            if (Physics.Raycast(origin, dir, out RaycastHit hit, detectionRange * 1.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(transform))
                {
                    var vr = hit.collider.GetComponentInParent<VehicleRuntime>();
                    if (vr != null) DamageSystem.DealDamageToVehicle(vr, damagePerShot, hit.point, skipControlParts: true);
                    VFXManager.BigExplosion(hit.point, 1.3f);
                }
            }
            _nextBarrelIdx = 1 - _nextBarrelIdx;
        }

        private Vector3 GetBarrelTipPosition(int idx)
        {
            var b = (idx == 0 ? _barrelL : _barrelR);
            if (b == null) return (barrelTransform != null ? barrelTransform.position : transform.position);
            return b.position + b.forward * 0.9f;
        }

        // ---- damage ----
        public void TakeDamage(int amount, Vector3 hitPoint)
        {
            if (_dead) return;
            _currentHealth = Mathf.Max(0, _currentHealth - amount);
            if (!_lightDamage && _currentHealth <= maxHealth * 0.6f)
            {
                _lightDamage = true;
                if (_housing != null)
                {
                    _dmgSmokeVfx = VFXManager.GroundFog(_housing.position + Vector3.up * 0.5f, 1.4f);
                    if (_dmgSmokeVfx != null) _dmgSmokeVfx.transform.SetParent(_housing, true);
                }
            }
            if (!_heavyDamage && _currentHealth <= maxHealth * 0.3f)
            {
                _heavyDamage = true;
                if (_housing != null)
                {
                    _dmgFireVfx = VFXManager.LargeFlames(_housing.position, 1.2f);
                    if (_dmgFireVfx != null) _dmgFireVfx.transform.SetParent(_housing, true);
                }
            }
            if (_currentHealth == 0) Die(hitPoint);
        }

        private void Die(Vector3 hitPoint)
        {
            _dead = true;
            VFXManager.BigExplosion(transform.position + Vector3.up * 2f, 2.5f);
            VFXManager.LargeFlames(transform.position + Vector3.up * 2f, 2f);
            // why: visual topple along a random horizontal axis
            Vector3 axis = Random.insideUnitSphere; axis.y = 0f;
            if (axis.sqrMagnitude < 0.01f) axis = Vector3.right;
            _topplingTargetRot = Quaternion.AngleAxis(90f, axis.normalized) * _housing.localRotation;
            _topplingStarted = true;
            Destroy(gameObject, 6f);
        }

        private void UpdateToppling()
        {
            if (_housing == null) return;
            _toppleT += Time.deltaTime / 1f;
            _housing.localRotation = Quaternion.Slerp(_housing.localRotation, _topplingTargetRot, Mathf.Clamp01(_toppleT));
        }

        // ---- visuals ----
        private void EnsureVisuals()
        {
            if (CountRenderers(transform) >= 3)
            {
                // prefab already has a visual — try to adopt
                _yawPivot = transform;
                _pitchPivot = FindChildByName(transform, "PitchPivot") ?? transform;
                if (barrelTransform == null) barrelTransform = _pitchPivot;
                _housing = transform;
                return;
            }

            // Base pedestal (static, with collider)
            var pedestal = CreatePrimitive(PrimitiveType.Cylinder, transform, "Pedestal",
                new Vector3(0f, 0.3f, 0f), new Vector3(2.4f, 0.3f, 2.4f), new Color(0.45f, 0.45f, 0.45f));

            // Ring collar
            CreatePrimitive(PrimitiveType.Cylinder, transform, "RingCollar",
                new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 0.08f, 1.8f), new Color(0.25f, 0.25f, 0.25f));

            // Yaw pivot
            _yawPivot = new GameObject("YawPivot").transform;
            _yawPivot.SetParent(transform, false);
            _yawPivot.localPosition = new Vector3(0f, 0.8f, 0f);

            // Housing (khaki) — the "toppling" object
            _housing = CreatePrimitive(PrimitiveType.Cube, _yawPivot, "Housing",
                Vector3.zero, new Vector3(1.6f, 0.7f, 2.2f), new Color(0.52f, 0.48f, 0.32f)).transform;
            _housingMat = _housing.GetComponent<MeshRenderer>().material;

            // Trunnion blocks
            CreatePrimitive(PrimitiveType.Cube, _housing, "TrunnionL",
                new Vector3(0.7f, 0.1f, 0.5f), new Vector3(0.2f, 0.3f, 0.3f), new Color(0.35f, 0.33f, 0.25f));
            CreatePrimitive(PrimitiveType.Cube, _housing, "TrunnionR",
                new Vector3(-0.7f, 0.1f, 0.5f), new Vector3(0.2f, 0.3f, 0.3f), new Color(0.35f, 0.33f, 0.25f));

            // Optics
            var optics = CreatePrimitive(PrimitiveType.Sphere, _housing, "Optics",
                new Vector3(0f, 0.3f, 0.9f), new Vector3(0.2f, 0.15f, 0.15f), new Color(0.1f, 0.15f, 0.25f));
            var opticsMat = optics.GetComponent<MeshRenderer>().material;
            opticsMat.EnableKeyword("_EMISSION");
            opticsMat.SetColor("_EmissionColor", new Color(0.2f, 0.5f, 1f) * 0.6f);

            // Antenna
            CreatePrimitive(PrimitiveType.Cylinder, _housing, "Antenna",
                new Vector3(0.5f, 0.75f, -0.8f), new Vector3(0.08f, 0.5f, 0.08f), new Color(0.2f, 0.2f, 0.2f));
            var antennaTop = CreatePrimitive(PrimitiveType.Sphere, _housing, "AntennaLight",
                new Vector3(0.5f, 1.3f, -0.8f), new Vector3(0.12f, 0.12f, 0.12f), new Color(1f, 0.1f, 0.05f));
            _antennaLight = antennaTop.transform;
            _antennaLightMat = antennaTop.GetComponent<MeshRenderer>().material;
            _antennaLightMat.EnableKeyword("_EMISSION");

            // Pitch pivot at trunnion axis
            _pitchPivot = new GameObject("PitchPivot").transform;
            _pitchPivot.SetParent(_housing, false);
            _pitchPivot.localPosition = new Vector3(0f, 0.1f, 0.3f);

            // Two barrels
            _barrelL = BuildBarrel(_pitchPivot, "BarrelL", -0.25f);
            _barrelR = BuildBarrel(_pitchPivot, "BarrelR", 0.25f);

            // Shared charge glow at midpoint forward
            var glow = CreatePrimitive(PrimitiveType.Sphere, _pitchPivot, "ChargeGlow",
                new Vector3(0f, 0f, 1.0f), new Vector3(0.05f, 0.05f, 0.05f), new Color(1f, 0.55f, 0.08f));
            _chargeGlow = glow.transform;
            _chargeGlowMat = glow.GetComponent<MeshRenderer>().material;
            _chargeGlowMat.EnableKeyword("_EMISSION");

            // Eject port (empty) on side of housing
            _ejectPort = new GameObject("EjectPort").transform;
            _ejectPort.SetParent(_housing, false);
            _ejectPort.localPosition = new Vector3(0.9f, 0.2f, 0.2f);

            if (barrelTransform == null) barrelTransform = _pitchPivot;
        }

        private Transform BuildBarrel(Transform parent, string name, float xOffset)
        {
            var barrelAnchor = new GameObject(name).transform;
            barrelAnchor.SetParent(parent, false);
            barrelAnchor.localPosition = new Vector3(xOffset, 0f, 0f);
            CreatePrimitive(PrimitiveType.Cylinder, barrelAnchor, name + "_Tube",
                new Vector3(0f, 0f, 0.5f),
                new Vector3(0.12f, 0.5f, 0.12f),
                new Color(0.18f, 0.18f, 0.18f));
            // tube is rotated so its length points +Z; rotate 90° on X
            var tube = barrelAnchor.Find(name + "_Tube");
            if (tube != null) tube.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreatePrimitive(PrimitiveType.Cylinder, barrelAnchor, name + "_Brake",
                new Vector3(0f, 0f, 1.05f),
                new Vector3(0.18f, 0.08f, 0.18f),
                new Color(0.1f, 0.1f, 0.1f));
            var brake = barrelAnchor.Find(name + "_Brake");
            if (brake != null) brake.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return barrelAnchor;
        }

        private static int CountRenderers(Transform t)
        {
            int c = 0;
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true)) c++;
            return c;
        }
        private static Transform FindChildByName(Transform t, string name)
        {
            foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                if (c.name == name) return c;
            return null;
        }

        private static GameObject CreatePrimitive(PrimitiveType type, Transform parent, string name,
            Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = color;
            m.SetFloat("_Smoothness", 0.3f);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // ---- laser + tracer + casing ----
        private void BuildLaserSight()
        {
            var go = new GameObject("LaserSight");
            go.transform.SetParent(_pitchPivot != null ? _pitchPivot : transform, false);
            _laserSight = go.AddComponent<LineRenderer>();
            _laserSight.positionCount = 2;
            _laserSight.startWidth = 0.08f;
            _laserSight.endWidth = 0.05f;
            _laserSight.useWorldSpace = true;
            if (_laserMat == null)
            {
                _laserMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _laserMat.color = new Color(1f, 0.1f, 0.05f, 0.9f);
            }
            _laserSight.sharedMaterial = _laserMat;
            _laserSight.startColor = new Color(1.5f, 0.15f, 0.08f, 1f);
            _laserSight.endColor = new Color(1.5f, 0.15f, 0.08f, 0.5f);
            _laserSight.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _laserSight.enabled = false;
        }

        private void SpawnTracer(Vector3 a, Vector3 b)
        {
            var go = new GameObject("TurretTracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.15f; lr.endWidth = 0.05f;
            lr.useWorldSpace = true;
            if (_tracerMat == null)
            {
                _tracerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _tracerMat.color = new Color(1f, 0.8f, 0.2f, 1f);
            }
            lr.sharedMaterial = _tracerMat;
            lr.startColor = new Color(1.5f, 1f, 0.3f, 1f);
            lr.endColor = new Color(1.5f, 0.7f, 0.15f, 0.2f);
            lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Object.Destroy(go, TracerLifetime);
        }

        private void StartBarrelRecoil(int idx)
        {
            var b = (idx == 0 ? _barrelL : _barrelR);
            if (b == null) return;
            b.localPosition += new Vector3(0f, 0f, -0.25f);
            // Spring it back via a simple coroutine-less approach: use a small helper
            var spring = b.gameObject.GetComponent<BarrelRecoilSpring>();
            if (spring == null) spring = b.gameObject.AddComponent<BarrelRecoilSpring>();
            spring.restLocalZ = b.localPosition.z + 0.25f;
            spring.returnTime = 0.15f;
            spring.t = 0f;
        }

        private void EjectCasing()
        {
            if (_ejectPort == null) return;
            GameObject casing;
            if (_casingPool.Count >= casingPoolCap)
            {
                casing = _casingPool.Dequeue();
                if (casing == null) casing = CreateCasing();
                else casing.SetActive(true);
            }
            else casing = CreateCasing();

            casing.transform.position = _ejectPort.position;
            casing.transform.rotation = Random.rotation;
            var rb = casing.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = _ejectPort.right * Random.Range(2f, 4f) + Vector3.up * Random.Range(1.5f, 2.5f);
                rb.angularVelocity = Random.insideUnitSphere * 10f;
            }
            _casingPool.Enqueue(casing);
            Object.Destroy(casing, casingLifetime);
        }

        private GameObject CreateCasing()
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = "Casing";
            c.transform.localScale = new Vector3(0.08f, 0.12f, 0.08f);
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
            rb.mass = 0.2f;
            rb.useGravity = true;
            var sc = c.AddComponent<SphereCollider>();
            sc.radius = 0.06f;
            return c;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, detectionRange);
        }
    }

    /// <summary>Simple spring-back for turret barrel recoil. Destroys itself when home.</summary>
    internal class BarrelRecoilSpring : MonoBehaviour
    {
        public float restLocalZ;
        public float returnTime = 0.15f;
        public float t;
        private void Update()
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / returnTime);
            var p = transform.localPosition;
            p.z = Mathf.Lerp(p.z, restLocalZ, u);
            transform.localPosition = p;
            if (u >= 1f) Destroy(this);
        }
    }
}
