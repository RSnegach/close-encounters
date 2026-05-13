using System.Reflection;
using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Spawns Habrador-style physics tornadoes. On instantiate, migrates the
    /// prefab's legacy shaders to URP (Habrador's 2016 assets use Built-in
    /// shaders that render pink under URP), slows the chase speed, and attaches
    /// a TornadoSuction component so the tornado lifts and scatters Rigidbodies.
    ///
    /// Expected prefab path: Resources/HabradorTornado/TornadoPrefab
    /// </summary>
    public class HabradorTornadoSpawner : MonoBehaviour
    {
        public string prefabPath = "HabradorTornado/TornadoPrefab";
        public float scale = 1f;
        public float spawnRadius = 150f;
        public float minInterval = 60f;
        public float maxInterval = 60f;
        public float minLifetime = 40f;
        public float maxLifetime = 55f;
        public int maxActive = 1;

        [Header("Tornado overrides")]
        public float tornadoSpeed = 15f;
        public float suctionRadius = 18f;
        public float suctionHeight = 40f;
        public float suctionStrength = 45f;

        private GameObject[] _active;
        private float[] _timers;
        private float[] _lifetimes;
        private GameObject _cachedPrefab;
        private bool _prefabMissingLogged;
        private static Shader _urpLit;
        private static Shader _urpUnlit;
        private static Shader _urpParticlesUnlit;

        private void Start()
        {
            _active = new GameObject[maxActive];
            _timers = new float[maxActive];
            _lifetimes = new float[maxActive];

            _cachedPrefab = Resources.Load<GameObject>(prefabPath);
            if (_cachedPrefab == null && !_prefabMissingLogged)
            {
                Debug.LogWarning($"[HabradorTornadoSpawner] Prefab not found at Resources/{prefabPath}.");
                _prefabMissingLogged = true;
            }

            for (int i = 0; i < maxActive; i++)
            {
                _timers[i] = Random.Range(0f, minInterval * 0.5f);
                _lifetimes[i] = 0f;
            }
        }

        private void Update()
        {
            if (_cachedPrefab == null) return;

            for (int i = 0; i < maxActive; i++)
            {
                if (_active[i] != null)
                {
                    _lifetimes[i] -= Time.deltaTime;
                    if (_lifetimes[i] <= 0f)
                    {
                        Destroy(_active[i]);
                        _active[i] = null;
                        _timers[i] = Random.Range(minInterval, maxInterval);
                    }
                }
                else
                {
                    _timers[i] -= Time.deltaTime;
                    if (_timers[i] <= 0f)
                        SpawnTornado(i);
                }
            }
        }

        private void SpawnTornado(int slot)
        {
            if (_cachedPrefab == null) return;

            // Pick a spawn position clear of mountains/rocks. 8 tries, then fallback.
            Vector3 pos = transform.position;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 rng = Random.insideUnitCircle * spawnRadius;
                Vector3 candidate = transform.position + new Vector3(rng.x, 0f, rng.y);
                if (IsSpawnClear(candidate))
                {
                    pos = candidate;
                    break;
                }
            }

            var go = Instantiate(_cachedPrefab, pos, Quaternion.identity);
            go.transform.localScale = Vector3.one * scale;
            go.name = $"HabradorTornado_{slot}";

            MigrateToURP(go);
            OverrideTornadoSpeed(go);
            AttachSuction(go);

            _active[slot] = go;
            _lifetimes[slot] = Random.Range(minLifetime, maxLifetime);
        }

        // Walks every Renderer under the spawned tornado and rebinds shaders
        // to URP equivalents so Habrador's 2016-era materials don't render pink.
        private static void MigrateToURP(GameObject root)
        {
            if (_urpLit == null) _urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (_urpUnlit == null) _urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (_urpParticlesUnlit == null)
                _urpParticlesUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].sharedMaterials;
                bool isParticle = renderers[i] is ParticleSystemRenderer || renderers[i] is TrailRenderer;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null || mat.shader == null) continue;
                    string name = mat.shader.name;
                    if (name.StartsWith("Universal Render Pipeline")) continue;

                    Shader target;
                    if (isParticle || name.Contains("Particle"))
                        target = _urpParticlesUnlit;
                    else if (name.Contains("Unlit"))
                        target = _urpUnlit;
                    else
                        target = _urpLit;

                    if (target != null)
                        mat.shader = target;
                }
            }
        }

        // Habrador's Tornado class is in the global namespace. We reflect into it
        // so this file doesn't need to #include/compile-depend on their scripts,
        // which live in the Assets/HabradorTornado folder and may be deleted later.
        private void OverrideTornadoSpeed(GameObject root)
        {
            var tornado = FindComponentByTypeName(root, "Tornado");
            if (tornado == null) return;
            SetField(tornado, "tornadoSpeed", tornadoSpeed);
        }

        private void AttachSuction(GameObject root)
        {
            var suction = root.AddComponent<TornadoSuction>();
            suction.radius = suctionRadius;
            suction.height = suctionHeight;
            suction.strength = suctionStrength;
        }

        private static bool IsSpawnClear(Vector3 pos)
        {
            var hits = Physics.OverlapSphere(pos + Vector3.up * 5f, 20f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                string n = hits[i].transform.root.name;
                if (n.Contains("Mountain") || n.Contains("Cliff") ||
                    n.Contains("Rock") || n.Contains("Cactus") ||
                    n.Contains("Boulder"))
                    return false;
            }
            return true;
        }

        private static Component FindComponentByTypeName(GameObject root, string typeName)
        {
            var comps = root.GetComponents<MonoBehaviour>();
            for (int i = 0; i < comps.Length; i++)
                if (comps[i] != null && comps[i].GetType().Name == typeName) return comps[i];
            return null;
        }

        private static void SetField(Component c, string fieldName, object value)
        {
            var f = c.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) f.SetValue(c, value);
        }

        private void OnDestroy()
        {
            if (_active == null) return;
            for (int i = 0; i < _active.Length; i++)
                if (_active[i] != null) Destroy(_active[i]);
        }
    }
}
