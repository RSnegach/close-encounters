using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloseEncounters.Editors
{
    /// <summary>
    /// One-shot Editor utility. Opens Habrador's tornado.unity, reparents its
    /// external helper objects (Chase Obj, Chase rotate obj, Target Obj, Debug
    /// Sphere) under the "Tornado" GameObject, then saves the whole hierarchy
    /// as Resources/HabradorTornado/TornadoPrefab.prefab so runtime code can
    /// spawn it with Resources.Load.
    ///
    /// Run once from: Tools -> Close Encounters -> Build Habrador Tornado Prefab.
    /// Does not modify tornado.unity on disk — reparenting happens in-memory only.
    /// </summary>
    public static class HabradorTornadoPrefabBuilder
    {
        private const string ScenePath = "Assets/HabradorTornado/tornado.unity";
        private const string ResourcesDir = "Assets/HabradorTornado/Resources/HabradorTornado";
        private const string PrefabPath = ResourcesDir + "/TornadoPrefab.prefab";

        [MenuItem("Tools/Close Encounters/Build Habrador Tornado Prefab")]
        public static void BuildPrefab()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("Habrador Tornado",
                    "Can't find " + ScenePath + ". Did the asset copy succeed?", "OK");
                return;
            }

            // Save current scene state before we load another one.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            GameObject tornado = FindRoot(scene, "Tornado");
            if (tornado == null)
            {
                EditorUtility.DisplayDialog("Habrador Tornado",
                    "Couldn't find a root GameObject named 'Tornado' in the scene.", "OK");
                return;
            }

            string[] externalNames = { "Chase Obj", "Chase rotate obj", "Target Obj", "Debug Sphere" };
            int reparented = 0;
            foreach (string name in externalNames)
            {
                GameObject go = FindRoot(scene, name);
                if (go == null)
                {
                    Debug.LogWarning("[HabradorTornadoPrefabBuilder] Skipped missing object: " + name);
                    continue;
                }
                go.transform.SetParent(tornado.transform, worldPositionStays: true);
                reparented++;
            }

            if (!AssetDatabase.IsValidFolder(ResourcesDir))
            {
                EnsureFolder("Assets/HabradorTornado");
                EnsureFolder("Assets/HabradorTornado/Resources");
                EnsureFolder(ResourcesDir);
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tornado, PrefabPath, out bool success);
            if (!success || prefab == null)
            {
                EditorUtility.DisplayDialog("Habrador Tornado",
                    "Prefab save failed. Check console for errors.", "OK");
                return;
            }

            Debug.Log("[HabradorTornadoPrefabBuilder] Wrote " + PrefabPath +
                      " with " + reparented + " reparented helper(s).");

            EditorUtility.DisplayDialog("Habrador Tornado",
                "TornadoPrefab saved.\n\nReparented " + reparented + " helper object(s).\n\n" +
                "Combat scene will now spawn Habrador tornadoes in the desert arena.", "Nice");
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
