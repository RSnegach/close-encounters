using UnityEngine;
using CloseEncounters.Vehicle;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// FPS-style third-person combat camera.
    /// Mouse locked+hidden. Mouse delta = camera yaw/pitch.
    /// WASD drives vehicle independently. Camera orbits behind vehicle.
    /// Aim point = raycast from camera through screen center.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;

        [Header("Orbit")]
        public float distance = 14f;
        public float heightOffset = 5f;
        public float positionSmooth = 10f;

        [Header("Mouse Sensitivity")]
        public float sensitivityX = 2.5f;
        public float sensitivityY = 2.0f;

        [Header("Pitch Limits")]
        public float pitchMin = -20f;
        public float pitchMax = 60f;

        [Header("Aim")]
        public float aimRayDistance = 500f;
        public LayerMask aimMask = ~0;

        [Header("Air FOV")]
        public float baseFOV = 60f;
        public float maxFOV = 80f; // why: clamp at 80 to avoid fisheye distortion on URP camera
        public float fovSmoothTime = 0.25f;
        public float aimZoomFOV = 30f; // why: 2× zoom-in on right-click for serious aiming
        private float _fovVel;

        // ── Public outputs ───────────────────────────────────────────────
        public Vector3 AimPoint { get; private set; }
        public Vector3 AimDirection { get; private set; }
        public float CameraYaw { get; private set; }
        public float CameraPitch { get; private set; }

        // ── Internal ─────────────────────────────────────────────────────
        private float _yaw;
        private float _pitch;
        private Camera _cam;
        private bool _cursorUnlocked;

        private void Start()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;

            if (target != null)
                _yaw = target.eulerAngles.y;

            LockCursorInternal();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // Keep cursor locked during gameplay
            if (!_cursorUnlocked && Cursor.lockState != CursorLockMode.Locked)
                LockCursorInternal();

            float dt = Time.unscaledDeltaTime; // use unscaled so pause doesn't block
            if (Time.timeScale <= 0f) return;   // but don't move camera while paused

            // Read mouse
            float mx = Input.GetAxisRaw("Mouse X") * sensitivityX;
            float my = Input.GetAxisRaw("Mouse Y") * sensitivityY;
            _yaw += mx;
            _pitch = Mathf.Clamp(_pitch - my, pitchMin, pitchMax);
            CameraYaw = _yaw;
            CameraPitch = _pitch;

            // Orbit behind target
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -distance);
            offset.y += heightOffset;
            Vector3 desiredPos = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desiredPos, positionSmooth * dt);
            transform.LookAt(target.position + Vector3.up * 1.5f);

            // FOV: right-click aim zoom takes priority; otherwise speed-sensitive for air
            if (_cam != null)
            {
                float targetFOV = baseFOV;
                if (Input.GetMouseButton(1))
                {
                    targetFOV = aimZoomFOV;
                }
                else
                {
                    var vehicle = target.GetComponentInParent<CloseEncounters.Vehicle.Vehicle>();
                    if (vehicle != null && string.Equals(vehicle.domain, "air", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var rb = vehicle.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            float topSpeed = CloseEncounters.Vehicle.Vehicle.MaxAirSpeed * (vehicle.isBoosting ? vehicle.boostMultiplier : 1f);
                            float speedFrac = Mathf.Clamp01(rb.linearVelocity.magnitude / Mathf.Max(topSpeed, 0.01f));
                            targetFOV = Mathf.Lerp(baseFOV, maxFOV, speedFrac);
                        }
                    }
                }
                _cam.fieldOfView = Mathf.SmoothDamp(_cam.fieldOfView, targetFOV, ref _fovVel, fovSmoothTime);
            }

            // Aim raycast through screen center
            if (_cam != null)
            {
                Ray ray = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                AimDirection = ray.direction;

                if (Physics.Raycast(ray, out RaycastHit hit, aimRayDistance, aimMask,
                    QueryTriggerInteraction.Ignore))
                    AimPoint = hit.point;
                else
                    AimPoint = ray.origin + ray.direction * aimRayDistance;
            }
        }

        // ── Cursor management ────────────────────────────────────────────

        private void LockCursorInternal()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void UnlockCursor()
        {
            _cursorUnlocked = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void RelockCursor()
        {
            _cursorUnlocked = false;
            LockCursorInternal();
        }

        // ── Public API ───────────────────────────────────────────────────

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (target != null)
            {
                _yaw = target.eulerAngles.y;
                LockCursorInternal();
            }
        }

        public Vector3 GetAimPoint() => AimPoint;
    }
}
