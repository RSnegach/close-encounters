using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Combat;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Floating naval mine with procedural body, Hertz horns, anchor chain,
    /// chain-reaction cascade, water-column explosion, and sonar ping.
    /// </summary>
    public class SeaMine : MonoBehaviour
    {
        public int damage = 150;
        public float blastRadius = 8f;
        public float bobAmplitude = 0.2f;
        public float bobFrequency = 1.2f;
        public float armDelay = 1.5f;
        public float chainReactionRadius = 15f;
        public float chainReactionDelayMin = 0.15f;
        public float chainReactionDelayMax = 0.4f;
        public string sonarClipPath = "Audio/Mine/SonarPing";
        public float sonarIntervalMin = 2f;
        public float sonarIntervalMax = 3f;

        public bool IsArmed { get; private set; }

        private float _startY, _phase, _armTimer, _nextSonarAt;
        private bool _detonated;
        private Light _blinkLight;
        private Material _lensMat;
        private Transform _zoneRing;
        private LineRenderer _anchor;
        private AudioSource _audio;
        private Coroutine _chainDelayCoroutine;

        private static readonly HashSet<SeaMine> s_detonatingSet = new HashSet<SeaMine>();

        private void Awake()
        {
            var col = GetComponent<SphereCollider>();
            if (col == null) { col = gameObject.AddComponent<SphereCollider>(); col.radius = 1.5f; }
            col.isTrigger = true;
            BuildVisualIfEmpty();
            BuildAnchorChain();
            BuildZoneRing();
            BuildAudio();
        }

        private void Start()
        {
            _startY = transform.position.y;
            _phase = Random.Range(0f, Mathf.PI * 2f);
            _armTimer = armDelay;
            _nextSonarAt = Time.time + Random.Range(sonarIntervalMin, sonarIntervalMax);
        }

        private void Update()
        {
            float t = Time.time * bobFrequency + _phase;
            Vector3 p = transform.position;
            p.y = _startY + Mathf.Sin(t) * bobAmplitude;
            transform.position = p;
            transform.rotation = Quaternion.Euler(Mathf.Sin(t * 0.7f) * 3f, transform.eulerAngles.y, Mathf.Cos(t * 0.5f) * 3f);

            if (!IsArmed)
            {
                _armTimer -= Time.deltaTime;
                if (_armTimer <= 0f) { IsArmed = true; SetZoneRingVisible(true); }
            }

            bool priming = _chainDelayCoroutine != null;
            float hz = priming ? 5f : (IsArmed ? 2f : 0.5f);
            Color c = IsArmed ? new Color(1f, 0.08f, 0.05f) : new Color(1f, 0.5f, 0.05f);
            float intensity = priming ? 4f : (IsArmed ? 2.2f : 1f);
            float blink = Mathf.Abs(Mathf.Sin(Time.time * hz * Mathf.PI));
            if (_blinkLight != null) { _blinkLight.color = c; _blinkLight.intensity = blink * intensity; }
            if (_lensMat != null) _lensMat.SetColor("_EmissionColor", c * (blink * intensity + 0.2f));

            if (IsArmed && _audio != null && _audio.clip != null && Time.time >= _nextSonarAt && !priming)
            {
                _audio.PlayOneShot(_audio.clip, 0.3f);
                _nextSonarAt = Time.time + Random.Range(sonarIntervalMin, sonarIntervalMax);
            }

            if (_anchor != null)
            {
                Vector3 top = transform.position;
                Vector3 bot = new Vector3(top.x + Mathf.Sin(t * 0.8f) * 0.3f, _startY - 10f, top.z + Mathf.Cos(t * 0.6f) * 0.3f);
                _anchor.SetPosition(0, top); _anchor.SetPosition(1, bot);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsArmed || _detonated) return;
            if (other.GetComponentInParent<VehicleRuntime>() != null || other.GetComponentInParent<Projectile>() != null)
                Detonate();
        }

        public void TakeDamage(int amount, Vector3 hitPoint) { if (!_detonated) Detonate(); }

        public void ScheduleChainDetonate(float delay)
        {
            if (_detonated || _chainDelayCoroutine != null) return;
            _chainDelayCoroutine = StartCoroutine(ChainDelay(delay));
        }

        private IEnumerator ChainDelay(float delay) { yield return new WaitForSeconds(delay); Detonate(); }

        private void Detonate()
        {
            if (_detonated) return;
            _detonated = true;
            s_detonatingSet.Add(this);
            Vector3 pos = transform.position;
            VFXManager.BigExplosion(pos, 2.8f);
            VFXManager.LargeFlames(pos, 2.2f);
            DamageSystem.DealAreaDamage(pos, blastRadius, damage);
            SpawnWaterColumn(pos);
            SpawnShockwaveRing(pos);

            var all = FindObjectsByType<SeaMine>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var m = all[i];
                if (m == null || m == this || m._detonated || s_detonatingSet.Contains(m)) continue;
                float d = Vector3.Distance(m.transform.position, pos);
                if (d > chainReactionRadius) continue;
                m.ScheduleChainDetonate(Mathf.Lerp(chainReactionDelayMin, chainReactionDelayMax, d / chainReactionRadius));
            }
            Destroy(gameObject, 0.05f);
        }

        private void OnDestroy()
        {
            if (_chainDelayCoroutine != null) StopCoroutine(_chainDelayCoroutine);
            s_detonatingSet.Remove(this);
        }

        // ---- visuals ----
        private void BuildVisualIfEmpty()
        {
            if (transform.childCount > 0) return;
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "MineBody"; body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(1.8f, 1.6f, 1.8f);
            Destroy(body.GetComponent<Collider>());
            SetMat(body, MakeLit(new Color(0.08f, 0.07f, 0.06f), 0.35f, 0.6f));

            for (int i = 0; i < 8; i++)
            {
                float a = (Mathf.PI * 2f / 8f) * i;
                Vector3 d = new Vector3(Mathf.Cos(a) * 0.9f, 0.9f, Mathf.Sin(a) * 0.9f);
                var h = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                h.name = $"Horn_{i}"; h.transform.SetParent(transform, false);
                h.transform.localPosition = d * 0.55f;
                h.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                h.transform.localScale = new Vector3(0.18f, 0.28f, 0.18f);
                Destroy(h.GetComponent<Collider>());
                SetMat(h, MakeLit(new Color(0.12f, 0.12f, 0.12f), 0.25f, 0.7f));
            }

            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stripe.name = "HazardStripe"; stripe.transform.SetParent(transform, false);
            stripe.transform.localScale = new Vector3(1.85f, 0.12f, 1.85f);
            Destroy(stripe.GetComponent<Collider>());
            var stripeMat = MakeLit(new Color(0.85f, 0.7f, 0.1f), 0.5f, 0.2f);
            stripeMat.EnableKeyword("_EMISSION");
            stripeMat.SetColor("_EmissionColor", new Color(0.85f, 0.7f, 0.1f) * 0.5f);
            SetMat(stripe, stripeMat);

            var pivot = new GameObject("Blinker").transform;
            pivot.SetParent(transform, false); pivot.localPosition = new Vector3(0f, 1.05f, 0f);
            var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.transform.SetParent(pivot, false); lens.transform.localScale = Vector3.one * 0.22f;
            Destroy(lens.GetComponent<Collider>());
            _lensMat = MakeLit(new Color(0.9f, 0.1f, 0.05f), 0.5f, 0f);
            _lensMat.EnableKeyword("_EMISSION");
            _lensMat.SetColor("_EmissionColor", new Color(1.5f, 0.2f, 0.05f));
            SetMat(lens, _lensMat);
            _blinkLight = pivot.gameObject.AddComponent<Light>();
            _blinkLight.type = LightType.Point; _blinkLight.range = 3.5f;
            _blinkLight.color = new Color(1f, 0.5f, 0.05f); _blinkLight.shadows = LightShadows.None;
        }

        private void BuildAnchorChain()
        {
            var go = new GameObject("AnchorChain");
            go.transform.SetParent(transform, false);
            _anchor = go.AddComponent<LineRenderer>();
            _anchor.positionCount = 2; _anchor.startWidth = 0.08f; _anchor.endWidth = 0.06f;
            _anchor.useWorldSpace = true;
            _anchor.sharedMaterial = MakeUnlit(new Color(0.22f, 0.2f, 0.18f, 1f));
            _anchor.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void BuildZoneRing()
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "DetectZone"; ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, -0.9f, 0f);
            float d = blastRadius * 2f;
            ring.transform.localScale = new Vector3(d, 0.02f, d);
            Destroy(ring.GetComponent<Collider>());
            var rend = ring.GetComponent<MeshRenderer>();
            if (rend != null) { rend.sharedMaterial = MakeTransparentUnlit(new Color(1f, 0.15f, 0.1f, 0.15f)); rend.enabled = false; }
            _zoneRing = ring.transform;
        }

        private void SetZoneRingVisible(bool v)
        {
            if (_zoneRing == null) return;
            var r = _zoneRing.GetComponent<MeshRenderer>();
            if (r != null) r.enabled = v;
        }

        private void BuildAudio()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f; _audio.minDistance = 5f; _audio.maxDistance = 40f;
            _audio.playOnAwake = false; _audio.loop = false;
            _audio.clip = Resources.Load<AudioClip>(sonarClipPath);
        }

        private void SpawnWaterColumn(Vector3 pos)
        {
            var go = new GameObject("WaterColumn"); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.duration = 1.4f; main.loop = false;
            main.startLifetime = 1.5f; main.startSpeed = 18f; main.startSize = 1.6f;
            main.startColor = new Color(0.85f, 0.92f, 1f, 0.85f);
            main.gravityModifier = 1.5f; main.maxParticles = 200;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 120) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f; shape.radius = 1.8f; shape.rotation = new Vector3(-90f, 0f, 0f);
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = MakeParticleUnlit(new Color(0.85f, 0.92f, 1f, 1f));
            ps.Play(); Destroy(go, 4f);
        }

        private void SpawnShockwaveRing(Vector3 pos)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Shockwave";
            ring.transform.position = new Vector3(pos.x, 0.05f, pos.z);
            ring.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
            Destroy(ring.GetComponent<Collider>());
            var rend = ring.GetComponent<MeshRenderer>();
            if (rend != null) rend.sharedMaterial = MakeTransparentUnlit(new Color(0.6f, 0.9f, 1f, 0.8f));
            var sw = ring.AddComponent<ShockwaveExpand>();
            sw.endScale = blastRadius * 3.5f;
            Destroy(ring, 1.5f);
        }

        // ---- material helpers ----
        private static void SetMat(GameObject go, Material m) { var r = go.GetComponent<MeshRenderer>(); if (r != null) r.sharedMaterial = m; }

        private static Material MakeLit(Color c, float smooth, float metal)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c; m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            return m;
        }
        private static Material MakeUnlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.color = c; return m;
        }
        private static Material MakeTransparentUnlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f); m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.color = c; return m;
        }
        private static Material MakeParticleUnlit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh); m.color = c; return m;
        }
    }

    internal class ShockwaveExpand : MonoBehaviour
    {
        public float startScale = 0.5f;
        public float endScale = 30f;
        public float duration = 1.2f;
        private float _t;
        private Material _mat;
        private void Start() { var r = GetComponent<MeshRenderer>(); if (r != null) _mat = r.material; }
        private void Update()
        {
            _t += Time.deltaTime;
            float u = Mathf.Clamp01(_t / duration);
            float s = Mathf.Lerp(startScale, endScale, u);
            transform.localScale = new Vector3(s, 0.02f, s);
            if (_mat != null) { Color c = _mat.color; c.a = Mathf.Lerp(0.85f, 0f, u); _mat.color = c; }
        }
    }
}
