using UnityEngine;
using System.Collections.Generic;
using CloseEncounters.Arena;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// Spectator system: when the player dies, this takes over.
    /// For real players: uses their camera directly (same view).
    /// For AI: mouse-controlled free-look chase camera.
    /// Left-click cycles to next alive vehicle.
    /// </summary>
    public class SpectatorCamera : MonoBehaviour
    {
        private List<VehicleRuntime> _aliveVehicles = new List<VehicleRuntime>();
        private int _currentIndex;
        private Camera _chaseCam;
        private GameObject _chaseCamObj;

        // Free-look chase cam (mouse-controlled, same style as player camera)
        private float _yaw;
        private float _pitch = 20f;
        private float _cameraDistance = 16f;
        private float _cameraHeight = 6f;
        private float _sensitivity = 2f;

        private void Start()
        {
            RefreshAliveList();
        }

        private void Update()
        {
            // Left click cycles spectate target
            if (Input.GetMouseButtonDown(0))
                CycleTarget();

            // Scroll wheel zooms in/out
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _cameraDistance -= scroll * _cameraDistance * 0.5f;
                _cameraDistance = Mathf.Clamp(_cameraDistance, 5f, 40f);
            }

            // Free-look chase cam for AI targets
            if (_chaseCamObj != null && _currentIndex >= 0 && _currentIndex < _aliveVehicles.Count)
            {
                var target = _aliveVehicles[_currentIndex];
                if (target == null || !target.IsAlive)
                {
                    CycleTarget();
                    return;
                }

                // Mouse controls camera angle
                _yaw += Input.GetAxisRaw("Mouse X") * _sensitivity;
                _pitch -= Input.GetAxisRaw("Mouse Y") * _sensitivity;
                _pitch = Mathf.Clamp(_pitch, 5f, 60f);

                Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
                Vector3 offset = rot * new Vector3(0f, 0f, -_cameraDistance);
                offset.y += _cameraHeight;

                Vector3 desiredPos = target.transform.position + offset;
                _chaseCamObj.transform.position = Vector3.Lerp(
                    _chaseCamObj.transform.position, desiredPos, 8f * Time.deltaTime);
                _chaseCamObj.transform.rotation = rot;
            }
        }

        public void SetTarget(VehicleRuntime target)
        {
            RefreshAliveList();
            _currentIndex = _aliveVehicles.IndexOf(target);
            if (_currentIndex < 0) _currentIndex = 0;
            AttachToTarget();
        }

        private void CycleTarget()
        {
            RefreshAliveList();
            if (_aliveVehicles.Count == 0) return;

            _currentIndex = (_currentIndex + 1) % _aliveVehicles.Count;
            AttachToTarget();

            // Update HUD with next target name
            UpdateHUDName();
        }

        private void AttachToTarget()
        {
            if (_aliveVehicles.Count == 0) return;
            if (_currentIndex < 0 || _currentIndex >= _aliveVehicles.Count)
                _currentIndex = 0;

            var target = _aliveVehicles[_currentIndex];
            if (target == null) return;

            // Clean up any chase cam we created previously
            if (_chaseCamObj != null)
            {
                Destroy(_chaseCamObj);
                _chaseCamObj = null;
                _chaseCam = null;
            }

            // Check if target has its own player camera
            var pvc = target.GetComponent<PlayerVehicleController>();
            if (pvc != null)
            {
                // Real player -- use their camera. Disable all other cameras.
                var allCams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                for (int i = 0; i < allCams.Length; i++)
                {
                    var owner = allCams[i].GetComponentInParent<VehicleRuntime>();
                    allCams[i].enabled = (owner == target);
                }
            }
            else
            {
                // AI vehicle -- create mouse-controlled chase cam
                var allCams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                for (int i = 0; i < allCams.Length; i++)
                    allCams[i].enabled = false;

                _chaseCamObj = new GameObject("SpectatorChaseCam");
                _chaseCam = _chaseCamObj.AddComponent<Camera>();
                _chaseCam.clearFlags = CameraClearFlags.Skybox;
                _chaseCam.backgroundColor = new Color(0.4f, 0.6f, 0.9f);
                _chaseCam.fieldOfView = 60f;
                _chaseCam.nearClipPlane = 0.3f;
                _chaseCam.farClipPlane = 1000f;

                if (FindAnyObjectByType<AudioListener>() == null)
                    _chaseCamObj.AddComponent<AudioListener>();

                // Snap camera to position behind the AI target immediately
                _yaw = target.transform.eulerAngles.y + 180f;
                _pitch = 20f;
                Quaternion startRot = Quaternion.Euler(_pitch, _yaw, 0f);
                Vector3 startOffset = startRot * new Vector3(0f, 0f, -_cameraDistance);
                startOffset.y += _cameraHeight;
                _chaseCamObj.transform.position = target.transform.position + startOffset;
                _chaseCamObj.transform.LookAt(target.transform.position + Vector3.up * 1.5f);
            }

            UpdateHUDName();
        }

        private void UpdateHUDName()
        {
            if (_aliveVehicles.Count == 0) return;

            // Figure out next target name for the hint text
            int nextIdx = (_currentIndex + 1) % _aliveVehicles.Count;
            var next = _aliveVehicles[nextIdx];
            string nextName = next != null
                ? (next.IsAI ? $"AI {next.PlayerId}" : $"Player {next.PlayerId}")
                : "???";

            var hud = FindAnyObjectByType<CloseEncounters.UI.HUD>();
            if (hud != null)
                hud.SetSpectatingTarget(nextName);
        }

        private void RefreshAliveList()
        {
            _aliveVehicles.Clear();
            if (ArenaManager.Instance == null) return;

            var all = ArenaManager.Instance.GetVehicles();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].IsAlive)
                    _aliveVehicles.Add(all[i]);
            }
        }

        private void OnDestroy()
        {
            if (_chaseCamObj != null)
                Destroy(_chaseCamObj);
        }
    }
}
