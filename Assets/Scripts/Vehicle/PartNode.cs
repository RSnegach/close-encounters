using System;
using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Core;
using CloseEncounters.Combat;

namespace CloseEncounters.Vehicle
{
    /// <summary>
    /// Runtime representation of a single part placed on a vehicle.
    /// Handles mesh creation, damage, and destruction.
    /// </summary>
    public class PartNode : MonoBehaviour
    {
        // --- Data ---
        public PartData partData;
        public int currentHp;
        public Vector3Int gridPosition;
        public bool isDestroyed;

        /// <summary>
        /// For armor parts: direction toward the block this armor is attached to.
        /// (1,0,0) = attached on +X face, (0,1,0) = on top, etc.
        /// Default (0,-1,0) = flat on floor (horizontal slab).
        /// </summary>
        public Vector3Int armorFace = new Vector3Int(0, -1, 0);

        // --- Components ---
        private MeshRenderer _meshRenderer;
        private Collider _collider;
        private Transform _wheelModel;
        private float _wheelSpinSpeed = 720f; // degrees per second at full speed
        private System.Collections.IEnumerator _flashCoroutine;
        private Color _flashOriginalColor;
        private bool _flashOriginalCached;
        private Rigidbody _vehicleRb;
        private bool _vehicleRbChecked;

        // --- Events ---
        public event Action OnPartDestroyed;
        public event Action<int> OnPartDamaged;

        // --- Constants ---
        private const float CellSize = 1f;

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        /// <summary>
        /// Initialize the part node from data. Creates the visual mesh
        /// as child primitive(s) based on the part's subcategory/meshData.
        /// </summary>
        public void Setup(PartData data, Vector3Int gridPos)
        {
            partData = data;
            gridPosition = gridPos;
            currentHp = data.hp;
            isDestroyed = false;

            gameObject.name = $"Part_{data.id}_{gridPos.x}_{gridPos.y}_{gridPos.z}";

            CreateMesh();

            // Grab or add a box collider covering the full part size
            _collider = GetComponent<Collider>();
            if (_collider == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(
                    data.size.x * CellSize,
                    data.size.y * CellSize,
                    data.size.z * CellSize
                );
                box.center = new Vector3(
                    (data.size.x - 1) * CellSize * 0.5f,
                    (data.size.y - 1) * CellSize * 0.5f,
                    (data.size.z - 1) * CellSize * 0.5f
                );
                _collider = box;
            }
        }

        private void Start()
        {
            SnapToBlockBelow();
        }

        private void Update()
        {
            // Spin wheels visually based on this vehicle's actual forward velocity,
            // not the local keyboard (a bug that made enemy/AI wheels spin on player input).
            if (_wheelModel != null && !isDestroyed)
            {
                if (!_vehicleRbChecked)
                {
                    _vehicleRb = GetComponentInParent<Rigidbody>();
                    _vehicleRbChecked = true;
                }
                if (_vehicleRb == null) return;

                float forwardSpeed = Vector3.Dot(_vehicleRb.linearVelocity, _vehicleRb.transform.forward);
                if (Mathf.Abs(forwardSpeed) > 0.05f)
                {
                    // Roughly one revolution per meter; negative forwardSpeed spins backward.
                    _wheelModel.Rotate(Vector3.right, forwardSpeed * 60f * Time.deltaTime, Space.Self);
                }
            }
        }

        // ------------------------------------------------------------------
        // Mesh creation
        // ------------------------------------------------------------------

        private void CreateMesh()
        {
            string sub = partData.subcategory ?? "";
            string id = partData.id ?? "";

            // Determine color from meshData
            Color partColor = GetMeshColor();

            // Branch on subcategory / id for special shapes
            string cat = partData.category?.ToLowerInvariant() ?? "";

            // Try loading a prefab model for specific weapons
            if (id == "machine_gun" && TryCreateFromPrefab("Models/MachineGunTurret", 0.015f))
            {
                return;
            }
            else if ((id == "rocket_pod" || id == "rocket") && TryCreateRocketPod())
            {
                return;
            }
            else if ((id == "heavy_cannon") && TryCreateFromPrefab("Models/DefenceCannon", new Vector3(0.8f, 1.0f, 0.8f)))
            {
                return;
            }
            else if (id == "laser" && TryCreateFromPrefab("Models/DefenceLazer", 0.8f))
            {
                return;
            }
            else if (id == "milk_gun")
            {
                CreateCumGunMesh();
                return;
            }
            else if (id == "broadside_cannon"
                && TryCreateFromPrefab("Models/NavalCannon", 0.012f))
            {
                // Broadside cannons mount perpendicular to the hull's long axis.
                // Rotate the model so its barrel points ±X based on grid side.
                if (transform.childCount > 0)
                {
                    var model = transform.GetChild(transform.childCount - 1);
                    float yRot = gridPosition.x >= 0 ? 90f : -90f;
                    model.localRotation = Quaternion.Euler(0f, yRot, 0f);
                }
                return;
            }
            else if ((id == "deck_gun" || id == "swivel_cannon")
                && TryCreateFromPrefab("Models/NavalCannon", 0.012f))
            {
                return;
            }
            else if (cat == "defense")
            {
                CreateArmorMesh(partColor);
            }
            else if (IsFuelTank(id, sub))
            {
                CreateFuelTankMesh(partColor, IsArmoredFuel(id));
            }
            else if (IsWheel(sub))
            {
                CreateWheelMesh(partColor);
            }
            else if (IsTrack(sub))
            {
                CreateTrackMesh(partColor);
            }
            else if (IsMast(sub))
            {
                CreateMastMesh(partColor);
            }
            else if (IsSail(sub))
            {
                CreateSailMesh(partColor);
            }
            else if (cat == "control" && (id.Contains("cockpit") || id.Contains("bridge") || id.Contains("conning")))
            {
                CreateCockpitMesh(partColor);
            }
            else
            {
                CreateDefaultCubeMesh(partColor);
            }
        }

        // --- Fuel tank: use Explosives Package prefabs ---

        private void CreateFuelTankMesh(Color baseColor, bool armored)
        {
            string id = partData.id?.ToLowerInvariant() ?? "";
            Vector3 meshSize = GetMeshSize();

            // Pick prefab based on fuel tank type
            string prefabPath;
            float scale;
            if (id.Contains("booster"))
            {
                prefabPath = "Models/GasCan";
                scale = 0.8f;
            }
            else if (id.Contains("ammo"))
            {
                prefabPath = "Models/Dynamite";
                scale = 0.6f;
            }
            else
            {
                // Standard fuel tank — propane tank
                prefabPath = "Models/FuelTank";
                scale = 1.5f;
            }

            // Try loading the prefab
            var prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab != null)
            {
                var instance = Instantiate(prefab, transform);
                instance.name = "FuelTankModel";
                instance.transform.localPosition = new Vector3(
                    GetMeshCenter().x, 0f, GetMeshCenter().z);
                instance.transform.localScale = Vector3.one * scale;
                // Stand tanks upright (vertical)
                instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

                // Remove prefab colliders
                var cols = instance.GetComponentsInChildren<Collider>();
                for (int i = 0; i < cols.Length; i++)
                    DestroyImmediate(cols[i]);

                // Fix URP materials
                var urpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpShader != null)
                {
                    var renderers = instance.GetComponentsInChildren<Renderer>();
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        var mats = renderers[r].materials;
                        for (int m = 0; m < mats.Length; m++)
                        {
                            if (mats[m] != null && !mats[m].shader.name.Contains("Universal"))
                            {
                                Color c = mats[m].HasProperty("_Color") ? mats[m].color : Color.gray;
                                Texture tex = mats[m].HasProperty("_MainTex") ? mats[m].mainTexture : null;
                                mats[m] = new Material(urpShader);
                                mats[m].color = c;
                                if (tex != null) mats[m].mainTexture = tex;
                            }
                        }
                        renderers[r].materials = mats;
                    }
                }

                _meshRenderer = instance.GetComponentInChildren<MeshRenderer>();
            }
            else
            {
                // Fallback: red capsule
                GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.transform.SetParent(transform, false);
                capsule.transform.localPosition = GetMeshCenter();
                float radius = Mathf.Max(meshSize.x, meshSize.z) * 0.5f;
                float height = meshSize.y;
                capsule.transform.localScale = new Vector3(radius, height * 0.5f, radius);
                SetColor(capsule, new Color(0.8f, 0.15f, 0.1f));
                DestroyImmediate(capsule.GetComponent<Collider>());
                _meshRenderer = capsule.GetComponent<MeshRenderer>();
            }

            // Armored variant: green band cage around the tank
            if (armored)
            {
                float cageRadius = Mathf.Max(meshSize.x, meshSize.z) * 0.55f;
                float cageHeight = meshSize.y;
                Color green = new Color(0.15f, 0.55f, 0.15f);

                // Vertical bars (4 corners)
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * 90f * Mathf.Deg2Rad;
                    GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bar.name = $"ArmorBar_V{i}";
                    bar.transform.SetParent(transform, false);
                    bar.transform.localPosition = new Vector3(
                        Mathf.Cos(angle) * cageRadius + GetMeshCenter().x,
                        cageHeight * 0.5f,
                        Mathf.Sin(angle) * cageRadius + GetMeshCenter().z);
                    bar.transform.localScale = new Vector3(0.06f, cageHeight, 0.06f);
                    SetColor(bar, green);
                    DestroyImmediate(bar.GetComponent<Collider>());
                }

                // Horizontal bands (3 rings)
                for (int ring = 0; ring < 3; ring++)
                {
                    float y = (ring + 1) * cageHeight * 0.25f;
                    for (int seg = 0; seg < 4; seg++)
                    {
                        float a1 = seg * 90f * Mathf.Deg2Rad;
                        float a2 = (seg + 1) * 90f * Mathf.Deg2Rad;
                        float mx = (Mathf.Cos(a1) + Mathf.Cos(a2)) * 0.5f * cageRadius;
                        float mz = (Mathf.Sin(a1) + Mathf.Sin(a2)) * 0.5f * cageRadius;
                        float segAngle = (seg * 90f + 45f);

                        GameObject band = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        band.name = $"ArmorBand_{ring}_{seg}";
                        band.transform.SetParent(transform, false);
                        band.transform.localPosition = new Vector3(
                            mx + GetMeshCenter().x, y, mz + GetMeshCenter().z);
                        band.transform.localRotation = Quaternion.Euler(0f, segAngle, 0f);
                        float segLen = cageRadius * 1.4f;
                        band.transform.localScale = new Vector3(segLen, 0.06f, 0.06f);
                        SetColor(band, green);
                        DestroyImmediate(band.GetComponent<Collider>());
                    }
                }
            }
        }

        // --- Wheel: black cylinder ---

        private static readonly string[] WheelPrefabPaths =
        {
            "Models/Wheels/wheel_01", "Models/Wheels/wheel_02", "Models/Wheels/wheel_03",
            "Models/Wheels/wheel_04", "Models/Wheels/wheel_05", "Models/Wheels/wheel_06",
            "Models/Wheels/wheel_07", "Models/Wheels/wheel_08", "Models/Wheels/wheel_09",
            "Models/Wheels/wheel_10", "Models/Wheels/wheel_11", "Models/Wheels/wheel_12"
        };

        private void CreateCumGunMesh()
        {
            Vector3 center = GetMeshCenter();
            float raiseHeight = 0.6f;

            // White support pillar from cell floor up to the cannon
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "CumGunSupport";
            pillar.transform.SetParent(transform, false);
            pillar.transform.localPosition = new Vector3(center.x, raiseHeight * 0.5f, center.z);
            pillar.transform.localScale = new Vector3(0.12f, raiseHeight * 0.5f, 0.12f);
            SetColor(pillar, Color.white);
            DestroyImmediate(pillar.GetComponent<Collider>());

            // Load the big cannon model, raised up on the support
            var prefab = Resources.Load<GameObject>("Models/MilkCannon");
            if (prefab != null)
            {
                var cannon = Instantiate(prefab, transform);
                cannon.name = "CumGunModel";
                cannon.transform.localPosition = new Vector3(center.x, raiseHeight + 0.1f, center.z);
                cannon.transform.localScale = Vector3.one * 1.2f;
                cannon.transform.localRotation = Quaternion.identity;

                foreach (var c in cannon.GetComponentsInChildren<Collider>())
                    DestroyImmediate(c);

                FixURPMaterials(cannon.transform);
                _meshRenderer = cannon.GetComponentInChildren<MeshRenderer>();
            }
            else
            {
                // Fallback white cube
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "CumGunFallback";
                cube.transform.SetParent(transform, false);
                cube.transform.localPosition = new Vector3(center.x, raiseHeight + 0.2f, center.z);
                cube.transform.localScale = new Vector3(0.4f, 0.3f, 0.7f);
                SetColor(cube, Color.white);
                DestroyImmediate(cube.GetComponent<Collider>());
                _meshRenderer = cube.GetComponent<MeshRenderer>();
            }
        }

        private void CreateWheelMesh(Color baseColor)
        {
            Vector3 meshSize = GetMeshSize();
            Vector3 center = GetMeshCenter();

            // Pick a random wheel from the Low Poly Wheel pack
            string path = WheelPrefabPaths[UnityEngine.Random.Range(0, WheelPrefabPaths.Length)];
            var prefab = Resources.Load<GameObject>(path);

            if (prefab != null)
            {
                var wheel = Instantiate(prefab, transform);
                wheel.name = "WheelModel";
                wheel.transform.localPosition = center;
                // Wheel prefab stands upright naturally -- no rotation needed
                wheel.transform.localRotation = Quaternion.identity;
                float targetRadius = Mathf.Max(meshSize.y, meshSize.z) * 0.45f;
                wheel.transform.localScale = Vector3.one * targetRadius;

                // Remove prefab colliders
                foreach (var c in wheel.GetComponentsInChildren<Collider>())
                    DestroyImmediate(c);

                FixURPMaterials(wheel.transform);
                _meshRenderer = wheel.GetComponentInChildren<MeshRenderer>();
                _wheelModel = wheel.transform;
            }
            else
            {
                // Fallback: simple black cylinder
                var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cyl.transform.SetParent(transform, false);
                cyl.transform.localPosition = center;
                cyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                float radius = Mathf.Max(meshSize.y, meshSize.z) * 0.5f;
                float width = meshSize.x * 0.5f;
                cyl.transform.localScale = new Vector3(radius, width, radius);
                SetColor(cyl, new Color(0.12f, 0.12f, 0.12f));
                DestroyImmediate(cyl.GetComponent<Collider>());
                _meshRenderer = cyl.GetComponent<MeshRenderer>();
            }
        }

        // --- Track: row of cylinders with tread boxes ---

        private void CreateTrackMesh(Color baseColor)
        {
            Vector3 meshSize = GetMeshSize();
            int segments = Mathf.Max(3, Mathf.RoundToInt(meshSize.z / 0.2f));
            float segSpacing = meshSize.z / segments;
            float wheelRadius = meshSize.y * 0.4f;

            Color trackColor = new Color(0.2f, 0.2f, 0.2f);
            MeshRenderer firstRenderer = null;

            // Road wheels
            for (int i = 0; i < segments; i++)
            {
                GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.transform.SetParent(transform, false);
                float zOff = -meshSize.z * 0.5f + segSpacing * 0.5f + i * segSpacing;
                wheel.transform.localPosition = GetMeshCenter() + new Vector3(0f, 0f, zOff);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheel.transform.localScale = new Vector3(wheelRadius, meshSize.x * 0.4f, wheelRadius);
                SetColor(wheel, trackColor);
                DestroyImmediate(wheel.GetComponent<Collider>());
                if (firstRenderer == null)
                    firstRenderer = wheel.GetComponent<MeshRenderer>();
            }

            // Top tread (flat box along the top)
            GameObject topTread = GameObject.CreatePrimitive(PrimitiveType.Cube);
            topTread.transform.SetParent(transform, false);
            topTread.transform.localPosition = GetMeshCenter() + new Vector3(0f, wheelRadius * 0.9f, 0f);
            topTread.transform.localScale = new Vector3(meshSize.x * 0.85f, meshSize.y * 0.1f, meshSize.z * 0.95f);
            SetColor(topTread, trackColor);
            DestroyImmediate(topTread.GetComponent<Collider>());

            // Bottom tread
            GameObject bottomTread = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bottomTread.transform.SetParent(transform, false);
            bottomTread.transform.localPosition = GetMeshCenter() + new Vector3(0f, -wheelRadius * 0.9f, 0f);
            bottomTread.transform.localScale = new Vector3(meshSize.x * 0.85f, meshSize.y * 0.1f, meshSize.z * 0.95f);
            SetColor(bottomTread, trackColor);
            DestroyImmediate(bottomTread.GetComponent<Collider>());

            _meshRenderer = firstRenderer;
        }

        // --- Mast: brown tapered cylinder ---

        private void CreateMastMesh(Color baseColor)
        {
            Vector3 meshSize = GetMeshSize();

            // Main shaft — a cylinder
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.transform.SetParent(transform, false);
            shaft.transform.localPosition = GetMeshCenter();
            float height = meshSize.y;
            float baseRadius = Mathf.Max(meshSize.x, meshSize.z) * 0.5f;
            shaft.transform.localScale = new Vector3(baseRadius, height * 0.5f, baseRadius);

            Color brown = new Color(0.55f, 0.41f, 0.08f); // #8B6914

            // Apply wood texture to the mast shaft
            Material mastMat = PartTextureManager.GetMaterialForPart(partData);
            if (mastMat != null)
            {
                SetTexturedMaterial(shaft, mastMat, brown);
            }
            else
            {
                SetColor(shaft, brown);
            }
            DestroyImmediate(shaft.GetComponent<Collider>());

            // Taper cap — smaller cylinder on top
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.transform.SetParent(transform, false);
            cap.transform.localPosition = GetMeshCenter() + new Vector3(0f, height * 0.4f, 0f);
            cap.transform.localScale = new Vector3(baseRadius * 0.4f, height * 0.12f, baseRadius * 0.4f);
            if (mastMat != null)
            {
                SetTexturedMaterial(cap, mastMat, brown * 0.8f);
            }
            else
            {
                SetColor(cap, brown * 0.8f);
            }
            DestroyImmediate(cap.GetComponent<Collider>());

            _meshRenderer = shaft.GetComponent<MeshRenderer>();
        }

        // --- Sail: thin cream-colored box ---

        private void CreateSailMesh(Color baseColor)
        {
            Vector3 meshSize = GetMeshSize();

            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(transform, false);
            box.transform.localPosition = GetMeshCenter();
            // Sails are thin along X, tall on Y, wide on Z
            box.transform.localScale = new Vector3(
                Mathf.Max(meshSize.x, 0.05f),
                meshSize.y,
                meshSize.z
            );

            Color cream = new Color(0.96f, 0.94f, 0.88f); // #F5F0E0
            SetColor(box, cream);
            DestroyImmediate(box.GetComponent<Collider>());

            _meshRenderer = box.GetComponent<MeshRenderer>();
        }

        // --- Cockpit: chair + procedural steering wheel ---

        private void CreateCockpitMesh(Color partColor)
        {
            Vector3 center = GetMeshCenter();

            // Load chair
            var chairPrefab = Resources.Load<GameObject>("Models/Chair");
            if (chairPrefab != null)
            {
                var chair = Instantiate(chairPrefab, transform);
                chair.name = "CockpitChair";
                chair.transform.localScale = Vector3.one * 0.55f;
                chair.transform.localRotation = Quaternion.identity;

                // Position at bottom of cell, centered
                chair.transform.localPosition = center;
                // Snap to bottom using bounds
                var chairRenderers = chair.GetComponentsInChildren<Renderer>();
                if (chairRenderers.Length > 0)
                {
                    Bounds cb = chairRenderers[0].bounds;
                    for (int i = 1; i < chairRenderers.Length; i++)
                        cb.Encapsulate(chairRenderers[i].bounds);
                    float bottomY = cb.min.y - chair.transform.position.y;
                    Vector3 cp = chair.transform.localPosition;
                    cp.y = -bottomY;
                    chair.transform.localPosition = cp;
                }

                // Remove colliders
                var cols = chair.GetComponentsInChildren<Collider>();
                for (int i = 0; i < cols.Length; i++) DestroyImmediate(cols[i]);

                FixPrefabMaterials(chair);
                _meshRenderer = chair.GetComponentInChildren<MeshRenderer>();
            }

            // Procedural steering wheel from primitives
            {
                var wheelParent = new GameObject("ShipWheel");
                wheelParent.transform.SetParent(transform, false);
                wheelParent.transform.localPosition = new Vector3(center.x, 0.3f, center.z + 0.35f);
                wheelParent.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);

                Color wheelColor = new Color(0.4f, 0.25f, 0.1f); // dark wood

                // Ring (torus approximated by 8 small cylinders in a circle)
                float ringRadius = 0.15f;
                for (int s = 0; s < 8; s++)
                {
                    float a1 = s * 45f * Mathf.Deg2Rad;
                    float a2 = (s + 1) * 45f * Mathf.Deg2Rad;
                    float mx = (Mathf.Cos(a1) + Mathf.Cos(a2)) * 0.5f * ringRadius;
                    float my = (Mathf.Sin(a1) + Mathf.Sin(a2)) * 0.5f * ringRadius;

                    var seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    seg.transform.SetParent(wheelParent.transform, false);
                    seg.transform.localPosition = new Vector3(mx, my, 0f);
                    seg.transform.localRotation = Quaternion.Euler(0f, 0f, s * 45f + 22.5f);
                    seg.transform.localScale = new Vector3(0.02f, ringRadius * 0.42f, 0.02f);
                    SetColor(seg, wheelColor);
                    DestroyImmediate(seg.GetComponent<Collider>());
                }

                // Center hub
                var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                hub.transform.SetParent(wheelParent.transform, false);
                hub.transform.localPosition = Vector3.zero;
                hub.transform.localScale = new Vector3(0.04f, 0.01f, 0.04f);
                SetColor(hub, wheelColor * 0.7f);
                DestroyImmediate(hub.GetComponent<Collider>());

                // 4 spokes
                for (int s = 0; s < 4; s++)
                {
                    float angle = s * 90f * Mathf.Deg2Rad;
                    var spoke = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    spoke.transform.SetParent(wheelParent.transform, false);
                    spoke.transform.localPosition = new Vector3(
                        Mathf.Cos(angle) * ringRadius * 0.5f,
                        Mathf.Sin(angle) * ringRadius * 0.5f, 0f);
                    spoke.transform.localRotation = Quaternion.Euler(0f, 0f, s * 90f);
                    spoke.transform.localScale = new Vector3(0.015f, ringRadius * 0.5f, 0.015f);
                    SetColor(spoke, wheelColor);
                    DestroyImmediate(spoke.GetComponent<Collider>());
                }

                // Vertical post from wheel down to cell floor
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "WheelPost";
                post.transform.SetParent(transform, false);
                float postTop = wheelParent.transform.localPosition.y;
                float postHeight = postTop;
                post.transform.localPosition = new Vector3(center.x, postHeight * 0.5f, center.z + 0.35f);
                post.transform.localScale = new Vector3(0.03f, postHeight * 0.5f, 0.03f);
                SetColor(post, wheelColor * 0.8f);
                DestroyImmediate(post.GetComponent<Collider>());
            }

            // Banana Man — posed into a seated position via bone rotations
            var bananaPrefab = Resources.Load<GameObject>("Models/BananaMan");
            if (bananaPrefab != null)
            {
                var banana = Instantiate(bananaPrefab, transform);
                banana.name = "BananaMan";
                banana.transform.localScale = Vector3.one * 0.5f;
                banana.transform.localRotation = Quaternion.identity;
                banana.transform.localPosition = new Vector3(center.x, 0.15f, center.z);

                // Pose bones for a seated position
                PoseBananaManSeated(banana.transform);
                banana.AddComponent<BananaManAnimator>();

                var bCols = banana.GetComponentsInChildren<Collider>();
                for (int i = 0; i < bCols.Length; i++) DestroyImmediate(bCols[i]);

                FixPrefabMaterials(banana);
            }

            // If no prefabs loaded, fall back to colored cube
            if (_meshRenderer == null)
            {
                CreateDefaultCubeMesh(partColor);
            }
        }

        /// <summary>
        /// Pose BananaMan skeleton into a seated position by rotating bones directly.
        /// Bone hierarchy: Armature > Hips > Spine > Neck > Head
        ///                               > Left/Right Thigh > Leg > Foot
        ///                               > Left/Right Shoulder > Arm > Forearm > Hand
        /// </summary>
        private static void PoseBananaManSeated(Transform root)
        {
            // Build a lookup of all child transforms by name
            var bones = new System.Collections.Generic.Dictionary<string, Transform>();
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                bones[all[i].name] = all[i];

            // Helper to apply a local rotation to a bone if it exists
            System.Action<string, Vector3> pose = (boneName, euler) =>
            {
                Transform bone;
                if (bones.TryGetValue(boneName, out bone))
                    bone.localRotation = Quaternion.Euler(euler);
            };

            // Hips: flat, butt on seat
            pose("Hips", new Vector3(0f, 0f, 0f));

            // Spine + Neck + Head: straight upright
            pose("Spine", new Vector3(0f, 0f, 0f));
            pose("Neck",  new Vector3(0f, 0f, 0f));
            pose("Head",  new Vector3(0f, 0f, 0f));

            // Thighs: bent 90° forward (sitting)
            pose("Left Thigh",  new Vector3(90f, 0f, 0f));
            pose("Right Thigh", new Vector3(90f, 0f, 0f));
            pose("LeftThigh",   new Vector3(90f, 0f, 0f));
            pose("RightThigh",  new Vector3(90f, 0f, 0f));
            pose("Thigh.L",     new Vector3(90f, 0f, 0f));
            pose("Thigh.R",     new Vector3(90f, 0f, 0f));

            // Shins: bent back 90° at knee (feet flat on ground)
            pose("Left Leg",  new Vector3(-85f, 0f, 0f));
            pose("Right Leg", new Vector3(-85f, 0f, 0f));
            pose("LeftLeg",   new Vector3(-85f, 0f, 0f));
            pose("RightLeg",  new Vector3(-85f, 0f, 0f));
            pose("Leg.L",     new Vector3(-85f, 0f, 0f));
            pose("Leg.R",     new Vector3(-85f, 0f, 0f));

            // Feet: flat on ground
            pose("Left Foot",  new Vector3(0f, 0f, 0f));
            pose("Right Foot", new Vector3(0f, 0f, 0f));
            pose("LeftFoot",   new Vector3(0f, 0f, 0f));
            pose("RightFoot",  new Vector3(0f, 0f, 0f));
            pose("Foot.L",     new Vector3(0f, 0f, 0f));
            pose("Foot.R",     new Vector3(0f, 0f, 0f));

            // Shoulders: drop from T-pose, angled forward toward wheel
            pose("Left Shoulder",  new Vector3(40f, 0f, 70f));
            pose("Right Shoulder", new Vector3(40f, 0f, -70f));
            pose("LeftShoulder",   new Vector3(40f, 0f, 70f));
            pose("RightShoulder",  new Vector3(40f, 0f, -70f));
            pose("Shoulder.L",     new Vector3(40f, 0f, 70f));
            pose("Shoulder.R",     new Vector3(40f, 0f, -70f));

            // Upper arms: straight out in front, toward steering wheel
            pose("Left Arm",  new Vector3(60f, 0f, 0f));
            pose("Right Arm", new Vector3(60f, 0f, 0f));
            pose("LeftArm",   new Vector3(60f, 0f, 0f));
            pose("RightArm",  new Vector3(60f, 0f, 0f));
            pose("Arm.L",     new Vector3(60f, 0f, 0f));
            pose("Arm.R",     new Vector3(60f, 0f, 0f));

            // Forearms: bent at elbow, hands gripping the wheel
            pose("Left Forearm",  new Vector3(-45f, 10f, 0f));
            pose("Right Forearm", new Vector3(-45f, -10f, 0f));
            pose("LeftForearm",   new Vector3(-45f, 10f, 0f));
            pose("RightForearm",  new Vector3(-45f, -10f, 0f));
            pose("Forearm.L",     new Vector3(-45f, 10f, 0f));
            pose("Forearm.R",     new Vector3(-45f, -10f, 0f));
        }

        private static void FixPrefabMaterials(GameObject instance)
        {
            var urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) return;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && !mats[m].shader.name.Contains("Universal"))
                    {
                        Color c = mats[m].HasProperty("_Color") ? mats[m].color : Color.gray;
                        Texture tex = mats[m].HasProperty("_MainTex") ? mats[m].mainTexture : null;
                        mats[m] = new Material(urpShader);
                        mats[m].color = c;
                        if (tex != null) mats[m].mainTexture = tex;
                    }
                }
                renderers[i].materials = mats;
            }
        }

        // --- Armor: thin slab oriented by armorFace, with cross-hatch ---

        private void CreateArmorMesh(Color partColor)
        {
            float thickness = 0.15f;
            Vector3 center = GetMeshCenter();
            Vector3 cellSize = new Vector3(
                partData.size.x * CellSize,
                partData.size.y * CellSize,
                partData.size.z * CellSize);

            Vector3 slabScale;
            Vector3 slabPos = center;
            Vector3 ridgeDir;  // direction ridges run across the slab
            float ridgeSpan;   // length of the ridges
            int ridgeAxis;     // 0=X, 1=Y, 2=Z — which axis ridges vary along

            if (armorFace.x != 0)
            {
                // Vertical slab on X face (YZ plane) — flush toward neighbor
                slabScale = new Vector3(thickness, cellSize.y, cellSize.z);
                slabPos.x = center.x + (armorFace.x > 0 ? 0.5f - thickness * 0.5f : -0.5f + thickness * 0.5f) * cellSize.x;
                ridgeDir = Vector3.up;
                ridgeSpan = cellSize.z * 0.95f;
                ridgeAxis = 1;
            }
            else if (armorFace.z != 0)
            {
                // Vertical slab on Z face (XY plane) — flush toward neighbor
                slabScale = new Vector3(cellSize.x, cellSize.y, thickness);
                slabPos.z = center.z + (armorFace.z > 0 ? 0.5f - thickness * 0.5f : -0.5f + thickness * 0.5f) * cellSize.z;
                ridgeDir = Vector3.up;
                ridgeSpan = cellSize.x * 0.95f;
                ridgeAxis = 1;
            }
            else
            {
                // Horizontal slab on Y face (XZ plane)
                slabScale = new Vector3(cellSize.x, thickness, cellSize.z);
                if (armorFace.y > 0)
                    slabPos.y = center.y + (cellSize.y * 0.5f - thickness * 0.5f);
                else
                    slabPos.y = center.y - (cellSize.y * 0.5f - thickness * 0.5f);
                ridgeDir = Vector3.right;
                ridgeSpan = cellSize.z * 0.95f;
                ridgeAxis = 0;
            }

            // Main slab
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.transform.SetParent(transform, false);
            slab.transform.localPosition = slabPos;
            slab.transform.localScale = slabScale;

            // Apply metallic texture to armor slab
            Material armorMat = PartTextureManager.GetMaterialForPart(partData);
            if (armorMat != null)
            {
                SetTexturedMaterial(slab, armorMat, partColor);
            }
            else
            {
                SetColor(slab, partColor);
            }
            DestroyImmediate(slab.GetComponent<Collider>());
            _meshRenderer = slab.GetComponent<MeshRenderer>();

            // Cross-hatch ridges
            Color lineColor = partColor * 0.55f;
            int lineCount = 4;
            float extent = (ridgeAxis == 0) ? slabScale.x : slabScale.y;

            for (int i = 0; i < lineCount; i++)
            {
                float t = (float)(i + 1) / (lineCount + 1);
                GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.transform.SetParent(transform, false);

                Vector3 linePos = slabPos;
                Vector3 lineScale;

                if (armorFace.x != 0)
                {
                    float yOff = -slabScale.y * 0.5f + t * slabScale.y;
                    linePos.y += yOff;
                    linePos.x += armorFace.x > 0 ? -thickness * 0.5f - 0.01f : thickness * 0.5f + 0.01f;
                    lineScale = new Vector3(0.02f, 0.04f, ridgeSpan);
                }
                else if (armorFace.z != 0)
                {
                    float yOff = -slabScale.y * 0.5f + t * slabScale.y;
                    linePos.y += yOff;
                    linePos.z += armorFace.z > 0 ? -thickness * 0.5f - 0.01f : thickness * 0.5f + 0.01f;
                    lineScale = new Vector3(ridgeSpan, 0.04f, 0.02f);
                }
                else
                {
                    float xOff = -slabScale.x * 0.5f + t * slabScale.x;
                    linePos.x += xOff;
                    linePos.y += armorFace.y > 0 ? -thickness * 0.5f - 0.01f : thickness * 0.5f + 0.01f;
                    lineScale = new Vector3(0.04f, 0.02f, ridgeSpan);
                }

                line.transform.localPosition = linePos;
                line.transform.localScale = lineScale;
                SetColor(line, lineColor);
                DestroyImmediate(line.GetComponent<Collider>());
            }
        }

        // --- Default: colored cube from meshData ---

        private void CreateDefaultCubeMesh(Color partColor)
        {
            Vector3 meshSize = GetMeshSize();
            string id = partData.id?.ToLowerInvariant() ?? "";

            // Medium frames are thin flat blocks (same height as tank tracks)
            if (id == "medium_frame")
                meshSize.y = 0.3f;
            else if (id == "light_frame")
                meshSize.y = 0.2f;

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(transform, false);

            // ALL parts snap to the bottom of their cell
            Vector3 pos = GetMeshCenter();
            pos.y = meshSize.y * 0.5f;

            cube.transform.localPosition = pos;
            cube.transform.localScale = meshSize;

            // Try to apply a textured material (wood plank or metal plate)
            Material texMat = PartTextureManager.GetMaterialForPart(partData);
            if (texMat != null)
            {
                SetTexturedMaterial(cube, texMat, partColor);
            }
            else
            {
                SetColor(cube, partColor);
            }

            DestroyImmediate(cube.GetComponent<Collider>());

            _meshRenderer = cube.GetComponent<MeshRenderer>();
        }

        // ------------------------------------------------------------------
        // Damage
        // ------------------------------------------------------------------

        /// <summary>
        /// Apply damage to this part. Returns true if the part was destroyed.
        /// </summary>
        public bool TakeDamage(int amount)
        {
            if (isDestroyed) return false;

            int clamped = Mathf.Max(0, amount);
            currentHp -= clamped;

            OnPartDamaged?.Invoke(clamped);

            if (currentHp <= 0)
            {
                currentHp = 0;
                DestroyPart();
                return true;
            }

            // Flash the part red briefly
            if (_meshRenderer != null)
            {
                if (_flashCoroutine != null)
                    StopCoroutine(_flashCoroutine);
                _flashCoroutine = DamageFlash();
                StartCoroutine(_flashCoroutine);
            }

            return false;
        }

        private System.Collections.IEnumerator DamageFlash()
        {
            if (_meshRenderer == null) yield break;

            if (!_flashOriginalCached)
            {
                _flashOriginalColor = _meshRenderer.material.color;
                _flashOriginalCached = true;
            }
            _meshRenderer.material.color = Color.red;
            yield return new WaitForSeconds(0.1f);
            if (_meshRenderer != null && !isDestroyed)
                _meshRenderer.material.color = _flashOriginalColor;
            _flashCoroutine = null;
        }

        /// <summary>
        /// Destroy this part. Disables visuals, fires event.
        /// </summary>
        public void DestroyPart()
        {
            if (isDestroyed) return;

            isDestroyed = true;
            currentHp = 0;

            // Eject Banana Man if this is a cockpit
            EjectBananaMan();

            // Disable visuals
            if (_meshRenderer != null)
                _meshRenderer.enabled = false;

            // Disable child renderers
            var childRenderers = GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < childRenderers.Length; i++)
                childRenderers[i].enabled = false;

            if (_collider != null)
                _collider.enabled = false;

            OnPartDestroyed?.Invoke();
        }

        private void EjectBananaMan()
        {
            var banana = transform.Find("BananaMan");
            if (banana == null) return;

            // Detach from vehicle
            banana.SetParent(null);
            banana.gameObject.SetActive(true);

            // Add physics for ragdoll flight
            var rb = banana.gameObject.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.linearDamping = 0.3f;

            // Add a small collider so he bounces
            var col = banana.gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.2f;
            col.height = 0.8f;
            col.center = new Vector3(0f, 0.4f, 0f);

            // Launch him upward and spinning
            Vector3 ejectDir = (Vector3.up * 2f + UnityEngine.Random.insideUnitSphere).normalized;
            rb.AddForce(ejectDir * 15f, ForceMode.VelocityChange);
            rb.AddTorque(UnityEngine.Random.insideUnitSphere * 20f, ForceMode.VelocityChange);

            // Re-enable renderers (they were disabled by DestroyPart)
            var bananaRenderers = banana.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < bananaRenderers.Length; i++)
                bananaRenderers[i].enabled = true;

            // Auto-destroy after 8 seconds
            Destroy(banana.gameObject, 8f);

            // VFX: small explosion where he ejected from
            VFXManager.TinyExplosion(transform.position, 0.5f);
        }

        /// <summary>
        /// Returns true if the part is placed and not destroyed.
        /// </summary>
        public bool IsFunctional()
        {
            return !isDestroyed && currentHp > 0;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private Color GetMeshColor()
        {
            string cat = partData.category?.ToLowerInvariant() ?? "";
            string sub = partData.subcategory?.ToLowerInvariant() ?? "";
            string id = partData.id?.ToLowerInvariant() ?? "";

            // ── Weapons: black ──
            if (cat == "weapon")
                return new Color(0.12f, 0.12f, 0.12f);

            // ── Defense/Armor: metallic olive ──
            if (cat == "defense")
                return new Color(0.50f, 0.52f, 0.48f);

            // ── Control: blue glass for cockpits, dark grey for bridges ──
            if (cat == "control")
            {
                if (id.Contains("cockpit"))
                    return new Color(0.25f, 0.45f, 0.70f); // blue glass
                return new Color(0.35f, 0.35f, 0.40f); // dark instrument grey
            }

            // ── Structural ──
            if (cat == "structural")
            {
                // Boat parts: brown wood (water-domain hulls, decks, keels)
                bool isWaterPart = false;
                if (partData.domains != null)
                    for (int i = 0; i < partData.domains.Length; i++)
                    {
                        string d = partData.domains[i]?.ToLowerInvariant() ?? "";
                        if (d == "water" || d == "sea") { isWaterPart = true; break; }
                    }
                if (isWaterPart && (sub.Contains("hull") || sub.Contains("deck")
                    || sub.Contains("keel") || sub.Contains("plank") || id.Contains("hull")
                    || id.Contains("deck") || id.Contains("bow") || id.Contains("superstructure")))
                    return new Color(0.45f, 0.30f, 0.12f); // warm brown wood

                // Sails and masts handled by their own CreateXxxMesh methods
                // Everything else: steel grey
                return new Color(0.60f, 0.60f, 0.63f);
            }

            // ── Propulsion: engine grey (wheels/tracks handled by their own mesh methods) ──
            if (cat == "propulsion")
                return new Color(0.40f, 0.40f, 0.42f);

            // ── Utility ──
            if (cat == "utility")
            {
                // Fuel tanks handled by CreateFuelTankMesh — skip here
                if (id.Contains("fuel"))
                    return new Color(0.80f, 0.15f, 0.10f); // red (won't be used, mesh overrides)
                return new Color(0.50f, 0.45f, 0.30f); // crate tan
            }

            // Fallback
            return new Color(0.53f, 0.53f, 0.53f);
        }

        private Vector3 GetMeshSize()
        {
            if (partData.meshData != null && partData.meshData.TryGetValue("size", out object sizeObj))
            {
                if (sizeObj is List<object> list && list.Count >= 3)
                {
                    return new Vector3(
                        ToFloat(list[0], 0.5f),
                        ToFloat(list[1], 0.5f),
                        ToFloat(list[2], 0.5f)
                    );
                }
            }

            // Fallback: use grid size
            return new Vector3(
                partData.size.x * CellSize,
                partData.size.y * CellSize,
                partData.size.z * CellSize
            );
        }

        /// <summary>
        /// If the block directly below this one in the grid has a mesh shorter
        /// than the full cell height, shift all visual children down so this
        /// part sits flush on top of that block's actual mesh surface.
        /// </summary>
        private void SnapToBlockBelow()
        {
            if (gridPosition.y == 0) return;
            if (transform.parent == null) return;

            // Find the PartNode directly below in the grid
            PartNode below = null;
            Vector3Int belowPos = new Vector3Int(gridPosition.x, gridPosition.y - 1, gridPosition.z);
            var siblings = transform.parent.GetComponentsInChildren<PartNode>(true);

            for (int i = 0; i < siblings.Length; i++)
            {
                if (siblings[i] == this) continue;
                // Check if this sibling occupies the cell below us
                // (multi-cell parts occupy from origin to origin+size-1)
                var s = siblings[i];
                var sp = s.gridPosition;
                var ss = s.partData != null ? s.partData.size : Vector3Int.one;
                if (belowPos.x >= sp.x && belowPos.x < sp.x + ss.x &&
                    belowPos.y >= sp.y && belowPos.y < sp.y + ss.y &&
                    belowPos.z >= sp.z && belowPos.z < sp.z + ss.z)
                {
                    below = s;
                    break;
                }
            }

            if (below == null || below.partData == null) return;

            // Get the below block's actual visual height
            string belowId = below.partData.id?.ToLowerInvariant() ?? "";
            float belowMeshHeight = below.partData.size.y * CellSize; // default: full cell
            // Match the hardcoded heights from CreateDefaultCubeMesh
            if (belowId == "medium_frame") belowMeshHeight = 0.3f;
            else if (belowId == "light_frame") belowMeshHeight = 0.2f;
            else if (belowId == "tank_tracks") belowMeshHeight = 0.35f;

            float cellHeight = below.partData.size.y * CellSize;
            float gap = cellHeight - belowMeshHeight;

            if (gap < 0.02f) return;

            // Move the entire transform down by the gap
            Vector3 pos = transform.localPosition;
            pos.y -= gap;
            transform.localPosition = pos;
        }

        private Vector3 GetMeshCenter()
        {
            return new Vector3(
                (partData.size.x - 1) * CellSize * 0.5f,
                (partData.size.y - 1) * CellSize * 0.5f,
                (partData.size.z - 1) * CellSize * 0.5f
            );
        }

        private static void SetColor(GameObject obj, Color color)
        {
            var renderer = obj.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                renderer.material.color = color;
            }
        }

        /// <summary>
        /// Apply a textured material (from PartTextureManager) to a GameObject,
        /// creating a per-instance copy tinted with the given color.
        /// </summary>
        private static void SetTexturedMaterial(GameObject obj, Material sourceMaterial, Color tintColor)
        {
            var renderer = obj.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            var mat = new Material(sourceMaterial);
            mat.color = tintColor;
            renderer.material = mat;
        }

        /// <summary>
        /// Assembles the rocket pod from base + connector + main turret prefabs.
        /// </summary>
        private bool TryCreateRocketPod()
        {
            var mainPrefab = Resources.Load<GameObject>("Models/DefenceRocket");
            if (mainPrefab == null) return false;

            float scale = 0.9f;
            Vector3 center = GetMeshCenter();

            // Main turret (has RocketBase + RocketRotator + RocketTower)
            var main = Instantiate(mainPrefab, transform);
            main.name = "RocketTurret";
            main.transform.localScale = Vector3.one * scale;
            main.transform.localRotation = Quaternion.identity;

            // Base pedestal underneath
            var basePrefab = Resources.Load<GameObject>("Models/RocketBase");
            if (basePrefab != null)
            {
                var baseInst = Instantiate(basePrefab, transform);
                baseInst.name = "RocketBasePedestal";
                baseInst.transform.localScale = Vector3.one * scale;
                baseInst.transform.localRotation = Quaternion.identity;
                baseInst.transform.localPosition = new Vector3(center.x, 0f, center.z);
                // Remove colliders
                foreach (var c in baseInst.GetComponentsInChildren<Collider>())
                    DestroyImmediate(c);
            }

            // Connector between base and turret
            var connPrefab = Resources.Load<GameObject>("Models/RocketConnector");
            if (connPrefab != null)
            {
                var conn = Instantiate(connPrefab, transform);
                conn.name = "RocketConnector";
                conn.transform.localScale = Vector3.one * scale;
                conn.transform.localRotation = Quaternion.identity;
                conn.transform.localPosition = new Vector3(center.x, 0f, center.z);
                // Remove colliders
                foreach (var c in conn.GetComponentsInChildren<Collider>())
                    DestroyImmediate(c);
            }

            // Position main turret: find bounds and sit at cell floor
            Vector3 pos = center;
            pos.y = 0f;
            var renderers = main.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int b = 1; b < renderers.Length; b++)
                    bounds.Encapsulate(renderers[b].bounds);
                float bottomY = bounds.min.y - main.transform.position.y;
                pos.y = -bottomY;
            }
            main.transform.localPosition = pos;

            // Remove colliders from main
            foreach (var c in main.GetComponentsInChildren<Collider>())
                DestroyImmediate(c);

            // Fix URP materials on all children
            FixURPMaterials(transform);

            return true;
        }

        /// <summary>
        /// Try to instantiate a prefab from Resources as the part's visual model.
        /// Returns true if the prefab was found and instantiated.
        /// </summary>
        private bool TryCreateFromPrefab(string resourcePath, float scale, float yOffset = 0f)
        {
            return TryCreateFromPrefab(resourcePath, Vector3.one * scale, yOffset);
        }

        private bool TryCreateFromPrefab(string resourcePath, Vector3 scale, float yOffset = 0f)
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null) return false;

            var instance = Instantiate(prefab, transform);
            instance.transform.localScale = scale;
            instance.transform.localRotation = Quaternion.identity;

            // Position so the model's bottom sits flush at the cell floor (y=0)
            // Calculate the model's bounds to find how far below origin the mesh extends
            Vector3 pos = GetMeshCenter();
            pos.y = 0f;
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds combinedBounds = renderers[0].bounds;
                for (int b = 1; b < renderers.Length; b++)
                    combinedBounds.Encapsulate(renderers[b].bounds);
                // Shift up so bottom of model sits at y=0
                float bottomY = combinedBounds.min.y - instance.transform.position.y;
                pos.y = -bottomY + yOffset;
            }
            instance.transform.localPosition = pos;

            // Remove any colliders from the prefab (we use our own BoxCollider)
            var cols = instance.GetComponentsInChildren<Collider>();
            for (int i = 0; i < cols.Length; i++)
                DestroyImmediate(cols[i]);

            FixURPMaterials(instance.transform);

            _meshRenderer = instance.GetComponentInChildren<MeshRenderer>();
            return true;
        }

        private static void FixURPMaterials(Transform root)
        {
            var urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) return;

            var renderers = root.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && mats[m].shader != urpShader
                        && !mats[m].shader.name.Contains("Universal"))
                    {
                        Color col = mats[m].HasProperty("_Color")
                            ? mats[m].color : Color.gray;
                        Texture tex = mats[m].HasProperty("_MainTex")
                            ? mats[m].mainTexture : null;
                        mats[m] = new Material(urpShader);
                        mats[m].color = col;
                        if (tex != null) mats[m].mainTexture = tex;
                    }
                }
                renderers[i].materials = mats;
            }
        }

        private static bool IsFuelTank(string id, string sub)
        {
            return id.Contains("fuel") || sub.Contains("fuel");
        }

        private static bool IsArmoredFuel(string id)
        {
            return id.Contains("armored_fuel");
        }

        private static bool IsWheel(string sub)
        {
            return string.Equals(sub, "wheel", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrack(string sub)
        {
            return string.Equals(sub, "track", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMast(string sub)
        {
            return string.Equals(sub, "mast", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSail(string sub)
        {
            return string.Equals(sub, "sail", StringComparison.OrdinalIgnoreCase);
        }

        private static float ToFloat(object v, float def)
        {
            if (v is float f) return f;
            if (v is double d) return (float)d;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v is string s && float.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out float r))
                return r;
            return def;
        }
    }
}
