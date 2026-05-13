using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Utility for loading desert asset pack prefabs from Resources at runtime.
    /// Handles URP material fix, collision, and breakable prop setup.
    /// Used by GroundDesert (Albuquerque) arena.
    /// </summary>
    public static class DesertPrefabHelper
    {
        // Mountains/Canyons (InfinitaStudio) - static backdrop with collision
        public static GameObject PlaceMountain(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Desert/Mountains/{prefabName}", position, yRotation, scale, true, false);
        }

        // Desert Buildings - static with collision
        public static GameObject PlaceBuilding(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Desert/Buildings/{prefabName}", position, yRotation, scale, true, false);
        }

        // EZ Tornado effects - no collision, not breakable
        public static GameObject PlaceTornado(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Desert/Tornado/{prefabName}", position, yRotation, scale, false, false);
        }

        // Low-Poly Desert Environment (Tiny Teacup) - breakable with collision (like Fentchester props)
        public static GameObject PlaceLowPoly(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Desert/LowPoly/{prefabName}", position, yRotation, scale, true, true);
        }

        // 3D Environment desert props - breakable with collision
        public static GameObject PlaceDesertProp(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Desert/Props/{prefabName}", position, yRotation, scale, true, true);
        }

        private static GameObject PlacePrefab(Transform parent, string resourcePath, Vector3 position,
            float yRotation, float scale, bool addCollider, bool breakable)
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"[DesertPrefabHelper] Failed to load '{resourcePath}'");
                return null;
            }

            var instance = Object.Instantiate(prefab, parent);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            instance.transform.localScale = Vector3.one * scale;

            CityPrefabHelper.FixURPMaterials(instance.transform);

            if (addCollider)
                AddPreciseColliders(instance, convex: breakable);

            // Only large rocks and cactuses stay static; everything else is breakable
            // (even if it's large) so the tornado can lift it.
            float maxDim = GetMaxBoundsDimension(instance);
            bool isLandmark = maxDim > 4f &&
                              (ContainsNoCase(resourcePath, "rock") || ContainsNoCase(resourcePath, "cactus"));

            if (breakable && !isLandmark)
            {
                float mass = Mathf.Clamp(maxDim * maxDim * 2f, 1f, 300f);
                var rb = instance.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.mass = mass;
                rb.linearDamping = 0.5f;
                var bp = instance.AddComponent<BreakableProp>();

                // Register with the scene respawner so this spot regenerates
                // a fresh prop after the player knocks it out.
                var respawner = BreakablePropRespawner.GetOrCreate();
                int id = respawner.Register(instance);
                bp.AttachRespawner(respawner, id);
            }
            else
            {
                instance.isStatic = true;
            }

            return instance;
        }

        /// <summary>
        /// Legacy entry point — routes through AddPreciseColliders with non-convex.
        /// </summary>
        private static void AddMeshColliders(GameObject obj)
        {
            AddPreciseColliders(obj, convex: false);
        }

        // why: prefab-shipped Box/Capsule/Sphere colliders rarely match the actual mesh
        // shape, leaving invisible bumpers and gaps. Strip them, then add precise MeshColliders.
        public static void AddPreciseColliders(GameObject obj, bool convex)
        {
            var meshFilters = obj.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0)
            {
                var skinned = obj.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (skinned.Length > 0) CityPrefabHelper.AddBoundsCollider(obj);
                else CityPrefabHelper.AddBoundsCollider(obj);
                return;
            }

            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf.sharedMesh == null) continue;
                if (IsDecorMesh(mf)) continue;

                // Strip non-MeshCollider colliders that aren't triggers
                var existing = mf.GetComponents<Collider>();
                for (int c = 0; c < existing.Length; c++)
                {
                    var col = existing[c];
                    if (col == null) continue;
                    if (col is TerrainCollider) continue;
                    if (col.isTrigger) continue;
                    if (col is MeshCollider mcOld && mcOld.convex == convex) continue;
                    Object.DestroyImmediate(col);
                }
                if (mf.GetComponent<MeshCollider>() != null) continue;

                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = convex;
            }
        }

        private static bool IsDecorMesh(MeshFilter mf)
        {
            string n = mf.gameObject.name;
            if (!string.IsNullOrEmpty(n))
            {
                if (ContainsNoCase(n, "LOD1") || ContainsNoCase(n, "LOD2") || ContainsNoCase(n, "LOD3")
                    || ContainsNoCase(n, "decor") || ContainsNoCase(n, "detail") || ContainsNoCase(n, "vent"))
                    return true;
            }
            var rend = mf.GetComponent<Renderer>();
            if (rend != null)
            {
                var b = rend.bounds.size;
                if (b.x < 0.3f && b.y < 0.3f && b.z < 0.3f) return true;
            }
            return false;
        }

        private static bool ContainsNoCase(string haystack, string needle)
        {
            return haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float GetMaxBoundsDimension(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        }
    }
}
