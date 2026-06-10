using UnityEngine;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// Aims a weapon at the player's (or AI's) aim point while it is the selected
    /// weapon (isActive). Two modes, chosen automatically:
    ///
    ///   * Turret rig — for Bruhassets Defence prefabs that expose *Base (yaw) and
    ///     *Tower (pitch) child transforms, those are rotated independently.
    ///   * Whole-model — any other gun has no rig, so the weapon model itself is
    ///     rotated so its forward (+Z) points at the target.
    ///
    /// When the weapon is NOT selected it eases back to its rest orientation, so
    /// unselected guns sit naturally instead of frozen mid-aim.
    /// Attach to a weapon PartNode; PlayerCombatInput/AICombat drive aimTarget + isActive.
    /// </summary>
    public class TurretAim : MonoBehaviour
    {
        /// <summary>World-space target the turret should aim at.</summary>
        public Vector3 aimTarget;

        /// <summary>Whether this weapon is currently selected and should track aim.</summary>
        public bool isActive = true;

        /// <summary>How fast the turret/model rotates (degrees/second).</summary>
        public float rotateSpeed = 180f;

        // Turret-rig transforms (Defence prefabs).
        private Transform _base;
        private Transform _tower;
        private Quaternion _baseRest;
        private Quaternion _towerRest;

        // Whole-model fallback (guns without a base/tower rig).
        private Transform _aimModel;
        private Quaternion _modelRest;

        private bool _resolved;

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

            if (_base != null) _baseRest = _base.localRotation;
            if (_tower != null) _towerRest = _tower.localRotation;

            // No turret rig — fall back to rotating the whole weapon model.
            if (_base == null && _tower == null)
            {
                _aimModel = PickAimModel();
                if (_aimModel != null) _modelRest = _aimModel.localRotation;
            }

            _resolved = true;
        }

        // Names that indicate a static mount, not the barrel — skip these so the gun
        // body (e.g. milk_gun's CumGunModel) rotates, not its support pillar.
        private static readonly string[] MountKeywords =
            { "support", "stand", "post", "pedestal", "pillar", "mount", "tripod" };

        /// <summary>Choose the direct child to rotate as the gun barrel: the largest
        /// mesh that isn't an obvious static mount, falling back to the largest mesh.</summary>
        private Transform PickAimModel()
        {
            Transform best = null;     float bestSize = -1f;
            Transform fallback = null; float fallbackSize = -1f;

            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                var rend = child.GetComponentInChildren<Renderer>();
                if (rend == null) continue;

                float size = rend.bounds.size.sqrMagnitude;
                if (size > fallbackSize) { fallbackSize = size; fallback = child; }

                string n = child.name.ToLowerInvariant();
                bool isMount = false;
                for (int k = 0; k < MountKeywords.Length; k++)
                    if (n.Contains(MountKeywords[k])) { isMount = true; break; }
                if (isMount) continue;

                if (size > bestSize) { bestSize = size; best = child; }
            }

            return best != null ? best : fallback;
        }

        private void LateUpdate()
        {
            if (!_resolved) return;

            float dt = Time.deltaTime * rotateSpeed;

            // Not the selected weapon: ease everything back to its rest pose.
            if (!isActive)
            {
                if (_base != null)
                    _base.localRotation = Quaternion.RotateTowards(_base.localRotation, _baseRest, dt);
                if (_tower != null)
                    _tower.localRotation = Quaternion.RotateTowards(_tower.localRotation, _towerRest, dt);
                if (_aimModel != null)
                    _aimModel.localRotation = Quaternion.RotateTowards(_aimModel.localRotation, _modelRest, dt);
                return;
            }

            Vector3 toTarget = aimTarget - transform.position;
            if (toTarget.sqrMagnitude < 0.1f) return;

            // --- Turret rig: Base yaws, Tower pitches ---
            if (_base != null || _tower != null)
            {
                if (_base != null)
                {
                    Vector3 flatDir = new Vector3(toTarget.x, 0f, toTarget.z);
                    if (flatDir.sqrMagnitude > 0.01f)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(flatDir, Vector3.up);
                        Quaternion localTarget = Quaternion.Inverse(transform.rotation) * targetRot;
                        _base.localRotation = Quaternion.RotateTowards(_base.localRotation, localTarget, dt);
                    }
                }

                if (_tower != null)
                {
                    Vector3 flatDir = new Vector3(toTarget.x, 0f, toTarget.z);
                    float horizontalDist = flatDir.magnitude;
                    float pitch = -Mathf.Atan2(toTarget.y, horizontalDist) * Mathf.Rad2Deg;
                    pitch = Mathf.Clamp(pitch, -60f, 30f);

                    Quaternion targetPitch = Quaternion.Euler(pitch, 0f, 0f);
                    _tower.localRotation = Quaternion.RotateTowards(_tower.localRotation, targetPitch, dt);
                }
                return;
            }

            // --- Whole-model fallback: point the gun's +Z at the target ---
            if (_aimModel != null)
            {
                Vector3 dir = aimTarget - _aimModel.position;
                if (dir.sqrMagnitude < 0.1f) return;

                Quaternion worldTarget = Quaternion.LookRotation(dir.normalized, Vector3.up);
                // Convert into the part's local space so vehicle orientation is removed.
                Quaternion localTarget = Quaternion.Inverse(transform.rotation) * worldTarget;
                _aimModel.localRotation = Quaternion.RotateTowards(_aimModel.localRotation, localTarget, dt);
            }
        }
    }
}
