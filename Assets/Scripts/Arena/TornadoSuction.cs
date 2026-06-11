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

                // Air vehicles: apply turbulence through their controller
                var airCtrl = rb.GetComponent<CloseEncounters.Combat.PlayerVehicleController>();
                if (airCtrl != null && airCtrl.IsAirMode)
                {
                    Vector3 tornadoForce = ComputeTornadoForce(rb);
                    airCtrl.ApplyTurbulence(tornadoForce, scanInterval + 0.1f);
                    continue;
                }

                // AI vehicles: nudge velocity directly (their physics controllers
                // otherwise damp out AddForce), but at parity with physics bodies and
                // with only a slight wobble — the old 3x push + ±5°/frame spin made the
                // tornado fling bots around uncontrollably and feel unfair.
                var aiCtrl = rb.GetComponent<CloseEncounters.AI.AIController>();
                if (aiCtrl != null)
                {
                    Vector3 tornadoForce = ComputeTornadoForce(rb);
                    rb.linearVelocity += tornadoForce * Time.fixedDeltaTime;
                    rb.MoveRotation(rb.rotation * Quaternion.Euler(
                        Random.Range(-1.5f, 1.5f), Random.Range(-1.5f, 1.5f), Random.Range(-1.5f, 1.5f)));
                    continue;
                }

                ApplyTornadoForce(rb);
            }
        }

        private Vector3 ComputeTornadoForce(Rigidbody rb)
        {
            Vector3 tornadoAxis = transform.position;
            Vector3 toAxis = new Vector3(tornadoAxis.x - rb.position.x, 0f, tornadoAxis.z - rb.position.z);
            float distFromAxis = toAxis.magnitude;
            if (distFromAxis < 0.01f) return Vector3.zero;

            float distFrac = Mathf.Clamp01(distFromAxis / radius);
            float heightFrac = Mathf.Clamp01((rb.position.y - transform.position.y) / Mathf.Max(height, 0.01f));

            Vector3 inward = (toAxis / distFromAxis) * strength * (1f - Mathf.Abs(distFrac - 0.8f));
            Vector3 tangent = Vector3.Cross(Vector3.up, toAxis / distFromAxis);
            Vector3 swirl = tangent * strength * swirlSpeed * (1f - heightFrac * 0.5f);
            Vector3 lift = Vector3.up * strength * liftFraction * (1f - heightFrac);

            return inward + swirl + lift;
        }

        private void ApplyTornadoForce(Rigidbody rb)
        {
            rb.AddForce(ComputeTornadoForce(rb), ForceMode.Acceleration);
        }
    }
}
