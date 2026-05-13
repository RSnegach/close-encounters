using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.VehiclePhysics;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Base class for procedural arena generation. Provides helper methods for
    /// building geometry, materials, hazard zones, and spawn points. Derived
    /// classes override Build() to compose a specific arena layout.
    /// </summary>
    public abstract class ArenaBase : MonoBehaviour
    {
        // --- Public ---
        public abstract string ArenaName { get; }
        public List<Transform> SpawnPoints { get; private set; } = new List<Transform>();

        // --- Constants ---
        protected const float CellSize = 1f;
        private const float MinBlockVolume = 40f;

        // --- Material cache ---
        private readonly Dictionary<string, Material> _materialCache = new Dictionary<string, Material>();

        // =====================================================================
        // Abstract
        // =====================================================================

        /// <summary>
        /// Derived classes populate the arena by calling Add* helpers inside this method.
        /// </summary>
        public abstract void Build();

        /// <summary>
        /// Arenas override these to declare their skybox / ambient / post-fx preferences.
        /// Default is empty — ArenaBase helpers below are optional.
        /// </summary>
        public virtual string SkyboxResourceName => null;
        public virtual string AmbientAudioResourcePath => null;
        public virtual PostFXBootstrap.Settings PostFX => PostFXBootstrap.Settings.Default;

        /// <summary>
        /// Apply skybox + post-fx + ambient audio. Call this at the top of Build() in derived arenas.
        /// </summary>
        protected void ApplyAtmospherics()
        {
            if (!string.IsNullOrEmpty(SkyboxResourceName))
                ArenaSkyboxes.Apply(SkyboxResourceName);
            PostFXBootstrap.Apply(PostFX);
            if (!string.IsNullOrEmpty(AmbientAudioResourcePath))
                CloseEncounters.Combat.AudioFX.CreateAmbientLoop(transform, AmbientAudioResourcePath, 0.35f);
        }

        /// <summary>Glowing molten-rock particle stream trailing from localStart → localEnd.</summary>
        protected void SpawnLavaStream(Transform parent, Vector3 localStart, Vector3 localEnd)
        {
            var go = new GameObject("LavaStream");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localStart;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 4f; main.loop = true;
            main.startLifetime = 2.5f;
            main.startSpeed = 4f;
            main.startSize = 0.8f;
            main.startColor = new Color(1f, 0.55f, 0.1f, 1f);
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 25f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 4f;
            Vector3 flow = (localEnd - localStart).normalized;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.forward, flow);
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] {
                new GradientColorKey(new Color(1.5f, 0.7f, 0.15f), 0f),
                new GradientColorKey(new Color(0.8f, 0.2f, 0.05f), 1f)
            }, new[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.color = new Color(1f, 0.6f, 0.1f, 1f);
            rend.sharedMaterial = mat;
            ps.Play();
        }

        // =====================================================================
        // Material Helpers
        // =====================================================================

        protected Material MakeMaterial(Color color, string name = null)
        {
            string key = name ?? ColorUtility.ToHtmlStringRGBA(color);
            if (_materialCache.TryGetValue(key, out Material cached))
                return cached;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            if (name != null) mat.name = name;
            _materialCache[key] = mat;
            return mat;
        }

        protected Material MakeTransparentMaterial(Color color, string name = null)
        {
            string key = "t_" + (name ?? ColorUtility.ToHtmlStringRGBA(color));
            if (_materialCache.TryGetValue(key, out Material cached))
                return cached;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            mat.color = color;
            if (name != null) mat.name = name;
            _materialCache[key] = mat;
            return mat;
        }

        protected Material MakeEmissiveMaterial(Color color, Color emission, string name = null)
        {
            string key = "e_" + (name ?? ColorUtility.ToHtmlStringRGBA(color));
            if (_materialCache.TryGetValue(key, out Material cached))
                return cached;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            if (name != null) mat.name = name;
            _materialCache[key] = mat;
            return mat;
        }

        // =====================================================================
        // Primitive Geometry Helpers
        // =====================================================================

        /// <summary>
        /// Add a box primitive. Skips if volume is less than MinBlockVolume.
        /// Returns null if skipped.
        /// </summary>
        protected GameObject AddBlock(Vector3 position, Vector3 scale, Color color, string label = "Block")
        {
            float volume = scale.x * scale.y * scale.z;
            if (volume < MinBlockVolume) return null;

            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = label;
            obj.transform.SetParent(transform, false);
            obj.transform.position = position;
            obj.transform.localScale = scale;
            SetMaterial(obj, MakeMaterial(color));
            obj.isStatic = true;
            return obj;
        }

        /// <summary>
        /// Add a box regardless of volume (no skip check).
        /// </summary>
        protected GameObject AddBlockUnchecked(Vector3 position, Vector3 scale, Color color, string label = "Block")
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = label;
            obj.transform.SetParent(transform, false);
            obj.transform.position = position;
            obj.transform.localScale = scale;
            SetMaterial(obj, MakeMaterial(color));
            obj.isStatic = true;
            return obj;
        }

        protected GameObject AddCylinder(Vector3 position, float radius, float height, Color color, string label = "Cylinder")
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            obj.name = label;
            obj.transform.SetParent(transform, false);
            obj.transform.position = position;
            obj.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            SetMaterial(obj, MakeMaterial(color));
            SwapCapsuleForMeshCollider(obj);
            obj.isStatic = true;
            return obj;
        }

        protected GameObject AddCone(Vector3 position, float radius, float height, Color color, string label = "Cone")
        {
            // Approximate cone as a scaled cylinder with top squished
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            obj.name = label;
            obj.transform.SetParent(transform, false);
            obj.transform.position = position;
            obj.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            SetMaterial(obj, MakeMaterial(color));
            SwapCapsuleForMeshCollider(obj);
            obj.isStatic = true;

            // Add a small sphere at top for pointed appearance
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = label + "_Tip";
            tip.transform.SetParent(obj.transform, false);
            tip.transform.localPosition = new Vector3(0f, 1f, 0f);
            tip.transform.localScale = new Vector3(0.15f, 0.3f, 0.15f);
            SetMaterial(tip, MakeMaterial(color));
            Object.DestroyImmediate(tip.GetComponent<Collider>());

            return obj;
        }

        // why: Unity's cylinder primitive ships with a CapsuleCollider whose hemisphere caps
        // extend past the flat cylinder ends, producing invisible bumpers above/below. Swap to a
        // non-convex MeshCollider that matches the cylinder mesh exactly.
        private static void SwapCapsuleForMeshCollider(GameObject cyl)
        {
            var cap = cyl.GetComponent<CapsuleCollider>();
            if (cap != null) Object.DestroyImmediate(cap);
            var mf = cyl.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var mc = cyl.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
        }

        protected GameObject AddSphere(Vector3 position, float radius, Color color, string label = "Sphere")
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.name = label;
            obj.transform.SetParent(transform, false);
            obj.transform.position = position;
            obj.transform.localScale = Vector3.one * radius * 2f;
            SetMaterial(obj, MakeMaterial(color));
            obj.isStatic = true;
            return obj;
        }

        // =====================================================================
        // Terrain Features
        // =====================================================================

        /// <summary>
        /// Flat ground plane at y=0 with the given color and scale.
        /// </summary>
        protected GameObject AddGroundPlane(float halfExtent, Color color, string label = "Ground")
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = label;
            obj.transform.SetParent(transform, false);
            obj.transform.position = new Vector3(0f, -0.5f, 0f);
            obj.transform.localScale = new Vector3(halfExtent * 2f, 1f, halfExtent * 2f);
            SetMaterial(obj, MakeMaterial(color));
            obj.isStatic = true;
            return obj;
        }

        /// <summary>
        /// Invisible boundary walls around the arena to prevent driving off the edge.
        /// Creates 4 tall invisible box colliders at the perimeter.
        /// </summary>
        protected void AddInvisibleWalls(float halfExtent, float height = 40f)
        {
            float thickness = 5f;
            Vector3[] positions = {
                new Vector3(0f, height * 0.5f, halfExtent + thickness * 0.5f),   // North
                new Vector3(0f, height * 0.5f, -halfExtent - thickness * 0.5f),  // South
                new Vector3(halfExtent + thickness * 0.5f, height * 0.5f, 0f),   // East
                new Vector3(-halfExtent - thickness * 0.5f, height * 0.5f, 0f),  // West
            };
            Vector3[] sizes = {
                new Vector3(halfExtent * 2f + thickness * 2f, height, thickness),
                new Vector3(halfExtent * 2f + thickness * 2f, height, thickness),
                new Vector3(thickness, height, halfExtent * 2f + thickness * 2f),
                new Vector3(thickness, height, halfExtent * 2f + thickness * 2f),
            };

            for (int i = 0; i < 4; i++)
            {
                var wall = new GameObject($"ArenaWall_{i}");
                wall.layer = 2; // IgnoreRaycast — so spawn raycasts don't hit walls
                wall.transform.SetParent(transform, false);
                wall.transform.position = positions[i];
                var col = wall.AddComponent<BoxCollider>();
                col.size = sizes[i];
                wall.isStatic = true;
            }
        }

        /// <summary>
        /// Water surface: subdivided deformable mesh at y=0 driven by WaveManager,
        /// plus a dark floor at y=-50.
        /// </summary>
        protected void AddWaterSurface(float halfExtent)
        {
            // --- Deformable water mesh ---
            float cellSize = 4f; // ~1 vertex per 4 units
            float extent = halfExtent * 2f;
            int vertsPerSide = Mathf.CeilToInt(extent / cellSize) + 1;
            float step = extent / (vertsPerSide - 1);

            var surface = new GameObject("WaterSurface");
            surface.transform.SetParent(transform, false);
            surface.transform.position = Vector3.zero;
            surface.layer = LayerMask.NameToLayer("Water");

            var meshFilter = surface.AddComponent<MeshFilter>();
            var meshRenderer = surface.AddComponent<MeshRenderer>();

            // Build subdivided plane mesh
            Mesh mesh = new Mesh();
            mesh.name = "WaterPlaneMesh";

            int vertCount = vertsPerSide * vertsPerSide;
            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];

            for (int z = 0; z < vertsPerSide; z++)
            {
                for (int x = 0; x < vertsPerSide; x++)
                {
                    int idx = z * vertsPerSide + x;
                    float px = -halfExtent + x * step;
                    float pz = -halfExtent + z * step;
                    vertices[idx] = new Vector3(px, 0f, pz);
                    uvs[idx] = new Vector2((float)x / (vertsPerSide - 1),
                                           (float)z / (vertsPerSide - 1));
                }
            }

            int quadCount = (vertsPerSide - 1) * (vertsPerSide - 1);
            var triangles = new int[quadCount * 6];
            int tri = 0;
            for (int z = 0; z < vertsPerSide - 1; z++)
            {
                for (int x = 0; x < vertsPerSide - 1; x++)
                {
                    int bl = z * vertsPerSide + x;
                    int br = bl + 1;
                    int tl = bl + vertsPerSide;
                    int tr = tl + 1;

                    triangles[tri++] = bl;
                    triangles[tri++] = tl;
                    triangles[tri++] = tr;

                    triangles[tri++] = bl;
                    triangles[tri++] = tr;
                    triangles[tri++] = br;
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshFilter.mesh = mesh;

            // --- URP transparent water material ---
            var urpShader = Shader.Find("Universal Render Pipeline/Lit");
            Material waterMat;
            if (urpShader != null)
            {
                waterMat = new Material(urpShader);
                waterMat.name = "WaterURP";

                // Set to transparent rendering
                waterMat.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
                waterMat.SetFloat("_Blend", 0f);   // 0=Alpha, 1=Premultiply
                waterMat.SetOverrideTag("RenderType", "Transparent");
                waterMat.renderQueue = 3000;
                waterMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                waterMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                waterMat.SetInt("_ZWrite", 0);
                waterMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

                waterMat.SetColor("_BaseColor", new Color(0.1f, 0.35f, 0.55f, 0.45f));
                waterMat.SetFloat("_Smoothness", 0.9f);
                waterMat.SetFloat("_Metallic", 0.1f);
            }
            else
            {
                waterMat = MakeTransparentMaterial(
                    new Color(0.1f, 0.35f, 0.55f, 0.45f), "Water");
            }
            meshRenderer.sharedMaterial = waterMat;

            // Trigger-only collider for raycasts (doesn't block physics bodies)
            var col = surface.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size = new Vector3(extent, 0.2f, extent);
            col.isTrigger = true;

            // Add UV scroll animator for the wave normal map
            var animator = surface.AddComponent<WaterBasicAnimator>();
            animator.waveSpeed  = new Vector4(5f, 5f, -4f, 0f);
            animator.waveScale  = 0.07f;
            animator.horizonColor = new Color(0.172f, 0.463f, 0.435f, 1f);
            animator.waterColor   = new Color(0.172f, 0.463f, 0.435f, 1f);

            // Add deformer component that updates vertices each frame
            var deformer = surface.AddComponent<WaterMeshDeformer>();
            deformer.Init(vertsPerSide, halfExtent, step);

            // --- Dark floor deep below surface; bottomless-feeling abyss ---
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SeaFloor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = new Vector3(0f, -800.5f, 0f); // why: deep enough that sinking feels endless
            floor.transform.localScale = new Vector3(halfExtent * 2f, 1f, halfExtent * 2f);
            SetMaterial(floor, MakeMaterial(new Color(0.02f, 0.03f, 0.05f), "SeaFloor")); // why: near-black reads as abyss under fog
            floor.isStatic = true;

            // --- Faint thermocline marker at old floor depth ---
            var thermocline = GameObject.CreatePrimitive(PrimitiveType.Cube);
            thermocline.name = "Thermocline";
            thermocline.transform.SetParent(transform, false);
            thermocline.transform.position = new Vector3(0f, -200f, 0f); // why: cosmetic hint at mid-depth
            thermocline.transform.localScale = new Vector3(halfExtent * 2f, 0.1f, halfExtent * 2f);
            SetMaterial(thermocline, MakeTransparentMaterial(new Color(0.05f, 0.12f, 0.18f, 0.02f), "Thermocline")); // why: barely visible except up close
            Object.DestroyImmediate(thermocline.GetComponent<Collider>()); // why: no gameplay interaction
            thermocline.isStatic = true;
        }

        /// <summary>
        /// Smooth hill built from stacked cylinders.
        /// </summary>
        protected void AddHill(Vector3 center, float baseRadius, float height, int layers, Color color, string label = "Hill")
        {
            int steps = Mathf.Max(3, layers);
            float layerHeight = height / steps;

            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / steps;
                float r = Mathf.Lerp(baseRadius, baseRadius * 0.15f, t * t);
                float y = center.y + i * layerHeight + layerHeight * 0.5f;

                AddCylinder(new Vector3(center.x, y, center.z), r, layerHeight, color, $"{label}_Layer{i}");
            }
        }

        /// <summary>
        /// Simple building: box + flat roof slab.
        /// </summary>
        protected GameObject AddBuilding(Vector3 position, Vector3 size, Color wallColor, Color roofColor, string label = "Building")
        {
            var parent = new GameObject(label);
            parent.transform.SetParent(transform, false);
            parent.transform.position = position;
            parent.isStatic = true;

            // Walls
            var walls = GameObject.CreatePrimitive(PrimitiveType.Cube);
            walls.name = "Walls";
            walls.transform.SetParent(parent.transform, false);
            walls.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            walls.transform.localScale = size;
            SetMaterial(walls, MakeMaterial(wallColor));
            walls.isStatic = true;

            // Roof
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "Roof";
            roof.transform.SetParent(parent.transform, false);
            roof.transform.localPosition = new Vector3(0f, size.y + 0.3f, 0f);
            roof.transform.localScale = new Vector3(size.x + 1f, 0.6f, size.z + 1f);
            SetMaterial(roof, MakeMaterial(roofColor));
            roof.isStatic = true;

            return parent;
        }

        /// <summary>
        /// Wall segment with optional thickness.
        /// </summary>
        protected GameObject AddWall(Vector3 from, Vector3 to, float height, float thickness, Color color, string label = "Wall")
        {
            Vector3 mid = (from + to) * 0.5f + Vector3.up * height * 0.5f;
            float length = Vector3.Distance(from, to);
            Vector3 dir = (to - from).normalized;
            float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = label;
            obj.transform.SetParent(transform, false);
            obj.transform.position = mid;
            obj.transform.localScale = new Vector3(thickness, height, length);
            obj.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            SetMaterial(obj, MakeMaterial(color));
            obj.isStatic = true;
            return obj;
        }

        /// <summary>
        /// Bridge: flat box between two points, elevated.
        /// </summary>
        protected GameObject AddBridge(Vector3 from, Vector3 to, float width, float thickness, Color color, string label = "Bridge")
        {
            Vector3 mid = (from + to) * 0.5f;
            float length = Vector3.Distance(from, to);
            Vector3 dir = (to - from).normalized;
            float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = label;
            obj.transform.SetParent(transform, false);
            obj.transform.position = mid;
            obj.transform.localScale = new Vector3(width, thickness, length);
            obj.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            SetMaterial(obj, MakeMaterial(color));
            obj.isStatic = true;
            return obj;
        }

        // =====================================================================
        // Vegetation / Props
        // =====================================================================

        /// <summary>
        /// Deciduous tree: brown cylinder trunk + green sphere canopy.
        /// </summary>
        protected GameObject AddTree(Vector3 position, float height, float canopyRadius, string label = "Tree")
        {
            var parent = new GameObject(label);
            parent.transform.SetParent(transform, false);
            parent.transform.position = position;
            parent.isStatic = true;

            Color brown = new Color(0.4f, 0.26f, 0.13f);
            Color green = new Color(0.13f, 0.55f, 0.13f);

            // Trunk
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(parent.transform, false);
            trunk.transform.localPosition = new Vector3(0f, height * 0.35f, 0f);
            trunk.transform.localScale = new Vector3(height * 0.12f, height * 0.35f, height * 0.12f);
            SetMaterial(trunk, MakeMaterial(brown, "TreeBark"));
            Object.DestroyImmediate(trunk.GetComponent<Collider>());

            // Canopy
            var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            canopy.name = "Canopy";
            canopy.transform.SetParent(parent.transform, false);
            canopy.transform.localPosition = new Vector3(0f, height * 0.7f, 0f);
            canopy.transform.localScale = Vector3.one * canopyRadius * 2f;
            SetMaterial(canopy, MakeMaterial(green, "Foliage"));
            Object.DestroyImmediate(canopy.GetComponent<Collider>());

            // Trunk collider on parent
            var col = parent.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, height * 0.35f, 0f);
            col.radius = height * 0.06f;
            col.height = height * 0.7f;

            return parent;
        }

        /// <summary>
        /// Pine / conifer tree: brown trunk + stacked green cones (cylinder approximations).
        /// </summary>
        protected GameObject AddPine(Vector3 position, float height, string label = "Pine")
        {
            var parent = new GameObject(label);
            parent.transform.SetParent(transform, false);
            parent.transform.position = position;
            parent.isStatic = true;

            Color brown = new Color(0.35f, 0.22f, 0.1f);
            Color darkGreen = new Color(0.05f, 0.35f, 0.1f);

            // Trunk
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(parent.transform, false);
            trunk.transform.localPosition = new Vector3(0f, height * 0.15f, 0f);
            trunk.transform.localScale = new Vector3(height * 0.08f, height * 0.15f, height * 0.08f);
            SetMaterial(trunk, MakeMaterial(brown));
            Object.DestroyImmediate(trunk.GetComponent<Collider>());

            // Cone layers
            int layers = 3;
            for (int i = 0; i < layers; i++)
            {
                float t = (float)i / layers;
                float radius = Mathf.Lerp(height * 0.35f, height * 0.08f, t);
                float y = height * 0.3f + t * height * 0.6f;
                float coneH = height * 0.25f;

                var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cone.name = $"Foliage_{i}";
                cone.transform.SetParent(parent.transform, false);
                cone.transform.localPosition = new Vector3(0f, y, 0f);
                cone.transform.localScale = new Vector3(radius * 2f, coneH * 0.5f, radius * 2f);
                SetMaterial(cone, MakeMaterial(darkGreen));
                Object.DestroyImmediate(cone.GetComponent<Collider>());
            }

            var col = parent.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, height * 0.4f, 0f);
            col.radius = height * 0.05f;
            col.height = height * 0.8f;

            return parent;
        }

        /// <summary>
        /// Cluster of 3-5 boulders (spheres) in a group.
        /// </summary>
        protected GameObject AddRockCluster(Vector3 center, float spread, float maxRadius, Color color, string label = "Rocks")
        {
            var parent = new GameObject(label);
            parent.transform.SetParent(transform, false);
            parent.transform.position = center;
            parent.isStatic = true;

            int count = Random.Range(3, 6);
            for (int i = 0; i < count; i++)
            {
                float r = Random.Range(maxRadius * 0.3f, maxRadius);
                Vector3 offset = new Vector3(
                    Random.Range(-spread, spread),
                    r * 0.5f,
                    Random.Range(-spread, spread)
                );

                var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = $"Rock_{i}";
                rock.transform.SetParent(parent.transform, false);
                rock.transform.localPosition = offset;
                rock.transform.localScale = new Vector3(r * 2f, r * 1.4f, r * 2f);
                SetMaterial(rock, MakeMaterial(color));
                rock.isStatic = true;
            }

            return parent;
        }

        // =====================================================================
        // Spawn Points
        // =====================================================================

        /// <summary>
        /// Place numbered spawn points at specific positions, looking at origin.
        /// </summary>
        protected void AddSpawnPoints(params Vector3[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                var sp = new GameObject($"SpawnPoint_{i}");
                sp.transform.SetParent(transform, false);
                sp.transform.position = positions[i];
                sp.transform.LookAt(new Vector3(0f, positions[i].y, 0f));
                SpawnPoints.Add(sp.transform);
            }
        }

        /// <summary>
        /// Place spawn points in a ring around center at given radius and count.
        /// </summary>
        protected void AddSpawnRing(Vector3 center, float radius, int count, float yOffset = 1f)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = (360f / count) * i * Mathf.Deg2Rad;
                float x = center.x + Mathf.Cos(angle) * radius;
                float z = center.z + Mathf.Sin(angle) * radius;
                Vector3 pos = new Vector3(x, center.y + yOffset, z);

                var sp = new GameObject($"SpawnPoint_{SpawnPoints.Count}");
                sp.transform.SetParent(transform, false);
                sp.transform.position = pos;
                sp.transform.LookAt(new Vector3(center.x, pos.y, center.z));
                SpawnPoints.Add(sp.transform);
            }
        }

        // =====================================================================
        // Hazard Zones
        // =====================================================================

        /// <summary>
        /// Water hazard: vehicles in this zone die after 3 seconds.
        /// </summary>
        protected GameObject AddWaterHazard(Vector3 center, Vector3 size, string label = "WaterHazard")
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(transform, false);
            obj.transform.position = center;

            var col = obj.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = size;

            var hazard = obj.AddComponent<HazardZone>();
            hazard.hazardType = HazardType.Water;
            hazard.damagePerSecond = 0;
            hazard.killDelay = 3f;

            return obj;
        }

        /// <summary>
        /// Lava hazard: 25 damage per second while in zone.
        /// </summary>
        protected GameObject AddLavaHazard(Vector3 center, Vector3 size, string label = "LavaHazard")
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(transform, false);
            obj.transform.position = center;

            var col = obj.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = size;

            var hazard = obj.AddComponent<HazardZone>();
            hazard.hazardType = HazardType.Lava;
            hazard.damagePerSecond = 25;

            // Visual lava surface
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "LavaVisual";
            visual.transform.SetParent(obj.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = size;
            SetMaterial(visual, MakeEmissiveMaterial(
                new Color(0.9f, 0.2f, 0.0f),
                new Color(1f, 0.3f, 0.0f) * 2f,
                "Lava"));
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            return obj;
        }

        /// <summary>
        /// Ice hazard: 1.5 second grace period then damage. Slippery surface.
        /// </summary>
        protected GameObject AddIceHazard(Vector3 center, Vector3 size, string label = "IceHazard")
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(transform, false);
            obj.transform.position = center;

            var col = obj.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = size;

            var hazard = obj.AddComponent<HazardZone>();
            hazard.hazardType = HazardType.Ice;
            hazard.damagePerSecond = 0;
            hazard.graceTime = 1.5f;

            // Visual ice surface
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "IceVisual";
            visual.transform.SetParent(obj.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = size;
            SetMaterial(visual, MakeTransparentMaterial(new Color(0.7f, 0.85f, 0.95f, 0.6f), "Ice"));
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            return obj;
        }

        // =====================================================================
        // Utility
        // =====================================================================

        protected static void SetMaterial(GameObject obj, Material mat)
        {
            var renderer = obj.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = mat;
        }

        protected static void SetRotation(GameObject obj, float yDeg)
        {
            obj.transform.rotation = Quaternion.Euler(0f, yDeg, 0f);
        }

        protected float Rng(float min, float max)
        {
            return Random.Range(min, max);
        }
    }

    // =========================================================================
    // HazardZone -- trigger volume that damages or kills vehicles inside it
    // =========================================================================

    public enum HazardType { Water, Lava, Ice }

    public class HazardZone : MonoBehaviour
    {
        public HazardType hazardType;
        public int damagePerSecond;
        public float killDelay = 3f;
        public float graceTime = 0f;

        private readonly Dictionary<Collider, float> _timers = new Dictionary<Collider, float>();
        private readonly Dictionary<Collider, float> _grace  = new Dictionary<Collider, float>();

        private void OnTriggerStay(Collider other)
        {
            var vr = other.GetComponentInParent<VehicleRuntime>();
            if (vr == null || !vr.IsAlive) return;

            // Grace period for ice
            if (graceTime > 0f)
            {
                if (!_grace.ContainsKey(other)) _grace[other] = 0f;
                _grace[other] += Time.deltaTime;
                if (_grace[other] < graceTime) return;
            }

            switch (hazardType)
            {
                case HazardType.Water:
                    if (!_timers.ContainsKey(other)) _timers[other] = 0f;
                    _timers[other] += Time.deltaTime;
                    if (_timers[other] >= killDelay)
                    {
                        // Kill all parts
                        foreach (var pn in vr.PartNodes)
                            if (!pn.isDestroyed) pn.TakeDamage(9999);
                    }
                    break;

                case HazardType.Lava:
                    if (damagePerSecond > 0)
                    {
                        int dmg = Mathf.CeilToInt(damagePerSecond * Time.deltaTime);
                        Combat.DamageSystem.DealDamageToVehicle(vr, dmg, vr.transform.position);
                    }
                    break;

                case HazardType.Ice:
                    // Reduce friction / apply slide force
                    var rb = vr.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.AddForce(rb.linearVelocity.normalized * 2f, ForceMode.Acceleration);
                    }
                    break;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            _timers.Remove(other);
            _grace.Remove(other);
        }
    }

    // =========================================================================
    // WaterMeshDeformer -- updates water plane vertices from WaveManager each
    //                      frame so the surface visually shows waves.
    // =========================================================================

    public class WaterMeshDeformer : MonoBehaviour
    {
        private Mesh _mesh;
        private Vector3[] _vertices;
        private int _vertsPerSide;
        private float _halfExtent;
        private float _step;

        /// <summary>
        /// Called by ArenaBase.AddWaterSurface after mesh creation.
        /// </summary>
        public void Init(int vertsPerSide, float halfExtent, float step)
        {
            _vertsPerSide = vertsPerSide;
            _halfExtent = halfExtent;
            _step = step;
        }

        private void Start()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf != null)
            {
                _mesh = mf.mesh; // creates instance we can mutate
                _vertices = _mesh.vertices;
            }
        }

        private void Update()
        {
            if (_mesh == null || _vertices == null) return;
            if (WaveManager.Instance == null) return;

            Vector3 origin = transform.position;

            for (int z = 0; z < _vertsPerSide; z++)
            {
                for (int x = 0; x < _vertsPerSide; x++)
                {
                    int idx = z * _vertsPerSide + x;
                    float worldX = origin.x + (-_halfExtent + x * _step);
                    float worldZ = origin.z + (-_halfExtent + z * _step);

                    _vertices[idx].y = WaveManager.Instance.GetWaterHeight(worldX, worldZ);
                }
            }

            _mesh.vertices = _vertices;
            _mesh.RecalculateNormals();
        }
    }
}
