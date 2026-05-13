using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Utility for loading POLYGON City Pack prefabs from Resources at runtime,
    /// fixing URP materials, and adding collision. Used by GroundTown arena.
    /// </summary>
    public static class CityPrefabHelper
    {
        /// <summary>
        /// Load and place a city building prefab. Adds BoxCollider from mesh bounds.
        /// </summary>
        public static GameObject PlaceBuilding(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"City/Buildings/{prefabName}", position, yRotation, scale, true);
        }

        /// <summary>
        /// Load and place a road/floor prefab.
        /// </summary>
        public static GameObject PlaceRoad(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"City/Roads/{prefabName}", position, yRotation, scale, false);
        }

        /// <summary>
        /// Load and place a lamp/street light prefab.
        /// </summary>
        public static GameObject PlaceLamp(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"City/Lamps/{prefabName}", position, yRotation, scale, true, breakable: true);
        }

        /// <summary>
        /// Load and place a prop prefab (bench, tree, sign, etc.).
        /// </summary>
        public static GameObject PlaceProp(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"City/Props/{prefabName}", position, yRotation, scale, true, breakable: true);
        }

        /// <summary>
        /// Load and place a traffic sign prefab.
        /// </summary>
        public static GameObject PlaceSign(Transform parent, string prefabName, Vector3 position,
            float yRotation = 0f, float scale = 1f)
        {
            return PlacePrefab(parent, $"City/Signs/{prefabName}", position, yRotation, scale, true, breakable: true);
        }

        private static GameObject PlacePrefab(Transform parent, string resourcePath, Vector3 position,
            float yRotation, float scale, bool addCollider, bool breakable = false)
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"[CityPrefabHelper] Failed to load '{resourcePath}'");
                return null;
            }

            var instance = Object.Instantiate(prefab, parent);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            instance.transform.localScale = Vector3.one * scale;

            FixURPMaterials(instance.transform);

            if (addCollider)
            {
                // Trees get a thin trunk collider, everything else gets precise MeshColliders
                bool isTree = resourcePath.ToLowerInvariant().Contains("tree");
                if (isTree)
                    AddTrunkCollider(instance);
                else
                    AddPreciseColliders(instance, convex: breakable);
            }

            if (breakable)
            {
                // Mass proportional to size: small props fly, heavy ones topple gently
                float maxDim = GetMaxBoundsDimension(instance);
                float mass = Mathf.Clamp(maxDim * maxDim * 2f, 1f, 150f);
                var rb = instance.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.mass = mass;
                rb.linearDamping = 0.5f;
                var bp = instance.AddComponent<BreakableProp>();

                var respawner = BreakablePropRespawner.GetOrCreate();
                int id = respawner.Register(instance);
                bp.AttachRespawner(respawner, id);
                // Don't set isStatic -- physics needs it dynamic
            }
            else
            {
                instance.isStatic = true;
            }

            return instance;
        }

        /// <summary>
        /// Add a BoxCollider that encompasses all rendered meshes.
        /// </summary>
        public static void AddBoundsCollider(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            // Convert world bounds to local space with per-axis scale
            Vector3 localCenter = obj.transform.InverseTransformPoint(bounds.center);
            Vector3 ls = obj.transform.lossyScale;
            Vector3 localSize = new Vector3(
                ls.x != 0f ? bounds.size.x / Mathf.Abs(ls.x) : bounds.size.x,
                ls.y != 0f ? bounds.size.y / Mathf.Abs(ls.y) : bounds.size.y,
                ls.z != 0f ? bounds.size.z / Mathf.Abs(ls.z) : bounds.size.z
            );

            // Always add a parent-level BoxCollider from mesh bounds
            // (prefab child colliders may be incomplete or missing)
            var box = obj.AddComponent<BoxCollider>();
            box.center = localCenter;
            box.size = localSize;
        }

        /// <summary>
        /// Legacy entry point — routes through AddPreciseColliders with non-convex.
        /// </summary>
        public static void AddMeshColliders(GameObject obj)
        {
            AddPreciseColliders(obj, convex: false);
        }

        // why: prefab-shipped Box/Capsule/Sphere colliders rarely match the actual mesh
        // shape. Strip them, then add precise MeshColliders per mesh child.
        public static void AddPreciseColliders(GameObject obj, bool convex)
        {
            var meshFilters = obj.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0)
            {
                AddBoundsCollider(obj);
                return;
            }

            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf.sharedMesh == null) continue;
                if (IsDecorMesh(mf)) continue;

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
                string low = n.ToLowerInvariant();
                if (low.Contains("lod1") || low.Contains("lod2") || low.Contains("lod3")
                    || low.Contains("detail") || low.Contains("decor") || low.Contains("trim")
                    || low.Contains("sign") || low.Contains("antenna") || low.Contains("window_frame"))
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

        /// <summary>
        /// Add a thin trunk-only collider for trees. Uses full height but narrow width
        /// so vehicles hit the trunk, not the wide canopy. Makes trees easy to knock over.
        /// </summary>
        public static void AddTrunkCollider(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 localCenter = obj.transform.InverseTransformPoint(bounds.center);
            Vector3 ls = obj.transform.lossyScale;
            float height = ls.y != 0f ? bounds.size.y / Mathf.Abs(ls.y) : bounds.size.y;
            // Trunk is ~20% of the full canopy width
            float trunkWidth = ls.x != 0f ? (bounds.size.x / Mathf.Abs(ls.x)) * 0.2f : 0.5f;

            var box = obj.AddComponent<BoxCollider>();
            box.center = new Vector3(localCenter.x, localCenter.y, localCenter.z);
            box.size = new Vector3(trunkWidth, height, trunkWidth);
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

        /// <summary>
        /// Fix non-URP materials to prevent pink rendering.
        /// </summary>
        public static void FixURPMaterials(Transform root)
        {
            var urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) return;

            var renderers = root.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].materials;
                bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && mats[m].shader != urpShader
                        && !mats[m].shader.name.Contains("Universal"))
                    {
                        Color c = mats[m].HasProperty("_Color") ? mats[m].color : Color.gray;
                        Texture tex = mats[m].HasProperty("_MainTex") ? mats[m].mainTexture : null;
                        mats[m] = new Material(urpShader);
                        mats[m].color = c;
                        if (tex != null) mats[m].mainTexture = tex;
                        changed = true;
                    }
                }
                if (changed) renderers[i].materials = mats;
            }
        }
    }
}
