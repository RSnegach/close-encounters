using UnityEngine;

namespace CloseEncounters.VehiclePhysics
{
    /// <summary>
    /// Animates UV scrolling on a water material using the "FX/Water (Basic)" shader.
    /// Ported from Unity Standard Assets WaterBasic.cs.
    ///
    /// At startup, if no normal map texture is assigned, generates a procedural
    /// normal map so the shader works without importing WaterBasicNormals.jpg.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class WaterBasicAnimator : MonoBehaviour
    {
        [Header("UV Scroll Speed")]
        [Tooltip("Scroll speed for normal map layer 1 (XY) and layer 2 (ZW).")]
        public Vector4 waveSpeed = new Vector4(5f, 5f, -4f, 0f);

        [Header("Wave Scale")]
        [Tooltip("Scale of the normal map tiling in world space.")]
        public float waveScale = 0.07f;

        [Header("Procedural Normal Map")]
        [Tooltip("Resolution of the generated normal map if none is assigned.")]
        public int proceduralTexSize = 256;

        [Header("Colors (applied once at init)")]
        public Color horizonColor = new Color(0.172f, 0.463f, 0.435f, 1f);
        public Color waterColor   = new Color(0.172f, 0.463f, 0.435f, 1f);

        private Material _mat;

        // Cache shader property IDs for performance
        private static readonly int PropWaveSpeed  = Shader.PropertyToID("_WaveSpeed");
        private static readonly int PropWaveScale  = Shader.PropertyToID("_WaveScale");
        private static readonly int PropWaveNormal = Shader.PropertyToID("_WaveNormal");
        private static readonly int PropHorizon    = Shader.PropertyToID("_horizonColor");
        private static readonly int PropWaterColor = Shader.PropertyToID("_waterColor");

        private void Start()
        {
            var rend = GetComponent<MeshRenderer>();
            if (rend == null) return;

            // Work on an instance so we don't modify the shared material
            _mat = rend.material;

            // Apply initial properties
            _mat.SetFloat(PropWaveScale, waveScale);
            _mat.SetVector(PropWaveSpeed, waveSpeed);
            _mat.SetColor(PropHorizon, horizonColor);
            _mat.SetColor(PropWaterColor, waterColor);

            // Generate a procedural normal map if none is assigned
            if (_mat.GetTexture(PropWaveNormal) == null)
            {
                _mat.SetTexture(PropWaveNormal, GenerateProceduralNormalMap(proceduralTexSize));
            }
        }

        /// <summary>
        /// The original WaterBasic.cs simply set _WaveSpeed each frame from a public
        /// field. We do the same here so designers can tweak speed at runtime.
        /// </summary>
        private void Update()
        {
            if (_mat == null) return;
            _mat.SetVector(PropWaveSpeed, waveSpeed);
        }

        // =====================================================================
        //  Procedural Normal Map Generation
        //
        //  Produces a tileable normal map similar to WaterBasicNormals.jpg.
        //  Uses multiple octaves of Perlin-like noise via Mathf.PerlinNoise,
        //  then converts height-field to normals via Sobel-style finite diffs.
        // =====================================================================

        private static Texture2D GenerateProceduralNormalMap(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.name = "ProceduralWaterNormals";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;

            // Step 1: Generate a tileable heightfield from layered Perlin noise
            float[] heights = new float[size * size];
            float maxH = float.MinValue;
            float minH = float.MaxValue;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float h = 0f;
                    float amp = 1f;
                    float freq = 1f;

                    // 4 octaves of noise
                    for (int oct = 0; oct < 4; oct++)
                    {
                        float nx = (float)x / size * freq * 4f;
                        float ny = (float)y / size * freq * 4f;
                        h += Mathf.PerlinNoise(nx + 0.1f, ny + 0.1f) * amp;
                        amp *= 0.5f;
                        freq *= 2f;
                    }

                    heights[y * size + x] = h;
                    if (h > maxH) maxH = h;
                    if (h < minH) minH = h;
                }
            }

            // Normalize heights to 0..1
            float range = maxH - minH;
            if (range < 0.0001f) range = 1f;
            for (int i = 0; i < heights.Length; i++)
                heights[i] = (heights[i] - minH) / range;

            // Step 2: Convert heightfield to normal map via finite differences
            var pixels = new Color[size * size];
            float strength = 2.0f; // bump strength

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Sample neighbors with wrapping
                    float hL = heights[y * size + ((x - 1 + size) % size)];
                    float hR = heights[y * size + ((x + 1) % size)];
                    float hD = heights[((y - 1 + size) % size) * size + x];
                    float hU = heights[((y + 1) % size) * size + x];

                    // Tangent-space normal via central differences
                    float dx = (hL - hR) * strength;
                    float dy = (hD - hU) * strength;
                    Vector3 n = new Vector3(dx, dy, 1f).normalized;

                    // Encode to 0..1 range (standard Unity normal map encoding)
                    pixels[y * size + x] = new Color(
                        n.x * 0.5f + 0.5f,
                        n.y * 0.5f + 0.5f,
                        n.z * 0.5f + 0.5f,
                        n.y * 0.5f + 0.5f  // alpha = G for DXT5nm compatibility
                    );
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true); // generate mipmaps
            return tex;
        }
    }
}
