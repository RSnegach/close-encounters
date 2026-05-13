using System.Collections.Generic;
using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Post-Build pass that expands an arena's invisible bounds for air mode.
    /// Ground/water arenas use ~300-750 unit half-extents which aircraft cross in
    /// a few seconds. This scans for existing perimeter walls, removes them, and
    /// installs larger trigger walls named "AirBounds_*" so OutOfBoundsController
    /// can detect fly-through events instead of the walls acting as solid bumpers.
    /// </summary>
    public static class AirArenaExpander
    {
        // why: aircraft cross small arenas in seconds; floor of 1500 keeps chase room even on tiny maps
        private const float MinHalfExtent = 1500f;
        private const float HalfExtentMultiplier = 2f;
        // why: vertical headroom for climbs/dives; old ArenaManager capped at 100
        private const float CeilingHeight = 800f;
        private const float WallThickness = 10f;
        private const string BoundsPrefix = "AirBounds";

        /// <summary>
        /// Apply the air expansion to an arena transform. No-op if arenaRoot is null.
        /// Safe to call on SceneBasedArena or procedural arenas.
        /// </summary>
        public static void Apply(Transform arenaRoot)
        {
            if (arenaRoot == null) return;

            float detected = DetectCurrentHalfExtent(arenaRoot);
            float airHalf = Mathf.Max(MinHalfExtent, detected * HalfExtentMultiplier);

            RemoveExistingPerimeterWalls(arenaRoot);
            BuildAirBounds(arenaRoot, airHalf, CeilingHeight);

            Debug.Log($"[AirArenaExpander] Expanded '{arenaRoot.name}' from halfExtent={detected:F0} " +
                      $"to airHalf={airHalf:F0}, ceiling={CeilingHeight:F0}");
        }

        /// <summary>
        /// Walk the arena hierarchy looking for existing perimeter wall colliders and
        /// return the largest |x|/|z| extent found. Returns 0 if none detected.
        /// </summary>
        private static float DetectCurrentHalfExtent(Transform root)
        {
            float maxExtent = 0f;
            var colliders = root.GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var c = colliders[i];
                if (c == null) continue;
                if (!IsPerimeterWallName(c.gameObject.name)) continue;

                Bounds b = c.bounds;
                float ex = Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x));
                float ez = Mathf.Max(Mathf.Abs(b.min.z), Mathf.Abs(b.max.z));
                float e = Mathf.Max(ex, ez);
                if (e > maxExtent) maxExtent = e;
            }
            return maxExtent;
        }

        private static void RemoveExistingPerimeterWalls(Transform root)
        {
            var toKill = new List<GameObject>();
            var colliders = root.GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var c = colliders[i];
                if (c == null) continue;
                if (IsPerimeterWallName(c.gameObject.name))
                    toKill.Add(c.gameObject);
            }
            for (int i = 0; i < toKill.Count; i++)
                Object.Destroy(toKill[i]);
        }

        private static bool IsPerimeterWallName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("ArenaWall") ||
                   name.StartsWith("InvisibleWall") ||
                   name.StartsWith(BoundsPrefix);
        }

        private static void BuildAirBounds(Transform parent, float halfExtent, float height)
        {
            float t = WallThickness;
            // (name, position, size)
            var names = new[] { "North", "South", "East", "West", "Ceiling" };
            var positions = new[]
            {
                new Vector3(0f, height * 0.5f,  halfExtent + t * 0.5f),
                new Vector3(0f, height * 0.5f, -halfExtent - t * 0.5f),
                new Vector3( halfExtent + t * 0.5f, height * 0.5f, 0f),
                new Vector3(-halfExtent - t * 0.5f, height * 0.5f, 0f),
                new Vector3(0f, height + t * 0.5f, 0f),
            };
            var sizes = new[]
            {
                new Vector3(halfExtent * 2f + t * 2f, height, t),
                new Vector3(halfExtent * 2f + t * 2f, height, t),
                new Vector3(t, height, halfExtent * 2f + t * 2f),
                new Vector3(t, height, halfExtent * 2f + t * 2f),
                new Vector3(halfExtent * 2f + t * 2f, t, halfExtent * 2f + t * 2f),
            };

            Material translucent = MakeTranslucentMaterial();

            for (int i = 0; i < names.Length; i++)
            {
                var go = new GameObject($"{BoundsPrefix}_{names[i]}");
                go.layer = 2; // why: IgnoreRaycast keeps spawn/aim raycasts from hitting bounds
                go.transform.SetParent(parent, false);
                go.transform.position = positions[i];

                var col = go.AddComponent<BoxCollider>();
                col.size = sizes[i];
                col.isTrigger = true; // why: aircraft pass through and OOB controller starts countdown

                AttachVisual(go, sizes[i], translucent);
            }
        }

        private static void AttachVisual(GameObject go, Vector3 size, Material mat)
        {
            if (mat == null) return;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "BoundsVisual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = size;
            var rend = visual.GetComponent<MeshRenderer>();
            if (rend != null) rend.sharedMaterial = mat;
            Object.DestroyImmediate(visual.GetComponent<Collider>());
        }

        private static Material _cachedMat;
        private static Material MakeTranslucentMaterial()
        {
            if (_cachedMat != null) return _cachedMat;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.name = "AirBoundsVisual";
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = 3000;
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // why: pale cyan alpha 0.05 — visible edge without obstructing flight view
            mat.SetColor("_BaseColor", new Color(0.4f, 0.9f, 1f, 0.05f));
            _cachedMat = mat;
            return mat;
        }
    }
}
