using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using CloseEncounters.Core;
using CloseEncounters.Vehicle;
using CloseEncounters.Combat;
using CloseEncounters.AI;
using CloseEncounters.UI;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Manages the full combat lifecycle: arena instantiation, vehicle spawning,
    /// match timer, win-condition evaluation, HUD, and results overlay.
    /// Auto-initializes from GameManager.Settings.
    /// </summary>
    public class ArenaManager : MonoBehaviour
    {
        // --- Public state ---
        public static ArenaManager Instance { get; private set; }

        public bool MatchRunning  { get; private set; }
        public float MatchTimer   { get; private set; }
        public int   WinnerPlayerId { get; private set; } = -1;

        // --- Settings ---
        private const float MatchDuration       = 300f;
        private const float GroundArenaHalfSize = 300f;
        private const float WaterArenaHalfSize  = 300f;
        private const float ResultsDelay        = 4f;

        // --- Runtime refs ---
        private ArenaBase _arena;
        private Camera _combatCamera;
        private readonly List<VehicleRuntime> _vehicles = new List<VehicleRuntime>();
        private Canvas _hudCanvas;
        private Text _timerText;
        private Text _statusText;
        private Canvas _resultsCanvas;
        private Text _resultsText;
        private MatchSettings _settings;
        private VehicleRuntime _playerRuntime;
        private PlayerVehicleController _playerController;
        private PlayerCombatInput _playerWeaponInput;
        private HUD _hud;
        public HUD Hud => _hud;
        private Canvas _healthbarCanvas;
        private readonly Dictionary<VehicleRuntime, FloatingHealthbar> _healthbars
            = new Dictionary<VehicleRuntime, FloatingHealthbar>();
        public Dictionary<VehicleRuntime, FloatingHealthbar> GetHealthbars() => _healthbars;

        // =====================================================================
        // Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Called by SceneBootstrapper immediately after AddComponent.
        /// Reads GameManager.Settings and builds the arena + vehicles.
        /// </summary>
        public void Initialize()
        {
            _settings = GameManager.Instance != null
                ? GameManager.Instance.Settings
                : new MatchSettings();

            Debug.Log($"[ArenaManager] Initializing: domain={_settings.domain}, arena={_settings.arena}, " +
                      $"players={_settings.playerCount}, ai={_settings.aiCount}");

            // Ensure VFXManager singleton exists for ParticlePack effects
            if (VFXManager.Instance == null)
                new GameObject("VFXManager").AddComponent<VFXManager>();

            EnsureEventSystem();
            SetupLighting();
            CreateCombatCamera();
            SpawnArena();

            // If arena is loading asynchronously (scene-based), the coroutine
            // handles SpawnVehicles + AttachCameraToPlayer after the scene loads.
            // For procedural arenas, _arena is already set so we proceed immediately.
            if (_arena != null)
            {
                SpawnVehicles();
                AttachCameraToPlayer();
            }

            CreateHUD();
            CreateResultsOverlay();
            StartMatch();
        }

        private void Update()
        {
            if (!MatchRunning) return;

            MatchTimer -= Time.deltaTime;
            UpdateHUD();

            if (MatchTimer <= 0f)
            {
                MatchTimer = 0f;
                EndMatchByTimeout();
                return;
            }

            CheckWinCondition();
            EnforceArenaBounds();
        }

        // =====================================================================
        // Arena Spawning
        // =====================================================================

        private void SpawnArena()
        {
            string key = (_settings.arena ?? "desert_flat").ToLowerInvariant();
            bool isWater = _settings.domain == "water" || _settings.domain == "sea";

            // Try scene-based arena first (additively loaded)
            string sceneName = "Arena_" + key;
            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                StartCoroutine(LoadArenaSceneAdditive(sceneName));
                return;
            }

            // Fallback: procedural arena
            var arenaObj = new GameObject("Arena");
            _arena = AttachArena(arenaObj, key, isWater);

            if (_arena != null)
            {
                _arena.Build();
                ApplyArenaAtmospherics(_arena);
                if (_settings.domain == "air")
                    AirArenaExpander.Apply(_arena.transform);
                EnsureMatchEndWatcher();
                Debug.Log($"[ArenaManager] Arena built (procedural): {_arena.ArenaName}");
            }
            else
            {
                Debug.LogWarning($"[ArenaManager] Unknown arena key '{key}', using empty arena");
            }
        }

        private void EnsureMatchEndWatcher()
        {
            if (GetComponent<MatchEndWatcher>() == null)
                gameObject.AddComponent<MatchEndWatcher>();
            // Also ensure minimap exists (HUD scene-level UI)
            if (GameObject.FindAnyObjectByType<CloseEncounters.UI.MinimapController>() == null)
            {
                var mm = new GameObject("[Minimap]");
                mm.AddComponent<CloseEncounters.UI.MinimapController>();
            }
        }

        private System.Collections.IEnumerator LoadArenaSceneAdditive(string sceneName)
        {
            var asyncLoad = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
                sceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            yield return asyncLoad;

            // Find SceneBasedArena in the loaded scene
            _arena = FindAnyObjectByType<SceneBasedArena>();
            if (_arena != null)
            {
                _arena.Build();
                ApplyArenaAtmospherics(_arena);
                if (_settings.domain == "air")
                    AirArenaExpander.Apply(_arena.transform);
                Debug.Log($"[ArenaManager] Arena loaded (scene): {_arena.ArenaName}");
            }
            else
            {
                Debug.LogWarning($"[ArenaManager] No SceneBasedArena found in '{sceneName}'");
            }

            // Continue initialization that was waiting for the arena
            SpawnVehicles();
            AttachCameraToPlayer();
        }

        private static void ApplyArenaAtmospherics(ArenaBase arena)
        {
            if (arena == null) return;
            if (!string.IsNullOrEmpty(arena.SkyboxResourceName))
                ArenaSkyboxes.Apply(arena.SkyboxResourceName);
            PostFXBootstrap.Apply(arena.PostFX);
            if (!string.IsNullOrEmpty(arena.AmbientAudioResourcePath))
                CloseEncounters.Combat.AudioFX.CreateAmbientLoop(arena.transform, arena.AmbientAudioResourcePath, 0.35f);
        }

        private static ArenaBase AttachArena(GameObject obj, string key, bool isWater)
        {
            if (isWater)
            {
                switch (key)
                {
                    case "archipelago":      return obj.AddComponent<WaterArchipelago>();
                    case "titans_peak":      return obj.AddComponent<WaterTitansPeak>();
                    case "frozen_strait":    return obj.AddComponent<WaterFrozenStrait>();
                    case "kraken_lair":      return obj.AddComponent<WaterKrakenLair>();
                    case "corsair_bay":
                    case "strait_of_hormuz": return obj.AddComponent<WaterStraitOfHormuz>();
                    default:                 return obj.AddComponent<WaterArchipelago>();
                }
            }

            switch (key)
            {
                case "desert_flat":
                case "desert":           return obj.AddComponent<GroundDesert>();
                case "town":
                case "fentchester":      return obj.AddComponent<GroundTown>();
                case "arctic":
                case "canada":           return obj.AddComponent<GroundArctic>();
                case "volcanic":
                case "florida":          return obj.AddComponent<GroundVolcanic>();
                case "highlands":
                case "kyrgyzstan":       return obj.AddComponent<GroundHighlands>();
                default:                 return obj.AddComponent<GroundDesert>();
            }
        }

        // =====================================================================
        // Vehicle Spawning
        // =====================================================================

        private void SpawnVehicles()
        {
            List<Transform> spawnPoints = _arena != null
                ? _arena.SpawnPoints
                : GenerateFallbackSpawns();

            int totalVehicles = 1 + _settings.aiCount;
            int spawnIndex = 0;

            // --- Player vehicle ---
            VehicleData playerData = LoadPlayerVehicle();
            SpawnVehicle(playerData, spawnPoints[spawnIndex % spawnPoints.Count], 0, false);
            spawnIndex++;

            // --- AI vehicles ---
            for (int i = 0; i < _settings.aiCount; i++)
            {
                VehicleData aiData = GenerateAIVehicle(i);
                SpawnVehicle(aiData, spawnPoints[spawnIndex % spawnPoints.Count], i + 1, true);
                spawnIndex++;
            }

            Debug.Log($"[ArenaManager] Spawned {_vehicles.Count} vehicles ({1} player, {_settings.aiCount} AI)");
        }

        private void SpawnVehicle(VehicleData data, Transform spawnPoint, int playerId, bool isAI)
        {
            var vehObj = new GameObject($"Vehicle_{(isAI ? "AI" : "Player")}_{playerId}");
            bool isWater = _settings.domain == "water" || _settings.domain == "sea";
            Vector3 spawnPos;

            if (isWater)
            {
                // Water vehicles spawn at water surface level (y=1)
                spawnPos = new Vector3(spawnPoint.position.x, 1f, spawnPoint.position.z);
            }
            else
            {
                // Ground vehicles: use terrain sampling (reliable) then raycast as backup
                float groundY = 0f;
                var activeTerrain = Terrain.activeTerrain;
                if (activeTerrain != null)
                {
                    groundY = activeTerrain.SampleHeight(spawnPoint.position) + activeTerrain.transform.position.y;
                }
                else
                {
                    // Fallback: raycast (may hit prefabs instead of terrain)
                    Vector3 rayOrigin = new Vector3(spawnPoint.position.x, 200f, spawnPoint.position.z);
                    if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 400f, ~0, QueryTriggerInteraction.Ignore))
                        groundY = hit.point.y;
                }
                spawnPos = new Vector3(spawnPoint.position.x, groundY + 2f, spawnPoint.position.z);
            }
            vehObj.transform.position = spawnPos;
            // Don't rotate root — we remap part positions so the builder's
            // "front" direction aligns with local +Z (transform.forward).

            var runtime = vehObj.AddComponent<VehicleRuntime>();
            runtime.Initialize(data, playerId, isAI);

            // Compute grid centroid so parts are centered on the vehicle origin
            Vector3 centroid = Vector3.zero;
            int partCount = 0;
            foreach (var e in data.parts)
            {
                centroid += new Vector3(
                    e.gridPosition.Length > 0 ? e.gridPosition[0] : 0,
                    e.gridPosition.Length > 1 ? e.gridPosition[1] : 0,
                    e.gridPosition.Length > 2 ? e.gridPosition[2] : 0);
                partCount++;
            }
            if (partCount > 0) centroid /= partCount;

            // Rotation that remaps the builder's forward direction to local +Z.
            // If forwardAngle=0 (+Z is front), no rotation needed.
            // If forwardAngle=90 (+X is front), rotate parts by -90° so +X → +Z.
            Quaternion remap = Quaternion.Euler(0f, -data.forwardAngle, 0f);

            // Build part nodes
            if (PartRegistry.Instance != null)
            {
                foreach (var entry in data.parts)
                {
                    PartData partData = PartRegistry.Instance.GetPart(entry.id);
                    if (partData == null) continue;

                    var partObj = new GameObject($"Part_{entry.id}");
                    partObj.transform.SetParent(vehObj.transform, false);

                    Vector3Int gp = new Vector3Int(
                        entry.gridPosition.Length > 0 ? entry.gridPosition[0] : 0,
                        entry.gridPosition.Length > 1 ? entry.gridPosition[1] : 0,
                        entry.gridPosition.Length > 2 ? entry.gridPosition[2] : 0
                    );
                    // Center on grid, then rotate so builder "front" = local +Z
                    Vector3 centered = new Vector3(gp.x, gp.y, gp.z) - centroid;
                    partObj.transform.localPosition = remap * centered;

                    var node = partObj.AddComponent<PartNode>();

                    // Restore armor orientation from saved data
                    if (entry.armorFace != null && entry.armorFace.Length >= 3)
                        node.armorFace = new Vector3Int(entry.armorFace[0], entry.armorFace[1], entry.armorFace[2]);

                    node.Setup(partData, gp);
                    runtime.PartNodes.Add(node);
                }
            }

            // Physics
            var rb = vehObj.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(50f, data.CalculateTotalMass());
            rb.linearDamping = 1f;
            rb.angularDamping = 2f;

            var col = vehObj.AddComponent<BoxCollider>();
            col.size = new Vector3(4f, 2f, 6f);
            col.center = new Vector3(0f, 1f, 0f);

            if (isAI)
            {
                // AI vehicles use physics controllers + AI brain
                if (isWater)
                {
                    vehObj.AddComponent<CloseEncounters.VehiclePhysics.WaterPhysics>();
                    rb.useGravity = false;
                    rb.constraints = RigidbodyConstraints.None;
                }
                else
                {
                    // Same physics setup as player ground vehicles
                    rb.useGravity = true;
                    rb.linearDamping = 1f;
                    rb.angularDamping = 5f;
                    rb.centerOfMass = new Vector3(0f, -1f, 0f);
                }

                var aiCtrl = vehObj.AddComponent<AIController>();
                AIDifficultyLevel difficulty = MapAIDifficulty(_settings.aiDifficulty);
                aiCtrl.SetDifficulty(difficulty);
                aiCtrl.SetDomain(_settings.domain);

                float halfSize = isWater ? WaterArenaHalfSize : GroundArenaHalfSize;
                // Ground/water AIs get tight Y boundary, air gets full height
                float yHalf = _settings.domain == "air" ? 100f : 20f;
                aiCtrl.arenaHalfSize = new Vector3(halfSize, yHalf, halfSize);

                // Initialize propulsion and fuel degradation tracking for AI
                int aiFuelCount = 0;
                int aiPropulsionCount = 0;
                for (int p = 0; p < runtime.PartNodes.Count; p++)
                {
                    var pn = runtime.PartNodes[p];
                    if (pn.partData == null) continue;
                    if (pn.partData.id.Contains("fuel") || (pn.partData.subcategory != null && pn.partData.subcategory.Contains("fuel")))
                        aiFuelCount++;
                    if (string.Equals(pn.partData.category, "propulsion", System.StringComparison.OrdinalIgnoreCase))
                        aiPropulsionCount++;
                }
                aiCtrl.InitBoost(aiFuelCount);
                aiCtrl.InitPropulsionTracking(aiPropulsionCount);

                // Initialize propulsion tracking on physics controllers too
                var aiWaterPhys = vehObj.GetComponent<CloseEncounters.VehiclePhysics.WaterPhysics>();
                if (aiWaterPhys != null)
                    aiWaterPhys.InitPropulsionTracking(aiPropulsionCount);
                var aiGroundPhys = vehObj.GetComponent<CloseEncounters.VehiclePhysics.GroundPhysics>();
                if (aiGroundPhys != null)
                    aiGroundPhys.InitPropulsionTracking(aiPropulsionCount);
            }
            else
            {
                // Player vehicle: combined WASD + camera + aiming controller
                _playerRuntime = runtime;

                // Water domain: add WaterPhysics for buoyancy and boat movement
                if (isWater)
                {
                    var wp = vehObj.AddComponent<CloseEncounters.VehiclePhysics.WaterPhysics>();
                    // WaterPhysics manages its own gravity, damping, and constraints
                    rb.useGravity = false;
                    rb.angularDamping = 2f;
                    rb.constraints = RigidbodyConstraints.None;

                    // Ensure WaveManager exists
                    if (CloseEncounters.VehiclePhysics.WaveManager.Instance == null)
                    {
                        var wmObj = new GameObject("WaveManager");
                        wmObj.AddComponent<CloseEncounters.VehiclePhysics.WaveManager>();
                    }
                }

                var playerCtrl = vehObj.AddComponent<PlayerVehicleController>();
                _playerController = playerCtrl;

                // Initialize boost fuel from fuel tank count (matching Godot: 3s base + 2s per tank)
                // and propulsion/fuel degradation tracking
                int fuelTankCount = 0;
                int propulsionCount = 0;
                for (int p = 0; p < runtime.PartNodes.Count; p++)
                {
                    var pn = runtime.PartNodes[p];
                    if (pn.partData == null) continue;
                    if (pn.partData.id.Contains("fuel") || (pn.partData.subcategory != null && pn.partData.subcategory.Contains("fuel")))
                        fuelTankCount++;
                    if (string.Equals(pn.partData.category, "propulsion", System.StringComparison.OrdinalIgnoreCase))
                        propulsionCount++;
                }
                playerCtrl.InitBoost(fuelTankCount);
                playerCtrl.InitFuelTracking(fuelTankCount);
                playerCtrl.InitPropulsionTracking(propulsionCount);

                // Initialize propulsion tracking on physics controllers too
                var playerWaterPhys = vehObj.GetComponent<CloseEncounters.VehiclePhysics.WaterPhysics>();
                if (playerWaterPhys != null)
                    playerWaterPhys.InitPropulsionTracking(propulsionCount);

                var weaponInput = vehObj.AddComponent<PlayerCombatInput>();
                weaponInput.Initialize(runtime, playerId);
                _playerWeaponInput = weaponInput;
            }

            _vehicles.Add(runtime);

            // Floating healthbar for every vehicle
            CreateFloatingHealthbar(runtime, vehObj.transform, playerId);
        }

        private VehicleData LoadPlayerVehicle()
        {
            // 1. Use the vehicle stored directly in MatchSettings (set by BuilderUI)
            if (_settings.playerVehicle != null && _settings.playerVehicle.parts.Count > 0)
            {
                Debug.Log($"[ArenaManager] Using player vehicle from MatchSettings: '{_settings.playerVehicle.name}' ({_settings.playerVehicle.parts.Count} parts)");
                return _settings.playerVehicle;
            }

            // 2. Try loading the temp combat vehicle saved by BuilderUI
            VehicleData combatVehicle = VehicleSerializer.Load("__combat_vehicle__");
            if (combatVehicle != null && combatVehicle.parts.Count > 0)
            {
                Debug.Log($"[ArenaManager] Loaded player vehicle from combat save: '{combatVehicle.name}' ({combatVehicle.parts.Count} parts)");
                return combatVehicle;
            }

            // 3. Try any saved vehicle on disk
            string[] saved = VehicleSerializer.ListSavedVehicles();
            for (int i = 0; i < saved.Length; i++)
            {
                if (saved[i] == "__combat_vehicle__") continue;
                VehicleData loaded = VehicleSerializer.Load(saved[i]);
                if (loaded != null && loaded.parts.Count > 0) return loaded;
            }

            // 4. Fallback: build a minimal test vehicle with a cockpit + frame + wheels + gun
            Debug.LogWarning("[ArenaManager] No player vehicle found, building fallback test vehicle");
            return BuildFallbackVehicle();
        }

        private VehicleData BuildFallbackVehicle()
        {
            var data = new VehicleData { name = "Fallback", domain = _settings.domain };

            // Try to build one via AIBuilder (reuses part registry)
            AIDifficultyLevel fallbackDifficulty = AIDifficultyLevel.Easy;
            VehicleData aiBuilt = AIBuilder.BuildVehicle(_settings.domain, _settings.budget, fallbackDifficulty);
            if (aiBuilt != null && aiBuilt.parts.Count > 0)
                return aiBuilt;

            // If PartRegistry is empty or unavailable, return an empty vehicle
            // (SpawnVehicle will still create a Rigidbody box so the match can run)
            return data;
        }

        private VehicleData GenerateAIVehicle(int index)
        {
            AIDifficultyLevel difficulty = MapAIDifficulty(_settings.aiDifficulty);
            VehicleData data = AIBuilder.BuildVehicle(_settings.domain, _settings.budget, difficulty);

            if (data != null && data.parts.Count > 0)
                return data;

            // Retry with each difficulty level
            Debug.LogWarning($"[ArenaManager] AIBuilder failed for AI #{index} at {difficulty}, retrying...");
            AIDifficultyLevel[] fallbacks = { AIDifficultyLevel.Easy, AIDifficultyLevel.Medium, AIDifficultyLevel.Hard };
            for (int d = 0; d < fallbacks.Length; d++)
            {
                data = AIBuilder.BuildVehicle(_settings.domain, _settings.budget, fallbacks[d]);
                if (data != null && data.parts.Count > 0)
                {
                    Debug.Log($"[ArenaManager] AI #{index} retry succeeded at {fallbacks[d]}");
                    return data;
                }
            }

            // Last resort: hardcoded minimal vehicle that always works
            Debug.LogWarning($"[ArenaManager] All AIBuilder attempts failed for AI #{index}, building hardcoded minimum");
            var fallbackData = new VehicleData
            {
                name = $"AI_Fallback_{index}",
                domain = _settings.domain
            };

            // Minimum viable vehicle: control + structure + propulsion + weapon
            string controlId = _settings.domain == "water" ? "bridge" : "cockpit";
            string structId = _settings.domain == "water" ? "hull_plate" : "light_frame";
            string propId = _settings.domain == "water" ? "marine_propeller" : "small_wheel";
            string weapId = "machine_gun";

            fallbackData.parts.Add(new PartEntry(controlId, new int[] { 0, 0, 0 }));
            fallbackData.parts.Add(new PartEntry(structId, new int[] { 1, 0, 0 }));
            fallbackData.parts.Add(new PartEntry(structId, new int[] { -1, 0, 0 }));
            fallbackData.parts.Add(new PartEntry(structId, new int[] { 0, 0, 1 }));
            fallbackData.parts.Add(new PartEntry(structId, new int[] { 0, 0, -1 }));
            fallbackData.parts.Add(new PartEntry(propId, new int[] { 0, 0, -2 }));
            fallbackData.parts.Add(new PartEntry(propId, new int[] { 1, 0, -2 }));
            fallbackData.parts.Add(new PartEntry(weapId, new int[] { 0, 1, 0 }));

            return fallbackData;
        }

        private static AIDifficultyLevel MapAIDifficulty(int level)
        {
            switch (level)
            {
                case 0:  return AIDifficultyLevel.Easy;
                case 1:  return AIDifficultyLevel.Medium;
                case 2:  return AIDifficultyLevel.Hard;
                default: return (AIDifficultyLevel)Mathf.Clamp(level, 0, 2);
            }
        }

        private List<Transform> GenerateFallbackSpawns()
        {
            var spawns = new List<Transform>();
            float radius = 60f;
            int count = Mathf.Max(4, _settings.playerCount + _settings.aiCount);
            for (int i = 0; i < count; i++)
            {
                float angle = (360f / count) * i * Mathf.Deg2Rad;
                var sp = new GameObject($"FallbackSpawn_{i}").transform;
                sp.SetParent(transform, false);
                sp.position = new Vector3(Mathf.Cos(angle) * radius, 0.5f, Mathf.Sin(angle) * radius);
                sp.LookAt(Vector3.zero);
                spawns.Add(sp);
            }
            return spawns;
        }

        // =====================================================================
        // Match Flow
        // =====================================================================

        private void StartMatch()
        {
            MatchTimer = MatchDuration;
            MatchRunning = true;
            WinnerPlayerId = -1;
            Debug.Log("[ArenaManager] Match started!");
        }

        private bool _matchEndTriggered;

        private void CheckWinCondition()
        {
            if (_matchEndTriggered) return; // prevent double-trigger

            int aliveCount = 0;
            int lastAliveId = -1;

            for (int i = 0; i < _vehicles.Count; i++)
            {
                if (_vehicles[i] != null && _vehicles[i].IsAlive)
                {
                    aliveCount++;
                    lastAliveId = _vehicles[i].PlayerId;
                }
            }

            if (aliveCount <= 1)
            {
                _matchEndTriggered = true;
                WinnerPlayerId = lastAliveId;
                MatchRunning = false;
                Debug.Log($"[ArenaManager] Match ended! Alive={aliveCount}, Last alive: Player {WinnerPlayerId}");
                StartCoroutine(ShowResultsAfterDelay());
            }
        }

        private void EndMatchByTimeout()
        {
            MatchRunning = false;

            // Winner = vehicle with highest remaining HP
            int bestHp = -1;
            int bestId = -1;
            for (int i = 0; i < _vehicles.Count; i++)
            {
                if (_vehicles[i] == null || !_vehicles[i].IsAlive) continue;
                int hp = _vehicles[i].TotalHP;
                if (hp > bestHp)
                {
                    bestHp = hp;
                    bestId = _vehicles[i].PlayerId;
                }
            }

            WinnerPlayerId = bestId;
            Debug.Log($"[ArenaManager] Match timed out! Winner by HP: Player {WinnerPlayerId}");
            StartCoroutine(ShowResultsAfterDelay());
        }

        private IEnumerator ShowResultsAfterDelay()
        {
            Debug.Log($"[ArenaManager] ShowResultsAfterDelay triggered. Winner={WinnerPlayerId}");

            // Show VICTORY/DEFEATED on HUD immediately
            bool playerWon = _playerRuntime != null && _playerRuntime.IsAlive;
            if (_hud != null)
                _hud.ShowGameOver(playerWon ? "VICTORY!" : "DEFEATED");

            // Disable player controller FIRST so it stops fighting for cursor/input
            var ctrl = _playerRuntime != null
                ? _playerRuntime.GetComponent<PlayerVehicleController>() : null;
            if (ctrl != null) ctrl.enabled = false;

            // Also disable combat input so weapons stop firing
            var combatInput = _playerRuntime != null
                ? _playerRuntime.GetComponent<PlayerCombatInput>() : null;
            if (combatInput != null) combatInput.enabled = false;

            // Release mouse cursor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Wait for results delay while wreckage burns
            yield return new WaitForSeconds(ResultsDelay);

            // Freeze all remaining vehicles
            for (int i = 0; i < _vehicles.Count; i++)
            {
                if (_vehicles[i] == null) continue;
                var rb = _vehicles[i].GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
            }

            // Pause the game
            Time.timeScale = 0f;

            ShowResults();
        }

        private void ShowResults()
        {
            // Hide the simple results canvas (we'll use the full ResultsUI)
            if (_resultsCanvas != null)
                _resultsCanvas.gameObject.SetActive(false);

            string winnerName = WinnerPlayerId >= 0
                ? (GameManager.Instance != null
                    ? GameManager.Instance.GetPlayerName(WinnerPlayerId)
                    : $"Player {WinnerPlayerId}")
                : "Nobody";

            // Create proper results overlay (matching Godot)
            var resultsCanvasObj = new GameObject("ResultsOverlayCanvas");
            var rCanvas = resultsCanvasObj.AddComponent<Canvas>();
            rCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rCanvas.sortingOrder = 50;
            var rScaler = resultsCanvasObj.AddComponent<CanvasScaler>();
            rScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            rScaler.referenceResolution = new Vector2(1920f, 1080f);
            rScaler.matchWidthOrHeight = 0.5f;
            resultsCanvasObj.AddComponent<GraphicRaycaster>();

            var resultsUI = resultsCanvasObj.AddComponent<ResultsUI>();
            resultsUI.Initialize();

            // Determine outcome
            bool playerWon = _playerRuntime != null && _playerRuntime.IsAlive;
            ResultsUI.Outcome outcome;
            if (WinnerPlayerId < 0)
                outcome = ResultsUI.Outcome.Draw;
            else if (playerWon)
                outcome = ResultsUI.Outcome.Victory;
            else
                outcome = ResultsUI.Outcome.Defeated;

            // Build stats rows with real combat data
            float matchTime = MatchDuration - MatchTimer;
            var rows = new List<ResultsUI.StatRow>();
            for (int i = 0; i < _vehicles.Count; i++)
            {
                var v = _vehicles[i];
                if (v == null) continue;
                string name = GameManager.Instance != null
                    ? GameManager.Instance.GetPlayerName(v.PlayerId)
                    : $"Vehicle {v.PlayerId}";
                rows.Add(new ResultsUI.StatRow
                {
                    playerName = name,
                    survived = v.IsAlive,
                    damageDealt = v.DamageDealt,
                    damageReceived = v.DamageReceived,
                    shotsFired = v.ShotsFired,
                    shotsHit = v.ShotsHit,
                    partsDestroyedOnEnemy = v.PartsDestroyedOnEnemy,
                    partsLost = v.PartsLost,
                    distanceTraveled = v.DistanceTraveled,
                    topSpeed = v.TopSpeed,
                });
            }

            resultsUI.SetResults(outcome, winnerName, rows, matchTime);

            if (GameManager.Instance != null)
                GameManager.Instance.SetState(GameState.Results);

            Debug.Log($"[ArenaManager] Results shown. Winner: {winnerName}");
        }

        // =====================================================================
        // Arena Bounds
        // =====================================================================

        private void EnforceArenaBounds()
        {
            bool isWater = _settings.domain == "water" || _settings.domain == "sea";
            float halfSize = isWater ? WaterArenaHalfSize : GroundArenaHalfSize;

            for (int i = 0; i < _vehicles.Count; i++)
            {
                if (_vehicles[i] == null) continue;

                Vector3 pos = _vehicles[i].transform.position;
                bool outOfBounds = Mathf.Abs(pos.x) > halfSize ||
                                   Mathf.Abs(pos.z) > halfSize;

                if (outOfBounds)
                {
                    pos.x = Mathf.Clamp(pos.x, -halfSize, halfSize);
                    pos.z = Mathf.Clamp(pos.z, -halfSize, halfSize);
                    _vehicles[i].transform.position = pos;

                    var rb = _vehicles[i].GetComponent<Rigidbody>();
                    if (rb != null) rb.linearVelocity *= 0.5f;
                }
            }
        }

        // =====================================================================
        // Camera
        // =====================================================================

        private void CreateCombatCamera()
        {
            var camObj = new GameObject("CombatCamera");
            camObj.tag = "MainCamera";
            _combatCamera = camObj.AddComponent<Camera>();
            _combatCamera.clearFlags = CameraClearFlags.Skybox;
            _combatCamera.backgroundColor = new Color(0.4f, 0.6f, 0.9f);
            _combatCamera.fieldOfView = 60f;
            _combatCamera.nearClipPlane = 0.5f;
            _combatCamera.farClipPlane = 1000f;

            // Default overhead position; a follow script will update later
            camObj.transform.position = new Vector3(0f, 80f, -60f);
            camObj.transform.rotation = Quaternion.Euler(50f, 0f, 0f);

            // Only add AudioListener if none exists yet
            if (FindAnyObjectByType<AudioListener>() == null)
                camObj.AddComponent<AudioListener>();
        }

        private void AttachCameraToPlayer()
        {
            // PlayerVehicleController manages its own camera now.
            // Destroy the ArenaManager-created camera so there's no conflict.
            if (_combatCamera != null)
            {
                Destroy(_combatCamera.gameObject);
                _combatCamera = null;
            }
            Debug.Log("[ArenaManager] Player uses PlayerVehicleController camera");
        }

        // =====================================================================
        // HUD
        // =====================================================================

        private void CreateHUD()
        {
            // Use existing HUD if SceneBootstrapper already created one
            _hud = FindAnyObjectByType<HUD>();

            var canvasObj = new GameObject("CombatHUD");
            _hudCanvas = canvasObj.AddComponent<Canvas>();
            _hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _hudCanvas.sortingOrder = 10;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // Timer
            var timerObj = CreateTextElement(canvasObj.transform, "TimerText",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f),
                new Vector2(300f, 60f), 36, TextAnchor.UpperCenter);
            _timerText = timerObj.GetComponent<Text>();

            // Status
            var statusObj = CreateTextElement(canvasObj.transform, "StatusText",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f),
                new Vector2(600f, 50f), 24, TextAnchor.LowerCenter);
            _statusText = statusObj.GetComponent<Text>();

            // Create HUD (health bar, crosshair, etc.) on a separate high-order canvas
            if (_hud == null)
            {
                var hudCanvasObj = new GameObject("HUDOverlay");
                var hCanvas = hudCanvasObj.AddComponent<Canvas>();
                hCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                hCanvas.sortingOrder = 12;
                var hScaler = hudCanvasObj.AddComponent<CanvasScaler>();
                hScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                hScaler.referenceResolution = new Vector2(1920f, 1080f);
                hScaler.matchWidthOrHeight = 0.5f;
                hudCanvasObj.AddComponent<GraphicRaycaster>();

                _hud = hudCanvasObj.AddComponent<HUD>();
            }

            if (_hud != null && _playerRuntime != null)
            {
                _hud.Setup(_playerRuntime);
                _hud.SetupVehiclePreview(_playerRuntime.transform);
            }
        }

        private void UpdateHUD()
        {
            if (_timerText != null)
            {
                int mins = Mathf.FloorToInt(MatchTimer / 60f);
                int secs = Mathf.FloorToInt(MatchTimer % 60f);
                _timerText.text = $"{mins:00}:{secs:00}";
                _timerText.color = MatchTimer < 30f ? Color.red : Color.white;
            }

            if (_statusText != null)
            {
                int alive = 0;
                for (int i = 0; i < _vehicles.Count; i++)
                    if (_vehicles[i] != null && _vehicles[i].IsAlive) alive++;
                _statusText.text = $"Vehicles alive: {alive}/{_vehicles.Count}";
            }

            // Feed player HP/ammo/speed to HUD
            if (_hud != null && _playerRuntime != null && _playerRuntime.IsAlive)
            {
                int currentHp = _playerRuntime.TotalHP;
                int maxHp = _playerRuntime.MaxHP;
                float normalized = maxHp > 0 ? (float)currentHp / maxHp : 0f;
                _hud.SetHealth(normalized, currentHp, maxHp);

                if (_playerController != null)
                    _hud.SetSpeed(_playerController.Speed);
                else
                {
                    var rb = _playerRuntime.GetComponent<Rigidbody>();
                    if (rb != null) _hud.SetSpeed(rb.linearVelocity.magnitude);
                }

                if (_playerWeaponInput != null)
                    _hud.SetAmmo(_playerWeaponInput.GetActiveWeaponName(),
                        _playerWeaponInput.GetTotalAmmo(), 0);
                else
                    _hud.SetAmmo("", _playerRuntime.TotalAmmo, 0);

                // Boost bar
                if (_playerController != null && _playerController.MaxBoostFuel > 0f)
                {
                    _hud.SetBoost(_playerController.BoostFuel / _playerController.MaxBoostFuel);
                    _hud.SetBoostLabel(_playerController.IsBoosting ? "BOOSTING" : "BOOST");
                }
            }

            // Update floating healthbars
            for (int i = 0; i < _vehicles.Count; i++)
            {
                var v = _vehicles[i];
                if (v == null) continue;
                if (_healthbars.TryGetValue(v, out var fhb) && fhb != null)
                {
                    if (!v.IsAlive)
                    {
                        fhb.ForceZero();
                        fhb.gameObject.SetActive(false);
                    }
                    else
                        fhb.UpdateHP(v.TotalHP, v.MaxHP);
                }
            }
        }

        private void CreateFloatingHealthbar(VehicleRuntime runtime, Transform target, int playerId)
        {
            // Create a shared canvas for all floating healthbars (once)
            if (_healthbarCanvas == null)
            {
                var canvasObj = new GameObject("HealthbarCanvas");
                _healthbarCanvas = canvasObj.AddComponent<Canvas>();
                _healthbarCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _healthbarCanvas.sortingOrder = 5;
                var scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            string name = GameManager.Instance != null
                ? GameManager.Instance.GetPlayerName(playerId)
                : $"Vehicle {playerId}";

            var hbGo = new GameObject($"HealthBar_{playerId}", typeof(RectTransform));
            hbGo.transform.SetParent(_healthbarCanvas.transform, false);
            var hb = hbGo.AddComponent<FloatingHealthbar>();
            hb.Setup(target, name, runtime.MaxHP);
            _healthbars[runtime] = hb;
        }

        // =====================================================================
        // Results overlay
        // =====================================================================

        private void CreateResultsOverlay()
        {
            var canvasObj = new GameObject("ResultsOverlay");
            _resultsCanvas = canvasObj.AddComponent<Canvas>();
            _resultsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _resultsCanvas.sortingOrder = 20;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // Dim background
            var bgObj = new GameObject("ResultsBG");
            bgObj.transform.SetParent(canvasObj.transform, false);
            var bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.7f);

            // Results text
            var textObj = CreateTextElement(canvasObj.transform, "ResultsText",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(800f, 400f), 48, TextAnchor.MiddleCenter);
            _resultsText = textObj.GetComponent<Text>();
            _resultsText.text = "";

            canvasObj.SetActive(false);
        }

        // =====================================================================
        // Lighting
        // =====================================================================

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esObj = new GameObject("EventSystem");
                esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        private void SetupLighting()
        {
            // Directional light
            var lightObj = new GameObject("DirectionalLight");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.84f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Ambient
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.6f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.5f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.25f);

            // Skybox — use the default procedural skybox
            var skyboxMat = RenderSettings.skybox;
            if (skyboxMat == null)
            {
                var skyShader = Shader.Find("Skybox/Procedural");
                if (skyShader != null)
                {
                    skyboxMat = new Material(skyShader);
                    skyboxMat.SetFloat("_SunSize", 0.04f);
                    skyboxMat.SetFloat("_AtmosphereThickness", 1.0f);
                    skyboxMat.SetColor("_SkyTint", new Color(0.5f, 0.5f, 0.5f));
                    skyboxMat.SetColor("_GroundColor", new Color(0.37f, 0.35f, 0.34f));
                    skyboxMat.SetFloat("_Exposure", 1.3f);
                    RenderSettings.skybox = skyboxMat;
                }
            }
        }

        // =====================================================================
        // UI Helpers
        // =====================================================================

        private static GameObject CreateTextElement(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position,
            Vector2 size, int fontSize, TextAnchor alignment)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = obj.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;

            var outline = obj.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2f, -2f);

            return obj;
        }

        // =====================================================================
        // Public accessors
        // =====================================================================

        public List<VehicleRuntime> GetVehicles() => _vehicles;

        public VehicleRuntime GetPlayerVehicle()
        {
            for (int i = 0; i < _vehicles.Count; i++)
                if (!_vehicles[i].IsAI) return _vehicles[i];
            return null;
        }

        /// <summary>
        /// Switch to a spectator camera following the next alive vehicle.
        /// Called when the player vehicle dies but the match is still running.
        /// </summary>
        public void StartSpectating(Transform deadVehicle)
        {
            // Find next alive vehicle to spectate
            VehicleRuntime spectateTarget = null;
            for (int i = 0; i < _vehicles.Count; i++)
            {
                if (_vehicles[i] != null && _vehicles[i].IsAlive && _vehicles[i].PlayerId != 0)
                {
                    spectateTarget = _vehicles[i];
                    break;
                }
            }
            if (spectateTarget == null) return;

            // Destroy dead player's camera and audio listener
            var playerCam = deadVehicle.GetComponentInChildren<Camera>();
            if (playerCam != null) Destroy(playerCam.gameObject);
            var listener = deadVehicle.GetComponentInChildren<AudioListener>();
            if (listener != null) Destroy(listener);

            // Create spectator controller (handles camera switching and cycling)
            var specObj = new GameObject("SpectatorController");
            var spec = specObj.AddComponent<CloseEncounters.Combat.SpectatorCamera>();
            spec.SetTarget(spectateTarget);

            // Hide vehicles alive counter
            if (_statusText != null)
                _statusText.gameObject.SetActive(false);

            // Switch HUD to spectator mode
            string targetName = spectateTarget.IsAI ? $"AI {spectateTarget.PlayerId}" : $"Player {spectateTarget.PlayerId}";
            if (_hud != null)
                _hud.EnterSpectatorMode(targetName);
        }
    }

    // =========================================================================
    // VehicleRuntime -- lightweight runtime wrapper for a spawned vehicle
    // =========================================================================

    public class VehicleRuntime : MonoBehaviour
    {
        // Registry so AI doesn't call FindObjectsByType<VehicleRuntime> every reaction tick.
        private static readonly List<VehicleRuntime> _liveInstances = new List<VehicleRuntime>();
        public static IReadOnlyList<VehicleRuntime> LiveInstances => _liveInstances;

        private void OnEnable()  { if (!_liveInstances.Contains(this)) _liveInstances.Add(this); }
        private void OnDisable() { _liveInstances.Remove(this); }

        public int PlayerId      { get; private set; }
        public bool IsAI         { get; private set; }
        public bool IsAlive      { get; private set; } = true;
        public VehicleData Data  { get; private set; }
        public List<PartNode> PartNodes { get; private set; } = new List<PartNode>();

        // ── Combat stats (tracked during match, read by results screen) ──
        public int DamageDealt;
        public int DamageReceived;
        public int ShotsFired;
        public int ShotsHit;
        public int PartsDestroyedOnEnemy;
        public int PartsLost;
        public float DistanceTraveled;
        public float TopSpeed;
        private Vector3 _prevPosition;

        public int TotalHP
        {
            get
            {
                int hp = 0;
                for (int i = 0; i < PartNodes.Count; i++)
                    if (!PartNodes[i].isDestroyed) hp += PartNodes[i].currentHp;
                return hp;
            }
        }

        public int MaxHP
        {
            get
            {
                int hp = 0;
                for (int i = 0; i < PartNodes.Count; i++)
                    hp += PartNodes[i].partData != null ? PartNodes[i].partData.hp : 0;
                return hp;
            }
        }

        public int TotalAmmo
        {
            get
            {
                int ammo = 0;
                for (int i = 0; i < PartNodes.Count; i++)
                {
                    if (PartNodes[i].isDestroyed || PartNodes[i].partData == null) continue;
                    ammo += PartNodes[i].partData.GetStat<int>("ammo", 0);
                }
                return ammo;
            }
        }

        public void Initialize(VehicleData data, int playerId, bool isAI)
        {
            Data = data;
            PlayerId = playerId;
            IsAI = isAI;
        }

        private void Update()
        {
            if (!IsAlive) return;

            // Track distance and top speed
            Vector3 pos = transform.position;
            if (_prevPosition != Vector3.zero)
            {
                float delta = Vector3.Distance(pos, _prevPosition);
                DistanceTraveled += delta;
            }
            _prevPosition = pos;

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                float spd = rb.linearVelocity.magnitude;
                if (spd > TopSpeed) TopSpeed = spd;
            }

            // Check if control module is destroyed (cockpit/bridge)
            bool controlAlive = false;
            bool anyAlive = false;
            for (int i = 0; i < PartNodes.Count; i++)
            {
                if (PartNodes[i].isDestroyed) continue;
                anyAlive = true;
                if (PartNodes[i].partData != null &&
                    string.Equals(PartNodes[i].partData.category, "control",
                        System.StringComparison.OrdinalIgnoreCase))
                    controlAlive = true;
            }

            // Die if control module destroyed OR all parts gone
            if ((!controlAlive || !anyAlive) && PartNodes.Count > 0)
            {
                Die();
            }
        }

        /// <summary>
        /// Kill the vehicle — cinematic destruction sequence matching Godot exactly.
        /// Staggered explosions, each part ejected as debris, persistent fire.
        /// </summary>
        public void Die()
        {
            if (!IsAlive) return;
            IsAlive = false;

            // Notify the match (HUD kill feed, end-of-match watcher)
            try { CloseEncounters.Combat.DamageSystem.FireVehicleKilled(this, null); } catch { }

            // ParticlePack VFX: big explosion and persistent fire at death
            VFXManager.BigExplosion(transform.position, 2f);
            VFXManager.LargeFlames(transform.position);

            // Force all remaining parts to 0 HP
            for (int i = 0; i < PartNodes.Count; i++)
            {
                if (!PartNodes[i].isDestroyed)
                {
                    PartNodes[i].currentHp = 0;
                    PartNodes[i].isDestroyed = true;
                }
            }

            Debug.Log($"[VehicleRuntime] Vehicle {PlayerId} destroyed! IsAI={IsAI}, Parts={PartNodes.Count}");

            // Force ALL healthbars to zero immediately
            if (ArenaManager.Instance != null)
            {
                // Main HUD healthbar (player only)
                if (PlayerId == 0 && ArenaManager.Instance.Hud != null)
                    ArenaManager.Instance.Hud.SetHealth(0f, 0, 1);

                // Floating healthbar
                var healthbars = ArenaManager.Instance.GetHealthbars();
                if (healthbars != null && healthbars.TryGetValue(this, out var fhb) && fhb != null)
                {
                    fhb.ForceZero();
                    fhb.gameObject.SetActive(false);
                }
            }

            // Disable player input but KEEP the camera alive for death animation
            var ctrl = GetComponent<PlayerVehicleController>();
            if (ctrl != null)
            {
                ctrl.enabled = false;
                // Keep camera running so player sees their own explosion
            }

            // Disable ALL controllers on this vehicle
            var ai = GetComponent<CloseEncounters.AI.AIController>();
            if (ai != null) ai.enabled = false;
            var gp = GetComponent<CloseEncounters.VehiclePhysics.GroundPhysics>();
            if (gp != null) gp.enabled = false;
            var wp = GetComponent<CloseEncounters.VehiclePhysics.WaterPhysics>();
            if (wp != null) wp.enabled = false;
            var combatIn = GetComponent<PlayerCombatInput>();
            if (combatIn != null) combatIn.enabled = false;

            // Show big DEFEATED banner immediately (player sees it over their explosion)
            if (PlayerId == 0 && ArenaManager.Instance != null && ArenaManager.Instance.Hud != null)
                ArenaManager.Instance.Hud.ShowGameOver("DEFEATED");

            StartCoroutine(CinematicDestruction());

            // Delay spectator switch so player watches their death animation first
            if (PlayerId == 0 && ArenaManager.Instance != null)
                StartCoroutine(DelayedSpectatorSwitch(3f));
        }

        private System.Collections.IEnumerator CinematicDestruction()
        {
            Vector3 center = transform.position + Vector3.up;

            // 1. Big central explosion
            DamageSystem.SpawnExplosionFX(center, 1f);

            // 2. Four scatter explosions at random offsets (0.1-0.5s staggered)
            for (int e = 0; e < 4; e++)
            {
                float scatterDelay = UnityEngine.Random.Range(0.1f, 0.5f);
                Vector3 offset = new Vector3(
                    UnityEngine.Random.Range(-2f, 2f),
                    UnityEngine.Random.Range(0f, 2f),
                    UnityEngine.Random.Range(-2f, 2f));
                StartCoroutine(DelayedExplosion(center + offset,
                    UnityEngine.Random.Range(0.5f, 0.8f), scatterDelay));

                // Fire burst at each scatter point
                VFXManager.TinyFlames(center + offset, 0.8f);
            }

            // 3. Persistent fire at death site
            SpawnPersistentFire(center);

            // 4. Two-wave part ejection (Godot style: 70% burst, 30% straggle)
            int totalParts = PartNodes.Count;
            int burstCount = Mathf.Max(Mathf.RoundToInt(totalParts * 0.7f), 1);

            for (int i = 0; i < totalParts; i++)
            {
                if (PartNodes[i] == null) continue;
                float delay;
                if (i < burstCount)
                    delay = UnityEngine.Random.Range(0f, 0.15f); // Wave 1: immediate burst
                else
                    delay = UnityEngine.Random.Range(0.2f, 0.6f); // Wave 2: stragglers

                StartCoroutine(EjectPartDelayed(PartNodes[i], center, delay));
            }

            // 5. Freeze hull after 1 second
            yield return new WaitForSeconds(1f);
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
                rb.isKinematic = true;
        }

        private System.Collections.IEnumerator DelayedSpectatorSwitch(float delay)
        {
            yield return new WaitForSeconds(delay);

            // Only switch if match is still running
            if (ArenaManager.Instance != null && ArenaManager.Instance.MatchRunning)
            {
                ArenaManager.Instance.StartSpectating(transform);
            }
        }

        private System.Collections.IEnumerator DelayedExplosion(Vector3 pos, float scale, float delay)
        {
            yield return new WaitForSeconds(delay);
            DamageSystem.SpawnExplosionFX(pos, scale);
        }

        private System.Collections.IEnumerator EjectPartDelayed(PartNode part, Vector3 explosionCenter, float delay)
        {
            yield return new WaitForSeconds(delay);
            EjectPart(part, explosionCenter);
        }

        /// <summary>
        /// Turn a single part into flying debris — matches Godot's _eject_part() exactly.
        /// </summary>
        private void EjectPart(PartNode part, Vector3 explosionCenter)
        {
            if (part == null) return;

            Vector3 worldPos = part.transform.position;

            // Fire VFX at detach point (not sparks)
            VFXManager.TinyFlames(worldPos, 0.5f);

            // Hide the original part completely
            part.gameObject.SetActive(false);

            // Simple darkened cube debris (no prefab cloning -- avoids giant mesh bugs)
            var debris = new GameObject("Debris");
            var debrisRb = debris.AddComponent<Rigidbody>();
            debrisRb.mass = Mathf.Max(
                part.partData != null ? part.partData.massKg * 0.5f : 1f, 0.5f);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(debris.transform, false);
            Vector3 partSize = part.partData != null
                ? new Vector3(part.partData.size.x * 0.6f, part.partData.size.y * 0.6f, part.partData.size.z * 0.6f)
                : Vector3.one * 0.4f;
            visual.transform.localScale = partSize;

            var rend = visual.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                var urpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpShader != null)
                {
                    var mat = new Material(urpShader);
                    mat.color = new Color(0.15f, 0.15f, 0.15f); // charred dark
                    rend.material = mat;
                }
            }
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());

            var debrisCol = debris.AddComponent<SphereCollider>();
            debrisCol.radius = 0.3f;

            debris.transform.position = worldPos;

            // Ignore collisions with ALL vehicles (debris only hits terrain/buildings/props)
            if (ArenaManager.Instance != null)
            {
                var vehicles = ArenaManager.Instance.GetVehicles();
                for (int v = 0; v < vehicles.Count; v++)
                {
                    if (vehicles[v] == null) continue;
                    foreach (var vc in vehicles[v].GetComponentsInChildren<Collider>())
                    {
                        if (vc != null)
                            Physics.IgnoreCollision(debrisCol, vc, true);
                    }
                }
            }

            // Fling outward from vehicle center
            Vector3 direction = (worldPos - explosionCenter).normalized;
            if (direction.sqrMagnitude < 0.01f)
                direction = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f), 1f,
                    UnityEngine.Random.Range(-1f, 1f)).normalized;
            direction.y = Mathf.Abs(direction.y) + 0.5f;

            float force = UnityEngine.Random.Range(10f, 22f);
            debrisRb.linearVelocity = direction * force;
            debrisRb.angularVelocity = new Vector3(
                UnityEngine.Random.Range(-8f, 8f),
                UnityEngine.Random.Range(-8f, 8f),
                UnityEngine.Random.Range(-8f, 8f));

            Destroy(debris, 8f);
        }

        /// <summary>Spawn a small persistent fire particle effect at the death site.</summary>
        private void SpawnPersistentFire(Vector3 position)
        {
            // Use pre-made VFX prefabs (already URP-compatible)
            VFXManager.LargeFlames(position, 1.5f);
            VFXManager.MediumFlames(position + Vector3.up, 1f);

            // Add a point light for fire glow
            var lightObj = new GameObject("FireLight");
            lightObj.transform.position = position;
            var fireLight = lightObj.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.5f, 0.1f);
            fireLight.intensity = 3f;
            fireLight.range = 8f;
            UnityEngine.Object.Destroy(lightObj, 12f);
        }
    }
}
