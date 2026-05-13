using UnityEngine;

namespace CloseEncounters.VehiclePhysics
{
    // =========================================================================
    //  AntiPhaseCapsule — reusable swept-sphere anti-tunnel gate.
    //
    //  Vehicles at high speed outrun discrete collision even with
    //  ContinuousDynamic rigidbody mode (e.g. terrain seams, collider edges
    //  at Z scene boundaries). This helper performs a SphereCast along the
    //  velocity direction and clamps the rigidbody's linearVelocity so the
    //  vehicle stops just short of an incoming wall. A small outward impulse
    //  prevents "sticking" to a surface the vehicle is pressed against.
    // =========================================================================
    public static class AntiPhaseCapsule
    {
        // Scratch buffer for non-alloc sphere casts. 4 entries is enough for
        // picking the nearest non-self hit — more than that is noise.
        private static readonly RaycastHit[] _sweepHits = new RaycastHit[4];

        /// <summary>
        /// Sweep forward along velocity. If an imminent collision is found
        /// (within velocity * dt * safetyFactor), clamp velocity to stop just
        /// short and apply a small outward nudge. Returns true if clamped.
        /// </summary>
        /// <param name="rb">Vehicle rigidbody.</param>
        /// <param name="selfRoot">Vehicle root transform (skip any hit whose collider is a child of this).</param>
        /// <param name="radius">Chassis radius used for the swept sphere.</param>
        /// <param name="mask">Layer mask — typically ~0 with self-filter, or a curated mask.</param>
        /// <param name="safetyFactor">Multiplier on velocity*dt for the sweep distance (1.5 is safe, 2.0 is paranoid).</param>
        /// <param name="minDistance">Don't clamp if the sweep would be shorter than this (prevents jitter when stationary).</param>
        /// <param name="nudgeImpulse">Outward N·s impulse applied when clamped (keeps vehicle from adhering to wall).</param>
        public static bool GateVelocity(
            Rigidbody rb,
            Transform selfRoot,
            float radius,
            LayerMask mask,
            float safetyFactor = 1.5f,
            float minDistance  = 0.05f,
            float nudgeImpulse = 5f)
        {
            if (rb == null || selfRoot == null) return false;

            Vector3 vel = rb.linearVelocity;
            float speed = vel.magnitude;
            if (speed < 0.5f) return false;

            float dt = Time.fixedDeltaTime;
            float sweepDist = speed * dt * safetyFactor;
            if (sweepDist < minDistance) return false;

            Vector3 dir = vel / speed;
            // Origin pulled back slightly so the sphere doesn't start already overlapping
            // the chassis — avoids zero-distance hits against self-adjacent geometry.
            Vector3 origin = rb.position - dir * (radius * 0.5f);

            int hitCount = UnityEngine.Physics.SphereCastNonAlloc(
                origin, radius, dir, _sweepHits, sweepDist + radius,
                mask, QueryTriggerInteraction.Ignore);
            if (hitCount <= 0) return false;

            // Find nearest non-self hit
            float bestDist = float.PositiveInfinity;
            Vector3 bestNormal = Vector3.zero;
            bool found = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit h = _sweepHits[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(selfRoot)) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    bestNormal = h.normal;
                    found = true;
                }
            }
            if (!found) return false;

            // Clearance in front of the chassis (origin was pulled back by radius*0.5)
            float clearance = bestDist - radius * 0.5f;
            if (clearance >= sweepDist) return false;

            // Clamp velocity: keep only the component tangent to the wall, plus
            // whatever forward component fits inside the safe clearance this step.
            float maxAdvance = Mathf.Max(clearance - 0.02f, 0f); // 2 cm skin
            float allowedForward = maxAdvance / Mathf.Max(dt, 1e-4f);
            Vector3 forwardComp = Vector3.Project(vel, dir);
            Vector3 lateralComp = vel - forwardComp;
            Vector3 clampedForward = dir * Mathf.Min(forwardComp.magnitude, allowedForward);
            rb.linearVelocity = lateralComp + clampedForward;

            // Small outward nudge along wall normal — prevents stick without bouncing
            if (nudgeImpulse > 0f && bestNormal.sqrMagnitude > 0.01f)
                rb.AddForce(bestNormal * nudgeImpulse, ForceMode.Impulse);

            return true;
        }
    }
}
