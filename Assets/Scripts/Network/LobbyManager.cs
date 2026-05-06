using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Vivox;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using UnityEngine.SceneManagement;
using Ecopoly.Core;
using Ecopoly.Data;

namespace Ecopoly.Network
{
    /// <summary>
    /// Manages lobby creation and joining through Unity Gaming Services.
    /// Integrates Relay for P2P connection without a dedicated server.
    ///
    /// Persistent singleton — lives in the Bootstrap scene and persists across
    /// scene loads. BootstrapController calls InitializeAsync() once at startup.
    /// </summary>
    public class LobbyManager : MonoBehaviour
    {
        public static LobbyManager Instance { get; private set; }

        public const int MAX_PLAYERS = 5;

        [Header("Vivox")]
        [SerializeField] private VivoxSettings _vivoxSettings;

        // --- State
        private Lobby  _currentLobby;
        private string _localPlayerId;
        private Coroutine _heartbeatCoroutine;
        private bool   _isHost;
        private bool   _gameStarted;
        private string _relayJoinCode;
        private string _localPlayerName;
        private readonly List<string> _cachedPlayerNames = new List<string>();
        private readonly string _gameBoardSceneName = "GameBoardEnv";
        private List<BotPersonalityData> _botPersonalities = null;
        private int _offlineBotCount = 0;
        private int _cachedBotCount = 0;

        // --- Public API
        public bool   IsInitialized     { get; private set; }
        public bool   HasLobby          => _currentLobby != null;
        public bool   IsHost            => _isHost;
        public string CurrentLobbyCode  => _currentLobby?.LobbyCode;
        public string CurrentLobbyId    => _currentLobby?.Id;
        public string LocalPlayerName   => _localPlayerName;
        public string LocalPlayerId     => _localPlayerId;
        public List<BotPersonalityData> BotPersonalities => _botPersonalities;

        // --- Events
        /// <summary>Fired once UGS authentication completes successfully.</summary>
        public event Action OnInitialized;
        /// <summary>Fired whenever the player list changes (create, join, poll).</summary>
        public event Action<List<string>> OnPlayersUpdated;
        /// <summary>Fired on the host side when it successfully starts the Relay session.</summary>
        public event Action OnGameStarting;

        // --- Lifecycle
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // DontDestroyOnLoad requires a root GameObject; detach first if parented.
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            StopHeartbeat();
        }

        // --- UGS Initialization
        /// <summary>
        /// Initializes Unity Services and signs in anonymously.
        /// Safe to call multiple times — subsequent calls are no-ops once done.
        /// Called by BootstrapController during the loading sequence.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (IsInitialized) return;

            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var initOptions = new InitializationOptions();

#if UNITY_EDITOR
                    // In Multiplayer Play Mode each virtual player must use a distinct UGS profile
                    // so auth tokens don't collide between editor instances.
                    // Virtual players (non-main editor) get a profile derived from their MPM tags.
                    if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
                    {
                        var tags = Unity.Multiplayer.PlayMode.CurrentPlayer.ReadOnlyTags();
                        string profile = tags != null && tags.Length > 0
                            ? string.Join("_", tags)
                            : "VirtualPlayer";
                        initOptions.SetProfile(profile);
                        Debug.Log($"[LobbyManager] MPM virtual player profile set: '{profile}'");
                    }
#else
                    // In a built game, support launching multiple instances (e.g. for local testing)
                    // by passing -ugsProfile <name> on the command line.
                    // Example: MyGame.exe -ugsProfile Player2
                    string buildProfile = GetCommandLineProfile();
                    if (!string.IsNullOrEmpty(buildProfile))
                    {
                        initOptions.SetProfile(buildProfile);
                        Debug.Log($"[LobbyManager] Build profile set from command line: '{buildProfile}'");
                    }
#endif

                    // Inject Vivox Developer Portal credentials when provided.
                    // This allows the SDK to generate tokens locally without UGS dashboard linking.
                    if (_vivoxSettings != null
                        && !string.IsNullOrEmpty(_vivoxSettings.Server)
                        && !string.IsNullOrEmpty(_vivoxSettings.TokenKey))
                    {
                        initOptions.SetVivoxCredentials(
                            _vivoxSettings.Server,
                            _vivoxSettings.Domain,
                            _vivoxSettings.TokenIssuer,
                            _vivoxSettings.TokenKey
                        );
                        Debug.Log("[LobbyManager] Vivox credentials injected into UGS init options.");
                    }

                    await UnityServices.InitializeAsync(initOptions);
                }
                else
                {
                    // UGS was already initialized before LobbyManager.InitializeAsync() ran.
                    // Vivox credentials cannot be injected after the fact.
                    Debug.LogWarning("[LobbyManager] UGS already initialized before LobbyManager. " +
                        "Vivox credentials were NOT injected. Assign unique MPM tags per virtual player " +
                        "so each instance initializes UGS independently.");
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                _localPlayerId = AuthenticationService.Instance.PlayerId;
                IsInitialized  = true;
                Debug.Log($"[LobbyManager] UGS ready. PlayerId: {_localPlayerId}");
                OnInitialized?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyManager] UGS initialization failed: {e.Message}");
            }
        }

        // --- Create Lobby
        /// <summary>
        /// Creates a new public lobby and returns its 6-character join code.
        /// Returns null on failure.
        /// </summary>
        public async Task<string> CreateLobbyAsync(string lobbyName, string hostPlayerName)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[LobbyManager] Cannot create lobby — not yet initialized.");
                return null;
            }

            try
            {
                var options = new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Player    = BuildPlayerData(hostPlayerName),
                };

                _currentLobby    = await LobbyService.Instance.CreateLobbyAsync(lobbyName, MAX_PLAYERS, options);
                _isHost          = true;
                _gameStarted     = false;
                _relayJoinCode   = null;
                _localPlayerName = hostPlayerName;

                _cachedPlayerNames.Clear();
                _cachedPlayerNames.Add(hostPlayerName);

                StartHeartbeat();
                NotifyPlayersUpdated();

                Debug.Log($"[LobbyManager] Lobby created. Code: {_currentLobby.LobbyCode}");
                return _currentLobby.LobbyCode;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] CreateLobby failed: {e.Message}");
                return null;
            }
        }

        public async Task<string> CreateSoloLobbyAsync(string lobbyName, string hostPlayerName)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[LobbyManager] Cannot create solo lobby — not yet initialized.");
                return null;
            }

            _currentLobby    = null;
            _isHost          = true;
            _gameStarted     = false;
            _relayJoinCode   = null;
            _localPlayerName = hostPlayerName;

            _cachedPlayerNames.Clear();
            _cachedPlayerNames.Add(hostPlayerName);

            NotifyPlayersUpdated();

            Debug.Log("[LobbyManager] Offline session started.");

            return null;
        }

        // --- Join Lobby
        /// <summary>Joins an existing lobby by its join code. Returns false on failure.</summary>
        public async Task<bool> JoinLobbyAsync(string lobbyCode, string playerName)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[LobbyManager] Cannot join lobby — not yet initialized.");
                return false;
            }

            try
            {
                var options = new JoinLobbyByCodeOptions { Player = BuildPlayerData(playerName) };
                _currentLobby    = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, options);
                _isHost          = false;
                _gameStarted     = false;
                _relayJoinCode   = null;
                _localPlayerName = playerName;

                _cachedPlayerNames.Clear();
                _cachedPlayerNames.Add(playerName);

                _offlineBotCount = GetBotCount();

                NotifyPlayersUpdated();
                Debug.Log($"[LobbyManager] Joined lobby: {lobbyCode}");
                return true;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"[LobbyManager] JoinLobby failed: {e.Message}");
                return false;
            }
        }

        // --- Start Game (host only)
        /// <summary>
        /// Host: creates a Relay allocation, writes the join code into the lobby data,
        /// configures UnityTransport, and starts as NetworkManager host.
        /// </summary>
        public async Task StartGameAsync()
        {
            if (_currentLobby == null || !_isHost)
            {
                Debug.LogWarning("[LobbyManager] StartGame called but not host or no lobby.");
                return;
            }

            try
            {
                // 1. Relay allocation
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MAX_PLAYERS - 1);
                string joinCode       = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                _relayJoinCode        = joinCode;
                _gameStarted          = true;

                // 2. Publish relay code and started flag into lobby data
                await LobbyService.Instance.UpdateLobbyAsync(_currentLobby.Id, new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        [LobbyDataKeys.RelayCode] = new DataObject(DataObject.VisibilityOptions.Member, joinCode),
                        [LobbyDataKeys.Started]   = new DataObject(DataObject.VisibilityOptions.Member, "true"),
                    }
                });

                // 3. Configure transport.
                //    RelayServer.IpV4/Port are deprecated in com.unity.services.multiplayer.
                //    Use ServerEndpoints with the "dtls" connection type instead.
                var dtlsEndpoint = allocation.ServerEndpoints
                    .First(e => e.ConnectionType == RelayServerEndpoint.ConnectionTypeDtls);
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.SetHostRelayData(
                    dtlsEndpoint.Host,
                    (ushort)dtlsEndpoint.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData,
                    isSecure: true
                );

                // 4. Tell EcopolyNetworkManager how many players to expect and
                //    what PlayerState list to use once everyone has connected.
                var players = BuildNetworkPlayerList();
                EcopolyNetworkManager.Instance?.PrepareGameStart(players, _offlineBotCount);

                NetworkManager.Singleton.StartHost();
                OnGameStarting?.Invoke();
                Debug.Log($"[LobbyManager] Game started. Relay code: {joinCode}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyManager] StartGame failed: {e.Message}");
            }
        }

        public async Task StartSoloGameAsync()
        {
            _gameStarted = true;
            if (GameManager.Instance == null)
            {
                Debug.LogError("GameManager missing — this should never happen.");
            }
            var result = new List<PlayerState>();
            result.Add(new PlayerState
            {
                PlayerId = 0,
                AnimalIndex = 0,
                PlayerName = string.IsNullOrWhiteSpace(_localPlayerName)
                    ? "Player"
                    : _localPlayerName,
                IsBot = false,
                IsEliminated = false,
                PersonalCEP = 0,
                BoardPosition = 0,
                JailTurnsRemaining = 0,
                IsInJail = false,
                ConsecutiveDoubles = 0,
                HasGetOutOfJailCard = false,
            });
            var players = GameManager.BuildOfflinePlayers(result, _offlineBotCount, _botPersonalities);
            SceneManager.LoadScene(_gameBoardSceneName);
            GameManager.Instance?.QueueGameStart(players);

            OnGameStarting?.Invoke();

            Debug.Log("[LobbyManager] Solo Game started (offline mode).");
        }

        // --- Join Relay (client)
        /// <summary>
        /// Converts the cached lobby player names into PlayerState objects.
        /// In a network lobby all participants are real humans — IsBot is always false.
        /// </summary>
        private List<Ecopoly.Core.PlayerState> BuildNetworkPlayerList()
        {
            var names   = GetCachedPlayerNames();
            var result  = new List<Ecopoly.Core.PlayerState>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                result.Add(new Ecopoly.Core.PlayerState
                {
                    PlayerId   = i,
                    AnimalIndex = i%5,
                    PlayerName = names[i],
                    IsBot      = false,
                });
            }
            result = GameManager.BuildOfflinePlayers(result, _offlineBotCount, _botPersonalities);
            return result;
        }

        /// <summary>Connects a non-host client to an existing Relay allocation.</summary>
        public async Task JoinRelayAsync(string relayCode)        {
            try
            {
                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayCode);
                var joinDtlsEndpoint = joinAllocation.ServerEndpoints
                    .First(e => e.ConnectionType == RelayServerEndpoint.ConnectionTypeDtls);
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.SetClientRelayData(
                    joinDtlsEndpoint.Host,
                    (ushort)joinDtlsEndpoint.Port,
                    joinAllocation.AllocationIdBytes,
                    joinAllocation.Key,
                    joinAllocation.ConnectionData,
                    joinAllocation.HostConnectionData,
                    isSecure: true
                );

                NetworkManager.Singleton.StartClient();
                Debug.Log($"[LobbyManager] Joined Relay. Code: {relayCode}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyManager] JoinRelay failed: {e.Message}");
            }
        }

        // --- Polling
        /// <summary>
        /// Fetches the latest lobby snapshot from the service.
        /// Fires OnPlayersUpdated with the current player list.
        /// </summary>
        public async Task<List<string>> RefreshLobbyAsync()
        {
            if (_currentLobby == null) return new List<string>();

            try
            {
                _currentLobby = await LobbyService.Instance.GetLobbyAsync(_currentLobby.Id);
                var players   = ExtractPlayerNames(_currentLobby);

                if (players.Count > 0)
                {
                    _cachedPlayerNames.Clear();
                    _cachedPlayerNames.AddRange(players);
                }

                if (_isHost) SetBotCount(_offlineBotCount);
                if (TryGetLobbyValue(_currentLobby, "botCount", out string value) &&
                int.TryParse(value, out int count))
                {
                    _cachedBotCount = count;
                }

                OnPlayersUpdated?.Invoke(players.Count > 0 ? players : new List<string>(_cachedPlayerNames));
                return players;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"[LobbyManager] RefreshLobby failed: {e.Message}");
                return new List<string>(_cachedPlayerNames);
            }
        }

        /// <summary>Returns true if the host has marked the game as started in the lobby data.</summary>
        public async Task<bool> HasGameStartedAsync()
        {
            if (_currentLobby == null) return false;
            if (_gameStarted) return true;

            try
            {
                _currentLobby = await LobbyService.Instance.GetLobbyAsync(_currentLobby.Id);

                if (TryGetLobbyValue(_currentLobby, LobbyDataKeys.Started, out string value) &&
                    string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    _gameStarted = true;
                    return true;
                }
                return false;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"[LobbyManager] HasGameStarted check failed: {e.Message}");
                return false;
            }
        }
        public void SetBotCount(int count)
        {
            if (!_isHost) return;

            // OFFLINE MODE
            if (_currentLobby == null)
            {
                _offlineBotCount = count;

                // Rebuild fake player list
                _cachedPlayerNames.Clear();
                _cachedPlayerNames.Add(_localPlayerName);

                for (int i = 0; i < _offlineBotCount; i++)
                    _cachedPlayerNames.Add($"Bot {i + 1}");

                NotifyPlayersUpdated();
                return;
            }

            // ONLINE MODE
            try
            {
                LobbyService.Instance.UpdateLobbyAsync(_currentLobby.Id, new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        ["botCount"] = new DataObject(DataObject.VisibilityOptions.Member, count.ToString())
                    }
                });
                _offlineBotCount = count;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"[LobbyManager] SetBotCount failed: {e.Message}");
            }
        }

        public int GetBotCount()
        {
            if (_currentLobby == null) return _offlineBotCount;

            return _cachedBotCount;
        }

        public void SetBotPersonalities(List<BotPersonalityData> personalities)
        {
            if (!_isHost) return;
            _botPersonalities = personalities;
        }

        /// <summary>
        /// Returns the Relay join code stored in the lobby data by the host,
        /// or null if the host hasn't started yet.
        /// </summary>
        public async Task<string> GetRelayCodeAsync()
        {
            if (_currentLobby == null) return null;
            if (!string.IsNullOrWhiteSpace(_relayJoinCode)) return _relayJoinCode;

            try
            {
                _currentLobby = await LobbyService.Instance.GetLobbyAsync(_currentLobby.Id);

                if (TryGetLobbyValue(_currentLobby, LobbyDataKeys.RelayCode, out string code) &&
                    !string.IsNullOrWhiteSpace(code))
                {
                    _relayJoinCode = code;
                    return code;
                }
                return null;
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"[LobbyManager] GetRelayCode failed: {e.Message}");
                return null;
            }
        }

        // --- Leave
        /// <summary>Removes the local player from the current lobby and cleans up state.</summary>
        public async Task LeaveLobbyAsync()
        {
            if (_currentLobby == null) return;

            StopHeartbeat();

            try
            {
                await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, _localPlayerId);
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning($"[LobbyManager] LeaveLobby failed: {e.Message}");
            }
            finally
            {
                ResetState();
            }
        }

        // --- Cached accessors
        public List<string> GetCachedPlayerNames() => new List<string>(_cachedPlayerNames);

        /// <summary>
        /// Reads game-started status and relay code from the lobby data already fetched
        /// by the most recent <see cref="RefreshLobbyAsync"/> call — zero extra network calls.
        /// Returns true when the host has started and a relay code is present.
        /// </summary>
        public bool TryGetCachedGameStart(out string relayCode)
        {
            relayCode = null;
            if (_currentLobby == null) return false;

            if (!TryGetLobbyValue(_currentLobby, LobbyDataKeys.Started, out string started) ||
                !string.Equals(started, "true", StringComparison.OrdinalIgnoreCase))
                return false;

            TryGetLobbyValue(_currentLobby, LobbyDataKeys.RelayCode, out relayCode);

            // Cache for future calls so the coroutine can short-circuit immediately
            if (!string.IsNullOrWhiteSpace(relayCode))
            {
                _gameStarted     = true;
                _relayJoinCode   = relayCode;
            }

            return !string.IsNullOrWhiteSpace(relayCode);
        }

        // --- Heartbeat
        private void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatCoroutine = StartCoroutine(HeartbeatLoop());
        }

        private void StopHeartbeat()
        {
            if (_heartbeatCoroutine == null) return;
            StopCoroutine(_heartbeatCoroutine);
            _heartbeatCoroutine = null;
        }

        private IEnumerator HeartbeatLoop()
        {
            var wait = new WaitForSeconds(15f);
            while (_currentLobby != null)
            {
                yield return wait;
                if (_currentLobby != null)
                    LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
            }
        }

        // --- Helpers
        private Unity.Services.Lobbies.Models.Player BuildPlayerData(string playerName)
        {
            return new Unity.Services.Lobbies.Models.Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    [LobbyDataKeys.PlayerName] = new PlayerDataObject(
                        PlayerDataObject.VisibilityOptions.Member, playerName)
                }
            };
        }

        private List<string> ExtractPlayerNames(Lobby lobby)
        {
            var result = new List<string>();
            if (lobby?.Players == null) return result;

            foreach (var player in lobby.Players)
            {
                if (player.Data != null &&
                    player.Data.TryGetValue(LobbyDataKeys.PlayerName, out var obj) &&
                    !string.IsNullOrWhiteSpace(obj.Value))
                {
                    result.Add(obj.Value);
                }
                else if (!string.IsNullOrWhiteSpace(player.Id))
                {
                    result.Add(player.Id);
                }
            }

            return result;
        }

        private static bool TryGetLobbyValue(Lobby lobby, string key, out string value)
        {
            value = null;
            if (lobby?.Data == null || !lobby.Data.TryGetValue(key, out var obj)) return false;
            value = obj.Value;
            return !string.IsNullOrWhiteSpace(value);
        }

        private void NotifyPlayersUpdated()
        {
            var players = _currentLobby != null
                ? ExtractPlayerNames(_currentLobby)
                : new List<string>();

            if (players.Count > 0)
            {
                _cachedPlayerNames.Clear();
                _cachedPlayerNames.AddRange(players);
            }

            OnPlayersUpdated?.Invoke(players.Count > 0 ? players : new List<string>(_cachedPlayerNames));
        }

        private void ResetState()
        {
            _currentLobby    = null;
            _isHost          = false;
            _gameStarted     = false;
            _relayJoinCode   = null;
            _localPlayerName = null;
            _cachedPlayerNames.Clear();
        }

#if !UNITY_EDITOR
        /// <summary>
        /// Reads a UGS profile name from the command-line argument "-ugsProfile &lt;name&gt;".
        /// Returns null if the argument is absent (single-instance builds need no profile override).
        /// </summary>
        private static string GetCommandLineProfile()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("-ugsProfile", System.StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
#endif

        // --- Lobby data key constants
        private static class LobbyDataKeys
        {
            public const string PlayerName = "name";
            public const string RelayCode  = "relayCode";
            public const string Started    = "started";
        }
    }
}

