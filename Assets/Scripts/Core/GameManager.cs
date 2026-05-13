using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloseEncounters.Core
{
    public enum GameState
    {
        MainMenu,
        Lobby,
        Building,
        Combat,
        Results
    }

    public enum BudgetTier
    {
        Amateur  = 1000,
        Normal   = 3000,
        Pro      = 6000,
        Legend    = 10000,
        Unlimited = 0
    }

    [Serializable]
    public class MatchSettings
    {
        public string domain      = "ground";
        public int budget         = (int)BudgetTier.Normal;
        public BudgetTier budgetTier = BudgetTier.Normal;
        public string arena       = "desert_flat";
        public string mode        = "deathmatch";
        public int playerCount    = 2;
        public int aiDifficulty   = 1;
        public int aiCount        = 1;

        /// <summary>
        /// The vehicle the player built. Set by BuilderUI before entering combat.
        /// If null, ArenaManager will attempt to load from disk or use a fallback.
        /// </summary>
        [NonSerialized]
        public VehicleData playerVehicle;

        public void SetBudgetTier(BudgetTier tier)
        {
            budgetTier = tier;
            budget = (int)tier;
        }
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public event Action<GameState, GameState> OnStateChanged;
        public event Action<MatchSettings> OnMatchConfigured;
        public event Action OnMatchStarted;
        public event Action<int> OnMatchEnded;

        public GameState CurrentState { get; private set; } = GameState.MainMenu;
        public MatchSettings Settings { get; private set; } = new MatchSettings();

        private readonly Dictionary<int, string> _playerNames = new Dictionary<int, string>();

        private static readonly Dictionary<GameState, string> StateSceneMap = new Dictionary<GameState, string>
        {
            { GameState.MainMenu, "MainMenu" },
            { GameState.Lobby,    "Lobby" },
            { GameState.Building, "Builder" },
            { GameState.Combat,   "Combat" },
            { GameState.Results,  "Results" }
        };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // --- Player name registry ---

        public void SetPlayerName(int playerId, string playerName)
        {
            _playerNames[playerId] = playerName;
        }

        public string GetPlayerName(int playerId)
        {
            return _playerNames.TryGetValue(playerId, out string name) ? name : $"Player {playerId}";
        }

        public IReadOnlyDictionary<int, string> GetAllPlayerNames()
        {
            return _playerNames;
        }

        // --- State transitions ---

        public void SetState(GameState newState)
        {
            if (newState == CurrentState) return;

            GameState previous = CurrentState;
            CurrentState = newState;
            OnStateChanged?.Invoke(previous, newState);
        }

        public void TransitionToState(GameState newState)
        {
            // Always restore timeScale on any scene transition
            // (combat pauses it, results screen pauses it, pause menu pauses it)
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            SetState(newState);

            if (StateSceneMap.TryGetValue(newState, out string sceneName))
            {
                SceneManager.LoadScene(sceneName);
            }
            else
            {
                Debug.LogError($"[GameManager] No scene mapped for state {newState}");
            }
        }

        // --- Match lifecycle ---

        public void ConfigureMatch(MatchSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            OnMatchConfigured?.Invoke(Settings);
        }

        public void StartMatch()
        {
            if (CurrentState != GameState.Building && CurrentState != GameState.Lobby)
            {
                Debug.LogWarning($"[GameManager] StartMatch called from unexpected state: {CurrentState}");
            }

            TransitionToState(GameState.Combat);
            OnMatchStarted?.Invoke();
        }

        public void EndMatch(int winnerPlayerId)
        {
            if (CurrentState != GameState.Combat)
            {
                Debug.LogWarning($"[GameManager] EndMatch called from unexpected state: {CurrentState}");
            }

            OnMatchEnded?.Invoke(winnerPlayerId);
            TransitionToState(GameState.Results);
        }

        public void ReturnToLobby()
        {
            // Don't clear player names or settings — lobby should restore them
            TransitionToState(GameState.Lobby);
        }

        public void ReturnToMainMenu()
        {
            _playerNames.Clear();
            Settings = new MatchSettings();
            TransitionToState(GameState.MainMenu);
        }

        public void GoToBuilder()
        {
            TransitionToState(GameState.Building);
        }

        // --- Utility ---

        public static int GetBudgetForTier(BudgetTier tier)
        {
            return (int)tier;
        }

        public static BudgetTier[] GetAllTiers()
        {
            return (BudgetTier[])Enum.GetValues(typeof(BudgetTier));
        }
    }
}
