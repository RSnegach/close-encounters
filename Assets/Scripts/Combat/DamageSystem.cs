using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Arena;
using CloseEncounters.AI;
using CloseEncounters.Vehicle;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// Static utility class for dealing damage to vehicles, shattering icebergs,
    /// ejecting destroyed parts as Rigidbody debris, and spawning explosion VFX.
    /// All methods are static and operate on the provided references directly.
    /// </summary>
    public static class DamageSystem
    {
        // Events (fire after damage has been applied)
        public static event System.Action<VehicleRuntime, VehicleRuntime, int> OnVehicleDamaged; // victim, attacker, amount
        public static event System.Action<VehicleRuntime, VehicleRuntime> OnVehicleKilled;        // victim, attacker

        internal static void FireVehicleDamaged(VehicleRuntime v, VehicleRuntime a, int amt) { OnVehicleDamaged?.Invoke(v, a, amt); }
        internal static void FireVehicleKilled(VehicleRuntime v, VehicleRuntime a) { OnVehicleKilled?.Invoke(v, a); }

        // --- Constants ---
        private const float FuelDetonationChance = 0.80f;
        private const float DebrisLifetime = 5f;
        private const float DebrisEjectForce = 8f;
        private const float ExplosionParticleLifetime = 2f;
        private const int   ExplosionBurstCount = 30;
        private const float IcebergChunkLifetime = 4f;
        private const int   IcebergChunkCount = 6;
        private const float IcebergChunkSinkSpeed = 2f;

        // =====================================================================
        // Primary Damage Entry Point
        // =====================================================================

        /// <summary>
        /// Deal damage to a vehicle. Finds the nearest PartNode to the impact
        /// point and applies damage there. Handles fuel tank detonation and
        /// part ejection on destruction.
        /// </summary>
        /// <param name="vehicle">Target vehicle runtime.</param>
        /// <param name="amount">Damage amount (HP).</param>
        /// <param name="impactPoint">World-space point of impact.</param>
        public static void DealDamageToVehicle(VehicleRuntime vehicle, int amount, Vector3 impactPoint,
            VehicleRuntime attacker = null, bool skipControlParts = false)
        {
            if (vehicle == null || !vehicle.IsAlive) return;
            if (amount <= 0) return;

            PartNode target = skipControlParts
                ? FindNearestNonControlPart(vehicle, impactPoint)
                : FindNearestPart(vehicle, impactPoint);
            if (target == null) return;

            // Track damage received
            vehicle.DamageReceived += amount;

            // Track damage dealt by attacker
            if (attacker != null)
                attacker.DamageDealt += amount;

            bool wasAlive = vehicle.IsAlive;
            bool destroyed = target.TakeDamage(amount);
            FireVehicleDamaged(vehicle, attacker, amount);
            if (wasAlive && !vehicle.IsAlive) FireVehicleKilled(vehicle, attacker);

            // No impact sparks — clean hits

            if (destroyed)
            {
                // ParticlePack VFX: explosion on part destruction
                // Armored parts just spark, non-armored explode
                if (IsArmored(target))
                {
                    VFXManager.Sparks(target.transform.position, 0.8f);
                }
                else
                {
                    VFXManager.SmallExplosion(target.transform.position);
                    VFXManager.TinyFlames(target.transform.position, 0.5f);
                }

                // Track parts lost / destroyed
                vehicle.PartsLost++;
                if (attacker != null)
                    attacker.PartsDestroyedOnEnemy++;

                // Reduce boost capacity when a fuel tank is destroyed
                if (IsFuelTank(target))
                {
                    var playerCtrl = vehicle.GetComponent<CloseEncounters.Combat.PlayerVehicleController>();
                    if (playerCtrl != null)
                        playerCtrl.OnFuelPartDestroyed();
                        // ctrl.ReduceBoostCapacity(2f); // flat fallback if OnFuelPartDestroyed not yet available

                    var aiCtrl = vehicle.GetComponent<AIController>();
                    if (aiCtrl != null)
                        aiCtrl.OnFuelPartDestroyed();
                }

                // Reduce speed when a propulsion part is destroyed
                if (IsPropulsionPart(target))
                {
                    var playerCtrl = vehicle.GetComponent<CloseEncounters.Combat.PlayerVehicleController>();
                    if (playerCtrl != null)
                        playerCtrl.OnPropulsionPartDestroyed();

                    var aiCtrl = vehicle.GetComponent<AIController>();
                    if (aiCtrl != null)
                        aiCtrl.OnPropulsionPartDestroyed();

                    var waterPhys = vehicle.GetComponent<CloseEncounters.VehiclePhysics.WaterPhysics>();
                    if (waterPhys != null)
                        waterPhys.OnPropulsionPartDestroyed();

                    var groundPhys = vehicle.GetComponent<CloseEncounters.VehiclePhysics.GroundPhysics>();
                    if (groundPhys != null)
                        groundPhys.OnPropulsionPartDestroyed();
                }

                // Check fuel tank detonation
                if (IsFuelTank(target) && !IsArmored(target))
                {
                    if (Random.value < FuelDetonationChance)
                    {
                        DetonateFuelTank(vehicle, target);
                    }
                }

                EjectDestroyedPart(target);
            }
        }

        // =====================================================================
        // Part Lookup
        // =====================================================================

        /// <summary>
        /// Find the closest non-destroyed PartNode to a world-space point.
        /// </summary>
        private static PartNode FindNearestPart(VehicleRuntime vehicle, Vector3 point)
        {
            PartNode closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < vehicle.PartNodes.Count; i++)
            {
                PartNode node = vehicle.PartNodes[i];
                if (node == null || node.isDestroyed) continue;

                float dist = Vector3.SqrMagnitude(node.transform.position - point);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = node;
                }
            }

            return closest;
        }

        /// <summary>
        /// Find nearest non-control part. Used by dragon attacks to avoid
        /// one-shotting cockpits/bridges. Falls back to any part if no
        /// non-control parts remain.
        /// </summary>
        private static PartNode FindNearestNonControlPart(VehicleRuntime vehicle, Vector3 point)
        {
            PartNode closest = null;
            PartNode fallback = null;
            float closestDist = float.MaxValue;
            float fallbackDist = float.MaxValue;

            for (int i = 0; i < vehicle.PartNodes.Count; i++)
            {
                PartNode node = vehicle.PartNodes[i];
                if (node == null || node.isDestroyed) continue;

                float dist = Vector3.SqrMagnitude(node.transform.position - point);

                bool isControl = node.partData != null &&
                    string.Equals(node.partData.category, "control",
                        System.StringComparison.OrdinalIgnoreCase);

                if (!isControl && dist < closestDist)
                {
                    closestDist = dist;
                    closest = node;
                }
                if (dist < fallbackDist)
                {
                    fallbackDist = dist;
                    fallback = node;
                }
            }

            return closest ?? fallback;
        }

        // =====================================================================
        // Fuel Tank Detonation
        // =====================================================================

        private static bool IsFuelTank(PartNode node)
        {
            if (node.partData == null) return false;
            string id = node.partData.id ?? "";
            string sub = node.partData.subcategory ?? "";
            return id.Contains("fuel") || sub.Contains("fuel");
        }

        private static bool IsPropulsionPart(PartNode node)
        {
            if (node.partData == null) return false;
            return string.Equals(node.partData.category, "propulsion", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsArmored(PartNode node)
        {
            if (node.partData == null) return false;
            string id = node.partData.id ?? "";
            return id.Contains("armored");
        }

        /// <summary>
        /// Detonation chain: destroy adjacent parts and create a large explosion.
        /// </summary>
        private static void DetonateFuelTank(VehicleRuntime vehicle, PartNode fuelNode)
        {
            Vector3 center = fuelNode.transform.position;
            float blastRadius = 4f;

            // ParticlePack VFX: fireball for fuel tank detonation
            VFXManager.FireBall(center, 2f);

            SpawnExplosionFX(center, 3f);

            // Damage nearby parts with splash
            for (int i = 0; i < vehicle.PartNodes.Count; i++)
            {
                PartNode node = vehicle.PartNodes[i];
                if (node == null || node.isDestroyed || node == fuelNode) continue;

                float dist = Vector3.Distance(node.transform.position, center);
                if (dist < blastRadius)
                {
                    int splashDmg = Mathf.CeilToInt(80f * (1f - dist / blastRadius));
                    bool partDestroyed = node.TakeDamage(splashDmg);
                    if (partDestroyed)
                    {
                        EjectDestroyedPart(node);
                    }
                }
            }

            Debug.Log($"[DamageSystem] Fuel tank detonation at {center}");
        }

        // =====================================================================
        // Part Ejection
        // =====================================================================

        /// <summary>
        /// Eject a destroyed part as a Rigidbody debris object that flies off
        /// and is cleaned up after DebrisLifetime seconds.
        /// </summary>
        public static void EjectDestroyedPart(PartNode destroyedPart)
        {
            if (destroyedPart == null) return;

            // Create debris clone
            var debrisObj = new GameObject($"Debris_{destroyedPart.partData?.id ?? "unknown"}");
            debrisObj.transform.position = destroyedPart.transform.position;
            debrisObj.transform.rotation = destroyedPart.transform.rotation;

            // Visual: small dark cube representing the wreck
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(debrisObj.transform, false);
            visual.transform.localScale = new Vector3(
                destroyedPart.partData != null ? destroyedPart.partData.size.x * 0.8f : 0.5f,
                destroyedPart.partData != null ? destroyedPart.partData.size.y * 0.8f : 0.5f,
                destroyedPart.partData != null ? destroyedPart.partData.size.z * 0.8f : 0.5f
            );
            var rend = visual.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                rend.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                rend.material.color = new Color(0.2f, 0.2f, 0.2f);
            }
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            // Physics
            var col = debrisObj.AddComponent<BoxCollider>();
            col.size = visual.transform.localScale;

            var rb = debrisObj.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.linearDamping = 0.5f;

            // Eject in random upward direction
            Vector3 ejectDir = (Vector3.up + Random.insideUnitSphere).normalized;
            rb.AddForce(ejectDir * DebrisEjectForce, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 5f, ForceMode.VelocityChange);

            // ParticlePack VFX: sparks on part ejection
            VFXManager.Sparks(destroyedPart.transform.position, 0.3f);

            // Small explosion at part location
            SpawnExplosionFX(destroyedPart.transform.position, 1f);

            // Cleanup after delay
            var cleanup = debrisObj.AddComponent<TimedDestroy>();
            cleanup.lifetime = DebrisLifetime;
        }

        // =====================================================================
        // Iceberg Shattering
        // =====================================================================

        /// <summary>
        /// Shatter an iceberg into sinking chunks. The original iceberg
        /// GameObject is destroyed and replaced with smaller debris pieces.
        /// </summary>
        public static void ShatterIceberg(GameObject iceberg)
        {
            if (iceberg == null) return;

            Vector3 center = iceberg.transform.position;
            float scale = iceberg.transform.localScale.x;
            if (scale < 1f) scale = 8f; // fallback

            // Get approximate size from child collider
            var col = iceberg.GetComponent<BoxCollider>();
            if (col != null) scale = col.size.x;

            SpawnExplosionFX(center, scale * 0.3f);

            // ParticlePack VFX: big splash for iceberg shattering
            VFXManager.BigSplash(center);

            // Create sinking chunks
            Color iceColor = new Color(0.70f, 0.85f, 0.95f);
            for (int i = 0; i < IcebergChunkCount; i++)
            {
                Vector3 offset = Random.insideUnitSphere * scale * 0.4f;
                offset.y = Mathf.Abs(offset.y);

                var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = $"IceChunk_{i}";
                chunk.transform.position = center + offset;
                chunk.transform.rotation = Random.rotation;

                float chunkScale = Random.Range(scale * 0.1f, scale * 0.25f);
                chunk.transform.localScale = new Vector3(chunkScale, chunkScale * 0.7f, chunkScale * 0.9f);

                var chunkRend = chunk.GetComponent<MeshRenderer>();
                if (chunkRend != null)
                {
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    mat.SetFloat("_Mode", 3);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.renderQueue = 3000;
                    mat.color = new Color(iceColor.r, iceColor.g, iceColor.b, 0.7f);
                    chunkRend.material = mat;
                }

                // Physics
                var rb = chunk.AddComponent<Rigidbody>();
                rb.mass = chunkScale * 10f;
                rb.linearDamping = 2f;
                rb.AddForce(offset.normalized * 3f + Vector3.up * 2f, ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.VelocityChange);

                // Sinking behavior
                var sink = chunk.AddComponent<SinkingChunk>();
                sink.sinkSpeed = IcebergChunkSinkSpeed;
                sink.lifetime = IcebergChunkLifetime;
            }

            // Destroy original iceberg
            Object.Destroy(iceberg);

            Debug.Log($"[DamageSystem] Iceberg shattered at {center}");
        }

        // =====================================================================
        // Explosion VFX
        // =====================================================================

        /// <summary>
        /// Spawn a particle-based explosion effect at the given position.
        /// Uses Unity's ParticleSystem for fire/smoke burst.
        /// </summary>
        public static void SpawnExplosionFX(Vector3 position, float scale)
        {
            // Pure ParticlePack VFX — no more programmatic particle systems
            if (scale >= 2f)
                VFXManager.BigExplosion(position, scale * 0.5f);
            else
                VFXManager.SmallExplosion(position, scale);
        }

        // =====================================================================
        // Damage Utility
        // =====================================================================

        /// <summary>
        /// Apply area-of-effect damage to all vehicles within a radius.
        /// </summary>
        public static void DealAreaDamage(Vector3 center, float radius, int maxDamage)
        {
            if (ArenaManager.Instance == null) return;

            // ParticlePack VFX: dust explosion for area damage
            VFXManager.DustExplosion(center, radius * 0.3f);

            var vehicles = ArenaManager.Instance.GetVehicles();
            for (int i = 0; i < vehicles.Count; i++)
            {
                if (vehicles[i] == null || !vehicles[i].IsAlive) continue;

                float dist = Vector3.Distance(vehicles[i].transform.position, center);
                if (dist < radius)
                {
                    float falloff = 1f - (dist / radius);
                    int dmg = Mathf.CeilToInt(maxDamage * falloff);
                    DealDamageToVehicle(vehicles[i], dmg, center);
                }
            }
        }

        /// <summary>
        /// Attempt to damage an iceberg at the given point.
        /// Returns true if an iceberg was found and damaged.
        /// </summary>
        public static bool DamageIcebergAt(Vector3 point, int damage, float searchRadius = 5f)
        {
            Collider[] hits = Physics.OverlapSphere(point, searchRadius);
            for (int i = 0; i < hits.Length; i++)
            {
                var iceberg = hits[i].GetComponent<Iceberg>();
                if (iceberg == null) iceberg = hits[i].GetComponentInParent<Iceberg>();
                if (iceberg != null)
                {
                    iceberg.TakeDamage(damage);
                    return true;
                }
            }
            return false;
        }
    }

    // =========================================================================
    // Helper Components
    // =========================================================================

    /// <summary>
    /// Destroys the GameObject after a set lifetime.
    /// </summary>
    public class TimedDestroy : MonoBehaviour
    {
        public float lifetime = 5f;
        private float _timer;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= lifetime)
                Destroy(gameObject);
        }
    }

    /// <summary>
    /// Fades a Light component intensity to zero over duration, then destroys.
    /// </summary>
    public class FadeLight : MonoBehaviour
    {
        public float duration = 0.5f;
        private Light _light;
        private float _startIntensity;
        private float _timer;

        private void Start()
        {
            _light = GetComponent<Light>();
            if (_light != null) _startIntensity = _light.intensity;
        }

        private void Update()
        {
            if (_light == null) return;
            _timer += Time.deltaTime;
            _light.intensity = Mathf.Lerp(_startIntensity, 0f, _timer / duration);
            if (_timer >= duration)
                Destroy(this);
        }
    }

    /// <summary>
    /// Iceberg chunk that sinks over time and fades out, then self-destructs.
    /// </summary>
    public class SinkingChunk : MonoBehaviour
    {
        public float sinkSpeed = 2f;
        public float lifetime = 4f;

        private float _timer;
        private MeshRenderer _renderer;

        private void Start()
        {
            _renderer = GetComponent<MeshRenderer>();
        }

        private void Update()
        {
            _timer += Time.deltaTime;

            // Sink
            transform.position += Vector3.down * sinkSpeed * Time.deltaTime;

            // Fade alpha
            if (_renderer != null && _renderer.material != null)
            {
                Color c = _renderer.material.color;
                c.a = Mathf.Lerp(0.7f, 0f, _timer / lifetime);
                _renderer.material.color = c;
            }

            // Slow rotation
            transform.Rotate(Vector3.up * 20f * Time.deltaTime, Space.World);

            if (_timer >= lifetime)
                Destroy(gameObject);
        }
    }
}
