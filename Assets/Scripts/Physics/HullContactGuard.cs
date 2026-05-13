using UnityEngine;

namespace CloseEncounters.VehiclePhysics
{
    // =========================================================================
    //  HullContactGuard — lateral sweep + depenetration solver.
    //
    //  Runs alongside WaterPhysics / AntiPhaseCapsule. Two jobs:
    //    1. Sphere-sweep in lateral/vertical directions and clamp velocity so
    //       boats can't slide sideways or climb up onto obstacles.
    //    2. OverlapBox around the hull bounds each FixedUpdate. For every
    //       foreign collider overlapping the hull, call Physics.ComputePenetration
    //       and apply the MTV as a position offset so drift-clipping gets
    //       resolved regardless of velocity direction.
    // =========================================================================
    [DisallowMultipleComponent]
    public class HullContactGuard : MonoBehaviour
    {
        [Header("Hull Volume")]
        [Tooltip("Fallback hull radius if child colliders can't be measured at Awake.")]
        public float hullRadius = 3.5f;

        [Header("Masks")]
        [Tooltip("Layers treated as solid obstacles. Water volumes should be triggers and are ignored anyway.")]
        public LayerMask obstacleMask = ~0;

        [Header("Depenetration")]
        [Tooltip("0 = no push, 1 = snap out. 0.7 resolves fast without jitter.")]
        [Range(0f, 1f)]
        public float pushLerp = 0.7f;
        [Tooltip("Cap on position correction per FixedUpdate. Prevents teleporting across the map.")]
        public float maxPushPerFrame = 2f;
        [Tooltip("Seconds between depenetration passes. 0 = every FixedUpdate.")]
        public float updateInterval = 0f;
        [Tooltip("Zero velocity along the push direction so the boat stops dead rather than bouncing.")]
        public bool zeroVelocityAlongPush = true;

        [Header("Lateral Sweep")]
        [Tooltip("Sweep left/right of velocity and clamp sideways drift.")]
        public bool enableLateralSweep = true;
        [Tooltip("Multiplier on velocity*dt for lateral sweep distance.")]
        public float lateralSweepSafetyFactor = 1.75f;
        [Tooltip("Also sweep downward so boats don't ride up onto landmasses.")]
        public bool enableDownSweep = true;

        // Scratch buffers — preallocated, no GC in FixedUpdate
        private readonly Collider[] _overlapBuf = new Collider[16];
        private static readonly RaycastHit[] _lateralSweepBuf = new RaycastHit[4];

        private Rigidbody _rb;
        private Transform _selfRoot;
        private Collider[] _selfColliders;
        private Vector3 _hullHalfExtentsLocal;
        private Vector3 _hullCenterLocal;
        private bool _hullBoundsValid;
        private float _nextUpdateTime;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _selfRoot = transform;
            _selfColliders = GetComponentsInChildren<Collider>(includeInactive: false);
            ComputeHullBounds();
        }

        private void ComputeHullBounds()
        {
            if (_selfColliders == null || _selfColliders.Length == 0)
            {
                _hullBoundsValid = false;
                return;
            }

            Bounds combined = default;
            bool has = false;
            for (int i = 0; i < _selfColliders.Length; i++)
            {
                Collider c = _selfColliders[i];
                if (c == null || c.isTrigger) continue;
                Bounds b = c.bounds; // world-space at current pose
                if (!has) { combined = b; has = true; }
                else combined.Encapsulate(b);
            }

            if (!has)
            {
                _hullBoundsValid = false;
                return;
            }

            // Convert world-space bounds into local-space via inverse transform of the extents.
            // Since hull rotates with the boat, we record the local center/extents once.
            Vector3 worldCenter = combined.center;
            Vector3 worldExtents = combined.extents;

            _hullCenterLocal = _selfRoot.InverseTransformPoint(worldCenter);
            // why: extents in world were axis-aligned — shrink slightly so overlap test
            // doesn't perma-trigger on the ground plane under the hull
            Vector3 lossy = _selfRoot.lossyScale;
            _hullHalfExtentsLocal = new Vector3(
                Mathf.Max(0.1f, worldExtents.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x))),
                Mathf.Max(0.1f, worldExtents.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y))),
                Mathf.Max(0.1f, worldExtents.z / Mathf.Max(0.001f, Mathf.Abs(lossy.z))));
            _hullHalfExtentsLocal *= 0.95f;
            _hullBoundsValid = true;
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;

            if (enableLateralSweep)
                RunLateralSweeps();

            if (updateInterval > 0f)
            {
                if (Time.fixedTime < _nextUpdateTime) return;
                _nextUpdateTime = Time.fixedTime + updateInterval;
            }

            ResolveHullPenetration();
        }

        // =====================================================================
        //  Lateral/vertical sweep — clamp sideways velocity into obstacles
        // =====================================================================

        private void RunLateralSweeps()
        {
            Vector3 vel = _rb.linearVelocity;
            float speed = vel.magnitude;
            if (speed < 0.25f) return;

            float dt = Time.fixedDeltaTime;
            float sweepDist = speed * dt * lateralSweepSafetyFactor;
            if (sweepDist < 0.05f) return;

            // Project velocity onto each lateral axis — only sweep if moving that way
            Vector3 right = _selfRoot.right;
            float rightSpeed = Vector3.Dot(vel, right);
            if (Mathf.Abs(rightSpeed) > 0.25f)
                ClampAlongAxis(right * Mathf.Sign(rightSpeed), Mathf.Abs(rightSpeed), dt);

            if (enableDownSweep)
            {
                float downSpeed = -vel.y;
                if (downSpeed > 0.25f)
                    ClampAlongAxis(Vector3.down, downSpeed, dt);
            }
        }

        private void ClampAlongAxis(Vector3 dir, float axisSpeed, float dt)
        {
            float sweepDist = axisSpeed * dt * lateralSweepSafetyFactor;
            if (sweepDist < 0.05f) return;

            Vector3 origin = _rb.position - dir * (hullRadius * 0.5f);
            int hitCount = Physics.SphereCastNonAlloc(
                origin, hullRadius, dir, _lateralSweepBuf, sweepDist + hullRadius,
                obstacleMask, QueryTriggerInteraction.Ignore);
            if (hitCount <= 0) return;

            float bestDist = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit h = _lateralSweepBuf[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(_selfRoot)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; found = true; }
            }
            if (!found) return;

            float clearance = bestDist - hullRadius * 0.5f;
            if (clearance >= sweepDist) return;

            // Zero out the component of velocity along this axis (keep tangential)
            Vector3 vel = _rb.linearVelocity;
            float along = Vector3.Dot(vel, dir);
            if (along <= 0f) return;
            float maxAdvance = Mathf.Max(clearance - 0.02f, 0f);
            float allowed = maxAdvance / Mathf.Max(dt, 1e-4f);
            float clampedAlong = Mathf.Min(along, allowed);
            _rb.linearVelocity = vel - dir * (along - clampedAlong);
        }

        // =====================================================================
        //  Depenetration — OverlapBox + ComputePenetration, sum MTVs
        // =====================================================================

        private void ResolveHullPenetration()
        {
            Vector3 center;
            Vector3 halfExtents;
            Quaternion rot = _selfRoot.rotation;

            if (_hullBoundsValid)
            {
                center = _selfRoot.TransformPoint(_hullCenterLocal);
                halfExtents = _hullHalfExtentsLocal;
            }
            else
            {
                center = _rb.position;
                halfExtents = new Vector3(hullRadius, hullRadius, hullRadius);
            }

            int hitCount = Physics.OverlapBoxNonAlloc(
                center, halfExtents, _overlapBuf, rot,
                obstacleMask, QueryTriggerInteraction.Ignore);
            if (hitCount <= 0) return;

            Vector3 summedPush = Vector3.zero;
            Vector3 largestPush = Vector3.zero;
            float largestMag = 0f;

            for (int i = 0; i < hitCount; i++)
            {
                Collider other = _overlapBuf[i];
                if (other == null) continue;
                if (other.isTrigger) continue;
                if (other.transform.IsChildOf(_selfRoot)) continue;

                // Pick any non-trigger self collider to test against — ComputePenetration
                // works collider-to-collider and we want the MTV for this foreign hit.
                for (int s = 0; s < _selfColliders.Length; s++)
                {
                    Collider self = _selfColliders[s];
                    if (self == null || self.isTrigger || !self.enabled) continue;

                    if (Physics.ComputePenetration(
                            self, self.transform.position, self.transform.rotation,
                            other, other.transform.position, other.transform.rotation,
                            out Vector3 dir, out float dist))
                    {
                        if (dist <= 0f) continue;
                        Vector3 push = dir * dist;
                        summedPush += push;
                        float mag = dist;
                        if (mag > largestMag) { largestMag = mag; largestPush = push; }
                        break; // one MTV per foreign hit is enough
                    }
                }
            }

            if (summedPush.sqrMagnitude < 1e-8f) return;

            // why: pinned between two obstacles sums can cancel — fall back to largest single MTV
            Vector3 applied = summedPush;
            if (applied.sqrMagnitude < largestPush.sqrMagnitude * 0.25f)
                applied = largestPush;

            applied *= pushLerp;
            float appliedMag = applied.magnitude;
            if (appliedMag > maxPushPerFrame)
                applied *= (maxPushPerFrame / appliedMag);

            _rb.position += applied;

            if (zeroVelocityAlongPush)
            {
                Vector3 pushDir = applied.normalized;
                Vector3 vel = _rb.linearVelocity;
                float into = Vector3.Dot(vel, -pushDir);
                if (into > 0f)
                    _rb.linearVelocity = vel + pushDir * into;
            }
        }

        // =====================================================================
        //  Debug
        // =====================================================================

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            if (!_hullBoundsValid) return;
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.35f);
            Matrix4x4 prev = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                _selfRoot.TransformPoint(_hullCenterLocal),
                _selfRoot.rotation,
                Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, _hullHalfExtentsLocal * 2f);
            Gizmos.matrix = prev;
        }
    }
}
