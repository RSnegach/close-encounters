using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

namespace CloseEncounters.Net
{
    /// <summary>
    /// Phase 1 connection layer for Steam multiplayer.
    ///
    /// Flow: a host creates a Steam lobby (over the free Steam Datagram Relay) and calls NGO
    /// StartHost; a client accepts an invite / "Join Game", enters the lobby, points the transport
    /// at the host's SteamId, and calls StartClient. No dedicated server, no IPs exchanged.
    ///
    /// SETUP (in the editor, once):
    ///   1. Create an empty GameObject "NetworkManager" in your bootstrap scene.
    ///   2. Add Unity's NetworkManager component + a FacepunchTransport component; assign the
    ///      transport as the NetworkManager's "Network Transport". Set the transport's Steam App
    ///      Id to 480 for dev.
    ///   3. Add this SteamLobbyManager component to the same (or another persistent) GameObject.
    ///
    /// This is intentionally standalone with a small debug OnGUI so it can be spike-tested before
    /// being wired into the real MainMenu/Lobby UI. It does NOT yet spawn vehicles — that is the
    /// next phase. The class survives scene loads.
    ///
    /// NOTE on local testing: ParrelSync clones share ONE Steam account, and Steam will not let you
    /// invite/relay to yourself, so the Steam path is best tested with a friend or a second Steam
    /// account on a second machine. (ParrelSync is still useful later for the non-Steam gameplay
    /// logic via UnityTransport.)
    /// </summary>
    public class SteamLobbyManager : MonoBehaviour
    {
        public static SteamLobbyManager Instance { get; private set; }

        [Tooltip("Steam App ID. 480 (Spacewar) is the universal dev/test id; swap for your real id later.")]
        public uint steamAppId = 480;

        [Tooltip("Maximum players per lobby (free-for-all).")]
        public int maxMembers = 8;

        [Tooltip("Draw a minimal on-screen Host/Leave panel for spike testing.")]
        public bool showDebugUI = true;

        /// <summary>The lobby we are currently in, if any.</summary>
        public Lobby? CurrentLobby { get; private set; }
        public bool InLobby => CurrentLobby.HasValue;

        private bool _isHost;
        private string _status = "Not connected";

        private FacepunchTransport Transport =>
            NetworkManager.Singleton != null
                ? NetworkManager.Singleton.NetworkConfig.NetworkTransport as FacepunchTransport
                : null;

        // ── Lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // The transport also calls SteamClient.Init when networking starts, but we need Steam
            // up FRONT so lobby calls work before StartHost. A duplicate init is caught harmlessly.
            try
            {
                if (!SteamClient.IsValid)
                    SteamClient.Init(steamAppId, false);
            }
            catch (System.Exception e)
            {
                _status = "Steam init failed (is Steam running?)";
                Debug.LogError($"[SteamLobbyManager] SteamClient.Init failed: {e}");
            }
        }

        private void OnEnable()
        {
            SteamMatchmaking.OnLobbyCreated += OnLobbyCreated;
            SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeave;
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
        }

        private void OnDisable()
        {
            SteamMatchmaking.OnLobbyCreated -= OnLobbyCreated;
            SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeave;
            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
        }

        private void Update()
        {
            // Pump Steam callbacks (the transport also pumps once networking is live; double is fine).
            if (SteamClient.IsValid)
                SteamClient.RunCallbacks();
        }

        private void OnApplicationQuit()
        {
            LeaveLobby();
            if (SteamClient.IsValid)
                SteamClient.Shutdown();
        }

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>Create a friends-joinable lobby and start hosting once Steam confirms it.</summary>
        public async void HostLobby()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[SteamLobbyManager] No NetworkManager in the scene.");
                return;
            }
            _status = "Creating lobby...";
            var created = await SteamMatchmaking.CreateLobbyAsync(maxMembers);
            if (!created.HasValue)
            {
                _status = "Failed to create lobby";
                Debug.LogError("[SteamLobbyManager] CreateLobbyAsync returned null.");
            }
            // Success continues in OnLobbyCreated.
        }

        /// <summary>Leave the current lobby and shut down the NGO session.</summary>
        public void LeaveLobby()
        {
            if (CurrentLobby.HasValue)
            {
                CurrentLobby.Value.Leave();
                CurrentLobby = null;
            }
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            _isHost = false;
            _status = "Not connected";
        }

        // ── Steam callbacks ──────────────────────────────────────────────

        private void OnLobbyCreated(Result result, Lobby lobby)
        {
            if (result != Result.OK)
            {
                _status = $"Lobby create error: {result}";
                Debug.LogError($"[SteamLobbyManager] Lobby creation failed: {result}");
                return;
            }

            lobby.SetFriendsOnly();          // friends can "Join Game" from the friends list
            lobby.SetJoinable(true);
            lobby.SetData("name", $"{SteamClient.Name}'s game");
            CurrentLobby = lobby;

            _isHost = true;
            NetworkManager.Singleton.StartHost();
            _status = $"Hosting (lobby {lobby.Id})";
            Debug.Log($"[SteamLobbyManager] Hosting lobby {lobby.Id}.");
        }

        // Fired when WE enter a lobby (both as host after creating, and as a joining client).
        private async void OnLobbyEntered(Lobby lobby)
        {
            CurrentLobby = lobby;

            if (_isHost)
                return; // host already started in OnLobbyCreated

            // Joining client: point the transport at the host and connect via the relay.
            await System.Threading.Tasks.Task.Yield(); // ensure transport exists this frame
            if (Transport == null)
            {
                _status = "No FacepunchTransport on NetworkManager";
                Debug.LogError("[SteamLobbyManager] Transport missing; cannot join.");
                return;
            }
            Transport.targetSteamId = lobby.Owner.Id;
            NetworkManager.Singleton.StartClient();
            _status = $"Joined {lobby.Owner.Name}'s game";
            Debug.Log($"[SteamLobbyManager] Joining host {lobby.Owner.Id}.");
        }

        private void OnGameLobbyJoinRequested(Lobby lobby, SteamId id)
        {
            // Accepted a Steam invite / clicked "Join Game" — enter the lobby (triggers OnLobbyEntered).
            _ = lobby.Join();
        }

        private void OnLobbyMemberJoined(Lobby lobby, Friend friend)
        {
            Debug.Log($"[SteamLobbyManager] {friend.Name} joined the lobby.");
        }

        private void OnLobbyMemberLeave(Lobby lobby, Friend friend)
        {
            Debug.Log($"[SteamLobbyManager] {friend.Name} left the lobby.");
        }

        // ── Spike debug UI ───────────────────────────────────────────────

        private void OnGUI()
        {
            if (!showDebugUI) return;

            const int w = 280, x = 12, y = 12;
            GUILayout.BeginArea(new Rect(x, y, w, 200), GUI.skin.box);
            GUILayout.Label("<b>Steam Multiplayer (spike)</b>");
            GUILayout.Label(SteamClient.IsValid
                ? $"You: {SteamClient.Name} ({SteamClient.SteamId})"
                : "Steam not initialized");
            GUILayout.Label($"Status: {_status}");

            bool live = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (!live)
            {
                if (GUILayout.Button("Host Game")) HostLobby();
                GUILayout.Label("To join: have a friend (or 2nd Steam account) " +
                                "click 'Join Game' on you in Steam.");
            }
            else
            {
                int members = CurrentLobby.HasValue ? CurrentLobby.Value.MemberCount : 1;
                GUILayout.Label($"Role: {(_isHost ? "Host" : "Client")} | Members: {members} | " +
                                $"Connected clients: {NetworkManager.Singleton.ConnectedClientsIds.Count}");
                if (GUILayout.Button("Leave")) LeaveLobby();
            }
            GUILayout.EndArea();
        }
    }
}
