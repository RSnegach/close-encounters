using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Vehicle;
using CloseEncounters.Core;
using CloseEncounters.Arena;
using CloseEncounters.AI;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// AI trigger-finger. Gives an AI-driven vehicle the ability to ACTUALLY fire
    /// its weapons — the missing half of the bot combat loop. The AIController brain
    /// already decides when/where to shoot (CurrentTarget + CurrentInput.fire +
    /// GetAimPoint), but nothing consumed that decision; this component does.
    ///
    /// It mirrors PlayerCombatInput's proven firing path exactly (same per-weapon
    /// muzzle, fixed/broadside/aimed direction logic, Projectile.Spawn ownership, and
    /// VFX) but is driven by the AI target instead of the mouse. Ammo uses a generous
    /// reloading magazine so bots never become defenceless "training cones".
    /// Attached to every AI vehicle (all domains) by ArenaManager on spawn.
    /// </summary>
    [DisallowMultipleComponent]
    public class AICombat : MonoBehaviour
    {
        private VehicleRuntime _runtime;
        private AIController _ai;
        private int _playerId;
        private readonly List<Slot> _weapons = new List<Slot>();
        private float _healthSyncTimer;
        private float _maxWeaponRange;
        private bool _hasAimed;

        private const float ReloadSeconds = 3f;
        private const float Gravity = 9.81f;   // matches Projectile ballistic gravity

        private struct Slot
        {
            public PartNode node;
            public PartData partData;
            public float cooldown;
            public int ammo;
            public int magazine;
            public float reloadTimer;
            public bool isFixed;
            public bool isBroadside;
            public bool rapidFire;
            public float muzzleSpeed;   // real projectile speed for lead prediction
            public bool ballistic;       // adds gravity drop compensation
        }

        // Per-weapon muzzle speed, mirroring Projectile.Spawn's hardcoded switch, so
        // the AI leads each weapon correctly instead of assuming a single 60 u/s.
        private static float MuzzleSpeed(string id)
        {
            switch (id)
            {
                case "machine_gun": return 200f;
                case "autocannon": return 150f;
                case "swivel_cannon":
                case "wing_cannon": return 180f;
                case "heavy_cannon": return 80f;
                case "broadside_cannon":
                case "deck_gun": return 120f;
                case "rocket":
                case "rocket_pod": return 100f;
                case "missile":
                case "missile_launcher": return 60f;
                case "torpedo_launcher": return 40f;
                // hitscan weapons — effectively instant, no lead needed
                case "laser":
                case "railgun":
                case "milk_gun": return 100000f;
                default: return 120f;
            }
        }

        private static bool IsBallistic(string id) => id == "heavy_cannon";

        public void Initialize(VehicleRuntime runtime, AIController ai, int playerId)
        {
            _runtime = runtime;
            _ai = ai;
            _playerId = playerId;
            if (runtime == null) return;

            foreach (var node in runtime.PartNodes)
            {
                if (node == null || node.partData == null) continue;
                if (!string.Equals(node.partData.category, "weapon",
                    System.StringComparison.OrdinalIgnoreCase)) continue;

                var pd = node.partData;
                string wid = pd.id?.ToLowerInvariant() ?? "";

                // Generous magazine so a bot stays dangerous all match; it reloads
                // when emptied rather than dying for good.
                int statAmmo = pd.GetStat<int>("ammo", 100);
                int mag = Mathf.Max(statAmmo, 120);

                bool isFixed = pd.GetStat<bool>("fixed", false);
                _weapons.Add(new Slot
                {
                    node = node,
                    partData = pd,
                    cooldown = Random.Range(0f, 0.5f), // desync simultaneous volleys
                    ammo = mag,
                    magazine = mag,
                    reloadTimer = 0f,
                    isFixed = isFixed,
                    isBroadside = pd.id == "broadside_cannon",
                    rapidFire = wid == "machine_gun" || wid == "autocannon"
                                || wid == "swivel_cannon" || wid == "wing_cannon",
                    muzzleSpeed = MuzzleSpeed(wid),
                    ballistic = IsBallistic(wid),
                });

                _maxWeaponRange = Mathf.Max(_maxWeaponRange, pd.GetStat<float>("range", 80f));

                // Visual aim-tracking for every aimed weapon so bot barrels point at
                // their target (matches the player). Fixed/broadside fire in a set
                // direction and keep their mounted orientation. TurretAim auto-detects
                // turret-rig vs whole-model rotation.
                bool isBroadside = pd.id == "broadside_cannon";
                bool aimed = !isFixed && !isBroadside;
                if (aimed && node.GetComponent<TurretAim>() == null)
                    node.gameObject.AddComponent<TurretAim>();
                if (aimed) _hasAimed = true;
            }

            if (_ai != null)
            {
                _ai.SetWeaponCount(_weapons.Count);
                _ai.SetWeaponRange(_maxWeaponRange);
                _ai.SetHasAimedWeapon(_hasAimed);
                _ai.SetPlayerId(_playerId);
            }
        }

        private void Update()
        {
            if (_ai == null || _runtime == null || !_runtime.IsAlive) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Keep the brain's health fresh so Retreat / target-scoring actually work
            // (nothing else updates AIController health).
            _healthSyncTimer -= dt;
            if (_healthSyncTimer <= 0f)
            {
                _healthSyncTimer = 0.5f;
                _ai.SetHealth(_runtime.TotalHP, _runtime.MaxHP);
            }

            Transform target = _ai.CurrentTarget;
            // Validate the target is still alive (don't keep shooting a wreck).
            VehicleRuntime targetVr = null;
            if (target != null)
            {
                targetVr = target.GetComponent<VehicleRuntime>();
                if (targetVr != null && !targetVr.IsAlive) { target = null; }
            }
            Vector3 targetPos = target != null ? target.position : transform.position + transform.forward * 50f;
            Vector3 targetVel = Vector3.zero;
            if (target != null)
            {
                var trb = target.GetComponent<Rigidbody>();
                if (trb != null) targetVel = trb.linearVelocity;
            }
            // Generic aim for cosmetic turret tracking.
            Vector3 turretAim = target != null ? _ai.GetAimPoint() : targetPos;

            // Tick cooldowns / reloads, keep turrets tracking the aim point.
            for (int i = 0; i < _weapons.Count; i++)
            {
                var s = _weapons[i];
                if (s.cooldown > 0f) s.cooldown -= dt;
                if (s.reloadTimer > 0f)
                {
                    s.reloadTimer -= dt;
                    if (s.reloadTimer <= 0f) s.ammo = s.magazine;
                }
                _weapons[i] = s;

                if (s.node != null)
                {
                    var turret = s.node.GetComponent<TurretAim>();
                    if (turret != null)
                    {
                        // Point the turret where THIS weapon's shell will actually go
                        // (its own muzzle speed + drop), not a generic lead, so the
                        // barrel and the firing solution agree.
                        Vector3 aim = turretAim;
                        if (target != null)
                        {
                            Vector3 m = s.node.transform.position + Vector3.up * 0.5f;
                            aim = ComputeWeaponAim(s, m, targetPos, targetVel);
                        }
                        turret.aimTarget = aim;
                        turret.isActive = true;
                    }
                }
            }

            // Fire when the brain wants to and we have a live target.
            if (target != null && _ai.CurrentInput.fire)
            {
                for (int i = 0; i < _weapons.Count; i++)
                    TryFire(i, target, targetPos, targetVel);
            }
        }

        // Per-weapon lead: time-of-flight from this weapon's real muzzle speed, plus
        // gravity-drop elevation for ballistic shells.
        private Vector3 ComputeWeaponAim(Slot slot, Vector3 muzzle, Vector3 targetPos, Vector3 targetVel)
        {
            float dist = Vector3.Distance(muzzle, targetPos);
            float tof = slot.muzzleSpeed > 0.01f ? dist / slot.muzzleSpeed : 0f;
            Vector3 aim = targetPos + targetVel * tof;
            if (slot.ballistic) aim.y += 0.5f * Gravity * tof * tof;
            return aim;
        }

        private void TryFire(int index, Transform target, Vector3 targetPos, Vector3 targetVel)
        {
            var slot = _weapons[index];
            if (slot.node == null || slot.node.isDestroyed) return;
            if (slot.cooldown > 0f || slot.reloadTimer > 0f) return;
            if (slot.ammo <= 0) return;

            Vector3 muzzle = slot.node.transform.position + Vector3.up * 0.5f;
            Vector3 aimPoint = ComputeWeaponAim(slot, muzzle, targetPos, targetVel);
            Vector3 dir;

            if (slot.isBroadside)
            {
                // Fires perpendicular to the hull; only when the target is off that side.
                float side = (slot.node.transform.position - transform.position).x >= 0f ? 1f : -1f;
                dir = transform.right * side;
                Vector3 toT = (targetPos - transform.position).normalized;
                if (Vector3.Dot(toT, dir) < 0.3f) return;
            }
            else if (slot.isFixed)
            {
                // Fixed weapon fires outboard from vehicle centre; only when bearing on target.
                Vector3 outboard = slot.node.transform.position - transform.position;
                outboard.y = 0f;
                if (outboard.sqrMagnitude < 0.01f) outboard = transform.forward;
                dir = outboard.normalized;
                Vector3 toT = (targetPos - transform.position).normalized;
                if (Vector3.Dot(toT, dir) < 0.4f) return;
            }
            else
            {
                // Aimed weapon fires toward the per-weapon predicted lead point.
                dir = (aimPoint - muzzle).normalized;
            }

            muzzle += dir * 2f;
            _runtime.ShotsFired++;
            Projectile.Spawn(slot.partData.id, muzzle, dir, aimPoint, _playerId);

            if (!slot.rapidFire) VFXManager.MuzzleFlash(muzzle, dir, 1f);

            slot.ammo--;
            if (slot.ammo <= 0) slot.reloadTimer = ReloadSeconds; // reload, never permanently dry
            float fireRate = slot.partData.GetStat<float>("fire_rate",
                slot.partData.GetStat<float>("fireRate", 2f));
            slot.cooldown = fireRate > 0f ? 1f / fireRate : 0.5f;
            _weapons[index] = slot;
        }
    }
}
