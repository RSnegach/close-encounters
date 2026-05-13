using UnityEngine;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// Rotates defence turret child transforms (Base for yaw, Tower for pitch)
    /// to track the player's aim point. Attach to a weapon PartNode that uses
    /// a Bruhassets Defence prefab (DefenceCannon, DefenceLazer, DefenceRocket).
    /// </summary>
    public class TurretAim : MonoBehaviour
    {
        /// <summary>World-space target the turret should aim at.</summary>
        public Vector3 aimTarget;

        /// <summary>Whether this turret should actively track (set by PlayerCombatInput).</summary>
        public bool isActive = true;

        /// <summary>How fast the turret rotates (degrees/second).</summary>
        public float rotateSpeed = 180f;

        private Transform _base;
        private Transform _tower;

        private void Start()
        {
            FindTurretParts();
        }

        private void FindTurretParts()
        {
            // Defence prefabs use naming: *Base for yaw, *Tower for pitch
            string[] baseNames = { "CannonBase", "LazerBase", "RocketBase", "RocketRotator" };
            string[] towerNames = { "CannonTower", "LazerTower", "RocketTower" };

            var children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                string n = children[i].name;
                if (_base == null)
                {
                    for (int b = 0; b < baseNames.Length; b++)
                        if (n == baseNames[b]) { _base = children[i]; break; }
                }
                if (_tower == null)
                {
                    for (int t = 0; t < towerNames.Length; t++)
                        if (n == towerNames[t]) { _tower = children[i]; break; }
                }
            }
        }

        private void LateUpdate()
        {
            if (!isActive) return;
            if (_base == null && _tower == null) return;

            Vector3 toTarget = aimTarget - transform.position;
            if (toTarget.sqrMagnitude < 0.1f) return;

            float dt = Time.deltaTime * rotateSpeed;

            // Base rotates on Y-axis (horizontal sweep)
            if (_base != null)
            {
                Vector3 flatDir = new Vector3(toTarget.x, 0f, toTarget.z);
                if (flatDir.sqrMagnitude > 0.01f)
                {
                    // Target rotation in world space, then convert to local
                    Quaternion targetRot = Quaternion.LookRotation(flatDir, Vector3.up);
                    // We need the rotation relative to the parent (the PartNode transform)
                    Quaternion localTarget = Quaternion.Inverse(transform.rotation) * targetRot;
                    _base.localRotation = Quaternion.RotateTowards(
                        _base.localRotation, localTarget, dt);
                }
            }

            // Tower rotates on X-axis (elevation)
            if (_tower != null)
            {
                Vector3 flatDir = new Vector3(toTarget.x, 0f, toTarget.z);
                float horizontalDist = flatDir.magnitude;
                float pitch = -Mathf.Atan2(toTarget.y, horizontalDist) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(pitch, -60f, 30f);

                Quaternion targetPitch = Quaternion.Euler(pitch, 0f, 0f);
                _tower.localRotation = Quaternion.RotateTowards(
                    _tower.localRotation, targetPitch, dt);
            }
        }
    }
}
