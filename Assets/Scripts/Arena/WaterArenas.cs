using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Combat;
using CloseEncounters.VehiclePhysics;

namespace CloseEncounters.Arena
{
    // =========================================================================
    // 1. WaterArchipelago -- pre-built island terrains with palms
    // =========================================================================

    public class WaterArchipelago : ArenaBase
    {
        public override string ArenaName => "Tropical Archipelago";

        public override void Build()
        {
            AddWaterSurface(750f);

            Color coral    = new Color(0.95f, 0.55f, 0.45f);
            Color coralPink = new Color(0.92f, 0.62f, 0.68f);
            Color sand     = new Color(0.92f, 0.85f, 0.65f);
            Color wetRock  = new Color(0.45f, 0.48f, 0.42f);

            // why: warm golden tropical fog tint
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.88f, 0.78f, 0.55f);
            RenderSettings.fogDensity = 0.0025f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.65f, 0.62f, 0.52f);

            var waterSurface = transform.Find("WaterSurface");
            if (waterSurface != null)
            {
                var waterRend = waterSurface.GetComponent<MeshRenderer>();
                if (waterRend != null && waterRend.material != null)
                    waterRend.material.SetColor("_BaseColor", new Color(0.18f, 0.65f, 0.75f, 0.55f));
            }

            // 10 islands spread across the expanded arena (was 6)
            Vector3[] islandPos =
            {
                new Vector3(-280f, 0f, 210f),
                new Vector3( 245f, 0f, 280f),
                new Vector3(-105f, 0f, -245f),
                new Vector3( 350f, 0f, -140f),
                new Vector3(-385f, 0f,  -70f),
                new Vector3(   0f, 0f,  105f),
                new Vector3( 140f, 0f, -350f),
                new Vector3(-200f, 0f,  400f),
                new Vector3( 420f, 0f,  120f),
                new Vector3(-450f, 0f, -320f)
            };
            Vector3[] islandSizes =
            {
                new Vector3(120f, 30f, 120f),
                new Vector3( 80f, 20f,  80f),
                new Vector3(100f, 25f, 100f),
                new Vector3( 60f, 15f,  60f),
                new Vector3(160f, 40f, 160f),
                new Vector3(140f, 35f, 140f),
                new Vector3( 70f, 18f,  70f),
                new Vector3( 90f, 22f,  90f),
                new Vector3( 55f, 14f,  55f),
                new Vector3(110f, 28f, 110f)
            };
            int[] islandRes = { 257, 129, 257, 129, 257, 257, 129, 129, 129, 257 };
            float[] islandRadius = { 20f, 15f, 18f, 12f, 22f, 25f, 13f, 16f, 11f, 19f };

            for (int i = 0; i < islandPos.Length; i++)
            {
                TerrainFactory.GenerateIsland(
                    transform,
                    islandPos[i],
                    islandSizes[i],
                    islandRes[i],
                    $"archipelago_{i}",
                    50,
                    0.02f,
                    20f,
                    $"Island_{i}");
            }

            // Palm trees on each island
            for (int i = 0; i < islandPos.Length; i++)
            {
                int palmCount = Mathf.CeilToInt(islandRadius[i] / 4f);
                for (int p = 0; p < palmCount; p++)
                {
                    float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    float dist = Random.Range(1f, islandRadius[i] * 0.6f);
                    Vector3 palmPos = islandPos[i] + new Vector3(
                        Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                    AddTree(palmPos, Random.Range(6f, 10f), Random.Range(2.5f, 4f), $"Palm_{i}_{p}");
                }
            }

            // Coral rubble + rock clusters on the shallows around each island
            for (int i = 0; i < islandPos.Length; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    float d = islandRadius[i] + Random.Range(8f, 22f);
                    Vector3 p = islandPos[i] + new Vector3(Mathf.Cos(a) * d, -0.5f, Mathf.Sin(a) * d);
                    Color col = (c % 2 == 0) ? coral : coralPink;
                    AddRockCluster(p, 4f, 1.8f, col, $"Coral_{i}_{c}");
                }
            }

            // Extra open-water rock clusters + buoys between islands
            Vector3[] rubblePos =
            {
                new Vector3(-520f, -0.5f, 40f), new Vector3(520f, -0.5f, -40f),
                new Vector3(80f, -0.5f, 520f),  new Vector3(-80f, -0.5f, -520f),
                new Vector3(310f, -0.5f, 320f), new Vector3(-310f, -0.5f, -310f),
                new Vector3(60f, -0.5f, -180f), new Vector3(-60f, -0.5f, 300f)
            };
            for (int i = 0; i < rubblePos.Length; i++)
                AddRockCluster(rubblePos[i], 5f, 2.2f, wetRock, $"ReefRubble_{i}");

            // Lighthouses / beacons on the outer island ring
            AddCylinder(new Vector3(-280f, 8f, 210f), 2.5f, 18f, new Color(0.95f, 0.95f, 0.9f), "Lighthouse_NW");
            AddCylinder(new Vector3(-280f, 17f, 210f), 1.2f, 2f, new Color(0.9f, 0.3f, 0.25f), "LighthouseLamp_NW");
            AddCylinder(new Vector3(420f, 6f, 120f), 2f, 14f, new Color(0.95f, 0.95f, 0.9f), "Beacon_E");
            AddCylinder(new Vector3(420f, 13f, 120f), 1f, 1.5f, new Color(0.9f, 0.3f, 0.25f), "BeaconLamp_E");

            // Sand bar / shallow platforms (flat blocks just below water)
            AddBlockUnchecked(new Vector3(180f, -0.2f, 180f), new Vector3(30f, 0.5f, 12f), sand, "SandBar_NE");
            AddBlockUnchecked(new Vector3(-200f, -0.2f, -140f), new Vector3(12f, 0.5f, 28f), sand, "SandBar_SW");
            AddBlockUnchecked(new Vector3(0f, -0.2f, -400f), new Vector3(24f, 0.5f, 10f), sand, "SandBar_S");

            // Spawn ring on open water -- large radius, well-separated
            AddSpawnRing(Vector3.zero, 550f, 8, 1f);
            AddInvisibleWalls(750f, 50f);

            // Ambient VFX
            VFXManager.Fireflies(new Vector3(0, 3, 0), 5f);
            VFXManager.DustMotes(new Vector3(0, 5, 0), 3f);
        }

        private float SampleTerrainHeight(Terrain terrain, float worldX, float worldZ)
        {
            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            float nx = (worldX - terrainPos.x) / terrainSize.x;
            float nz = (worldZ - terrainPos.z) / terrainSize.z;
            nx = Mathf.Clamp01(nx);
            nz = Mathf.Clamp01(nz);
            return terrain.terrainData.GetInterpolatedHeight(nx, nz) + terrainPos.y;
        }

    }

    // =========================================================================
    // 2. WaterTitansPeak -- central mountain with lava glow summit
    // =========================================================================

    public class WaterTitansPeak : ArenaBase
    {
        public override string ArenaName => "Titan's Peak";

        public override void Build()
        {
            Color deepRock   = new Color(0.30f, 0.28f, 0.25f);
            Color midRock    = new Color(0.42f, 0.38f, 0.33f);
            Color highRock   = new Color(0.50f, 0.45f, 0.40f);
            Color blackSand  = new Color(0.12f, 0.10f, 0.10f);
            Color ash        = new Color(0.35f, 0.32f, 0.30f);
            AddWaterSurface(750f);

            // why: ash-gray fog to sell the volcanic atmosphere
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.55f, 0.50f, 0.48f);
            RenderSettings.fogDensity = 0.0035f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.55f, 0.50f, 0.48f);

            // Central mountain terrain
            var peakTerrain = TerrainFactory.GenerateIsland(transform, new Vector3(-150f, -15f, -150f), new Vector3(300f, 120f, 300f), 257, "titans_peak", 80, 0.03f, 15f, "TitansPeak");
            // The island generator alone often leaves Titan's Peak nearly flat; force a prominent central mountain.
            if (peakTerrain != null)
            {
                TerrainFactory.AddHill(peakTerrain, 0.5f, 0.5f, 0.35f, 0.65f);
            }

            // Rock clusters around the base outcrops (pushed outward for the expanded arena)
            float[] outAnglesWorld = { 15f, 50f, 90f, 130f, 170f, 210f, 250f, 290f, 330f };
            for (int i = 0; i < outAnglesWorld.Length; i++)
            {
                float rad = outAnglesWorld[i] * Mathf.Deg2Rad;
                float dist = Random.Range(190f, 240f);
                Vector3 pos = new Vector3(Mathf.Cos(rad) * dist, 0f, Mathf.Sin(rad) * dist);
                AddRockCluster(pos + new Vector3(5f, 0f, 0f), 6f, 3f, deepRock, $"OutcropRocks_{i}");
            }

            // Satellite volcanic islets around the central peak
            Vector3[] isletPos =
            {
                new Vector3(-420f, 0f,  250f),
                new Vector3( 430f, 0f, -220f),
                new Vector3(-300f, 0f, -400f),
                new Vector3( 340f, 0f,  380f),
                new Vector3(   0f, 0f,  480f),
                new Vector3(   0f, 0f, -480f)
            };
            float[] isletRad = { 35f, 30f, 40f, 28f, 32f, 34f };
            for (int i = 0; i < isletPos.Length; i++)
            {
                TerrainFactory.GenerateIsland(transform, isletPos[i] + new Vector3(0f, -6f, 0f),
                    new Vector3(isletRad[i] * 2f, 14f, isletRad[i] * 2f), 129,
                    $"titan_islet_{i}", 40, 0.02f, 12f, $"Islet_{i}");
                // black sand ringing each islet
                AddRockCluster(isletPos[i] + new Vector3(isletRad[i] * 0.6f, -0.3f, 0f), 5f, 1.5f, blackSand, $"BlackSand_{i}_A");
                AddRockCluster(isletPos[i] + new Vector3(-isletRad[i] * 0.6f, -0.3f, 0f), 5f, 1.5f, blackSand, $"BlackSand_{i}_B");
                AddRockCluster(isletPos[i] + new Vector3(0f, -0.3f, isletRad[i] * 0.6f), 5f, 1.5f, blackSand, $"BlackSand_{i}_C");
            }

            // Extra open-water ash/rock rubble
            Vector3[] rubblePos =
            {
                new Vector3( 200f, -0.5f,  450f), new Vector3(-450f, -0.5f,   50f),
                new Vector3( 500f, -0.5f,   80f), new Vector3( -80f, -0.5f, -300f),
                new Vector3( 260f, -0.5f, -340f), new Vector3(-180f, -0.5f,  340f)
            };
            for (int i = 0; i < rubblePos.Length; i++)
                AddRockCluster(rubblePos[i], 6f, 2.5f, ash, $"AshRubble_{i}");

            // Black sand beach blocks around the central peak
            AddBlockUnchecked(new Vector3(180f, -0.2f, 0f), new Vector3(20f, 0.4f, 50f), blackSand, "BlackBeach_E");
            AddBlockUnchecked(new Vector3(-180f, -0.2f, 0f), new Vector3(20f, 0.4f, 50f), blackSand, "BlackBeach_W");
            AddBlockUnchecked(new Vector3(0f, -0.2f, 180f), new Vector3(50f, 0.4f, 20f), blackSand, "BlackBeach_N");
            AddBlockUnchecked(new Vector3(0f, -0.2f, -180f), new Vector3(50f, 0.4f, 20f), blackSand, "BlackBeach_S");

            // Volcano: raised well above the expanded terrain peak (~y=50) and
            // driven by a VolcanoEruption component so it erupts periodically
            // with lava debris instead of a static smoke sphere.
            var volcanoPrefab = Resources.Load<GameObject>("Arena/MagmaMountain_01");
            if (volcanoPrefab != null)
            {
                var volcano = Object.Instantiate(volcanoPrefab, transform);
                volcano.name = "VolcanoPeak";
                volcano.transform.localPosition = new Vector3(0f, 60f, 0f);
                volcano.transform.localScale = Vector3.one * 18f;
                volcano.isStatic = true;

                foreach (var c in volcano.GetComponentsInChildren<Collider>())
                    Object.DestroyImmediate(c);

                var erupt = volcano.AddComponent<VolcanoEruption>();
                erupt.craterLocalOffset = new Vector3(0f, 3.5f, 0f); // why: lands just above the mesh cone tip
                erupt.craterRadius = 3.5f;
                erupt.lavaGlowRadius = 5f;
                erupt.eruptionInterval = 9f;
                erupt.debrisPerEruption = 10;

                // Lava streams trickling down the volcano's sides
                SpawnLavaStream(volcano.transform, new Vector3(0f, 3.5f, 0f), new Vector3(12f, -18f, 4f));
                SpawnLavaStream(volcano.transform, new Vector3(0f, 3.5f, 0f), new Vector3(-10f, -20f, 8f));
                SpawnLavaStream(volcano.transform, new Vector3(0f, 3.5f, 0f), new Vector3(-4f, -19f, -13f));
                SpawnLavaStream(volcano.transform, new Vector3(0f, 3.5f, 0f), new Vector3(14f, -21f, -6f));
            }
            else
            {
                Debug.LogWarning("[WaterTitansPeak] MagmaMountain_01 prefab missing — volcano not spawned.");
            }

            // Paths/ledges on mountain (flat blocks)
            AddBlockUnchecked(new Vector3(-22.5f, 9f, 82.5f), new Vector3(30f, 1f, 6f), midRock, "Ledge_N");
            AddBlockUnchecked(new Vector3(75f, 20f, -15f), new Vector3(6f, 1f, 25f), highRock, "Ledge_E");
            AddBlockUnchecked(new Vector3(-60f, 14f, -45f), new Vector3(6f, 1f, 20f), midRock, "Ledge_SW");
            AddBlockUnchecked(new Vector3(40f, 28f, 60f), new Vector3(18f, 1f, 6f), highRock, "Ledge_NE");
            AddBlockUnchecked(new Vector3(-70f, 35f, 20f), new Vector3(6f, 1f, 18f), highRock, "Ledge_High");

            AddSpawnRing(Vector3.zero, 550f, 8, 1f);
            AddInvisibleWalls(750f, 50f);

            // Ambient VFX
            VFXManager.HeatDistortion(new Vector3(0, 40, 0), 3f);
            VFXManager.HeatDistortion(new Vector3(120f, 8f, 120f), 2f);
            VFXManager.HeatDistortion(new Vector3(-140f, 8f, 140f), 2f);
            VFXManager.GroundFog(Vector3.zero, 6f);
        }
    }

    // =========================================================================
    // 3. WaterFrozenStrait -- floating pushable icebergs with buoyancy
    // =========================================================================

    public class WaterFrozenStrait : ArenaBase
    {
        public override string ArenaName => "Arctic";

        public override void Build()
        {
            AddWaterSurface(750f);

            // --- Arctic water color override ---
            // The base AddWaterSurface uses tropical blue; recolor to dark icy green
            var waterSurface = transform.Find("WaterSurface");
            if (waterSurface != null)
            {
                var renderer = waterSurface.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                {
                    renderer.sharedMaterial.SetColor("_BaseColor",
                        new Color(0.08f, 0.2f, 0.35f, 0.55f));
                    renderer.sharedMaterial.SetFloat("_Smoothness", 0.85f);
                }

                var animator = waterSurface.GetComponent<WaterBasicAnimator>();
                if (animator != null)
                {
                    animator.horizonColor = new Color(0.12f, 0.22f, 0.30f, 1f);
                    animator.waterColor   = new Color(0.08f, 0.18f, 0.28f, 1f);
                }
            }

            // --- Frozen atmosphere render settings ---
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.55f, 0.68f, 0.82f);
            RenderSettings.fogDensity = 0.0045f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.45f, 0.55f, 0.70f);

            // --- North and south ice shelves with snow/rock splatmap ---
            // Pushed outward with the expanded 750 half-extent. Shelves sit
            // beyond the 550 spawn ring so spawn points remain on open water.
            //   North shelf: x=-300..300, z=600..740
            //   South shelf: x=-300..300, z=-740..-600
            var northShelf = TerrainFactory.GenerateIsland(transform, new Vector3(-300f, -15f, 600f), new Vector3(600f, 18f, 140f), 129, "frozen_north", 50, 0.01f, 20f, "NorthIceShelf");
            var southShelf = TerrainFactory.GenerateIsland(transform, new Vector3(-300f, -15f, -740f), new Vector3(600f, 18f, 140f), 129, "frozen_south", 50, 0.01f, 20f, "SouthIceShelf");

            // Paint ice shelves with snow and rock layers
            PaintIceShelfSplatmap(northShelf);
            PaintIceShelfSplatmap(southShelf);

            // Floating Ice Mountains from Low Poly Environment asset pack.
            // All positions sit inside the open-water channel (|z| < 210) so
            // they don't overlap the ice shelves (z >= 220 / z <= -220).
            // Every mountain is well inside the spawn ring (r=200) and at
            // least 45 units from every other mountain, giving vehicles clear
            // navigation lanes.
            // 24 icebergs spread wide across the channel. Some start partially
            // submerged (negative Y) so only their peaks poke above water.
            Vector3[] bergPositions =
            {
                new Vector3(-220f, 0f,  150f),   //  0
                new Vector3( 200f, 0f, -120f),   //  1
                new Vector3( -80f, 0f, -260f),   //  2
                new Vector3( 300f, 0f,  220f),   //  3
                new Vector3(-320f, 0f, -170f),   //  4
                new Vector3( 260f, 0f, -260f),   //  5
                new Vector3(-150f, 0f,  300f),   //  6
                new Vector3( 340f, 0f,   60f),   //  7
                new Vector3(   0f, 0f,    0f),   //  8 -- dead centre
                new Vector3(-300f, 0f,  260f),   //  9
                new Vector3( 110f, 0f,  190f),   // 10
                new Vector3( -60f, 0f, -100f),   // 11
                new Vector3( 190f,-2f,  110f),   // 12 -- partially submerged
                new Vector3(-190f,-3f,  -40f),   // 13 -- partially submerged
                new Vector3(  80f,-2f, -190f),   // 14 -- partially submerged
                new Vector3(-260f,-1f,   80f),   // 15 -- partially submerged
                new Vector3( 440f, 0f,  340f),   // 16
                new Vector3(-440f, 0f, -320f),   // 17
                new Vector3( 480f,-2f, -120f),   // 18 -- partially submerged
                new Vector3(-460f,-1f,  200f),   // 19 -- partially submerged
                new Vector3( 380f, 0f, -420f),   // 20
                new Vector3(-360f, 0f,  440f),   // 21
                new Vector3(  40f,-3f,  380f),   // 22 -- partially submerged
                new Vector3(  60f,-2f, -380f),   // 23 -- partially submerged
            };

            float[] bergScales = { 6f, 5f, 7f, 5f, 8f, 4f, 6f, 4f, 7f, 5f, 4f, 5f, 6f, 7f, 5f, 6f, 7f, 8f, 5f, 6f, 7f, 6f, 5f, 5f };
            int[] bergHP =       { 400,250,500,300,600,200,350,200,450,300,200,250,350,450,250,350,500,600,250,350,500,400,250,250 };

            for (int i = 0; i < bergPositions.Length; i++)
            {
                CreateIceMountain(bergPositions[i], bergScales[i], bergHP[i], $"IceMountain_{i}");
            }

            // Floating small ice floes (non-rigid eye candy, flat cylinders)
            Color floeColor = new Color(0.85f, 0.92f, 0.98f);
            Vector3[] floePos =
            {
                new Vector3(-120f, -0.2f,  40f), new Vector3(  90f, -0.2f,  -30f),
                new Vector3( 210f, -0.2f,  180f), new Vector3(-250f, -0.2f,  210f),
                new Vector3( 340f, -0.2f, -240f), new Vector3(-390f, -0.2f, -260f),
                new Vector3(  30f, -0.2f,  460f), new Vector3( -40f, -0.2f, -450f),
                new Vector3( 520f, -0.2f,   50f), new Vector3(-540f, -0.2f,  -20f),
                new Vector3( 150f, -0.2f, -480f), new Vector3(-160f, -0.2f,  490f)
            };
            for (int i = 0; i < floePos.Length; i++)
            {
                float r = Random.Range(3.5f, 7f);
                AddCylinder(floePos[i], r, 0.6f, floeColor, $"IceFloe_{i}");
            }

            // Spawn ring on open water between the ice shelves.
            // Expanded to radius 550 to match the new 750 half-extent arena.
            // Shelves pushed out to z = +-600, leaving 50+ unit clearance at
            // the worst-case spawn points (90/270 deg, z = +-550).
            AddSpawnRing(Vector3.zero, 550f, 8, 1f);
            AddInvisibleWalls(750f, 50f);

            // --- Ambient VFX ---
            VFXManager.GroundFog(Vector3.zero, 14f);
            VFXManager.Rain(new Vector3(0, 25, 0), 10f);
            VFXManager.DustMotes(new Vector3(0, 8, 0), 8f);   // doubles as snow particles

            // Steam wisps along the waterline for cold atmosphere
            var steamPrefab = Resources.Load<GameObject>("VFX/Smoke/Steam");
            if (steamPrefab != null)
            {
                // Along the waterline at the foot of each ice shelf (z = +-590)
                Vector3[] steamPositions =
                {
                    new Vector3(-200f, 0.5f,  590f),
                    new Vector3( -60f, 0.5f,  590f),
                    new Vector3(  60f, 0.5f,  590f),
                    new Vector3( 200f, 0.5f,  590f),
                    new Vector3(-200f, 0.5f, -590f),
                    new Vector3( -60f, 0.5f, -590f),
                    new Vector3(  60f, 0.5f, -590f),
                    new Vector3( 200f, 0.5f, -590f)
                };
                for (int i = 0; i < steamPositions.Length; i++)
                {
                    var steam = Object.Instantiate(steamPrefab, transform);
                    steam.name = $"WaterlineSteam_{i}";
                    steam.transform.localPosition = steamPositions[i];
                    steam.transform.localScale = Vector3.one * 3f;
                    foreach (var ps in steam.GetComponentsInChildren<ParticleSystem>())
                    {
                        var main = ps.main;
                        main.loop = true;
                    }
                }
            }

            // --- Cold directional light with aurora tint ---
            var dirLight = new GameObject("FrozenStraitLight");
            dirLight.transform.SetParent(transform, false);
            dirLight.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            var light = dirLight.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.60f, 0.85f, 0.92f); // why: aurora-tinged sky
            light.intensity = 0.8f;
            light.shadows = LightShadows.Soft;
        }

        /// <summary>
        /// Paint an ice shelf terrain with snow on flat areas and rock on steep slopes.
        /// Uses TerrainFactory.Layer indices: Snow=6, Rock=7.
        /// </summary>
        private static void PaintIceShelfSplatmap(Terrain terrain)
        {
            if (terrain == null) return;

            int layerCount = terrain.terrainData.terrainLayers.Length;
            int snowIdx = (int)TerrainFactory.Layer.Snow;   // 6
            int rockIdx = (int)TerrainFactory.Layer.Rock;   // 7
            if (layerCount <= snowIdx || layerCount <= rockIdx) return;

            TerrainFactory.PaintSplatmap(terrain, (nx, nz, height, steepness) =>
            {
                float[] weights = new float[layerCount];
                // Steep areas get rock, flat areas get snow
                float rockWeight = Mathf.Clamp01((steepness - 20f) / 25f);
                weights[snowIdx] = 1f - rockWeight;
                weights[rockIdx] = rockWeight;
                return weights;
            });
        }

        private static readonly string[] IceMountainPrefabPaths =
        {
            "Arena/IceMountain_01",
            "Arena/IceMountain_02",
            "Arena/IceMountain_03"
        };

        private void CreateIceMountain(Vector3 pos, float scale, int hp, string label)
        {
            var berg = new GameObject(label);
            berg.transform.SetParent(transform, false);
            berg.transform.position = pos;
            berg.tag = "Iceberg";

            // Load a random Ice Mountain prefab from the Low Poly asset pack
            string prefabPath = IceMountainPrefabPaths[Random.Range(0, IceMountainPrefabPaths.Length)];
            var prefab = Resources.Load<GameObject>(prefabPath);

            Bounds visualBounds = new Bounds(Vector3.zero, Vector3.one * scale * 0.3f);

            if (prefab != null)
            {
                var model = Object.Instantiate(prefab, berg.transform);
                model.name = "IceMountainModel";
                model.transform.localPosition = Vector3.zero;
                model.transform.localScale = Vector3.one * (scale * 0.08f);
                model.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                // Remove prefab colliders — parent handles collision
                foreach (var c in model.GetComponentsInChildren<Collider>())
                    Object.DestroyImmediate(c);

                // Fix non-URP materials (Low Poly asset pack uses Standard shader → pink)
                var urpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpShader != null)
                {
                    foreach (var rend in model.GetComponentsInChildren<Renderer>())
                    {
                        var mats = rend.materials;
                        for (int m = 0; m < mats.Length; m++)
                        {
                            if (mats[m] != null && mats[m].shader != urpShader
                                && !mats[m].shader.name.Contains("Universal"))
                            {
                                Color matColor = mats[m].HasProperty("_Color")
                                    ? mats[m].color : Color.gray;
                                Texture tex = mats[m].HasProperty("_MainTex")
                                    ? mats[m].mainTexture : null;
                                mats[m] = new Material(urpShader);
                                mats[m].color = matColor;
                                if (tex != null) mats[m].mainTexture = tex;
                            }
                        }
                        rend.materials = mats;
                    }
                }

                // Compute collider from actual rendered mesh bounds
                var renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    visualBounds = renderers[0].bounds;
                    for (int b = 1; b < renderers.Length; b++)
                        visualBounds.Encapsulate(renderers[b].bounds);
                    // Convert world-space bounds to local space of berg
                    visualBounds.center -= berg.transform.position;
                }
            }
            else
            {
                Debug.LogWarning($"[FrozenStrait] Failed to load prefab '{prefabPath}' for {label}. Using fallback cube.");
                var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
                top.name = "IceTop";
                top.transform.SetParent(berg.transform, false);
                top.transform.localPosition = new Vector3(0f, scale * 0.1f, 0f);
                top.transform.localScale = new Vector3(scale * 0.3f, scale * 0.2f, scale * 0.25f);
                top.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                SetMaterial(top, MakeMaterial(new Color(0.75f, 0.88f, 0.95f)));
                Object.DestroyImmediate(top.GetComponent<Collider>());
                visualBounds = new Bounds(new Vector3(0f, scale * 0.1f, 0f),
                    new Vector3(scale * 0.3f, scale * 0.2f, scale * 0.25f));
            }

            // Collider sized to actual visual mesh bounds
            var col = berg.AddComponent<BoxCollider>();
            col.size = visualBounds.size * 0.9f;
            col.center = visualBounds.center;

            // Rigidbody — pushable, buoyant
            var rb = berg.AddComponent<Rigidbody>();
            rb.mass = scale * 8f;
            rb.linearDamping = 1.5f;
            rb.angularDamping = 2f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Iceberg component (HP, buoyancy, contact damage, shot knockback)
            var ib = berg.AddComponent<Iceberg>();
            ib.maxHP = hp;
            ib.currentHP = hp;
            ib.waterLevel = 0f;
            ib.buoyancyForce = scale * 30f;
            ib.submergedDepth = scale * 0.4f;
            ib.contactDamagePerSecond = 6;
        }
    }

    /// <summary>
    /// Iceberg component: tracks HP, applies buoyancy in Update, and can be shattered.
    /// Uses WaveManager for dynamic water height when available, falls back to static waterLevel.
    /// </summary>
    public class Iceberg : MonoBehaviour
    {
        public int maxHP;
        public int currentHP;
        public float waterLevel = 0f;
        public float buoyancyForce = 500f;
        public float submergedDepth = 5f;
        public int contactDamagePerSecond = 0;

        private Rigidbody _rb;
        private bool _isShattered;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Lazily initialize Rigidbody reference. Needed because TakeDamage can be
        /// called before Start() runs (e.g., same-frame spawn + area damage).
        /// </summary>
        private Rigidbody GetRb()
        {
            if (_rb == null)
                _rb = GetComponent<Rigidbody>();
            return _rb;
        }

        /// <summary>
        /// Get the effective water surface height at this iceberg's XZ position.
        /// Uses WaveManager when available for dynamic wave-displaced height,
        /// otherwise falls back to the static waterLevel field.
        /// </summary>
        private float GetEffectiveWaterLevel()
        {
            if (WaveManager.Instance != null)
                return WaveManager.Instance.GetWaterHeight(transform.position);
            return waterLevel;
        }

        private void Update()
        {
            if (_isShattered) return;

            var rb = GetRb();
            if (rb == null) return;

            // Buoyancy: push upward when below water level
            float effectiveWater = GetEffectiveWaterLevel();
            float depth = effectiveWater - transform.position.y;
            if (depth > 0f)
            {
                float submergedFraction = Mathf.Clamp01(depth / submergedDepth);
                Vector3 force = Vector3.up * buoyancyForce * submergedFraction;
                rb.AddForce(force, ForceMode.Force);

                // Damping in water
                rb.linearVelocity *= 0.995f;
            }
            else
            {
                // Gravity pulls it back if above water
                rb.AddForce(Vector3.down * 20f, ForceMode.Force);
            }

            // Clamp Y-axis angular velocity to prevent wild spinning from repeated hits
            Vector3 angVel = rb.angularVelocity;
            const float maxYSpin = 1.5f; // radians/sec
            if (Mathf.Abs(angVel.y) > maxYSpin)
            {
                angVel.y = Mathf.Sign(angVel.y) * maxYSpin;
                rb.angularVelocity = angVel;
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            // Guard: dead or shattered icebergs deal no damage
            if (_isShattered || currentHP <= 0) return;
            if (contactDamagePerSecond <= 0) return;

            // Null-safe collider access
            if (collision.collider == null) return;

            var vr = collision.collider.GetComponentInParent<VehicleRuntime>();
            if (vr == null || !vr.IsAlive) return;

            int dmg = Mathf.CeilToInt(contactDamagePerSecond * Time.deltaTime);
            if (dmg <= 0) return;

            Vector3 contactPoint = collision.contactCount > 0
                ? collision.GetContact(0).point : vr.transform.position;
            DamageSystem.DealDamageToVehicle(vr, dmg, contactPoint);
        }

        /// <summary>
        /// Deal damage to the iceberg. Applies knockback force. Returns true if destroyed.
        /// Safe to call before Start() and after shattering.
        /// </summary>
        public bool TakeDamage(int amount)
        {
            // Already shattered -- ignore further damage
            if (_isShattered) return false;

            currentHP -= amount;

            // Nudge the ice mountain when hit
            var rb = GetRb();
            if (rb != null)
            {
                Vector3 push = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
                rb.AddForce(push * amount * 0.5f, ForceMode.Impulse);
                rb.AddTorque(Vector3.up * amount * 0.1f, ForceMode.Impulse);
            }

            if (currentHP <= 0)
            {
                currentHP = 0;
                // Set flag BEFORE ShatterIceberg to prevent same-frame callbacks
                // from dealing damage or applying physics after destruction.
                _isShattered = true;
                DamageSystem.ShatterIceberg(gameObject);
                return true;
            }
            return false;
        }
    }

    // =========================================================================
    // 4. WaterDragonLair -- "Puff the Magic Dragon": twilight water arena
    //    with central island, dark mountains, and a dragon boss with full
    //    AI, animations, fire breath, and destructibility.
    // =========================================================================

    public class WaterKrakenLair : ArenaBase
    {
        public override string ArenaName => "Dragon Deez Nuts";

        public override void Build()
        {
            AddWaterSurface(900f);

            // ── Districts ──
            BuildAtmosphere();
            BuildMountainRing();
            BuildCentralIsland();
            BuildShipwrecks();
            SpawnDragon();

            // ── Spawn + bounds (expanded to give the dragon room to roam) ──
            AddSpawnRing(Vector3.zero, 600f, 8, 1f);
            AddInvisibleWalls(900f, 120f);
        }

        // ── ATMOSPHERE: twilight sky, dark fog, moody lighting ──
        private void BuildAtmosphere()
        {
            // Twilight sky
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.12f, 0.10f, 0.18f); // dark purple
            RenderSettings.fogDensity = 0.008f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.25f, 0.20f, 0.30f); // dim purple

            // Try to load twilight skybox
            var skyboxTex = Resources.Load<Texture2D>("Skybox_Twilight");
            if (skyboxTex != null)
            {
                // Create skybox material from HDR cubemap if possible
            }

            // Twilight directional light
            var lightObj = new GameObject("TwilightSun");
            lightObj.transform.SetParent(transform, false);
            var sun = lightObj.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.9f, 0.5f, 0.3f); // orange sunset
            sun.intensity = 0.6f;
            sun.transform.rotation = Quaternion.Euler(15f, -30f, 0f); // low angle sunset
            sun.shadows = LightShadows.Soft;

            // Darken the water surface
            var waterSurface = transform.Find("WaterSurface");
            if (waterSurface != null)
            {
                var waterRend = waterSurface.GetComponent<MeshRenderer>();
                if (waterRend != null && waterRend.material != null)
                    waterRend.material.SetColor("_BaseColor", new Color(0.05f, 0.08f, 0.15f, 0.6f));
            }

            // Ambient VFX
            VFXManager.GroundFog(Vector3.zero, 18f);
            VFXManager.Rain(new Vector3(0, 25, 0), 14f);
            VFXManager.Rain(new Vector3(400f, 25f, 400f), 10f);
            VFXManager.Rain(new Vector3(-400f, 25f, -400f), 10f);
            VFXManager.HeatDistortion(new Vector3(0, 2, 0), 4f);

            // Lightning strikes around the perimeter (optional prefab)
            var lightningPrefab = Resources.Load<GameObject>("VFX/Lightning/LightningStrike");
            if (lightningPrefab != null)
            {
                Vector3[] boltPositions =
                {
                    new Vector3(  600f, 40f,  300f),
                    new Vector3( -600f, 40f, -300f),
                    new Vector3(  350f, 40f, -650f),
                    new Vector3( -350f, 40f,  650f)
                };
                for (int i = 0; i < boltPositions.Length; i++)
                {
                    var bolt = Object.Instantiate(lightningPrefab, transform);
                    bolt.name = $"Lightning_{i}";
                    bolt.transform.localPosition = boltPositions[i];
                    bolt.transform.localScale = Vector3.one * 4f;
                }
            }
        }

        // ── MOUNTAIN RING: dark mountains enclosing the arena ──
        private void BuildMountainRing()
        {
            // Helper to darken all materials on a placed mountain
            System.Action<GameObject> DarkenMountain = (mtn) =>
            {
                if (mtn != null)
                {
                    foreach (var rend in mtn.GetComponentsInChildren<Renderer>())
                    {
                        var mats = rend.materials;
                        for (int m = 0; m < mats.Length; m++)
                            mats[m].color *= new Color(0.3f, 0.3f, 0.35f); // darken to near-black
                        rend.materials = mats;
                    }
                }
            };

            GameObject mtn;

            // ── North wall (z ~ 250..270): 5 mountains ──
            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-200f, 0f, 260f), 15f, 7f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(-100f, 0f, 270f), 50f, 6f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(0f, 0f, 255f), 90f, 8f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(100f, 0f, 265f), 160f, 6f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(200f, 0f, 258f), 200f, 7f);
            DarkenMountain(mtn);

            // ── South wall (z ~ -250..-270): 5 mountains ──
            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-190f, 0f, -260f), 180f, 7f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_01",
                new Vector3(-80f, 0f, -270f), 135f, 6f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(30f, 0f, -255f), 270f, 8f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(130f, 0f, -265f), 220f, 6f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(210f, 0f, -258f), 60f, 5f);
            DarkenMountain(mtn);

            // ── East wall (x ~ 260): 4 mountains ──
            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_03",
                new Vector3(265f, 0f, -140f), 90f, 6f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_02",
                new Vector3(260f, 0f, -30f), 120f, 7f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_02",
                new Vector3(268f, 0f, 80f), 75f, 6f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_Snow_000",
                new Vector3(258f, 0f, 170f), 150f, 5f);
            DarkenMountain(mtn);

            // ── West wall (x ~ -260): 4 mountains ──
            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_04",
                new Vector3(-268f, 0f, -150f), 0f, 7f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_01",
                new Vector3(-260f, 0f, -30f), 270f, 6f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "IceMountain_03",
                new Vector3(-265f, 0f, 80f), 315f, 8f);
            DarkenMountain(mtn);

            mtn = HighlandsPrefabHelper.PlaceMountain(transform, "mountain_canyon_05",
                new Vector3(-258f, 0f, 180f), 190f, 6f);
            DarkenMountain(mtn);

            // Outer silhouette ring — tall dark cylinders visible through fog,
            // sitting outside the original 300-unit ring but well inside the
            // 900-unit walls. Gives depth without adding hundreds of mountain
            // prefabs.
            Color silhouette = new Color(0.10f, 0.09f, 0.13f);
            int silhouetteCount = 18;
            for (int i = 0; i < silhouetteCount; i++)
            {
                float a = (i / (float)silhouetteCount) * Mathf.PI * 2f;
                float d = Random.Range(600f, 780f);
                Vector3 p = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                float h = Random.Range(60f, 110f);
                float r = Random.Range(20f, 35f);
                AddCylinder(p, r, h, silhouette, $"OuterSilhouette_{i}");
            }
        }

        // ── CENTRAL ISLAND: dragon's lair with terrain island ──
        private void BuildCentralIsland()
        {
            Color darkRock = new Color(0.20f, 0.18f, 0.22f); // near-black with purple tint

            // Helper to darken all materials on a placed rock prefab
            System.Action<GameObject> DarkenRock = (rock) =>
            {
                if (rock != null)
                {
                    foreach (var rend in rock.GetComponentsInChildren<Renderer>())
                    {
                        var mats = rend.materials;
                        for (int m = 0; m < mats.Length; m++)
                            mats[m].color = darkRock;
                        rend.materials = mats;
                    }
                }
            };

            // ── Main dragon island ──
            TerrainFactory.GenerateIsland(transform, new Vector3(-40f, -4f, -40f),
                new Vector3(80f, 25f, 80f), 129, "dragon_island", 60, 0.03f, 15f, "DragonIsland");

            // ── Dark rocks around the island perimeter ──
            GameObject rock;

            rock = HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 1",
                new Vector3(35f, -1f, 10f), 0f, 3.0f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 3",
                new Vector3(-30f, 0f, 25f), 60f, 2.5f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 5",
                new Vector3(20f, 1f, -30f), 130f, 3.5f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 2",
                new Vector3(-25f, -1f, -20f), 200f, 2.8f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 4",
                new Vector3(10f, 0f, 35f), 280f, 4.0f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "IceRock_01",
                new Vector3(-35f, -1f, 5f), 45f, 3.2f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "IceRock_02",
                new Vector3(30f, 1f, -15f), 170f, 2.5f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "IceRock_03",
                new Vector3(-10f, 0f, -35f), 310f, 3.0f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "Standard Rock 1",
                new Vector3(0f, -1f, 38f), 90f, 2.2f);
            DarkenRock(rock);

            // ── Dramatic cliff formations on island edges ──
            rock = HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 1",
                new Vector3(38f, 0f, 0f), 15f, 4.0f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 2",
                new Vector3(-38f, 0f, -10f), 120f, 3.5f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 3",
                new Vector3(5f, 0f, -38f), 240f, 5.0f);
            DarkenRock(rock);

            rock = HighlandsPrefabHelper.PlaceRock(transform, "Rock Cliff 1",
                new Vector3(-15f, 0f, 36f), 330f, 3.0f);
            DarkenRock(rock);

            // ── 4 smaller cover islands / outcrops ──
            TerrainFactory.GenerateIsland(transform, new Vector3(-130f, -10f, 100f),
                new Vector3(60f, 15f, 60f), 129, "cover_island_NW", 50, 0.03f, 15f, "CoverIsland_NW");

            TerrainFactory.GenerateIsland(transform, new Vector3(120f, -10f, -90f),
                new Vector3(60f, 15f, 60f), 129, "cover_island_SE", 50, 0.03f, 15f, "CoverIsland_SE");

            TerrainFactory.GenerateIsland(transform, new Vector3(-100f, -10f, -120f),
                new Vector3(60f, 15f, 60f), 129, "cover_island_SW", 50, 0.03f, 15f, "CoverIsland_SW");

            TerrainFactory.GenerateIsland(transform, new Vector3(140f, -10f, 60f),
                new Vector3(60f, 15f, 60f), 129, "cover_island_NE", 50, 0.03f, 15f, "CoverIsland_NE");
        }

        // ── SHIPWRECKS: scattered destroyed ships ──
        private void BuildShipwrecks()
        {
            Color shipBrown = new Color(0.30f, 0.22f, 0.12f); // dark brown

            Vector3[] wreckPositions =
            {
                new Vector3(-160f, -2f, 140f),
                new Vector3(170f, -1f, 120f),
                new Vector3(150f, -3f, -150f),
                new Vector3(-180f, -2f, -100f),
                new Vector3(60f, -1f, 180f),
                new Vector3(420f, -2f, 360f),
                new Vector3(-460f, -3f, 220f),
                new Vector3(380f, -1f, -440f),
                new Vector3(-340f, -2f, -500f),
                new Vector3(540f, -3f, -80f),
                new Vector3(-540f, -1f, 60f)
            };
            float[] wreckYaws   = { 35f, 110f, 200f, 290f, 160f,  50f, 220f, 340f,  80f, 150f, 260f };
            float[] wreckTiltsX = { 12f, -8f, 15f, -10f, 6f,      9f, -12f,  7f,  -6f, 11f,  -9f };
            // why: deterministic list, alternating upright (~5-15°) and heavily listing (35-55°) to vary silhouette
            float[] wreckTiltsZ = { -45f, 12f, -52f, 8f, 40f, -10f, 48f, -15f, 55f, 9f, -38f };
            // why: sink factor 0.3-0.6 of ship height — partial submerge per wreck, alternating shallow/deep
            float[] sinkFactors = { 0.35f, 0.55f, 0.40f, 0.60f, 0.32f, 0.48f, 0.45f, 0.58f, 0.38f, 0.52f, 0.42f };

            var shipPrefab = Resources.Load<GameObject>("Models/ColonialShip");
            if (shipPrefab == null)
            {
                Debug.LogWarning("[KrakenLair] Failed to load Models/ColonialShip — falling back to procedural wrecks.");
                for (int i = 0; i < wreckPositions.Length; i++)
                    BuildProceduralWreck(i, wreckPositions[i], wreckYaws[i], wreckTiltsX[i], wreckTiltsZ[i], shipBrown);
                return;
            }

            for (int i = 0; i < wreckPositions.Length; i++)
            {
                var ship = Object.Instantiate(shipPrefab, transform);
                ship.name = $"Shipwreck_{i}";

                // why: scale 3 — user's small boats ~10 units, ColonialShip prefab likely small; flagged for playtest
                const float scale = 3f;
                ship.transform.localScale = Vector3.one * scale;

                float shipHeightApprox = 4f * scale; // approx prefab height before scale × scale
                Vector3 pos = wreckPositions[i];
                pos.y = -shipHeightApprox * sinkFactors[i];
                ship.transform.localPosition = pos;
                ship.transform.localRotation = Quaternion.Euler(wreckTiltsX[i], wreckYaws[i], wreckTiltsZ[i]);
                ship.isStatic = true;

                FixupShipwreckInstance(ship, 0.7f);
            }
        }

        private void BuildProceduralWreck(int i, Vector3 pos, float yaw, float tiltX, float tiltZ, Color shipBrown)
        {
            var ship = new GameObject($"Shipwreck_{i}");
            ship.transform.SetParent(transform, false);
            ship.transform.localPosition = pos;
            ship.transform.localRotation = Quaternion.Euler(tiltX, yaw, tiltZ);
            ship.isStatic = true;

            var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "Hull";
            hull.transform.SetParent(ship.transform, false);
            hull.transform.localPosition = Vector3.zero;
            hull.transform.localScale = new Vector3(5f, 3f, 18f);
            SetMaterial(hull, MakeMaterial(shipBrown));

            var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.name = "BrokenMast";
            mast.transform.SetParent(ship.transform, false);
            mast.transform.localPosition = new Vector3(0f, 3f, Random.Range(-4f, 4f));
            mast.transform.localScale = new Vector3(0.4f, 5f, 0.4f);
            mast.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-35f, 35f));
            SetMaterial(mast, MakeMaterial(shipBrown * 0.7f));
            Object.DestroyImmediate(mast.GetComponent<Collider>());
        }

        private static void FixupShipwreckInstance(GameObject ship, float darken)
        {
            CityPrefabHelper.FixURPMaterials(ship.transform);

            foreach (var rb in ship.GetComponentsInChildren<Rigidbody>())
                Object.Destroy(rb);

            var meshFilters = ship.GetComponentsInChildren<MeshFilter>();
            for (int mf = 0; mf < meshFilters.Length; mf++)
            {
                if (meshFilters[mf].sharedMesh == null) continue;
                if (meshFilters[mf].GetComponent<Collider>() != null) continue;
                var mc = meshFilters[mf].gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = meshFilters[mf].sharedMesh;
                mc.convex = false; // why: non-convex — accurate hull shape, wrecks don't move so kinematic-only limits don't apply
            }

            // why: darken materials so prefab reads as weathered/damaged, not freshly built
            foreach (var rend in ship.GetComponentsInChildren<Renderer>())
            {
                var mats = rend.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null) continue;
                    if (mats[m].HasProperty("_BaseColor"))
                        mats[m].SetColor("_BaseColor", mats[m].GetColor("_BaseColor") * darken);
                    else if (mats[m].HasProperty("_Color"))
                        mats[m].color = mats[m].color * darken;
                }
                rend.materials = mats;
            }

            foreach (var t in ship.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
        }

        // ── DRAGON: load prefab, attach DragonBoss AI ──
        private void SpawnDragon()
        {
            var dragonPrefab = Resources.Load<GameObject>("Models/Dragon/RedDragon");
            if (dragonPrefab == null)
            {
                Debug.LogWarning("[DragonLair] Failed to load dragon prefab!");
                return;
            }

            var dragon = Object.Instantiate(dragonPrefab, transform);
            dragon.name = "PuffTheDragon";
            dragon.transform.localPosition = new Vector3(0f, 8f, 0f); // on top of island
            dragon.transform.localScale = Vector3.one * 2.0f; // large imposing dragon
            dragon.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face outward

            // Convert Standard shader materials to URP Lit with full PBR properties
            // DO NOT call FixURPMaterials first -- it strips textures before we can copy them
            var urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader != null)
            {
                foreach (var rend in dragon.GetComponentsInChildren<Renderer>())
                {
                    var mats = rend.sharedMaterials; // use sharedMaterials to read ORIGINAL data
                    var newMats = new Material[mats.Length];
                    for (int m = 0; m < mats.Length; m++)
                    {
                        var oldMat = mats[m];
                        if (oldMat == null || oldMat.shader.name.Contains("Universal"))
                        {
                            newMats[m] = oldMat;
                            continue;
                        }

                        var newMat = new Material(urpShader);
                        newMat.name = oldMat.name + "_URP";

                        // Albedo
                        Texture albedo = oldMat.HasProperty("_MainTex") ? oldMat.GetTexture("_MainTex") : null;
                        if (albedo != null) newMat.SetTexture("_BaseMap", albedo);
                        Color baseColor = oldMat.HasProperty("_Color") ? oldMat.GetColor("_Color") : Color.white;
                        newMat.SetColor("_BaseColor", baseColor);

                        // Normal map
                        Texture normal = oldMat.HasProperty("_BumpMap") ? oldMat.GetTexture("_BumpMap") : null;
                        if (normal != null)
                        {
                            newMat.SetTexture("_BumpMap", normal);
                            newMat.EnableKeyword("_NORMALMAP");
                        }

                        // Metallic + smoothness
                        Texture metallic = oldMat.HasProperty("_MetallicGlossMap") ? oldMat.GetTexture("_MetallicGlossMap") : null;
                        if (metallic != null)
                        {
                            newMat.SetTexture("_MetallicGlossMap", metallic);
                            newMat.EnableKeyword("_METALLICSPECGLOSSMAP");
                        }
                        if (oldMat.HasProperty("_Metallic"))
                            newMat.SetFloat("_Metallic", oldMat.GetFloat("_Metallic"));
                        if (oldMat.HasProperty("_Glossiness"))
                            newMat.SetFloat("_Smoothness", oldMat.GetFloat("_Glossiness"));

                        newMats[m] = newMat;
                    }
                    rend.materials = newMats;
                }
            }

            // Ensure Animator has a controller (may be lost when prefab is copied to Resources)
            var animator = dragon.GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController == null)
            {
                var ctrl = Resources.Load<RuntimeAnimatorController>("Dragon/RedDragon1.1Example");
                if (ctrl != null)
                    animator.runtimeAnimatorController = ctrl;
                else
                    Debug.LogWarning("[DragonLair] Failed to load animator controller for dragon!");
            }
            if (animator == null)
            {
                Debug.LogWarning("[DragonLair] No Animator found on dragon prefab!");
            }

            // Add MeshColliders for exact-shape projectile detection
            var meshFilters = dragon.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length > 0)
            {
                for (int mf = 0; mf < meshFilters.Length; mf++)
                {
                    if (meshFilters[mf].sharedMesh == null) continue;
                    if (meshFilters[mf].GetComponent<Collider>() != null) continue;
                    var mc = meshFilters[mf].gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = meshFilters[mf].sharedMesh;
                    mc.convex = true; // convex required for trigger/collision interaction
                }
            }
            else
            {
                // Fallback BoxCollider
                var col = dragon.AddComponent<BoxCollider>();
                col.size = new Vector3(8f, 4f, 12f);
                col.center = new Vector3(0f, 2f, 0f);
            }

            // Attach AI controller
            var boss = dragon.AddComponent<DragonBoss>();
            boss.islandCenter = new Vector3(0f, 8f, 0f);
            boss.maxHP = 500;
            boss.flyHeight = 80f; // why: high enough to clear the 40-ish-tall mountain ring
            boss.patrolRadius = 500f; // why: arena expanded — dragon patrols the outer perimeter

            // Attach health system
            dragon.AddComponent<DragonHealth>();
        }
    }

    // =========================================================================
    // 5. WaterStraitOfHormuz -- narrow channel flanked by Iranian & Omani shores
    //    with a fortified central island (analog of Hormuz Island). Players
    //    spawn tight in the middle so both shores and the island are visible.
    // =========================================================================

    public class WaterStraitOfHormuz : ArenaBase
    {
        public override string ArenaName => "Strait of Hormuz";

        // why: colors reused across bases, ships, and props
        private static readonly Color IranSand      = new Color(0.78f, 0.66f, 0.46f);
        private static readonly Color IranRoof      = new Color(0.55f, 0.42f, 0.28f);
        private static readonly Color OmanSand      = new Color(0.86f, 0.78f, 0.58f);
        private static readonly Color OmanRoof      = new Color(0.65f, 0.56f, 0.40f);
        private static readonly Color FortStone     = new Color(0.60f, 0.54f, 0.42f);
        private static readonly Color HullGray      = new Color(0.38f, 0.40f, 0.42f);
        private static readonly Color HullDark      = new Color(0.22f, 0.24f, 0.26f);
        private static readonly Color OilTank       = new Color(0.80f, 0.80f, 0.78f);
        private static readonly Color RustRed       = new Color(0.55f, 0.25f, 0.15f);
        private static readonly Color ContainerRed  = new Color(0.70f, 0.22f, 0.18f);
        private static readonly Color ContainerBlue = new Color(0.18f, 0.38f, 0.60f);
        private static readonly Color ContainerTan  = new Color(0.80f, 0.70f, 0.45f);
        private static readonly Color IranFlag      = new Color(0.08f, 0.55f, 0.20f);
        private static readonly Color OmanFlag      = new Color(0.75f, 0.12f, 0.12f);
        private static readonly Color LampWhite     = new Color(1.00f, 0.95f, 0.80f);

        public override void Build()
        {
            AddWaterSurface(750f);

            // why: warm Persian Gulf daylight palette
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.88f, 0.78f, 0.60f);
            RenderSettings.fogDensity = 0.0025f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.ambientLight = new Color(0.78f, 0.70f, 0.56f);

            BuildTerrain();
            // Composite base builder now handles structure, perimeter, turrets, utility, etc.
            // Legacy BuildIranianBase/BuildOmaniBase/BuildCentralIslandBase retained for backward compat
            // but replaced by MilitaryBaseBuilder.Build for denser, more mechanically-complete bases.
            MilitaryBaseBuilder.Build(transform, new Vector3(-120f, 22f, 190f), 0f,
                MilitaryBaseBuilder.IranianPalette(), 1.0f, turretCount: 6, hasHelipad: false);
            MilitaryBaseBuilder.Build(transform, new Vector3(80f, 20f, -190f), 180f,
                MilitaryBaseBuilder.OmaniPalette(), 0.9f, turretCount: 4, hasHelipad: false);
            MilitaryBaseBuilder.Build(transform, new Vector3(0f, 7f, 0f), 0f,
                MilitaryBaseBuilder.HormuzIslandPalette(), 0.7f, turretCount: 2, hasHelipad: true);
            BuildWarships();
            BuildSeaMines();
            BuildDecorAndProps();
            BuildShipwrecks();

            // Tight central spawn so players start in the channel and see both shores
            AddSpawnRing(Vector3.zero, 200f, 8, 1f);
            AddInvisibleWalls(750f, 50f);

            // Ambient VFX
            VFXManager.GroundFog(Vector3.zero, 8f);
            VFXManager.DustMotes(new Vector3(0f, 5f, 0f), 4f);
            VFXManager.HeatDistortion(new Vector3(-360f, 8f, 170f), 6f); // over Iranian refinery

            // Warm directional sun
            var sunObj = new GameObject("StraitSun");
            sunObj.transform.SetParent(transform, false);
            sunObj.transform.rotation = Quaternion.Euler(50f, -25f, 0f);
            var sun = sunObj.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.90f, 0.72f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
        }

        // =================================================================
        // Terrain: two long shores + central fortified island
        // =================================================================
        private void BuildTerrain()
        {
            // North shore (Iranian side): larger, industrial. Runs east-west.
            // Offset north so near edge sits around z ~ +120 leaving a ~240 wide channel center.
            TerrainFactory.GenerateIsland(
                transform, new Vector3(0f, -8f, 300f), new Vector3(1000f, 30f, 240f),
                257, "hormuz_north", 80, 0.02f, 22f, "IranianShore");

            // South shore (Omani/UAE side): slightly smaller, dune-like
            TerrainFactory.GenerateIsland(
                transform, new Vector3(0f, -8f, -300f), new Vector3(900f, 26f, 220f),
                257, "hormuz_south", 75, 0.022f, 20f, "OmaniShore");

            // Central Hormuz island — visible from both shores
            TerrainFactory.GenerateIsland(
                transform, new Vector3(0f, -5f, 0f), new Vector3(120f, 25f, 120f),
                129, "hormuz_island", 55, 0.02f, 14f, "HormuzIsland");

            // Eastern cape (wider area opens up)
            TerrainFactory.GenerateIsland(
                transform, new Vector3(560f, -8f, 320f), new Vector3(260f, 22f, 200f),
                129, "hormuz_ne_cape", 60, 0.02f, 18f, "IranianCape_E");
            TerrainFactory.GenerateIsland(
                transform, new Vector3(560f, -8f, -340f), new Vector3(240f, 20f, 200f),
                129, "hormuz_se_cape", 60, 0.02f, 18f, "OmaniCape_E");

            // Western cape
            TerrainFactory.GenerateIsland(
                transform, new Vector3(-560f, -8f, 340f), new Vector3(240f, 22f, 200f),
                129, "hormuz_nw_cape", 60, 0.02f, 18f, "IranianCape_W");
            TerrainFactory.GenerateIsland(
                transform, new Vector3(-560f, -8f, -320f), new Vector3(220f, 20f, 200f),
                129, "hormuz_sw_cape", 60, 0.02f, 18f, "OmaniCape_W");
        }

        // =================================================================
        // Iranian base (north shore, industrial)
        // =================================================================
        private void BuildIranianBase()
        {
            // Main compound on the north shore, east of center. y~22 = on top of shore terrain.
            Vector3 baseCenter = new Vector3(-120f, 22f, 190f);

            // Command HQ + barracks + hangar
            AddBuilding(baseCenter + new Vector3(0f, 0f, 0f),       new Vector3(30f, 10f, 18f), IranSand, IranRoof, "IR_HQ");
            AddBuilding(baseCenter + new Vector3(-35f, 0f, 0f),     new Vector3(22f, 8f, 14f),  IranSand, IranRoof, "IR_Barracks_W");
            AddBuilding(baseCenter + new Vector3(35f, 0f, 0f),      new Vector3(22f, 8f, 14f),  IranSand, IranRoof, "IR_Barracks_E");
            AddBuilding(baseCenter + new Vector3(0f, 0f, -30f),     new Vector3(40f, 12f, 20f), IranSand, IranRoof, "IR_Hangar");
            AddBuilding(baseCenter + new Vector3(-60f, 0f, -20f),   new Vector3(18f, 7f, 10f),  IranSand, IranRoof, "IR_Guardhouse_W");
            AddBuilding(baseCenter + new Vector3(60f, 0f, -20f),    new Vector3(18f, 7f, 10f),  IranSand, IranRoof, "IR_Guardhouse_E");

            // Perimeter walls (short berm-style)
            AddWall(baseCenter + new Vector3(-75f, 0f, 20f), baseCenter + new Vector3(75f, 0f, 20f), 4f, 1.5f, IranSand * 0.85f, "IR_Wall_N");
            AddWall(baseCenter + new Vector3(-75f, 0f, -45f), baseCenter + new Vector3(-75f, 0f, 20f), 4f, 1.5f, IranSand * 0.85f, "IR_Wall_W");
            AddWall(baseCenter + new Vector3(75f, 0f, -45f), baseCenter + new Vector3(75f, 0f, 20f), 4f, 1.5f, IranSand * 0.85f, "IR_Wall_E");

            // Radar dish (cylinder + sphere)
            AddCylinder(baseCenter + new Vector3(0f, 6f, 10f), 1.2f, 12f, HullGray, "IR_RadarMast");
            AddSphere(baseCenter + new Vector3(0f, 18f, 10f), 3.2f, new Color(0.88f, 0.88f, 0.90f), "IR_RadarDome");

            // Antenna masts (tall thin)
            AddCylinder(baseCenter + new Vector3(-20f, 5f, 8f), 0.4f, 24f, HullDark, "IR_Antenna_1");
            AddCylinder(baseCenter + new Vector3(20f, 5f, 8f),  0.4f, 20f, HullDark, "IR_Antenna_2");

            // Refinery / oil storage cluster (east of compound)
            Vector3 refinery = new Vector3(-360f, 22f, 170f);
            for (int r = 0; r < 6; r++)
            {
                float rx = (r % 3) * 18f - 18f;
                float rz = (r / 3) * 18f - 9f;
                AddCylinder(refinery + new Vector3(rx, 0f, rz), 6f, 10f, OilTank, $"IR_OilTank_{r}");
                AddCylinder(refinery + new Vector3(rx, 10f, rz), 6f, 0.4f, OilTank * 0.9f, $"IR_OilTankCap_{r}");
            }
            // Refinery stacks
            AddCylinder(refinery + new Vector3(30f, 0f, 10f), 1.8f, 34f, HullGray, "IR_Stack_1");
            AddCylinder(refinery + new Vector3(30f, 0f, -10f), 1.8f, 30f, HullGray, "IR_Stack_2");
            AddCylinder(refinery + new Vector3(40f, 0f, 0f),  1.5f, 26f, HullGray, "IR_Stack_3");

            // Dock pilings along north shoreline
            for (int i = 0; i < 8; i++)
            {
                AddCylinder(new Vector3(-180f + i * 10f, 0f, 110f), 1f, 6f, HullDark, $"IR_Piling_{i}");
            }
            AddBlockUnchecked(new Vector3(-145f, 4.5f, 110f), new Vector3(80f, 0.6f, 4f), new Color(0.45f, 0.30f, 0.15f), "IR_Dock");

            // Cargo containers near the dock
            Color[] cargoPalette = { ContainerRed, ContainerBlue, ContainerTan };
            for (int c = 0; c < 10; c++)
            {
                float cx = -190f + (c % 5) * 7f;
                float cy = (c / 5) * 3.2f + 1.6f;
                AddBlockUnchecked(new Vector3(cx, cy, 125f), new Vector3(6f, 3f, 2.5f), cargoPalette[c % 3], $"IR_Cargo_{c}");
            }

            // Shore turrets (4-6 on elevated mounts). detectionRange=80, fireInterval=4, damagePerShot=30
            Vector3[] iranTurretPos = {
                new Vector3(-420f, 24f, 150f),
                new Vector3(-300f, 24f, 130f),
                new Vector3(-140f, 24f, 125f),
                new Vector3(20f,   24f, 130f),
                new Vector3(200f,  24f, 140f),
                new Vector3(380f,  24f, 150f),
            };
            for (int i = 0; i < iranTurretPos.Length; i++)
            {
                AddCylinder(iranTurretPos[i] + new Vector3(0f, -2f, 0f), 3f, 2f, FortStone, $"IR_TurretMount_{i}");
                CreateShoreTurret(iranTurretPos[i], $"IR_Turret_{i}");
            }

            // Flagpole + searchlights
            SafeAddFlagpole(baseCenter + new Vector3(0f, 10f, 10f), IranFlag, "Iran");
            SafeAddSearchlight(baseCenter + new Vector3(-60f, 12f, 20f), new Vector3(-0.4f, -0.3f, -1f));
            SafeAddSearchlight(baseCenter + new Vector3(60f, 12f, 20f),  new Vector3(0.4f, -0.3f, -1f));
            SafeAddSearchlight(refinery  + new Vector3(0f, 14f, -20f),   new Vector3(0f, -0.3f, -1f));
        }

        // =================================================================
        // Omani base (south shore, lighter)
        // =================================================================
        private void BuildOmaniBase()
        {
            Vector3 baseCenter = new Vector3(80f, 20f, -190f);

            AddBuilding(baseCenter + new Vector3(0f, 0f, 0f),     new Vector3(26f, 9f, 16f),  OmanSand, OmanRoof, "OM_HQ");
            AddBuilding(baseCenter + new Vector3(-28f, 0f, 0f),   new Vector3(18f, 7f, 12f),  OmanSand, OmanRoof, "OM_Barracks_W");
            AddBuilding(baseCenter + new Vector3(28f, 0f, 0f),    new Vector3(18f, 7f, 12f),  OmanSand, OmanRoof, "OM_Barracks_E");
            AddBuilding(baseCenter + new Vector3(0f, 0f, 25f),    new Vector3(32f, 10f, 16f), OmanSand, OmanRoof, "OM_Hangar");

            // Small fuel tank cluster
            for (int r = 0; r < 4; r++)
            {
                AddCylinder(baseCenter + new Vector3(-45f + r * 8f, 0f, -15f), 4f, 8f, OilTank, $"OM_Tank_{r}");
            }

            // Radar
            AddCylinder(baseCenter + new Vector3(0f, 5f, -10f), 1f, 10f, HullGray, "OM_RadarMast");
            AddSphere(baseCenter + new Vector3(0f, 16f, -10f),  2.5f, new Color(0.85f, 0.85f, 0.88f), "OM_RadarDome");

            // Antenna
            AddCylinder(baseCenter + new Vector3(15f, 4f, -8f), 0.35f, 18f, HullDark, "OM_Antenna");

            // Dock pilings + deck
            for (int i = 0; i < 6; i++)
            {
                AddCylinder(new Vector3(40f + i * 10f, 0f, -115f), 1f, 6f, HullDark, $"OM_Piling_{i}");
            }
            AddBlockUnchecked(new Vector3(65f, 4.5f, -115f), new Vector3(60f, 0.6f, 3.5f), new Color(0.50f, 0.35f, 0.18f), "OM_Dock");

            // Shore turrets (4) on elevated mounts
            Vector3[] omanTurretPos = {
                new Vector3(-380f, 22f, -150f),
                new Vector3(-120f, 22f, -140f),
                new Vector3(140f,  22f, -140f),
                new Vector3(360f,  22f, -155f),
            };
            for (int i = 0; i < omanTurretPos.Length; i++)
            {
                AddCylinder(omanTurretPos[i] + new Vector3(0f, -2f, 0f), 3f, 2f, FortStone, $"OM_TurretMount_{i}");
                CreateShoreTurret(omanTurretPos[i], $"OM_Turret_{i}");
            }

            // Flagpole + searchlights
            SafeAddFlagpole(baseCenter + new Vector3(0f, 9f, 10f), OmanFlag, "Oman");
            SafeAddSearchlight(baseCenter + new Vector3(-40f, 10f, 14f), new Vector3(-0.3f, -0.3f, 1f));
            SafeAddSearchlight(baseCenter + new Vector3(40f, 10f, 14f),  new Vector3(0.3f, -0.3f, 1f));
        }

        // =================================================================
        // Central Hormuz Island fortified base
        // =================================================================
        private void BuildCentralIslandBase()
        {
            Vector3 center = new Vector3(0f, 7f, 0f);

            // Fort compound — 4 buildings
            AddBuilding(center + new Vector3(0f, 0f, 0f),    new Vector3(22f, 10f, 18f), FortStone, FortStone * 0.8f, "HI_Keep");
            AddBuilding(center + new Vector3(-20f, 0f, 12f), new Vector3(14f, 7f, 10f),  FortStone, FortStone * 0.8f, "HI_Barracks");
            AddBuilding(center + new Vector3(20f, 0f, 12f),  new Vector3(14f, 7f, 10f),  FortStone, FortStone * 0.8f, "HI_Armory");
            AddBuilding(center + new Vector3(0f, 0f, -18f),  new Vector3(18f, 6f, 10f),  FortStone, FortStone * 0.8f, "HI_Mess");

            // Central watchtower
            AddCylinder(center + new Vector3(0f, 10f, 0f), 4f, 12f, FortStone, "HI_Tower");
            AddCylinder(center + new Vector3(0f, 22f, 0f), 5f, 2f, FortStone * 0.9f, "HI_Battlement");

            // Perimeter walls
            AddWall(center + new Vector3(-32f, 0f, -30f), center + new Vector3(32f, 0f, -30f), 4f, 1.5f, FortStone * 0.85f, "HI_Wall_S");
            AddWall(center + new Vector3(-32f, 0f, 30f),  center + new Vector3(32f, 0f, 30f),  4f, 1.5f, FortStone * 0.85f, "HI_Wall_N");
            AddWall(center + new Vector3(-32f, 0f, -30f), center + new Vector3(-32f, 0f, 30f), 4f, 1.5f, FortStone * 0.85f, "HI_Wall_W");
            AddWall(center + new Vector3(32f, 0f, -30f),  center + new Vector3(32f, 0f, 30f),  4f, 1.5f, FortStone * 0.85f, "HI_Wall_E");

            // Helicopter landing pad (flat circle) with a cross marking
            AddCylinder(center + new Vector3(35f, -5f, -20f), 6f, 0.5f, new Color(0.20f, 0.20f, 0.22f), "HI_Pad");
            AddBlockUnchecked(center + new Vector3(35f, -4.7f, -20f), new Vector3(8f, 0.1f, 1f), LampWhite, "HI_PadMark_H");
            AddBlockUnchecked(center + new Vector3(35f, -4.7f, -20f), new Vector3(1f, 0.1f, 8f), LampWhite, "HI_PadMark_V");

            // Two turrets
            Vector3[] islandTurretPos = {
                center + new Vector3(-34f, 2f, 0f),
                center + new Vector3(34f,  2f, 0f),
            };
            for (int i = 0; i < islandTurretPos.Length; i++)
            {
                AddCylinder(islandTurretPos[i] + new Vector3(0f, -2f, 0f), 2.5f, 2f, FortStone, $"HI_TurretMount_{i}");
                CreateShoreTurret(islandTurretPos[i], $"HI_Turret_{i}");
            }

            // Flagpole + searchlight
            SafeAddFlagpole(center + new Vector3(0f, 24f, 0f), new Color(0.95f, 0.95f, 0.95f), "Hormuz");
            SafeAddSearchlight(center + new Vector3(0f, 24f, 0f), new Vector3(0f, -0.2f, 1f));
        }

        // =================================================================
        // Patrolling warships (2-3 hulls, each with its own loop)
        // =================================================================
        private void BuildWarships()
        {
            // Three closed-loop patrols threading through the channel
            Vector3[][] loops = {
                new[] {
                    new Vector3(-400f, 0f,  60f), new Vector3(-200f, 0f,  30f),
                    new Vector3( 100f, 0f,  40f), new Vector3( 350f, 0f,  70f),
                    new Vector3( 450f, 0f, -20f), new Vector3( 200f, 0f,  -30f),
                },
                new[] {
                    new Vector3( 400f, 0f,  80f), new Vector3( 150f, 0f,  60f),
                    new Vector3(-150f, 0f,  50f), new Vector3(-380f, 0f,  20f),
                    new Vector3(-200f, 0f, -50f), new Vector3( 250f, 0f, -60f),
                },
                new[] {
                    new Vector3(-480f, 0f, -30f), new Vector3(-250f, 0f, -70f),
                    new Vector3(  50f, 0f, -80f), new Vector3( 340f, 0f, -60f),
                    new Vector3( 200f, 0f,  30f), new Vector3( -80f, 0f,  60f),
                },
            };

            for (int s = 0; s < loops.Length; s++)
            {
                CreateWarship($"Warship_{s}", loops[s]);
            }
        }

        private void CreateWarship(string shipName, Vector3[] loop)
        {
            var shipObj = new GameObject(shipName);
            shipObj.transform.SetParent(transform, false);
            shipObj.transform.position = loop[0];

            // Hull (long block)
            AddChildPrimitive(shipObj.transform, PrimitiveType.Cube, new Vector3(0f, 1.2f, 0f),
                new Vector3(8f, 2.4f, 36f), HullGray, "Hull");

            // Deck trim
            AddChildPrimitive(shipObj.transform, PrimitiveType.Cube, new Vector3(0f, 2.5f, 0f),
                new Vector3(7.4f, 0.3f, 34f), HullDark, "Deck");

            // Bridge superstructure
            AddChildPrimitive(shipObj.transform, PrimitiveType.Cube, new Vector3(0f, 4.2f, -2f),
                new Vector3(5f, 3f, 8f), new Color(0.52f, 0.54f, 0.56f), "Bridge");
            AddChildPrimitive(shipObj.transform, PrimitiveType.Cube, new Vector3(0f, 6.2f, -4f),
                new Vector3(3.6f, 1.6f, 4f), new Color(0.58f, 0.60f, 0.62f), "BridgeTop");

            // Radar mast
            AddChildPrimitive(shipObj.transform, PrimitiveType.Cylinder, new Vector3(0f, 9.5f, -4f),
                new Vector3(0.3f, 3f, 0.3f), HullDark, "RadarMast");
            AddChildPrimitive(shipObj.transform, PrimitiveType.Sphere, new Vector3(0f, 12f, -4f),
                new Vector3(1.2f, 1.2f, 1.2f), new Color(0.85f, 0.85f, 0.88f), "RadarDome");

            // Turret barbettes (cylinders fore + aft)
            AddChildPrimitive(shipObj.transform, PrimitiveType.Cylinder, new Vector3(0f, 3f, 12f),
                new Vector3(2.2f, 0.8f, 2.2f), HullGray * 0.85f, "TurretFore");
            AddChildPrimitive(shipObj.transform, PrimitiveType.Cube, new Vector3(0f, 3.6f, 14f),
                new Vector3(0.6f, 0.6f, 4f), HullDark, "BarrelFore");
            AddChildPrimitive(shipObj.transform, PrimitiveType.Cylinder, new Vector3(0f, 3f, -12f),
                new Vector3(2.2f, 0.8f, 2.2f), HullGray * 0.85f, "TurretAft");
            AddChildPrimitive(shipObj.transform, PrimitiveType.Cube, new Vector3(0f, 3.6f, -14f),
                new Vector3(0.6f, 0.6f, 4f), HullDark, "BarrelAft");

            // Build waypoints as child empties
            var waypoints = new Transform[loop.Length];
            for (int i = 0; i < loop.Length; i++)
            {
                var wpObj = new GameObject($"WP_{i}");
                wpObj.transform.SetParent(shipObj.transform.parent, false); // why: keep waypoints in arena space, not moving with ship
                wpObj.transform.position = loop[i];
                waypoints[i] = wpObj.transform;
            }

            // Attach Warship component if type available; graceful fallback otherwise
            var warshipType = System.Type.GetType("CloseEncounters.Arena.Warship") ?? System.Type.GetType("Warship");
            if (warshipType != null)
            {
                var comp = shipObj.AddComponent(warshipType) as MonoBehaviour;
                if (comp != null)
                {
                    SetField(comp, "patrolWaypoints", waypoints);
                    SetField(comp, "patrolSpeed", 8f);
                    SetField(comp, "detectionRange", 100f);
                    SetField(comp, "damagePerShot", 25);
                    SetField(comp, "fireInterval", 4f);
                }
            }
            else
            {
                Debug.LogWarning("[StraitOfHormuz] Warship type not found — warship hull spawned as static decor.");
            }
        }

        // =================================================================
        // Sea mines — scattered along the channel, avoiding the spawn ring
        // =================================================================
        private void BuildSeaMines()
        {
            const int mineCount = 20;
            const float spawnAvoid = 220f; // why: 200 spawn ring + buffer
            var rand = new System.Random(0x486F726D); // "Horm"
            int placed = 0, attempts = 0;
            while (placed < mineCount && attempts < mineCount * 10)
            {
                attempts++;
                float x = (float)(rand.NextDouble() * 1200.0 - 600.0);
                float z = (float)(rand.NextDouble() * 200.0 - 100.0); // keep within channel band
                var pos = new Vector3(x, 0.5f, z);
                if (pos.magnitude < spawnAvoid) continue;
                // Avoid the central island footprint
                if (Mathf.Abs(x) < 70f && Mathf.Abs(z) < 70f) continue;
                CreateSeaMine(pos, placed);
                placed++;
            }
        }

        private void CreateSeaMine(Vector3 pos, int idx)
        {
            var mineObj = new GameObject($"SeaMine_{idx}");
            mineObj.transform.SetParent(transform, false);
            mineObj.transform.position = pos;

            // Black sphere body
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "MineBody";
            body.transform.SetParent(mineObj.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = Vector3.one * 1.4f;
            SetMaterial(body, MakeMaterial(new Color(0.06f, 0.06f, 0.08f), "MineBody"));

            // Red warning stripe (thin flattened sphere)
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            stripe.name = "MineStripe";
            stripe.transform.SetParent(mineObj.transform, false);
            stripe.transform.localPosition = Vector3.zero;
            stripe.transform.localScale = new Vector3(1.45f, 0.25f, 1.45f);
            SetMaterial(stripe, MakeEmissiveMaterial(new Color(0.70f, 0.08f, 0.05f), new Color(0.70f, 0.08f, 0.05f) * 2f, "MineStripe"));
            Object.DestroyImmediate(stripe.GetComponent<Collider>());

            // Spike protrusions (small cubes)
            for (int s = 0; s < 6; s++)
            {
                float ang = s * 60f * Mathf.Deg2Rad;
                var spike = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spike.transform.SetParent(mineObj.transform, false);
                spike.transform.localPosition = new Vector3(Mathf.Cos(ang) * 0.9f, 0f, Mathf.Sin(ang) * 0.9f);
                spike.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
                SetMaterial(spike, MakeMaterial(new Color(0.2f, 0.2f, 0.22f)));
                Object.DestroyImmediate(spike.GetComponent<Collider>());
            }

            var mineType = System.Type.GetType("CloseEncounters.Arena.SeaMine") ?? System.Type.GetType("SeaMine");
            if (mineType != null)
            {
                var comp = mineObj.AddComponent(mineType) as MonoBehaviour;
                if (comp != null)
                {
                    SetField(comp, "damage", 150);
                    SetField(comp, "blastRadius", 8f);
                    SetField(comp, "bobAmplitude", 0.2f);
                }
            }
            else
            {
                Debug.LogWarning("[StraitOfHormuz] SeaMine type not found — mine spawned as static prop.");
            }
        }

        // =================================================================
        // Decor + props (tankers, rigs, helicopters, lighthouses, buoys)
        // =================================================================
        private void BuildDecorAndProps()
        {
            // 4 Tankers — wider east/west basins
            SafeAddTanker(new Vector3(540f, 0f, 80f),  15f, 1.0f);
            SafeAddTanker(new Vector3(620f, 0f, -140f), 195f, 1.1f);
            SafeAddTanker(new Vector3(-560f, 0f, 100f), 340f, 1.0f);
            SafeAddTanker(new Vector3(-620f, 0f, -130f), 160f, 1.15f);

            // 2 Oil rigs on Iranian side
            SafeAddOilRig(new Vector3(-420f, 0f, 80f), 1.0f);
            SafeAddOilRig(new Vector3(300f, 0f, 90f),  1.1f);

            // 2 Helicopters on scripted loops (one per shore)
            SafeAddHelicopter(new Vector3(-500f, 40f, 200f), new Vector3(500f, 40f, 200f), 18f);
            SafeAddHelicopter(new Vector3(500f, 35f, -210f),  new Vector3(-500f, 35f, -210f), 16f);

            // 5 Lighthouses on outer shore points
            SafeAddLighthouse(new Vector3(-650f, 0f, 220f),  new Color(0.95f, 0.35f, 0.25f));
            SafeAddLighthouse(new Vector3(650f, 0f, 230f),   new Color(0.95f, 0.35f, 0.25f));
            SafeAddLighthouse(new Vector3(-640f, 0f, -220f), new Color(0.30f, 0.85f, 0.40f));
            SafeAddLighthouse(new Vector3(640f, 0f, -230f),  new Color(0.30f, 0.85f, 0.40f));
            SafeAddLighthouse(new Vector3(0f, 18f, 70f),     new Color(1.00f, 0.90f, 0.40f)); // on Hormuz island tip

            // Buoys along shipping lane (red starboard, green port)
            Vector3[] buoyPos = {
                new Vector3(-350f, 0.5f, 40f),  new Vector3(-350f, 0.5f, -40f),
                new Vector3(-150f, 0.5f, 45f),  new Vector3(-150f, 0.5f, -45f),
                new Vector3(150f,  0.5f, 45f),  new Vector3(150f,  0.5f, -45f),
                new Vector3(350f,  0.5f, 40f),  new Vector3(350f,  0.5f, -40f),
            };
            for (int i = 0; i < buoyPos.Length; i++)
            {
                bool starboard = buoyPos[i].z > 0f;
                Color body = starboard ? new Color(0.75f, 0.15f, 0.12f) : new Color(0.15f, 0.60f, 0.25f);
                AddCylinder(buoyPos[i], 1.2f, 2.2f, body, $"Buoy_{i}");
                // Lamp
                var lamp = AddSphere(buoyPos[i] + new Vector3(0f, 1.6f, 0f), 0.5f, body * 1.8f, $"BuoyLamp_{i}");
                var rend = lamp.GetComponent<MeshRenderer>();
                if (rend != null) rend.sharedMaterial = MakeEmissiveMaterial(body, body * 2.5f, $"BuoyLampEmis_{i}");
            }

            // Dock piling extra clusters on each shore corner (decor)
            for (int i = 0; i < 4; i++)
            {
                AddCylinder(new Vector3(420f + i * 3f, 0f, 120f), 0.8f, 5f, HullDark, $"IR_PilingExtra_{i}");
                AddCylinder(new Vector3(-420f - i * 3f, 0f, -120f), 0.8f, 5f, HullDark, $"OM_PilingExtra_{i}");
            }
        }

        // =================================================================
        // Shipwrecks (Colonial ship prefab, same tilt/sink pattern as corsair)
        // =================================================================
        private static bool s_hormuzPrefabWarned = false;
        private static int s_hormuzWreckIndex = 0;
        private static readonly float[] s_hormuzTiltsZ = { -40f, 15f, 50f };
        private static readonly float[] s_hormuzSinkFactors = { 0.45f, 0.55f, 0.38f };

        private void BuildShipwrecks()
        {
            // 3 wrecks scattered along shorelines
            CreateShipwreck(new Vector3(-480f, -3f, 140f), HullDark);
            CreateShipwreck(new Vector3(460f, -3f, -150f), HullDark);
            CreateShipwreck(new Vector3(200f, -3f, 120f),  HullDark);
        }

        private void CreateShipwreck(Vector3 pos, Color hullColor)
        {
            int idx = s_hormuzWreckIndex++ % s_hormuzTiltsZ.Length;
            float tiltZ = s_hormuzTiltsZ[idx];
            float sink = s_hormuzSinkFactors[idx];

            var shipPrefab = Resources.Load<GameObject>("Models/ColonialShip");
            if (shipPrefab == null)
            {
                if (!s_hormuzPrefabWarned)
                {
                    Debug.LogWarning("[StraitOfHormuz] Failed to load Models/ColonialShip — falling back to procedural wreck.");
                    s_hormuzPrefabWarned = true;
                }
                CreateProceduralWreck(pos, hullColor, tiltZ);
                return;
            }

            var ship = Object.Instantiate(shipPrefab, transform);
            ship.name = $"Shipwreck_Hormuz_{idx}";
            const float scale = 3f;
            ship.transform.localScale = Vector3.one * scale;

            float shipHeightApprox = 4f * scale;
            var wreckPos = pos;
            wreckPos.y = -shipHeightApprox * sink;
            ship.transform.position = wreckPos;
            ship.transform.rotation = Quaternion.Euler(10f, 30f, tiltZ);
            ship.isStatic = true;

            CityPrefabHelper.FixURPMaterials(ship.transform);

            foreach (var rb in ship.GetComponentsInChildren<Rigidbody>())
                Object.Destroy(rb);

            var meshFilters = ship.GetComponentsInChildren<MeshFilter>();
            for (int mf = 0; mf < meshFilters.Length; mf++)
            {
                if (meshFilters[mf].sharedMesh == null) continue;
                if (meshFilters[mf].GetComponent<Collider>() != null) continue;
                var mc = meshFilters[mf].gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = meshFilters[mf].sharedMesh;
                mc.convex = false;
            }

            const float darken = 0.65f;
            foreach (var rend in ship.GetComponentsInChildren<Renderer>())
            {
                var mats = rend.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null) continue;
                    if (mats[m].HasProperty("_BaseColor"))
                        mats[m].SetColor("_BaseColor", mats[m].GetColor("_BaseColor") * darken);
                    else if (mats[m].HasProperty("_Color"))
                        mats[m].color = mats[m].color * darken;
                }
                rend.materials = mats;
            }

            foreach (var t in ship.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
        }

        private void CreateProceduralWreck(Vector3 pos, Color hullColor, float tiltZ)
        {
            var ship = new GameObject("ShipwreckProc");
            ship.transform.SetParent(transform, false);
            ship.transform.position = pos;
            ship.transform.rotation = Quaternion.Euler(10f, 30f, tiltZ);
            ship.isStatic = true;

            var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "Hull";
            hull.transform.SetParent(ship.transform, false);
            hull.transform.localScale = new Vector3(6f, 4f, 22f);
            SetMaterial(hull, MakeMaterial(hullColor));

            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "Deck";
            deck.transform.SetParent(ship.transform, false);
            deck.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            deck.transform.localScale = new Vector3(5.5f, 0.3f, 20f);
            SetMaterial(deck, MakeMaterial(hullColor * 0.7f));
            Object.DestroyImmediate(deck.GetComponent<Collider>());
        }

        // =================================================================
        // ShoreTurret helper
        // =================================================================
        private void CreateShoreTurret(Vector3 pos, string label)
        {
            var turretObj = new GameObject(label);
            turretObj.transform.SetParent(transform, false);
            turretObj.transform.position = pos;

            // Pedestal
            AddChildPrimitive(turretObj.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.5f, 0f),
                new Vector3(2.4f, 0.5f, 2.4f), HullGray, "Pedestal");

            // Rotating housing
            var housingGo = AddChildPrimitive(turretObj.transform, PrimitiveType.Cube, new Vector3(0f, 1.6f, 0f),
                new Vector3(3f, 1.4f, 3.2f), HullDark, "Housing");

            // Barrel (child of housing so if it rotates, barrel follows)
            var barrelGo = AddChildPrimitive(housingGo.transform, PrimitiveType.Cube, new Vector3(0f, 0.2f, 2.3f),
                new Vector3(0.4f, 0.4f, 3.5f), new Color(0.25f, 0.25f, 0.28f), "Barrel");
            // Adjust barrel's local position — it inherits housing's lossyScale. Reset relative to housing origin:
            barrelGo.transform.localPosition = new Vector3(0f, 0.15f, 0.72f);
            barrelGo.transform.localScale = new Vector3(0.13f, 0.13f, 1.1f);

            var turretType = System.Type.GetType("CloseEncounters.Arena.ShoreTurret") ?? System.Type.GetType("ShoreTurret");
            if (turretType != null)
            {
                var comp = turretObj.AddComponent(turretType) as MonoBehaviour;
                if (comp != null)
                {
                    SetField(comp, "detectionRange", 80f);
                    SetField(comp, "fireInterval", 3.5f);
                    SetField(comp, "damagePerShot", 30);
                    SetField(comp, "barrelTransform", barrelGo.transform);
                }
            }
            else
            {
                Debug.LogWarning("[StraitOfHormuz] ShoreTurret type not found — turret spawned as static prop.");
            }
        }

        // =================================================================
        // Decor helpers — route through StraitOfHormuzDecor with fallbacks
        // =================================================================
        private void SafeAddTanker(Vector3 pos, float yRot, float scale)
        {
            if (!InvokeDecor("AddTanker", new object[] { transform, pos, yRot, scale }))
            {
                // Fallback: long gray hull
                AddBlockUnchecked(pos + new Vector3(0f, 3f, 0f), new Vector3(14f * scale, 6f * scale, 55f * scale), HullGray, "TankerHullFallback");
            }
        }

        private void SafeAddOilRig(Vector3 pos, float scale)
        {
            if (!InvokeDecor("AddOilRig", new object[] { transform, pos, scale }))
            {
                // Fallback: 4 legs + deck
                for (int i = 0; i < 4; i++)
                {
                    float lx = (i % 2 == 0) ? -12f : 12f;
                    float lz = (i / 2 == 0) ? -12f : 12f;
                    AddCylinder(pos + new Vector3(lx, 0f, lz), 1.2f, 24f * scale, HullDark, "RigLegFallback");
                }
                AddBlockUnchecked(pos + new Vector3(0f, 22f * scale, 0f), new Vector3(28f * scale, 2f, 28f * scale), HullGray, "RigDeckFallback");
            }
        }

        private void SafeAddHelicopter(Vector3 startPos, Vector3 endPos, float speed)
        {
            if (!InvokeDecor("AddHelicopter", new object[] { transform, startPos, endPos, speed }))
            {
                // Fallback: static helicopter body at midpoint
                AddBlockUnchecked((startPos + endPos) * 0.5f, new Vector3(4f, 2f, 8f), HullDark, "HelicopterFallback");
            }
        }

        private void SafeAddFlagpole(Vector3 pos, Color flagColor, string label)
        {
            if (!InvokeDecor("AddFlagpole", new object[] { transform, pos, flagColor, label }))
            {
                // Fallback: pole + flag
                AddCylinder(pos, 0.2f, 12f, LampWhite, $"FlagPole_{label}");
                AddBlockUnchecked(pos + new Vector3(1.6f, 10f, 0f), new Vector3(3f, 1.8f, 0.1f), flagColor, $"Flag_{label}");
            }
        }

        private void SafeAddLighthouse(Vector3 pos, Color lampColor)
        {
            if (!InvokeDecor("AddLighthouse", new object[] { transform, pos, lampColor }))
            {
                // Fallback: tower + lamp
                AddCylinder(pos + new Vector3(0f, 12f, 0f), 2.5f, 22f, new Color(0.95f, 0.95f, 0.90f), "LighthouseTowerFallback");
                var lamp = AddSphere(pos + new Vector3(0f, 24f, 0f), 1.4f, lampColor, "LighthouseLampFallback");
                var rend = lamp.GetComponent<MeshRenderer>();
                if (rend != null) rend.sharedMaterial = MakeEmissiveMaterial(lampColor, lampColor * 3f, "LighthouseLampEmis");
            }
        }

        private void SafeAddSearchlight(Vector3 pos, Vector3 lookDir)
        {
            if (!InvokeDecor("AddSearchlight", new object[] { transform, pos, lookDir }))
            {
                // Fallback: spotlight
                var sl = new GameObject("SearchlightFallback");
                sl.transform.SetParent(transform, false);
                sl.transform.position = pos;
                sl.transform.forward = lookDir.sqrMagnitude > 0.001f ? lookDir.normalized : Vector3.forward;
                var light = sl.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = 80f;
                light.spotAngle = 28f;
                light.color = LampWhite;
                light.intensity = 2.5f;
            }
        }

        // =================================================================
        // Reflection glue (keeps us running even if decor/agent types missing)
        // =================================================================
        private static System.Type s_decorType;
        private static bool s_decorTypeSearched;

        private static System.Type DecorType()
        {
            if (s_decorTypeSearched) return s_decorType;
            s_decorTypeSearched = true;
            s_decorType = System.Type.GetType("CloseEncounters.Arena.StraitOfHormuzDecor")
                ?? System.Type.GetType("StraitOfHormuzDecor");
            if (s_decorType == null)
                Debug.LogWarning("[StraitOfHormuz] StraitOfHormuzDecor type not found — using procedural fallbacks for decor.");
            return s_decorType;
        }

        private static bool InvokeDecor(string methodName, object[] args)
        {
            var t = DecorType();
            if (t == null) return false;
            // Resolve by name + arg count (all StraitOfHormuzDecor methods have unique arg counts).
            var methods = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == methodName && methods[i].GetParameters().Length == args.Length)
                {
                    try
                    {
                        methods[i].Invoke(null, args);
                        return true;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[StraitOfHormuz] {methodName} threw: {e.Message}");
                        return false;
                    }
                }
            }
            Debug.LogWarning($"[StraitOfHormuz] StraitOfHormuzDecor.{methodName} not found — using fallback.");
            return false;
        }

        private static void SetField(MonoBehaviour comp, string fieldName, object value)
        {
            if (comp == null) return;
            var t = comp.GetType();
            var f = t.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (f != null)
            {
                try { f.SetValue(comp, value); }
                catch (System.Exception e) { Debug.LogWarning($"[StraitOfHormuz] SetField {fieldName} failed: {e.Message}"); }
                return;
            }
            var p = t.GetProperty(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                try { p.SetValue(comp, value); }
                catch (System.Exception e) { Debug.LogWarning($"[StraitOfHormuz] SetProp {fieldName} failed: {e.Message}"); }
            }
        }

        // =================================================================
        // Primitive helper — creates and materials a child of 'parent'
        // =================================================================
        private GameObject AddChildPrimitive(Transform parent, PrimitiveType type, Vector3 localPos,
            Vector3 localScale, Color color, string label)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = label;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            SetMaterial(go, MakeMaterial(color));
            return go;
        }
    }

    // why: back-compat shim — old saves and editor tooling still reference WaterCorsairBay
    public class WaterCorsairBay : WaterStraitOfHormuz { }
}
