using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Combat;

namespace CloseEncounters.Arena
{
    /// <summary>Composite military-base prop builders used by MilitaryBaseBuilder.</summary>
    public static class BaseDetailProps
    {
        private static readonly Dictionary<int, Material> _matCache = new Dictionary<int, Material>();

        // ---- material helpers ----
        private static int PackKey(Color c, bool emissive, float smooth, float metal)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 31f), 0, 31);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 31f), 0, 31);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 31f), 0, 31);
            int a = Mathf.Clamp(Mathf.RoundToInt(c.a * 15f), 0, 15);
            int s = Mathf.Clamp(Mathf.RoundToInt(smooth * 7f), 0, 7);
            int m = Mathf.Clamp(Mathf.RoundToInt(metal * 7f), 0, 7);
            return (r << 24) | (g << 19) | (b << 14) | (a << 10) | (s << 6) | (m << 3) | (emissive ? 1 : 0);
        }

        public static Material GetMat(Color c, float smooth = 0.3f, float metal = 0.1f, Color? emission = null, float emitIntensity = 0f)
        {
            bool emissive = emission.HasValue && emitIntensity > 0f;
            int key = PackKey(c, emissive, smooth, metal);
            if (_matCache.TryGetValue(key, out var m)) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c; m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value * emitIntensity);
            }
            _matCache[key] = m;
            if (_matCache.Count > 256) _matCache.Clear();
            return m;
        }

        private static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, bool keepCollider = false, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // ---- Sandbag wall ----
        public static GameObject AddSandbagWall(Transform parent, Vector3 start, Vector3 end, float height)
        {
            var root = new GameObject("SandbagWall");
            root.transform.SetParent(parent, false);
            Vector3 dir = end - start;
            float length = dir.magnitude;
            if (length < 0.3f) return root;
            dir /= length;
            int segments = Mathf.Max(1, Mathf.RoundToInt(length / 0.45f));
            int rows = Mathf.Max(1, Mathf.RoundToInt(height / 0.22f));
            var lightTan = GetMat(new Color(0.82f, 0.72f, 0.48f));
            var darkTan = GetMat(new Color(0.70f, 0.60f, 0.40f));
            for (int r = 0; r < rows; r++)
            {
                for (int i = 0; i < segments; i++)
                {
                    float t = i / (float)segments + Random.Range(-0.04f, 0.04f);
                    Vector3 p = Vector3.Lerp(start, end, t);
                    p.y += 0.12f + r * 0.22f;
                    var bag = Prim(PrimitiveType.Cube, root.transform, $"Bag_{r}_{i}",
                        p - parent.position,
                        new Vector3(0.45f, 0.2f, 0.3f),
                        ((i + r) % 2 == 0 ? lightTan : darkTan),
                        keepCollider: false,
                        euler: new Vector3(Random.Range(-5f, 5f), Random.Range(-8f, 8f), Random.Range(-3f, 3f)));
                }
            }
            // single collider spanning the wall
            var col = new GameObject("WallCollider");
            col.transform.SetParent(root.transform, false);
            col.transform.position = Vector3.Lerp(start, end, 0.5f) + Vector3.up * height * 0.5f;
            col.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            var bc = col.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.4f, height, length);
            return root;
        }

        // ---- Blast wall ----
        public static GameObject AddBlastWall(Transform parent, Vector3 pos, float yRotation, float length, float height)
        {
            var root = new GameObject("BlastWall");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            var concrete = GetMat(new Color(0.55f, 0.54f, 0.52f), 0.2f, 0.0f);
            var concreteDark = GetMat(new Color(0.42f, 0.41f, 0.39f), 0.2f, 0.0f);
            Prim(PrimitiveType.Cube, root.transform, "Body",
                new Vector3(0f, height * 0.5f, 0f),
                new Vector3(length, height, 0.5f),
                concrete, keepCollider: true);
            Prim(PrimitiveType.Cube, root.transform, "Cap",
                new Vector3(0f, height + 0.1f, 0f),
                new Vector3(length * 0.95f, 0.15f, 0.6f),
                concreteDark, keepCollider: false);
            return root;
        }

        // ---- Barbed wire ----
        public static GameObject AddBarbedWire(Transform parent, Vector3 start, Vector3 end)
        {
            var root = new GameObject("BarbedWire");
            root.transform.SetParent(parent, false);
            Vector3 dir = end - start;
            float length = dir.magnitude;
            if (length < 0.3f) return root;
            dir /= length;
            int posts = Mathf.Max(2, Mathf.RoundToInt(length / 3f));
            var metal = GetMat(new Color(0.3f, 0.3f, 0.3f), 0.5f, 0.7f);
            for (int i = 0; i <= posts; i++)
            {
                float t = i / (float)posts;
                Vector3 p = Vector3.Lerp(start, end, t);
                Prim(PrimitiveType.Cylinder, root.transform, $"Post_{i}",
                    p - parent.position + Vector3.up * 0.75f,
                    new Vector3(0.1f, 0.75f, 0.1f),
                    metal, keepCollider: false);
            }
            // 3 strands
            for (int s = 0; s < 3; s++)
            {
                var go = new GameObject($"Strand_{s}");
                go.transform.SetParent(root.transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = 0.03f; lr.endWidth = 0.03f;
                lr.useWorldSpace = true;
                lr.sharedMaterial = GetMat(new Color(0.25f, 0.25f, 0.25f), 0.4f, 0.6f);
                float h = 0.5f + s * 0.45f;
                lr.SetPosition(0, start + Vector3.up * h);
                lr.SetPosition(1, end + Vector3.up * h);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }

        // ---- Ammo dump ----
        public static GameObject AddAmmoDump(Transform parent, Vector3 pos)
        {
            var root = new GameObject("AmmoDump");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            var crateMat = GetMat(new Color(0.38f, 0.42f, 0.22f), 0.2f, 0.1f);
            var strapMat = GetMat(new Color(0.2f, 0.2f, 0.18f), 0.3f, 0.4f);
            var signMat = GetMat(new Color(0.8f, 0.1f, 0.1f), 0.2f, 0.0f, new Color(1f, 0.1f, 0.05f), 1.5f);
            // 4 stacks
            Vector3[] stackBases = { new Vector3(-2f, 0f, -2f), new Vector3(2f, 0f, -2f), new Vector3(-2f, 0f, 2f), new Vector3(2f, 0f, 2f) };
            for (int s = 0; s < stackBases.Length; s++)
            {
                int h = Random.Range(3, 5);
                for (int y = 0; y < h; y++)
                {
                    Prim(PrimitiveType.Cube, root.transform, $"Crate_{s}_{y}",
                        stackBases[s] + new Vector3(0f, 0.4f + y * 0.8f, 0f),
                        new Vector3(1.2f, 0.7f, 1.4f),
                        crateMat, keepCollider: true,
                        euler: new Vector3(0f, Random.Range(-15f, 15f), 0f));
                    Prim(PrimitiveType.Cube, root.transform, $"CrateStrap_{s}_{y}",
                        stackBases[s] + new Vector3(0f, 0.4f + y * 0.8f, 0f),
                        new Vector3(1.25f, 0.08f, 1.45f), strapMat);
                }
            }
            Prim(PrimitiveType.Cube, root.transform, "HazardSign",
                new Vector3(0f, 1.5f, -3f),
                new Vector3(0.8f, 0.8f, 0.05f),
                signMat, keepCollider: false);
            Prim(PrimitiveType.Cylinder, root.transform, "SignStake",
                new Vector3(0f, 0.75f, -3f),
                new Vector3(0.06f, 0.75f, 0.06f),
                GetMat(new Color(0.4f, 0.3f, 0.2f)));
            root.AddComponent<AmmoDumpDetonator>();
            return root;
        }

        // ---- Fuel depot ----
        public static GameObject AddFuelDepot(Transform parent, Vector3 pos, float scale)
        {
            var root = new GameObject("FuelDepot");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localScale = Vector3.one * scale;
            var tankMat = GetMat(new Color(0.95f, 0.88f, 0.62f), 0.4f, 0.3f);
            var bandMat = GetMat(new Color(0.85f, 0.70f, 0.1f));
            var pad = GetMat(new Color(0.45f, 0.44f, 0.42f));
            var pipeMat = GetMat(new Color(0.55f, 0.55f, 0.55f), 0.5f, 0.5f);
            var signMat = GetMat(new Color(0.8f, 0.1f, 0.1f), 0.2f, 0f, new Color(1f, 0.1f, 0.05f), 2f);

            Prim(PrimitiveType.Cube, root.transform, "Platform",
                new Vector3(0f, 0.1f, 0f),
                new Vector3(9f, 0.2f, 9f),
                pad, keepCollider: true);

            Vector3[] tankPos = { new Vector3(-2.5f, 0f, -2.5f), new Vector3(2.5f, 0f, -2.5f), new Vector3(0f, 0f, 2.8f) };
            var tanks = new List<GameObject>();
            for (int i = 0; i < tankPos.Length; i++)
            {
                var tank = Prim(PrimitiveType.Cylinder, root.transform, $"Tank_{i}",
                    tankPos[i] + Vector3.up * 2.5f,
                    new Vector3(2.2f, 2.5f, 2.2f),
                    tankMat, keepCollider: true);
                tanks.Add(tank);
                Prim(PrimitiveType.Cylinder, root.transform, $"TankBand_{i}",
                    tankPos[i] + Vector3.up * 2.5f,
                    new Vector3(2.25f, 0.2f, 2.25f),
                    bandMat);
                // pipe
                Prim(PrimitiveType.Cube, root.transform, $"Valve_{i}",
                    tankPos[i] + Vector3.up * 0.4f,
                    new Vector3(0.5f, 0.5f, 0.5f),
                    pipeMat, keepCollider: false);
            }
            // Signs
            Prim(PrimitiveType.Cube, root.transform, "FlammableSign1",
                new Vector3(4f, 2f, 0f),
                new Vector3(0.05f, 1f, 1f),
                signMat);
            Prim(PrimitiveType.Cube, root.transform, "FlammableSign2",
                new Vector3(-4f, 2f, 0f),
                new Vector3(0.05f, 1f, 1f),
                signMat);
            var detonator = root.AddComponent<FuelDepotDetonator>();
            detonator.Tanks = tanks;
            return root;
        }

        // ---- Jeep ----
        public static GameObject AddJeep(Transform parent, Vector3 pos, float yRotation)
        {
            var root = new GameObject("Jeep");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            var body = GetMat(new Color(0.38f, 0.42f, 0.22f), 0.25f, 0.2f);
            var tire = GetMat(new Color(0.12f, 0.12f, 0.12f), 0.1f, 0.0f);
            var glass = GetMat(new Color(0.2f, 0.3f, 0.4f), 0.8f, 0.3f);
            var head = GetMat(new Color(1f, 1f, 0.9f), 0.5f, 0f, new Color(1f, 1f, 0.9f), 1.5f);

            Prim(PrimitiveType.Cube, root.transform, "Hull",
                new Vector3(0f, 0.7f, 0f),
                new Vector3(2f, 0.9f, 3.5f),
                body, keepCollider: true);
            Prim(PrimitiveType.Cube, root.transform, "Windshield",
                new Vector3(0f, 1.5f, 0.7f),
                new Vector3(2f, 0.6f, 0.05f),
                glass);
            Prim(PrimitiveType.Cylinder, root.transform, "GunMount",
                new Vector3(0f, 1.4f, -0.7f),
                new Vector3(0.25f, 0.4f, 0.25f),
                body);
            Prim(PrimitiveType.Cylinder, root.transform, "GunBarrel",
                new Vector3(0f, 1.9f, 0.2f),
                new Vector3(0.12f, 0.6f, 0.12f),
                GetMat(new Color(0.2f, 0.2f, 0.22f)),
                euler: new Vector3(90f, 0f, 0f));

            float[] wx = { -0.95f, 0.95f }; float[] wz = { -1.2f, 1.2f };
            for (int a = 0; a < 2; a++)
                for (int b = 0; b < 2; b++)
                {
                    Prim(PrimitiveType.Cylinder, root.transform, $"Wheel_{a}_{b}",
                        new Vector3(wx[a], 0.4f, wz[b]),
                        new Vector3(0.45f, 0.2f, 0.45f),
                        tire, euler: new Vector3(0f, 0f, 90f));
                }
            Prim(PrimitiveType.Sphere, root.transform, "HeadL",
                new Vector3(-0.6f, 0.85f, 1.75f), new Vector3(0.25f, 0.25f, 0.25f), head);
            Prim(PrimitiveType.Sphere, root.transform, "HeadR",
                new Vector3(0.6f, 0.85f, 1.75f), new Vector3(0.25f, 0.25f, 0.25f), head);
            return root;
        }

        // ---- Military truck ----
        public static GameObject AddMilitaryTruck(Transform parent, Vector3 pos, float yRotation)
        {
            var root = new GameObject("MilitaryTruck");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            var body = GetMat(new Color(0.34f, 0.38f, 0.24f), 0.25f, 0.2f);
            var canvas = GetMat(new Color(0.28f, 0.31f, 0.2f), 0.3f, 0.1f);
            var tire = GetMat(new Color(0.12f, 0.12f, 0.12f));
            var glass = GetMat(new Color(0.2f, 0.3f, 0.4f), 0.8f, 0.3f);

            Prim(PrimitiveType.Cube, root.transform, "Cab", new Vector3(0f, 1.1f, 1.5f), new Vector3(2.3f, 1.4f, 2f), body, keepCollider: true);
            Prim(PrimitiveType.Cube, root.transform, "Windshield", new Vector3(0f, 1.6f, 2.5f), new Vector3(2.2f, 0.8f, 0.05f), glass);
            Prim(PrimitiveType.Cube, root.transform, "Bed", new Vector3(0f, 1.1f, -1.3f), new Vector3(2.4f, 1f, 3.5f), body, keepCollider: true);
            Prim(PrimitiveType.Cube, root.transform, "Canopy", new Vector3(0f, 2f, -1.3f), new Vector3(2.3f, 0.7f, 3.4f), canvas);

            float[] wx = { -1.1f, 1.1f };
            for (int a = 0; a < 2; a++)
            {
                Prim(PrimitiveType.Cylinder, root.transform, $"FrontWheel_{a}",
                    new Vector3(wx[a], 0.5f, 1.8f), new Vector3(0.55f, 0.25f, 0.55f),
                    tire, euler: new Vector3(0f, 0f, 90f));
                Prim(PrimitiveType.Cylinder, root.transform, $"RearWheelA_{a}",
                    new Vector3(wx[a], 0.5f, -1.5f), new Vector3(0.55f, 0.25f, 0.55f),
                    tire, euler: new Vector3(0f, 0f, 90f));
                Prim(PrimitiveType.Cylinder, root.transform, $"RearWheelB_{a}",
                    new Vector3(wx[a], 0.5f, -2.3f), new Vector3(0.55f, 0.25f, 0.55f),
                    tire, euler: new Vector3(0f, 0f, 90f));
            }
            return root;
        }

        // ---- Radar dish ----
        public static GameObject AddRadarDish(Transform parent, Vector3 pos, float scale)
        {
            var root = new GameObject("RadarDish");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localScale = Vector3.one * scale;
            var metal = GetMat(new Color(0.55f, 0.55f, 0.55f), 0.5f, 0.5f);
            var dark = GetMat(new Color(0.3f, 0.3f, 0.3f));
            Prim(PrimitiveType.Cylinder, root.transform, "Pedestal",
                new Vector3(0f, 1f, 0f), new Vector3(0.5f, 1f, 0.5f), dark, keepCollider: true);

            var spinner = new GameObject("Spinner").transform;
            spinner.SetParent(root.transform, false);
            spinner.localPosition = new Vector3(0f, 2.2f, 0f);
            spinner.gameObject.AddComponent<RadarSpin>();

            Prim(PrimitiveType.Cube, spinner, "Yoke",
                new Vector3(0f, 0f, 0f), new Vector3(0.4f, 0.2f, 1.2f), metal);
            var dish = Prim(PrimitiveType.Sphere, spinner, "Dish",
                new Vector3(0f, 0.3f, 0.5f), new Vector3(2.5f, 0.6f, 2.5f), metal);
            dish.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
            Prim(PrimitiveType.Sphere, root.transform, "Blinker",
                new Vector3(0f, 0.3f, 0f), new Vector3(0.15f, 0.15f, 0.15f),
                GetMat(new Color(1f, 0.1f, 0.1f), 0.5f, 0f, new Color(1f, 0.1f, 0.05f), 3f));
            return root;
        }

        // ---- Comms tower ----
        public static GameObject AddCommsTower(Transform parent, Vector3 pos, float height)
        {
            var root = new GameObject("CommsTower");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            var metal = GetMat(new Color(0.35f, 0.35f, 0.35f), 0.4f, 0.6f);
            float radius = Mathf.Max(0.5f, height * 0.08f);
            // 4 tapered legs
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                Vector3 footXZ = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                Vector3 topXZ = footXZ * 0.3f;
                Vector3 mid = (footXZ + topXZ) * 0.5f + Vector3.up * height * 0.5f;
                var leg = Prim(PrimitiveType.Cylinder, root.transform, $"Leg_{i}",
                    mid, new Vector3(0.15f, height * 0.5f, 0.15f),
                    metal);
                Vector3 axis = (Vector3.up * height + (topXZ - footXZ)).normalized;
                leg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            }
            // cross-braces every 3m
            int braces = Mathf.Max(1, Mathf.FloorToInt(height / 3f));
            for (int b = 0; b < braces; b++)
            {
                float y = (b + 1) * (height / (braces + 1));
                float rAtY = Mathf.Lerp(radius, radius * 0.3f, y / height);
                Prim(PrimitiveType.Cylinder, root.transform, $"Brace_{b}_H",
                    new Vector3(0f, y, 0f),
                    new Vector3(rAtY * 2f, 0.06f, 0.06f),
                    metal, euler: new Vector3(0f, 0f, 90f));
                Prim(PrimitiveType.Cylinder, root.transform, $"Brace_{b}_V",
                    new Vector3(0f, y, 0f),
                    new Vector3(0.06f, 0.06f, rAtY * 2f),
                    metal, euler: new Vector3(90f, 0f, 0f));
            }
            Prim(PrimitiveType.Cylinder, root.transform, "Spire",
                new Vector3(0f, height + 1f, 0f),
                new Vector3(0.1f, 1f, 0.1f), metal);
            var blink = Prim(PrimitiveType.Sphere, root.transform, "Aviation",
                new Vector3(0f, height + 2.2f, 0f),
                new Vector3(0.3f, 0.3f, 0.3f),
                GetMat(new Color(1f, 0.1f, 0.05f), 0.5f, 0f, new Color(1f, 0.1f, 0.05f), 3f));
            blink.AddComponent<CommsTowerBlink>();
            return root;
        }

        // ---- Guard tower ----
        public static GameObject AddGuardTower(Transform parent, Vector3 pos, float yRotation)
        {
            var root = new GameObject("GuardTower");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            var wood = GetMat(new Color(0.45f, 0.35f, 0.25f));
            var roof = GetMat(new Color(0.3f, 0.2f, 0.15f));
            // 4 posts
            float px = 1.5f;
            for (int a = 0; a < 2; a++)
                for (int b = 0; b < 2; b++)
                    Prim(PrimitiveType.Cylinder, root.transform, $"Post_{a}_{b}",
                        new Vector3((a == 0 ? -px : px), 2f, (b == 0 ? -px : px)),
                        new Vector3(0.25f, 2f, 0.25f), wood, keepCollider: true);
            // platform
            Prim(PrimitiveType.Cube, root.transform, "Platform",
                new Vector3(0f, 4.1f, 0f),
                new Vector3(3.5f, 0.2f, 3.5f), wood, keepCollider: true);
            // walls
            float wh = 0.9f;
            Prim(PrimitiveType.Cube, root.transform, "WallN", new Vector3(0f, 4.1f + wh * 0.5f, px), new Vector3(3.5f, wh, 0.15f), wood, keepCollider: true);
            Prim(PrimitiveType.Cube, root.transform, "WallS", new Vector3(0f, 4.1f + wh * 0.5f, -px), new Vector3(3.5f, wh, 0.15f), wood, keepCollider: true);
            Prim(PrimitiveType.Cube, root.transform, "WallE", new Vector3(px, 4.1f + wh * 0.5f, 0f), new Vector3(0.15f, wh, 3.5f), wood, keepCollider: true);
            Prim(PrimitiveType.Cube, root.transform, "WallW", new Vector3(-px, 4.1f + wh * 0.5f, 0f), new Vector3(0.15f, wh, 3.5f), wood, keepCollider: true);
            // roof (pitched-pyramid approx: one slanted plane)
            Prim(PrimitiveType.Cube, root.transform, "RoofA",
                new Vector3(0f, 5.6f, 0f),
                new Vector3(4f, 0.2f, 4f),
                roof, euler: new Vector3(15f, 0f, 0f));
            // ladder
            var rail = GetMat(new Color(0.3f, 0.2f, 0.15f));
            Prim(PrimitiveType.Cylinder, root.transform, "LadderRailL",
                new Vector3(-0.3f, 2f, 1.7f), new Vector3(0.08f, 2f, 0.08f), rail);
            Prim(PrimitiveType.Cylinder, root.transform, "LadderRailR",
                new Vector3(0.3f, 2f, 1.7f), new Vector3(0.08f, 2f, 0.08f), rail);
            for (int r = 0; r < 5; r++)
            {
                Prim(PrimitiveType.Cylinder, root.transform, $"Rung_{r}",
                    new Vector3(0f, 0.5f + r * 0.7f, 1.7f),
                    new Vector3(0.6f, 0.04f, 0.04f),
                    rail, euler: new Vector3(0f, 0f, 90f));
            }
            return root;
        }

        // ---- Helipad ----
        public static GameObject AddHelipad(Transform parent, Vector3 pos, float yRotation)
        {
            var root = new GameObject("Helipad");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            var concrete = GetMat(new Color(0.4f, 0.4f, 0.4f));
            var paint = GetMat(new Color(1f, 0.95f, 0.2f), 0.2f, 0f, new Color(1f, 0.9f, 0.1f), 1.2f);
            var edge = GetMat(new Color(0.8f, 0.2f, 0.1f));
            Prim(PrimitiveType.Cylinder, root.transform, "Pad",
                new Vector3(0f, 0.15f, 0f),
                new Vector3(16f, 0.15f, 16f),
                concrete, keepCollider: true);
            // H
            Prim(PrimitiveType.Cube, root.transform, "HLeft",
                new Vector3(-2f, 0.32f, 0f), new Vector3(0.8f, 0.05f, 5f), paint);
            Prim(PrimitiveType.Cube, root.transform, "HRight",
                new Vector3(2f, 0.32f, 0f), new Vector3(0.8f, 0.05f, 5f), paint);
            Prim(PrimitiveType.Cube, root.transform, "HCross",
                new Vector3(0f, 0.32f, 0f), new Vector3(4.8f, 0.05f, 0.8f), paint);
            // 8 edge lights
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Prim(PrimitiveType.Sphere, root.transform, $"EdgeLight_{i}",
                    new Vector3(Mathf.Cos(a) * 7.5f, 0.4f, Mathf.Sin(a) * 7.5f),
                    new Vector3(0.25f, 0.25f, 0.25f),
                    GetMat(new Color(1f, 1f, 0.3f), 0.4f, 0f, new Color(1f, 1f, 0.3f), 2f));
            }
            return root;
        }

        // ---- Flag plaza ----
        public static GameObject AddFlagPlaza(Transform parent, Vector3 pos, Color flagColor, string label)
        {
            var root = new GameObject("FlagPlaza");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            var slab = GetMat(new Color(0.55f, 0.54f, 0.52f));
            var pole = GetMat(new Color(0.2f, 0.2f, 0.2f), 0.5f, 0.6f);
            var gold = GetMat(new Color(0.9f, 0.8f, 0.2f), 0.6f, 0.8f);
            var flag = GetMat(flagColor);
            Prim(PrimitiveType.Cube, root.transform, "Slab",
                new Vector3(0f, 0.1f, 0f),
                new Vector3(5f, 0.2f, 5f),
                slab, keepCollider: true);
            Prim(PrimitiveType.Cylinder, root.transform, "Pole",
                new Vector3(0f, 10f, 0f),
                new Vector3(0.25f, 10f, 0.25f),
                pole, keepCollider: true);
            Prim(PrimitiveType.Sphere, root.transform, "Finial",
                new Vector3(0f, 20.2f, 0f),
                new Vector3(0.4f, 0.4f, 0.4f), gold);
            Prim(PrimitiveType.Cube, root.transform, "Flag",
                new Vector3(1.2f, 18f, 0f),
                new Vector3(2.4f, 1.4f, 0.05f), flag);
            // label via TextMesh
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(root.transform, false);
            labelGO.transform.localPosition = new Vector3(0f, 0.3f, 2.3f);
            var tm = labelGO.AddComponent<TextMesh>();
            tm.text = string.IsNullOrEmpty(label) ? "BASE" : label;
            tm.characterSize = 0.1f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.15f, 0.15f, 0.15f);
            return root;
        }

        // ---- Searchlight ----
        public static GameObject AddSearchlight(Transform parent, Vector3 pos, Vector3 lookDirection)
        {
            var root = new GameObject("Searchlight");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            var dark = GetMat(new Color(0.25f, 0.25f, 0.25f), 0.4f, 0.5f);
            Prim(PrimitiveType.Cylinder, root.transform, "Mount",
                new Vector3(0f, 0.5f, 0f), new Vector3(0.6f, 0.5f, 0.6f), dark, keepCollider: true);
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1.2f, 0f);
            if (lookDirection.sqrMagnitude < 0.01f) lookDirection = Vector3.forward;
            head.localRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            Prim(PrimitiveType.Cube, head, "HeadBox",
                new Vector3(0f, 0f, 0.3f), new Vector3(0.5f, 0.5f, 0.6f), dark);
            Prim(PrimitiveType.Sphere, head, "Lens",
                new Vector3(0f, 0f, 0.65f), new Vector3(0.45f, 0.45f, 0.1f),
                GetMat(Color.white, 0.8f, 0.1f, Color.white, 5f));
            var light = head.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.intensity = 8f;
            light.range = 40f;
            light.spotAngle = 22f;
            light.shadows = LightShadows.None;
            head.gameObject.AddComponent<SearchlightSweep>();
            return root;
        }

        // ---- Fuel pipe ----
        public static GameObject AddFuelPipe(Transform parent, Vector3 start, Vector3 end)
        {
            var root = new GameObject("FuelPipe");
            root.transform.SetParent(parent, false);
            Vector3 mid = (start + end) * 0.5f;
            Vector3 dir = end - start;
            float length = dir.magnitude;
            if (length < 0.1f) return root;
            var pipeMat = GetMat(new Color(0.55f, 0.55f, 0.55f), 0.4f, 0.6f);
            var pipe = Prim(PrimitiveType.Cylinder, root.transform, "PipeBody",
                mid - parent.position, new Vector3(0.35f, length * 0.5f, 0.35f), pipeMat, keepCollider: true);
            pipe.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            return root;
        }

        // ---- Concrete bunker ----
        public static GameObject AddConcreteBunker(Transform parent, Vector3 pos, float yRotation)
        {
            var root = new GameObject("ConcreteBunker");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            var concrete = GetMat(new Color(0.52f, 0.5f, 0.48f));
            var slit = GetMat(new Color(0.05f, 0.05f, 0.08f), 0f, 0f, new Color(0.1f, 0.3f, 0.5f), 0.5f);
            Prim(PrimitiveType.Cube, root.transform, "Base",
                new Vector3(0f, 1f, 0f), new Vector3(6f, 2f, 5f), concrete, keepCollider: true);
            Prim(PrimitiveType.Sphere, root.transform, "Dome",
                new Vector3(0f, 2f, 0f), new Vector3(6f, 2f, 5f), concrete, keepCollider: true);
            Prim(PrimitiveType.Cube, root.transform, "ViewSlit",
                new Vector3(0f, 1.3f, 2.51f), new Vector3(3f, 0.25f, 0.02f), slit);
            return root;
        }

        // ---- Missile launcher ----
        public static GameObject AddMissileLauncher(Transform parent, Vector3 pos, float yRotation)
        {
            var root = new GameObject("MissileLauncher");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            var body = GetMat(new Color(0.42f, 0.45f, 0.28f), 0.25f, 0.2f);
            var tube = GetMat(new Color(0.35f, 0.35f, 0.35f), 0.4f, 0.5f);
            var tip = GetMat(new Color(0.85f, 0.7f, 0.1f));
            var light = GetMat(new Color(0.1f, 0.9f, 0.15f), 0.5f, 0f, new Color(0.1f, 1f, 0.15f), 3f);
            Prim(PrimitiveType.Cube, root.transform, "Chassis",
                new Vector3(0f, 0.6f, 0f), new Vector3(4f, 0.6f, 3f), body, keepCollider: true);
            // 6 wheels
            float[] wx = { -1.6f, 1.6f };
            float[] wz = { -1.1f, 0f, 1.1f };
            var tire = GetMat(new Color(0.12f, 0.12f, 0.12f));
            for (int a = 0; a < 2; a++)
                for (int b = 0; b < 3; b++)
                    Prim(PrimitiveType.Cylinder, root.transform, $"Wheel_{a}_{b}",
                        new Vector3(wx[a], 0.3f, wz[b]), new Vector3(0.4f, 0.2f, 0.4f),
                        tire, euler: new Vector3(0f, 0f, 90f));
            // rail pivot angled up
            var rail = new GameObject("Rail").transform;
            rail.SetParent(root.transform, false);
            rail.localPosition = new Vector3(0f, 1.1f, 0f);
            rail.localRotation = Quaternion.Euler(-45f, 0f, 0f);
            // 4 tubes
            for (int i = 0; i < 4; i++)
            {
                float xo = (i - 1.5f) * 0.6f;
                var t = Prim(PrimitiveType.Cylinder, rail, $"Tube_{i}",
                    new Vector3(xo, 0f, 1f), new Vector3(0.3f, 1f, 0.3f), tube);
                t.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Prim(PrimitiveType.Sphere, rail, $"TubeTip_{i}",
                    new Vector3(xo, 1f, 1f), new Vector3(0.3f, 0.3f, 0.3f), tip);
            }
            // targeting box
            Prim(PrimitiveType.Cube, root.transform, "TargetingBox",
                new Vector3(-2.2f, 1f, 0f), new Vector3(0.6f, 0.6f, 1f), body);
            Prim(PrimitiveType.Sphere, root.transform, "TargetingLight",
                new Vector3(-2.2f, 1.4f, 0f), new Vector3(0.2f, 0.2f, 0.2f), light);
            root.AddComponent<MissileLauncherFire>();
            return root;
        }
    }

    // ---- MonoBehaviours ----

    internal class RadarSpin : MonoBehaviour
    {
        public float yawDegPerSec = 25f;
        private void Update() { transform.Rotate(0f, yawDegPerSec * Time.deltaTime, 0f, Space.Self); }
    }

    internal class SearchlightSweep : MonoBehaviour
    {
        public float amplitude = 45f;
        public float period = 6f;
        private Quaternion _base;
        private float _phase;
        private void Awake() { _base = transform.localRotation; _phase = Random.Range(0f, 10f); }
        private void Update()
        {
            float yaw = Mathf.Sin((Time.time + _phase) / period * Mathf.PI * 2f) * amplitude;
            transform.localRotation = _base * Quaternion.Euler(0f, yaw, 0f);
        }
    }

    internal class CommsTowerBlink : MonoBehaviour
    {
        private Material _mat;
        private Color _base = new Color(1f, 0.1f, 0.05f);
        private MaterialPropertyBlock _mpb;
        private Renderer _rend;
        private void Awake()
        {
            _rend = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }
        private void Update()
        {
            if (_rend == null) return;
            float t = Mathf.Sin(Time.time * Mathf.PI) * 0.5f + 0.5f;
            _rend.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissionColor", _base * (0.5f + t * 2.5f));
            _rend.SetPropertyBlock(_mpb);
        }
    }

    internal class AmmoDumpDetonator : MonoBehaviour
    {
        public int health = 150;
        private bool _detonating;
        public void TakeDamage(int amount)
        {
            if (_detonating) return;
            health -= amount;
            if (health <= 0) StartCoroutine(Detonate());
        }
        private System.Collections.IEnumerator Detonate()
        {
            _detonating = true;
            Vector3 p = transform.position;
            VFXManager.BigExplosion(p + Vector3.up, 2f);
            VFXManager.LargeFlames(p + Vector3.up, 1.8f);
            float t = 0f;
            while (t < 3f)
            {
                VFXManager.SmallExplosion(p + Random.insideUnitSphere * 3f, Random.Range(0.6f, 1.2f));
                yield return new WaitForSeconds(Random.Range(0.08f, 0.22f));
                t += 0.18f;
            }
            VFXManager.BigExplosion(p + Vector3.up * 2f, 2.5f);
            Destroy(gameObject);
        }
    }

    internal class FuelDepotDetonator : MonoBehaviour
    {
        public int health = 250;
        public List<GameObject> Tanks;
        private bool _detonating;
        public void TakeDamage(int amount)
        {
            if (_detonating) return;
            health -= amount;
            if (health <= 0) StartCoroutine(Detonate());
        }
        private System.Collections.IEnumerator Detonate()
        {
            _detonating = true;
            Vector3 p = transform.position;
            VFXManager.BigExplosion(p + Vector3.up * 2f, 2.5f);
            VFXManager.LargeFlames(p + Vector3.up, 2f);
            if (Tanks != null)
            {
                for (int i = 0; i < Tanks.Count; i++)
                {
                    if (Tanks[i] == null) continue;
                    yield return new WaitForSeconds(Random.Range(0.35f, 0.65f));
                    VFXManager.BigExplosion(Tanks[i].transform.position, 2f);
                    VFXManager.LargeFlames(Tanks[i].transform.position, 2.5f);
                    Destroy(Tanks[i]);
                }
            }
            float t = 0f;
            while (t < 8f)
            {
                VFXManager.SmallExplosion(p + Random.insideUnitSphere * 5f, Random.Range(0.8f, 1.4f));
                yield return new WaitForSeconds(Random.Range(0.2f, 0.5f));
                t += 0.35f;
            }
            VFXManager.BigExplosion(p, 3f);
            Destroy(gameObject);
        }
    }

    internal class MissileLauncherFire : MonoBehaviour
    {
        public float minInterval = 15f;
        public float maxInterval = 25f;
        private float _nextAt;
        private void Start() { _nextAt = Time.time + Random.Range(minInterval, maxInterval); }
        private void Update()
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + Random.Range(minInterval, maxInterval);
            LaunchMissile();
        }
        private void LaunchMissile()
        {
            var m = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m.name = "AmbientMissile";
            m.transform.position = transform.position + Vector3.up * 2f;
            m.transform.localScale = new Vector3(0.22f, 0.7f, 0.22f);
            Object.DestroyImmediate(m.GetComponent<Collider>());
            var r = m.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = BaseDetailProps.GetMat(new Color(0.85f, 0.7f, 0.1f), 0.5f, 0.3f, new Color(1f, 0.6f, 0.1f), 1.5f);
            var arc = m.AddComponent<AmbientMissileArc>();
            Vector3 dir = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(-30f, 30f), 0f) * Vector3.up;
            arc.velocity = dir * 35f;
            arc.lifespan = Random.Range(4f, 5.5f);
            var tr = m.AddComponent<TrailRenderer>();
            tr.time = 1f;
            tr.startWidth = 0.35f; tr.endWidth = 0.02f;
            tr.material = BaseDetailProps.GetMat(new Color(1f, 0.8f, 0.3f));
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    internal class AmbientMissileArc : MonoBehaviour
    {
        public Vector3 velocity;
        public float gravity = 2f;
        public float lifespan = 5f;
        private float _t;
        private void Update()
        {
            _t += Time.deltaTime;
            velocity += Vector3.down * gravity * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            if (velocity.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.FromToRotation(Vector3.up, velocity.normalized);
            if (_t >= lifespan)
            {
                VFXManager.BigExplosion(transform.position, 1.5f);
                Destroy(gameObject);
            }
        }
    }
}
