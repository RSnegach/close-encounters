using System.Collections.Generic;
using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Drives a continuous + periodic volcano effect: persistent fire/smoke plume
    /// and a glowing lava pool at the crater, plus periodic eruptions that launch
    /// MagmaRock debris upward on ballistic arcs and spawn a FireBall explosion.
    ///
    /// Attach to the volcano GameObject. Set `craterLocalOffset` to the crater
    /// location relative to the volcano's transform (typically up along +Y).
    /// </summary>
    public class VolcanoEruption : MonoBehaviour
    {
        [Header("Crater")]
        [Tooltip("Local-space offset from the volcano transform to the crater mouth.")]
        public Vector3 craterLocalOffset = new Vector3(0f, 4f, 0f);
        public float craterRadius = 3f;
        public float lavaGlowRadius = 4f;

        [Header("Eruption timing")]
        public float eruptionInterval = 8f;
        public float eruptionIntervalJitter = 3f;
        public float initialDelay = 4f;

        [Header("Debris")]
        public int debrisPerEruption = 8;
        public float debrisMinUpwardSpeed = 22f;
        public float debrisMaxUpwardSpeed = 38f;
        public float debrisSpreadDegrees = 38f;
        public float debrisLifetime = 9f;
        public float debrisMinScale = 0.6f;
        public float debrisMaxScale = 1.8f;
        public string[] debrisResourcePaths =
        {
            "Volcanic/Props/MagmaRock_01",
            "Volcanic/Props/MagmaRock_02",
            "Volcanic/Props/MagmaRock_03",
        };

        [Header("VFX prefab paths (Resources)")]
        public string firePrefabPath       = "VFX/Fire/LargeFlames";
        public string wildFirePrefabPath   = "VFX/Fire/WildFire";
        public string smokePlumePrefabPath = "VFX/CinematicExplosions/CinematicSmoke";
        public string fireBallPrefabPath   = "VFX/Explosions/FireBall";
        public string bigExplosionPath     = "VFX/Explosions/BigExplosion";
        public string heatDistortionPath   = "VFX/Ambient/HeatDistortion";

        private Vector3 _craterWorldPos;
        private float _nextEruptionAt;
        private readonly List<GameObject> _persistentVfx = new List<GameObject>(8);
        private GameObject[] _debrisPrefabs;
        private GameObject _fireBallPrefab;
        private GameObject _bigExplosionPrefab;

        private void Start()
        {
            _craterWorldPos = transform.TransformPoint(craterLocalOffset);

            // Persistent VFX — looped fire, plume, heat shimmer, and a glowing lava disc.
            SpawnLoopedVFX(firePrefabPath, _craterWorldPos, 2.2f);
            SpawnLoopedVFX(firePrefabPath, _craterWorldPos + Vector3.right * craterRadius * 0.5f, 1.6f);
            SpawnLoopedVFX(firePrefabPath, _craterWorldPos - Vector3.right * craterRadius * 0.5f, 1.6f);
            SpawnLoopedVFX(wildFirePrefabPath, _craterWorldPos, 1.8f);
            SpawnLoopedVFX(smokePlumePrefabPath, _craterWorldPos + Vector3.up * 2f, 3.5f);
            SpawnLoopedVFX(heatDistortionPath, _craterWorldPos + Vector3.up * 3f, 2.5f);
            BuildLavaPool();

            // Cache eruption-time prefabs up-front.
            _fireBallPrefab = Resources.Load<GameObject>(fireBallPrefabPath);
            _bigExplosionPrefab = Resources.Load<GameObject>(bigExplosionPath);
            _debrisPrefabs = new GameObject[debrisResourcePaths.Length];
            for (int i = 0; i < debrisResourcePaths.Length; i++)
                _debrisPrefabs[i] = Resources.Load<GameObject>(debrisResourcePaths[i]);

            _nextEruptionAt = Time.time + initialDelay;
        }

        private void Update()
        {
            if (Time.time < _nextEruptionAt) return;
            Erupt();
            _nextEruptionAt = Time.time + eruptionInterval + Random.Range(-eruptionIntervalJitter, eruptionIntervalJitter);
        }

        private void Erupt()
        {
            // Big visible blast at the crater.
            if (_fireBallPrefab != null)
            {
                var fb = Object.Instantiate(_fireBallPrefab, _craterWorldPos, Quaternion.identity);
                fb.transform.localScale = Vector3.one * 3f;
                Object.Destroy(fb, 5f);
            }
            if (_bigExplosionPrefab != null)
            {
                var ex = Object.Instantiate(_bigExplosionPrefab, _craterWorldPos, Quaternion.identity);
                ex.transform.localScale = Vector3.one * 2.5f;
                Object.Destroy(ex, 4f);
            }

            // Debris: launch MagmaRock pieces upward in a cone.
            if (_debrisPrefabs == null || _debrisPrefabs.Length == 0) return;
            for (int i = 0; i < debrisPerEruption; i++)
                LaunchDebris();
        }

        private void LaunchDebris()
        {
            var prefab = _debrisPrefabs[Random.Range(0, _debrisPrefabs.Length)];
            if (prefab == null) return;

            var rock = Object.Instantiate(prefab, _craterWorldPos, Random.rotation);
            rock.name = "VolcanoDebris";
            rock.transform.localScale = Vector3.one * Random.Range(debrisMinScale, debrisMaxScale);

            // Strip any prefab colliders; we'll add a small sphere collider for cleanliness.
            foreach (var c in rock.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);
            var sc = rock.AddComponent<SphereCollider>();
            sc.radius = 0.6f;
            sc.isTrigger = false;

            var rb = rock.GetComponent<Rigidbody>();
            if (rb == null) rb = rock.AddComponent<Rigidbody>();
            rb.mass = 12f;
            rb.useGravity = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Upward velocity with a cone spread.
            Vector3 dir = Quaternion.Euler(
                Random.Range(-debrisSpreadDegrees, debrisSpreadDegrees),
                Random.Range(0f, 360f),
                Random.Range(-debrisSpreadDegrees, debrisSpreadDegrees)) * Vector3.up;
            float speed = Random.Range(debrisMinUpwardSpeed, debrisMaxUpwardSpeed);
            rb.linearVelocity = dir.normalized * speed;
            rb.angularVelocity = Random.insideUnitSphere * 8f;

            // Glowing trail so debris reads as molten rock, not just a grey blob.
            var trail = rock.AddComponent<TrailRenderer>();
            trail.time = 0.6f;
            trail.startWidth = 0.35f;
            trail.endWidth = 0.05f;
            trail.minVertexDistance = 0.15f;
            trail.material = MakeEmissiveTrailMaterial();
            trail.startColor = new Color(1f, 0.55f, 0.1f, 1f);
            trail.endColor = new Color(1f, 0.2f, 0.02f, 0f);

            // Attach small TinyFlames so each piece actually looks hot, not just a trail.
            var tinyFire = Resources.Load<GameObject>("VFX/Fire/TinyFlames");
            if (tinyFire != null)
            {
                var f = Object.Instantiate(tinyFire, rock.transform);
                f.transform.localPosition = Vector3.zero;
                f.transform.localScale = Vector3.one * 0.8f;
                foreach (var ps in f.GetComponentsInChildren<ParticleSystem>())
                {
                    var main = ps.main;
                    main.loop = true;
                }
            }

            Object.Destroy(rock, debrisLifetime);
        }

        private void SpawnLoopedVFX(string path, Vector3 worldPos, float scale)
        {
            if (string.IsNullOrEmpty(path)) return;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return;

            var go = Object.Instantiate(prefab, worldPos, Quaternion.identity, transform);
            go.transform.localScale = Vector3.one * scale;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.loop = true;
            }
            _persistentVfx.Add(go);
        }

        private void BuildLavaPool()
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "LavaPool";
            disc.transform.SetParent(transform, false);
            disc.transform.position = _craterWorldPos + Vector3.down * 0.2f;
            disc.transform.localScale = new Vector3(lavaGlowRadius * 2f, 0.15f, lavaGlowRadius * 2f);
            Object.DestroyImmediate(disc.GetComponent<Collider>());

            var rend = disc.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(1f, 0.35f, 0.05f, 1f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1.6f, 0.55f, 0.05f) * 4f);
                rend.material = mat;
            }
            _persistentVfx.Add(disc);
        }

        private static Material _trailMat;
        private static Material MakeEmissiveTrailMaterial()
        {
            if (_trailMat != null) return _trailMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            _trailMat = new Material(sh);
            _trailMat.color = new Color(1f, 0.55f, 0.1f, 1f);
            _trailMat.name = "LavaTrail";
            return _trailMat;
        }
    }
}
