using UnityEngine;

namespace CloseEncounters.Vehicle
{
    /// <summary>
    /// Adds subtle procedural animation to BananaMan's bone pose:
    /// breathing (spine oscillation), head look-around, and arm micro-movement.
    /// Attached after PoseBananaManSeated sets the base pose.
    /// </summary>
    public class BananaManAnimator : MonoBehaviour
    {
        private Transform _spine;
        private Transform _head;
        private Transform _leftArm;
        private Transform _rightArm;

        private Quaternion _spineBase;
        private Quaternion _headBase;
        private Quaternion _leftArmBase;
        private Quaternion _rightArmBase;

        private float _breathPhase;
        private float _lookPhase;

        private void Start()
        {
            // Find bones by name (supports multiple naming conventions)
            var all = GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                string n = t.name;
                if (_spine == null && n == "Spine") _spine = t;
                if (_head == null && n == "Head") _head = t;
                if (_leftArm == null && (n == "Left Arm" || n == "LeftArm" || n == "Arm.L")) _leftArm = t;
                if (_rightArm == null && (n == "Right Arm" || n == "RightArm" || n == "Arm.R")) _rightArm = t;
            }

            // Store base pose rotations
            if (_spine != null) _spineBase = _spine.localRotation;
            if (_head != null) _headBase = _head.localRotation;
            if (_leftArm != null) _leftArmBase = _leftArm.localRotation;
            if (_rightArm != null) _rightArmBase = _rightArm.localRotation;

            _breathPhase = Random.Range(0f, Mathf.PI * 2f);
            _lookPhase = Random.Range(0f, Mathf.PI * 2f);
        }

        private void LateUpdate()
        {
            float t = Time.time;

            // Breathing: subtle spine pitch oscillation
            if (_spine != null)
            {
                float breathAngle = Mathf.Sin(t * 1.2f + _breathPhase) * 2f;
                _spine.localRotation = _spineBase * Quaternion.Euler(breathAngle, 0f, 0f);
            }

            // Head look-around: slow yaw + subtle pitch
            if (_head != null)
            {
                float headYaw = Mathf.Sin(t * 0.4f + _lookPhase) * 15f;
                float headPitch = Mathf.Sin(t * 0.7f + _lookPhase * 0.5f) * 5f;
                _head.localRotation = _headBase * Quaternion.Euler(headPitch, headYaw, 0f);
            }

            // Arms: subtle steering micro-movement
            if (_leftArm != null)
            {
                float armMove = Mathf.Sin(t * 0.8f) * 3f;
                _leftArm.localRotation = _leftArmBase * Quaternion.Euler(armMove, 0f, 0f);
            }
            if (_rightArm != null)
            {
                float armMove = Mathf.Sin(t * 0.8f + Mathf.PI) * 3f;
                _rightArm.localRotation = _rightArmBase * Quaternion.Euler(armMove, 0f, 0f);
            }
        }
    }
}
