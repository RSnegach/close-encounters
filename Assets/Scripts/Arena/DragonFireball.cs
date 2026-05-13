using UnityEngine;
using CloseEncounters.Combat;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Straight-line fire projectile launched by the dragon's Drakaris attack.
    /// Travels fast in a fixed direction with fire/smoke trail VFX.
    /// Explodes on impact with terrain, vehicles, or anything solid.
    /// </summary>
    public class DragonFireball : MonoBehaviour
    {
        public Vector3 direction;
        public int damage = 80;
        public float speed = 25f;
        public float lifetime = 4f;
        public float explosionRadius = 15f;

        private float _timer;
        private Vector3 _prevPos;
        private GameObject _fireTrail;
        private GameObject _smokeTrail;
        private float _trailTimer;

        private void Start()
        {
            _prevPos = transform.position;

            // Visual: glowing orange sphere
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.transform.SetParent(transform, false);
            visual.transform.localScale = Vector3.one * 1.2f;
            var rend = visual.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(1f, 0.5f, 0.1f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1f, 0.4f, 0.05f) * 3f);
                rend.material = mat;
            }
            Object.Destroy(visual.GetComponent<Collider>());

            // Trigger collider for hit detection
            var col = gameObject.AddComponent<SphereCollider>();
            col.radius = 1.5f;
            col.isTrigger = true;

            // Initial fire burst at mouth
            VFXManager.MediumFlames(transform.position, 1f);
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= lifetime)
            {
                Explode();
                return;
            }

            // Move in straight line
            _prevPos = transform.position;
            transform.position += direction * speed * Time.deltaTime;

            // Spawn fire/smoke trail every 0.1s
            _trailTimer += Time.deltaTime;
            if (_trailTimer >= 0.1f)
            {
                _trailTimer = 0f;
                VFXManager.TinyFlames(transform.position, 0.4f);
            }

            // Raycast sweep for fast projectile (same pattern as Projectile.cs)
            Vector3 delta = transform.position - _prevPos;
            float dist = delta.magnitude;
            if (dist > 0.01f && Physics.Raycast(_prevPos, delta.normalized, out RaycastHit hit, dist,
                ~0, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point;

                // Damage vehicle if hit directly
                var vr = hit.collider.GetComponentInParent<VehicleRuntime>();
                if (vr != null && vr.IsAlive)
                    DamageSystem.DealDamageToVehicle(vr, damage, hit.point, skipControlParts: true);

                Explode();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Don't hit the dragon itself
            if (other.GetComponentInParent<DragonBoss>() != null) return;
            if (other.GetComponentInParent<DragonHealth>() != null) return;

            var vr = other.GetComponentInParent<VehicleRuntime>();
            if (vr != null && vr.IsAlive)
                DamageSystem.DealDamageToVehicle(vr, damage, transform.position, skipControlParts: true);

            Explode();
        }

        private void Explode()
        {
            // Big cinematic explosion
            VFXManager.BigExplosion(transform.position, 2f);
            VFXManager.LargeFlames(transform.position, 1.5f);

            // Area damage at impact
            DamageSystem.DealAreaDamage(transform.position, explosionRadius, damage);

            Destroy(gameObject);
        }
    }
}
