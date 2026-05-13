using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Spawns tornado/dust devil prefabs at random positions within a radius,
    /// each lasting a random duration before fading out and respawning elsewhere.
    /// Applies Yughues sand texture to all particle renderers.
    /// </summary>
    public class TornadoSpawner : MonoBehaviour
    {
        public string prefabPath = "Desert/Tornado/ToonTornadoEfc";
        public float scale = 0.15f;
        public float spawnRadius = 150f;
        public float minInterval = 10f;
        public float maxInterval = 30f;
        public float minLifetime = 8f;
        public float maxLifetime = 20f;
        public int maxActive = 2;

        private GameObject[] _active;
        private float[] _timers;
        private float[] _lifetimes;
        private Texture2D _sandTexture;

        private void Start()
        {
            _active = new GameObject[maxActive];
            _timers = new float[maxActive];
            _lifetimes = new float[maxActive];

            _sandTexture = Resources.Load<Texture2D>("Textures/SandParticle");

            // Stagger initial spawns
            for (int i = 0; i < maxActive; i++)
            {
                _timers[i] = Random.Range(0f, minInterval * 0.5f);
                _lifetimes[i] = 0f;
            }
        }

        private void Update()
        {
            for (int i = 0; i < maxActive; i++)
            {
                if (_active[i] != null)
                {
                    // Count down lifetime
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
                    // Count down spawn timer
                    _timers[i] -= Time.deltaTime;
                    if (_timers[i] <= 0f)
                    {
                        SpawnTornado(i);
                    }
                }
            }
        }

        private void SpawnTornado(int slot)
        {
            var prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab == null) return;

            // Random position within radius on the ground plane
            Vector2 rng = Random.insideUnitCircle * spawnRadius;
            Vector3 pos = transform.position + new Vector3(rng.x, 0f, rng.y);

            var go = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.transform.localScale = Vector3.one * scale;
            go.name = $"Tornado_{slot}";

            // Apply Yughues sand texture to all particle renderers
            if (_sandTexture != null)
            {
                foreach (var pr in go.GetComponentsInChildren<ParticleSystemRenderer>())
                {
                    if (pr.material != null)
                    {
                        pr.material.mainTexture = _sandTexture;
                        pr.material.color = new Color(0.85f, 0.72f, 0.50f, 0.6f);
                    }
                }
            }

            // Ensure looping
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.loop = true;
            }

            // Fix URP materials
            CityPrefabHelper.FixURPMaterials(go.transform);

            _active[slot] = go;
            _lifetimes[slot] = Random.Range(minLifetime, maxLifetime);
        }
    }
}
