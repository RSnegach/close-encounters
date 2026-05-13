#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CloseEncounters.Arena;

namespace CloseEncounters.Editor
{
    /// <summary>
    /// Editor tool that generates editable arena scenes from the current procedural
    /// arena code. Run once to create scenes, then edit them with terrain tools,
    /// EasyRoads3D, Waterworks, etc.
    ///
    /// Menu: Close Encounters > Generate Arena Scenes
    /// </summary>
    public static class ArenaSceneSetup
    {
        private static readonly Dictionary<string, System.Type> GroundArenas = new Dictionary<string, System.Type>
        {
            { "desert_flat",  typeof(GroundDesert) },
            { "town",         typeof(GroundTown) },
            { "arctic",       typeof(GroundArctic) },
            { "volcanic",     typeof(GroundVolcanic) },
            { "highlands",    typeof(GroundHighlands) },
        };

        private static readonly Dictionary<string, System.Type> WaterArenas = new Dictionary<string, System.Type>
        {
            { "archipelago",  typeof(WaterArchipelago) },
            { "titans_peak",  typeof(WaterTitansPeak) },
            { "frozen_strait",typeof(WaterFrozenStrait) },
            { "kraken_lair",  typeof(WaterKrakenLair) },
            { "corsair_bay",  typeof(WaterCorsairBay) },
        };

        [MenuItem("Close Encounters/Generate Arena Scenes")]
        public static void GenerateAllArenaScenes()
        {
            if (!EditorUtility.DisplayDialog("Generate Arena Scenes",
                "This will create 10 arena scene files in Assets/Scenes/Arenas/.\n\n" +
                "Each scene will contain the procedurally generated terrain, structures, " +
                "and spawn points from the current arena code. You can then edit them " +
                "with terrain tools, EasyRoads3D, Waterworks, etc.\n\n" +
                "Existing scenes with the same names will be OVERWRITTEN.\n\nProceed?",
                "Generate", "Cancel"))
                return;

            int count = 0;

            foreach (var kv in GroundArenas)
            {
                GenerateArenaScene(kv.Key, kv.Value, false);
                count++;
                EditorUtility.DisplayProgressBar("Generating Arena Scenes",
                    $"Building {kv.Key}...", (float)count / 10);
            }

            foreach (var kv in WaterArenas)
            {
                GenerateArenaScene(kv.Key, kv.Value, true);
                count++;
                EditorUtility.DisplayProgressBar("Generating Arena Scenes",
                    $"Building {kv.Key}...", (float)count / 10);
            }

            EditorUtility.ClearProgressBar();
            AddScenesToBuildSettings();

            EditorUtility.DisplayDialog("Done!",
                $"Generated {count} arena scenes in Assets/Scenes/Arenas/.\n\n" +
                "You can now open each scene and edit terrain, add roads with EasyRoads3D, " +
                "add rivers with Waterworks, place props, etc.\n\n" +
                "All scenes have been added to Build Settings.",
                "OK");
        }

        [MenuItem("Close Encounters/Generate Single Arena Scene")]
        public static void GenerateSingleArenaScene()
        {
            var allArenas = new List<string>();
            allArenas.AddRange(GroundArenas.Keys);
            allArenas.AddRange(WaterArenas.Keys);

            var menu = new GenericMenu();
            foreach (var key in allArenas)
            {
                string k = key;
                menu.AddItem(new GUIContent(k), false, () =>
                {
                    if (GroundArenas.TryGetValue(k, out var gt))
                        GenerateArenaScene(k, gt, false);
                    else if (WaterArenas.TryGetValue(k, out var wt))
                        GenerateArenaScene(k, wt, true);
                    AddScenesToBuildSettings();
                    Debug.Log($"[ArenaSceneSetup] Generated scene for '{k}'");
                });
            }
            menu.ShowAsContext();
        }

        private static void GenerateArenaScene(string key, System.Type arenaType, bool isWater)
        {
            // Create a new empty scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Create the arena root with SceneBasedArena
            var root = new GameObject("ArenaRoot");
            var sceneArena = root.AddComponent<SceneBasedArena>();

            // Also temporarily add the procedural arena to generate geometry
            var proceduralArena = root.AddComponent(arenaType) as ArenaBase;
            if (proceduralArena != null)
            {
                sceneArena.arenaDisplayName = proceduralArena.ArenaName;
                proceduralArena.Build(); // generates terrain, structures, spawn points

                // Move spawn points from procedural to scene-based naming
                for (int i = 0; i < proceduralArena.SpawnPoints.Count; i++)
                {
                    var sp = proceduralArena.SpawnPoints[i];
                    sp.name = $"SpawnPoint_{i}";
                    sp.SetParent(root.transform, true);
                }

                // Remove the procedural component — we keep the generated GameObjects
                Object.DestroyImmediate(proceduralArena);
            }

            // Add directional light
            var lightObj = new GameObject("DirectionalLight");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.84f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Set skybox
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.6f, 0.8f);

            // Save the scene
            string scenePath = $"Assets/Scenes/Arenas/Arena_{key}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            Debug.Log($"[ArenaSceneSetup] Saved arena scene: {scenePath}");
        }

        private static void AddScenesToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            var existingPaths = new HashSet<string>();
            foreach (var s in scenes) existingPaths.Add(s.path);

            // Add all arena scenes
            var allKeys = new List<string>();
            allKeys.AddRange(GroundArenas.Keys);
            allKeys.AddRange(WaterArenas.Keys);

            foreach (var key in allKeys)
            {
                string path = $"Assets/Scenes/Arenas/Arena_{key}.unity";
                if (!existingPaths.Contains(path))
                {
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                    Debug.Log($"[ArenaSceneSetup] Added to build settings: {path}");
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
