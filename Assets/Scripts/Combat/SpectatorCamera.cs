using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using CloseEncounters.Arena;

namespace CloseEncounters.Combat
{
    public class SpectatorCamera : MonoBehaviour
    {
        public enum ViewMode { ThirdPersonChase, FirstPerson, FreeCam }

        private readonly List<VehicleRuntime> _aliveVehicles = new List<VehicleRuntime>();
        private int _currentIndex;

        private Camera _specCam;
        private GameObject _specCamObj;

        private ViewMode _viewMode = ViewMode.ThirdPersonChase;

        // Chase cam state
        private float _yaw;
        private float _pitch = 20f;
        private float _cameraDistance = 16f;
        private float _cameraHeight = 6f;
        private const float Sensitivity = 2f;

        // Free cam state
        private float _freeYaw;
        private float _freePitch;
        private const float FreeSpeed = 30f;
        private const float FreeSprintSpeed = 90f;

        // Smooth target switch
        private Coroutine _switchTween;
        private bool _tweening;

        // Owner of the SpectatorCamera (the dead vehicle to filter from list)
        private VehicleRuntime _selfOwner;

        // Cached HUD
        private CloseEncounters.UI.HUD _hud;

        // Layer mask for camera collision: ~0 minus "Vehicle" layer if it exists
        private int _collisionMask;

        private void Awake()
        {
            _specCamObj = new GameObject("SpectatorCam");
            _specCam = _specCamObj.AddComponent<Camera>();
            _specCam.clearFlags = CameraClearFlags.Skybox;
            _specCam.backgroundColor = new Color(0.4f, 0.6f, 0.9f);
            _specCam.fieldOfView = 60f;
            _specCam.nearClipPlane = 0.3f;
            _specCam.farClipPlane = 1000f;

            if (FindAnyObjectByType<AudioListener>() == null)
                _specCamObj.AddComponent<AudioListener>();

            int vehicleLayer = LayerMask.NameToLayer("Vehicle");
            _collisionMask = vehicleLayer >= 0 ? ~(1 << vehicleLayer) : ~0;
        }

        private void Start()
        {
            _hud = FindAnyObjectByType<CloseEncounters.UI.HUD>();
            RefreshAliveList();
            PushViewModeToHUD();
        }

        public void SetTarget(VehicleRuntime target)
        {
            // Capture the dead vehicle to filter from cycling list (the prior player vehicle)
            if (ArenaManager.Instance != null)
            {
                var pv = ArenaManager.Instance.GetPlayerVehicle();
                if (pv != null) _selfOwner = pv;
            }

            RefreshAliveList();
            _currentIndex = _aliveVehicles.IndexOf(target);
            if (_currentIndex < 0) _currentIndex = 0;
            ClampIndex();
            SnapCameraToCurrent();
            PushTargetDataToHUD();
        }

        private void Update()
        {
            // View mode toggle
            if (Input.GetKeyDown(KeyCode.V))
            {
                _viewMode = (ViewMode)(((int)_viewMode + 1) % 3);
                PushViewModeToHUD();
                if (_viewMode == ViewMode.FreeCam)
                {
                    InitFreeCamFromCurrent();
                }
                else
                {
                    SnapCameraToCurrent();
                }
            }

            // Cycling (no-op in FreeCam)
            if (_viewMode != ViewMode.FreeCam)
            {
                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.D)
                    || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetMouseButtonDown(0))
                {
                    CycleTarget(1);
                }
                else if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.A)
                    || Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    CycleTarget(-1);
                }
            }

            // Scroll wheel zoom (chase only)
            if (_viewMode == ViewMode.ThirdPersonChase)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.001f)
                {
                    _cameraDistance -= scroll * _cameraDistance * 0.5f;
                    _cameraDistance = Mathf.Clamp(_cameraDistance, 5f, 40f);
                }
            }

            switch (_viewMode)
            {
                case ViewMode.ThirdPersonChase: TickChase(); break;
                case ViewMode.FirstPerson:      TickFirstPerson(); break;
                case ViewMode.FreeCam:          TickFreeCam(); break;
            }

            PushTargetDataToHUD();
        }

        private void TickChase()
        {
            if (!EnsureValidTarget(out var target)) return;

            _yaw += Input.GetAxisRaw("Mouse X") * Sensitivity;
            _pitch -= Input.GetAxisRaw("Mouse Y") * Sensitivity;
            _pitch = Mathf.Clamp(_pitch, 5f, 60f);

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = target.transform.position + Vector3.up * 1.5f;
            Vector3 dir = rot * Vector3.back;

            float dist = _cameraDistance;
            if (Physics.SphereCast(pivot, 0.4f, dir, out RaycastHit hit, _cameraDistance, _collisionMask, QueryTriggerInteraction.Ignore))
            {
                dist = Mathf.Max(1.0f, hit.distance - 0.5f);
            }

            Vector3 desiredPos = pivot + dir * dist + Vector3.up * (_cameraHeight - 1.5f);

            if (!_tweening)
            {
                _specCamObj.transform.position = Vector3.Lerp(
                    _specCamObj.transform.position, desiredPos, 8f * Time.unscaledDeltaTime);
            }
            _specCamObj.transform.rotation = rot;
        }

        private void TickFirstPerson()
        {
            if (!EnsureValidTarget(out var target)) return;

            // Mount near the front of the vehicle, looking forward.
            Vector3 fwdAnchor = target.transform.position
                + target.transform.forward * 1.4f
                + target.transform.up * 1.0f;

            if (!_tweening)
            {
                _specCamObj.transform.position = fwdAnchor;
            }

            _specCam.fieldOfView = 70f;
            // Auto-roll with target (full rotation of vehicle, including roll/pitch).
            _specCamObj.transform.rotation = target.transform.rotation;
        }

        private void TickFreeCam()
        {
            _specCam.fieldOfView = 60f;

            _freeYaw += Input.GetAxisRaw("Mouse X") * Sensitivity;
            _freePitch -= Input.GetAxisRaw("Mouse Y") * Sensitivity;
            _freePitch = Mathf.Clamp(_freePitch, -85f, 85f);

            Quaternion rot = Quaternion.Euler(_freePitch, _freeYaw, 0f);
            _specCamObj.transform.rotation = rot;

            float speed = Input.GetKey(KeyCode.LeftShift) ? FreeSprintSpeed : FreeSpeed;
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += rot * Vector3.forward;
            if (Input.GetKey(KeyCode.S)) move += rot * Vector3.back;
            if (Input.GetKey(KeyCode.A)) move += rot * Vector3.left;
            if (Input.GetKey(KeyCode.D)) move += rot * Vector3.right;
            if (Input.GetKey(KeyCode.Space))     move += Vector3.up;
            if (Input.GetKey(KeyCode.LeftControl)) move += Vector3.down;

            if (move.sqrMagnitude > 0.001f)
            {
                _specCamObj.transform.position += move.normalized * speed * Time.unscaledDeltaTime;
            }
        }

        private void InitFreeCamFromCurrent()
        {
            Vector3 e = _specCamObj.transform.eulerAngles;
            _freePitch = NormalizeAngle(e.x);
            _freeYaw = e.y;
        }

        private static float NormalizeAngle(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            return a;
        }

        private bool EnsureValidTarget(out VehicleRuntime target)
        {
            target = null;
            if (_aliveVehicles.Count == 0)
            {
                RefreshAliveList();
                if (_aliveVehicles.Count == 0) return false;
            }
            ClampIndex();
            target = _aliveVehicles[_currentIndex];
            if (target == null || !target.IsAlive)
            {
                RefreshAliveList();
                if (_aliveVehicles.Count == 0) return false;
                ClampIndex();
                target = _aliveVehicles[_currentIndex];
                if (target == null) return false;
            }
            return true;
        }

        private void CycleTarget(int direction)
        {
            RefreshAliveList();
            if (_aliveVehicles.Count == 0) return;

            int n = _aliveVehicles.Count;
            _currentIndex = ((_currentIndex + direction) % n + n) % n;

            // Start tween from current camera position.
            if (_switchTween != null) StopCoroutine(_switchTween);
            _switchTween = StartCoroutine(SmoothSwitchTween());

            PushTargetDataToHUD();
        }

        private IEnumerator SmoothSwitchTween()
        {
            _tweening = true;
            Vector3 startPos = _specCamObj.transform.position;
            float t = 0f;
            const float dur = 0.3f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dur);
                float s = u * u * (3f - 2f * u);
                Vector3 destPos = ComputeDesiredCamPos();
                _specCamObj.transform.position = Vector3.Lerp(startPos, destPos, s);
                yield return null;
            }
            _tweening = false;
            _switchTween = null;
        }

        private Vector3 ComputeDesiredCamPos()
        {
            if (!EnsureValidTarget(out var target))
                return _specCamObj.transform.position;

            if (_viewMode == ViewMode.FirstPerson)
            {
                return target.transform.position
                    + target.transform.forward * 1.4f
                    + target.transform.up * 1.0f;
            }

            // Chase
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = target.transform.position + Vector3.up * 1.5f;
            Vector3 dir = rot * Vector3.back;
            float dist = _cameraDistance;
            if (Physics.SphereCast(pivot, 0.4f, dir, out RaycastHit hit, _cameraDistance, _collisionMask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(1.0f, hit.distance - 0.5f);
            return pivot + dir * dist + Vector3.up * (_cameraHeight - 1.5f);
        }

        private void SnapCameraToCurrent()
        {
            if (!EnsureValidTarget(out var target)) return;

            if (_viewMode == ViewMode.FirstPerson)
            {
                _specCam.fieldOfView = 70f;
                _specCamObj.transform.position = target.transform.position
                    + target.transform.forward * 1.4f
                    + target.transform.up * 1.0f;
                _specCamObj.transform.rotation = target.transform.rotation;
                return;
            }

            _specCam.fieldOfView = 60f;
            _yaw = target.transform.eulerAngles.y + 180f;
            _pitch = 20f;
            _specCamObj.transform.position = ComputeDesiredCamPos();
            _specCamObj.transform.LookAt(target.transform.position + Vector3.up * 1.5f);
        }

        private void RefreshAliveList()
        {
            _aliveVehicles.Clear();
            if (ArenaManager.Instance == null) return;

            var all = ArenaManager.Instance.GetVehicles();
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (v == null || !v.IsAlive) continue;
                if (_selfOwner != null && ReferenceEquals(v, _selfOwner)) continue;
                _aliveVehicles.Add(v);
            }
        }

        private void ClampIndex()
        {
            if (_aliveVehicles.Count == 0) { _currentIndex = 0; return; }
            if (_currentIndex < 0) _currentIndex = 0;
            if (_currentIndex >= _aliveVehicles.Count)
                _currentIndex = _aliveVehicles.Count - 1;
        }

        private void PushTargetDataToHUD()
        {
            if (_hud == null) _hud = FindAnyObjectByType<CloseEncounters.UI.HUD>();
            if (_hud == null) return;

            if (_viewMode == ViewMode.FreeCam || _aliveVehicles.Count == 0)
            {
                _hud.SetSpectatorTargetData(string.Empty, false, 0, 0);
                return;
            }

            ClampIndex();
            var t = _aliveVehicles[_currentIndex];
            if (t == null) return;

            string name = t.IsAI ? $"AI {t.PlayerId}" : $"Player {t.PlayerId}";
            _hud.SetSpectatorTargetData(name, t.IsAI, t.TotalHP, t.MaxHP);
        }

        private void PushViewModeToHUD()
        {
            if (_hud == null) _hud = FindAnyObjectByType<CloseEncounters.UI.HUD>();
            if (_hud == null) return;
            string label = _viewMode switch
            {
                ViewMode.ThirdPersonChase => "CHASE",
                ViewMode.FirstPerson      => "FPS",
                ViewMode.FreeCam          => "FREECAM",
                _ => "?"
            };
            _hud.SetSpectatorViewMode(label);
        }

        private void OnDestroy()
        {
            if (_specCamObj != null) Destroy(_specCamObj);
        }
    }
}
