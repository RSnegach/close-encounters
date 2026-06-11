using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Cosmetic flock of gulls that circle high above a point, banking and flapping.
    /// Purely visual ambiance — birds fly well above gameplay altitude and have no
    /// colliders. Build with SeabirdFlock.Create().
    /// </summary>
    public class SeabirdFlock : MonoBehaviour
    {
        private Transform[] _birds;
        private float[] _phase, _radius, _height, _speed;
        private Vector3 _center;

        public static SeabirdFlock Create(Transform parent, Vector3 center, int count, float spread, Color? color = null)
        {
            var go = new GameObject("SeabirdFlock");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var f = go.AddComponent<SeabirdFlock>();
            f.Init(center, count, spread, color ?? new Color(0.92f, 0.92f, 0.95f));
            return f;
        }

        private void Init(Vector3 center, int count, float spread, Color color)
        {
            _center = center;
            count = Mathf.Max(1, count);
            _birds  = new Transform[count];
            _phase  = new float[count];
            _radius = new float[count];
            _height = new float[count];
            _speed  = new float[count];

            var urp = Shader.Find("Universal Render Pipeline/Lit");
            var mat = urp != null ? new Material(urp) : null;
            if (mat != null) mat.SetColor("_BaseColor", color);

            for (int i = 0; i < count; i++)
            {
                var b = new GameObject($"Gull_{i}");
                b.transform.SetParent(transform, false);
                CreateWing(b.transform, mat, -1f);
                CreateWing(b.transform, mat,  1f);
                _birds[i]  = b.transform;
                _phase[i]  = Random.Range(0f, 6.283f);
                _radius[i] = Random.Range(spread * 0.4f, spread);
                _height[i] = Random.Range(30f, 60f);
                _speed[i]  = Random.Range(0.14f, 0.30f) * (Random.value > 0.5f ? 1f : -1f);
            }
        }

        private static void CreateWing(Transform parent, Material mat, float side)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(w.GetComponent<Collider>());
            w.transform.SetParent(parent, false);
            w.transform.localScale = new Vector3(2.4f, 0.12f, 0.55f);
            w.transform.localPosition = new Vector3(side * 1.3f, 0f, 0f);
            w.transform.localRotation = Quaternion.Euler(0f, 0f, side * 18f);
            if (mat != null) w.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private void Update()
        {
            float t = Time.time;
            for (int i = 0; i < _birds.Length; i++)
            {
                var bt = _birds[i];
                if (bt == null) continue;
                float a = _phase[i] + t * _speed[i];
                float x = _center.x + Mathf.Cos(a) * _radius[i];
                float z = _center.z + Mathf.Sin(a) * _radius[i];
                float y = _height[i] + Mathf.Sin(t * 0.8f + _phase[i]) * 2.5f;
                Vector3 pos = new Vector3(x, y, z);

                Vector3 fwd = pos - bt.position;
                bt.position = pos;
                if (fwd.sqrMagnitude > 1e-4f)
                    bt.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

                // Wing flap
                float flap = Mathf.Sin(t * 6f + _phase[i]) * 15f;
                if (bt.childCount >= 2)
                {
                    bt.GetChild(0).localRotation = Quaternion.Euler(0f, 0f,  18f + flap);
                    bt.GetChild(1).localRotation = Quaternion.Euler(0f, 0f, -18f - flap);
                }
            }
        }
    }
}
