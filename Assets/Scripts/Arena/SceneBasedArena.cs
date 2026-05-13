using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Arena component for pre-built scenes. Instead of generating geometry,
    /// it finds existing SpawnPoint GameObjects in the scene hierarchy.
    /// Place this on the root GameObject of each arena scene.
    /// </summary>
    public class SceneBasedArena : ArenaBase
    {
        [Header("Arena Info")]
        public string arenaDisplayName = "Scene Arena";

        public override string ArenaName => arenaDisplayName;

        public override void Build()
        {
            // Find all spawn points in the scene by name prefix
            var allTransforms = GetComponentsInChildren<Transform>(true);
            var spawnTransforms = allTransforms
                .Where(t => t.name.StartsWith("SpawnPoint"))
                .OrderBy(t => t.name)
                .ToList();

            SpawnPoints.Clear();
            foreach (var sp in spawnTransforms)
                SpawnPoints.Add(sp);

            if (SpawnPoints.Count == 0)
            {
                Debug.LogWarning($"[SceneBasedArena] No spawn points found in '{arenaDisplayName}'. Generating fallback ring.");
                AddSpawnRing(Vector3.zero, 80f, 8, 2f);
            }

            Debug.Log($"[SceneBasedArena] '{arenaDisplayName}' ready with {SpawnPoints.Count} spawn points.");
        }
    }
}
