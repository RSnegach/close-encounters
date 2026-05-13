using System;
using System.Reflection;
using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Composite builder that produces a full, dense military base at a given
    /// location. Uses BaseDetailProps for individual prop construction and
    /// ShoreTurret (via reflection) for turret emplacements so this compiles
    /// cleanly even if those siblings change signatures.
    /// </summary>
    public static class MilitaryBaseBuilder
    {
        public struct BasePalette
        {
            public Color wall;
            public Color roof;
            public Color accent;
            public Color flag;
            public string label;
        }

        public static BasePalette IranianPalette() => new BasePalette
        {
            wall = new Color(0.72f, 0.65f, 0.45f),
            roof = new Color(0.35f, 0.30f, 0.25f),
            accent = new Color(0.08f, 0.55f, 0.20f),
            flag = new Color(0.08f, 0.55f, 0.20f),
            label = "Iranian Base",
        };

        public static BasePalette OmaniPalette() => new BasePalette
        {
            wall = new Color(0.92f, 0.88f, 0.82f),
            roof = new Color(0.55f, 0.20f, 0.15f),
            accent = new Color(0.75f, 0.12f, 0.12f),
            flag = new Color(0.75f, 0.12f, 0.12f),
            label = "Omani Base",
        };

        public static BasePalette HormuzIslandPalette() => new BasePalette
        {
            wall = new Color(0.60f, 0.58f, 0.52f),
            roof = new Color(0.25f, 0.22f, 0.20f),
            accent = new Color(0.85f, 0.70f, 0.30f),
            flag = new Color(0.95f, 0.95f, 0.95f),
            label = "Hormuz Fort",
        };

        public static GameObject Build(Transform parent, Vector3 center, float yaw,
            BasePalette palette, float scale, int turretCount, bool hasHelipad)
        {
            scale = Mathf.Max(0.1f, scale);
            var root = new GameObject("MilitaryBase_" + palette.label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = center;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            root.transform.localScale = Vector3.one * scale;

            BuildBuildings(root.transform, palette);
            BuildPerimeter(root.transform, palette);
            BuildUtility(root.transform, palette);
            BuildVehicles(root.transform);
            BuildTurrets(root.transform, turretCount);
            BuildMissileLaunchers(root.transform);
            BuildDecor(root.transform, palette, hasHelipad);

            return root;
        }

        // ---- buildings ----
        private static void BuildBuildings(Transform root, BasePalette palette)
        {
            var wallMat = BaseDetailProps.GetMat(palette.wall);
            var roofMat = BaseDetailProps.GetMat(palette.roof);
            var window = BaseDetailProps.GetMat(new Color(0.55f, 0.75f, 0.95f), 0.6f, 0f, new Color(0.55f, 0.75f, 1f), 1.2f);
            var doorMat = BaseDetailProps.GetMat(palette.accent);

            // HQ — 3-story
            var hq = PrimInline(PrimitiveType.Cube, root, "HQ",
                new Vector3(0f, 5f, 0f), new Vector3(16f, 10f, 12f), wallMat, true);
            PrimInline(PrimitiveType.Cube, hq.transform, "HQRoof",
                new Vector3(0f, 0.55f, 0f), new Vector3(1.03f, 0.1f, 1.05f), roofMat);
            // trim bands
            for (int t = 0; t < 3; t++)
            {
                PrimInline(PrimitiveType.Cube, hq.transform, $"HQBand_{t}",
                    new Vector3(0f, -0.4f + t * 0.3f, 0f), new Vector3(1.02f, 0.04f, 1.02f), BaseDetailProps.GetMat(palette.accent));
            }
            // windows
            for (int y = 0; y < 3; y++)
                for (int x = -2; x <= 2; x++)
                {
                    PrimInline(PrimitiveType.Cube, hq.transform, $"HQWin_{y}_{x}",
                        new Vector3(x * 0.2f, -0.25f + y * 0.25f, 0.51f),
                        new Vector3(0.08f, 0.1f, 0.02f), window);
                }
            PrimInline(PrimitiveType.Cube, hq.transform, "HQDoor",
                new Vector3(0f, -0.4f, 0.51f), new Vector3(0.15f, 0.2f, 0.02f), doorMat);

            // Barracks × 3
            for (int b = 0; b < 3; b++)
            {
                float zOff = 22f + b * 6f;
                var barrack = PrimInline(PrimitiveType.Cube, root, $"Barracks_{b}",
                    new Vector3(-18f, 2.5f, zOff), new Vector3(14f, 5f, 5f), wallMat, true);
                PrimInline(PrimitiveType.Cube, barrack.transform, "Roof",
                    new Vector3(0f, 0.55f, 0f), new Vector3(1.02f, 0.12f, 1.05f), roofMat);
                for (int w = -3; w <= 3; w++)
                    PrimInline(PrimitiveType.Cube, barrack.transform, $"Win_{w}",
                        new Vector3(w * 0.14f, 0.1f, 0.51f),
                        new Vector3(0.08f, 0.16f, 0.02f), window);
            }

            // Hangar
            var hangar = PrimInline(PrimitiveType.Cube, root, "Hangar",
                new Vector3(22f, 4f, -12f), new Vector3(16f, 8f, 20f), wallMat, true);
            PrimInline(PrimitiveType.Cube, hangar.transform, "HangarRoof",
                new Vector3(0f, 0.52f, 0f), new Vector3(1.03f, 0.08f, 1.05f), roofMat);
            PrimInline(PrimitiveType.Cube, hangar.transform, "HangarDoor",
                new Vector3(0f, -0.2f, 0.51f), new Vector3(0.6f, 0.7f, 0.02f),
                BaseDetailProps.GetMat(new Color(0.15f, 0.15f, 0.15f)));

            // Warehouse
            var warehouse = PrimInline(PrimitiveType.Cube, root, "Warehouse",
                new Vector3(-24f, 3f, -14f), new Vector3(12f, 6f, 10f), wallMat, true);
            PrimInline(PrimitiveType.Cube, warehouse.transform, "Roof",
                new Vector3(0f, 0.52f, 0f), new Vector3(1.02f, 0.08f, 1.04f), roofMat);
        }

        // ---- perimeter ----
        private static void BuildPerimeter(Transform root, BasePalette palette)
        {
            // Blast-wall U around the base, opening toward -Z (water side)
            BaseDetailProps.AddBlastWall(root, new Vector3(-35f, 0f, 0f), 90f, 60f, 3.5f);
            BaseDetailProps.AddBlastWall(root, new Vector3(35f, 0f, 0f), 90f, 60f, 3.5f);
            BaseDetailProps.AddBlastWall(root, new Vector3(0f, 0f, 35f), 0f, 70f, 3.5f);
            // Sandbag reinforcements at corners
            BaseDetailProps.AddSandbagWall(root, root.TransformPoint(new Vector3(-35f, 0f, -32f)), root.TransformPoint(new Vector3(-35f, 0f, -20f)), 1f);
            BaseDetailProps.AddSandbagWall(root, root.TransformPoint(new Vector3(35f, 0f, -32f)), root.TransformPoint(new Vector3(35f, 0f, -20f)), 1f);
            // Barbed wire on outer edges
            BaseDetailProps.AddBarbedWire(root, root.TransformPoint(new Vector3(-36f, 3.5f, -32f)), root.TransformPoint(new Vector3(-36f, 3.5f, 32f)));
            BaseDetailProps.AddBarbedWire(root, root.TransformPoint(new Vector3(36f, 3.5f, -32f)), root.TransformPoint(new Vector3(36f, 3.5f, 32f)));
            BaseDetailProps.AddBarbedWire(root, root.TransformPoint(new Vector3(-36f, 3.5f, 32f)), root.TransformPoint(new Vector3(36f, 3.5f, 32f)));
            // Guard towers at 4 corners
            BaseDetailProps.AddGuardTower(root, new Vector3(-35f, 0f, 33f), 45f);
            BaseDetailProps.AddGuardTower(root, new Vector3(35f, 0f, 33f), -45f);
            BaseDetailProps.AddGuardTower(root, new Vector3(-35f, 0f, -18f), 135f);
            BaseDetailProps.AddGuardTower(root, new Vector3(35f, 0f, -18f), -135f);
        }

        // ---- utility ----
        private static void BuildUtility(Transform root, BasePalette palette)
        {
            BaseDetailProps.AddRadarDish(root, new Vector3(-26f, 8f, -8f), 1.0f);
            BaseDetailProps.AddRadarDish(root, new Vector3(26f, 8f, 10f), 1.2f);
            BaseDetailProps.AddCommsTower(root, new Vector3(-18f, 0f, 30f), 22f);
            BaseDetailProps.AddCommsTower(root, new Vector3(18f, 0f, -30f), 18f);
            BaseDetailProps.AddFuelDepot(root, new Vector3(-32f, 0f, 28f), 1f);
            BaseDetailProps.AddAmmoDump(root, new Vector3(30f, 0f, 28f));
            BaseDetailProps.AddFuelPipe(root, root.TransformPoint(new Vector3(-32f, 0.5f, 28f)), root.TransformPoint(new Vector3(22f, 0.5f, -12f)));
            BaseDetailProps.AddConcreteBunker(root, new Vector3(12f, 0f, 22f), 0f);
        }

        // ---- vehicles ----
        private static void BuildVehicles(Transform root)
        {
            // Motor pool slab
            PrimInline(PrimitiveType.Cube, root, "MotorPool",
                new Vector3(-10f, 0.1f, -22f), new Vector3(22f, 0.2f, 10f),
                BaseDetailProps.GetMat(new Color(0.35f, 0.33f, 0.3f)), false);
            // Jeeps
            for (int i = 0; i < 5; i++)
                BaseDetailProps.AddJeep(root, new Vector3(-16f + i * 3.5f, 0.3f, -22f), 90f);
            // Trucks
            BaseDetailProps.AddMilitaryTruck(root, new Vector3(-12f, 0.3f, -26f), 0f);
            BaseDetailProps.AddMilitaryTruck(root, new Vector3(-6f, 0.3f, -26f), 0f);
            BaseDetailProps.AddMilitaryTruck(root, new Vector3(0f, 0.3f, -26f), 0f);
        }

        // ---- turrets ----
        private static void BuildTurrets(Transform root, int turretCount)
        {
            Vector3[] slots = {
                new Vector3(-20f, 0f, -34f),
                new Vector3(20f, 0f, -34f),
                new Vector3(0f, 0f, -36f),
                new Vector3(-32f, 0f, -18f),
                new Vector3(32f, 0f, -18f),
                new Vector3(-32f, 0f, 12f),
                new Vector3(32f, 0f, 12f),
                new Vector3(-8f, 0f, 34f),
                new Vector3(8f, 0f, 34f),
                new Vector3(-28f, 0f, 34f),
                new Vector3(28f, 0f, 34f),
                new Vector3(0f, 0f, 36f),
            };
            int n = Mathf.Min(turretCount, slots.Length);
            var padMat = BaseDetailProps.GetMat(new Color(0.45f, 0.44f, 0.42f));
            var turretType = System.Type.GetType("CloseEncounters.Arena.ShoreTurret, Assembly-CSharp")
                ?? System.Type.GetType("CloseEncounters.Arena.ShoreTurret");
            for (int i = 0; i < n; i++)
            {
                var mount = new GameObject($"TurretMount_{i}");
                mount.transform.SetParent(root, false);
                mount.transform.localPosition = slots[i];
                PrimInline(PrimitiveType.Cylinder, mount.transform, "Pad",
                    new Vector3(0f, 0.2f, 0f), new Vector3(3.5f, 0.2f, 3.5f), padMat, true);
                // small sandbag ring (4 walls)
                BaseDetailProps.AddSandbagWall(mount.transform,
                    mount.transform.TransformPoint(new Vector3(-1.5f, 0f, -1.5f)),
                    mount.transform.TransformPoint(new Vector3(1.5f, 0f, -1.5f)), 0.6f);
                BaseDetailProps.AddSandbagWall(mount.transform,
                    mount.transform.TransformPoint(new Vector3(-1.5f, 0f, 1.5f)),
                    mount.transform.TransformPoint(new Vector3(1.5f, 0f, 1.5f)), 0.6f);
                var turretGO = new GameObject("Turret");
                turretGO.transform.SetParent(mount.transform, false);
                turretGO.transform.localPosition = new Vector3(0f, 0.4f, 0f);
                if (turretType != null)
                {
                    try { turretGO.AddComponent(turretType); }
                    catch (Exception e) { Debug.LogWarning($"[MilitaryBaseBuilder] couldn't add ShoreTurret: {e.Message}"); }
                }
            }
        }

        private static void BuildMissileLaunchers(Transform root)
        {
            BaseDetailProps.AddMissileLauncher(root, new Vector3(-10f, 0.7f, 30f), 10f);
            BaseDetailProps.AddMissileLauncher(root, new Vector3(10f, 0.7f, 30f), -10f);
        }

        // ---- decor ----
        private static void BuildDecor(Transform root, BasePalette palette, bool hasHelipad)
        {
            BaseDetailProps.AddFlagPlaza(root, new Vector3(0f, 0f, -8f), palette.flag, palette.label);
            if (hasHelipad)
                BaseDetailProps.AddHelipad(root, new Vector3(0f, 0f, -32f), 0f);
            // Searchlights on building corners
            Vector3[] slPositions = {
                new Vector3(-8f, 10f, 0f),
                new Vector3(8f, 10f, 0f),
                new Vector3(-8f, 10f, 6f),
                new Vector3(8f, 10f, 6f),
                new Vector3(-22f, 5f, 22f),
                new Vector3(22f, 5f, -16f),
            };
            for (int i = 0; i < slPositions.Length; i++)
            {
                Vector3 look = new Vector3(-slPositions[i].x, -0.3f, -slPositions[i].z).normalized;
                BaseDetailProps.AddSearchlight(root, slPositions[i], look);
            }
            // Pole lights along walkway
            for (int i = 0; i < 8; i++)
            {
                float x = -28f + i * 8f;
                PrimInline(PrimitiveType.Cylinder, root, $"PoleLight_{i}",
                    new Vector3(x, 3f, 10f), new Vector3(0.15f, 3f, 0.15f),
                    BaseDetailProps.GetMat(new Color(0.2f, 0.2f, 0.2f)), false);
                PrimInline(PrimitiveType.Sphere, root, $"PoleLamp_{i}",
                    new Vector3(x, 6.2f, 10f), new Vector3(0.4f, 0.4f, 0.4f),
                    BaseDetailProps.GetMat(new Color(1f, 0.95f, 0.7f), 0.3f, 0f, new Color(1f, 0.9f, 0.5f), 2f), false);
            }
        }

        // ---- util ----
        private static GameObject PrimInline(PrimitiveType type, Transform parent, string name,
            Vector3 pos, Vector3 scale, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
    }
}
