using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Props with this component stand upright (kinematic Rigidbody) until hit
    /// by a vehicle or projectile, then break free and tumble under gravity.
    /// After despawnDelay the object is destroyed; if a respawner is attached
    /// the prop is re-created at its original transform after respawnCooldown.
    /// </summary>
    public class BreakableProp : MonoBehaviour
    {
        [Tooltip("Seconds the broken prop tumbles before it's destroyed. 12-15s is a good range.")]
        public float despawnDelay = 12f;

        [Tooltip("Seconds after despawn before a fresh prop is spawned at the original spot.")]
        public float respawnCooldown = 30f;

        private bool _isFree;
        private Rigidbody _rb;
        private BreakablePropRespawner _respawner;
        private int _recordId;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Called by the respawner right after Register() so we know where to
        /// report when we break. Optional — props without a respawner simply
        /// don't regenerate.
        /// </summary>
        public void AttachRespawner(BreakablePropRespawner respawner, int recordId)
        {
            _respawner = respawner;
            _recordId = recordId;
        }

        /// <summary>
        /// Break the prop free from its tether. It becomes a physics object
        /// and tumbles away from the impact.
        /// </summary>
        public void BreakFree(Vector3 impactForce)
        {
            if (_isFree) return;
            _isFree = true;

            gameObject.isStatic = false;

            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.AddForce(impactForce, ForceMode.Impulse);
                _rb.AddTorque(Random.insideUnitSphere * impactForce.magnitude * 0.3f, ForceMode.Impulse);
            }

            // Unparent so it doesn't stay in the static arena hierarchy
            transform.SetParent(null, true);

            // Schedule respawn — cooldown is measured from despawn time so a
            // still-tumbling prop isn't doubled up by a fresh one.
            if (_respawner != null && _recordId > 0)
            {
                _respawner.ScheduleRespawn(_recordId, despawnDelay + respawnCooldown);
            }

            Destroy(gameObject, despawnDelay);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_isFree) return;

            // Break when a vehicle drives into us
            var vr = collision.collider.GetComponentInParent<VehicleRuntime>();
            if (vr == null) return;

            // Mass-proportional response: light objects fly, heavy objects topple gently
            // A 5kg bush gets force/5 = high acceleration
            // A 100kg pole gets force/100 = gentle topple
            float mass = _rb != null ? _rb.mass : 10f;
            // Small props (1-3kg): forceMag = 60-180, fly far
            // Medium props (10-30kg): forceMag = 6-18, tumble nicely
            // Large props (80-150kg): forceMag = 1-2, barely budge
            float basePower = 180f;
            float forceMag = basePower / Mathf.Max(mass, 0.5f);
            forceMag = Mathf.Clamp(forceMag, 1f, 50f);

            Vector3 pushDir = transform.position - collision.GetContact(0).point;
            pushDir.y = Mathf.Max(pushDir.y, 0.5f); // upward launch bias
            BreakFree(pushDir.normalized * forceMag);
        }
    }
}
