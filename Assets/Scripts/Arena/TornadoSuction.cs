using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Applies swirl + lift + inward force to Rigidbodies near the tornado.
    /// Breaks any nearby BreakableProp so it becomes a physics body we can lift.
    /// Attach at runtime from HabradorTornadoSpawner.
    /// </summary>
    public class TornadoSuction : MonoBehaviour
    {
        public float radius = 18f;
        public float height = 40f;
        public float strength = 45f;
        public float swirlSpeed = 4f;
        public float liftFraction = 0.6f;
        public float scanInterval = 0.2f;

        private static readonly Collider[] _overlapBuffer = new Collider[64];
        private float _scanTimer;

        private void FixedUpdate()
        {
            _scanTimer -= Time.fixedDeltaTime;
            if (_scanTimer > 0f) return;
            _scanTimer = scanInterval;

            Vector3 center = transform.position + Vector3.up * (height * 0.5f);
            int count = Physics.OverlapSphereNonAlloc(
                center, radius, _overlapBuffer, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var col = _overlapBuffer[i];
                if (col == null) continue;

                // Break any standing prop so it has free physics to be picked up.
                var breakable = col.GetComponentInParent<BreakableProp>();
                if (breakable != null)
                {
                    Vector3 pushDir = (col.transform.position - transform.position).normalized + Vector3.up * 0.3f;
                    breakable.BreakFree(pushDir * 8f);
                }

                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;

                ApplyTornadoForce(rb);
            }
        }

        private void ApplyTornadoForce(Rigidbody rb)
        {
            Vector3 tornadoAxis = transform.position;
            Vector3 toAxis = new Vector3(tornadoAxis.x - rb.position.x, 0f, tornadoAxis.z - rb.position.z);
            float distFromAxis = toAxis.magnitude;
            if (distFromAxis < 0.01f) return;

            float distFrac = Mathf.Clamp01(distFromAxis / radius);
            float heightFrac = Mathf.Clamp01((rb.position.y - transform.position.y) / Mathf.Max(height, 0.01f));

            // Inward pull scales up near the edge (max at rim), down near the core.
            Vector3 inward = (toAxis / distFromAxis) * strength * (1f - Mathf.Abs(distFrac - 0.8f));

            // Tangential swirl (perpendicular to inward, horizontal).
            Vector3 tangent = Vector3.Cross(Vector3.up, toAxis / distFromAxis);
            Vector3 swirl = tangent * strength * swirlSpeed * (1f - heightFrac * 0.5f);

            // Lift gets weaker with altitude so objects eventually fling out the top.
            Vector3 lift = Vector3.up * strength * liftFraction * (1f - heightFrac);

            rb.AddForce(inward + swirl + lift, ForceMode.Acceleration);
        }
    }
}
