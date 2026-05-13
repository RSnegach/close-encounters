using System.Collections.Generic;
using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Creates Unity Terrain objects at runtime with procedural heightmaps
    /// and splatmap painting using the imported terrain layers.
    /// Layers: 0=Grass1, 1=Grass2, 2=Dirt1, 3=Dirt2, 4=Sand, 5=MossyRock, 6=Cliff1, 7=Cliff2
    /// </summary>
    public static class TerrainFactory
    {
        public enum Layer
        {
            GrassA,      // 0 - lush green grass
            GrassB,      // 1 - lighter grass
            GrassDry,    // 2 - dry/dead grass
            GrassMoss,   // 3 - mossy grass
            Sand,        // 4 - beach sand
            BlackSand,   // 5 - volcanic black sand
            Snow,        // 6 - snow
            Rock,        // 7 - bare rock
            Muddy,       // 8 - mud
            PebblesA,    // 9 - pebbles
            SoilRocks,   // 10 - rocky soil
            Heather,     // 11 - heather/scrub
            GrassSoil,   // 12 - grass-soil transition
            TidalPools,  // 13 - tidal pools
            PebblesB,    // 14 - pebbles variant
            PebblesC,    // 15 - pebbles variant
        }

        // Texture names in Resources/Terrain/Textures/ (BaseColor + Normal)
        private static readonly string[] TextureNames =
        {
            "Grass_A",      // 0
            "Grass_B",      // 1
            "Grass_Dry",    // 2
            "Grass_Moss",   // 3
            "Sand",         // 4
            "Black_Sand",   // 5
            "Snow",         // 6
            "Rock",         // 7
            "Muddy",        // 8
            "Pebbles_A",    // 9
            "Soil_Rocks",   // 10
            "Heather",      // 11
            "Grass_Soil",   // 12
            "Tidal_Pools",  // 13
            "Pebbles_B",    // 14
            "Pebbles_C",    // 15
        };

        /// <summary>
        /// Create a terrain GameObject with the given parameters.
        /// </summary>
        /// <param name="parent">Parent transform for hierarchy.</param>
        /// <param name="position">World position of terrain origin (bottom-left corner).</param>
        /// <param name="size">Terrain size (width, height, length) in world units.</param>
        /// <param name="heightmapRes">Heightmap resolution (power of 2 + 1, e.g. 129, 257).</param>
        /// <param name="name">GameObject name.</param>
        public static Terrain Create(Transform parent, Vector3 position, Vector3 size,
            int heightmapRes = 129, string name = "Terrain")
        {
            var terrainData = new TerrainData();
            terrainData.heightmapResolution = heightmapRes;
            terrainData.size = size;
            terrainData.alphamapResolution = heightmapRes - 1;
            terrainData.baseMapResolution = 512;

            // Load and assign terrain layers
            var layers = LoadTerrainLayers();
            if (layers.Count > 0)
                terrainData.terrainLayers = layers.ToArray();

            var terrainObj = Terrain.CreateTerrainGameObject(terrainData);
            terrainObj.name = name;
            terrainObj.transform.SetParent(parent, false);
            terrainObj.transform.position = position;

            var terrain = terrainObj.GetComponent<Terrain>();
            terrain.drawInstanced = true;

            // URP requires an explicit terrain material — create one from the URP terrain shader
            var terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (terrainShader != null)
            {
                terrain.materialTemplate = new Material(terrainShader);
                terrain.materialTemplate.name = "URP_TerrainMat";
            }
            else
            {
                Debug.LogWarning("[TerrainFactory] URP Terrain/Lit shader not found. Terrain may render incorrectly.");
            }

            // Try to add MicroSplat if available
            TryAddMicroSplat(terrainObj, terrain);

            return terrain;
        }

        /// <summary>
        /// Set heights using a function that maps (normalizedX, normalizedZ) -> height [0..1].
        /// </summary>
        public static void SetHeights(Terrain terrain, System.Func<float, float, float> heightFunc)
        {
            var data = terrain.terrainData;
            int res = data.heightmapResolution;
            float[,] heights = new float[res, res];

            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / (res - 1);
                float nz = (float)z / (res - 1);
                heights[z, x] = heightFunc(nx, nz);
            }

            data.SetHeights(0, 0, heights);
        }

        /// <summary>
        /// Paint the splatmap using a function that returns layer weights for each point.
        /// The function receives (normalizedX, normalizedZ, height, steepness) and returns
        /// a float[] of weights (one per layer). Weights are auto-normalized.
        /// </summary>
        public static void PaintSplatmap(Terrain terrain,
            System.Func<float, float, float, float, float[]> weightFunc)
        {
            var data = terrain.terrainData;
            int res = data.alphamapResolution;
            int layerCount = data.terrainLayers.Length;
            if (layerCount == 0) return;

            float[,,] alphamap = new float[res, res, layerCount];

            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / (res - 1);
                float nz = (float)z / (res - 1);
                float h = data.GetInterpolatedHeight(nx, nz) / data.size.y;
                float steepness = data.GetSteepness(nx, nz);

                float[] weights = weightFunc(nx, nz, h, steepness);
                if (weights == null || weights.Length == 0) continue;

                // Normalize
                float sum = 0f;
                for (int i = 0; i < weights.Length; i++) sum += weights[i];
                if (sum < 0.001f) { weights[0] = 1f; sum = 1f; }

                for (int i = 0; i < layerCount && i < weights.Length; i++)
                    alphamap[z, x, i] = weights[i] / sum;
            }

            data.SetAlphamaps(0, 0, alphamap);
        }

        /// <summary>
        /// Add a hill/mound at a normalized (cx, cz) position with given radius and height.
        /// </summary>
        public static void AddHill(Terrain terrain, float cx, float cz,
            float radiusNorm, float heightNorm)
        {
            var data = terrain.terrainData;
            int res = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, res, res);

            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / (res - 1);
                float nz = (float)z / (res - 1);
                float dx = nx - cx;
                float dz = nz - cz;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < radiusNorm)
                {
                    float t = 1f - (dist / radiusNorm);
                    float add = heightNorm * t * t; // quadratic falloff
                    heights[z, x] = Mathf.Max(heights[z, x], heights[z, x] + add);
                }
            }

            data.SetHeights(0, 0, heights);
        }

        /// <summary>
        /// Flatten a rectangular area for spawn points or roads.
        /// </summary>
        public static void Flatten(Terrain terrain, float x1, float z1, float x2, float z2, float height)
        {
            var data = terrain.terrainData;
            int res = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, res, res);

            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / (res - 1);
                float nz = (float)z / (res - 1);
                if (nx >= x1 && nx <= x2 && nz >= z1 && nz <= z2)
                    heights[z, x] = height;
            }

            data.SetHeights(0, 0, heights);
        }

        // =====================================================================
        // Internal helpers
        // =====================================================================

        private static List<TerrainLayer> LoadTerrainLayers()
        {
            var layers = new List<TerrainLayer>();

            for (int i = 0; i < TextureNames.Length; i++)
            {
                string name = TextureNames[i];
                var diffuse = Resources.Load<Texture2D>($"Terrain/Textures/{name}_BaseColor");
                var normal  = Resources.Load<Texture2D>($"Terrain/Textures/{name}_Normal");

                if (diffuse == null)
                {
                    Debug.LogWarning($"[TerrainFactory] Texture not found: Terrain/Textures/{name}_BaseColor");
                    // Create a fallback colored layer so indices stay correct
                    var fallback = new TerrainLayer();
                    fallback.tileSize = new Vector2(10f, 10f);
                    layers.Add(fallback);
                    continue;
                }

                var layer = new TerrainLayer();
                layer.diffuseTexture = diffuse;
                if (normal != null)
                    layer.normalMapTexture = normal;
                layer.tileSize = new Vector2(10f, 10f);
                layer.tileOffset = Vector2.zero;
                layers.Add(layer);
            }

            Debug.Log($"[TerrainFactory] Created {layers.Count} terrain layers from textures.");
            return layers;
        }

        /// <summary>
        /// Load a pre-built island terrain from the Free Island Collection.
        /// Returns the Terrain component, positioned at the given world position.
        /// Island terrains are 1-5. Scale adjusts the terrain size.
        /// </summary>
        public static Terrain LoadIsland(Transform parent, int islandIndex, Vector3 position,
            float scale = 1f, string name = "Island")
        {
            string path = $"Terrain/Islands/Terrain {Mathf.Clamp(islandIndex, 1, 5)}";
            var terrainData = Resources.Load<TerrainData>(path);

            if (terrainData == null)
            {
                Debug.LogWarning($"[TerrainFactory] Island terrain not found: {path}");
                return null;
            }

            // Clone the terrain data so each island instance is independent
            var clonedData = Object.Instantiate(terrainData);
            if (scale != 1f)
                clonedData.size = new Vector3(
                    clonedData.size.x * scale,
                    clonedData.size.y * scale,
                    clonedData.size.z * scale);

            var terrainObj = Terrain.CreateTerrainGameObject(clonedData);
            terrainObj.name = name;
            terrainObj.transform.SetParent(parent, false);
            terrainObj.transform.position = position;

            var terrain = terrainObj.GetComponent<Terrain>();
            terrain.drawInstanced = true;

            // URP terrain material
            var terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (terrainShader != null)
            {
                terrain.materialTemplate = new Material(terrainShader);
                terrain.materialTemplate.name = "URP_IslandMat";
            }

            return terrain;
        }

        /// <summary>
        /// Procedurally generate an island using the IslandGenerator (Cellular Automata).
        /// Creates a terrain, runs generation, smoothing, perlin noise, and shore calc synchronously.
        /// </summary>
        public static Terrain GenerateIsland(Transform parent, Vector3 position, Vector3 size,
            int heightmapRes = 257, string seed = null, int maxShoreRadius = 50,
            float perlinHeight = 0.02f, float perlinScale = 20f, string name = "ProceduralIsland")
        {
            // Create terrain
            var terrain = Create(parent, position, size, heightmapRes, name);
            if (terrain == null) return null;

            // Add IslandGenerator and configure
            var generator = terrain.gameObject.AddComponent<IslandGenerator>();
            generator.terrain = terrain;
            generator.maxRadius = Mathf.Clamp(maxShoreRadius, 20, 100);
            generator.perlinHeight = perlinHeight;
            generator.perlinScale = perlinScale;
            generator.randomFillPercent = 50;

            if (!string.IsNullOrEmpty(seed))
            {
                generator.useRandomSeed = false;
                generator.seed = seed;
            }
            else
            {
                generator.useRandomSeed = true;
            }

            // Run generation synchronously (no threading for runtime)
            generator.StartGeneration();

            // Calculate shores synchronously (normally threaded in editor)
            generator.terrain = terrain;

            int reso = terrain.terrainData.heightmapResolution;
            int scaleH = reso / 64;
            int scaleW = reso / 64;
            int scaledH = (reso / scaleH) * scaleH;
            int scaledW = (reso / scaleW) * scaleW;

            // Shore calc would normally be threaded — just run CalculateShores
            // and wait briefly, or call SmoothShores directly
            generator.CalculateShores();

            // Wait for the shore thread to finish (it processes pixels)
            if (generator.th1 != null)
                generator.th1.Join(5000); // max 5 seconds

            generator.SmoothShores();

            // Add perlin noise detail
            generator.PerlinNoise();

            // Smooth the heightmap
            for (int i = 0; i < 3; i++)
                generator.BlendHeights();

            // Lower sea floor to 0
            generator.ResetSeaFloor();

            Debug.Log($"[TerrainFactory] Generated procedural island: {name}");
            return terrain;
        }

        private static void TryAddMicroSplat(GameObject terrainObj, Terrain terrain)
        {
            // Try to find the MicroSplatTerrain type via reflection
            var msType = System.Type.GetType("JBooth.MicroSplat.MicroSplatTerrain, Assembly-CSharp");
            if (msType == null)
                msType = System.Type.GetType("JBooth.MicroSplat.MicroSplatTerrain, com.jbooth.microsplat.core");
            if (msType != null)
            {
                terrainObj.AddComponent(msType);
                Debug.Log("[TerrainFactory] MicroSplat component added to terrain.");
            }
        }
    }
}
