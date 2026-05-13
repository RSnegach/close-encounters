using UnityEngine;
using CloseEncounters.Combat;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Spawns a herd of horses as Mob instances. Each horse has its own
    /// AI roaming, HP, vehicle/projectile collision, and death effects
    /// handled by the Mob component.
    /// </summary>
    public class HorseHerd : MonoBehaviour
    {
        public int horseCount = 30;
        public float spawnRadius = 150f;
        public float roamRadius = 40f;

        private void Start()
        {
            var prefab = Resources.Load<GameObject>("Models/Animals/Horse_001");
            if (prefab == null)
            {
                Debug.LogWarning("[HorseHerd] Horse_001 prefab not found in Resources!");
                return;
            }

            for (int i = 0; i < horseCount; i++)
            {
                Vector2 rng = Random.insideUnitCircle * spawnRadius;
                Vector3 pos = transform.position + new Vector3(rng.x, 0f, rng.y);
                // Sample terrain height to prevent underground spawning
                var terrain = Terrain.activeTerrain;
                if (terrain != null)
                    pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y + 1f;
                else
                    pos.y = 2f;

                var horse = Instantiate(prefab, pos,
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                horse.name = $"Horse_{i}";
                horse.transform.localScale = Vector3.one * 1.2f;

                // Fix URP materials
                CityPrefabHelper.FixURPMaterials(horse.transform);

                // Attach Mob component with horse-specific config
                var mob = horse.AddComponent<Mob>();
                mob.maxHP = 60; // dies to ~6 machine gun bullets or 1 missile
                mob.roamRadius = roamRadius;
                mob.walkSpeed = 3f;
                mob.runSpeed = 8f;
                mob.idleAnim = "Horse_001_idle";
                mob.walkAnim = "Horse_001_walk";
                mob.runAnim = "Horse_001_run";
                mob.eatAnim = "Horse_001_eat";
            }
        }
    }
}
