using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Vehicle;
using CloseEncounters.Core;
using CloseEncounters.Arena;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// Handles player combat input: mouse click fires weapons toward the
    /// CombatCamera's aim point. Attached to the player vehicle by ArenaManager.
    /// </summary>
    public class PlayerCombatInput : MonoBehaviour
    {
        private PlayerVehicleController _playerCtrl;
        private VehicleRuntime _runtime;
        private int _playerId;

        // Weapon firing state
        private List<WeaponSlot> _weapons = new List<WeaponSlot>();
        private int _activeWeaponIndex = 0; // -1 = fire all
        private float _fireCooldown;

        public void Initialize(VehicleRuntime runtime, int playerId)
        {
            _runtime = runtime;
            _playerId = playerId;

            // Find all weapon parts
            if (runtime != null)
            {
                foreach (var node in runtime.PartNodes)
                {
                    if (node.partData == null) continue;
                    if (string.Equals(node.partData.category, "weapon",
                        System.StringComparison.OrdinalIgnoreCase))
                    {
                        _weapons.Add(new WeaponSlot
                        {
                            node = node,
                            partData = node.partData,
                            cooldownRemaining = 0f,
                            ammoRemaining = node.partData.GetStat<int>("ammo", 100)
                        });
                    }
                }
            }

            // Attach TurretAim to any weapon that uses a defence turret prefab
            for (int w = 0; w < _weapons.Count; w++)
            {
                var slot = _weapons[w];
                if (slot.node == null) continue;
                string wid = slot.partData.id?.ToLowerInvariant() ?? "";
                bool hasTurret = wid == "heavy_cannon" || wid == "laser"
                    || wid == "rocket_pod" || wid == "rocket"
                    || wid == "machine_gun";
                if (hasTurret && slot.node.GetComponent<TurretAim>() == null)
                {
                    slot.node.gameObject.AddComponent<TurretAim>();
                }
            }

            Debug.Log($"[PlayerCombatInput] Found {_weapons.Count} weapons on player vehicle");
        }

        private void Update()
        {
            if (_playerCtrl == null)
            {
                _playerCtrl = GetComponent<PlayerVehicleController>();
                if (_playerCtrl == null) return;
            }

            // Tick weapon cooldowns
            for (int i = 0; i < _weapons.Count; i++)
            {
                if (_weapons[i].cooldownRemaining > 0f)
                {
                    var s = _weapons[i];
                    s.cooldownRemaining -= Time.deltaTime;
                    _weapons[i] = s;
                }
            }

            // Left click = fire
            if (Input.GetMouseButton(0) && Time.timeScale > 0f)
            {
                FireWeapons();
            }

            // Update turret aim targets and active state
            if (_playerCtrl != null)
            {
                Vector3 aim = _playerCtrl.AimPoint;
                for (int w = 0; w < _weapons.Count; w++)
                {
                    var turret = _weapons[w].node != null
                        ? _weapons[w].node.GetComponent<TurretAim>() : null;
                    if (turret == null) continue;
                    turret.aimTarget = aim;
                    turret.isActive = _activeWeaponIndex == -1 || _activeWeaponIndex == w;
                }
            }

            // Scroll wheel to cycle weapons (when not over UI)
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (_weapons.Count > 0)
                {
                    _activeWeaponIndex += scroll > 0f ? 1 : -1;
                    // -1 = fire all, then cycle through individual weapons
                    if (_activeWeaponIndex >= _weapons.Count) _activeWeaponIndex = -1;
                    if (_activeWeaponIndex < -1) _activeWeaponIndex = _weapons.Count - 1;
                }
            }
        }

        private void FireWeapons()
        {
            if (_weapons.Count == 0) return;
            if (_playerCtrl == null) return;

            Vector3 aimPoint = _playerCtrl.AimPoint;

            // Sanity: if aim point is at origin, controller hasn't computed yet
            if (aimPoint.sqrMagnitude < 1f)
            {
                Camera cam = Camera.main;
                if (cam != null)
                    aimPoint = cam.transform.position + cam.transform.forward * 100f;
                else
                    return;
            }

            if (_activeWeaponIndex == -1)
            {
                // Fire all weapons
                for (int i = 0; i < _weapons.Count; i++)
                    TryFireWeapon(i, aimPoint);
            }
            else if (_activeWeaponIndex >= 0 && _activeWeaponIndex < _weapons.Count)
            {
                TryFireWeapon(_activeWeaponIndex, aimPoint);
            }
        }

        private void TryFireWeapon(int index, Vector3 aimPoint)
        {
            var slot = _weapons[index];
            if (slot.node == null || slot.node.isDestroyed) return;
            if (slot.cooldownRemaining > 0f) return;
            if (slot.ammoRemaining <= 0) return;

            // Muzzle position = weapon part position + offset forward in fire direction
            // so bullets spawn OUTSIDE the vehicle, not inside weapon colliders
            Vector3 muzzle = slot.node.transform.position + Vector3.up * 0.5f;

            Vector3 dir;
            bool isFixed = slot.partData.GetStat<bool>("fixed", false);
            string pid = slot.partData.id;

            if (pid == "broadside_cannon")
            {
                // Broadside cannons fire strictly perpendicular to the vehicle's
                // long axis (+Z). Side is determined by weapon placement (left/right of center).
                float side = (slot.node.transform.position - transform.position).x >= 0f ? 1f : -1f;
                dir = transform.right * side;
            }
            else if (isFixed)
            {
                // Fixed weapons fire outboard: direction from vehicle center to weapon position
                Vector3 outboard = (slot.node.transform.position - transform.position);
                outboard.y = 0f;
                if (outboard.sqrMagnitude < 0.01f)
                    outboard = transform.forward; // fallback if weapon is dead center
                dir = outboard.normalized;
            }
            else
            {
                // Aimed weapons fire toward the reticle aim point
                dir = (aimPoint - muzzle).normalized;
                Camera cam = Camera.main;
                if (cam != null)
                {
                    float dot = Vector3.Dot(dir, cam.transform.forward);
                    if (dot < 0.25f)
                        dir = cam.transform.forward;
                }
            }

            // Push muzzle forward along fire direction so bullet spawns outside the vehicle
            muzzle += dir * 2f;

            // Track shots fired
            var vr = GetComponent<VehicleRuntime>();
            if (vr != null) vr.ShotsFired++;

            // Spawn projectile
            string weaponId = slot.partData.id;
            Projectile.Spawn(weaponId, muzzle, dir, aimPoint, _playerId);

            // VFX: muzzle flash — skip for rapid-fire weapons (clean look)
            string wid = slot.partData.id?.ToLowerInvariant() ?? "";
            bool rapidFire = wid == "machine_gun" || wid == "autocannon" || wid == "swivel_cannon" || wid == "wing_cannon";
            if (!rapidFire)
                VFXManager.MuzzleFlash(muzzle, dir, 1f);

            // Consume ammo and start cooldown
            slot.ammoRemaining--;
            float fireRate = slot.partData.GetStat<float>("fire_rate",
                slot.partData.GetStat<float>("fireRate", 2f));
            slot.cooldownRemaining = fireRate > 0f ? 1f / fireRate : 0.5f;

            _weapons[index] = slot;
        }

        /// <summary>Get total remaining ammo across all weapons.</summary>
        public int GetTotalAmmo()
        {
            int total = 0;
            for (int i = 0; i < _weapons.Count; i++)
                total += _weapons[i].ammoRemaining;
            return total;
        }

        /// <summary>Get the active weapon's display name.</summary>
        public string GetActiveWeaponName()
        {
            if (_weapons.Count == 0) return "No Weapons";
            if (_activeWeaponIndex == -1) return "ALL";
            if (_activeWeaponIndex >= 0 && _activeWeaponIndex < _weapons.Count)
            {
                var pd = _weapons[_activeWeaponIndex].partData;
                return string.IsNullOrEmpty(pd.partName) ? pd.id : pd.partName;
            }
            return "---";
        }

        private struct WeaponSlot
        {
            public PartNode node;
            public PartData partData;
            public float cooldownRemaining;
            public int ammoRemaining;
        }
    }
}
