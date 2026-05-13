using UnityEngine;
using CloseEncounters.Core;

namespace CloseEncounters.Vehicle
{
    /// <summary>
    /// Manages texture loading and material caching for vehicle part visuals.
    /// Loads wood plank textures for wooden structural parts (hulls, decks, masts)
    /// and metallic textures for metal structural parts (frames, trusses, armor).
    ///
    /// Texture search order:
    /// 1. Substance Parquet01 generated outputs (from Assets/Substances/Parquet01)
    /// 2. Resources/Textures/ folder (manually placed textures)
    /// 3. Procedural fallback textures (always available)
    /// </summary>
    public static class PartTextureManager
    {
        // Cached materials (shared across all parts of the same type)
        private static Material _woodPlankMaterial;
        private static Material _metalMaterial;
        private static Material _mastWoodMaterial;
        private static Material _deckPlankMaterial;

        // Cached textures
        private static Texture2D _woodPlankTexture;
        private static Texture2D _woodNormalTexture;
        private static Texture2D _metalTexture;
        private static Texture2D _metalNormalTexture;

        private static bool _initialized;

        // URP Lit shader reference
        private static Shader _urpLitShader;

        // -------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns the appropriate textured material for a part based on its
        /// id, subcategory, category, and domain context.
        /// Returns null if the part shouldn't get a texture override (e.g. weapons,
        /// wheels, sails, etc. which have their own specialized visuals).
        /// </summary>
        public static Material GetMaterialForPart(PartData partData)
        {
            if (partData == null) return null;

            EnsureInitialized();

            string id = partData.id?.ToLowerInvariant() ?? "";
            string sub = partData.subcategory?.ToLowerInvariant() ?? "";
            string cat = partData.category?.ToLowerInvariant() ?? "";

            // Only texture structural and defense parts
            if (cat != "structural" && cat != "defense")
                return null;

            // --- WOOD textures: hulls, decks, masts, planking, keels ---
            if (IsWoodenPart(id, sub, partData))
            {
                // Masts get a slightly different tinted wood material
                if (sub == "mast")
                    return GetMastWoodMaterial();

                // Decks get the deck plank material (lighter)
                if (id.Contains("deck") || id.Contains("plating"))
                    return GetDeckPlankMaterial();

                // Hulls, keels, bows, superstructures, ship parts
                return GetWoodPlankMaterial();
            }

            // --- METAL textures: frames, trusses, armor, fairings ---
            if (IsMetalPart(id, sub, cat))
            {
                return GetMetalMaterial();
            }

            return null;
        }

        /// <summary>
        /// Apply the textured material to a GameObject's MeshRenderer,
        /// preserving the tint color. Returns the MeshRenderer for convenience.
        /// </summary>
        public static MeshRenderer ApplyTexturedMaterial(GameObject obj, Material texMat, Color tintColor)
        {
            if (obj == null || texMat == null) return null;

            var renderer = obj.GetComponent<MeshRenderer>();
            if (renderer == null) return null;

            // Create an instance of the shared material so we can tint per-part
            var mat = new Material(texMat);
            mat.color = tintColor;
            renderer.material = mat;

            return renderer;
        }

        /// <summary>
        /// Clean up cached materials. Call on scene unload if needed.
        /// </summary>
        public static void ClearCache()
        {
            _woodPlankMaterial = null;
            _metalMaterial = null;
            _mastWoodMaterial = null;
            _deckPlankMaterial = null;
            _woodPlankTexture = null;
            _woodNormalTexture = null;
            _metalTexture = null;
            _metalNormalTexture = null;
            _initialized = false;
        }

        // -------------------------------------------------------------------
        // Part classification
        // -------------------------------------------------------------------

        /// <summary>
        /// Determines if a part should receive a wood plank texture.
        /// Wooden parts: ship hulls, decks, keels, masts, crow's nests,
        /// bows, superstructures, and any water-domain hull parts.
        /// </summary>
        private static bool IsWoodenPart(string id, string sub, PartData partData)
        {
            // Masts are always wood
            if (sub == "mast") return true;

            // Sails are NOT wood (they have their own cloth visual)
            if (sub == "sail") return false;

            // Check for water-domain hull parts (ship hulls, decks, keels)
            bool isWaterDomain = false;
            if (partData.domains != null)
            {
                for (int i = 0; i < partData.domains.Length; i++)
                {
                    string d = partData.domains[i]?.ToLowerInvariant() ?? "";
                    if (d == "water" || d == "sea")
                    {
                        isWaterDomain = true;
                        break;
                    }
                }
            }

            // Water-domain hull-type parts are wooden
            if (isWaterDomain && (sub == "hull" || id.Contains("hull") || id.Contains("deck")
                || id.Contains("keel") || id.Contains("plank") || id.Contains("bow")
                || id.Contains("superstructure") || id.Contains("plating")))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines if a part should receive a metallic texture.
        /// Metallic parts: frames, trusses, armor plates, fairings,
        /// and non-water hull parts.
        /// </summary>
        private static bool IsMetalPart(string id, string sub, string cat)
        {
            // Defense category (armor) is always metal
            if (cat == "defense") return true;

            // Frames and trusses
            if (sub == "frame") return true;
            if (id.Contains("truss")) return true;
            if (id.Contains("frame")) return true;

            // Fairings (aircraft skin panels)
            if (id.Contains("fairing")) return true;

            // Radar masts and smokestacks are metal
            if (id.Contains("radar") || id.Contains("smokestack")) return true;

            // Bow ram has metal reinforcement
            if (id.Contains("bow_ram")) return true;

            return false;
        }

        // -------------------------------------------------------------------
        // Material creation
        // -------------------------------------------------------------------

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _urpLitShader = Shader.Find("Universal Render Pipeline/Lit");

            // Load textures
            LoadWoodTextures();
            LoadMetalTextures();
        }

        private static Material GetWoodPlankMaterial()
        {
            if (_woodPlankMaterial != null) return _woodPlankMaterial;

            _woodPlankMaterial = CreateTexturedMaterial(
                _woodPlankTexture, _woodNormalTexture,
                new Color(0.45f, 0.30f, 0.12f), // warm brown wood tint
                0f,   // smoothness (rough wood)
                0f    // metallic (non-metal)
            );
            _woodPlankMaterial.name = "WoodPlank_Mat";

            // Set texture tiling for plank-like appearance
            if (_woodPlankTexture != null)
            {
                _woodPlankMaterial.mainTextureScale = new Vector2(2f, 2f);
            }

            return _woodPlankMaterial;
        }

        private static Material GetDeckPlankMaterial()
        {
            if (_deckPlankMaterial != null) return _deckPlankMaterial;

            _deckPlankMaterial = CreateTexturedMaterial(
                _woodPlankTexture, _woodNormalTexture,
                new Color(0.55f, 0.42f, 0.22f), // lighter deck wood
                0.1f,  // slightly smoother (polished deck)
                0f
            );
            _deckPlankMaterial.name = "DeckPlank_Mat";

            if (_woodPlankTexture != null)
            {
                _deckPlankMaterial.mainTextureScale = new Vector2(3f, 3f);
            }

            return _deckPlankMaterial;
        }

        private static Material GetMastWoodMaterial()
        {
            if (_mastWoodMaterial != null) return _mastWoodMaterial;

            _mastWoodMaterial = CreateTexturedMaterial(
                _woodPlankTexture, _woodNormalTexture,
                new Color(0.55f, 0.41f, 0.08f), // darker mast brown
                0.05f,
                0f
            );
            _mastWoodMaterial.name = "MastWood_Mat";

            // Tighter tiling for mast cylinder
            if (_woodPlankTexture != null)
            {
                _mastWoodMaterial.mainTextureScale = new Vector2(1f, 4f);
            }

            return _mastWoodMaterial;
        }

        private static Material GetMetalMaterial()
        {
            if (_metalMaterial != null) return _metalMaterial;

            _metalMaterial = CreateTexturedMaterial(
                _metalTexture, _metalNormalTexture,
                new Color(0.60f, 0.60f, 0.63f), // steel grey
                0.4f,  // moderately smooth
                0.7f   // metallic
            );
            _metalMaterial.name = "Metal_Mat";

            if (_metalTexture != null)
            {
                _metalMaterial.mainTextureScale = new Vector2(2f, 2f);
            }

            return _metalMaterial;
        }

        /// <summary>
        /// Creates a URP Lit material with the given texture, normal map,
        /// color tint, smoothness, and metallic value.
        /// </summary>
        private static Material CreateTexturedMaterial(
            Texture2D albedo, Texture2D normal,
            Color color, float smoothness, float metallic)
        {
            Shader shader = _urpLitShader ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                // Absolute fallback: standard shader
                shader = Shader.Find("Standard");
            }

            var mat = new Material(shader);
            mat.color = color;

            // URP Lit property names
            if (albedo != null)
            {
                mat.SetTexture("_BaseMap", albedo);
                mat.mainTexture = albedo;
            }

            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }

            // Smoothness and metallic
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);

            return mat;
        }

        // -------------------------------------------------------------------
        // Texture loading
        // -------------------------------------------------------------------

        /// <summary>
        /// Attempts to load wood plank textures from multiple sources.
        /// </summary>
        private static void LoadWoodTextures()
        {
            // 1. Try Substance Parquet01 output (Unity Substance plugin
            //    generates textures alongside the .sbsar at import time)
            _woodPlankTexture = TryLoadTexture(
                "Textures/Parquet01_basecolor",
                "Textures/Parquet01_diffuse",
                "Textures/Parquet01_albedo",
                "Textures/WoodPlank"
            );

            _woodNormalTexture = TryLoadTexture(
                "Textures/Parquet01_normal",
                "Textures/WoodPlank_Normal"
            );

            // 2. If no pre-made textures, generate procedural wood plank texture
            if (_woodPlankTexture == null)
            {
                _woodPlankTexture = GenerateWoodPlankTexture(256, 256);
                Debug.Log("[PartTextureManager] Using procedural wood plank texture.");
            }

            if (_woodNormalTexture == null)
            {
                _woodNormalTexture = GenerateFlatNormal(64, 64);
            }
        }

        /// <summary>
        /// Attempts to load metallic textures from multiple sources.
        /// </summary>
        private static void LoadMetalTextures()
        {
            _metalTexture = TryLoadTexture(
                "Textures/Parquet01_metallic",
                "Textures/MetalPlate",
                "Textures/Metal"
            );

            _metalNormalTexture = TryLoadTexture(
                "Textures/MetalPlate_Normal",
                "Textures/Metal_Normal"
            );

            // Generate procedural metal texture if none found
            if (_metalTexture == null)
            {
                _metalTexture = GenerateMetalTexture(256, 256);
                Debug.Log("[PartTextureManager] Using procedural metal plate texture.");
            }

            if (_metalNormalTexture == null)
            {
                _metalNormalTexture = GenerateMetalNormalTexture(256, 256);
            }
        }

        /// <summary>
        /// Try loading a texture from Resources by checking multiple possible paths.
        /// Returns the first successfully loaded texture, or null.
        /// </summary>
        private static Texture2D TryLoadTexture(params string[] resourcePaths)
        {
            for (int i = 0; i < resourcePaths.Length; i++)
            {
                var tex = Resources.Load<Texture2D>(resourcePaths[i]);
                if (tex != null)
                {
                    Debug.Log($"[PartTextureManager] Loaded texture: Resources/{resourcePaths[i]}");
                    return tex;
                }
            }
            return null;
        }

        // -------------------------------------------------------------------
        // Procedural texture generation
        // -------------------------------------------------------------------

        /// <summary>
        /// Generates a procedural wood plank texture with visible plank lines,
        /// grain variation, and warm wood tones.
        /// </summary>
        private static Texture2D GenerateWoodPlankTexture(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true);
            tex.name = "ProceduralWoodPlank";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color[width * height];

            // Base wood colors
            Color woodLight = new Color(0.72f, 0.56f, 0.34f);
            Color woodDark = new Color(0.42f, 0.28f, 0.12f);
            Color woodMid = new Color(0.58f, 0.42f, 0.22f);

            // Number of planks across the texture width
            int plankCount = 6;
            float plankWidth = (float)width / plankCount;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;

                    // Which plank are we on?
                    int plankIndex = Mathf.FloorToInt(x / plankWidth);
                    float withinPlank = (x % plankWidth) / plankWidth;

                    // Plank gap (dark line between planks)
                    float gapWidth = 0.02f;
                    if (withinPlank < gapWidth || withinPlank > (1f - gapWidth))
                    {
                        pixels[idx] = woodDark * 0.4f;
                        continue;
                    }

                    // Base color with slight per-plank variation
                    float plankVariation = PseudoNoise(plankIndex * 17, 0) * 0.15f;
                    Color baseColor = Color.Lerp(woodMid, woodLight, 0.5f + plankVariation);

                    // Wood grain: wavy horizontal lines
                    float grainFreq = 40f;
                    float grainPhase = PseudoNoise(plankIndex * 7, 0) * 100f;
                    float grainWave = Mathf.Sin((y + grainPhase) * Mathf.PI / grainFreq
                        + Mathf.Sin(x * 0.02f) * 2f) * 0.5f + 0.5f;

                    // Finer secondary grain
                    float fineGrain = Mathf.Sin((y + grainPhase * 0.7f) * Mathf.PI / 8f
                        + Mathf.Cos(x * 0.05f) * 1.5f) * 0.5f + 0.5f;

                    // Combine grains
                    float grain = grainWave * 0.7f + fineGrain * 0.3f;
                    Color grainColor = Color.Lerp(woodDark, baseColor, grain);

                    // Add subtle noise for roughness
                    float noise = PseudoNoise(x * 3 + y * width, plankIndex) * 0.08f - 0.04f;
                    grainColor.r += noise;
                    grainColor.g += noise * 0.8f;
                    grainColor.b += noise * 0.5f;

                    // Knot holes (occasional dark spots)
                    float knotChance = PseudoNoise(plankIndex * 31 + 7, y / 60);
                    if (knotChance > 0.92f)
                    {
                        int knotCenterX = (int)(plankIndex * plankWidth + plankWidth * 0.5f);
                        int knotCenterY = (y / 60) * 60 + 30;
                        float distToKnot = Mathf.Sqrt(
                            (x - knotCenterX) * (x - knotCenterX) +
                            (y - knotCenterY) * (y - knotCenterY));
                        if (distToKnot < 8f)
                        {
                            float knotBlend = 1f - distToKnot / 8f;
                            grainColor = Color.Lerp(grainColor, woodDark * 0.5f, knotBlend * 0.6f);
                        }
                    }

                    grainColor.a = 1f;
                    pixels[idx] = grainColor;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// Generates a procedural metal plate texture with subtle brushed
        /// steel appearance and panel line details.
        /// </summary>
        private static Texture2D GenerateMetalTexture(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true);
            tex.name = "ProceduralMetal";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color[width * height];

            Color metalDark = new Color(0.40f, 0.40f, 0.43f);
            Color metalBright = new Color(0.78f, 0.78f, 0.80f);

            // Metal panels: 4x4 grid
            int panelCountX = 4;
            int panelCountY = 4;
            float panelW = (float)width / panelCountX;
            float panelH = (float)height / panelCountY;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;

                    int panelX = Mathf.FloorToInt(x / panelW);
                    int panelY = Mathf.FloorToInt(y / panelH);
                    float withinX = (x % panelW) / panelW;
                    float withinY = (y % panelH) / panelH;

                    // Panel seam lines
                    float seamWidth = 0.015f;
                    if (withinX < seamWidth || withinX > (1f - seamWidth)
                        || withinY < seamWidth || withinY > (1f - seamWidth))
                    {
                        pixels[idx] = metalDark * 0.6f;
                        pixels[idx].a = 1f;
                        continue;
                    }

                    // Rivet dots near seams
                    float rivetDist = 0.06f;
                    float rivetRadius = 0.015f;
                    bool nearRivet = false;
                    // Corner rivets
                    float[][] rivetPositions = new float[][] {
                        new float[] { rivetDist, rivetDist },
                        new float[] { 1f - rivetDist, rivetDist },
                        new float[] { rivetDist, 1f - rivetDist },
                        new float[] { 1f - rivetDist, 1f - rivetDist }
                    };
                    for (int r = 0; r < rivetPositions.Length; r++)
                    {
                        float dx = withinX - rivetPositions[r][0];
                        float dy = withinY - rivetPositions[r][1];
                        if (dx * dx + dy * dy < rivetRadius * rivetRadius)
                        {
                            nearRivet = true;
                            break;
                        }
                    }

                    if (nearRivet)
                    {
                        pixels[idx] = metalBright * 0.85f;
                        pixels[idx].a = 1f;
                        continue;
                    }

                    // Per-panel tint variation
                    float panelVar = PseudoNoise(panelX * 13 + panelY * 7, 0) * 0.06f - 0.03f;

                    // Brushed metal: horizontal streaks
                    float brushFreq = 80f;
                    float brush = Mathf.Sin(y * Mathf.PI / brushFreq * 6f
                        + PseudoNoise(x, panelX) * 3f) * 0.5f + 0.5f;
                    float brushFine = Mathf.Sin(y * Mathf.PI / 3f
                        + PseudoNoise(x * 7, y) * 2f) * 0.5f + 0.5f;
                    float brushed = brush * 0.6f + brushFine * 0.4f;

                    Color base_c = Color.Lerp(metalDark, metalBright, brushed * 0.5f + 0.25f);
                    base_c.r += panelVar;
                    base_c.g += panelVar;
                    base_c.b += panelVar;

                    // Subtle noise
                    float noise = PseudoNoise(x * 5 + y * width + 123, panelX + panelY * 10) * 0.04f - 0.02f;
                    base_c.r += noise;
                    base_c.g += noise;
                    base_c.b += noise;

                    base_c.a = 1f;
                    pixels[idx] = base_c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// Generates a simple normal map for metal panels with beveled edges.
        /// </summary>
        private static Texture2D GenerateMetalNormalTexture(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true);
            tex.name = "ProceduralMetalNormal";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color[width * height];

            int panelCountX = 4;
            int panelCountY = 4;
            float panelW = (float)width / panelCountX;
            float panelH = (float)height / panelCountY;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    float withinX = (x % panelW) / panelW;
                    float withinY = (y % panelH) / panelH;

                    // Bevel at panel edges
                    float bevelWidth = 0.04f;
                    float nx = 0.5f;
                    float ny = 0.5f;

                    if (withinX < bevelWidth)
                        nx = 0.5f - (1f - withinX / bevelWidth) * 0.3f;
                    else if (withinX > 1f - bevelWidth)
                        nx = 0.5f + (1f - (1f - withinX) / bevelWidth) * 0.3f;

                    if (withinY < bevelWidth)
                        ny = 0.5f - (1f - withinY / bevelWidth) * 0.3f;
                    else if (withinY > 1f - bevelWidth)
                        ny = 0.5f + (1f - (1f - withinY) / bevelWidth) * 0.3f;

                    pixels[idx] = new Color(nx, ny, 1f, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// Generates a flat (neutral) normal map.
        /// </summary>
        private static Texture2D GenerateFlatNormal(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.name = "FlatNormal";
            tex.wrapMode = TextureWrapMode.Repeat;

            Color flatNormal = new Color(0.5f, 0.5f, 1f, 1f);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = flatNormal;

            tex.SetPixels(pixels);
            tex.Apply(false);
            return tex;
        }

        /// <summary>
        /// Simple deterministic pseudo-noise for texture generation.
        /// Returns a value in [0, 1].
        /// </summary>
        private static float PseudoNoise(int x, int y)
        {
            int n = x + y * 57;
            n = (n << 13) ^ n;
            float result = (1f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff)
                / 1073741824f);
            return result * 0.5f + 0.5f;
        }
    }
}
