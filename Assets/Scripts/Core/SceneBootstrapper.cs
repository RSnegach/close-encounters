using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CloseEncounters.Arena;
using CloseEncounters.UI;
using CloseEncounters.Vehicle;

namespace CloseEncounters.Core
{
    /// <summary>
    /// Placed in every scene. On Start, ensures singletons exist and spawns
    /// the appropriate root objects for the current scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SceneBootstrapper : MonoBehaviour
    {
        private void Awake()
        {
            EnsureSingletons();
        }

        private void Start()
        {
            string sceneName = SceneManager.GetActiveScene().name;

            switch (sceneName)
            {
                case "MainMenu":
                    BootMainMenu();
                    break;
                case "Lobby":
                    BootLobby();
                    break;
                case "Builder":
                    BootBuilder();
                    break;
                case "Combat":
                    BootCombat();
                    break;
                case "Results":
                    BootResults();
                    break;
                default:
                    Debug.LogWarning($"[SceneBootstrapper] Unrecognized scene: {sceneName}");
                    break;
            }
        }

        // --- Singleton bootstrapping ---

        private void EnsureSingletons()
        {
            if (GameManager.Instance == null)
            {
                var gmObj = new GameObject("GameManager");
                gmObj.AddComponent<GameManager>();
            }

            if (PartRegistry.Instance == null)
            {
                var prObj = new GameObject("PartRegistry");
                prObj.AddComponent<PartRegistry>();
            }
        }

        // --- Scene-specific boot methods ---

        private void BootMainMenu()
        {
            GameManager.Instance.SetState(GameState.MainMenu);

            GameObject canvas = CreateCanvas("MainMenuCanvas");
            var ui = canvas.AddComponent<MainMenuUI>();
            ui.Initialize();
        }

        private void BootLobby()
        {
            GameManager.Instance.SetState(GameState.Lobby);

            GameObject canvas = CreateCanvas("LobbyCanvas");
            canvas.AddComponent<LobbyUI>();
        }

        private void BootBuilder()
        {
            GameManager.Instance.SetState(GameState.Building);

            var builderObj = new GameObject("VehicleBuilder");
            var builder = builderObj.AddComponent<VehicleBuilder>();

            // Configure with the match's domain and budget, then initialize
            string domain = GameManager.Instance.Settings.domain;
            int budget = GameManager.Instance.Settings.budget;
            builder.Setup(domain, budget);

            GameObject canvas = CreateCanvas("BuilderCanvas");
            var ui = canvas.AddComponent<BuilderUI>();
            ui.Initialize();
        }

        private void BootCombat()
        {
            GameManager.Instance.SetState(GameState.Combat);

            var arenaObj = new GameObject("ArenaManager");
            var arena = arenaObj.AddComponent<CloseEncounters.Arena.ArenaManager>();
            arena.Initialize();
        }

        private void BootResults()
        {
            GameManager.Instance.SetState(GameState.Results);

            GameObject canvas = CreateCanvas("ResultsCanvas");
            var ui = canvas.AddComponent<ResultsUI>();
            ui.Initialize();
        }

        // --- Canvas factory ---

        /// <summary>
        /// Creates a standard UI canvas with a CanvasScaler targeting 1920x1080.
        /// </summary>
        private static GameObject CreateCanvas(string canvasName)
        {
            var canvasObj = new GameObject(canvasName);

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // EventSystem is required for UI clicks to register.
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            return canvasObj;
        }
    }

}
