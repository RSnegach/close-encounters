using UnityEngine;
using CloseEncounters.Combat;

namespace CloseEncounters.Arena
{
    // =========================================================================
    // 1. GroundDesert -- Albuquerque: vast desert with canyons, oasis,
    //    desert settlement, dust devils, and low-poly breakable props.
    //    Assets: Mountains Canyons Cliffs, Desert Buildings, EZ Tornado,
    //    Tiny Teacup Low-Poly Desert, 3D Environment Desert Pack.
    // =========================================================================

    public class GroundDesert : ArenaBase
    {
        public override string ArenaName => "Albuquerque Desert";

        public override void Build()
        {
            // ── Terrain ────────────────────────────────────────────
            // why: 750x750 gives ~2.5x old playable area (600x600); offset -375 keeps origin-centred
            var terrain = TerrainFactory.Create(transform,
                new Vector3(-375f, 0f, -375f), new Vector3(750f, 60f, 750f),
                513, "DesertTerrain");

            TerrainFactory.SetHeights(terrain, (nx, nz) =>
            {
                float h = 0.01f; // flat desert floor

                // Large mesa NW (arena landmark)
                float dx1 = nx - 0.25f, dz1 = nz - 0.78f;
                float d1 = Mathf.Sqrt(dx1 * dx1 + dz1 * dz1);
                if (d1 < 0.10f)
                    h = Mathf.Max(h, 0.20f * Mathf.SmoothStep(1f, 0f, d1 / 0.10f));

                // Mesa SE
                float dx2 = nx - 0.78f, dz2 = nz - 0.30f;
                float d2 = Mathf.Sqrt(dx2 * dx2 + dz2 * dz2);
                if (d2 < 0.08f)
                    h = Mathf.Max(h, 0.16f * Mathf.SmoothStep(1f, 0f, d2 / 0.08f));

                // Small mesa S-center
                float dx3 = nx - 0.45f, dz3 = nz - 0.22f;
                float d3 = Mathf.Sqrt(dx3 * dx3 + dz3 * dz3);
                if (d3 < 0.06f)
                    h = Mathf.Max(h, 0.12f * Mathf.SmoothStep(1f, 0f, d3 / 0.06f));

                // Ridge along east edge
                float ridgeDist = Mathf.Abs(nx - 0.88f);
                if (ridgeDist < 0.05f)
                    h = Mathf.Max(h, 0.10f * Mathf.SmoothStep(1f, 0f, ridgeDist / 0.05f));

                // Oasis depression at center-north
                float odx = nx - 0.50f, odz = nz - 0.62f;
                float oasisDist = Mathf.Sqrt(odx * odx + odz * odz);
                if (oasisDist < 0.05f)
                    h = Mathf.Min(h, -0.005f); // sunken pool

                // Dune ripples + detail noise
                h += 0.006f * Mathf.PerlinNoise(nx * 10f, nz * 10f);
                h += 0.008f * Mathf.PerlinNoise(nx * 20f, nz * 20f);
                h += 0.003f * Mathf.PerlinNoise(nx * 50f + 100f, nz * 50f + 100f);
                h += 0.001f * Mathf.PerlinNoise(nx * 120f + 200f, nz * 120f + 200f);

                return Mathf.Max(0f, h);
            });

            // Flatten oasis and settlement areas
            TerrainFactory.Flatten(terrain, 0.42f, 0.58f, 0.58f, 0.68f, 0.005f); // oasis pool area
            TerrainFactory.Flatten(terrain, 0.55f, 0.50f, 0.72f, 0.62f, 0.01f);  // settlement east of oasis

            // ── Splatmap ───────────────────────────────────────────
            TerrainFactory.PaintSplatmap(terrain, (nx, nz, height, steepness) =>
            {
                float[] w = new float[16];
                if (steepness > 30f)
                {
                    w[7] = 1f; // Rock on cliffs
                }
                else if (height > 0.08f)
                {
                    w[9] = 0.5f; w[7] = 0.5f; // Mesa tops
                }
                else
                {
                    float noise = Mathf.PerlinNoise(nx * 8f + 100f, nz * 8f + 100f);
                    // Oasis area gets some green
                    float odx = nx - 0.50f, odz = nz - 0.62f;
                    float oasisProx = Mathf.Sqrt(odx * odx + odz * odz);
                    if (oasisProx < 0.08f)
                    {
                        float green = 1f - oasisProx / 0.08f;
                        w[0] = 0.3f * green;  // GrassA near oasis
                        w[2] = 0.2f * green;  // GrassDry
                        w[4] = 0.5f * (1f - green); // Sand
                    }
                    else
                    {
                        w[4]  = 0.6f + 0.3f * noise;  // Sand
                        w[10] = 0.3f - 0.2f * noise;  // SoilRocks
                        w[2]  = 0.1f;                  // GrassDry sparse
                    }
                }
                return w;
            });

            // ── District builders ──────────────────────────────────
            BuildCanyonRim();
            BuildOasis();
            BuildSettlement();
            BuildDesertFloor();
            BuildStormEffects();

            // ── Spawn on cliff/mesa tops ───────────────────────────
            // Place spawn points on elevated terrain (mesas) so players
            // start with a commanding view and drive down into the arena
            // why: spawn ring pushed outward to ~250 to match expanded arena; mesa tops kept but on outer ring
            AddSpawnPoints(
                new Vector3(-230f, 12f, 220f),
                new Vector3(-150f, 12f, 240f),
                new Vector3( 230f, 10f,-200f),
                new Vector3( 160f, 10f,-230f),
                new Vector3( -50f,  8f,-250f),
                new Vector3(  30f,  8f,-240f),
                new Vector3(-260f,  3f,   0f),
                new Vector3( 260f,  3f,   0f)
            );
            AddInvisibleWalls(375f, 50f);

            // ── Desert warm sun ─────────────────────────────────────
            var desertSun = new GameObject("DesertSun");
            desertSun.transform.SetParent(transform, false);
            var dsl = desertSun.AddComponent<Light>();
            dsl.type = LightType.Directional;
            dsl.color = new Color(1f, 0.88f, 0.68f);
            dsl.intensity = 1.15f;
            dsl.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
            dsl.shadows = LightShadows.Soft;

            // ── Atmosphere ──────────────────────────────────────────
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.86f, 0.74f, 0.55f); // warm dust
            RenderSettings.fogDensity = 0.0028f; // why: slightly lighter — bigger arena should feel open
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.78f, 0.70f, 0.55f);

            VFXManager.DustStorm(Vector3.zero, 8f);
            VFXManager.SandSwirls(new Vector3(0, 1, 0), 5f);
            VFXManager.HeatDistortion(new Vector3(0, 2, 0), 4f);
            VFXManager.DustMotes(new Vector3(0, 3, 0), 6f);
            VFXManager.SandSwirls(new Vector3(-180f, 1f, 120f), 4f);
            VFXManager.SandSwirls(new Vector3( 200f, 1f,-140f), 4f);
        }

        // ── CANYON RIM: mountain/canyon prefabs around arena edges ──
        private void BuildCanyonRim()
        {
            // why: rim pushed to ~340 to hug the new 375 half-extent wall
            // ── North wall (z = 300..340) ── 5 canyon mountains
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-240f, 0f, 330f), 0f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(-110f, 0f, 340f), 45f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(  30f, 0f, 320f), 90f, 13f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3( 170f, 0f, 335f), 135f, 10f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3( 290f, 0f, 325f), 200f, 11f);

            // ── South wall ── 5 canyon mountains
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-220f, 0f, -330f), 180f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3( -70f, 0f, -345f), 135f, 13f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(  90f, 0f, -325f), 90f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3( 230f, 0f, -340f), 45f, 10f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-300f, 0f, -320f), 225f, 11f);

            // ── East wall ── 4 canyon mountains
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(340f, 0f, -160f), 90f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(325f, 0f,  -30f), 135f, 13f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(345f, 0f,  110f), 45f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(330f, 0f,  230f), 70f, 10f);

            // ── West wall ── 4 canyon mountains
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(-335f, 0f, -160f), 0f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(-345f, 0f,  -30f), 90f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-325f, 0f,  110f), 180f, 13f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-340f, 0f,  240f), 225f, 10f);

            // ── Corner cliff details for continuity ──
            DesertPrefabHelper.PlaceLowPoly(transform, "CliffCorner_01",
                new Vector3( 320f, 0f,  310f), 45f, 16f);
            DesertPrefabHelper.PlaceLowPoly(transform, "CliffCorner_02",
                new Vector3(-320f, 0f,  310f), 135f, 15f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Cliff_01",
                new Vector3( 320f, 0f, -315f), 0f, 14f);
            DesertPrefabHelper.PlaceLowPoly(transform, "CliffCorner_01",
                new Vector3(-320f, 0f, -315f), 90f, 16f);
        }

        // ── OASIS: water feature + palm trees + rocks ──────────────
        private void BuildOasis()
        {
            Vector3 oasisCenter = new Vector3(0f, 0f, 75f);

            // ── Deep water pool (Fentchester canal style) ──────────
            {
                var waterGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                waterGO.name = "OasisWater";
                waterGO.transform.SetParent(transform, false);
                waterGO.transform.position = new Vector3(0f, -0.5f, 75f);
                waterGO.transform.localScale = new Vector3(50f, 1f, 50f);
                Object.DestroyImmediate(waterGO.GetComponent<Collider>());

                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Smoothness", 0.92f);
                mat.SetFloat("_Metallic", 0.1f);
                mat.color = new Color(0.10f, 0.35f, 0.55f, 0.55f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                SetMaterial(waterGO, mat);
            }
            AddWaterHazard(new Vector3(0f, -0.3f, 75f), new Vector3(48f, 4f, 48f), "OasisHazard");

            // ── Palm trees spread wide (radius 30-45) ──────────────
            AddTree(new Vector3(-35f, 1f, 110f),  9f, 3.5f, "Palm_1");
            AddTree(new Vector3(38f,  1f, 105f),  8f, 3.0f, "Palm_2");
            AddTree(new Vector3(-40f, 1f, 70f),   7f, 2.5f, "Palm_3");
            AddTree(new Vector3(35f,  1f, 45f),  10f, 4.0f, "Palm_4");
            AddTree(new Vector3(-15f, 1f, 40f),   8f, 3.5f, "Palm_5");
            AddTree(new Vector3(42f,  1f, 80f),   9f, 3.0f, "Palm_6");
            AddTree(new Vector3(-8f,  1f, 115f),  7f, 3.0f, "Palm_7");
            AddTree(new Vector3(10f,  1f, 35f),   8f, 3.0f, "Palm_8");

            // ── Rocks spread wider (radius 30-40) ──────────────────
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_01", new Vector3(-30f, 1f, 108f), 0f,   1.5f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_02", new Vector3(32f,  1f, 100f), 45f,  1.2f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_03", new Vector3(-38f, 1f, 85f),  120f, 1.4f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_04", new Vector3(36f,  1f, 55f),  200f, 1.8f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_05", new Vector3(-12f, 1f, 38f),  90f,  1.0f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_01", new Vector3(18f,  1f, 42f),  270f, 1.6f);

            // ── Vegetation spread wider ────────────────────────────
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertGrass_01", new Vector3(-28f, 1f, 105f), 0f,  1.5f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertGrass_02", new Vector3(25f,  1f, 98f),  30f, 1.5f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertGrass_01", new Vector3(-32f, 1f, 58f),  150f,1.2f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertGrass_02", new Vector3(30f,  1f, 50f),  220f,1.3f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertTree", new Vector3(-45f, 1f, 90f), 45f,  1.2f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertTree", new Vector3(40f,  1f, 55f), 180f, 1.1f);

            // ── Cacti ring (radius 50-60) ──────────────────────────
            DesertPrefabHelper.PlaceLowPoly(transform, "Cactus_01", new Vector3(-55f, 1f, 120f), 0f,   2.5f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Cactus_02", new Vector3(55f,  1f, 110f), 90f,  2.5f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Cactus_03", new Vector3(-50f, 1f, 40f),  180f, 2.5f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Cactus_01", new Vector3(50f,  1f, 35f),  270f, 2.5f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Cactus_02", new Vector3(-15f, 1f, 25f),  45f,  2.5f);
        }

        // ── SETTLEMENT: desert buildings cluster ────────────────────
        private void BuildSettlement()
        {
            // ── Settlement buildings (x=30..130, z=0..75) ──────────
            // Place all 5 Desert Building variants around a central plaza (~x=80, z=40)
            // Place all 5 building variants -- scale 2x for visibility, y=2 above terrain
            var b1 = DesertPrefabHelper.PlaceBuilding(transform, "Desert_Building_V1",
                new Vector3(60f, 2f, 55f), 15f, 2.0f);
            if (b1 == null) Debug.LogWarning("[GroundDesert] Desert_Building_V1 failed to load!");
            var b2 = DesertPrefabHelper.PlaceBuilding(transform, "Desert_Building_V2",
                new Vector3(95f, 2f, 60f), 210f, 2.0f);
            if (b2 == null) Debug.LogWarning("[GroundDesert] Desert_Building_V2 failed to load!");
            var b3 = DesertPrefabHelper.PlaceBuilding(transform, "Desert_Building_V3",
                new Vector3(115f, 2f, 35f), 120f, 2.0f);
            if (b3 == null) Debug.LogWarning("[GroundDesert] Desert_Building_V3 failed to load!");
            var b4 = DesertPrefabHelper.PlaceBuilding(transform, "Desert_Building_V4",
                new Vector3(85f, 2f, 15f), 275f, 2.0f);
            if (b4 == null) Debug.LogWarning("[GroundDesert] Desert_Building_V4 failed to load!");
            var b5 = DesertPrefabHelper.PlaceBuilding(transform, "Desert_Building_V5",
                new Vector3(45f, 2f, 25f), 340f, 2.0f);
            if (b5 == null) Debug.LogWarning("[GroundDesert] Desert_Building_V5 failed to load!");

            // ── Lookout platforms near settlement ────────────────────
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertPlatform_01",
                new Vector3(130f, 0f, 10f), 45f, 1.5f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertPlatform_01",
                new Vector3(35f, 0f, 70f), 200f, 1.5f);

            // ── Ancient adobe ruins SW of settlement ─────────────────
            Color ruin = new Color(0.65f, 0.55f, 0.40f);
            AddBlock(new Vector3(-80f, 3f, -60f), new Vector3(14f, 6f, 14f), ruin, "Ruin_Base");
            AddBlock(new Vector3(-80f, 8f, -60f), new Vector3(10f, 4f, 10f), ruin, "Ruin_Upper");
            AddBlock(new Vector3(-90f, 2f, -45f), new Vector3(5f, 4f, 8f), ruin, "Ruin_Wall_1");
            AddBlock(new Vector3(-66f, 1.5f, -68f), new Vector3(6f, 3f, 5f), ruin, "Ruin_Wall_2");
            AddBlock(new Vector3(-72f, 1f, -75f), new Vector3(8f, 2f, 4f), ruin, "Ruin_Wall_3");

            // ── Desert cave south of ruins ───────────────────────────
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertCave",
                new Vector3(-120f, 0f, -100f), 30f, 2f);
        }

        // ── DESERT FLOOR: scattered rocks, cacti, vegetation ────────
        private void BuildDesertFloor()
        {
            // Exclusion zones:
            //   Oasis:     roughly x=-30..30, z=55..95
            //   Settlement: x=25..135, z=-5..80
            //   Spawn ring: radius 200 from origin (keep 8+ units away)

            string[] envRocks  = { "DesertRock_01", "DesertRock_02", "DesertRock_03" };
            string[] lpRocks   = { "Rock_01", "Rock_02", "Rock_03", "Rock_04", "Rock_05" };
            string[] envCacti  = { "Cactus_01", "Cactus_02", "Cactus_03", "Cactus_04" };
            string[] lpCacti   = { "Cactus_01", "Cactus_02", "Cactus_03" };
            string[] mountains = { "DesertMountain_01", "DesertMountain_02", "DesertMountain_03" };
            string[] grasses   = { "DesertGrass_01", "DesertGrass_02" };

            // why: 14 clusters (was 8) spread into new outer ring 200-320 to avoid empty expansion zones
            Vector2[] clusterCenters =
            {
                new Vector2(-120f, -80f),
                new Vector2(-160f,  40f),
                new Vector2( -70f, -140f),
                new Vector2(  60f, -120f),
                new Vector2( 150f, -60f),
                new Vector2(-100f,  130f),
                new Vector2( 140f,  110f),
                new Vector2( -20f, -50f),
                new Vector2(-260f,  180f),
                new Vector2( 270f,   60f),
                new Vector2( 220f, -220f),
                new Vector2(-280f, -180f),
                new Vector2(  60f,  250f),
                new Vector2(-190f, -250f),
            };

            foreach (var center in clusterCenters)
            {
                int count = Random.Range(3, 5); // 3-4 rocks per cluster
                for (int i = 0; i < count; i++)
                {
                    float ox = center.x + Random.Range(-8f, 8f);
                    float oz = center.y + Random.Range(-8f, 8f);
                    float rot = Random.Range(0f, 360f);
                    float scl = Random.Range(0.5f, 2.0f);

                    // Alternate between 3D Environment and Low-Poly rocks
                    if (Random.value > 0.5f)
                    {
                        string rock = envRocks[Random.Range(0, envRocks.Length)];
                        DesertPrefabHelper.PlaceDesertProp(transform, rock,
                            new Vector3(ox, 1f, oz), rot, scl);
                    }
                    else
                    {
                        string rock = lpRocks[Random.Range(0, lpRocks.Length)];
                        DesertPrefabHelper.PlaceLowPoly(transform, rock,
                            new Vector3(ox, 1f, oz), rot, scl);
                    }
                }
            }

            // why: 36 cacti (was 22) over the larger range to keep density up in the 2.5x arena
            int cactiPlaced = 0;
            int cactiTarget = 36;
            int cactiAttempts = 0;
            while (cactiPlaced < cactiTarget && cactiAttempts < 400)
            {
                cactiAttempts++;
                float cx = Random.Range(-330f, 330f);
                float cz = Random.Range(-330f, 330f);

                // Skip oasis zone
                if (cx > -30f && cx < 30f && cz > 55f && cz < 95f) continue;
                // Skip settlement zone
                if (cx > 25f && cx < 135f && cz > -5f && cz < 80f) continue;
                // Skip spawn ring proximity
                float dist = Mathf.Sqrt(cx * cx + cz * cz);
                if (dist > 192f && dist < 208f) continue;

                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(2.0f, 3.5f);

                // Mix all 7 cactus variants (4 env + 3 low-poly)
                if (Random.value > 0.43f)
                {
                    string cactus = envCacti[Random.Range(0, envCacti.Length)];
                    DesertPrefabHelper.PlaceDesertProp(transform, cactus,
                        new Vector3(cx, 1f, cz), rot, scl);
                }
                else
                {
                    string cactus = lpCacti[Random.Range(0, lpCacti.Length)];
                    DesertPrefabHelper.PlaceLowPoly(transform, cactus,
                        new Vector3(cx, 1f, cz), rot, scl);
                }
                cactiPlaced++;
            }

            // ── Desert mountains (8 peaks for medium cover — doubled, outer pushed farther) ────
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[0],
                new Vector3(-180f, 1f, -20f), Random.Range(0f, 360f), 2.5f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[1],
                new Vector3(170f, 1f, 140f), Random.Range(0f, 360f), 2.0f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[2],
                new Vector3(-140f, 1f, -160f), Random.Range(0f, 360f), 1.8f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[0],
                new Vector3(100f, 1f, -150f), Random.Range(0f, 360f), 2.2f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[1],
                new Vector3(-280f, 1f,  220f), Random.Range(0f, 360f), 2.8f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[2],
                new Vector3( 290f, 1f,  230f), Random.Range(0f, 360f), 2.4f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[0],
                new Vector3(-260f, 1f, -280f), Random.Range(0f, 360f), 2.6f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[1],
                new Vector3( 250f, 1f, -300f), Random.Range(0f, 360f), 2.3f);

            // ── Grass tufts (28 total, near rock clusters and cacti) ───────
            for (int i = 0; i < 28; i++)
            {
                // Place grass near a random cluster center with offset
                var nearCluster = clusterCenters[Random.Range(0, clusterCenters.Length)];
                float gx = nearCluster.x + Random.Range(-20f, 20f);
                float gz = nearCluster.y + Random.Range(-20f, 20f);
                float scl = Random.Range(1.0f, 2.0f);
                string grass = grasses[Random.Range(0, grasses.Length)];

                DesertPrefabHelper.PlaceDesertProp(transform, grass,
                    new Vector3(gx, 1f, gz), Random.Range(0f, 360f), scl);
            }

            // ── Trees (9 total, scattered across the map, not near oasis) ──
            Vector3[] treePositions =
            {
                new Vector3(-100f, 1f,  -90f),
                new Vector3( 160f, 1f,  -30f),
                new Vector3(-170f, 1f,  100f),
                new Vector3(  80f, 1f, -170f),
                new Vector3( -50f, 1f, -160f),
                new Vector3( 240f, 1f,  180f),
                new Vector3(-240f, 1f, -110f),
                new Vector3( 190f, 1f, -260f),
                new Vector3(-260f, 1f,  260f),
            };

            for (int i = 0; i < treePositions.Length; i++)
            {
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.0f, 1.5f);

                // Alternate between DesertTree and low-poly Tree_01
                if (i % 2 == 0)
                    DesertPrefabHelper.PlaceDesertProp(transform, "DesertTree",
                        treePositions[i], rot, scl);
                else
                    DesertPrefabHelper.PlaceLowPoly(transform, "Tree_01",
                        treePositions[i], rot, scl);
            }
        }

        // ── STORM EFFECTS: Habrador physics tornado ──────
        private void BuildStormEffects()
        {
            var tornadoSpawner = new GameObject("HabradorTornadoSpawner");
            tornadoSpawner.transform.SetParent(transform, false);
            tornadoSpawner.transform.localPosition = Vector3.zero;
            var ts = tornadoSpawner.AddComponent<HabradorTornadoSpawner>();
            ts.prefabPath = "HabradorTornado/TornadoPrefab";
            ts.scale = 1f;
            ts.spawnRadius = 180f;
            ts.minInterval = 60f;
            ts.maxInterval = 60f;
            ts.minLifetime = 40f;
            ts.maxLifetime = 55f;
            ts.maxActive = 1;
        }
    }

    // =========================================================================
    // 2. GroundTown -- Fentchester: massive city with canal, districts,
    //    crashed fighters in skyscrapers, and POLYGON City Pack buildings.
    //    Road grid: E-W at z=55,115,-55,-115. N-S at x=-140,-60,0,60,140.
    //    Canal: z=-15..15. Bridges at x=-140,-60,0,60,140.
    // =========================================================================

    public class GroundTown : ArenaBase
    {
        public override string ArenaName => "Fentchester";

        // Shared road-grid constants (normalized 0-1 for terrain 600x600 at origin -300)
        // World z=55  → nz = (55+300)/600 = 0.592
        // World z=115 → nz = 0.692
        // World z=-55 → nz = 0.408
        // World z=-115→ nz = 0.308
        // World x=-140→ nx = 0.267, x=-60→0.400, x=0→0.500, x=60→0.600, x=140→0.733
        // Canal: z=-15..15 → nz = 0.475..0.525

        public override void Build()
        {
            // ── Terrain ────────────────────────────────────────────
            // why: 750x750 for ~2.5x playable area; normalized block flatten coords still line up on centre roads
            var terrain = TerrainFactory.Create(transform,
                new Vector3(-375f, 0f, -375f), new Vector3(750f, 50f, 750f),
                513, "CityTerrain");

            TerrainFactory.SetHeights(terrain, (nx, nz) =>
            {
                float h = 0.02f; // flat city base (~1 world unit)
                h += 0.003f * Mathf.PerlinNoise(nx * 8f, nz * 8f); // subtle variation

                // Canal cut at nz ~0.5 (world z=0)
                float canalDist = Mathf.Abs(nz - 0.5f);
                if (canalDist < 0.025f)
                    h = 0.0f; // canal floor
                else if (canalDist < 0.04f)
                {
                    float t = (canalDist - 0.025f) / 0.015f;
                    h = Mathf.Lerp(0f, h, t * t); // steep bank
                }

                return Mathf.Max(0f, h);
            });

            // Flatten all city blocks where buildings will sit
            // North blocks: z=55..115 (nz 0.592..0.692)
            TerrainFactory.Flatten(terrain, 0.267f, 0.592f, 0.400f, 0.692f, 0.02f); // block x:-140..-60
            TerrainFactory.Flatten(terrain, 0.400f, 0.592f, 0.500f, 0.692f, 0.02f); // block x:-60..0
            TerrainFactory.Flatten(terrain, 0.500f, 0.592f, 0.600f, 0.692f, 0.02f); // block x:0..60
            TerrainFactory.Flatten(terrain, 0.600f, 0.592f, 0.733f, 0.692f, 0.02f); // block x:60..140
            // Far north: z=115..180 (nz 0.692..0.800)
            TerrainFactory.Flatten(terrain, 0.267f, 0.692f, 0.733f, 0.800f, 0.02f);
            // South blocks: z=-115..-55 (nz 0.308..0.408)
            TerrainFactory.Flatten(terrain, 0.267f, 0.308f, 0.400f, 0.408f, 0.02f);
            TerrainFactory.Flatten(terrain, 0.400f, 0.308f, 0.500f, 0.408f, 0.02f);
            TerrainFactory.Flatten(terrain, 0.500f, 0.308f, 0.600f, 0.408f, 0.02f);
            TerrainFactory.Flatten(terrain, 0.600f, 0.308f, 0.733f, 0.408f, 0.02f);
            // Far south: z=-180..-115 (nz 0.200..0.308)
            TerrainFactory.Flatten(terrain, 0.267f, 0.200f, 0.733f, 0.308f, 0.02f);

            // ── Splatmap ───────────────────────────────────────────
            TerrainFactory.PaintSplatmap(terrain, (nx, nz, height, steepness) =>
            {
                float[] w = new float[16];

                // Road grid detection (normalized coordinates)
                float[] ewRoads = { 0.592f, 0.692f, 0.408f, 0.308f };
                float[] nsRoads = { 0.267f, 0.400f, 0.500f, 0.600f, 0.733f };
                float roadHalf = 0.008f; // ~5 world units half-width

                bool onRoad = false;
                for (int r = 0; r < ewRoads.Length; r++)
                    if (Mathf.Abs(nz - ewRoads[r]) < roadHalf) onRoad = true;
                for (int r = 0; r < nsRoads.Length; r++)
                    if (Mathf.Abs(nx - nsRoads[r]) < roadHalf) onRoad = true;

                // Canal zone
                bool inCanal = Mathf.Abs(nz - 0.5f) < 0.04f;

                if (onRoad)
                {
                    w[8] = 0.85f; // Muddy = asphalt
                    w[9] = 0.15f; // PebblesA = gravel edge
                }
                else if (inCanal)
                {
                    w[8]  = 0.4f;
                    w[12] = 0.4f; // GrassSoil
                    w[9]  = 0.2f;
                }
                else
                {
                    float noise = Mathf.PerlinNoise(nx * 6f, nz * 6f);
                    w[0] = 0.6f + 0.2f * noise;  // GrassA
                    w[1] = 0.2f - 0.1f * noise;  // GrassB
                    w[9] = 0.15f;                 // PebblesA
                    w[12] = 0.05f;                // GrassSoil
                }
                return w;
            });

            // ── Canal water ────────────────────────────────────────
            {
                var waterGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                waterGO.name = "CanalWater";
                waterGO.transform.SetParent(transform, false);
                waterGO.transform.position = new Vector3(0f, 0.5f, 0f);
                waterGO.transform.localScale = new Vector3(560f, 0.2f, 24f);
                Object.DestroyImmediate(waterGO.GetComponent<Collider>());

                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Smoothness", 0.92f);
                mat.SetFloat("_Metallic", 0.1f);
                mat.color = new Color(0.12f, 0.38f, 0.48f, 0.5f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                SetMaterial(waterGO, mat);
            }
            AddWaterHazard(new Vector3(0f, 0.3f, 0f), new Vector3(560f, 3f, 24f), "CanalHazard");

            // ── Bridges (5 crossings) ──────────────────────────────
            Color bridgeStone = new Color(0.50f, 0.48f, 0.45f);
            AddBridge(new Vector3(-140f, 1.2f, -14f), new Vector3(-140f, 1.2f, 14f), 10f, 1f, bridgeStone, "Bridge_FarW");
            AddBridge(new Vector3(-60f, 1.2f, -14f), new Vector3(-60f, 1.2f, 14f), 10f, 1f, bridgeStone, "Bridge_W");
            AddBridge(new Vector3(0f, 1.2f, -14f), new Vector3(0f, 1.2f, 14f), 10f, 1f, bridgeStone, "Bridge_Center");
            AddBridge(new Vector3(60f, 1.2f, -14f), new Vector3(60f, 1.2f, 14f), 10f, 1f, bridgeStone, "Bridge_E");
            AddBridge(new Vector3(140f, 1.2f, -14f), new Vector3(140f, 1.2f, 14f), 10f, 1f, bridgeStone, "Bridge_FarE");
            BuildCanalDetail();

            // ── City districts (filled by dedicated builder methods) ──
            BuildDowntown();
            BuildCommercial();
            BuildResidentialSouth();
            BuildIndustrial();
            BuildCrashSites();
            BuildOutskirts();
            BuildWaterfront();

            // ── City-wide props: trees, furniture, street objects ───
            BuildCityProps();

            // ── Spawn points — pushed outward into new outskirts (avoids canal z=-15..15) ──
            AddSpawnPoints(
                new Vector3(-250f, 1f,  120f),
                new Vector3( 250f, 1f,  120f),
                new Vector3(-250f, 1f, -120f),
                new Vector3( 250f, 1f, -120f),
                new Vector3(   0f, 1f,  260f),
                new Vector3(   0f, 1f, -260f),
                new Vector3(-200f, 1f,  240f),
                new Vector3( 200f, 1f, -240f),
                new Vector3(-300f, 1f,    60f),
                new Vector3( 300f, 1f,   -60f)
            );
            AddInvisibleWalls(375f, 45f);

            // ── City key light (overcast) ──────────────────────────
            var cityLight = new GameObject("CitySun");
            cityLight.transform.SetParent(transform, false);
            var cl = cityLight.AddComponent<Light>();
            cl.type = LightType.Directional;
            cl.color = new Color(0.92f, 0.94f, 1f);
            cl.intensity = 0.85f;
            cl.transform.rotation = Quaternion.Euler(50f, -20f, 0f);
            cl.shadows = LightShadows.Soft;

            // ── Atmosphere — cool urban overcast ───────────────────
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.58f, 0.62f, 0.68f);
            RenderSettings.fogDensity = 0.0035f; // why: slightly thinner given larger view distance
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.58f, 0.60f, 0.66f);

            VFXManager.DustMotes(new Vector3(0, 4, 0), 5f);
            VFXManager.GroundFog(Vector3.zero, 4f);
            VFXManager.GroundFog(new Vector3(-220f, 1f,  200f), 3f);
            VFXManager.GroundFog(new Vector3( 220f, 1f, -200f), 3f);
            VFXManager.DustMotes(new Vector3(0, 4, 250f), 4f);
            VFXManager.DustMotes(new Vector3(0, 4,-250f), 4f);
        }

        // ── DOWNTOWN: x=-60..60, z=55..115 ─────────────────────
        // Tallest buildings, dense commercial core.
        private void BuildDowntown()
        {
            // ── West block buildings (x=-55..-4, z=58..112) ──────────
            // Row near south road (z ~62-75)
            CityPrefabHelper.PlaceBuilding(transform, "Building_I_1_prefab",
                new Vector3(-40f, 1f, 65f), 0f, 1.1f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_J_prefab",
                new Vector3(-18f, 1f, 65f), 90f, 1.0f);

            // Center of west block (z ~82-95)
            CityPrefabHelper.PlaceBuilding(transform, "Building_K_prefab",
                new Vector3(-42f, 1f, 85f), 0f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_O_PREFAB",
                new Vector3(-20f, 1f, 88f), 180f, 0.9f);

            // Row near north road (z ~100-110)
            CityPrefabHelper.PlaceBuilding(transform, "Bank_prefab",
                new Vector3(-30f, 1f, 106f), 180f, 1.0f);

            // ── East block buildings (x=4..55, z=58..112) ────────────
            // Row near south road
            CityPrefabHelper.PlaceBuilding(transform, "Building_I_2_Prefab",
                new Vector3(18f, 1f, 65f), 0f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_M_prefab",
                new Vector3(42f, 1f, 66f), 270f, 1.1f);

            // Center of east block
            CityPrefabHelper.PlaceBuilding(transform, "Building_I_3_prefab",
                new Vector3(15f, 1f, 87f), 90f, 1.2f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_N_Prefab",
                new Vector3(40f, 1f, 85f), 0f, 1.0f);

            // Row near north road
            CityPrefabHelper.PlaceBuilding(transform, "Police_station_prefab",
                new Vector3(28f, 1f, 106f), 180f, 1.0f);

            // ── Street lamps along downtown roads ────────────────────
            // South road edge (z=58)
            for (int x = -50; x <= 50; x += 25)
                CityPrefabHelper.PlaceLamp(transform, "street lamp 2 prefab",
                    new Vector3(x, 1f, 58f), 0f);

            // North road edge (z=112)
            for (int x = -50; x <= 50; x += 25)
                CityPrefabHelper.PlaceLamp(transform, "street lamp 2 prefab",
                    new Vector3(x, 1f, 112f), 180f);

            // Center road edges (x=-4 and x=4)
            for (int z = 62; z <= 108; z += 23)
            {
                CityPrefabHelper.PlaceLamp(transform, "street lamp 2 prefab",
                    new Vector3(-5f, 1f, z), 90f);
                CityPrefabHelper.PlaceLamp(transform, "street lamp 2 prefab",
                    new Vector3(5f, 1f, z), 270f);
            }

            // ── Props: trees, benches, trash bins ────────────────────
            // Trees along south sidewalk
            CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                new Vector3(-35f, 1f, 59f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                new Vector3(10f, 1f, 59f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                new Vector3(35f, 1f, 59f));

            // Benches near the bank and police station
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(-22f, 1f, 104f), 0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(18f, 1f, 104f), 0f);

            // Trash bins at block corners
            CityPrefabHelper.PlaceProp(transform, "Bin prefab",
                new Vector3(-52f, 1f, 59f));
            CityPrefabHelper.PlaceProp(transform, "Big_trash_bin prefab",
                new Vector3(52f, 1f, 59f));
            CityPrefabHelper.PlaceProp(transform, "Bin prefab",
                new Vector3(-52f, 1f, 111f));
        }

        // ── COMMERCIAL: x=-200..-60, z=55..180 ─────────────────
        // Shops, bank, police, fire dept, hospital, medium buildings.
        private void BuildCommercial()
        {
            // ── South row, west block (x -190..-148, z 63..107) ────
            CityPrefabHelper.PlaceBuilding(transform, "Shop_A_prefab",
                new Vector3(-185f, 1f, 70f), 0f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Shop_B_prefab",
                new Vector3(-165f, 1f, 70f), 0f, 0.9f);
            CityPrefabHelper.PlaceBuilding(transform, "Hospital_prefab",
                new Vector3(-175f, 1f, 95f), 180f, 1.0f);

            // ── South row, east block (x -132..-68, z 63..107) ─────
            CityPrefabHelper.PlaceBuilding(transform, "Supermaket_prefab",
                new Vector3(-125f, 1f, 70f), 0f, 1.1f);
            CityPrefabHelper.PlaceBuilding(transform, "Shop_C_prefab",
                new Vector3(-100f, 1f, 70f), 0f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Fire_department_prefab",
                new Vector3(-80f, 1f, 70f), 0f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_A_prefab",
                new Vector3(-112f, 1f, 98f), 180f, 0.9f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_D_prefab",
                new Vector3(-88f, 1f, 98f), 180f, 1.0f);

            // ── North row, west block (x -190..-148, z 123..170) ───
            CityPrefabHelper.PlaceBuilding(transform, "Building_B_prefab",
                new Vector3(-185f, 1f, 130f), 0f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_C1_prefab",
                new Vector3(-165f, 1f, 130f), 0f, 0.85f);

            // ── North row, east block: modular G-building + filler ──
            // Three G-parts placed adjacent to form one large structure
            CityPrefabHelper.PlaceBuilding(transform, "Build_G-Left_Prefab",
                new Vector3(-125f, 1f, 155f), 0f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Build_G-middle_Prefab",
                new Vector3(-112f, 1f, 155f), 0f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Build_G-right_Prefab",
                new Vector3(-99f, 1f, 155f), 0f, 1.0f);

            CityPrefabHelper.PlaceBuilding(transform, "Building_F_prefab",
                new Vector3(-78f, 1f, 135f), 90f, 1.0f);

            // ── Street lamps along roads ────────────────────────────
            // Along z=55 road (south edge, north sidewalk)
            for (float x = -180f; x <= -70f; x += 30f)
                CityPrefabHelper.PlaceLamp(transform, "Lamp_3_prefab",
                    new Vector3(x, 1f, 60f));

            // Along z=115 road (middle road, both sidewalks)
            for (float x = -180f; x <= -70f; x += 30f)
            {
                CityPrefabHelper.PlaceLamp(transform, "Lamp_3_prefab",
                    new Vector3(x, 1f, 110f));
                CityPrefabHelper.PlaceLamp(transform, "Lamp_3_prefab",
                    new Vector3(x, 1f, 120f));
            }

            // Along x=-140 road (N-S connector, east sidewalk)
            CityPrefabHelper.PlaceLamp(transform, "Lamp_3_prefab",
                new Vector3(-145f, 1f, 80f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_3_prefab",
                new Vector3(-145f, 1f, 145f));

            // ── Props: hedges between shops ─────────────────────────
            CityPrefabHelper.PlaceProp(transform, "hedge prefab",
                new Vector3(-175f, 1f, 70f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "hedge prefab",
                new Vector3(-112f, 1f, 70f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "hedge prefab",
                new Vector3(-175f, 1f, 130f), 0f, 0.9f);
            CityPrefabHelper.PlaceProp(transform, "hedge prefab",
                new Vector3(-112f, 1f, 155f), 90f, 1.0f);

            // Bus stops along main roads
            CityPrefabHelper.PlaceProp(transform, "Bus_stop_prefab",
                new Vector3(-155f, 1f, 60f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Bus_stop_prefab",
                new Vector3(-95f, 1f, 120f), 180f, 1.0f);

            // Phone booths near hospital and shops
            CityPrefabHelper.PlaceProp(transform, "Phone_booth_prefab",
                new Vector3(-170f, 1f, 88f), 90f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Phone_booth_prefab",
                new Vector3(-92f, 1f, 63f), 0f, 1.0f);
        }

        // ── RESIDENTIAL SOUTH: x=-200..200, z=-180..-55 ────────
        // Smaller houses, motel, trees, suburban feel.
        private void BuildResidentialSouth()
        {
            // ── Buildings: 16 smaller residential structures ──────────
            // Road grid creates blocks between x = {-140, -60, 0, 60, 140}
            // and z = {-55, -115}.  Buildings placed well inside blocks.

            // -- Row 1: z = -75 to -105 (between z=-55 road and z=-115 road) --

            // Block: far west (x -195..-144)
            CityPrefabHelper.PlaceBuilding(transform, "Building_A1_prefab", new Vector3(-170f, 1f, -78f), 0f, 0.85f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_S_prefab",  new Vector3(-170f, 1f, -100f), 180f, 0.80f);

            // Block: x -136..-64
            CityPrefabHelper.PlaceBuilding(transform, "Building_B1_prefab", new Vector3(-110f, 1f, -80f), 90f, 0.90f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_T_prefab",  new Vector3(-85f,  1f, -100f), 0f, 0.85f);

            // Block: x -56..-4
            CityPrefabHelper.PlaceBuilding(transform, "Building_D1_prefab", new Vector3(-35f, 1f, -78f), 0f, 0.90f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_V_prefab",  new Vector3(-30f, 1f, -102f), 270f, 0.85f);

            // Block: x 4..56
            CityPrefabHelper.PlaceBuilding(transform, "Building_W_prefab",  new Vector3(30f, 1f, -80f), 0f, 0.80f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_Y_prefab",  new Vector3(35f, 1f, -100f), 180f, 0.90f);

            // Block: x 64..136
            CityPrefabHelper.PlaceBuilding(transform, "Building_Z_Prefab",  new Vector3(90f, 1f, -78f), 90f, 0.85f);
            CityPrefabHelper.PlaceBuilding(transform, "building_X_prefab",  new Vector3(110f, 1f, -100f), 0f, 0.90f);

            // Block: far east (x 144..195)
            CityPrefabHelper.PlaceBuilding(transform, "Building_p_prefab",  new Vector3(170f, 1f, -82f), 0f, 0.80f);

            // -- Row 2: z = -123 to -170 (south of z=-115 road to district edge) --

            CityPrefabHelper.PlaceBuilding(transform, "Building_u_prefab",  new Vector3(-165f, 1f, -140f), 0f, 0.85f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_A1_prefab", new Vector3(-95f,  1f, -145f), 90f, 0.80f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_B1_prefab", new Vector3(-25f,  1f, -138f), 0f, 0.90f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_S_prefab",  new Vector3(40f,   1f, -142f), 270f, 0.85f);

            // Motel near south-east edge
            CityPrefabHelper.PlaceBuilding(transform, "Motel_prefab",       new Vector3(160f,  1f, -155f), 0f, 0.95f);

            // ── Trees: scattered between buildings (18 trees) ─────────
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(-180f, 1f, -88f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(-155f, 1f, -72f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(-120f, 1f, -95f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(-75f,  1f, -75f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(-50f,  1f, -92f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(-15f,  1f, -70f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(15f,   1f, -88f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(50f,   1f, -72f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(75f,   1f, -95f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(130f,  1f, -75f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(180f,  1f, -92f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(-175f, 1f, -150f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(-120f, 1f, -160f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(-55f,  1f, -148f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(20f,   1f, -155f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(80f,   1f, -135f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(145f,  1f, -165f));
            CityPrefabHelper.PlaceProp(transform, "Tree prefab", new Vector3(105f,  1f, -160f));

            // ── Bushes: along property lines ──────────────────────────
            CityPrefabHelper.PlaceProp(transform, "Bush prefab", new Vector3(-160f, 1f, -85f));
            CityPrefabHelper.PlaceProp(transform, "Bush prefab", new Vector3(-100f, 1f, -88f));
            CityPrefabHelper.PlaceProp(transform, "Bush prefab", new Vector3(-40f,  1f, -85f));
            CityPrefabHelper.PlaceProp(transform, "Bush prefab", new Vector3(25f,   1f, -88f));
            CityPrefabHelper.PlaceProp(transform, "Bush prefab", new Vector3(100f,  1f, -85f));
            CityPrefabHelper.PlaceProp(transform, "Bush prefab", new Vector3(165f,  1f, -88f));
            CityPrefabHelper.PlaceProp(transform, "Bush prefab", new Vector3(-80f,  1f, -150f));
            CityPrefabHelper.PlaceProp(transform, "Bush prefab", new Vector3(55f,   1f, -148f));

            // ── Hedges: defining yards ────────────────────────────────
            CityPrefabHelper.PlaceProp(transform, "hedge prefab", new Vector3(-165f, 1f, -90f), 0f);
            CityPrefabHelper.PlaceProp(transform, "hedge prefab", new Vector3(-105f, 1f, -93f), 90f);
            CityPrefabHelper.PlaceProp(transform, "hedge prefab", new Vector3(-30f,  1f, -90f), 0f);
            CityPrefabHelper.PlaceProp(transform, "hedge prefab", new Vector3(35f,   1f, -93f), 90f);
            CityPrefabHelper.PlaceProp(transform, "hedge prefab", new Vector3(95f,   1f, -90f), 0f);
            CityPrefabHelper.PlaceProp(transform, "hedge prefab", new Vector3(165f,  1f, -93f), 90f);
            CityPrefabHelper.PlaceProp(transform, "hedge prefab", new Vector3(-90f,  1f, -155f), 0f);
            CityPrefabHelper.PlaceProp(transform, "hedge prefab", new Vector3(45f,   1f, -155f), 90f);

            // ── Pot trees: decorative accents ─────────────────────────
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(-35f,  1f, -72f));
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(30f,   1f, -72f));
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(155f,  1f, -148f));
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(-170f, 1f, -130f));

            // ── Benches: seating along streets ────────────────────────
            CityPrefabHelper.PlaceProp(transform, "bench prefab",   new Vector3(-145f, 1f, -62f), 0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab", new Vector3(-65f,  1f, -62f), 0f);
            CityPrefabHelper.PlaceProp(transform, "bench prefab",   new Vector3(5f,    1f, -62f), 0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab", new Vector3(65f,   1f, -62f), 0f);
            CityPrefabHelper.PlaceProp(transform, "bench prefab",   new Vector3(145f,  1f, -62f), 0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab", new Vector3(-100f, 1f, -120f), 0f);
            CityPrefabHelper.PlaceProp(transform, "bench prefab",   new Vector3(50f,   1f, -120f), 0f);

            // ── Street lamps: along roads ─────────────────────────────
            // Along z=-55 road (north edge of district)
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(-170f, 1f, -50f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(-100f, 1f, -50f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(-30f,  1f, -50f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(30f,   1f, -50f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(100f,  1f, -50f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(170f,  1f, -50f));

            // Along z=-115 road (middle crossing)
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(-170f, 1f, -110f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(-100f, 1f, -110f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(-30f,  1f, -110f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(30f,   1f, -110f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(100f,  1f, -110f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(170f,  1f, -110f));

            // Along north-south streets (x=-140, x=-60, x=0, x=60, x=140)
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(-144f, 1f, -85f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(-64f,  1f, -85f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(-4f,   1f, -85f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(64f,   1f, -85f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_1_prefab", new Vector3(144f,  1f, -85f));
        }

        // ── INDUSTRIAL: x=60..200, z=55..180 ───────────────────
        // Gas station, car repair, warehouses, parking, water tank.
        private void BuildIndustrial()
        {
            // ── South row: x=68..132, z=62..108 (between roads z=55 and z=115) ──

            // Gas station at south-west corner of industrial zone
            CityPrefabHelper.PlaceBuilding(transform, "Gas_station_A_PREFAB",
                new Vector3(75f, 1f, 68f), 0f, 1.1f);

            // Mechanic shop east of gas station
            CityPrefabHelper.PlaceBuilding(transform, "Car_repair_prefab",
                new Vector3(100f, 1f, 68f), 0f, 1.0f);

            // Parking structure near x=140 road
            CityPrefabHelper.PlaceBuilding(transform, "Parking_checkOut_prefab",
                new Vector3(125f, 1f, 70f), 90f, 1.1f);

            // Warehouse mid-block
            CityPrefabHelper.PlaceBuilding(transform, "Building_Q_prefab",
                new Vector3(80f, 1f, 90f), 180f, 1.2f);

            // Second warehouse next to first
            CityPrefabHelper.PlaceBuilding(transform, "Building_R_Prefab",
                new Vector3(108f, 1f, 92f), 0f, 1.1f);

            // ── East row: x=148..190, z=62..108 (east of x=140 road) ────

            // Generic industrial building
            CityPrefabHelper.PlaceBuilding(transform, "Building_E_prefab",
                new Vector3(158f, 1f, 68f), 270f, 1.0f);

            // Office building
            CityPrefabHelper.PlaceBuilding(transform, "Building_A_prefab",
                new Vector3(180f, 1f, 68f), 0f, 1.0f);

            // ── North row: x=68..190, z=123..170 (north of z=115 road) ──

            // Large industrial building
            CityPrefabHelper.PlaceBuilding(transform, "Building_F_prefab",
                new Vector3(78f, 1f, 130f), 0f, 1.2f);

            // Office building north-east
            CityPrefabHelper.PlaceBuilding(transform, "Building_B_prefab",
                new Vector3(110f, 1f, 132f), 90f, 1.0f);

            // Additional warehouse far north-east
            CityPrefabHelper.PlaceBuilding(transform, "Building_E_prefab",
                new Vector3(160f, 1f, 135f), 180f, 1.1f);

            // ── Water tanks ─────────────────────────────────────────────
            CityPrefabHelper.PlaceProp(transform, "Water tank prefab",
                new Vector3(130f, 1f, 95f));
            CityPrefabHelper.PlaceProp(transform, "Water tank prefab",
                new Vector3(185f, 1f, 90f));
            CityPrefabHelper.PlaceProp(transform, "Water tank prefab",
                new Vector3(90f, 1f, 155f));

            // ── Power poles along east road (x=140) ─────────────────────
            for (int z = 62; z <= 170; z += 20)
                CityPrefabHelper.PlaceProp(transform, "Power_poles prefab",
                    new Vector3(144f, 1f, z));

            // ── Parking barriers near gas station and parking structure ──
            CityPrefabHelper.PlaceProp(transform, "Parking_barrier prefab",
                new Vector3(72f, 1f, 62f), 0f);
            CityPrefabHelper.PlaceProp(transform, "Parking_barrier prefab",
                new Vector3(122f, 1f, 62f), 0f);
            CityPrefabHelper.PlaceProp(transform, "Parking_barrier prefab",
                new Vector3(128f, 1f, 62f), 0f);

            // ── Chain link fences around warehouse lots ──────────────────
            CityPrefabHelper.PlaceProp(transform, "Fence_B_1 prefab",
                new Vector3(70f, 1f, 82f), 0f);
            CityPrefabHelper.PlaceProp(transform, "Fence_B_1 prefab",
                new Vector3(90f, 1f, 82f), 0f);
            CityPrefabHelper.PlaceProp(transform, "Fence_B_1 prefab",
                new Vector3(70f, 1f, 100f), 0f);
            CityPrefabHelper.PlaceProp(transform, "Fence_B_1 prefab",
                new Vector3(148f, 1f, 125f), 90f);
            CityPrefabHelper.PlaceProp(transform, "Fence_B_1 prefab",
                new Vector3(148f, 1f, 145f), 90f);

            // ── Dumpsters behind buildings ──────────────────────────────
            CityPrefabHelper.PlaceProp(transform, "Big_trash_bin prefab",
                new Vector3(88f, 1f, 75f));
            CityPrefabHelper.PlaceProp(transform, "Big_trash_bin prefab",
                new Vector3(115f, 1f, 100f));
            CityPrefabHelper.PlaceProp(transform, "Big_trash_bin prefab",
                new Vector3(165f, 1f, 75f));
            CityPrefabHelper.PlaceProp(transform, "Big_trash_bin prefab",
                new Vector3(85f, 1f, 140f));

            // ── Street lamps along roads ────────────────────────────────
            // Along south road edge (z=58)
            for (int x = 70; x <= 190; x += 25)
                CityPrefabHelper.PlaceLamp(transform, "Lamp_5_prefab",
                    new Vector3(x, 1f, 58f), 0f);

            // Along north road edge (z=118, north side of z=115 road)
            for (int x = 70; x <= 190; x += 25)
                CityPrefabHelper.PlaceLamp(transform, "Lamp_5_prefab",
                    new Vector3(x, 1f, 118f), 180f);

            // Along x=140 road (east side)
            for (int z = 62; z <= 170; z += 25)
                CityPrefabHelper.PlaceLamp(transform, "Lamp_5_prefab",
                    new Vector3(148f, 1f, z), 270f);

            // Along east boundary road (x=64)
            for (int z = 62; z <= 170; z += 30)
                CityPrefabHelper.PlaceLamp(transform, "Lamp_5_prefab",
                    new Vector3(65f, 1f, z), 90f);

            // ── Traffic signs at intersections ──────────────────────────
            // Intersection of x=60 road and z=55 road
            CityPrefabHelper.PlaceSign(transform, "stop sign",
                new Vector3(66f, 1f, 58f), 0f);

            // Intersection of x=140 road and z=55 road
            CityPrefabHelper.PlaceSign(transform, "stop sign",
                new Vector3(144f, 1f, 58f), 0f);

            // Intersection of x=140 road and z=115 road
            CityPrefabHelper.PlaceSign(transform, "stop sign",
                new Vector3(144f, 1f, 118f), 180f);

            // Intersection of x=60 road and z=115 road
            CityPrefabHelper.PlaceSign(transform, "stop sign",
                new Vector3(66f, 1f, 118f), 180f);
        }

        // ── CRASH SITES: two enormous towers dead center of the map,
        //    each with a fighter clearly embedded half in / half out
        //    and wreathed in fire, smoke, and falling embers.
        private void BuildCrashSites()
        {
            // Towers straddle the origin. Tower A sits at x=-30, Tower B at x=+30
            // so the player spawns near the center surrounded by the crash site.
            // Scale 6.5× vs. the old 3.5× — much taller landmark.
            CityPrefabHelper.PlaceBuilding(transform, "Building_I_1_prefab",
                new Vector3(-30f, 1f, 0f), 0f, 6.5f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_I_2_Prefab",
                new Vector3( 30f, 1f, 0f), 18f, 6.5f);

            // ── Fighters clearly protruding from the towers ────────────
            // Tower A fighter: enters the east face, body diagonal, nose buried
            // in the tower core, tail sticking out into open air at x=-17.
            // why: yaw 90° so the fighter's long axis runs east-west into the wall
            SpawnCrashedFighter(new Vector3(-17f, 55f, 3f), new Vector3(12f, 90f, -8f), 5f);

            // Tower B fighter: enters the west face at a different angle and altitude
            SpawnCrashedFighter(new Vector3( 17f, 68f, -4f), new Vector3(-8f, -85f, 15f), 5f);

            // ── Debris / rubble around the new centered tower bases ──
            Color debris = new Color(0.5f, 0.5f, 0.5f);
            AddRockCluster(new Vector3(-45f, 1f, -8f), 10f, 2.5f, debris, "Debris_A1");
            AddRockCluster(new Vector3(-40f, 1f,  10f), 8f, 2.0f, debris, "Debris_A2");
            AddRockCluster(new Vector3(-55f, 1f,   5f), 7f, 1.8f, debris, "Debris_A3");
            AddRockCluster(new Vector3( 45f, 1f,  -6f), 10f, 2.5f, debris, "Debris_B1");
            AddRockCluster(new Vector3( 40f, 1f,  12f), 8f, 2.0f, debris, "Debris_B2");
            AddRockCluster(new Vector3( 55f, 1f,   4f), 7f, 1.8f, debris, "Debris_B3");
            AddRockCluster(new Vector3( 48f, 1f, -18f), 6f, 1.5f, debris, "Debris_B4");

            // ── Skyline backdrop — tall dark silhouette blocks at arena edges ─
            Color skyline = new Color(0.25f, 0.25f, 0.30f);
            AddBlock(new Vector3(240f, 20f, 0f), new Vector3(6f, 40f, 120f), skyline, "Skyline_E");
            AddBlock(new Vector3(-240f, 18f, 30f), new Vector3(6f, 36f, 100f), skyline, "Skyline_W");
            AddBlock(new Vector3(0f, 15f, 240f), new Vector3(160f, 30f, 6f), skyline, "Skyline_N");
            AddBlock(new Vector3(0f, 12f, -240f), new Vector3(140f, 24f, 6f), skyline, "Skyline_S");
        }

        private void BuildWaterfront()
        {
            // ── North bank buildings (z=20..30) ───────────────────
            CityPrefabHelper.PlaceBuilding(transform, "Building_E_prefab",
                new Vector3(-160f, 1f, 25f), 90f, 0.9f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_F_prefab",
                new Vector3(-110f, 1f, 28f), 0f, 0.85f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_H_prefab",
                new Vector3(-50f, 1f, 24f), 90f, 0.95f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_W_prefab",
                new Vector3(30f, 1f, 27f), 0f, 0.8f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_V_prefab",
                new Vector3(100f, 1f, 25f), 90f, 0.9f);

            // ── South bank buildings (z=-30..-20) ─────────────────
            CityPrefabHelper.PlaceBuilding(transform, "Building_Q_prefab",
                new Vector3(-140f, 1f, -26f), 90f, 0.85f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_E_prefab",
                new Vector3(-40f, 1f, -24f), 0f, 0.9f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_H_prefab",
                new Vector3(50f, 1f, -28f), 90f, 0.8f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_F_prefab",
                new Vector3(130f, 1f, -25f), 0f, 0.95f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_W_prefab",
                new Vector3(170f, 1f, -27f), 90f, 0.85f);

            // ── Canal-edge fences ─────────────────────────────────
            // North bank fence segments along z=15
            CityPrefabHelper.PlaceBuilding(transform, "Fence_B_1",
                new Vector3(-150f, 1f, 15f), 0f, 1f);
            CityPrefabHelper.PlaceBuilding(transform, "Fence_B_2",
                new Vector3(-80f, 1f, 15f), 0f, 1f);
            CityPrefabHelper.PlaceBuilding(transform, "Fence_B_1",
                new Vector3(20f, 1f, 15f), 0f, 1f);
            CityPrefabHelper.PlaceBuilding(transform, "Fence_B_2",
                new Vector3(110f, 1f, 15f), 0f, 1f);
            // South bank fence segments along z=-15
            CityPrefabHelper.PlaceBuilding(transform, "Fence_B_2",
                new Vector3(-130f, 1f, -15f), 0f, 1f);
            CityPrefabHelper.PlaceBuilding(transform, "Fence_B_1",
                new Vector3(-30f, 1f, -15f), 0f, 1f);
            CityPrefabHelper.PlaceBuilding(transform, "Fence_B_2",
                new Vector3(60f, 1f, -15f), 0f, 1f);
            CityPrefabHelper.PlaceBuilding(transform, "Fence_B_1",
                new Vector3(150f, 1f, -15f), 0f, 1f);

            // ── Stone bollards along canal edges ──────────────────
            for (int i = -4; i <= 4; i++)
            {
                CityPrefabHelper.PlaceBuilding(transform, "Stone 1 Prefab",
                    new Vector3(i * 40f, 1f, 14f), 0f, 0.6f);
                CityPrefabHelper.PlaceBuilding(transform, "Stone 1 Prefab",
                    new Vector3(i * 40f + 20f, 1f, -14f), 0f, 0.6f);
            }
        }

        // ── CITY PROPS: trees, benches, bins, traffic, vending, mail/ATM ──
        // Scatters street furniture across the entire city to add life and detail.
        private void BuildCityProps()
        {
            // ── Trees along ALL major east-west roads (35 trees) ────────

            // z=55 road: south sidewalk at z=52, every 40 units x=-180..180
            for (float x = -180f; x <= 180f; x += 40f)
                CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                    new Vector3(x, 1f, 52f), Random.Range(0f, 360f), 1.0f);

            // z=-55 road: north sidewalk at z=-52, every 40 units
            for (float x = -180f; x <= 180f; x += 40f)
                CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                    new Vector3(x, 1f, -52f), Random.Range(0f, 360f), 1.0f);

            // z=115 road: north sidewalk at z=118, every 50 units
            for (float x = -180f; x <= 180f; x += 50f)
                CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                    new Vector3(x, 1f, 118f), Random.Range(0f, 360f), 1.0f);

            // z=-115 road: south sidewalk at z=-118, every 50 units
            for (float x = -180f; x <= 180f; x += 50f)
                CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                    new Vector3(x, 1f, -118f), Random.Range(0f, 360f), 1.0f);

            // ── Benches at road intersections (14 benches) ──────────────

            // z=55 intersections
            CityPrefabHelper.PlaceProp(transform, "bench prefab",
                new Vector3(65f, 1f, 52f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(-65f, 1f, 52f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "bench prefab",
                new Vector3(145f, 1f, 52f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(-145f, 1f, 52f), 0f, 1.0f);

            // z=-55 intersections
            CityPrefabHelper.PlaceProp(transform, "bench prefab",
                new Vector3(65f, 1f, -52f), 180f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(-65f, 1f, -52f), 180f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "bench prefab",
                new Vector3(145f, 1f, -52f), 180f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(-145f, 1f, -52f), 180f, 1.0f);

            // z=115 intersections
            CityPrefabHelper.PlaceProp(transform, "bench prefab",
                new Vector3(65f, 1f, 118f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(-65f, 1f, 118f), 0f, 1.0f);

            // z=-115 intersections
            CityPrefabHelper.PlaceProp(transform, "bench prefab",
                new Vector3(65f, 1f, -118f), 180f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(-65f, 1f, -118f), 180f, 1.0f);

            // Near central bridge approaches
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(5f, 1f, 52f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "bench prefab",
                new Vector3(-5f, 1f, -52f), 180f, 1.0f);

            // ── Trash bins at building entrances (10 bins) ──────────────

            // Commercial / downtown streets
            CityPrefabHelper.PlaceProp(transform, "Bin prefab",
                new Vector3(-100f, 1f, 58f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "trashcan prefab",
                new Vector3(-150f, 1f, 58f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Bin prefab",
                new Vector3(-80f, 1f, 118f), 180f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "trashcan prefab",
                new Vector3(35f, 1f, 58f), 0f, 1.0f);

            // Industrial area
            CityPrefabHelper.PlaceProp(transform, "Bin prefab",
                new Vector3(90f, 1f, 58f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "trashcan prefab",
                new Vector3(160f, 1f, 58f), 0f, 1.0f);

            // Residential south
            CityPrefabHelper.PlaceProp(transform, "Bin prefab",
                new Vector3(-110f, 1f, -58f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "trashcan prefab",
                new Vector3(30f, 1f, -58f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Bin prefab",
                new Vector3(100f, 1f, -118f), 180f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "trashcan prefab",
                new Vector3(-80f, 1f, -118f), 180f, 1.0f);

            // ── Traffic objects at major intersections (8 objects) ───────

            CityPrefabHelper.PlaceProp(transform, "traffic_obj prefab",
                new Vector3(0f, 1f, 55f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "traffic_obj prefab",
                new Vector3(0f, 1f, -55f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "traffic_obj prefab",
                new Vector3(-60f, 1f, 55f), 90f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "traffic_obj prefab",
                new Vector3(60f, 1f, 55f), 270f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "traffic_obj prefab",
                new Vector3(-60f, 1f, -55f), 90f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "traffic_obj prefab",
                new Vector3(60f, 1f, -55f), 270f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "traffic_obj prefab",
                new Vector3(-140f, 1f, 55f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "traffic_obj prefab",
                new Vector3(140f, 1f, -55f), 180f, 1.0f);

            // ── Cola machines and street sellers (5 objects) ─────────────

            // Cola machines near shops in commercial district
            CityPrefabHelper.PlaceProp(transform, "ColaMachine prefab",
                new Vector3(-160f, 1f, 68f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "ColaMachine prefab",
                new Vector3(-95f, 1f, 68f), 0f, 1.0f);

            // Street seller stands near downtown
            CityPrefabHelper.PlaceProp(transform, "StreetSellerStand prefab",
                new Vector3(-15f, 1f, 58f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "StreetSellerStand prefab",
                new Vector3(25f, 1f, 112f), 180f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "StreetSellerStand prefab",
                new Vector3(-40f, 1f, 112f), 180f, 1.0f);

            // ── Mail boxes and ATMs (5 objects) ─────────────────────────

            // Mail boxes near residential buildings
            CityPrefabHelper.PlaceProp(transform, "Mail_box prefab",
                new Vector3(-170f, 1f, -72f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Mail_box prefab",
                new Vector3(35f, 1f, -75f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Mail_box prefab",
                new Vector3(170f, 1f, -80f), 0f, 1.0f);

            // ATMs near the bank in downtown
            CityPrefabHelper.PlaceProp(transform, "ATM_prefab",
                new Vector3(-25f, 1f, 104f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "ATM_prefab",
                new Vector3(32f, 1f, 104f), 180f, 1.0f);
        }

        // ── CANAL DETAIL: low walls, lamps, trees, and south park ──────
        // Adds detail along both canal banks and fills the far south edge
        // with a small park area.
        private void BuildCanalDetail()
        {
            // ── Canal-side walls / railings (both banks) ─────────────
            Color canalWall = new Color(0.45f, 0.43f, 0.40f);

            // North bank wall segments (z=16, between bridges)
            AddWall(new Vector3(-135f, 1f, 16f), new Vector3(-65f, 1f, 16f), 1.5f, 0.5f, canalWall, "CanalWall_N1");
            AddWall(new Vector3(-55f, 1f, 16f), new Vector3(-5f, 1f, 16f), 1.5f, 0.5f, canalWall, "CanalWall_N2");
            AddWall(new Vector3(5f, 1f, 16f), new Vector3(55f, 1f, 16f), 1.5f, 0.5f, canalWall, "CanalWall_N3");
            AddWall(new Vector3(65f, 1f, 16f), new Vector3(135f, 1f, 16f), 1.5f, 0.5f, canalWall, "CanalWall_N4");

            // South bank wall segments (z=-16, between bridges)
            AddWall(new Vector3(-135f, 1f, -16f), new Vector3(-65f, 1f, -16f), 1.5f, 0.5f, canalWall, "CanalWall_S1");
            AddWall(new Vector3(-55f, 1f, -16f), new Vector3(-5f, 1f, -16f), 1.5f, 0.5f, canalWall, "CanalWall_S2");
            AddWall(new Vector3(5f, 1f, -16f), new Vector3(55f, 1f, -16f), 1.5f, 0.5f, canalWall, "CanalWall_S3");
            AddWall(new Vector3(65f, 1f, -16f), new Vector3(135f, 1f, -16f), 1.5f, 0.5f, canalWall, "CanalWall_S4");

            // ── Canal-side lamps (10 total, every ~50 units) ─────────
            // North bank lamps along z=18
            CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(-125f, 1f, 18f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(-75f, 1f, 18f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(-25f, 1f, 18f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(25f, 1f, 18f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(75f, 1f, 18f));
            // South bank lamps along z=-18
            CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(-100f, 1f, -18f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(-50f, 1f, -18f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(0f, 1f, -18f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(50f, 1f, -18f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(100f, 1f, -18f));

            // ── Canal-side potted trees (8 total) ────────────────────
            // North bank walkway at z=19
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(-110f, 1f, 19f));
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(-40f, 1f, 19f));
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(40f, 1f, 19f));
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(110f, 1f, 19f));
            // South bank walkway at z=-19
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(-90f, 1f, -19f));
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(-20f, 1f, -19f));
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(60f, 1f, -19f));
            CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(120f, 1f, -19f));

            // ── Far south park area (z=-150 to -180) ─────────────────
            // Trees in a park-like cluster around x=-30..30, z=-160
            CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                new Vector3(-25f, 1f, -155f), Random.Range(0f, 360f), 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                new Vector3(-10f, 1f, -162f), Random.Range(0f, 360f), 1.1f);
            CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                new Vector3(8f, 1f, -158f), Random.Range(0f, 360f), 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                new Vector3(22f, 1f, -165f), Random.Range(0f, 360f), 0.95f);
            CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                new Vector3(-18f, 1f, -172f), Random.Range(0f, 360f), 1.05f);
            CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                new Vector3(5f, 1f, -175f), Random.Range(0f, 360f), 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                new Vector3(28f, 1f, -170f), Random.Range(0f, 360f), 0.9f);

            // Benches in the park
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(-15f, 1f, -160f), 90f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(12f, 1f, -168f), 0f);
            CityPrefabHelper.PlaceProp(transform, "Bench 2 prefab",
                new Vector3(-5f, 1f, -178f), 270f);

            // Stones for natural feel
            CityPrefabHelper.PlaceProp(transform, "Stone 1 Prefab",
                new Vector3(-22f, 1f, -166f), Random.Range(0f, 360f), 0.7f);
            CityPrefabHelper.PlaceProp(transform, "Stone 2 Prefab",
                new Vector3(15f, 1f, -157f), Random.Range(0f, 360f), 0.6f);
            CityPrefabHelper.PlaceProp(transform, "Stone 1 Prefab",
                new Vector3(0f, 1f, -174f), Random.Range(0f, 360f), 0.8f);
            CityPrefabHelper.PlaceProp(transform, "Stone 2 Prefab",
                new Vector3(-8f, 1f, -180f), Random.Range(0f, 360f), 0.65f);
        }

        /// <summary>
        /// Assemble a crashed Omega fighter from part prefabs with heavy fire,
        /// smoke, sparks, a glowing emissive impact scar, and a cinematic ember
        /// plume. All VFX loop so they persist for the match duration.
        /// </summary>
        private void SpawnCrashedFighter(Vector3 pos, Vector3 eulerAngles, float scale = 5f)
        {
            string[] partNames = { "OutBody", "Frame_body", "Cockipt", "Cockipt_Glass" };
            var parent = new GameObject("CrashedFighter");
            parent.transform.SetParent(transform, false);
            parent.transform.position = pos;
            parent.transform.rotation = Quaternion.Euler(eulerAngles);

            foreach (var partName in partNames)
            {
                var prefab = Resources.Load<GameObject>($"Models/{partName}");
                if (prefab != null)
                {
                    var part = Object.Instantiate(prefab, parent.transform);
                    part.transform.localPosition = Vector3.zero;
                    part.transform.localRotation = Quaternion.identity;
                }
            }
            parent.transform.localScale = Vector3.one * scale;
            CityPrefabHelper.FixURPMaterials(parent.transform);

            // Multi-plume smoke column rising above + drifting behind the crash
            var smokePrefab = Resources.Load<GameObject>("VFX/Smoke/SmokeEffect");
            if (smokePrefab != null)
            {
                Vector3[] smokeOffsets =
                {
                    new Vector3( 0f,  2f,  0f),
                    new Vector3( 0f,  7f,  0f),
                    new Vector3( 0f, 13f,  0f),
                    new Vector3( 2f,  9f,  2f),
                    new Vector3(-2f, 11f, -1f),
                };
                float[] smokeScales = { 6f, 5f, 4f, 3.5f, 3f };
                for (int s = 0; s < smokeOffsets.Length; s++)
                {
                    var smoke = Object.Instantiate(smokePrefab, transform);
                    smoke.name = $"CrashSmoke_{s}";
                    smoke.transform.position = pos + smokeOffsets[s];
                    smoke.transform.localScale = Vector3.one * smokeScales[s];
                    foreach (var ps in smoke.GetComponentsInChildren<ParticleSystem>())
                    { var m = ps.main; m.loop = true; }
                }
            }

            // Dense fire — multiple sources around the fuselage and impact wound
            var firePrefab = Resources.Load<GameObject>("VFX/Fire/LargeFlames");
            if (firePrefab != null)
            {
                Vector3[] fireOffsets =
                {
                    Vector3.zero,
                    new Vector3( 2f, -1f,  1.5f),
                    new Vector3(-1.5f, 0.5f, -1f),
                    new Vector3( 0f,  1.5f,  2f),
                    new Vector3( 3f,  0.5f, -1.5f),
                    new Vector3(-2.5f, -0.5f, 2f),
                    new Vector3( 0f,  3f,  0f),
                };
                float[] fireScales = { 3.5f, 2.2f, 2.0f, 1.8f, 1.6f, 1.6f, 1.3f };
                for (int f = 0; f < fireOffsets.Length; f++)
                {
                    var fire = Object.Instantiate(firePrefab, transform);
                    fire.name = $"CrashFire_{f}";
                    fire.transform.position = pos + fireOffsets[f];
                    fire.transform.localScale = Vector3.one * fireScales[f];
                    foreach (var ps in fire.GetComponentsInChildren<ParticleSystem>())
                    { var m = ps.main; m.loop = true; }
                }
            }

            // Smaller flickering flames near the cockpit
            var tinyFirePrefab = Resources.Load<GameObject>("VFX/Fire/TinyFlames");
            if (tinyFirePrefab != null)
            {
                Vector3[] tinyOffsets =
                {
                    new Vector3( 1f,  0.8f,  1f),
                    new Vector3(-1.2f, 1.2f, 0.5f),
                    new Vector3( 0.5f, 1.8f, -1f),
                };
                for (int t = 0; t < tinyOffsets.Length; t++)
                {
                    var fire = Object.Instantiate(tinyFirePrefab, transform);
                    fire.name = $"CrashTinyFire_{t}";
                    fire.transform.position = pos + tinyOffsets[t];
                    fire.transform.localScale = Vector3.one * 1.3f;
                    foreach (var ps in fire.GetComponentsInChildren<ParticleSystem>())
                    { var m = ps.main; m.loop = true; }
                }
            }

            // Electrical sparks — multiple sources on damaged hull
            var sparksPrefab = Resources.Load<GameObject>("VFX/Weapons/ElectricalSparksEffect");
            if (sparksPrefab != null)
            {
                Vector3[] sparkOffsets =
                {
                    new Vector3(-1f,  1f, -1f),
                    new Vector3( 1.5f, 0.5f,  0.5f),
                    new Vector3( 0f,   1.8f, -1.5f),
                };
                for (int k = 0; k < sparkOffsets.Length; k++)
                {
                    var sparks = Object.Instantiate(sparksPrefab, transform);
                    sparks.name = $"CrashSparks_{k}";
                    sparks.transform.position = pos + sparkOffsets[k];
                    sparks.transform.localScale = Vector3.one * 2.5f;
                    foreach (var ps in sparks.GetComponentsInChildren<ParticleSystem>())
                    { var m = ps.main; m.loop = true; }
                }
            }

            // Heat distortion shimmer so the crash feels hot even at distance
            var heatPrefab = Resources.Load<GameObject>("VFX/Ambient/HeatDistortion");
            if (heatPrefab != null)
            {
                var heat = Object.Instantiate(heatPrefab, transform);
                heat.name = "CrashHeat";
                heat.transform.position = pos + Vector3.up * 4f;
                heat.transform.localScale = Vector3.one * 4f;
            }

            // Cinematic smoke plume reaching into the sky
            var bigSmokePrefab = Resources.Load<GameObject>("VFX/CinematicExplosions/CinematicSmoke");
            if (bigSmokePrefab != null)
            {
                var plume = Object.Instantiate(bigSmokePrefab, transform);
                plume.name = "CrashPlume";
                plume.transform.position = pos + Vector3.up * 6f;
                plume.transform.localScale = Vector3.one * 5f;
                foreach (var ps in plume.GetComponentsInChildren<ParticleSystem>())
                { var m = ps.main; m.loop = true; }
            }

            // Glowing emissive "impact scar" — a small flattened sphere behind
            // the fighter so the wall looks damaged/melted around the hole.
            var scar = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            scar.name = "CrashImpactScar";
            scar.transform.SetParent(transform, false);
            scar.transform.position = pos;
            scar.transform.localScale = new Vector3(6f, 4f, 6f);
            Object.DestroyImmediate(scar.GetComponent<Collider>());
            var scarRend = scar.GetComponent<MeshRenderer>();
            if (scarRend != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.15f, 0.05f, 0.02f, 1f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1.8f, 0.5f, 0.08f) * 3f);
                scarRend.material = mat;
            }
        }

        // ── OUTSKIRTS: sparse buildings filling empty outer zones ────
        private void BuildOutskirts()
        {
            // ── Far North (z=125..170, x=-55..55) ───────────────────
            CityPrefabHelper.PlaceBuilding(transform, "Building_C1_prefab",
                new Vector3(-45f, 1f, 135f), 0f, 0.95f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_D_prefab",
                new Vector3(-18f, 1f, 150f), 90f, 0.9f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_D1_prefab",
                new Vector3(10f, 1f, 130f), 180f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_T_prefab",
                new Vector3(32f, 1f, 155f), 270f, 0.95f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_S_prefab",
                new Vector3(-30f, 1f, 165f), 0f, 0.9f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_p_prefab",
                new Vector3(48f, 1f, 142f), 90f, 1.0f);

            CityPrefabHelper.PlaceLamp(transform, "Lamp_2_prefab",
                new Vector3(-10f, 1f, 140f));
            CityPrefabHelper.PlaceLamp(transform, "Lamp_2_prefab",
                new Vector3(25f, 1f, 158f));

            // ── Northeast corner (x=145..195, z=130..175) ───────────
            CityPrefabHelper.PlaceBuilding(transform, "Building_R_Prefab",
                new Vector3(155f, 1f, 145f), 0f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_A_prefab",
                new Vector3(175f, 1f, 160f), 90f, 1.1f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_H_prefab",
                new Vector3(185f, 1f, 135f), 180f, 1.05f);

            CityPrefabHelper.PlaceLamp(transform, "Lamp_2_prefab",
                new Vector3(165f, 1f, 150f));

            // ── Northwest corner (x=-195..-145, z=130..175) ─────────
            CityPrefabHelper.PlaceBuilding(transform, "Building_M_prefab",
                new Vector3(-160f, 1f, 145f), 0f, 0.95f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_K_prefab",
                new Vector3(-180f, 1f, 160f), 270f, 0.9f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_Y_prefab",
                new Vector3(-150f, 1f, 170f), 90f, 1.0f);

            CityPrefabHelper.PlaceLamp(transform, "Lamp_2_prefab",
                new Vector3(-165f, 1f, 155f));

            // ── Southeast corner (x=145..190, z=-155..-175) ─────────
            CityPrefabHelper.PlaceBuilding(transform, "Building_u_prefab",
                new Vector3(155f, 1f, -160f), 0f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "building_X_prefab",
                new Vector3(180f, 1f, -170f), 180f, 0.95f);

            CityPrefabHelper.PlaceLamp(transform, "Lamp_2_prefab",
                new Vector3(168f, 1f, -165f));

            // ── Southwest corner (x=-190..-145, z=-155..-175) ───────
            CityPrefabHelper.PlaceBuilding(transform, "Building_Z_Prefab",
                new Vector3(-160f, 1f, -160f), 90f, 1.0f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_B1_prefab",
                new Vector3(-185f, 1f, -170f), 270f, 0.95f);

            CityPrefabHelper.PlaceLamp(transform, "Lamp_2_prefab",
                new Vector3(-172f, 1f, -165f));

            // ══════════════════════════════════════════════════════
            // EXPANDED OUTER SUBURBS (new 375 boundary)
            // why: outer ring (±210..±340) needs its own district so
            //      the expanded playable area doesn't feel empty
            // ══════════════════════════════════════════════════════

            // ── Far-north suburban ring (z=210..290) ────────────────
            string[] subBuildings = {
                "Building_A1_prefab", "Building_B1_prefab", "Building_D1_prefab",
                "Building_S_prefab",  "Building_T_prefab",  "Building_V_prefab",
                "Building_W_prefab",  "Building_Y_prefab",  "Building_p_prefab",
                "Building_u_prefab",  "building_X_prefab",
            };
            for (int i = 0; i < 10; i++)
            {
                float bx = -280f + i * 60f + Random.Range(-8f, 8f);
                float bz = 220f + Random.Range(0f, 70f);
                float rot = Random.Range(0, 4) * 90f;
                float scl = Random.Range(0.80f, 1.05f);
                CityPrefabHelper.PlaceBuilding(transform, subBuildings[i % subBuildings.Length],
                    new Vector3(bx, 1f, bz), rot, scl);
            }
            for (int i = 0; i < 10; i++)
            {
                float bx = -280f + i * 60f + Random.Range(-8f, 8f);
                float bz = -290f + Random.Range(0f, 70f);
                float rot = Random.Range(0, 4) * 90f;
                float scl = Random.Range(0.80f, 1.05f);
                CityPrefabHelper.PlaceBuilding(transform, subBuildings[(i + 3) % subBuildings.Length],
                    new Vector3(bx, 1f, bz), rot, scl);
            }

            // ── Far-east / far-west outskirts ──────────────────────
            for (int i = 0; i < 6; i++)
            {
                float bz = -180f + i * 60f + Random.Range(-6f, 6f);
                CityPrefabHelper.PlaceBuilding(transform, subBuildings[i % subBuildings.Length],
                    new Vector3(250f + Random.Range(-8f, 20f), 1f, bz),
                    Random.Range(0, 4) * 90f, Random.Range(0.80f, 1.05f));
                CityPrefabHelper.PlaceBuilding(transform, subBuildings[(i + 5) % subBuildings.Length],
                    new Vector3(-250f - Random.Range(0f, 28f), 1f, bz),
                    Random.Range(0, 4) * 90f, Random.Range(0.80f, 1.05f));
            }

            // ── Trees along outskirts roads (40 trees) ─────────────
            for (int i = 0; i < 20; i++)
            {
                float ax = Random.Range(-320f, 320f);
                float azN =  220f + Random.Range(0f, 90f);
                float azS = -310f + Random.Range(0f, 90f);
                CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                    new Vector3(ax + Random.Range(-4f, 4f), 1f, azN),
                    Random.Range(0f, 360f), Random.Range(0.9f, 1.2f));
                CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                    new Vector3(ax + Random.Range(-4f, 4f), 1f, azS),
                    Random.Range(0f, 360f), Random.Range(0.9f, 1.2f));
            }

            // ── Bushes / hedges / pot trees scattered in suburbs ───
            string[] subGreens = { "Bush prefab", "hedge prefab", "Pot_tree prefab" };
            for (int i = 0; i < 30; i++)
            {
                float gx = Random.Range(-320f, 320f);
                float gz;
                if (i % 2 == 0) gz = Random.Range(215f, 310f);
                else            gz = Random.Range(-310f, -215f);
                CityPrefabHelper.PlaceProp(transform, subGreens[Random.Range(0, subGreens.Length)],
                    new Vector3(gx, 1f, gz), Random.Range(0f, 360f), Random.Range(0.9f, 1.2f));
            }

            // ── Street lamps along new outer road loops ────────────
            for (int i = 0; i < 12; i++)
            {
                float t = (i / 12f) * Mathf.PI * 2f;
                CityPrefabHelper.PlaceLamp(transform, "Lamp_2_prefab",
                    new Vector3(Mathf.Cos(t) * 290f, 1f, Mathf.Sin(t) * 290f),
                    Mathf.Rad2Deg * (-t));
            }

            // ── Dumpsters / bins / benches alley dressing ──────────
            string[] alleyProps = {
                "Big_trash_bin prefab", "Bin prefab", "trashcan prefab",
                "bench prefab", "Bench 2 prefab", "StreetSellerStand prefab"
            };
            for (int i = 0; i < 18; i++)
            {
                float ax = Random.Range(-310f, 310f);
                float az;
                if (i % 2 == 0) az = Random.Range(220f, 300f);
                else            az = Random.Range(-300f, -220f);
                CityPrefabHelper.PlaceProp(transform, alleyProps[Random.Range(0, alleyProps.Length)],
                    new Vector3(ax, 1f, az), Random.Range(0, 4) * 90f, 1f);
            }

            // ── Power poles along outer roads ──────────────────────
            for (int z = -300; z <= 300; z += 60)
            {
                CityPrefabHelper.PlaceProp(transform, "Power_poles prefab",
                    new Vector3( 320f, 1f, z));
                CityPrefabHelper.PlaceProp(transform, "Power_poles prefab",
                    new Vector3(-320f, 1f, z));
            }

            // ── Secondary crashed fighter clusters at new edges ────
            Color rubble = new Color(0.5f, 0.5f, 0.5f);
            AddRockCluster(new Vector3( 260f, 1f,  220f), 7f, 1.8f, rubble, "Outer_Debris_NE");
            AddRockCluster(new Vector3(-260f, 1f, -230f), 7f, 1.8f, rubble, "Outer_Debris_SW");
            AddRockCluster(new Vector3(-250f, 1f,  260f), 6f, 1.6f, rubble, "Outer_Debris_NW");
            AddRockCluster(new Vector3( 250f, 1f, -260f), 6f, 1.6f, rubble, "Outer_Debris_SE");

            // ── Skyline backdrop extension at new edges ────────────
            Color skyline = new Color(0.22f, 0.22f, 0.28f);
            AddBlock(new Vector3( 340f, 22f,   0f), new Vector3(6f, 44f, 180f), skyline, "Skyline2_E");
            AddBlock(new Vector3(-340f, 20f,  30f), new Vector3(6f, 40f, 150f), skyline, "Skyline2_W");
            AddBlock(new Vector3(   0f, 18f, 340f), new Vector3(220f, 36f, 6f), skyline, "Skyline2_N");
            AddBlock(new Vector3(   0f, 15f,-340f), new Vector3(200f, 30f, 6f), skyline, "Skyline2_S");
        }
    }

    // =========================================================================
    // 3. GroundArctic -- Canada: snowy rolling terrain, frozen lake, pines
    // =========================================================================

    public class GroundArctic : ArenaBase
    {
        public override string ArenaName => "Canadian Arctic";

        public override void Build()
        {
            Color ice       = new Color(0.70f, 0.85f, 0.95f);
            Color darkRock  = new Color(0.35f, 0.33f, 0.30f);
            Color cliffGray = new Color(0.50f, 0.48f, 0.45f);

            // --- Unity Terrain: 750x750, height 50 (why: 2.5x playable area) ---
            var terrain = TerrainFactory.Create(
                transform,
                new Vector3(-375f, 0f, -375f),
                new Vector3(750f, 50f, 750f),
                513,
                "ArcticTerrain");

            // Gentle rolling hills with a few rocky outcrops
            TerrainFactory.SetHeights(terrain, (nx, nz) =>
            {
                float h = 0.02f;

                // Broad gentle rolls
                h += 0.04f * Mathf.PerlinNoise(nx * 3f + 10f, nz * 3f + 10f);
                h += 0.02f * Mathf.PerlinNoise(nx * 7f + 20f, nz * 7f + 20f);

                // Rocky outcrop NE
                float dx1 = nx - 0.70f, dz1 = nz - 0.70f;
                float d1 = Mathf.Sqrt(dx1 * dx1 + dz1 * dz1);
                if (d1 < 0.12f)
                    h += 0.12f * Mathf.SmoothStep(1f, 0f, d1 / 0.12f);

                // Rocky outcrop SW
                float dx2 = nx - 0.25f, dz2 = nz - 0.25f;
                float d2 = Mathf.Sqrt(dx2 * dx2 + dz2 * dz2);
                if (d2 < 0.10f)
                    h += 0.08f * Mathf.SmoothStep(1f, 0f, d2 / 0.10f);

                // Depression for frozen lake at center-ish
                float lx = nx - 0.53f, lz = nz - 0.47f;
                float lDist = Mathf.Sqrt(lx * lx + lz * lz);
                if (lDist < 0.12f)
                    h -= 0.015f * (1f - lDist / 0.12f);

                // Multi-octave detail noise
                h += 0.008f * Mathf.PerlinNoise(nx * 20f, nz * 20f);  // medium bumps
                h += 0.003f * Mathf.PerlinNoise(nx * 50f + 100f, nz * 50f + 100f);  // fine detail
                h += 0.001f * Mathf.PerlinNoise(nx * 120f + 200f, nz * 120f + 200f);  // micro detail

                return Mathf.Max(0f, h);
            });

            // Splatmap: Snow everywhere, Rock on outcrops, PebblesB rocky areas,
            // GrassMoss in sheltered spots
            TerrainFactory.PaintSplatmap(terrain, (nx, nz, height, steepness) =>
            {
                float[] w = new float[16];

                if (steepness > 35f)
                {
                    // Steep rocky faces
                    w[7]  = 0.6f; // Rock
                    w[14] = 0.4f; // PebblesB
                }
                else if (height > 0.10f)
                {
                    // High rocky outcrops
                    w[7]  = 0.7f; // Rock
                    w[14] = 0.2f; // PebblesB
                    w[6]  = 0.1f; // Snow dusting
                }
                else if (height < 0.015f)
                {
                    // Low frozen areas -- icy snow
                    w[6] = 0.7f; // Snow
                    w[3] = 0.3f; // GrassMoss (sheltered)
                }
                else
                {
                    // General snowy ground
                    float noise = Mathf.PerlinNoise(nx * 6f + 30f, nz * 6f + 30f);
                    w[6]  = 0.6f + 0.2f * noise;  // Snow
                    w[7]  = 0.3f - 0.1f * noise;  // Rock (exposed patches)
                    w[3]  = 0.1f;                  // GrassMoss traces
                }

                return w;
            });

            // --- Keep non-terrain features ---

            // Frozen lake -- center with ice hazard
            AddCylinder(new Vector3(15f, -0.1f, -15f), 35f, 0.2f, ice, "FrozenLake");
            AddIceHazard(new Vector3(15f, 0f, -15f), new Vector3(70f, 2f, 70f), "LakeIce");

            // Pine forests -- west cluster
            float[] treeX = { -120f, -105f, -127.5f, -97.5f, -112.5f, -135f, -90f, -142.5f };
            float[] treeZ = { 45f, 67.5f, 30f, 82.5f, 15f, 60f, 52.5f, 22.5f };
            for (int i = 0; i < treeX.Length; i++)
            {
                AddPine(new Vector3(treeX[i], 0f, treeZ[i]), Random.Range(8f, 14f), $"Pine_W_{i}");
            }

            // Pine forests -- east cluster
            float[] treeX2 = { 90f, 105f, 82.5f, 112.5f, 120f, 97.5f, 127.5f };
            float[] treeZ2 = { -75f, -60f, -90f, -52.5f, -82.5f, -105f, -67.5f };
            for (int i = 0; i < treeX2.Length; i++)
            {
                AddPine(new Vector3(treeX2[i], 0f, treeZ2[i]), Random.Range(8f, 14f), $"Pine_E_{i}");
            }

            // Ice rock formations
            AddRockCluster(new Vector3(-45f, 0f, -60f), 4f, 8f * 0.3f, new Color(0.7f, 0.85f, 0.95f), "IceRock_1");
            AddRockCluster(new Vector3(60f, 0f, 30f), 3f, 6f * 0.3f, new Color(0.7f, 0.85f, 0.95f), "IceRock_2");
            AddRockCluster(new Vector3(-22.5f, 0f, 45f), 3.5f, 7f * 0.3f, new Color(0.7f, 0.85f, 0.95f), "IceRock_3");
            AddRockCluster(new Vector3(37.5f, 0f, -75f), 2.5f, 5f * 0.3f, new Color(0.7f, 0.85f, 0.95f), "IceRock_4");

            // Rock clusters
            AddRockCluster(new Vector3(75f, 0f, 90f), 8f, 3f, darkRock, "Rocks_NE");
            AddRockCluster(new Vector3(-60f, 0f, -105f), 6f, 2.5f, cliffGray, "Rocks_S");

            // ── Outer pine forest rings (new expansion zone) ──────────
            // why: keeps the expanded outskirts visually interesting without blocking spawn lanes
            for (int i = 0; i < 26; i++)
            {
                float t = (i / 26f) * Mathf.PI * 2f;
                float r = Random.Range(210f, 320f);
                float px = Mathf.Cos(t) * r + Random.Range(-12f, 12f);
                float pz = Mathf.Sin(t) * r + Random.Range(-12f, 12f);
                AddPine(new Vector3(px, 0f, pz), Random.Range(8f, 16f), $"Pine_Outer_{i}");
            }

            // ── Outer rock clusters ──────────────────────────────────
            AddRockCluster(new Vector3( 240f, 0f,  220f), 10f, 3.5f, darkRock, "Rocks_NE_Outer");
            AddRockCluster(new Vector3(-230f, 0f,  230f), 9f, 3f, cliffGray, "Rocks_NW_Outer");
            AddRockCluster(new Vector3( 250f, 0f, -240f), 8f, 3f, darkRock, "Rocks_SE_Outer");
            AddRockCluster(new Vector3(-250f, 0f, -220f), 10f, 3.5f, cliffGray, "Rocks_SW_Outer");
            AddRockCluster(new Vector3(-300f, 0f,    0f), 8f, 3f, darkRock, "Rocks_W_Outer");
            AddRockCluster(new Vector3( 300f, 0f,   40f), 8f, 3f, cliffGray, "Rocks_E_Outer");
            AddRockCluster(new Vector3(   0f, 0f,  300f), 10f, 3.5f, darkRock, "Rocks_N_Outer");
            AddRockCluster(new Vector3(  20f, 0f, -300f), 9f, 3f, cliffGray, "Rocks_S_Outer");

            // ── Ice rock clusters scattered in outer ring ────────────
            Color iceCol = new Color(0.7f, 0.85f, 0.95f);
            for (int i = 0; i < 10; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(180f, 300f);
                AddRockCluster(new Vector3(Mathf.Cos(t) * r, 0f, Mathf.Sin(t) * r),
                    Random.Range(3f, 6f), Random.Range(1.5f, 2.5f), iceCol, $"IceRock_Outer_{i}");
            }

            // ── Snow drift blocks (low white cover props) ────────────
            Color drift = new Color(0.92f, 0.96f, 1f);
            for (int i = 0; i < 14; i++)
            {
                float dx = Random.Range(-320f, 320f);
                float dz = Random.Range(-320f, 320f);
                if (Mathf.Sqrt(dx * dx + dz * dz) < 80f) continue; // keep center clean
                float sx = Random.Range(4f, 10f);
                float sz = Random.Range(4f, 10f);
                AddBlockUnchecked(new Vector3(dx, 0.4f, dz), new Vector3(sx, 0.8f, sz), drift, $"SnowDrift_{i}");
            }

            // why: spawn ring pushed from 160→260 so players start outside the new dense props
            AddSpawnRing(Vector3.zero, 260f, 8, 1f);

            // Invisible arena boundary walls (expanded)
            AddInvisibleWalls(375f, 50f);

            // ── Cold directional light ─────────────────────────────
            var arcticSun = new GameObject("ArcticSun");
            arcticSun.transform.SetParent(transform, false);
            var asl = arcticSun.AddComponent<Light>();
            asl.type = LightType.Directional;
            asl.color = new Color(0.80f, 0.88f, 1f); // cold blue-white
            asl.intensity = 0.9f;
            asl.transform.rotation = Quaternion.Euler(35f, 15f, 0f); // low arctic sun
            asl.shadows = LightShadows.Soft;

            // ── Atmosphere: cold blue fog ──────────────────────────
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.75f, 0.82f, 0.90f);
            RenderSettings.fogDensity = 0.005f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.62f, 0.70f, 0.82f);

            // Ambient VFX
            VFXManager.GroundFog(Vector3.zero, 6f);
            VFXManager.GroundFog(new Vector3( 200f, 1f,  200f), 4f);
            VFXManager.GroundFog(new Vector3(-200f, 1f, -200f), 4f);
            VFXManager.Rain(new Vector3(0, 30, 0), 8f);
            VFXManager.DustMotes(new Vector3(0, 10, 0), 5f); // snow specks feel
        }
    }

    // =========================================================================
    // 4. GroundVolcanic -- Florida: massive volcanic landscape with central
    //    caldera, lava streams, magma formations, fire VFX, and dark atmosphere.
    //    Uses Magma prefabs, canyon mountains tinted volcanic, and fire effects.
    // =========================================================================

    public class GroundVolcanic : ArenaBase
    {
        public override string ArenaName => "Florida";

        public override void Build()
        {
            // ── Terrain ────────────────────────────────────────────
            // why: 750x750 for ~2.5x playable area
            var terrain = TerrainFactory.Create(transform,
                new Vector3(-375f, 0f, -375f), new Vector3(750f, 80f, 750f),
                513, "VolcanicTerrain");

            TerrainFactory.SetHeights(terrain, (nx, nz) =>
            {
                float h = 0.02f;
                h += 0.015f * Mathf.PerlinNoise(nx * 6f + 5f, nz * 6f + 5f);
                h += 0.008f * Mathf.PerlinNoise(nx * 14f + 8f, nz * 14f + 8f);

                // Rise toward center for caldera base
                float cx = nx - 0.5f, cz = nz - 0.5f;
                float cDist = Mathf.Sqrt(cx * cx + cz * cz);
                if (cDist < 0.35f) h += 0.03f * (1f - cDist / 0.35f);

                h += 0.008f * Mathf.PerlinNoise(nx * 20f, nz * 20f);
                h += 0.003f * Mathf.PerlinNoise(nx * 50f + 100f, nz * 50f + 100f);
                h += 0.001f * Mathf.PerlinNoise(nx * 120f + 200f, nz * 120f + 200f);
                return h;
            });

            TerrainFactory.AddHill(terrain, 0.5f, 0.5f, 0.18f, 0.45f); // central cone
            TerrainFactory.Flatten(terrain, 0.47f, 0.47f, 0.53f, 0.53f, 0.35f); // caldera crater
            TerrainFactory.AddHill(terrain, 0.25f, 0.70f, 0.08f, 0.12f); // cinder cone NW
            TerrainFactory.AddHill(terrain, 0.78f, 0.30f, 0.07f, 0.10f); // cinder cone SE
            TerrainFactory.AddHill(terrain, 0.60f, 0.75f, 0.06f, 0.08f); // small vent NE
            TerrainFactory.AddHill(terrain, 0.35f, 0.25f, 0.05f, 0.07f); // small vent SW

            // ── Splatmap ───────────────────────────────────────────
            TerrainFactory.PaintSplatmap(terrain, (nx, nz, height, steepness) =>
            {
                float[] w = new float[16];
                if (steepness > 40f) { w[7]=0.8f; w[5]=0.2f; }
                else if (height > 0.30f) { w[7]=0.6f; w[10]=0.3f; w[5]=0.1f; }
                else if (height > 0.10f) { w[5]=0.5f; w[7]=0.3f; w[10]=0.2f; }
                else
                {
                    float n = Mathf.PerlinNoise(nx*10f+40f, nz*10f+40f);
                    w[5]=0.6f+0.2f*n; w[7]=0.2f; w[10]=0.1f; w[2]=0.1f*(1f-n);
                }
                return w;
            });

            // ── District builders ──────────────────────────────────
            BuildCalderaAndLava();
            BuildVolcanicMountains();
            BuildMagmaFormations();
            BuildVolcanicProps();
            BuildFireAndAtmosphere();

            // ── Spawn + bounds ─────────────────────────────────────
            // why: spawns pushed to outer ring (~230) away from caldera & new magma fields
            AddSpawnPoints(
                new Vector3(-220f, 3f,  180f),  new Vector3(220f, 3f,  180f),
                new Vector3(-220f, 3f, -180f),  new Vector3(220f, 3f, -180f),
                new Vector3(-260f, 3f,    0f),  new Vector3(260f, 3f,    0f),
                new Vector3(   0f, 3f,  260f),  new Vector3(  0f, 3f, -260f)
            );
            AddInvisibleWalls(375f, 50f);
        }

        // ── CALDERA: lava pool + streams ───────────────────────────
        private void BuildCalderaAndLava()
        {
            // ── Central caldera lava pool ────────────────────────────
            AddLavaHazard(new Vector3(0f, 22f, 0f), new Vector3(36f, 2f, 36f), "CalderaLava");

            // ── 5 lava streams radiating from caldera like spokes ────
            AddLavaHazard(new Vector3(60f, 1f, 0f), new Vector3(80f, 1f, 6f), "LavaStream_E");
            AddLavaHazard(new Vector3(-55f, 1f, 40f), new Vector3(70f, 1f, 5f), "LavaStream_NW");
            AddLavaHazard(new Vector3(30f, 1f, -70f), new Vector3(6f, 1f, 90f), "LavaStream_S");
            AddLavaHazard(new Vector3(-70f, 1f, -50f), new Vector3(60f, 1f, 5f), "LavaStream_SW");
            AddLavaHazard(new Vector3(40f, 1f, 60f), new Vector3(5f, 1f, 70f), "LavaStream_NE");

            // ── Obsidian volcanic pillars around the caldera rim ─────
            Color obsidian = new Color(0.08f, 0.06f, 0.10f);
            Color ashGray = new Color(0.40f, 0.38f, 0.35f);

            AddCylinder(new Vector3(-40f, 20f, 30f), 4f, 15f, obsidian, "Pillar_1");
            AddCylinder(new Vector3(35f, 20f, 35f), 3.5f, 18f, obsidian, "Pillar_2");
            AddCylinder(new Vector3(45f, 20f, -25f), 4.5f, 12f, ashGray, "Pillar_3");
            AddCylinder(new Vector3(-30f, 20f, -40f), 3f, 16f, obsidian, "Pillar_4");
            AddCylinder(new Vector3(-50f, 20f, -10f), 5f, 14f, ashGray, "Pillar_5");
            AddCylinder(new Vector3(10f, 20f, -45f), 3.5f, 20f, obsidian, "Pillar_6");

            // ── Obsidian shard blocks (tall thin dark spikes) ────────
            AddBlockUnchecked(new Vector3(25f, 20f, 50f), new Vector3(2f, 10f, 3f), obsidian, "Shard_N");
            AddBlockUnchecked(new Vector3(-55f, 20f, -30f), new Vector3(3f, 12f, 2f), obsidian, "Shard_SW");
            AddBlockUnchecked(new Vector3(50f, 20f, -40f), new Vector3(2f, 8f, 2.5f), obsidian, "Shard_SE");
            AddBlockUnchecked(new Vector3(-20f, 20f, -55f), new Vector3(2.5f, 11f, 2f), obsidian, "Shard_S");
        }

        // ── VOLCANIC MOUNTAINS: dark peaks around perimeter ────────
        private void BuildVolcanicMountains()
        {
            Color volcanicTint = new Color(0.4f, 0.3f, 0.3f);
            GameObject mtn;

            // ── North wall (z = 240..270): 3 canyon + 2 magma ───────
            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-200f, 0f, 255f), 15f, 7f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_01",
                new Vector3(-100f, 0f, 245f), 60f, 4f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(0f, 0f, 260f), 90f, 8f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_02",
                new Vector3(100f, 0f, 248f), 150f, 3f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(200f, 0f, 252f), 200f, 6f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            // ── South wall (z = -270..-240): 3 canyon + 2 magma ─────
            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-190f, 0f, -258f), 180f, 7f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_03",
                new Vector3(-80f, 0f, -245f), 240f, 5f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(30f, 0f, -265f), 135f, 8f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_01",
                new Vector3(130f, 0f, -250f), 300f, 4f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(210f, 0f, -255f), 45f, 6f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            // ── East wall (x = 240..270): 3 canyon + 1 magma ────────
            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(260f, 0f, -150f), 90f, 7f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_02",
                new Vector3(250f, 0f, -40f), 120f, 5f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(265f, 0f, 60f), 270f, 6f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(255f, 0f, 170f), 160f, 5f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            // ── West wall (x = -270..-240): 3 canyon + 1 magma ─────
            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-265f, 0f, -160f), 0f, 6f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-258f, 0f, -40f), 315f, 7f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_03",
                new Vector3(-252f, 0f, 70f), 210f, 4f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(-260f, 0f, 180f), 100f, 5f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);
        }

        // ── MAGMA FORMATIONS: rocks, platforms, cave ───────────────
        private void BuildMagmaFormations()
        {
            // ── 8 rock clusters (2-3 rocks each) ────────────────────
            // Cluster 1 – NE quadrant
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(100f, 2f, 80f), 0f, 3.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(108f, 2f, 74f), 45f, 2.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(94f, 2f, 88f), 120f, 2.0f);

            // Cluster 2 – NW quadrant
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(-110f, 2f, 90f), 90f, 3.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(-102f, 2f, 96f), 200f, 2.8f);

            // Cluster 3 – SE quadrant
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(90f, 2f, -100f), 180f, 3.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(98f, 2f, -94f), 270f, 2.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(83f, 2f, -108f), 135f, 3.2f);

            // Cluster 4 – SW quadrant
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(-95f, 2f, -110f), 60f, 4.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(-103f, 2f, -104f), 150f, 2.5f);

            // Cluster 5 – east-center
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(140f, 2f, -20f), 30f, 3.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(148f, 2f, -14f), 210f, 2.8f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(133f, 2f, -28f), 300f, 2.2f);

            // Cluster 6 – west-center
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(-140f, 2f, 30f), 270f, 3.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(-132f, 2f, 24f), 90f, 3.8f);

            // Cluster 7 – north-center
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(-20f, 2f, 130f), 15f, 2.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(-12f, 2f, 138f), 165f, 3.0f);

            // Cluster 8 – south-center
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(25f, 2f, -135f), 240f, 3.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(33f, 2f, -128f), 75f, 2.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(18f, 2f, -142f), 330f, 2.0f);

            // ── 4 elevated platforms (cover) ────────────────────────
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_01",
                new Vector3(160f, 2f, 120f), 0f, 2.5f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_02",
                new Vector3(-155f, 2f, -115f), 90f, 2.8f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_01",
                new Vector3(-160f, 2f, 130f), 180f, 2.2f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_02",
                new Vector3(150f, 2f, -125f), 270f, 3.0f);

            // ── 2 caves (major cover) ───────────────────────────────
            // NW sector
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaCave",
                new Vector3(-120f, 2f, 140f), 45f, 3.5f);
            // SE sector
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaCave",
                new Vector3(125f, 2f, -145f), 225f, 4.0f);

            // ── 6 standalone large boulders (scattered cover) ───────
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(60f, 2f, 50f), 0f, 4.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(-70f, 2f, -60f), 120f, 3.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(-55f, 2f, 70f), 210f, 5.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(75f, 2f, -55f), 315f, 4.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(170f, 2f, 10f), 60f, 3.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(-175f, 2f, -15f), 150f, 3.5f);

            // ── Outer ring magma fields (new expansion zone) ────────
            string[] magmaRocks = { "MagmaRock_01", "MagmaRock_02", "MagmaRock_03" };
            for (int c = 0; c < 6; c++)
            {
                float t = (c / 6f) * Mathf.PI * 2f;
                Vector3 anchor = new Vector3(Mathf.Cos(t) * 270f, 2f, Mathf.Sin(t) * 270f);
                int count = Random.Range(3, 6);
                for (int i = 0; i < count; i++)
                {
                    float ox = Random.Range(-22f, 22f);
                    float oz = Random.Range(-22f, 22f);
                    VolcanicPrefabHelper.PlaceMagmaRock(transform,
                        magmaRocks[Random.Range(0, magmaRocks.Length)],
                        new Vector3(anchor.x + ox, 2f, anchor.z + oz),
                        Random.Range(0f, 360f), Random.Range(2.2f, 4.0f));
                }
            }

            // ── Additional outer platforms & caves ──────────────────
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_01",
                new Vector3( 240f, 2f,  220f), 30f, 2.8f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_02",
                new Vector3(-240f, 2f,  230f), 120f, 2.6f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_01",
                new Vector3( 250f, 2f, -230f), 210f, 3.0f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_02",
                new Vector3(-260f, 2f, -210f), 300f, 2.4f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaCave",
                new Vector3( 280f, 2f,   40f), 90f, 3.8f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaCave",
                new Vector3(-280f, 2f,  -30f), 270f, 3.8f);

            // ── Outer obsidian shard field ──────────────────────────
            Color obsidian = new Color(0.08f, 0.06f, 0.10f);
            for (int i = 0; i < 14; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(200f, 310f);
                float x = Mathf.Cos(t) * r + Random.Range(-10f, 10f);
                float z = Mathf.Sin(t) * r + Random.Range(-10f, 10f);
                float h = Random.Range(6f, 14f);
                AddBlockUnchecked(new Vector3(x, h, z),
                    new Vector3(Random.Range(1.5f, 3.5f), h * 2f, Random.Range(1.5f, 3.5f)),
                    obsidian, $"OuterShard_{i}");
            }
        }

        // ── VOLCANIC PROPS: trees, grass, obsidian, wood crosses ───
        private void BuildVolcanicProps()
        {
            // Helper: generate a position on the arena floor, avoiding central caldera
            // why: range ±320 covers the expanded 750 arena (half-extent 375 minus buffer)
            Vector3 ArenaPos()
            {
                float x, z;
                do
                {
                    x = Random.Range(-320f, 320f);
                    z = Random.Range(-320f, 320f);
                } while (Mathf.Sqrt(x * x + z * z) < 50f);
                return new Vector3(x, 2f, z);
            }

            // ── MagmaTree (22-30): dead charred trees, some clustered ───
            int treeCount = Random.Range(22, 31);
            int treesPlaced = 0;
            while (treesPlaced < treeCount)
            {
                int clusterSize = (Random.value < 0.3f) ? Random.Range(2, 4) : 1;
                Vector3 anchor = ArenaPos();

                for (int c = 0; c < clusterSize && treesPlaced < treeCount; c++)
                {
                    float ox = (c == 0) ? 0f : Random.Range(-8f, 8f);
                    float oz = (c == 0) ? 0f : Random.Range(-8f, 8f);
                    Vector3 pos = new Vector3(anchor.x + ox, 2f, anchor.z + oz);
                    if (Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z) < 50f) continue;

                    float rot = Random.Range(0f, 360f);
                    float scl = Random.Range(1.5f, 3.0f);
                    VolcanicPrefabHelper.PlaceMagmaProp(transform, "MagmaTree", pos, rot, scl);
                    treesPlaced++;
                }
            }

            // ── MagmaGrass (34-42): volcanic scrub scattered everywhere ─
            int grassCount = Random.Range(34, 43);
            for (int i = 0; i < grassCount; i++)
            {
                Vector3 pos = ArenaPos();
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.5f, 2.5f);
                VolcanicPrefabHelper.PlaceMagmaProp(transform, "MagmaGrass", pos, rot, scl);
            }

            // ── MagmaWoodCross (14-18): eerie markers near edges & caves ─
            int crossCount = Random.Range(14, 19);
            for (int i = 0; i < crossCount; i++)
            {
                // Bias toward outer ring (60-290) for lava-edge / cave feel
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float radius = Random.Range(60f, 290f);
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                Vector3 pos = new Vector3(x, 2f, z);

                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.5f, 2.0f);
                VolcanicPrefabHelper.PlaceMagmaProp(transform, "MagmaWoodCross", pos, rot, scl);
            }
        }

        // ── FIRE VFX + ATMOSPHERE ──────────────────────────────────
        private void BuildFireAndAtmosphere()
        {
            // ── Atmosphere ─────────────────────────────────────────
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.20f, 0.12f, 0.08f); // dark volcanic haze
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.35f, 0.25f, 0.20f); // warm dim volcanic light

            // ── Directional light (orange volcanic glow from below) ─
            var lightObj = new GameObject("VolcanicGlow");
            lightObj.transform.SetParent(transform, false);
            var sun = lightObj.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.5f, 0.2f); // orange lava glow
            sun.intensity = 0.7f;
            sun.transform.rotation = Quaternion.Euler(40f, -20f, 0f);
            sun.shadows = LightShadows.Soft;

            // ── Fire VFX at caldera crater rim (8 fires in a ring) ──
            var firePrefab = Resources.Load<GameObject>("VFX/Fire/LargeFlames");
            if (firePrefab != null)
            {
                for (int f = 0; f < 8; f++)
                {
                    float angle = (360f / 8) * f * Mathf.Deg2Rad;
                    Vector3 pos = new Vector3(Mathf.Cos(angle) * 18f, 24f, Mathf.Sin(angle) * 18f);
                    var fire = Object.Instantiate(firePrefab, transform);
                    fire.name = $"CraterFire_{f}";
                    fire.transform.localPosition = pos;
                    fire.transform.localScale = Vector3.one * 2f;
                    foreach (var ps in fire.GetComponentsInChildren<ParticleSystem>())
                    { var main = ps.main; main.loop = true; }
                }
            }

            // ── Smoke columns rising from the caldera ───────────────
            var smokePrefab = Resources.Load<GameObject>("VFX/Smoke/SmokeEffect");
            if (smokePrefab != null)
            {
                Vector3[] smokePositions = {
                    new Vector3(0f, 24f, 0f),
                    new Vector3(8f, 24f, -6f),
                    new Vector3(-7f, 24f, 7f)
                };
                for (int s = 0; s < smokePositions.Length; s++)
                {
                    var smoke = Object.Instantiate(smokePrefab, transform);
                    smoke.name = $"CalderaSmoke_{s}";
                    smoke.transform.localPosition = smokePositions[s];
                    smoke.transform.localScale = Vector3.one * (5f - s);
                    foreach (var ps in smoke.GetComponentsInChildren<ParticleSystem>())
                    { var m = ps.main; m.loop = true; }
                }
            }

            // ── Ambient VFX ─────────────────────────────────────────
            VFXManager.HeatDistortion(new Vector3(0, 5, 0), 8f);
            VFXManager.GroundFog(Vector3.zero, 6f);
            VFXManager.DustMotes(new Vector3(0, 5, 0), 4f);

            // ── Steam vents at secondary cinder cones ───────────────
            var steamPrefab = Resources.Load<GameObject>("VFX/Smoke/PressurisedSteam")
                           ?? Resources.Load<GameObject>("VFX/Smoke/Steam");
            if (steamPrefab != null)
            {
                Vector3[] ventPositions = {
                    new Vector3(-150f, 8f, 120f),
                    new Vector3(168f, 6f, -120f),
                    new Vector3( 260f, 6f,  210f),
                    new Vector3(-270f, 6f, -200f),
                    new Vector3( 280f, 6f,  -40f),
                    new Vector3(-290f, 6f,   50f),
                };
                for (int v = 0; v < ventPositions.Length; v++)
                {
                    var vent = Object.Instantiate(steamPrefab, transform);
                    vent.name = $"SteamVent_{v}";
                    vent.transform.localPosition = ventPositions[v];
                    vent.transform.localScale = Vector3.one * 3f;
                    foreach (var ps in vent.GetComponentsInChildren<ParticleSystem>())
                    { var m = ps.main; m.loop = true; }
                }
            }

            // ── Small fires along lava stream edges ─────────────────
            var medFirePrefab = Resources.Load<GameObject>("VFX/Fire/MediumFlames");
            if (medFirePrefab != null)
            {
                Vector3[] lavaFirePositions = {
                    new Vector3(40f, 1.5f, 3f),     // east stream edge
                    new Vector3(80f, 1.5f, -2f),    // east stream far
                    new Vector3(-35f, 1.5f, 42f),   // NW stream edge
                    new Vector3(-60f, 1.5f, 38f),   // NW stream far
                    new Vector3(30f, 1.5f, -40f),   // south stream edge
                    new Vector3(42f, 1.5f, 55f)     // NE stream edge
                };
                for (int lf = 0; lf < lavaFirePositions.Length; lf++)
                {
                    var lavaFire = Object.Instantiate(medFirePrefab, transform);
                    lavaFire.name = $"LavaFire_{lf}";
                    lavaFire.transform.localPosition = lavaFirePositions[lf];
                    lavaFire.transform.localScale = Vector3.one * 1.5f;
                    foreach (var ps in lavaFire.GetComponentsInChildren<ParticleSystem>())
                    { var m = ps.main; m.loop = true; }
                }
            }
        }
    }

    // =========================================================================
    // 5. GroundHighlands -- Kyrgyzstan: vast mountain-enclosed steppe valley
    //    with river, farms, snow peaks, and dense nature from Snow Mountain,
    //    Stylized Nature Kit Lite, and Nature Starter Kit 2.
    // =========================================================================

    public class GroundHighlands : ArenaBase
    {
        public override string ArenaName => "Kyrgyzstan";

        public override void Build()
        {
            // ── Terrain: 750x750, 80m height (why: 2.5x playable area) ───
            var terrain = TerrainFactory.Create(transform,
                new Vector3(-375f, 0f, -375f), new Vector3(750f, 80f, 750f),
                513, "HighlandsTerrain");

            TerrainFactory.SetHeights(terrain, (nx, nz) =>
            {
                float h = 0.04f; // valley floor

                // Gentle rolling hills only in the mid-ring (NOT in center)
                float cx = nx - 0.5f, cz = nz - 0.5f;
                float cDist = Mathf.Sqrt(cx * cx + cz * cz);

                // Mountain formations only beyond the valley (cDist > 0.20)
                float mountainMask = Mathf.SmoothStep(0f, 1f, (cDist - 0.18f) / 0.10f);
                h += mountainMask * 0.08f * Mathf.PerlinNoise(nx * 2.5f + 3f, nz * 2.5f + 3f);
                h += mountainMask * 0.04f * Mathf.PerlinNoise(nx * 5f + 7f, nz * 5f + 7f);

                // Tall mountain wall at all edges (enclosure)
                float edgeDist = Mathf.Max(Mathf.Abs(nx - 0.5f), Mathf.Abs(nz - 0.5f));
                if (edgeDist > 0.30f)
                    h += 0.35f * Mathf.SmoothStep(0f, 1f, (edgeDist - 0.30f) / 0.15f);

                // Wide flat central valley -- force flat in center
                if (cDist < 0.18f)
                    h = 0.04f; // perfectly flat steppe floor

                // River channel N-S
                float riverDist = Mathf.Abs(nx - 0.5f);
                if (riverDist < 0.02f)
                    h = Mathf.Min(h, 0.02f);

                h += 0.006f * Mathf.PerlinNoise(nx * 20f, nz * 20f);
                h += 0.002f * Mathf.PerlinNoise(nx * 50f + 100f, nz * 50f + 100f);
                return Mathf.Clamp01(h);
            });

            // Gentle foothills only at mid-ring
            TerrainFactory.AddHill(terrain, 0.20f, 0.65f, 0.06f, 0.10f);
            TerrainFactory.AddHill(terrain, 0.75f, 0.40f, 0.06f, 0.08f);

            // Wide flat areas for gameplay
            TerrainFactory.Flatten(terrain, 0.30f, 0.30f, 0.70f, 0.70f, 0.04f); // main valley
            TerrainFactory.Flatten(terrain, 0.28f, 0.43f, 0.38f, 0.50f, 0.04f); // farm area
            TerrainFactory.Flatten(terrain, 0.55f, 0.38f, 0.65f, 0.48f, 0.04f); // village area

            // ── Splatmap ───────────────────────────────────────────
            TerrainFactory.PaintSplatmap(terrain, (nx, nz, height, steepness) =>
            {
                float[] w = new float[16];
                if (steepness > 40f)       { w[7]=0.8f; w[10]=0.2f; }
                else if (steepness > 25f)  { w[7]=0.4f; w[11]=0.4f; w[2]=0.2f; }
                else if (height > 0.45f)   { w[6]=0.6f; w[7]=0.3f; w[10]=0.1f; }
                else if (height > 0.25f)   { w[11]=0.4f; w[3]=0.3f; w[2]=0.2f; w[10]=0.1f; }
                else if (height > 0.10f)
                {
                    float n = Mathf.PerlinNoise(nx*8f+60f, nz*8f+60f);
                    w[2]=0.5f+0.2f*n; w[3]=0.3f-0.1f*n; w[11]=0.2f;
                }
                else
                {
                    float n = Mathf.PerlinNoise(nx*5f+70f, nz*5f+70f);
                    w[2]=0.6f; w[3]=0.2f; w[10]=0.1f+0.1f*n; w[11]=0.1f*(1f-n);
                }
                return w;
            });

            // ── District builders ──────────────────────────────────
            BuildMountainEnclosure();
            BuildRiverValley();
            BuildSteppeVegetation();
            BuildFarmAndVillage();
            BuildRockFormations();

            // Big central Kyrgyz flag
            SpawnFlag(new Vector3(0f, 4f, 0f), 6f, "CentralFlag");

            // Animated horse herds roaming the steppe
            var herdObj = new GameObject("HorseHerd");
            herdObj.transform.SetParent(transform, false);
            herdObj.transform.localPosition = Vector3.zero;
            var herd = herdObj.AddComponent<HorseHerd>();
            herd.horseCount = 45; // why: larger arena needs more visual activity
            herd.spawnRadius = 220f;
            herd.roamRadius = 50f;

            // ── Spawn + bounds ─────────────────────────────────────
            // why: spawns pushed outward into outer steppe (~230) for the bigger arena
            AddSpawnPoints(
                new Vector3(-200f, 5f,  170f), new Vector3( 200f, 5f,  170f),
                new Vector3(-200f, 5f, -170f), new Vector3( 200f, 5f, -170f),
                new Vector3(-260f, 4f,    0f), new Vector3( 260f, 4f,    0f),
                new Vector3(   0f, 5f,  260f), new Vector3(   0f, 5f, -260f)
            );
            AddInvisibleWalls(375f, 70f);

            // ── Highland sun (soft, slightly warm) ─────────────────
            var hSun = new GameObject("HighlandsSun");
            hSun.transform.SetParent(transform, false);
            var hsl = hSun.AddComponent<Light>();
            hsl.type = LightType.Directional;
            hsl.color = new Color(1f, 0.96f, 0.88f);
            hsl.intensity = 0.95f;
            hsl.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            hsl.shadows = LightShadows.Soft;

            // ── Atmosphere: moody highland mist ─────────────────────
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.70f, 0.74f, 0.78f); // muted gray-blue mist
            RenderSettings.fogDensity = 0.0038f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.64f, 0.66f, 0.72f);

            VFXManager.GroundFog(Vector3.zero, 5f);
            VFXManager.GroundFog(new Vector3(-240f, 3f,  220f), 4f);
            VFXManager.GroundFog(new Vector3( 240f, 3f, -220f), 4f);
            VFXManager.DustMotes(new Vector3(0, 5, 0), 4f);
            VFXManager.Rain(new Vector3(0, 20, 0), 2f);
        }

        // ── MOUNTAIN ENCLOSURE: tall peaks around all edges ────────
        private void BuildMountainEnclosure()
        {
            // why: peaks pushed to ~340 to hug the new 375 half-extent wall
            // ── Center backdrop: dominant snow peak ──────────────────
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(0f, 0f, 345f), 0f, 12f);

            // ── North wall: 6 snow/canyon mountains ──
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-260f, 0f, 330f), 10f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(-140f, 0f, 340f), 45f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3( -30f, 0f, 320f), 90f, 8f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3( 120f, 0f, 335f), 160f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3( 240f, 0f, 325f), 200f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3( 320f, 0f, 300f), 230f, 6f);

            // ── South wall ─────────────────────────────────────────
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-260f, 0f, -335f), 180f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-120f, 0f, -345f), 135f, 8f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(  30f, 0f, -325f), 270f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3( 160f, 0f, -340f), 220f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3( 270f, 0f, -320f), 60f, 6f);

            // ── East wall ──────────────────────────────────────────
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(340f, 0f, -200f), 90f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(335f, 0f,  -60f), 120f, 8f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(345f, 0f,   80f), 75f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(335f, 0f,  220f), 150f, 7f);

            // ── West wall ──────────────────────────────────────────
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(-345f, 0f, -200f), 0f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-335f, 0f,  -60f), 270f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(-340f, 0f,   80f), 315f, 8f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(-335f, 0f,  220f), 190f, 6f);

            // ── Corner peaks ───────────────────────────────────────
            HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_01",
                new Vector3( 325f, 0f,  325f), 45f, 5f);
            HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_02",
                new Vector3(-325f, 0f,  325f), 135f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_03",
                new Vector3( 325f, 0f, -325f), 315f, 5f);
            HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_01",
                new Vector3(-325f, 0f, -325f), 225f, 6f);
        }

        // ── RIVER VALLEY: water, bridges, riverside vegetation ─────
        private void BuildRiverValley()
        {
            // ── River water (Fentchester canal style) ──────────────
            {
                Color riverBlue = new Color(0.18f, 0.42f, 0.62f);
                var riverGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                riverGO.name = "RiverWater";
                riverGO.transform.SetParent(transform, false);
                riverGO.transform.position = new Vector3(0f, 0.5f, 0f);
                riverGO.transform.localScale = new Vector3(14f, 0.3f, 500f);
                Object.DestroyImmediate(riverGO.GetComponent<Collider>());

                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Smoothness", 0.92f);
                mat.SetFloat("_Metallic", 0.1f);
                mat.color = new Color(riverBlue.r, riverBlue.g, riverBlue.b, 0.5f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                SetMaterial(riverGO, mat);
            }
            AddWaterHazard(new Vector3(0f, 0f, 0f), new Vector3(14f, 3f, 500f), "RiverHazard");

            // ── Stone bridges (3 crossings) ────────────────────────
            Color stone = new Color(0.55f, 0.52f, 0.48f);
            AddBridge(new Vector3(-8f, 2f, 60f), new Vector3(8f, 2f, 60f), 8f, 1f, stone, "Bridge_N");
            AddBridge(new Vector3(-8f, 2f, -30f), new Vector3(8f, 2f, -30f), 8f, 1f, stone, "Bridge_Center");
            AddBridge(new Vector3(-8f, 2f, -100f), new Vector3(8f, 2f, -100f), 8f, 1f, stone, "Bridge_S");

            // ── Riverside bushes (both banks) ──────────────────────
            HighlandsPrefabHelper.PlaceBush(transform, "bush01", new Vector3(-12f, 1f, 130f), 0f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush03", new Vector3(-15f, 1f, 80f), 45f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush05", new Vector3(-11f, 1f, 20f), 120f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush02", new Vector3(-18f, 1f, -40f), 200f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush04", new Vector3(-13f, 1f, -90f), 70f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush06", new Vector3(-16f, 1f, -140f), 310f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush02", new Vector3(12f, 1f, 120f), 15f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush04", new Vector3(14f, 1f, 50f), 160f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush06", new Vector3(17f, 1f, -10f), 90f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush01", new Vector3(11f, 1f, -70f), 250f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush03", new Vector3(19f, 1f, -120f), 30f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush05", new Vector3(13f, 1f, -145f), 180f);

            // ── Riverside trees ────────────────────────────────────
            HighlandsPrefabHelper.PlaceTree(transform, "tree01", new Vector3(-20f, 1f, 100f), 0f);
            HighlandsPrefabHelper.PlaceTree(transform, "tree02", new Vector3(18f, 1f, 40f), 90f);
            HighlandsPrefabHelper.PlaceTree(transform, "tree03", new Vector3(-17f, 1f, -50f), 180f);
            HighlandsPrefabHelper.PlaceTree(transform, "tree04", new Vector3(15f, 1f, -110f), 270f);
            HighlandsPrefabHelper.PlaceTree(transform, "tree01", new Vector3(20f, 1f, 140f), 45f);

            // ── River stones along banks ───────────────────────────
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 1", new Vector3(-9f, 1f, 110f), 30f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 2", new Vector3(9f, 1f, 70f), 150f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 3", new Vector3(-10f, 1f, 10f), 80f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 4", new Vector3(10f, 1f, -30f), 220f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 5", new Vector3(-8f, 1f, -80f), 300f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 1", new Vector3(8f, 1f, -130f), 60f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 3", new Vector3(-11f, 1f, -140f), 170f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 5", new Vector3(11f, 1f, 145f), 110f);
        }

        // ── STEPPE VEGETATION: trees, bushes, grass, flowers ───────
        private void BuildSteppeVegetation()
        {
            // Prefab name arrays
            string[] treeVariants = { "tree01", "tree02", "tree03", "tree04",
                                      "Spruce 1", "Spruce 2", "IceTree" };
            string[] bushVariants = { "bush01", "bush02", "bush03", "bush04",
                                      "bush05", "bush06", "Bush" };

            // Helper: returns true if position is in the river or farm exclusion zones
            bool IsExcluded(float x, float z)
            {
                if (x > -8f && x < 8f) return true;                          // river
                if (x > -130f && x < -70f && z > -50f && z < 30f) return true; // farm
                return false;
            }

            // Helper: generate a valid valley-floor position, rejection-sampling exclusions
            // why: range ±320 fills the expanded 750 arena
            Vector3 ValleyPos()
            {
                float x, z;
                do
                {
                    x = Random.Range(-320f, 320f);
                    z = Random.Range(-320f, 320f);
                } while (IsExcluded(x, z));
                return new Vector3(x, 2f, z);
            }

            // ── Scattered trees (55-65): mix of all 7 variants ──────────
            int treeCount = Random.Range(55, 66);
            int placed = 0;
            while (placed < treeCount)
            {
                // Occasionally place a cluster of 2-3 trees
                int clusterSize = (Random.value < 0.35f) ? Random.Range(2, 4) : 1;
                Vector3 anchor = ValleyPos();

                for (int c = 0; c < clusterSize && placed < treeCount; c++)
                {
                    float ox = (c == 0) ? 0f : Random.Range(-6f, 6f);
                    float oz = (c == 0) ? 0f : Random.Range(-6f, 6f);
                    Vector3 pos = new Vector3(anchor.x + ox, 2f, anchor.z + oz);
                    if (IsExcluded(pos.x, pos.z)) continue;

                    string name = treeVariants[Random.Range(0, treeVariants.Length)];
                    float rot = Random.Range(0f, 360f);
                    float scl = Random.Range(1.0f, 2.0f);
                    HighlandsPrefabHelper.PlaceTree(transform, name, pos, rot, scl);
                    placed++;
                }
            }

            // ── Spruce groves at foothills (4-6 clusters of 5-8) ────────
            int groveCount = Random.Range(4, 7);
            float[] groveSigns = { -1f, 1f };
            for (int g = 0; g < groveCount; g++)
            {
                float gx = groveSigns[g % 2] * Random.Range(180f, 280f);
                float gz = Random.Range(-260f, 260f);
                int spruceCount = Random.Range(5, 9);

                for (int s = 0; s < spruceCount; s++)
                {
                    float sx = gx + Random.Range(-10f, 10f);
                    float sz = gz + Random.Range(-10f, 10f);
                    if (IsExcluded(sx, sz)) continue;

                    string spruce = (Random.value < 0.5f) ? "Spruce 1" : "Spruce 2";
                    float rot = Random.Range(0f, 360f);
                    float scl = Random.Range(1.5f, 2.5f);
                    HighlandsPrefabHelper.PlaceTree(transform, spruce,
                        new Vector3(sx, 2f, sz), rot, scl);
                }
            }

            // ── Bushes (70-85): dense coverage with all 7 variants ──────
            int bushCount = Random.Range(70, 86);
            for (int i = 0; i < bushCount; i++)
            {
                Vector3 pos = ValleyPos();
                string name = bushVariants[Random.Range(0, bushVariants.Length)];
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(0.8f, 1.5f);
                HighlandsPrefabHelper.PlaceBush(transform, name, pos, rot, scl);
            }

            // ── Grass patches (45-55): scattered everywhere ─────────────
            int grassCount = Random.Range(45, 56);
            for (int i = 0; i < grassCount; i++)
            {
                Vector3 pos = ValleyPos();
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.5f, 3.0f);
                HighlandsPrefabHelper.PlaceFoliage(transform, "Grass", pos, rot, scl);
            }

            // ── Flowers (28-36): scattered among grass ──────────────────
            int flowerCount = Random.Range(28, 37);
            for (int i = 0; i < flowerCount; i++)
            {
                Vector3 pos = ValleyPos();
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.0f, 2.0f);
                HighlandsPrefabHelper.PlaceFoliage(transform, "Flower", pos, rot, scl);
            }

            // ── Stumps and logs (16-22): near tree clusters ──────────────
            int debrisCount = Random.Range(16, 23);
            for (int i = 0; i < debrisCount; i++)
            {
                Vector3 pos = ValleyPos();
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.0f, 1.5f);
                string debris = (Random.value < 0.5f) ? "Stump" : "Log";
                HighlandsPrefabHelper.PlaceFoliage(transform, debris, pos, rot, scl);
            }
        }

        // ── FARM & VILLAGE: buildings, fences, windmill ────────────
        private void BuildFarmAndVillage()
        {
            // ── Colors ────────────────────────────────────────────
            Color farmBrown  = new Color(0.55f, 0.40f, 0.22f);
            Color roofThatch = new Color(0.60f, 0.50f, 0.30f);
            Color stoneGray  = new Color(0.55f, 0.52f, 0.48f);
            Color stone      = new Color(0.60f, 0.58f, 0.55f);
            Color blade      = new Color(0.85f, 0.82f, 0.78f);

            // ══════════════════════════════════════════════════════
            //  FARM AREA  (x=-130..-70, z=-50..30)
            // ══════════════════════════════════════════════════════

            // ── Farm yurts ────────────────────────────────────────
            CreateYurt(new Vector3(-100f, 4f, -20f), 12f, 3f, 2f, "Yurt_Farm_1");
            CreateYurt(new Vector3(-115f, 4f, 5f), 14f, 3f, 2.5f, "Yurt_Farm_2");
            CreateYurt(new Vector3(-80f, 4f, 15f), 10f, 2.5f, 1.5f, "Yurt_Farm_3");

            // ── Stone walls around farm perimeter ─────────────────
            // North wall
            AddWall(new Vector3(-130f, 2f, 30f), new Vector3(-70f, 2f, 30f),
                2.5f, 0.6f, stoneGray, "FarmWall_N");
            // South wall
            AddWall(new Vector3(-130f, 2f, -50f), new Vector3(-70f, 2f, -50f),
                2.5f, 0.6f, stoneGray, "FarmWall_S");
            // West wall
            AddWall(new Vector3(-130f, 2f, -50f), new Vector3(-130f, 2f, 30f),
                2.5f, 0.6f, stoneGray, "FarmWall_W");
            // East wall (gap for entrance)
            AddWall(new Vector3(-70f, 2f, -50f), new Vector3(-70f, 2f, -10f),
                2.5f, 0.6f, stoneGray, "FarmWall_E_S");
            AddWall(new Vector3(-70f, 2f, 10f), new Vector3(-70f, 2f, 30f),
                2.5f, 0.6f, stoneGray, "FarmWall_E_N");

            // ── Wooden fences along farm perimeter ────────────────
            // Inner fences along south edge
            for (float x = -125f; x <= -75f; x += 10f)
                HighlandsPrefabHelper.PlaceFoliage(transform, "FenceWood",
                    new Vector3(x, 2f, -45f), 0f, 1.5f);
            // Inner fences along north edge
            for (float x = -125f; x <= -75f; x += 10f)
                HighlandsPrefabHelper.PlaceFoliage(transform, "FenceWood",
                    new Vector3(x, 2f, 25f), 0f, 1.5f);
            // Inner fences along west edge
            for (float z = -40f; z <= 20f; z += 10f)
                HighlandsPrefabHelper.PlaceFoliage(transform, "FenceWood",
                    new Vector3(-125f, 2f, z), 90f, 1.5f);
            // Inner fences along east edge
            for (float z = -40f; z <= 20f; z += 10f)
                HighlandsPrefabHelper.PlaceFoliage(transform, "FenceWood",
                    new Vector3(-75f, 2f, z), 90f, 1.5f);

            // ── Farm props ────────────────────────────────────────
            // Firewood stacks near farmhouse
            HighlandsPrefabHelper.PlaceFoliage(transform, "Log",
                new Vector3(-93f, 2f, -25f));
            HighlandsPrefabHelper.PlaceFoliage(transform, "Log",
                new Vector3(-106f, 2f, -18f));
            // Chopping blocks
            HighlandsPrefabHelper.PlaceFoliage(transform, "Stump",
                new Vector3(-95f, 2f, -15f));
            HighlandsPrefabHelper.PlaceFoliage(transform, "Stump",
                new Vector3(-112f, 2f, -5f));
            // Garden bushes around buildings
            HighlandsPrefabHelper.PlaceBush(transform, "bush01",
                new Vector3(-88f, 2f, -22f));
            HighlandsPrefabHelper.PlaceBush(transform, "bush01",
                new Vector3(-105f, 2f, 12f));
            HighlandsPrefabHelper.PlaceBush(transform, "bush01",
                new Vector3(-78f, 2f, 10f));

            // ══════════════════════════════════════════════════════
            //  VILLAGE AREA  (x=30..80, z=-30..30)
            // ══════════════════════════════════════════════════════

            // ── Village yurts ─────────────────────────────────────
            CreateYurt(new Vector3(45f, 4f, -10f), 11f, 3f, 2f, "Yurt_Village_1");
            CreateYurt(new Vector3(60f, 4f, 10f), 10f, 3f, 2f, "Yurt_Village_2");
            CreateYurt(new Vector3(38f, 4f, 15f), 10f, 2.5f, 1.5f, "Yurt_Village_3");
            CreateYurt(new Vector3(70f, 4f, -15f), 12f, 3f, 2f, "Yurt_Village_4");

            // ── Nomadic camp yurts (scattered in valley) ──────────
            CreateYurt(new Vector3(10f, 4f, -40f), 11f, 3f, 2f, "Yurt_Camp_1");
            CreateYurt(new Vector3(-20f, 4f, 50f), 12f, 3f, 2.5f, "Yurt_Camp_2");
            CreateYurt(new Vector3(25f, 4f, 65f), 10f, 2.5f, 1.5f, "Yurt_Camp_3");

            // ── Stone walls connecting village buildings ───────────
            AddWall(new Vector3(35f, 2f, -25f), new Vector3(75f, 2f, -25f),
                2f, 0.5f, stoneGray, "VillageWall_S");
            AddWall(new Vector3(35f, 2f, 25f), new Vector3(75f, 2f, 25f),
                2f, 0.5f, stoneGray, "VillageWall_N");
            AddWall(new Vector3(35f, 2f, -25f), new Vector3(35f, 2f, 25f),
                2f, 0.5f, stoneGray, "VillageWall_W");
            AddWall(new Vector3(75f, 2f, -25f), new Vector3(75f, 2f, 25f),
                2f, 0.5f, stoneGray, "VillageWall_E");

            // ── Village props ─────────────────────────────────────
            // Firewood
            HighlandsPrefabHelper.PlaceFoliage(transform, "Log",
                new Vector3(50f, 2f, -15f));
            HighlandsPrefabHelper.PlaceFoliage(transform, "Log",
                new Vector3(65f, 2f, 5f));
            // Chopping blocks
            HighlandsPrefabHelper.PlaceFoliage(transform, "Stump",
                new Vector3(42f, 2f, 0f));
            HighlandsPrefabHelper.PlaceFoliage(transform, "Stump",
                new Vector3(68f, 2f, -8f));
            // Garden bushes
            HighlandsPrefabHelper.PlaceBush(transform, "bush01",
                new Vector3(52f, 2f, 18f));
            HighlandsPrefabHelper.PlaceBush(transform, "bush01",
                new Vector3(40f, 2f, -18f));
            HighlandsPrefabHelper.PlaceBush(transform, "bush01",
                new Vector3(72f, 2f, 12f));

            // ══════════════════════════════════════════════════════
            //  WINDMILL  (procedural, near village)
            // ══════════════════════════════════════════════════════

            // Tower
            AddCylinder(new Vector3(85f, 11f, 50f), 3f, 14f, stone, "WindmillTower");

            // Blades (static cross)
            var bladesParent = new GameObject("WindmillBlades");
            bladesParent.transform.SetParent(transform, false);
            bladesParent.transform.position = new Vector3(85f, 17f, 53.5f);

            for (int i = 0; i < 4; i++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = $"WindmillBlade_{i}";
                b.transform.SetParent(bladesParent.transform, false);
                float angle = i * 90f;
                b.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                b.transform.localPosition = Quaternion.Euler(0f, 0f, angle) * new Vector3(0f, 5f, 0f);
                b.transform.localScale = new Vector3(1.5f, 10f, 0.2f);
                SetMaterial(b, MakeMaterial(blade));
                Object.DestroyImmediate(b.GetComponent<Collider>());
            }
        }

        // ── YURT HELPER: procedural nomadic tent ────────────────────
        private static readonly Color[] _hemiColors =
        {
            new Color(0.85f, 0.75f, 0.55f), // warm tan
            new Color(0.70f, 0.35f, 0.25f), // terracotta
            new Color(0.55f, 0.65f, 0.45f), // sage green
            new Color(0.80f, 0.80f, 0.75f), // off-white
            new Color(0.50f, 0.40f, 0.30f), // brown
        };

        private void CreateYurt(Vector3 pos, float radius, float wallHeight, float roofHeight, string label)
        {
            // Hemisphere: sphere sunk halfway into the ground
            Color col = _hemiColors[Mathf.Abs(label.GetHashCode()) % _hemiColors.Length];
            AddSphere(new Vector3(pos.x, pos.y - radius, pos.z), radius, col, label);

            // Flag on top of each yurt
            SpawnFlag(new Vector3(pos.x, pos.y + 0.5f, pos.z), 3f, label + "_Flag");
        }

        private static Texture2D _kyrgyzFlagTex;

        private static Texture2D GetKyrgyzFlag()
        {
            if (_kyrgyzFlagTex != null) return _kyrgyzFlagTex;

            // Draw the Kyrgyz flag procedurally with correct 3:5 aspect ratio
            // Red background, yellow 40-ray sun, tunduk (crossed bands) in center
            int w = 500, h = 300;
            _kyrgyzFlagTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color red = new Color(0.89f, 0.07f, 0.15f);
            Color yellow = new Color(1f, 0.82f, 0f);

            float cx = w * 0.5f, cy = h * 0.5f;
            float outerRing = h * 0.38f;   // outer sun circle
            float innerRing = h * 0.28f;   // inner ring (tunduk boundary)
            float coreRadius = h * 0.18f;  // tunduk interior
            int rays = 40;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);

                    Color pixel = red;

                    if (dist < outerRing)
                    {
                        // Sun rays: 40 pointed triangles radiating outward
                        float rayAngle = angle * rays / (2f * Mathf.PI);
                        float rayShape = Mathf.Abs(Mathf.Cos(rayAngle * Mathf.PI));
                        float rayEdge = Mathf.Lerp(innerRing * 1.05f, outerRing, rayShape);

                        if (dist > innerRing && dist < rayEdge)
                            pixel = yellow; // ray
                        else if (dist <= innerRing && dist > innerRing - h * 0.02f)
                            pixel = yellow; // ring outline
                        else if (dist <= innerRing)
                        {
                            // Tunduk: crossed curved bands inside the circle
                            // Simplified as 3 pairs of crossing arcs
                            float normDist = dist / coreRadius;
                            float a = angle * Mathf.Rad2Deg;

                            // 3 crossing bands at 60 degree intervals
                            bool onBand = false;
                            for (int b = 0; b < 3; b++)
                            {
                                float bandAngle = b * 60f;
                                float relAngle = Mathf.Abs(Mathf.DeltaAngle(a, bandAngle));
                                float relAngle2 = Mathf.Abs(Mathf.DeltaAngle(a, bandAngle + 180f));
                                // Curved band: thickness varies with distance from center
                                float thickness = 12f + normDist * 8f;
                                if (relAngle < thickness || relAngle2 < thickness)
                                    onBand = true;
                            }

                            if (dist < coreRadius * 0.15f)
                                pixel = yellow; // center dot
                            else if (onBand && dist < coreRadius)
                                pixel = yellow; // tunduk bands
                            else if (dist < coreRadius)
                                pixel = red;    // red gaps between bands
                            else
                                pixel = red;    // between ring and tunduk
                        }
                    }

                    _kyrgyzFlagTex.SetPixel(x, y, pixel);
                }
            }

            _kyrgyzFlagTex.filterMode = FilterMode.Bilinear;
            _kyrgyzFlagTex.Apply();
            return _kyrgyzFlagTex;
        }

        private void SpawnFlag(Vector3 pos, float flagScale, string label)
        {
            // Pole
            Color poleColor = new Color(0.4f, 0.35f, 0.3f);
            float poleHeight = flagScale * 3f;
            AddCylinder(pos + Vector3.up * (poleHeight * 0.5f), 0.08f, poleHeight, poleColor, label + "_Pole");

            // Flag from prefab
            var flagPrefab = Resources.Load<GameObject>("Models/Flag");
            if (flagPrefab != null)
            {
                var flag = Object.Instantiate(flagPrefab, transform);
                flag.name = label;
                flag.transform.localPosition = pos + Vector3.up * (poleHeight * 0.85f);
                flag.transform.localScale = Vector3.one * flagScale * 0.5f;
                flag.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

                // Apply Kyrgyz flag texture
                Texture2D flagTex = GetKyrgyzFlag();
                foreach (var rend in flag.GetComponentsInChildren<Renderer>())
                {
                    foreach (var mat in rend.materials)
                    {
                        if (mat != null)
                        {
                            mat.mainTexture = flagTex;
                            if (mat.HasProperty("_BaseMap"))
                                mat.SetTexture("_BaseMap", flagTex);
                        }
                    }
                }
            }
            else
            {
                // Fallback: flat red quad with yellow circle
                var flagObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                flagObj.name = label;
                flagObj.transform.SetParent(transform, false);
                flagObj.transform.localPosition = pos + Vector3.up * (poleHeight * 0.85f) + Vector3.right * flagScale * 0.3f;
                flagObj.transform.localScale = new Vector3(flagScale * 0.6f, flagScale * 0.4f, 1f);
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.mainTexture = GetKyrgyzFlag();
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", GetKyrgyzFlag());
                SetMaterial(flagObj, mat);
                Object.DestroyImmediate(flagObj.GetComponent<Collider>());
            }
        }

        // ── ROCK FORMATIONS: boulders, cliffs, scattered rocks ─────
        private void BuildRockFormations()
        {
            // ── 6 cliff formation clusters (2-3 cliffs each) ─────────
            // Cluster 1 – east valley
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 1",
                new Vector3(120f, 2f, 80f), 0f, 3.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 3",
                new Vector3(128f, 2f, 75f), 45f, 2.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 5",
                new Vector3(114f, 2f, 88f), 120f, 2.0f);

            // Cluster 2 – northwest
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 2",
                new Vector3(-130f, 2f, 100f), 90f, 3.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 4",
                new Vector3(-122f, 2f, 106f), 200f, 2.8f);

            // Cluster 3 – southeast
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 1",
                new Vector3(80f, 2f, -120f), 180f, 3.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 3",
                new Vector3(88f, 2f, -114f), 270f, 2.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 5",
                new Vector3(73f, 2f, -126f), 135f, 3.2f);

            // Cluster 4 – southwest
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 2",
                new Vector3(-100f, 2f, -130f), 60f, 4.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 4",
                new Vector3(-108f, 2f, -124f), 150f, 2.5f);

            // Cluster 5 – east-center
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 5",
                new Vector3(150f, 2f, -30f), 30f, 3.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 1",
                new Vector3(158f, 2f, -24f), 210f, 2.8f);
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 3",
                new Vector3(143f, 2f, -36f), 300f, 2.2f);

            // Cluster 6 – west-center
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 4",
                new Vector3(-150f, 2f, 60f), 270f, 3.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 2",
                new Vector3(-142f, 2f, 54f), 90f, 3.8f);

            // ── 2 large Mountain rocks near foothills ────────────────
            HighlandsPrefabHelper.PlaceRock(transform, "Mountain",
                new Vector3(180f, 2f, 160f), 25f, 4.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Mountain",
                new Vector3(-170f, 2f, -170f), 200f, 3.5f);

            // ── 18 standard boulders scattered across the valley ─────
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 1",
                new Vector3(40f, 2f, 30f), 0f, 2.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 3",
                new Vector3(-50f, 2f, 60f), 45f, 2.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 5",
                new Vector3(70f, 2f, -40f), 90f, 1.8f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 2",
                new Vector3(-80f, 2f, -50f), 135f, 2.2f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 4",
                new Vector3(20f, 2f, -90f), 180f, 2.8f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 1",
                new Vector3(-30f, 2f, 110f), 225f, 1.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 3",
                new Vector3(100f, 2f, 50f), 270f, 3.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 5",
                new Vector3(-110f, 2f, -20f), 315f, 2.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 2",
                new Vector3(60f, 2f, 100f), 60f, 2.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 4",
                new Vector3(-70f, 2f, -100f), 120f, 1.8f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 1",
                new Vector3(10f, 2f, -60f), 150f, 2.3f);
            HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 3",
                new Vector3(-140f, 2f, 30f), 30f, 2.0f);

            // IceRock variants near the mountains (higher x/z values)
            HighlandsPrefabHelper.PlaceRock(transform, "IceRock_01",
                new Vector3(160f, 2f, 140f), 75f, 2.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "IceRock_02",
                new Vector3(-155f, 2f, 150f), 190f, 2.8f);
            HighlandsPrefabHelper.PlaceRock(transform, "IceRock_03",
                new Vector3(170f, 2f, -150f), 240f, 2.2f);
            HighlandsPrefabHelper.PlaceRock(transform, "IceRock_01",
                new Vector3(-160f, 2f, -145f), 310f, 2.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "IceRock_02",
                new Vector3(145f, 2f, 170f), 100f, 3.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "IceRock_03",
                new Vector3(-175f, 2f, -160f), 50f, 1.8f);

            // ── 28 tiny rocks scattered everywhere for detail ────────
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 1",
                new Vector3(15f, 2f, 20f), 10f, 1.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 2",
                new Vector3(-25f, 2f, 45f), 80f, 1.2f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 3",
                new Vector3(55f, 2f, -30f), 160f, 1.8f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 4",
                new Vector3(-65f, 2f, -70f), 220f, 1.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 5",
                new Vector3(85f, 2f, 45f), 300f, 1.6f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 1",
                new Vector3(-40f, 2f, 90f), 50f, 2.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 2",
                new Vector3(30f, 2f, -110f), 130f, 1.3f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 3",
                new Vector3(-95f, 2f, 15f), 190f, 1.7f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 4",
                new Vector3(110f, 2f, -70f), 260f, 1.1f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 5",
                new Vector3(-120f, 2f, -55f), 340f, 1.9f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 1",
                new Vector3(50f, 2f, 75f), 25f, 1.4f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 2",
                new Vector3(-15f, 2f, -35f), 95f, 1.6f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 3",
                new Vector3(130f, 2f, 20f), 175f, 1.2f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 4",
                new Vector3(-85f, 2f, 120f), 245f, 2.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 5",
                new Vector3(25f, 2f, 140f), 15f, 1.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 1",
                new Vector3(-145f, 2f, -85f), 115f, 1.8f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 2",
                new Vector3(95f, 2f, -140f), 285f, 1.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 3",
                new Vector3(-55f, 2f, 135f), 355f, 1.3f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 4",
                new Vector3(140f, 2f, 110f), 70f, 1.7f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 5",
                new Vector3(-100f, 2f, 70f), 200f, 1.1f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 1",
                new Vector3(5f, 2f, -150f), 140f, 1.9f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 2",
                new Vector3(-35f, 2f, -120f), 40f, 1.4f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 3",
                new Vector3(75f, 2f, 130f), 230f, 1.6f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 4",
                new Vector3(-150f, 2f, -30f), 310f, 1.2f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 5",
                new Vector3(45f, 2f, -55f), 165f, 2.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 1",
                new Vector3(-75f, 2f, 40f), 280f, 1.5f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 2",
                new Vector3(160f, 2f, -10f), 55f, 1.0f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 3",
                new Vector3(-10f, 2f, -80f), 125f, 1.8f);

            // ── OUTER RING rock field (new expansion zone) ───────────
            // why: fills the outer steppe (200-320) so the expanded arena has cover
            string[] cliffs = { "Rock Cliff 1", "Rock Cliff 2", "Rock Cliff 3",
                                "Rock Cliff 4", "Rock Cliff 5" };
            string[] stdRocks = { "Standard Rock 1", "Standard Rock 2", "Standard Rock 3",
                                  "Standard Rock 4", "Standard Rock 5" };
            string[] iceRocks = { "IceRock_01", "IceRock_02", "IceRock_03" };
            string[] tinyRocks = { "Tiny Rock 1", "Tiny Rock 2", "Tiny Rock 3",
                                   "Tiny Rock 4", "Tiny Rock 5" };

            // 8 outer cliff clusters
            for (int c = 0; c < 8; c++)
            {
                float t = (c / 8f) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                float r = Random.Range(210f, 290f);
                Vector3 anchor = new Vector3(Mathf.Cos(t) * r, 2f, Mathf.Sin(t) * r);
                int n = Random.Range(2, 4);
                for (int i = 0; i < n; i++)
                {
                    float ox = Random.Range(-10f, 10f);
                    float oz = Random.Range(-10f, 10f);
                    HighlandsPrefabHelper.PlaceRock(transform,
                        cliffs[Random.Range(0, cliffs.Length)],
                        new Vector3(anchor.x + ox, 2f, anchor.z + oz),
                        Random.Range(0f, 360f), Random.Range(2.4f, 3.8f));
                }
            }

            // 18 outer standard boulders scattered
            for (int i = 0; i < 18; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(200f, 320f);
                HighlandsPrefabHelper.PlaceRock(transform,
                    stdRocks[Random.Range(0, stdRocks.Length)],
                    new Vector3(Mathf.Cos(t) * r, 2f, Mathf.Sin(t) * r),
                    Random.Range(0f, 360f), Random.Range(1.8f, 3.0f));
            }

            // 10 outer ice rocks near mountain wall
            for (int i = 0; i < 10; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(270f, 320f);
                HighlandsPrefabHelper.PlaceRock(transform,
                    iceRocks[Random.Range(0, iceRocks.Length)],
                    new Vector3(Mathf.Cos(t) * r, 2f, Mathf.Sin(t) * r),
                    Random.Range(0f, 360f), Random.Range(2.0f, 3.2f));
            }

            // 30 outer tiny rocks for detail
            for (int i = 0; i < 30; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(190f, 320f);
                HighlandsPrefabHelper.PlaceRock(transform,
                    tinyRocks[Random.Range(0, tinyRocks.Length)],
                    new Vector3(Mathf.Cos(t) * r + Random.Range(-6f, 6f), 2f,
                                Mathf.Sin(t) * r + Random.Range(-6f, 6f)),
                    Random.Range(0f, 360f), Random.Range(1.0f, 2.0f));
            }
        }
    }
}
