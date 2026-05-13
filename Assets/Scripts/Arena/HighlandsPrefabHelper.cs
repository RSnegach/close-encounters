using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Utility for loading highlands/nature prefabs from Resources at runtime.
    /// MeshColliders for accurate hitboxes. Mass proportional to object size.
    /// Used by GroundHighlands (Kyrgyzstan) arena.
    /// </summary>
    public static class HighlandsPrefabHelper
    {
        public static GameObject PlaceMountain(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Highlands/Mountains/{prefabName}", position, yRotation, scale, true, false);
        }

        public static GameObject PlaceTree(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Highlands/Trees/{prefabName}", position, yRotation, scale, true, true);
        }

        public static GameObject PlaceBush(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Highlands/Bushes/{prefabName}", position, yRotation, scale, true, true);
        }

        public static GameObject PlaceRock(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Highlands/Rocks/{prefabName}", position, yRotation, scale, true, true);
        }

        public static GameObject PlaceFoliage(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Highlands/Foliage/{prefabName}", position, yRotation, scale, true, true);
        }

        public static GameObject PlaceWater(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"Highlands/Water/{prefabName}", position, yRotation, scale, false, false);
        }

        private static GameObject PlacePrefab(Transform parent, string resourcePath, Vector3 position,
            float yRotation, float scale, bool addCollider, bool breakable)
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"[HighlandsPrefabHelper] Failed to load '{resourcePath}'");
                return null;
            }

            var instance = Object.Instantiate(prefab, parent);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            instance.transform.localScale = Vector3.one * scale;

            CityPrefabHelper.FixURPMaterials(instance.transform);

            if (addCollider)
                AddPreciseColliders(instance, convex: breakable);

            float maxDim = GetMaxBoundsDimension(instance);
            bool isLarge = maxDim > 4f;

            if (breakable && !isLarge)
            {
                // Mass proportional to volume: small bushes ~5kg, medium trees ~40kg, poles ~80kg
                float mass = Mathf.Clamp(maxDim * maxDim * 2f, 1f, 150f);
                var rb = instance.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.mass = mass;
                rb.linearDamping = 0.5f;
                var bp = instance.AddComponent<BreakableProp>();

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

        private static void AddMeshColliders(GameObject obj)
        {
            AddPreciseColliders(obj, convex: false);
        }

        // why: prefab-shipped Box/Sphere/Capsule colliders often don't match the mesh —
        // swap to precise MeshColliders.
        public static void AddPreciseColliders(GameObject obj, bool convex)
        {
            var meshFilters = obj.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0) { CityPrefabHelper.AddBoundsCollider(obj); return; }

            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf.sharedMesh == null) continue;
                string nm = mf.gameObject.name == null ? "" : mf.gameObject.name.ToLowerInvariant();
                if (nm.Contains("lod1") || nm.Contains("lod2") || nm.Contains("lod3")
                    || nm.Contains("detail") || nm.Contains("decor") || nm.Contains("trim")) continue;
                var r = mf.GetComponent<Renderer>();
                if (r != null)
                {
                    var b = r.bounds.size;
                    if (b.x < 0.3f && b.y < 0.3f && b.z < 0.3f) continue;
                }
                var existing = mf.GetComponents<Collider>();
                for (int c = 0; c < existing.Length; c++)
                {
                    var col = existing[c];
                    if (col == null || col is TerrainCollider || col.isTrigger) continue;
                    if (col is MeshCollider mcOld && mcOld.convex == convex) continue;
                    Object.DestroyImmediate(col);
                }
                if (mf.GetComponent<MeshCollider>() != null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = convex;
            }
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
