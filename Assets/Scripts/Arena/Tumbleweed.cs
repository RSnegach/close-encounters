using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// A field of cosmetic tumbleweeds that roll across the desert floor on the wind
    /// and wrap back to the upwind edge when they leave the play area. No colliders —
    /// pure ambiance, so they never trap or deflect vehicles. Build with Create().
    /// </summary>
    public class TumbleweedField : MonoBehaviour
    {
        private Transform[] _weeds;
        private float[] _spin;
        private Vector3 _wind;       // horizontal drift direction * speed
        private Vector3 _center;
        private float _playRadius;
        private float _groundY;

        public static TumbleweedField Create(Transform parent, Vector3 center, int count,
            float playRadius, Vector3 windDir, float groundY)
        {
            var go = new GameObject("TumbleweedField");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var f = go.AddComponent<TumbleweedField>();
            f.Init(center, count, playRadius, windDir, groundY);
            return f;
        }

        private void Init(Vector3 center, int count, float playRadius, Vector3 windDir, float groundY)
        {
            _center = center;
            _playRadius = Mathf.Max(20f, playRadius);
            _groundY = groundY;

            Vector3 wd = windDir; wd.y = 0f;
            if (wd.sqrMagnitude < 0.001f) wd = Vector3.right;
            _wind = wd.normalized * 16f;

            count = Mathf.Max(1, count);
            _weeds = new Transform[count];
            _spin = new float[count];

            var urp = Shader.Find("Universal Render Pipeline/Lit");
            Material mat = urp != null ? new Material(urp) : null;
            if (mat != null)
            {
                mat.SetColor("_BaseColor", new Color(0.55f, 0.45f, 0.27f)); // dry brush tan
                mat.SetFloat("_Smoothness", 0.08f);
            }

            for (int i = 0; i < count; i++)
            {
                Transform w = BuildWeed(mat, Random.Range(1.2f, 2.0f));
                w.SetParent(transform, false);
                w.position = RandomStart();
                _weeds[i] = w;
                _spin[i] = Random.Range(160f, 300f) * (Random.value > 0.5f ? 1f : -1f);
            }
        }

        private static Transform BuildWeed(Material mat, float size)
        {
            var root = new GameObject("Tumbleweed").transform;
            // A tangle of crossed twigs reads as a brushy ball when rolling.
            for (int i = 0; i < 5; i++)
            {
                var twig = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(twig.GetComponent<Collider>());
                twig.transform.SetParent(root, false);
                twig.transform.localScale = new Vector3(0.08f, 0.08f, size);
                twig.transform.localRotation = Random.rotation;
                if (mat != null) twig.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            return root;
        }

        private Vector3 RandomStart()
        {
            Vector3 up = -_wind.normalized;                  // upwind edge
            Vector3 lat = Vector3.Cross(Vector3.up, up);
            return _center + up * _playRadius
                 + lat * Random.Range(-_playRadius, _playRadius)
                 + Vector3.up * _groundY;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 rollAxis = Vector3.Cross(Vector3.up, _wind.normalized);
            for (int i = 0; i < _weeds.Length; i++)
            {
                var w = _weeds[i];
                if (w == null) continue;
                Vector3 p = w.position + _wind * dt;
                p.y = _groundY;
                w.position = p;
                w.Rotate(rollAxis, _spin[i] * dt, Space.World);

                float dx = p.x - _center.x, dz = p.z - _center.z;
                if (dx * dx + dz * dz > _playRadius * _playRadius)
                    w.position = RandomStart();
            }
        }
    }
}
