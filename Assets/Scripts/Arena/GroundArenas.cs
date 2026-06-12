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
            // why: 1125x1125 (1.5x of 750) gives even more playable area; offset -562.5 keeps origin-centred
            var terrain = TerrainFactory.Create(transform,
                new Vector3(-562.5f, 0f, -562.5f), new Vector3(1125f, 60f, 1125f),
                769, "DesertTerrain");

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
            // why: spawn ring scaled 1.5x to ~375 to match expanded arena; mesa tops kept but on outer ring
            AddSpawnPoints(
                new Vector3(-345f, 12f, 330f),
                new Vector3(-225f, 12f, 360f),
                new Vector3( 345f, 10f,-300f),
                new Vector3( 240f, 10f,-345f),
                new Vector3( -75f,  8f,-375f),
                new Vector3(  45f,  8f,-360f),
                new Vector3(-390f,  3f,   0f),
                new Vector3( 390f,  3f,   0f)
            );
            AddInvisibleWalls(562f, 50f);

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
            // why: fog thinned so the mesas + canyon rim read across the vast flats
            // (the desert should feel open, not boxed in by haze); a real sky added.
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.86f, 0.78f, 0.62f); // warm dust haze
            RenderSettings.fogDensity = 0.0016f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.78f, 0.70f, 0.55f);
            RenderSettings.sun = dsl;
            BuildSky();   // procedural sun-bleached desert sky (drives skybox ambient + sun disc)

            VFXManager.DustStorm(Vector3.zero, 8f);
            VFXManager.SandSwirls(new Vector3(0, 1, 0), 5f);
            VFXManager.HeatDistortion(new Vector3(0, 2, 0), 4f);
            VFXManager.DustMotes(new Vector3(0, 3, 0), 6f);
            VFXManager.SandSwirls(new Vector3(-270f, 1f, 180f), 4f);
            VFXManager.SandSwirls(new Vector3( 300f, 1f,-210f), 4f);

            // ── Dynamic life + AI navigation ───────────────────────
            // Tumbleweeds rolling on the wind across the open flats
            TumbleweedField.Create(transform, Vector3.zero, 14, 280f,
                new Vector3(1f, 0f, 0.35f), 1.5f);
            // Vultures wheeling over the arena (reused flock, dark plumage)
            Color vulture = new Color(0.16f, 0.14f, 0.12f);
            SeabirdFlock.Create(transform, new Vector3(0f, 0f, 0f), 5, 240f, vulture);
            SeabirdFlock.Create(transform, new Vector3(-200f, 0f, 150f), 3, 130f, vulture);
            RegisterAINavZones();   // steer bots around the mesas + lethal oasis
        }

        // =================================================================
        // Atmosphere / AI nav helpers
        // =================================================================

        /// <summary>Procedural sun-bleached desert sky: pale hot blue with a sandy
        /// horizon and dusty haze; drives skybox-based ambient + the sun disc.</summary>
        private void BuildSky()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return;
            var sky = new Material(shader);
            sky.SetColor("_SkyTint",     new Color(0.62f, 0.66f, 0.78f)); // pale hot-sky blue
            sky.SetColor("_GroundColor", new Color(0.78f, 0.66f, 0.46f)); // sandy horizon
            sky.SetFloat("_AtmosphereThickness", 1.3f);                   // dusty haze
            sky.SetFloat("_Exposure",  1.25f);                            // bright desert glare
            sky.SetFloat("_SunSize",   0.05f);
            sky.SetFloat("_SunSizeConvergence", 4f);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>Register the impassable mesas and the lethal oasis pool as AI
        /// navigation-avoid zones (the pure-data AI HazardZone — steer only, no damage)
        /// so bots route around the cliffs instead of ramming them and don't drive into
        /// the oasis. The 18-unit obstacle ray is far too short for 90-120u mesas.</summary>
        private void RegisterAINavZones()
        {
            RegisterNavBox(new Vector3(-281f, 0f,  315f), 120f); // NW mesa
            RegisterNavBox(new Vector3( 315f, 0f, -225f),  98f); // SE mesa
            RegisterNavBox(new Vector3( -56f, 0f, -315f),  74f); // S-center mesa
            RegisterNavBox(new Vector3(   0f, 0f,  112f),  34f); // lethal oasis pool
        }

        private static void RegisterNavBox(Vector3 center, float radius)
        {
            CloseEncounters.AI.AIController.RegisterHazardZone(new CloseEncounters.AI.HazardZone
            {
                center = new Vector3(center.x, 0f, center.z),
                halfExtents = new Vector3(radius, 40f, radius)
            });
        }

        // ── CANYON RIM: mountain/canyon prefabs around arena edges ──
        private void BuildCanyonRim()
        {
            // why: rim scaled 1.5x to ~500 to hug the new 562 half-extent wall; extra peaks added to seal the longer perimeter
            // ── North wall (z ~480..517) ── 8 canyon mountains (was 5) ──
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-360f, 0f, 495f), 0f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(-165f, 0f, 510f), 45f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(  45f, 0f, 480f), 90f, 13f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3( 255f, 0f, 502f), 135f, 10f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3( 435f, 0f, 487f), 200f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-470f, 0f, 470f), 20f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3( -60f, 0f, 505f), 70f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3( 150f, 0f, 495f), 110f, 12f);

            // ── South wall ── 8 canyon mountains (was 5) ──
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-330f, 0f, -495f), 180f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-105f, 0f, -517f), 135f, 13f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3( 135f, 0f, -487f), 90f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3( 345f, 0f, -510f), 45f, 10f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-450f, 0f, -480f), 225f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3( 470f, 0f, -485f), 200f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(  15f, 0f, -500f), 100f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(-225f, 0f, -505f), 160f, 12f);

            // ── East wall ── 7 canyon mountains (was 4) ──
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(510f, 0f, -240f), 90f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(487f, 0f,  -45f), 135f, 13f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(517f, 0f,  165f), 45f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(495f, 0f,  345f), 70f, 10f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(500f, 0f, -420f), 110f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(505f, 0f,   60f), 130f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(490f, 0f,  450f), 150f, 11f);

            // ── West wall ── 7 canyon mountains (was 4) ──
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(-502f, 0f, -240f), 0f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(-517f, 0f,  -45f), 90f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-487f, 0f,  165f), 180f, 13f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-510f, 0f,  360f), 225f, 10f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-500f, 0f, -420f), 60f, 11f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-505f, 0f,   60f), 200f, 12f);
            DesertPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(-490f, 0f,  455f), 240f, 11f);

            // ── Corner cliff details for continuity ──
            DesertPrefabHelper.PlaceLowPoly(transform, "CliffCorner_01",
                new Vector3( 480f, 0f,  465f), 45f, 16f);
            DesertPrefabHelper.PlaceLowPoly(transform, "CliffCorner_02",
                new Vector3(-480f, 0f,  465f), 135f, 15f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Cliff_01",
                new Vector3( 480f, 0f, -472f), 0f, 14f);
            DesertPrefabHelper.PlaceLowPoly(transform, "CliffCorner_01",
                new Vector3(-480f, 0f, -472f), 90f, 16f);
        }

        // ── OASIS: water feature + palm trees + rocks ──────────────
        private void BuildOasis()
        {
            Vector3 oasisCenter = new Vector3(0f, 0f, 112.5f);

            // ── Deep water pool (Fentchester canal style) ──────────
            // why: oasis is the off-centre destination feature — position moved 1.5x (z 75->112.5), pool size kept as centrepiece
            {
                var waterGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                waterGO.name = "OasisWater";
                waterGO.transform.SetParent(transform, false);
                waterGO.transform.position = new Vector3(0f, -0.5f, 112.5f);
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
            AddWaterHazard(new Vector3(0f, -0.3f, 112.5f), new Vector3(48f, 4f, 48f), "OasisHazard");

            // ── Palm trees spread wide (positions 1.5x) ────────────
            AddTree(new Vector3(-52f, 1f, 165f),  9f, 3.5f, "Palm_1");
            AddTree(new Vector3(57f,  1f, 157f),  8f, 3.0f, "Palm_2");
            AddTree(new Vector3(-60f, 1f, 105f),  7f, 2.5f, "Palm_3");
            AddTree(new Vector3(52f,  1f, 67f),  10f, 4.0f, "Palm_4");
            AddTree(new Vector3(-22f, 1f, 60f),   8f, 3.5f, "Palm_5");
            AddTree(new Vector3(63f,  1f, 120f),  9f, 3.0f, "Palm_6");
            AddTree(new Vector3(-12f, 1f, 172f),  7f, 3.0f, "Palm_7");
            AddTree(new Vector3(15f,  1f, 52f),   8f, 3.0f, "Palm_8");
            AddTree(new Vector3(-70f, 1f, 135f),  9f, 3.2f, "Palm_9");
            AddTree(new Vector3(70f,  1f, 90f),   8f, 3.4f, "Palm_10");

            // ── Rocks spread wider (positions 1.5x) ────────────────
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_01", new Vector3(-45f, 1f, 162f), 0f,   1.5f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_02", new Vector3(48f,  1f, 150f), 45f,  1.2f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_03", new Vector3(-57f, 1f, 127f), 120f, 1.4f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_04", new Vector3(54f,  1f, 82f),  200f, 1.8f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_05", new Vector3(-18f, 1f, 57f),  90f,  1.0f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Rock_01", new Vector3(27f,  1f, 63f),  270f, 1.6f);

            // ── Vegetation spread wider (positions 1.5x) ───────────
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertGrass_01", new Vector3(-42f, 1f, 157f), 0f,  1.5f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertGrass_02", new Vector3(37f,  1f, 147f), 30f, 1.5f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertGrass_01", new Vector3(-48f, 1f, 87f),  150f,1.2f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertGrass_02", new Vector3(45f,  1f, 75f),  220f,1.3f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertTree", new Vector3(-67f, 1f, 135f), 45f,  1.2f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertTree", new Vector3(60f,  1f, 82f), 180f, 1.1f);

            // ── Cacti ring (positions 1.5x) ────────────────────────
            DesertPrefabHelper.PlaceLowPoly(transform, "Cactus_01", new Vector3(-82f, 1f, 180f), 0f,   2.5f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Cactus_02", new Vector3(82f,  1f, 165f), 90f,  2.5f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Cactus_03", new Vector3(-75f, 1f, 60f),  180f, 2.5f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Cactus_01", new Vector3(75f,  1f, 52f),  270f, 2.5f);
            DesertPrefabHelper.PlaceLowPoly(transform, "Cactus_02", new Vector3(-22f, 1f, 37f),  45f,  2.5f);
        }

        // ── SETTLEMENT: desert buildings cluster ────────────────────
        private void BuildSettlement()
        {
            // ── Settlement buildings (x=30..130, z=0..75) ──────────
            // Place all 5 Desert Building variants around a central plaza (~x=80, z=40)
            // Place all 5 building variants -- scale 2x for visibility, y=2 above terrain
            // why: settlement cluster positions scaled 1.5x so it stays proportionally placed in the bigger map
            var b1 = DesertPrefabHelper.PlaceBuilding(transform, "Desert_Building_V1",
                new Vector3(90f, 2f, 82f), 15f, 2.0f);
            if (b1 == null) Debug.LogWarning("[GroundDesert] Desert_Building_V1 failed to load!");
            var b2 = DesertPrefabHelper.PlaceBuilding(transform, "Desert_Building_V2",
                new Vector3(142f, 2f, 90f), 210f, 2.0f);
            if (b2 == null) Debug.LogWarning("[GroundDesert] Desert_Building_V2 failed to load!");
            var b3 = DesertPrefabHelper.PlaceBuilding(transform, "Desert_Building_V3",
                new Vector3(172f, 2f, 52f), 120f, 2.0f);
            if (b3 == null) Debug.LogWarning("[GroundDesert] Desert_Building_V3 failed to load!");
            var b4 = DesertPrefabHelper.PlaceBuilding(transform, "Desert_Building_V4",
                new Vector3(127f, 2f, 22f), 275f, 2.0f);
            if (b4 == null) Debug.LogWarning("[GroundDesert] Desert_Building_V4 failed to load!");
            var b5 = DesertPrefabHelper.PlaceBuilding(transform, "Desert_Building_V5",
                new Vector3(67f, 2f, 37f), 340f, 2.0f);
            if (b5 == null) Debug.LogWarning("[GroundDesert] Desert_Building_V5 failed to load!");

            // ── Lookout platforms near settlement (positions 1.5x) ───
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertPlatform_01",
                new Vector3(195f, 0f, 15f), 45f, 1.5f);
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertPlatform_01",
                new Vector3(52f, 0f, 105f), 200f, 1.5f);

            // ── Ancient adobe ruins SW of settlement (positions 1.5x) ─
            Color ruin = new Color(0.65f, 0.55f, 0.40f);
            AddBlock(new Vector3(-120f, 3f, -90f), new Vector3(14f, 6f, 14f), ruin, "Ruin_Base");
            AddBlock(new Vector3(-120f, 8f, -90f), new Vector3(10f, 4f, 10f), ruin, "Ruin_Upper");
            AddBlock(new Vector3(-135f, 2f, -67f), new Vector3(5f, 4f, 8f), ruin, "Ruin_Wall_1");
            AddBlock(new Vector3(-99f, 1.5f, -102f), new Vector3(6f, 3f, 5f), ruin, "Ruin_Wall_2");
            AddBlock(new Vector3(-108f, 1f, -112f), new Vector3(8f, 2f, 4f), ruin, "Ruin_Wall_3");

            // ── Desert cave south of ruins (position 1.5x) ───────────
            DesertPrefabHelper.PlaceDesertProp(transform, "DesertCave",
                new Vector3(-180f, 0f, -150f), 30f, 2f);
        }

        // ── DESERT FLOOR: scattered rocks, cacti, vegetation ────────
        private void BuildDesertFloor()
        {
            // Exclusion zones (scaled 1.5x):
            //   Oasis:     roughly x=-45..45, z=82..142
            //   Settlement: x=37..202, z=-7..120
            //   Spawn ring: radius 300 from origin (keep 8+ units away)

            string[] envRocks  = { "DesertRock_01", "DesertRock_02", "DesertRock_03" };
            string[] lpRocks   = { "Rock_01", "Rock_02", "Rock_03", "Rock_04", "Rock_05" };
            string[] envCacti  = { "Cactus_01", "Cactus_02", "Cactus_03", "Cactus_04" };
            string[] lpCacti   = { "Cactus_01", "Cactus_02", "Cactus_03" };
            string[] mountains = { "DesertMountain_01", "DesertMountain_02", "DesertMountain_03" };
            string[] grasses   = { "DesertGrass_01", "DesertGrass_02" };

            // why: 21 clusters (was 14) with centres scaled 1.5x to fill the bigger map (count ~1.5x for density)
            Vector2[] clusterCenters =
            {
                new Vector2(-180f, -120f),
                new Vector2(-240f,   60f),
                new Vector2(-105f, -210f),
                new Vector2(  90f, -180f),
                new Vector2( 225f,  -90f),
                new Vector2(-150f,  195f),
                new Vector2( 210f,  165f),
                new Vector2( -30f,  -75f),
                new Vector2(-390f,  270f),
                new Vector2( 405f,   90f),
                new Vector2( 330f, -330f),
                new Vector2(-420f, -270f),
                new Vector2(  90f,  375f),
                new Vector2(-285f, -375f),
                new Vector2( 360f,  300f),
                new Vector2(-360f,  -60f),
                new Vector2( 150f, -390f),
                new Vector2(-120f,  390f),
                new Vector2( 270f,  360f),
                new Vector2(-450f,  120f),
                new Vector2( 420f, -180f),
            };

            foreach (var center in clusterCenters)
            {
                int count = Random.Range(6, 10); // 6-9 rocks per cluster (~2.25x density)
                for (int i = 0; i < count; i++)
                {
                    float ox = center.x + Random.Range(-12f, 12f);
                    float oz = center.y + Random.Range(-12f, 12f);
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

            // why: 81 cacti (was 36, ~2.25x) over the 1.5x range to keep density up in the bigger arena
            int cactiPlaced = 0;
            int cactiTarget = 81;
            int cactiAttempts = 0;
            while (cactiPlaced < cactiTarget && cactiAttempts < 900)
            {
                cactiAttempts++;
                float cx = Random.Range(-495f, 495f);
                float cz = Random.Range(-495f, 495f);

                // Skip oasis zone (1.5x)
                if (cx > -45f && cx < 45f && cz > 82f && cz < 142f) continue;
                // Skip settlement zone (1.5x)
                if (cx > 37f && cx < 202f && cz > -7f && cz < 120f) continue;
                // Skip spawn ring proximity (1.5x)
                float dist = Mathf.Sqrt(cx * cx + cz * cz);
                if (dist > 288f && dist < 312f) continue;

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

            // ── Desert mountains (18 peaks for medium cover — positions 1.5x, count ~2.25x) ────
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[0],
                new Vector3(-270f, 1f, -30f), Random.Range(0f, 360f), 2.5f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[1],
                new Vector3(255f, 1f, 210f), Random.Range(0f, 360f), 2.0f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[2],
                new Vector3(-210f, 1f, -240f), Random.Range(0f, 360f), 1.8f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[0],
                new Vector3(150f, 1f, -225f), Random.Range(0f, 360f), 2.2f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[1],
                new Vector3(-420f, 1f,  330f), Random.Range(0f, 360f), 2.8f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[2],
                new Vector3( 435f, 1f,  345f), Random.Range(0f, 360f), 2.4f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[0],
                new Vector3(-390f, 1f, -420f), Random.Range(0f, 360f), 2.6f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[1],
                new Vector3( 375f, 1f, -450f), Random.Range(0f, 360f), 2.3f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[2],
                new Vector3(-90f, 1f,  300f), Random.Range(0f, 360f), 2.1f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[0],
                new Vector3( 120f, 1f,  330f), Random.Range(0f, 360f), 2.5f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[1],
                new Vector3(-330f, 1f,  150f), Random.Range(0f, 360f), 2.2f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[2],
                new Vector3( 345f, 1f,  -90f), Random.Range(0f, 360f), 1.9f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[0],
                new Vector3(-150f, 1f, -360f), Random.Range(0f, 360f), 2.4f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[1],
                new Vector3( 240f, 1f, -360f), Random.Range(0f, 360f), 2.3f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[2],
                new Vector3(-450f, 1f,  -60f), Random.Range(0f, 360f), 2.6f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[0],
                new Vector3( 450f, 1f,  120f), Random.Range(0f, 360f), 2.5f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[1],
                new Vector3(-240f, 1f,  420f), Random.Range(0f, 360f), 2.2f);
            DesertPrefabHelper.PlaceDesertProp(transform, mountains[2],
                new Vector3( 300f, 1f,  -270f), Random.Range(0f, 360f), 2.0f);

            // ── Grass tufts (63 total, ~2.25x, near rock clusters and cacti) ───────
            for (int i = 0; i < 63; i++)
            {
                // Place grass near a random cluster center with offset
                var nearCluster = clusterCenters[Random.Range(0, clusterCenters.Length)];
                float gx = nearCluster.x + Random.Range(-30f, 30f);
                float gz = nearCluster.y + Random.Range(-30f, 30f);
                float scl = Random.Range(1.0f, 2.0f);
                string grass = grasses[Random.Range(0, grasses.Length)];

                DesertPrefabHelper.PlaceDesertProp(transform, grass,
                    new Vector3(gx, 1f, gz), Random.Range(0f, 360f), scl);
            }

            // ── Trees (20 total, ~2.25x, positions 1.5x, not near oasis) ──
            Vector3[] treePositions =
            {
                new Vector3(-150f, 1f,  -135f),
                new Vector3( 240f, 1f,  -45f),
                new Vector3(-255f, 1f,  150f),
                new Vector3( 120f, 1f, -255f),
                new Vector3( -75f, 1f, -240f),
                new Vector3( 360f, 1f,  270f),
                new Vector3(-360f, 1f, -165f),
                new Vector3( 285f, 1f, -390f),
                new Vector3(-390f, 1f,  390f),
                new Vector3( 180f, 1f,  240f),
                new Vector3(-210f, 1f,  -45f),
                new Vector3(  60f, 1f, -330f),
                new Vector3(-300f, 1f,  300f),
                new Vector3( 420f, 1f,  -60f),
                new Vector3(-435f, 1f,   45f),
                new Vector3( 150f, 1f,  420f),
                new Vector3(-120f, 1f, -420f),
                new Vector3( 330f, 1f,  150f),
                new Vector3(-180f, 1f,  255f),
                new Vector3( 270f, 1f, -150f),
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

            // ── DENSITY PASS 1: breakable grass/shrub carpet across the open desert ──
            // why: fills the enlarged 1.5x floor with low breakable cover (DesertGrass is breakable PlaceDesertProp)
            {
                string[] shrubs = { "DesertGrass_01", "DesertGrass_02", "DesertTree" };
                int shrubPlaced = 0, shrubAttempts = 0;
                while (shrubPlaced < 70 && shrubAttempts < 700)
                {
                    shrubAttempts++;
                    float sx = Random.Range(-510f, 510f);
                    float sz = Random.Range(-510f, 510f);
                    if (sx > -45f && sx < 45f && sz > 82f && sz < 142f) continue;  // oasis
                    if (sx > 37f && sx < 202f && sz > -7f && sz < 120f) continue;  // settlement
                    float d = Mathf.Sqrt(sx * sx + sz * sz);
                    if (d > 288f && d < 312f) continue;                            // spawn ring
                    string shrub = shrubs[Random.Range(0, shrubs.Length)];
                    DesertPrefabHelper.PlaceDesertProp(transform, shrub,
                        new Vector3(sx, 1f, sz), Random.Range(0f, 360f), Random.Range(1.0f, 1.8f));
                    shrubPlaced++;
                }
            }

            // ── DENSITY PASS 2: static boulder/rock landmarks for cover in the new outer ring ──
            // why: rock-named props stay static (landmarks) — used as hard cover lining the wider arena
            {
                string[] bigRocks = { "DesertRock_01", "DesertRock_02", "DesertRock_03" };
                string[] lpBigRocks = { "Rock_01", "Rock_03", "Rock_05" };
                int rockPlaced = 0, rockAttempts = 0;
                while (rockPlaced < 40 && rockAttempts < 400)
                {
                    rockAttempts++;
                    float rx = Random.Range(-500f, 500f);
                    float rz = Random.Range(-500f, 500f);
                    float d = Mathf.Sqrt(rx * rx + rz * rz);
                    if (d < 230f) continue;                                        // keep to outer ring only
                    if (d > 288f && d < 312f) continue;                            // spawn ring
                    float rot = Random.Range(0f, 360f);
                    float scl = Random.Range(2.0f, 4.0f);
                    if (Random.value > 0.5f)
                        DesertPrefabHelper.PlaceDesertProp(transform, bigRocks[Random.Range(0, bigRocks.Length)],
                            new Vector3(rx, 1f, rz), rot, scl);
                    else
                        DesertPrefabHelper.PlaceLowPoly(transform, lpBigRocks[Random.Range(0, lpBigRocks.Length)],
                            new Vector3(rx, 1f, rz), rot, scl);
                    rockPlaced++;
                }
            }

            // ── DENSITY PASS 3: extra cactus garden mid-field for variety ──
            // why: Cactus-named props stay static landmarks; adds vertical interest between clusters
            {
                string[] gardenCacti = { "Cactus_01", "Cactus_02", "Cactus_03", "Cactus_04" };
                int cgPlaced = 0, cgAttempts = 0;
                while (cgPlaced < 45 && cgAttempts < 450)
                {
                    cgAttempts++;
                    float gx = Random.Range(-460f, 460f);
                    float gz = Random.Range(-460f, 460f);
                    if (gx > -45f && gx < 45f && gz > 82f && gz < 142f) continue;  // oasis
                    if (gx > 37f && gx < 202f && gz > -7f && gz < 120f) continue;  // settlement
                    float d = Mathf.Sqrt(gx * gx + gz * gz);
                    if (d > 288f && d < 312f) continue;                            // spawn ring
                    DesertPrefabHelper.PlaceDesertProp(transform, gardenCacti[Random.Range(0, gardenCacti.Length)],
                        new Vector3(gx, 1f, gz), Random.Range(0f, 360f), Random.Range(2.0f, 3.5f));
                    cgPlaced++;
                }
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
            ts.spawnRadius = 270f;
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
            // why: 1125x1125 (1.5x of 750) for ~5.6x playable area; canal stays centred on z=0 (nz=0.5) automatically.
            // res bumped 513->769 so detail-per-metre holds at the larger size.
            // why: the terrain is lowered to y-4 so the canal can be a real ~5-deep
            // trench, while the city surface stays at world y1 (cityH 0.10 →
            // -4 + 0.10*50 = 1). The canal floor (h=0) lands at y-4. Everything placed
            // on the city surface at y1 is unaffected — only the trench changes.
            const float cityH = 0.10f;
            var terrain = TerrainFactory.Create(transform,
                new Vector3(-562.5f, -4f, -562.5f), new Vector3(1125f, 50f, 1125f),
                769, "CityTerrain");

            // Canal geometry (world z, terrain is 1125 wide so nz-dist = |z|/1125):
            //   floor   |z| < 18  (dist 0.016)   → trench bottom, y-4
            //   bank     18..23   (dist..0.0204) → wall sloping up to the street
            // Wider water + a deep trench vs the old shallow 1-deep, ±12 cut.
            const float canalFloorNz = 0.016f;   // |z| = 18
            const float canalBankNz  = 0.0204f;  // |z| = 23

            TerrainFactory.SetHeights(terrain, (nx, nz) =>
            {
                float h = cityH; // flat city base → world y1
                h += 0.003f * Mathf.PerlinNoise(nx * 8f, nz * 8f); // subtle variation

                // Canal trench cut at nz ~0.5 (world z=0)
                float canalDist = Mathf.Abs(nz - 0.5f);
                if (canalDist < canalFloorNz)
                    h = 0.0f; // canal floor (world y-4)
                else if (canalDist < canalBankNz)
                {
                    float t = (canalDist - canalFloorNz) / (canalBankNz - canalFloorNz);
                    h = Mathf.Lerp(0f, cityH, t * t); // bank wall up to street level
                }

                return Mathf.Max(0f, h);
            });

            // Flatten all city blocks where buildings will sit
            // North blocks: z=55..115 (nz 0.592..0.692)
            TerrainFactory.Flatten(terrain, 0.267f, 0.592f, 0.400f, 0.692f, cityH); // block x:-140..-60
            TerrainFactory.Flatten(terrain, 0.400f, 0.592f, 0.500f, 0.692f, cityH); // block x:-60..0
            TerrainFactory.Flatten(terrain, 0.500f, 0.592f, 0.600f, 0.692f, cityH); // block x:0..60
            TerrainFactory.Flatten(terrain, 0.600f, 0.592f, 0.733f, 0.692f, cityH); // block x:60..140
            // Far north: z=115..180 (nz 0.692..0.800)
            TerrainFactory.Flatten(terrain, 0.267f, 0.692f, 0.733f, 0.800f, cityH);
            // South blocks: z=-115..-55 (nz 0.308..0.408)
            TerrainFactory.Flatten(terrain, 0.267f, 0.308f, 0.400f, 0.408f, cityH);
            TerrainFactory.Flatten(terrain, 0.400f, 0.308f, 0.500f, 0.408f, cityH);
            TerrainFactory.Flatten(terrain, 0.500f, 0.308f, 0.600f, 0.408f, cityH);
            TerrainFactory.Flatten(terrain, 0.600f, 0.308f, 0.733f, 0.408f, cityH);
            // Far south: z=-180..-115 (nz 0.200..0.308)
            TerrainFactory.Flatten(terrain, 0.267f, 0.200f, 0.733f, 0.308f, cityH);

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

                // Canal zone (matches the widened trench)
                bool inCanal = Mathf.Abs(nz - 0.5f) < 0.0204f;

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
                // Surface at y-0.8 (well below the deck), filling the wider trench.
                waterGO.transform.position = new Vector3(0f, -2.4f, 0f);
                // Span the FULL carved canal length (terrain ~1125 wide, walls at +/-562) so the
                // water reaches both ends instead of stopping at +/-280 and leaving dry trench.
                waterGO.transform.localScale = new Vector3(1120f, 3.2f, 36f);
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

                // Make it read as moving water rather than a static slab — scrolling-UV
                // animator (same component the open-water arenas use).
                var canalAnim = waterGO.AddComponent<CloseEncounters.VehiclePhysics.WaterBasicAnimator>();
                canalAnim.waterColor   = new Color(0.10f, 0.32f, 0.40f, 0.55f);
                canalAnim.horizonColor = new Color(0.16f, 0.42f, 0.50f, 0.55f);
            }
            // ── Canal kill-water ───────────────────────────────────
            // One continuous hazard filling the trench BELOW deck level (top y0.3;
            // the deck bottom is y0.4). Anything that falls into the canal dies, while
            // vehicles crossing on a bridge sit at y1, safely above it — so no need to
            // carve gaps around the bridges anymore.
            float[] bridgeX = { -140f, -60f, 0f, 60f, 140f };
            AddWaterHazard(new Vector3(0f, -1.85f, 0f),
                new Vector3(1120f, 4.3f, 36f), "CanalHazard");

            // ── Bridges (5 crossings) ──────────────────────────────
            // Each deck is a flat slab whose TOP sits exactly at street level (y1) and
            // spans the full trench onto solid ground at both ends (z=±28, past the
            // bank lip at z=23), so cars roll on/off flush with zero step and no steep
            // ramp. Deck bottom (y0.4) clears the water surface (y-0.8) — no clipping.
            Color bridgeStone = new Color(0.50f, 0.48f, 0.45f);
            const float deckHalfZ = 28f;   // reaches solid ground beyond the z=23 lip
            const float deckWidth = 16f;
            for (int b = 0; b < bridgeX.Length; b++)
            {
                float x = bridgeX[b];
                // Deck: 16 wide, 0.6 thick, centre y0.7 → top y1.0 (street level).
                AddBridge(new Vector3(x, 0.7f, -deckHalfZ), new Vector3(x, 0.7f, deckHalfZ),
                    deckWidth, 0.6f, bridgeStone, b == 2 ? "Bridge_Center" : $"Bridge_{b}");
                // Side rails so vehicles don't slip off into the trench while crossing.
                AddWall(new Vector3(x - 8.2f, 1f, -deckHalfZ), new Vector3(x - 8.2f, 1f, deckHalfZ), 1f, 0.4f, bridgeStone, $"BridgeRailL_{b}");
                AddWall(new Vector3(x + 8.2f, 1f, -deckHalfZ), new Vector3(x + 8.2f, 1f, deckHalfZ), 1f, 0.4f, bridgeStone, $"BridgeRailR_{b}");
            }
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

            // Remove anything that spawned inside a bridge crossing corridor so every
            // bridge entrance stays drivable (waterfront blockers, etc.).
            ClearBridgeCorridors();

            // ── Spawn points — pushed outward into new outskirts (avoids canal z=-15..15) ──
            AddSpawnPoints(
                new Vector3(-375f, 1f,  180f),
                new Vector3( 375f, 1f,  180f),
                new Vector3(-375f, 1f, -180f),
                new Vector3( 375f, 1f, -180f),
                new Vector3(   0f, 1f,  390f),
                new Vector3(   0f, 1f, -390f),
                new Vector3(-300f, 1f,  360f),
                new Vector3( 300f, 1f, -360f),
                new Vector3(-450f, 1f,    90f),
                new Vector3( 450f, 1f,   -90f)
            );
            AddInvisibleWalls(562f, 45f);

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
            CityPrefabHelper.PlaceProp(transform, "Bus stop prefab",
                new Vector3(-155f, 1f, 60f), 0f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "Bus stop prefab",
                new Vector3(-95f, 1f, 120f), 180f, 1.0f);

            // Phone booths near hospital and shops
            CityPrefabHelper.PlaceProp(transform, "phone booth prefab",
                new Vector3(-170f, 1f, 88f), 90f, 1.0f);
            CityPrefabHelper.PlaceProp(transform, "phone booth prefab",
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
            // Moved to the north canal bank (z=50). The old z=0 sat the towers IN the canal,
            // and their wide footprints ran straight into the centre + middle bridges. Bridges
            // only exist within |z|<28, so z=50 keeps this a central riverside landmark while
            // leaving every crossing clear.
            CityPrefabHelper.PlaceBuilding(transform, "Building_I_1_prefab",
                new Vector3(-30f, 1f, 50f), 0f, 6.5f);
            CityPrefabHelper.PlaceBuilding(transform, "Building_I_2_Prefab",
                new Vector3( 30f, 1f, 50f), 18f, 6.5f);

            // ── Fighters clearly protruding from the towers (z follows the towers) ──
            SpawnCrashedFighter(new Vector3(-17f, 55f, 53f), new Vector3(12f, 90f, -8f), 5f);
            SpawnCrashedFighter(new Vector3( 17f, 68f, 46f), new Vector3(-8f, -85f, 15f), 5f);

            // ── Debris / rubble around the tower bases (z follows the towers) ──
            Color debris = new Color(0.5f, 0.5f, 0.5f);
            AddRockCluster(new Vector3(-45f, 1f, 42f), 10f, 2.5f, debris, "Debris_A1");
            AddRockCluster(new Vector3(-40f, 1f, 60f), 8f, 2.0f, debris, "Debris_A2");
            AddRockCluster(new Vector3(-55f, 1f, 55f), 7f, 1.8f, debris, "Debris_A3");
            AddRockCluster(new Vector3( 45f, 1f, 44f), 10f, 2.5f, debris, "Debris_B1");
            AddRockCluster(new Vector3( 40f, 1f, 62f), 8f, 2.0f, debris, "Debris_B2");
            AddRockCluster(new Vector3( 55f, 1f, 54f), 7f, 1.8f, debris, "Debris_B3");
            AddRockCluster(new Vector3( 48f, 1f, 32f), 6f, 1.5f, debris, "Debris_B4");

            // ── Skyline backdrop — tall dark silhouette blocks at arena edges (1.5x pos, wider spans) ─
            Color skyline = new Color(0.25f, 0.25f, 0.30f);
            AddBlock(new Vector3(360f, 20f, 0f), new Vector3(6f, 40f, 180f), skyline, "Skyline_E");
            AddBlock(new Vector3(-360f, 18f, 45f), new Vector3(6f, 36f, 150f), skyline, "Skyline_W");
            AddBlock(new Vector3(0f, 15f, 360f), new Vector3(240f, 30f, 6f), skyline, "Skyline_N");
            AddBlock(new Vector3(0f, 12f, -360f), new Vector3(210f, 24f, 6f), skyline, "Skyline_S");
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

            // Canal-edge barriers are now breakable guardrails built along the new
            // (widened) canal lip in BuildCanalDetail — see AddBreakableGuardrail.
        }

        // Deletes any building that spawned inside a bridge crossing corridor so no bridge
        // entrance is blocked. Bridges run N-S at x = {-140,-60,0,60,140} (deck 16 wide) and
        // only exist within |z| < 28; this clears the deck width plus the on-ramp approach band.
        private void ClearBridgeCorridors()
        {
            float[] bridgeX = { -140f, -60f, 0f, 60f, 140f };
            const float clearHalfX = 16f;      // deck half (8) + building clearance margin
            const float corridorHalfZ = 40f;   // bridge ends at z=28; also clear the approach
            var doomed = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in transform)
            {
                if (child == null) continue;
                if (child.name.IndexOf("Building", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Vector3 p = child.localPosition;
                if (Mathf.Abs(p.z) > corridorHalfZ) continue;
                for (int b = 0; b < bridgeX.Length; b++)
                {
                    if (Mathf.Abs(p.x - bridgeX[b]) < clearHalfX)
                    {
                        doomed.Add(child.gameObject);
                        break;
                    }
                }
            }
            for (int i = 0; i < doomed.Count; i++)
                if (doomed[i] != null) Object.Destroy(doomed[i]);
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
        /// <summary>
        /// A short highway-style guardrail (rail beam on posts) running along X,
        /// breakable so vehicles can crash through it into the canal. Centre sits on
        /// the ground; length is along X.
        /// </summary>
        private void AddBreakableGuardrail(Vector3 center, float length)
        {
            var parent = new GameObject("CanalGuardrail");
            parent.transform.SetParent(transform, false);
            parent.transform.position = center;

            Color metal = new Color(0.62f, 0.64f, 0.66f);

            // Horizontal rail beam (runs along X)
            var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.name = "Rail";
            rail.transform.SetParent(parent.transform, false);
            rail.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            rail.transform.localScale = new Vector3(length, 0.22f, 0.12f);
            SetMaterial(rail, MakeMaterial(metal));
            Object.DestroyImmediate(rail.GetComponent<Collider>());

            // Support posts every ~4 units
            int posts = Mathf.Max(2, Mathf.RoundToInt(length / 4f));
            for (int i = 0; i < posts; i++)
            {
                float tx = Mathf.Lerp(-length * 0.5f + 0.3f, length * 0.5f - 0.3f, i / (float)(posts - 1));
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = "Post";
                post.transform.SetParent(parent.transform, false);
                post.transform.localPosition = new Vector3(tx, 0.3f, 0f);
                post.transform.localScale = new Vector3(0.14f, 0.7f, 0.14f);
                SetMaterial(post, MakeMaterial(metal));
                Object.DestroyImmediate(post.GetComponent<Collider>());
            }

            // One parent collider for the whole rail, then make it breakable.
            var box = parent.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.45f, 0f);
            box.size = new Vector3(length, 0.95f, 0.25f);

            CityPrefabHelper.MakeBreakable(parent);
        }

        // Adds detail along both canal banks and fills the far south edge
        // with a small park area.
        private void BuildCanalDetail()
        {
            // ── Breakable guardrails lining both canal lips ──────────
            // Highway-guardrail style: cars and shots smash through them and tumble
            // into the wider/deeper trench. Continuous along each lip EXCEPT a gap in
            // front of every bridge approach so the crossings stay open.
            float[] railBridgeX = { -140f, -60f, 0f, 60f, 140f };
            const float railClear = 13f;   // clear zone each side of a bridge centre (deck 16 wide)
            const float railStep = 16f;
            const float lipZ = 23f;        // canal lip (bank top)
            for (float gx = -264f; gx <= 264f; gx += railStep)
            {
                bool nearBridge = false;
                for (int b = 0; b < railBridgeX.Length; b++)
                    if (Mathf.Abs(gx - railBridgeX[b]) < railClear) { nearBridge = true; break; }
                if (nearBridge) continue;
                AddBreakableGuardrail(new Vector3(gx, 1f,  lipZ), railStep - 2f);
                AddBreakableGuardrail(new Vector3(gx, 1f, -lipZ), railStep - 2f);
            }

            // ── Canal-side lamps (set back on solid ground beyond the lip) ──
            for (float lx = -125f; lx <= 125f; lx += 50f)
            {
                CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(lx, 1f, 26f));
                CityPrefabHelper.PlaceLamp(transform, "Lamp_4_prefab", new Vector3(lx + 25f, 1f, -26f));
            }

            // ── Canal-side potted trees ──────────────────────────────
            for (float tx = -110f; tx <= 110f; tx += 70f)
            {
                CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(tx, 1f, 27f));
                CityPrefabHelper.PlaceProp(transform, "Pot_tree prefab", new Vector3(tx + 20f, 1f, -27f));
            }

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

            // ── Far-north / far-south suburban ring (1.5x: z=330..435; counts ~2.25x) ────
            string[] subBuildings = {
                "Building_A1_prefab", "Building_B1_prefab", "Building_D1_prefab",
                "Building_S_prefab",  "Building_T_prefab",  "Building_V_prefab",
                "Building_W_prefab",  "Building_Y_prefab",  "Building_p_prefab",
                "Building_u_prefab",  "building_X_prefab",
            };
            for (int i = 0; i < 22; i++)
            {
                float bx = -420f + i * 40f + Random.Range(-12f, 12f);
                float bz = 330f + Random.Range(0f, 105f);
                float rot = Random.Range(0, 4) * 90f;
                float scl = Random.Range(0.80f, 1.05f);
                CityPrefabHelper.PlaceBuilding(transform, subBuildings[i % subBuildings.Length],
                    new Vector3(bx, 1f, bz), rot, scl);
            }
            for (int i = 0; i < 22; i++)
            {
                float bx = -420f + i * 40f + Random.Range(-12f, 12f);
                float bz = -435f + Random.Range(0f, 105f);
                float rot = Random.Range(0, 4) * 90f;
                float scl = Random.Range(0.80f, 1.05f);
                CityPrefabHelper.PlaceBuilding(transform, subBuildings[(i + 3) % subBuildings.Length],
                    new Vector3(bx, 1f, bz), rot, scl);
            }

            // ── Far-east / far-west outskirts (1.5x pos, ~2.25x count) ──────────
            for (int i = 0; i < 14; i++)
            {
                float bz = -270f + i * 40f + Random.Range(-9f, 9f);
                CityPrefabHelper.PlaceBuilding(transform, subBuildings[i % subBuildings.Length],
                    new Vector3(375f + Random.Range(-12f, 30f), 1f, bz),
                    Random.Range(0, 4) * 90f, Random.Range(0.80f, 1.05f));
                CityPrefabHelper.PlaceBuilding(transform, subBuildings[(i + 5) % subBuildings.Length],
                    new Vector3(-375f - Random.Range(0f, 42f), 1f, bz),
                    Random.Range(0, 4) * 90f, Random.Range(0.80f, 1.05f));
            }

            // ── Trees along outskirts roads (1.5x pos, ~2.25x count) ─────────────
            for (int i = 0; i < 45; i++)
            {
                float ax = Random.Range(-480f, 480f);
                float azN =  330f + Random.Range(0f, 135f);
                float azS = -465f + Random.Range(0f, 135f);
                CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                    new Vector3(ax + Random.Range(-6f, 6f), 1f, azN),
                    Random.Range(0f, 360f), Random.Range(0.9f, 1.2f));
                CityPrefabHelper.PlaceProp(transform, "Tree prefab",
                    new Vector3(ax + Random.Range(-6f, 6f), 1f, azS),
                    Random.Range(0f, 360f), Random.Range(0.9f, 1.2f));
            }

            // ── Bushes / hedges / pot trees scattered in suburbs (~2.25x count) ───
            string[] subGreens = { "Bush prefab", "hedge prefab", "Pot_tree prefab" };
            for (int i = 0; i < 68; i++)
            {
                float gx = Random.Range(-480f, 480f);
                float gz;
                if (i % 2 == 0) gz = Random.Range(323f, 465f);
                else            gz = Random.Range(-465f, -323f);
                CityPrefabHelper.PlaceProp(transform, subGreens[Random.Range(0, subGreens.Length)],
                    new Vector3(gx, 1f, gz), Random.Range(0f, 360f), Random.Range(0.9f, 1.2f));
            }

            // ── Street lamps along new outer road loops (1.5x radius, more lamps) ────────────
            for (int i = 0; i < 24; i++)
            {
                float t = (i / 24f) * Mathf.PI * 2f;
                CityPrefabHelper.PlaceLamp(transform, "Lamp_2_prefab",
                    new Vector3(Mathf.Cos(t) * 435f, 1f, Mathf.Sin(t) * 435f),
                    Mathf.Rad2Deg * (-t));
            }

            // ── Dumpsters / bins / benches alley dressing (~2.25x count) ──────────
            string[] alleyProps = {
                "Big_trash_bin prefab", "Bin prefab", "trashcan prefab",
                "bench prefab", "Bench 2 prefab", "StreetSellerStand prefab"
            };
            for (int i = 0; i < 40; i++)
            {
                float ax = Random.Range(-465f, 465f);
                float az;
                if (i % 2 == 0) az = Random.Range(330f, 450f);
                else            az = Random.Range(-450f, -330f);
                CityPrefabHelper.PlaceProp(transform, alleyProps[Random.Range(0, alleyProps.Length)],
                    new Vector3(ax, 1f, az), Random.Range(0, 4) * 90f, 1f);
            }

            // ── Power poles along outer roads (1.5x pos) ──────────────────────
            for (int z = -450; z <= 450; z += 60)
            {
                CityPrefabHelper.PlaceProp(transform, "Power_poles prefab",
                    new Vector3( 480f, 1f, z));
                CityPrefabHelper.PlaceProp(transform, "Power_poles prefab",
                    new Vector3(-480f, 1f, z));
            }

            // ── Secondary crashed fighter clusters at new edges (1.5x pos) ────
            Color rubble = new Color(0.5f, 0.5f, 0.5f);
            AddRockCluster(new Vector3( 390f, 1f,  330f), 7f, 1.8f, rubble, "Outer_Debris_NE");
            AddRockCluster(new Vector3(-390f, 1f, -345f), 7f, 1.8f, rubble, "Outer_Debris_SW");
            AddRockCluster(new Vector3(-375f, 1f,  390f), 6f, 1.6f, rubble, "Outer_Debris_NW");
            AddRockCluster(new Vector3( 375f, 1f, -390f), 6f, 1.6f, rubble, "Outer_Debris_SE");

            // ── Skyline backdrop extension at new edges (1.5x pos, wider spans) ────────────
            Color skyline = new Color(0.22f, 0.22f, 0.28f);
            AddBlock(new Vector3( 510f, 22f,   0f), new Vector3(6f, 44f, 270f), skyline, "Skyline2_E");
            AddBlock(new Vector3(-510f, 20f,  45f), new Vector3(6f, 40f, 225f), skyline, "Skyline2_W");
            AddBlock(new Vector3(   0f, 18f, 510f), new Vector3(330f, 36f, 6f), skyline, "Skyline2_N");
            AddBlock(new Vector3(   0f, 15f,-510f), new Vector3(300f, 30f, 6f), skyline, "Skyline2_S");

            // ══════════════════════════════════════════════════════
            // DENSITY PASS 1: outer-ring static building wall to seal gaps
            // why: the 1.5x perimeter (±480..±540) is long; a denser ring of
            //      static buildings keeps the wider boundary visually walled.
            // PlaceBuilding is static (good hard cover, no breakage).
            // ══════════════════════════════════════════════════════
            {
                int ringPlaced = 0, ringAttempts = 0;
                while (ringPlaced < 36 && ringAttempts < 360)
                {
                    ringAttempts++;
                    float t = Random.Range(0f, Mathf.PI * 2f);
                    float r = Random.Range(470f, 535f);
                    float bx = Mathf.Cos(t) * r;
                    float bz = Mathf.Sin(t) * r;
                    if (Mathf.Abs(bz) < 30f) continue; // keep clear of canal mouth at the edges
                    CityPrefabHelper.PlaceBuilding(transform,
                        subBuildings[Random.Range(0, subBuildings.Length)],
                        new Vector3(bx, 1f, bz),
                        Random.Range(0, 4) * 90f, Random.Range(0.85f, 1.15f));
                    ringPlaced++;
                }
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS 2: breakable street-furniture carpet across the
            // expanded mid-to-outer band (±200..±520). PlaceProp items are
            // breakable cover; excludes canal/road core (|z|<200) so the city
            // grid and canal corridor stay clean.
            // ══════════════════════════════════════════════════════
            {
                string[] furniture = {
                    "Bin prefab", "trashcan prefab", "Big_trash_bin prefab",
                    "bench prefab", "Bench 2 prefab", "Bush prefab",
                    "hedge prefab", "Pot_tree prefab", "Tree prefab",
                    "Bus stop prefab", "Mail_box prefab",
                };
                int furPlaced = 0, furAttempts = 0;
                while (furPlaced < 90 && furAttempts < 900)
                {
                    furAttempts++;
                    float fx = Random.Range(-520f, 520f);
                    float fz = Random.Range(-520f, 520f);
                    float d = Mathf.Sqrt(fx * fx + fz * fz);
                    if (d < 200f) continue;             // leave the core city/canal alone
                    if (d > 545f) continue;             // stay inside the 562 wall
                    CityPrefabHelper.PlaceProp(transform,
                        furniture[Random.Range(0, furniture.Length)],
                        new Vector3(fx, 1f, fz), Random.Range(0f, 360f),
                        Random.Range(0.9f, 1.25f));
                    furPlaced++;
                }
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS 3: breakable traffic-sign + lamp dressing along the
            // outer ring band so the wider streets read as a real city edge.
            // PlaceSign / PlaceLamp are breakable.
            // ══════════════════════════════════════════════════════
            {
                string[] signs = {
                    "stop sign", "parking sign", "traffic sign 3",
                    "traffic sign 4", "traffic sign 5", "traffic sign 6",
                    "traffic sign speed 50", "traffic sign speed 70",
                };
                string[] lamps = {
                    "Lamp_1_prefab", "Lamp_2_prefab", "Lamp_5_prefab",
                    "street lamp 2 prefab",
                };
                int sgPlaced = 0, sgAttempts = 0;
                while (sgPlaced < 60 && sgAttempts < 600)
                {
                    sgAttempts++;
                    float sx = Random.Range(-520f, 520f);
                    float sz = Random.Range(-520f, 520f);
                    float d = Mathf.Sqrt(sx * sx + sz * sz);
                    if (d < 210f) continue;
                    if (d > 545f) continue;
                    if (Random.value > 0.5f)
                        CityPrefabHelper.PlaceSign(transform,
                            signs[Random.Range(0, signs.Length)],
                            new Vector3(sx, 1f, sz), Random.Range(0, 4) * 90f);
                    else
                        CityPrefabHelper.PlaceLamp(transform,
                            lamps[Random.Range(0, lamps.Length)],
                            new Vector3(sx, 1f, sz), Random.Range(0f, 360f));
                    sgPlaced++;
                }
            }
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

            // --- Unity Terrain: 1125x1125, height 50 (why: 1.5x enlarge of 750) ---
            var terrain = TerrainFactory.Create(
                transform,
                new Vector3(-562.5f, 0f, -562.5f),
                new Vector3(1125f, 50f, 1125f),
                769,
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

            // Pine forests -- west cluster (1.5x positions; ~2.25x density via second pass)
            float[] treeX = { -180f, -157.5f, -191.25f, -146.25f, -168.75f, -202.5f, -135f, -213.75f };
            float[] treeZ = { 67.5f, 101.25f, 45f, 123.75f, 22.5f, 90f, 78.75f, 33.75f };
            for (int i = 0; i < treeX.Length; i++)
            {
                AddPine(new Vector3(treeX[i], 0f, treeZ[i]), Random.Range(8f, 14f), $"Pine_W_{i}");
            }
            // West cluster densify pass (fill-in pines, jittered between the originals)
            for (int i = 0; i < treeX.Length; i++)
            {
                AddPine(new Vector3(treeX[i] + Random.Range(-14f, 14f), 0f, treeZ[i] + Random.Range(-14f, 14f)),
                    Random.Range(8f, 14f), $"Pine_W_fill_{i}");
            }

            // Pine forests -- east cluster (1.5x positions; ~2.25x density via second pass)
            float[] treeX2 = { 135f, 157.5f, 123.75f, 168.75f, 180f, 146.25f, 191.25f };
            float[] treeZ2 = { -112.5f, -90f, -135f, -78.75f, -123.75f, -157.5f, -101.25f };
            for (int i = 0; i < treeX2.Length; i++)
            {
                AddPine(new Vector3(treeX2[i], 0f, treeZ2[i]), Random.Range(8f, 14f), $"Pine_E_{i}");
            }
            // East cluster densify pass
            for (int i = 0; i < treeX2.Length; i++)
            {
                AddPine(new Vector3(treeX2[i] + Random.Range(-14f, 14f), 0f, treeZ2[i] + Random.Range(-14f, 14f)),
                    Random.Range(8f, 14f), $"Pine_E_fill_{i}");
            }

            // Ice rock formations (1.5x positions)
            AddRockCluster(new Vector3(-67.5f, 0f, -90f), 4f, 8f * 0.3f, new Color(0.7f, 0.85f, 0.95f), "IceRock_1");
            AddRockCluster(new Vector3(90f, 0f, 45f), 3f, 6f * 0.3f, new Color(0.7f, 0.85f, 0.95f), "IceRock_2");
            AddRockCluster(new Vector3(-33.75f, 0f, 67.5f), 3.5f, 7f * 0.3f, new Color(0.7f, 0.85f, 0.95f), "IceRock_3");
            AddRockCluster(new Vector3(56.25f, 0f, -112.5f), 2.5f, 5f * 0.3f, new Color(0.7f, 0.85f, 0.95f), "IceRock_4");

            // Rock clusters (1.5x positions)
            AddRockCluster(new Vector3(112.5f, 0f, 135f), 8f, 3f, darkRock, "Rocks_NE");
            AddRockCluster(new Vector3(-90f, 0f, -157.5f), 6f, 2.5f, cliffGray, "Rocks_S");

            // ── Outer pine forest rings (1.5x radii; count 26→58 for the longer perimeter) ──
            // why: keeps the expanded outskirts visually interesting without blocking spawn lanes
            for (int i = 0; i < 58; i++)
            {
                float t = (i / 58f) * Mathf.PI * 2f;
                float r = Random.Range(315f, 480f);
                float px = Mathf.Cos(t) * r + Random.Range(-18f, 18f);
                float pz = Mathf.Sin(t) * r + Random.Range(-18f, 18f);
                AddPine(new Vector3(px, 0f, pz), Random.Range(8f, 16f), $"Pine_Outer_{i}");
            }

            // ── Outer rock clusters (1.5x positions) ──────────────────
            AddRockCluster(new Vector3( 360f, 0f,  330f), 10f, 3.5f, darkRock, "Rocks_NE_Outer");
            AddRockCluster(new Vector3(-345f, 0f,  345f), 9f, 3f, cliffGray, "Rocks_NW_Outer");
            AddRockCluster(new Vector3( 375f, 0f, -360f), 8f, 3f, darkRock, "Rocks_SE_Outer");
            AddRockCluster(new Vector3(-375f, 0f, -330f), 10f, 3.5f, cliffGray, "Rocks_SW_Outer");
            AddRockCluster(new Vector3(-450f, 0f,    0f), 8f, 3f, darkRock, "Rocks_W_Outer");
            AddRockCluster(new Vector3( 450f, 0f,   60f), 8f, 3f, cliffGray, "Rocks_E_Outer");
            AddRockCluster(new Vector3(   0f, 0f,  450f), 10f, 3.5f, darkRock, "Rocks_N_Outer");
            AddRockCluster(new Vector3(  30f, 0f, -450f), 9f, 3f, cliffGray, "Rocks_S_Outer");

            // ── Perimeter enclosure ring of rock peaks lining the 562 wall ───────
            // why: the longer 1.5x perimeter needs more peaks so there are no visible
            //      gaps; ring radius ~510 sits just inside the 562 invisible wall.
            for (int i = 0; i < 30; i++)
            {
                float t = (i / 30f) * Mathf.PI * 2f;
                float r = Random.Range(495f, 535f);
                float wx = Mathf.Cos(t) * r;
                float wz = Mathf.Sin(t) * r;
                Color wallCol = (i % 2 == 0) ? darkRock : cliffGray;
                AddRockCluster(new Vector3(wx, 0f, wz), Random.Range(9f, 13f), Random.Range(3.5f, 5f),
                    wallCol, $"Rocks_Wall_{i}");
            }

            // ── Ice rock clusters scattered in outer ring (1.5x radii; count 10→23) ──
            Color iceCol = new Color(0.7f, 0.85f, 0.95f);
            for (int i = 0; i < 23; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(270f, 450f);
                AddRockCluster(new Vector3(Mathf.Cos(t) * r, 0f, Mathf.Sin(t) * r),
                    Random.Range(3f, 6f), Random.Range(1.5f, 2.5f), iceCol, $"IceRock_Outer_{i}");
            }

            // ── Snow drift blocks (1.5x range ±480; count 14→32) ────────────
            Color drift = new Color(0.92f, 0.96f, 1f);
            for (int i = 0; i < 32; i++)
            {
                float dx = Random.Range(-480f, 480f);
                float dz = Random.Range(-480f, 480f);
                if (Mathf.Sqrt(dx * dx + dz * dz) < 120f) continue; // keep center clean
                float sx = Random.Range(4f, 10f);
                float sz = Random.Range(4f, 10f);
                AddBlockUnchecked(new Vector3(dx, 0.4f, dz), new Vector3(sx, 0.8f, sz), drift, $"SnowDrift_{i}");
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS 1: mid-band pine scatter (filling the 1.5x ring gap)
            // why: between the inner forests (±200) and outer ring (±315) the
            //      enlarged map had a sparse band; ~45 jittered pines add cover.
            // ══════════════════════════════════════════════════════
            for (int i = 0; i < 45; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(180f, 320f);
                float px = Mathf.Cos(t) * r + Random.Range(-20f, 20f);
                float pz = Mathf.Sin(t) * r + Random.Range(-20f, 20f);
                AddPine(new Vector3(px, 0f, pz), Random.Range(8f, 15f), $"Pine_Mid_{i}");
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS 2: scattered ice + dark rock clusters across the
            // expanded mid-to-outer band (±150..±500). Static landmark cover.
            // ══════════════════════════════════════════════════════
            {
                int rkPlaced = 0, rkAttempts = 0;
                while (rkPlaced < 40 && rkAttempts < 400)
                {
                    rkAttempts++;
                    float rx = Random.Range(-500f, 500f);
                    float rz = Random.Range(-500f, 500f);
                    float d = Mathf.Sqrt(rx * rx + rz * rz);
                    if (d < 150f) continue;   // leave the frozen-lake core clear
                    if (d > 510f) continue;   // stay inside the perimeter wall ring
                    bool icy = (rkPlaced % 3 == 0);
                    Color rc = icy ? iceCol : ((rkPlaced % 2 == 0) ? darkRock : cliffGray);
                    AddRockCluster(new Vector3(rx, 0f, rz),
                        Random.Range(4f, 8f), Random.Range(2f, 3.5f), rc, $"Rocks_Scatter_{rkPlaced}");
                    rkPlaced++;
                }
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS 3: low snow-drift cover carpet (breakable-feel low
            // blocks) across the mid-to-outer band so the wider field reads as
            // a populated snowscape, not empty terrain.
            // ══════════════════════════════════════════════════════
            {
                int dPlaced = 0, dAttempts = 0;
                while (dPlaced < 36 && dAttempts < 360)
                {
                    dAttempts++;
                    float dx = Random.Range(-500f, 500f);
                    float dz = Random.Range(-500f, 500f);
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < 140f) continue;
                    if (d > 515f) continue;
                    float sx = Random.Range(3f, 8f);
                    float sz = Random.Range(3f, 8f);
                    AddBlockUnchecked(new Vector3(dx, 0.35f, dz), new Vector3(sx, 0.7f, sz),
                        drift, $"SnowDrift_Scatter_{dPlaced}");
                    dPlaced++;
                }
            }

            // why: spawn ring radius 260→390 (1.5x) so players start outside the new dense props
            AddSpawnRing(Vector3.zero, 390f, 8, 1f);

            // Invisible arena boundary walls (1.5x: 375→562)
            AddInvisibleWalls(562f, 50f);

            // ── Cold directional light ─────────────────────────────
            var arcticSun = new GameObject("ArcticSun");
            arcticSun.transform.SetParent(transform, false);
            var asl = arcticSun.AddComponent<Light>();
            asl.type = LightType.Directional;
            asl.color = new Color(0.80f, 0.88f, 1f); // cold blue-white
            asl.intensity = 0.9f;
            asl.transform.rotation = Quaternion.Euler(35f, 15f, 0f); // low arctic sun
            asl.shadows = LightShadows.Soft;

            // ── Atmosphere: cold polar air + sky ───────────────────
            // why: fog thinned (was 0.005, a near-whiteout) so the snowfields, ridges
            // and rock formations read across the tundra; a real cold sky added.
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.80f, 0.86f, 0.92f);
            RenderSettings.fogDensity = 0.0025f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.62f, 0.70f, 0.82f);
            RenderSettings.sun = asl;
            BuildSky();   // procedural pale-cold polar sky (drives skybox ambient + sun disc)

            // Ambient VFX
            VFXManager.GroundFog(Vector3.zero, 6f);
            VFXManager.GroundFog(new Vector3( 300f, 1f,  300f), 4f);
            VFXManager.GroundFog(new Vector3(-300f, 1f, -300f), 4f);
            VFXManager.DustMotes(new Vector3(0, 10, 0), 5f); // fine ice-crystal sparkle

            // ── Dynamic life ───────────────────────────────────────
            // Real driving snow (replaces the thematically-wrong rain), camera-following
            Snowfall.Create(transform, 70f, 700f, 280f, new Vector3(3f, 0f, 1f));
            // White arctic gulls wheeling over the tundra
            SeabirdFlock.Create(transform, new Vector3(0f, 0f, 0f), 5, 240f);
            SeabirdFlock.Create(transform, new Vector3(180f, 0f, -150f), 3, 130f);

            RegisterAINavZones();   // keep bots off the slippery frozen lake
        }

        // =================================================================
        // Atmosphere / AI nav helpers
        // =================================================================

        /// <summary>Procedural pale-cold polar sky with a snow-white horizon; drives
        /// skybox-based ambient and the sun disc.</summary>
        private void BuildSky()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return;
            var sky = new Material(shader);
            sky.SetColor("_SkyTint",     new Color(0.58f, 0.68f, 0.82f)); // pale cold blue
            sky.SetColor("_GroundColor", new Color(0.80f, 0.85f, 0.92f)); // snow-white horizon
            sky.SetFloat("_AtmosphereThickness", 1.1f);                   // crisp polar air
            sky.SetFloat("_Exposure",  1.1f);
            sky.SetFloat("_SunSize",   0.05f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>Register the slippery frozen lake as an AI navigation-avoid zone
        /// (the pure-data AI HazardZone — steer only, no damage) so bots don't slide
        /// out of control across the ice. The lake ice imparts a slide force.</summary>
        private void RegisterAINavZones()
        {
            CloseEncounters.AI.AIController.RegisterHazardZone(new CloseEncounters.AI.HazardZone
            {
                center = new Vector3(15f, 0f, -15f),
                halfExtents = new Vector3(42f, 40f, 42f)   // frozen lake (ice radius ~35 + margin)
            });
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
            // why: 1.5x enlarge of 750 -> 1125x1125; res 513->769 to hold detail-per-metre
            var terrain = TerrainFactory.Create(transform,
                new Vector3(-562.5f, 0f, -562.5f), new Vector3(1125f, 80f, 1125f),
                769, "VolcanicTerrain");

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
            // why: spawn x/z multiplied by 1.5 (outer ring ~330..390) away from caldera & magma fields
            AddSpawnPoints(
                new Vector3(-330f, 3f,  270f),  new Vector3(330f, 3f,  270f),
                new Vector3(-330f, 3f, -270f),  new Vector3(330f, 3f, -270f),
                new Vector3(-390f, 3f,    0f),  new Vector3(390f, 3f,    0f),
                new Vector3(   0f, 3f,  390f),  new Vector3(  0f, 3f, -390f)
            );
            AddInvisibleWalls(562f, 50f);

            // ── Dynamic life + AI navigation ───────────────────────
            // Make the volcano actually erupt: periodic fireballs + ballistic magma
            // debris + persistent crater fire/smoke. The VolcanoEruption component was
            // fully built but never attached, so the centrepiece did nothing.
            var volcano = new GameObject("Volcano");
            volcano.transform.SetParent(transform, false);
            volcano.transform.position = new Vector3(0f, 22f, 0f); // caldera floor
            volcano.AddComponent<VolcanoEruption>();

            // Falling volcanic ash drifting on the wind (reuses the snow field, grey).
            Snowfall.Create(transform, 80f, 760f, 220f, new Vector3(4f, 0f, 2f),
                new Color(0.35f, 0.32f, 0.30f, 0.55f));

            RegisterAINavZones();   // keep bots out of the lethal volcanic core (cone + lava)
        }

        // =================================================================
        // Atmosphere / AI nav helpers
        // =================================================================

        /// <summary>Brooding ash-and-ember procedural sky: a dark smoke-grey dome over a
        /// hot ember-orange horizon; drives skybox-based ambient and the glow sun disc.</summary>
        private void BuildSky()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return;
            var sky = new Material(shader);
            sky.SetColor("_SkyTint",     new Color(0.20f, 0.16f, 0.18f)); // dark ashen sky
            sky.SetColor("_GroundColor", new Color(0.45f, 0.20f, 0.08f)); // ember-orange horizon
            sky.SetFloat("_AtmosphereThickness", 1.7f);                   // thick ash atmosphere
            sky.SetFloat("_Exposure",  0.95f);
            sky.SetFloat("_SunSize",   0.06f);
            sky.SetFloat("_SunSizeConvergence", 3f);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.85f;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>Register the volcanic core (cone + caldera + radiating lava streams)
        /// as an AI navigation-avoid zone (the pure-data AI HazardZone — steer only, no
        /// damage) so bots fight in the ring around the volcano instead of driving into
        /// the lethal lava or ramming the impassable cone.</summary>
        private void RegisterAINavZones()
        {
            CloseEncounters.AI.AIController.RegisterHazardZone(new CloseEncounters.AI.HazardZone
            {
                center = new Vector3(0f, 0f, 0f),
                halfExtents = new Vector3(120f, 60f, 120f)   // cone (radius ~100) + lava streams
            });
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

            // ── North wall (z ≈ 368..405, 1.5x): 3 canyon + 2 magma ──
            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-300f, 0f, 382.5f), 15f, 7f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_01",
                new Vector3(-150f, 0f, 367.5f), 60f, 4f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(0f, 0f, 390f), 90f, 8f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_02",
                new Vector3(150f, 0f, 372f), 150f, 3f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(300f, 0f, 378f), 200f, 6f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            // ── South wall (z ≈ -405..-368, 1.5x): 3 canyon + 2 magma ─
            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-285f, 0f, -387f), 180f, 7f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_03",
                new Vector3(-120f, 0f, -367.5f), 240f, 5f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(45f, 0f, -397.5f), 135f, 8f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_01",
                new Vector3(195f, 0f, -375f), 300f, 4f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(315f, 0f, -382.5f), 45f, 6f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            // ── East wall (x ≈ 368..405, 1.5x): 3 canyon + 1 magma ───
            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(390f, 0f, -225f), 90f, 7f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_02",
                new Vector3(375f, 0f, -60f), 120f, 5f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(397.5f, 0f, 90f), 270f, 6f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(382.5f, 0f, 255f), 160f, 5f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            // ── West wall (x ≈ -405..-368, 1.5x): 3 canyon + 1 magma ─
            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-397.5f, 0f, -240f), 0f, 6f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-387f, 0f, -60f), 315f, 7f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaMountain_03",
                new Vector3(-378f, 0f, 105f), 210f, 4f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            mtn = VolcanicPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(-390f, 0f, 270f), 100f, 5f);
            if (mtn != null) VolcanicPrefabHelper.TintVolcanic(mtn, volcanicTint);

            // ── Perimeter peak in-fill ring lining the 562 wall ──────
            // why: the 1.5x perimeter is longer; the 4 hand-placed walls (~5 each =
            //      20 peaks) leave gaps. Add 28 more peaks on a ~395..425 ring so the
            //      longer edge stays walled with no visible see-through gaps.
            string[] ringCanyons = { "mountain_canyon_01", "mountain_canyon_02",
                "mountain_canyon_03", "mountain_canyon_04", "mountain_canyon_05" };
            string[] ringMagma = { "MagmaMountain_01", "MagmaMountain_02", "MagmaMountain_03" };
            for (int i = 0; i < 28; i++)
            {
                float t = (i / 28f) * Mathf.PI * 2f + 0.11f; // offset so they sit between the hand-placed peaks
                float r = Random.Range(395f, 425f);
                float wx = Mathf.Cos(t) * r;
                float wz = Mathf.Sin(t) * r;
                GameObject peak = (i % 3 == 0)
                    ? VolcanicPrefabHelper.PlaceStaticProp(transform,
                        ringMagma[Random.Range(0, ringMagma.Length)],
                        new Vector3(wx, 0f, wz), Random.Range(0f, 360f), Random.Range(3f, 5f))
                    : VolcanicPrefabHelper.PlaceMountain(transform,
                        ringCanyons[Random.Range(0, ringCanyons.Length)],
                        new Vector3(wx, 0f, wz), Random.Range(0f, 360f), Random.Range(5f, 8f));
                if (peak != null) VolcanicPrefabHelper.TintVolcanic(peak, volcanicTint);
            }
        }

        // ── MAGMA FORMATIONS: rocks, platforms, cave ───────────────
        private void BuildMagmaFormations()
        {
            // ── 8 rock clusters (2-3 rocks each), positions ×1.5 ────
            // Cluster 1 – NE quadrant
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(150f, 2f, 120f), 0f, 3.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(162f, 2f, 111f), 45f, 2.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(141f, 2f, 132f), 120f, 2.0f);

            // Cluster 2 – NW quadrant
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(-165f, 2f, 135f), 90f, 3.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(-153f, 2f, 144f), 200f, 2.8f);

            // Cluster 3 – SE quadrant
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(135f, 2f, -150f), 180f, 3.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(147f, 2f, -141f), 270f, 2.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(124.5f, 2f, -162f), 135f, 3.2f);

            // Cluster 4 – SW quadrant
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(-142.5f, 2f, -165f), 60f, 4.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(-154.5f, 2f, -156f), 150f, 2.5f);

            // Cluster 5 – east-center
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(210f, 2f, -30f), 30f, 3.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(222f, 2f, -21f), 210f, 2.8f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(199.5f, 2f, -42f), 300f, 2.2f);

            // Cluster 6 – west-center
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(-210f, 2f, 45f), 270f, 3.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(-198f, 2f, 36f), 90f, 3.8f);

            // Cluster 7 – north-center
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(-30f, 2f, 195f), 15f, 2.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(-18f, 2f, 207f), 165f, 3.0f);

            // Cluster 8 – south-center
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(37.5f, 2f, -202.5f), 240f, 3.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(49.5f, 2f, -192f), 75f, 2.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(27f, 2f, -213f), 330f, 2.0f);

            // ── 4 elevated platforms (cover), positions ×1.5 ────────
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_01",
                new Vector3(240f, 2f, 180f), 0f, 2.5f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_02",
                new Vector3(-232.5f, 2f, -172.5f), 90f, 2.8f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_01",
                new Vector3(-240f, 2f, 195f), 180f, 2.2f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_02",
                new Vector3(225f, 2f, -187.5f), 270f, 3.0f);

            // ── 2 caves (major cover), positions ×1.5 ───────────────
            // NW sector
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaCave",
                new Vector3(-180f, 2f, 210f), 45f, 3.5f);
            // SE sector
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaCave",
                new Vector3(187.5f, 2f, -217.5f), 225f, 4.0f);

            // ── 6 standalone large boulders (scattered cover), ×1.5 ──
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(90f, 2f, 75f), 0f, 4.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(-105f, 2f, -90f), 120f, 3.5f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(-82.5f, 2f, 105f), 210f, 5.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_01",
                new Vector3(112.5f, 2f, -82.5f), 315f, 4.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_02",
                new Vector3(255f, 2f, 15f), 60f, 3.0f);
            VolcanicPrefabHelper.PlaceMagmaRock(transform, "MagmaRock_03",
                new Vector3(-262.5f, 2f, -22.5f), 150f, 3.5f);

            // ── Outer ring magma fields (expansion zone, radius ×1.5: 270→405) ─
            // why: count 6→13 anchors so the longer ring stays dense
            string[] magmaRocks = { "MagmaRock_01", "MagmaRock_02", "MagmaRock_03" };
            for (int c = 0; c < 13; c++)
            {
                float t = (c / 13f) * Mathf.PI * 2f;
                Vector3 anchor = new Vector3(Mathf.Cos(t) * 405f, 2f, Mathf.Sin(t) * 405f);
                int count = Random.Range(5, 9);
                for (int i = 0; i < count; i++)
                {
                    float ox = Random.Range(-33f, 33f);
                    float oz = Random.Range(-33f, 33f);
                    VolcanicPrefabHelper.PlaceMagmaRock(transform,
                        magmaRocks[Random.Range(0, magmaRocks.Length)],
                        new Vector3(anchor.x + ox, 2f, anchor.z + oz),
                        Random.Range(0f, 360f), Random.Range(2.2f, 4.0f));
                }
            }

            // ── Additional outer platforms & caves, positions ×1.5 ───
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_01",
                new Vector3( 360f, 2f,  330f), 30f, 2.8f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_02",
                new Vector3(-360f, 2f,  345f), 120f, 2.6f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_01",
                new Vector3( 375f, 2f, -345f), 210f, 3.0f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaPlatform_02",
                new Vector3(-390f, 2f, -315f), 300f, 2.4f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaCave",
                new Vector3( 420f, 2f,   60f), 90f, 3.8f);
            VolcanicPrefabHelper.PlaceStaticProp(transform, "MagmaCave",
                new Vector3(-420f, 2f,  -45f), 270f, 3.8f);

            // ── Outer obsidian shard field (radius ×1.5: 200-310→300-465; count 14→32) ─
            Color obsidian = new Color(0.08f, 0.06f, 0.10f);
            for (int i = 0; i < 32; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(300f, 465f);
                float x = Mathf.Cos(t) * r + Random.Range(-10f, 10f);
                float z = Mathf.Sin(t) * r + Random.Range(-10f, 10f);
                float h = Random.Range(6f, 14f);
                AddBlockUnchecked(new Vector3(x, h, z),
                    new Vector3(Random.Range(1.5f, 3.5f), h * 2f, Random.Range(1.5f, 3.5f)),
                    obsidian, $"OuterShard_{i}");
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS A: breakable small magma rocks scattered across the
            // expanded mid-to-outer band. PlaceMagmaRock with maxDim<=4 stays
            // breakable, so small scale (2.0..3.5) keeps these destructible cover.
            // ══════════════════════════════════════════════════════
            {
                int mrPlaced = 0, mrAttempts = 0;
                while (mrPlaced < 70 && mrAttempts < 700)
                {
                    mrAttempts++;
                    float rx = Random.Range(-500f, 500f);
                    float rz = Random.Range(-500f, 500f);
                    float d = Mathf.Sqrt(rx * rx + rz * rz);
                    if (d < 90f) continue;    // keep caldera + lava streams clear
                    if (d > 480f) continue;   // stay inside perimeter peaks
                    VolcanicPrefabHelper.PlaceMagmaRock(transform,
                        magmaRocks[Random.Range(0, magmaRocks.Length)],
                        new Vector3(rx, 2f, rz), Random.Range(0f, 360f),
                        Random.Range(2.0f, 3.5f));
                    mrPlaced++;
                }
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS B: mid-band magma platforms (static cover islands)
            // filling the gap between inner formations (±240) and outer ring (±405).
            // ══════════════════════════════════════════════════════
            {
                string[] platforms = { "MagmaPlatform_01", "MagmaPlatform_02" };
                int plPlaced = 0, plAttempts = 0;
                while (plPlaced < 16 && plAttempts < 160)
                {
                    plAttempts++;
                    float px = Random.Range(-420f, 420f);
                    float pz = Random.Range(-420f, 420f);
                    float d = Mathf.Sqrt(px * px + pz * pz);
                    if (d < 150f) continue;
                    if (d > 430f) continue;
                    VolcanicPrefabHelper.PlaceStaticProp(transform,
                        platforms[Random.Range(0, platforms.Length)],
                        new Vector3(px, 2f, pz), Random.Range(0f, 360f),
                        Random.Range(2.2f, 3.0f));
                    plPlaced++;
                }
            }
        }

        // ── VOLCANIC PROPS: trees, grass, obsidian, wood crosses ───
        private void BuildVolcanicProps()
        {
            // Helper: generate a position on the arena floor, avoiding central caldera
            // why: range ±480 (×1.5) covers the expanded 1125 arena (half-extent 562 minus buffer)
            Vector3 ArenaPos()
            {
                float x, z;
                do
                {
                    x = Random.Range(-480f, 480f);
                    z = Random.Range(-480f, 480f);
                } while (Mathf.Sqrt(x * x + z * z) < 50f);
                return new Vector3(x, 2f, z);
            }

            // ── MagmaTree (50-68 ≈ ×2.25): dead charred trees, some clustered ─
            int treeCount = Random.Range(50, 69);
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

            // ── MagmaGrass (76-96 ≈ ×2.25): volcanic scrub scattered everywhere ─
            int grassCount = Random.Range(76, 97);
            for (int i = 0; i < grassCount; i++)
            {
                Vector3 pos = ArenaPos();
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.5f, 2.5f);
                VolcanicPrefabHelper.PlaceMagmaProp(transform, "MagmaGrass", pos, rot, scl);
            }

            // ── MagmaWoodCross (31-40 ≈ ×2.25): eerie markers near edges & caves ─
            int crossCount = Random.Range(31, 41);
            for (int i = 0; i < crossCount; i++)
            {
                // Bias toward outer ring (radius ×1.5: 90-435) for lava-edge / cave feel
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float radius = Random.Range(90f, 435f);
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                Vector3 pos = new Vector3(x, 2f, z);

                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.5f, 2.0f);
                VolcanicPrefabHelper.PlaceMagmaProp(transform, "MagmaWoodCross", pos, rot, scl);
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS C: extra MagmaGrass + MagmaTree carpet across the
            // newly-exposed outer band (±240..±470) so the enlarged floor reads
            // as dense volcanic scrub rather than empty terrain. Both are
            // breakable PlaceMagmaProp calls (small scale).
            // ══════════════════════════════════════════════════════
            {
                int gPlaced = 0, gAttempts = 0;
                while (gPlaced < 80 && gAttempts < 800)
                {
                    gAttempts++;
                    float gx = Random.Range(-470f, 470f);
                    float gz = Random.Range(-470f, 470f);
                    float d = Mathf.Sqrt(gx * gx + gz * gz);
                    if (d < 240f) continue;   // inner band already dense from passes above
                    if (d > 470f) continue;
                    Vector3 pos = new Vector3(gx, 2f, gz);
                    if (Random.value < 0.7f)
                        VolcanicPrefabHelper.PlaceMagmaProp(transform, "MagmaGrass", pos,
                            Random.Range(0f, 360f), Random.Range(1.5f, 2.5f));
                    else
                        VolcanicPrefabHelper.PlaceMagmaProp(transform, "MagmaTree", pos,
                            Random.Range(0f, 360f), Random.Range(1.5f, 3.0f));
                    gPlaced++;
                }
            }
        }

        // ── FIRE VFX + ATMOSPHERE ──────────────────────────────────
        private void BuildFireAndAtmosphere()
        {
            // ── Atmosphere ─────────────────────────────────────────
            // why: fog thinned (was 0.006) so the central volcano and its eruptions read
            // across the arena; kept dark + warm for an ash-choked sky.
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.22f, 0.15f, 0.12f); // dark volcanic haze
            RenderSettings.fogDensity = 0.0035f;
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
            RenderSettings.sun = sun;
            BuildSky();   // brooding ash-and-ember procedural sky

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
                // why: vent x/z ×1.5 to track the moved magma formations
                Vector3[] ventPositions = {
                    new Vector3(-225f, 8f, 180f),
                    new Vector3(252f, 6f, -180f),
                    new Vector3( 390f, 6f,  315f),
                    new Vector3(-405f, 6f, -300f),
                    new Vector3( 420f, 6f,  -60f),
                    new Vector3(-435f, 6f,   75f),
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
            // ── Terrain: 1125x1125, 80m height (why: 1.5x scale, ~5.6x playable area) ───
            var terrain = TerrainFactory.Create(transform,
                new Vector3(-562.5f, 0f, -562.5f), new Vector3(1125f, 80f, 1125f),
                769, "HighlandsTerrain");

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
            herd.horseCount = 101; // why: 1.5x arena, ~2.25x density for visual activity
            herd.spawnRadius = 330f;
            herd.roamRadius = 75f;

            // ── Spawn + bounds ─────────────────────────────────────
            // why: spawns pushed outward into outer steppe (~345) for the 1.5x arena
            AddSpawnPoints(
                new Vector3(-300f, 5f,  255f), new Vector3( 300f, 5f,  255f),
                new Vector3(-300f, 5f, -255f), new Vector3( 300f, 5f, -255f),
                new Vector3(-390f, 4f,    0f), new Vector3( 390f, 4f,    0f),
                new Vector3(   0f, 5f,  390f), new Vector3(   0f, 5f, -390f)
            );
            AddInvisibleWalls(562f, 70f);

            // ── Highland sun (soft, slightly warm) ─────────────────
            var hSun = new GameObject("HighlandsSun");
            hSun.transform.SetParent(transform, false);
            var hsl = hSun.AddComponent<Light>();
            hsl.type = LightType.Directional;
            hsl.color = new Color(1f, 0.96f, 0.88f);
            hsl.intensity = 0.95f;
            hsl.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            hsl.shadows = LightShadows.Soft;

            // ── Atmosphere: clear high-altitude air so the peaks read ───
            // why: the old 0.0038 mist hid the entire mountain ring — the whole point
            // of a mountain-enclosed valley. Thinned + cooled so the enclosure + sky show.
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.74f, 0.78f, 0.84f); // cool alpine haze
            RenderSettings.fogDensity = 0.0022f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.64f, 0.66f, 0.72f);
            RenderSettings.sun = hsl;
            BuildSky();   // procedural high-alpine sky (drives skybox ambient + sun disc)

            VFXManager.GroundFog(Vector3.zero, 5f);
            VFXManager.GroundFog(new Vector3(-360f, 3f,  330f), 4f);
            VFXManager.GroundFog(new Vector3( 360f, 3f, -330f), 4f);
            VFXManager.DustMotes(new Vector3(0, 5, 0), 4f);
            VFXManager.Rain(new Vector3(0, 20, 0), 2f);

            // ── Dynamic life + AI navigation ───────────────────────
            BuildHearthSmoke();      // hearth smoke rising from the farm & village yurts
            Color eagle = new Color(0.30f, 0.24f, 0.16f);
            SeabirdFlock.Create(transform, new Vector3(0f, 0f, 0f), 5, 260f, eagle);
            SeabirdFlock.Create(transform, new Vector3(160f, 0f, -120f), 4, 170f, eagle);
            RegisterAINavZones();    // bias bots onto the bridges rather than into the river
        }

        // =================================================================
        // Atmosphere / dynamic life / AI nav helpers
        // =================================================================

        /// <summary>Procedural high-alpine sky: deep thin-air blue, cool rocky horizon,
        /// bright sun — and skybox-based ambient.</summary>
        private void BuildSky()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return;
            var sky = new Material(shader);
            sky.SetColor("_SkyTint",     new Color(0.45f, 0.58f, 0.82f)); // deep alpine blue
            sky.SetColor("_GroundColor", new Color(0.52f, 0.55f, 0.58f)); // cool rock/snow horizon
            sky.SetFloat("_AtmosphereThickness", 0.85f);                  // thin high-altitude air
            sky.SetFloat("_Exposure",  1.25f);                            // bright snow-glare sun
            sky.SetFloat("_SunSize",   0.04f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>Thin wisps of hearth smoke over the farm & village yurt clusters.</summary>
        private void BuildHearthSmoke()
        {
            Vector3[] hearths =
            {
                new Vector3(-100f, 5.5f, -10f), new Vector3(-118f, 5.5f, 12f), new Vector3(-82f, 5.5f, -32f), // farm
                new Vector3(48f, 5.5f, 2f),     new Vector3(66f, 5.5f, 14f),   new Vector3(38f, 5.5f, -18f),   // village
            };
            foreach (var h in hearths)
            {
                var anchor = new GameObject("Hearth");
                anchor.transform.SetParent(transform, false);
                anchor.transform.position = h;
                VFXManager.AttachSmoke(anchor.transform, 0.6f);
            }
        }

        /// <summary>The N-S river bisects the valley and is a "don't-loiter" kill ditch.
        /// Register the OPEN-WATER spans (x≈0) as AI navigation-avoid zones with a gap at
        /// each of the 5 bridges, so bots route over the crossings instead of through the
        /// river. Pure steering bias (the AI HazardZone struct never damages).</summary>
        private void RegisterAINavZones()
        {
            float[] bridgeZ = { 225f, 90f, -45f, -150f, -285f };
            System.Array.Sort(bridgeZ); // ascending
            const float gap = 12f;       // clear zone each side of a bridge (deck z half ~4)
            const float zMin = -300f, zMax = 300f; // only within the AI bound (~±300)

            float cursor = zMin;
            for (int i = 0; i < bridgeZ.Length; i++)
            {
                float gMin = bridgeZ[i] - gap;
                if (gMin > cursor + 1f) AddRiverAvoid(cursor, gMin);
                cursor = bridgeZ[i] + gap;
            }
            if (zMax > cursor + 1f) AddRiverAvoid(cursor, zMax);
        }

        private void AddRiverAvoid(float zStart, float zEnd)
        {
            float cz = (zStart + zEnd) * 0.5f;
            float hz = (zEnd - zStart) * 0.5f;
            CloseEncounters.AI.AIController.RegisterHazardZone(new CloseEncounters.AI.HazardZone
            {
                center = new Vector3(0f, 4f, cz),
                halfExtents = new Vector3(12f, 30f, hz)
            });
        }

        // ── MOUNTAIN ENCLOSURE: tall peaks around all edges ────────
        private void BuildMountainEnclosure()
        {
            // why: peaks pushed to ~487-517 (1.5x) to hug the new 562 half-extent wall
            // ── Center backdrop: dominant snow peak ──────────────────
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(0f, 0f, 517.5f), 0f, 12f);

            // ── North wall: 9 snow/canyon mountains (extra peaks fill the longer perimeter) ──
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-390f, 0f, 495f), 10f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-315f, 0f, 505f), 50f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(-210f, 0f, 510f), 45f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3( -45f, 0f, 480f), 90f, 8f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(  60f, 0f, 500f), 120f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3( 180f, 0f, 502f), 160f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3( 360f, 0f, 487f), 200f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3( 480f, 0f, 450f), 230f, 6f);

            // ── South wall: 8 peaks ─────────────────────────────────
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-390f, 0f, -502f), 180f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(-270f, 0f, -512f), 150f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-180f, 0f, -517f), 135f, 8f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(  45f, 0f, -487f), 270f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3( 150f, 0f, -500f), 250f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3( 240f, 0f, -510f), 220f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3( 405f, 0f, -480f), 60f, 6f);

            // ── East wall: 6 peaks ──────────────────────────────────
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(510f, 0f, -300f), 90f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(515f, 0f, -180f), 110f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(502f, 0f,  -90f), 120f, 8f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(517f, 0f,  120f), 75f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(512f, 0f,  240f), 200f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(502f, 0f,  330f), 150f, 7f);

            // ── West wall: 6 peaks ──────────────────────────────────
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(-517f, 0f, -300f), 0f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(-515f, 0f, -180f), 200f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(-502f, 0f,  -90f), 270f, 7f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(-510f, 0f,  120f), 315f, 8f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-517f, 0f,  240f), 95f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(-502f, 0f,  330f), 190f, 6f);

            // ── Corner peaks ───────────────────────────────────────
            HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_01",
                new Vector3( 487f, 0f,  487f), 45f, 5f);
            HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_02",
                new Vector3(-487f, 0f,  487f), 135f, 6f);
            HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_03",
                new Vector3( 487f, 0f, -487f), 315f, 5f);
            HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_01",
                new Vector3(-487f, 0f, -487f), 225f, 6f);
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
                riverGO.transform.localScale = new Vector3(14f, 0.3f, 1125f);
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
            AddWaterHazard(new Vector3(0f, 0f, 0f), new Vector3(14f, 3f, 1125f), "RiverHazard");

            // ── Stone bridges (5 crossings; z scaled 1.5x, +2 for the longer river) ──
            Color stone = new Color(0.55f, 0.52f, 0.48f);
            AddBridge(new Vector3(-8f, 2f, 225f), new Vector3(8f, 2f, 225f), 8f, 1f, stone, "Bridge_NN");
            AddBridge(new Vector3(-8f, 2f, 90f), new Vector3(8f, 2f, 90f), 8f, 1f, stone, "Bridge_N");
            AddBridge(new Vector3(-8f, 2f, -45f), new Vector3(8f, 2f, -45f), 8f, 1f, stone, "Bridge_Center");
            AddBridge(new Vector3(-8f, 2f, -150f), new Vector3(8f, 2f, -150f), 8f, 1f, stone, "Bridge_S");
            AddBridge(new Vector3(-8f, 2f, -285f), new Vector3(8f, 2f, -285f), 8f, 1f, stone, "Bridge_SS");

            // ── Riverside bushes (both banks; x/z scaled 1.5x) ─────
            HighlandsPrefabHelper.PlaceBush(transform, "bush01", new Vector3(-18f, 1f, 195f), 0f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush03", new Vector3(-22f, 1f, 120f), 45f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush05", new Vector3(-16f, 1f, 30f), 120f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush02", new Vector3(-27f, 1f, -60f), 200f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush04", new Vector3(-19f, 1f, -135f), 70f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush06", new Vector3(-24f, 1f, -210f), 310f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush02", new Vector3(18f, 1f, 180f), 15f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush04", new Vector3(21f, 1f, 75f), 160f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush06", new Vector3(25f, 1f, -15f), 90f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush01", new Vector3(16f, 1f, -105f), 250f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush03", new Vector3(28f, 1f, -180f), 30f);
            HighlandsPrefabHelper.PlaceBush(transform, "bush05", new Vector3(19f, 1f, -217f), 180f);

            // ── Riverside trees (x/z scaled 1.5x) ──────────────────
            HighlandsPrefabHelper.PlaceTree(transform, "tree01", new Vector3(-30f, 1f, 150f), 0f);
            HighlandsPrefabHelper.PlaceTree(transform, "tree02", new Vector3(27f, 1f, 60f), 90f);
            HighlandsPrefabHelper.PlaceTree(transform, "tree03", new Vector3(-25f, 1f, -75f), 180f);
            HighlandsPrefabHelper.PlaceTree(transform, "tree04", new Vector3(22f, 1f, -165f), 270f);
            HighlandsPrefabHelper.PlaceTree(transform, "tree01", new Vector3(30f, 1f, 210f), 45f);

            // ── River stones along banks (x/z scaled 1.5x) ─────────
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 1", new Vector3(-13f, 1f, 165f), 30f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 2", new Vector3(13f, 1f, 105f), 150f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 3", new Vector3(-15f, 1f, 15f), 80f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 4", new Vector3(15f, 1f, -45f), 220f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 5", new Vector3(-12f, 1f, -120f), 300f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 1", new Vector3(12f, 1f, -195f), 60f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 3", new Vector3(-16f, 1f, -210f), 170f);
            HighlandsPrefabHelper.PlaceRock(transform, "Tiny Rock 5", new Vector3(16f, 1f, 217f), 110f);
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
                if (x > -8f && x < 8f) return true;                              // river (centred, fixed width)
                if (x > -195f && x < -105f && z > -75f && z < 45f) return true; // farm (1.5x)
                return false;
            }

            // Helper: generate a valid valley-floor position, rejection-sampling exclusions
            // why: range ±480 fills the expanded 1125 arena (1.5x)
            Vector3 ValleyPos()
            {
                float x, z;
                do
                {
                    x = Random.Range(-480f, 480f);
                    z = Random.Range(-480f, 480f);
                } while (IsExcluded(x, z));
                return new Vector3(x, GroundY(x, z), z);   // snap to terrain so props don't float/sink
            }

            // ── Scattered trees (124-146): mix of all 7 variants ────────
            // Enforce a minimum spacing (shared with the spruce groves below) so trees never
            // spawn right on top of each other, and rely on ValleyPos's terrain-snapped Y so
            // none float or sink. Clustering removed — it produced overlapping trunks.
            var treePts = new System.Collections.Generic.List<Vector2>();
            const float minTreeDist = 7f;
            System.Func<float, float, bool> treeSpotOk = (tx, tz) =>
            {
                var q = new Vector2(tx, tz);
                for (int i = 0; i < treePts.Count; i++)
                    if ((treePts[i] - q).sqrMagnitude < minTreeDist * minTreeDist) return false;
                return true;
            };

            int treeCount = Random.Range(124, 147);
            int placed = 0, attempts = 0;
            while (placed < treeCount && attempts < treeCount * 15)
            {
                attempts++;
                Vector3 pos = ValleyPos();   // already terrain-snapped Y
                if (IsExcluded(pos.x, pos.z) || !treeSpotOk(pos.x, pos.z)) continue;

                string name = treeVariants[Random.Range(0, treeVariants.Length)];
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.0f, 2.0f);
                HighlandsPrefabHelper.PlaceTree(transform, name, pos, rot, scl);
                treePts.Add(new Vector2(pos.x, pos.z));
                placed++;
            }

            // ── Spruce groves at foothills (9-13 clusters of 5-8; ranges 1.5x) ──
            int groveCount = Random.Range(9, 14);
            float[] groveSigns = { -1f, 1f };
            for (int g = 0; g < groveCount; g++)
            {
                float gx = groveSigns[g % 2] * Random.Range(270f, 420f);
                float gz = Random.Range(-390f, 390f);
                int spruceCount = Random.Range(5, 9);

                for (int s = 0; s < spruceCount; s++)
                {
                    float sx = gx + Random.Range(-10f, 10f);
                    float sz = gz + Random.Range(-10f, 10f);
                    if (IsExcluded(sx, sz) || !treeSpotOk(sx, sz)) continue;

                    string spruce = (Random.value < 0.5f) ? "Spruce 1" : "Spruce 2";
                    float rot = Random.Range(0f, 360f);
                    float scl = Random.Range(1.5f, 2.5f);
                    HighlandsPrefabHelper.PlaceTree(transform, spruce,
                        new Vector3(sx, GroundY(sx, sz), sz), rot, scl);
                    treePts.Add(new Vector2(sx, sz));
                }
            }

            // ── Bushes (157-191): dense coverage with all 7 variants ────
            int bushCount = Random.Range(157, 192);
            for (int i = 0; i < bushCount; i++)
            {
                Vector3 pos = ValleyPos();
                string name = bushVariants[Random.Range(0, bushVariants.Length)];
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(0.8f, 1.5f);
                HighlandsPrefabHelper.PlaceBush(transform, name, pos, rot, scl);
            }

            // ── Grass patches (101-124): scattered everywhere ───────────
            int grassCount = Random.Range(101, 125);
            for (int i = 0; i < grassCount; i++)
            {
                Vector3 pos = ValleyPos();
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.5f, 3.0f);
                HighlandsPrefabHelper.PlaceFoliage(transform, "Grass", pos, rot, scl);
            }

            // ── Flowers (63-81): scattered among grass ──────────────────
            int flowerCount = Random.Range(63, 82);
            for (int i = 0; i < flowerCount; i++)
            {
                Vector3 pos = ValleyPos();
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.0f, 2.0f);
                HighlandsPrefabHelper.PlaceFoliage(transform, "Flower", pos, rot, scl);
            }

            // ── Stumps and logs (36-49): near tree clusters ──────────────
            int debrisCount = Random.Range(36, 50);
            for (int i = 0; i < debrisCount; i++)
            {
                Vector3 pos = ValleyPos();
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.0f, 1.5f);
                string debris = (Random.value < 0.5f) ? "Stump" : "Log";
                HighlandsPrefabHelper.PlaceFoliage(transform, debris, pos, rot, scl);
            }

            // ── NEW PASS 1: Mushroom patches (40-54) for forest-floor detail ──
            int mushroomCount = Random.Range(40, 55);
            for (int i = 0; i < mushroomCount; i++)
            {
                Vector3 pos = ValleyPos();
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.0f, 2.0f);
                HighlandsPrefabHelper.PlaceFoliage(transform, "Mushrooms Patch", pos, rot, scl);
            }

            // ── NEW PASS 2: Scattered branches (45-58) breakable ground litter ──
            int branchCount = Random.Range(45, 59);
            for (int i = 0; i < branchCount; i++)
            {
                Vector3 pos = ValleyPos();
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.0f, 1.8f);
                HighlandsPrefabHelper.PlaceFoliage(transform, "Branch", pos, rot, scl);
            }

            // ── NEW PASS 3: Outer-steppe bush thickets (54-68) using "Bush" ──
            int thicketCount = Random.Range(54, 69);
            for (int i = 0; i < thicketCount; i++)
            {
                Vector3 pos = ValleyPos();
                string name = bushVariants[Random.Range(0, bushVariants.Length)];
                float rot = Random.Range(0f, 360f);
                float scl = Random.Range(1.0f, 2.0f);
                HighlandsPrefabHelper.PlaceBush(transform, name, pos, rot, scl);
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS 6: grass + flower carpet across the newly-exposed
            // outer steppe band (±240..±470) so the enlarged floor reads as
            // dense pasture rather than empty terrain. Reuses ValleyPos for the
            // river/farm exclusions, then keeps only outer-band hits.
            // ══════════════════════════════════════════════════════
            {
                int fPlaced = 0, fAttempts = 0;
                while (fPlaced < 110 && fAttempts < 1100)
                {
                    fAttempts++;
                    Vector3 pos = ValleyPos();
                    float d = Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z);
                    if (d < 240f) continue;   // inner band already dense from passes above
                    if (d > 470f) continue;
                    float rot = Random.Range(0f, 360f);
                    if (Random.value < 0.65f)
                        HighlandsPrefabHelper.PlaceFoliage(transform, "Grass", pos, rot,
                            Random.Range(1.5f, 3.0f));
                    else
                        HighlandsPrefabHelper.PlaceFoliage(transform, "Flower", pos, rot,
                            Random.Range(1.0f, 2.0f));
                    fPlaced++;
                }
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

            // Spin the sail cross like a working windmill (blades lie in the XY plane,
            // so rotate the hub about its local Z / facing axis).
            var spin = bladesParent.AddComponent<Rotator>();
            spin.axis = Vector3.forward;
            spin.degreesPerSecond = 18f;
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

        // World-space ground height at (x,z) on the active terrain, for snapping props.
        private static float GroundY(float x, float z)
        {
            var t = Terrain.activeTerrain;
            if (t == null) return 0f;
            return t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
        }

        private void CreateYurt(Vector3 pos, float radius, float wallHeight, float roofHeight, string label)
        {
            // Dome resting ON the ground: sphere centre at terrain height so the top hemisphere
            // is the visible roof (bottom half hidden under terrain), instead of the whole sphere
            // being sunk a full radius underground.
            Color col = _hemiColors[Mathf.Abs(label.GetHashCode()) % _hemiColors.Length];
            float groundY = GroundY(pos.x, pos.z);
            AddSphere(new Vector3(pos.x, groundY, pos.z), radius, col, label);

            // Flag planted at the dome apex.
            SpawnFlag(new Vector3(pos.x, groundY + radius * 0.85f, pos.z), 3f, label + "_Flag");
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

            // Banner: a thin vertical cloth panel hanging off the upper pole. (The old
            // Models/Flag prefab rendered as a flat HORIZONTAL disc; a thin box gives a proper
            // double-sided vertical banner that reads as a flag.)
            var flagObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flagObj.name = label;
            flagObj.transform.SetParent(transform, false);
            float fw = flagScale * 1.6f;   // banner length out from the pole
            float fh = flagScale * 1.0f;   // banner drop
            flagObj.transform.localPosition = pos
                + Vector3.up * (poleHeight - fh * 0.55f)
                + Vector3.right * (fw * 0.5f);
            flagObj.transform.localScale = new Vector3(fw, fh, 0.06f);
            var flagMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var flagTex = GetKyrgyzFlag();
            flagMat.mainTexture = flagTex;
            if (flagMat.HasProperty("_BaseMap")) flagMat.SetTexture("_BaseMap", flagTex);
            SetMaterial(flagObj, flagMat);
            Object.DestroyImmediate(flagObj.GetComponent<Collider>());
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
            // why: radii ×1.5 (200-320 → 300-480) and counts ×2.25 so the
            //      expanded outer steppe has cover scaled to the 1.5x arena
            string[] cliffs = { "Rock Cliff 1", "Rock Cliff 2", "Rock Cliff 3",
                                "Rock Cliff 4", "Rock Cliff 5" };
            string[] stdRocks = { "Standard Rock 1", "Standard Rock 2", "Standard Rock 3",
                                  "Standard Rock 4", "Standard Rock 5" };
            string[] iceRocks = { "IceRock_01", "IceRock_02", "IceRock_03" };
            string[] tinyRocks = { "Tiny Rock 1", "Tiny Rock 2", "Tiny Rock 3",
                                   "Tiny Rock 4", "Tiny Rock 5" };

            // 18 outer cliff clusters (8→18 ≈ ×2.25; radii ×1.5: 315-435)
            for (int c = 0; c < 18; c++)
            {
                float t = (c / 18f) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                float r = Random.Range(315f, 435f);
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

            // 40 outer standard boulders scattered (18→40 ≈ ×2.25; radii ×1.5: 300-480)
            for (int i = 0; i < 40; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(300f, 480f);
                HighlandsPrefabHelper.PlaceRock(transform,
                    stdRocks[Random.Range(0, stdRocks.Length)],
                    new Vector3(Mathf.Cos(t) * r, 2f, Mathf.Sin(t) * r),
                    Random.Range(0f, 360f), Random.Range(1.8f, 3.0f));
            }

            // 23 outer ice rocks near mountain wall (10→23 ≈ ×2.25; radii ×1.5: 405-480)
            for (int i = 0; i < 23; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(405f, 480f);
                HighlandsPrefabHelper.PlaceRock(transform,
                    iceRocks[Random.Range(0, iceRocks.Length)],
                    new Vector3(Mathf.Cos(t) * r, 2f, Mathf.Sin(t) * r),
                    Random.Range(0f, 360f), Random.Range(2.0f, 3.2f));
            }

            // 68 outer tiny rocks for detail (30→68 ≈ ×2.25; radii ×1.5: 285-480)
            for (int i = 0; i < 68; i++)
            {
                float t = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(285f, 480f);
                HighlandsPrefabHelper.PlaceRock(transform,
                    tinyRocks[Random.Range(0, tinyRocks.Length)],
                    new Vector3(Mathf.Cos(t) * r + Random.Range(-6f, 6f), 2f,
                                Mathf.Sin(t) * r + Random.Range(-6f, 6f)),
                    Random.Range(0f, 360f), Random.Range(1.0f, 2.0f));
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS 4: mid-band boulder + cliff carpet filling the gap
            // between the inner formations (±180) and the outer ring (≥285) so
            // the enlarged steppe reads as dense cover rather than empty floor.
            // PlaceRock on rock/cliff names stays static (landmark cover).
            // ══════════════════════════════════════════════════════
            {
                int rkPlaced = 0, rkAttempts = 0;
                while (rkPlaced < 60 && rkAttempts < 600)
                {
                    rkAttempts++;
                    float rx = Random.Range(-470f, 470f);
                    float rz = Random.Range(-470f, 470f);
                    float d = Mathf.Sqrt(rx * rx + rz * rz);
                    if (d < 170f) continue;   // inner formations + settlement already dense
                    if (d > 470f) continue;   // stay inside perimeter peaks
                    if (rx > -8f && rx < 8f) continue;   // keep river channel clear
                    string name = (Random.value < 0.45f)
                        ? cliffs[Random.Range(0, cliffs.Length)]
                        : stdRocks[Random.Range(0, stdRocks.Length)];
                    HighlandsPrefabHelper.PlaceRock(transform, name,
                        new Vector3(rx, 2f, rz), Random.Range(0f, 360f),
                        Random.Range(1.8f, 3.2f));
                    rkPlaced++;
                }
            }

            // ══════════════════════════════════════════════════════
            // DENSITY PASS 5: tiny-rock gravel scatter across the whole expanded
            // floor for fine ground detail (small scale, breakable-feel clutter).
            // ══════════════════════════════════════════════════════
            {
                int tnPlaced = 0, tnAttempts = 0;
                while (tnPlaced < 90 && tnAttempts < 900)
                {
                    tnAttempts++;
                    float tx = Random.Range(-475f, 475f);
                    float tz = Random.Range(-475f, 475f);
                    float d = Mathf.Sqrt(tx * tx + tz * tz);
                    if (d < 60f) continue;    // keep central flag area clear
                    if (d > 475f) continue;
                    if (tx > -8f && tx < 8f) continue;   // keep river channel clear
                    HighlandsPrefabHelper.PlaceRock(transform,
                        tinyRocks[Random.Range(0, tinyRocks.Length)],
                        new Vector3(tx, 2f, tz), Random.Range(0f, 360f),
                        Random.Range(1.0f, 2.0f));
                    tnPlaced++;
                }
            }
        }
    }
}
