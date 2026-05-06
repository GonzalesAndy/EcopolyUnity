using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Ecopoly.Utils;
using Ecopoly.Data;
using Ecopoly.Network;

namespace Ecopoly.Core
{
    /// <summary>
    /// Persistent singleton. Central reference for the game's state.
    /// Does NOT manage turn logic (TurnManager) nor the board (BoardController).
    /// Responsibilities: initialization, player list, win/lose state,
    /// global CEP counter, intensity thresholds.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private GameSettings _settings;
        [SerializeField] private BoardConfig _boardConfig;
        [SerializeField] private string _gameBoardSceneName = "GameBoard";

        private Ecopoly.Data.AnimalRosterData _animalRoster;
        [SerializeField] private GameObject _playerPrefab;

        // --- Game State ---

        public List<PlayerState> Players { get; private set; } = new List<PlayerState>();
        public int ActivePlayerCount => Players.Count(p => !p.IsEliminated);
        public bool IsGameOver { get; private set; }
        public bool IsGameInitialized { get; private set; }

        private List<PlayerState> _pendingPlayers;

        // --- Global CEP ---

        private int _globalCEP;
        public int GlobalCEP
        {
            get => _globalCEP;
            private set
            {
                int old = _globalCEP;
                _globalCEP = value;
                EventBus.Emit(GameEvent.GlobalCEPChanged, _globalCEP);
                CheckGlobalThresholds(old, _globalCEP);
            }
        }

        private int _currentIntensityLevel = 1; // 1-4
        public int CurrentIntensityLevel => _currentIntensityLevel;

        public GameSettings Settings => _settings;
        public BoardConfig BoardConfig => _boardConfig;

        // --- Lifecycle ---

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.Log("[GameManager] Another instance exists - destroying this duplicate.");
                Destroy(gameObject);
                return;
            }

            if (_settings == null)
                _settings = Resources.Load<GameSettings>(Constants.SO_GAME_SETTINGS);

            if (_boardConfig == null)
                _boardConfig = Resources.Load<BoardConfig>(Constants.SO_BOARD_CONFIG);

            if (_settings == null)
                Debug.LogError("[GameManager] Missing GameSettings. Assign in inspector or create Resources/Settings/GameSettings.asset.");

            if (_boardConfig == null)
                Debug.LogError("[GameManager] Missing BoardConfig. Assign in inspector or create Resources/Board/BoardConfig.asset.");

            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            _animalRoster = Resources.Load<Ecopoly.Data.AnimalRosterData>("Animals/SO_AnimalRoster");
            Debug.Log($"[GameManager] Awake complete. Instance assigned, persistent across scenes: {gameObject.name}");
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                Instance = null;
                var trace = System.Environment.StackTrace;
                Debug.Log($"[GameManager] OnDestroy called; clearing Instance reference. StackTrace:\n{trace}");
            }
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.CEPChanged, OnPlayerCEPChanged);
            EventBus.On(GameEvent.PlayerEliminated, OnPlayerEliminated);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.CEPChanged, OnPlayerCEPChanged);
            EventBus.Off(GameEvent.PlayerEliminated, OnPlayerEliminated);
        }

        // --- Initialization ---

        /// <param name="localPlayerIdHint">
        /// When called from the client-side network RPC, pass the PlayerId that corresponds
        /// to this machine's LocalClientId so the HUD is initialized for the correct player.
        /// Pass -1 (default) to fall back to name-matching / first-non-bot heuristic.
        /// </param>
        public void InitGame(List<PlayerState> players, int localPlayerIdHint = -1)
        {
            if (players == null || players.Count == 0)
            {
                Debug.LogError("[GameManager] InitGame called with no players.");
                return;
            }

            Debug.Log($"[GameManager] InitGame called. Players={players.Count}. Names={string.Join(",", players.Select(p=>p.PlayerName))}. ShouldRunLocalTurnLoop={ShouldRunLocalTurnLoop()}. localPlayerIdHint={localPlayerIdHint}");

            IsGameOver = false;
            IsGameInitialized = true;
            _globalCEP = 0; // Direct write to avoid emitting before players/UI are ready.
            _currentIntensityLevel = 1;
            Players = players;

            foreach (var p in Players)
                p.Money = _settings.startingMoney;

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                SpawnOfflinePlayers();
            } else if (players.Any(p => p.IsBot))
            {
                SpawnOfflinePlayers(onlyBots: true, botCount: players.Count(p => p.IsBot));
            }

            InitializeScenePlayers(localPlayerIdHint);

            if (ShouldRunLocalTurnLoop())
                EventBus.Emit(GameEvent.GameStarted);
        }

        public void QueueGameStart(List<PlayerState> players)
        {
            if (players == null || players.Count == 0)
            {
                Debug.LogWarning("[GameManager] QueueGameStart ignored: no players provided.");
                return;
            }

            _pendingPlayers = players;

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.name == _gameBoardSceneName)
            {
                InitGame(_pendingPlayers);
                _pendingPlayers = null;
            }
        }

        public static List<PlayerState> BuildOfflinePlayers(List<PlayerState> result, int botCount, List<BotPersonalityData> botPersonalities = null)
        {    
            for (int i = 0; i < botCount; i++)
            {
                Debug.Log($"[GameManager] Adding bot player {i} with personality {(botPersonalities != null && i - 1 < botPersonalities.Count ? botPersonalities[i].botName : "null")}");
                result.Add(new PlayerState
                {
                    PlayerId = result.Count,
                    AnimalIndex = result.Count % 5,
                    PlayerName = $"Bot {i+1}",
                    IsBot = true,
                    IsEliminated = false,
                    PersonalCEP = 0,
                    BoardPosition = 0,
                    JailTurnsRemaining = 0,
                    IsInJail = false,
                    ConsecutiveDoubles = 0,
                    HasGetOutOfJailCard = false,
                    BotPersonality = botPersonalities?[i],
                });
            }

            return result;
        }

        /// <summary>
        /// Ensure a persistent GameManager instance exists. If missing (e.g., destroyed
        /// during unexpected scene transitions), create one at runtime so gameplay
        /// logic can continue.
        /// </summary>
        public static void EnsureInstanceExists()
        {
            if (Instance != null) return;
            var go = new GameObject("GameManager [auto]");
            go.AddComponent<GameManager>();
            Debug.Log("[GameManager] EnsureInstanceExists created runtime GameManager.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[GameManager] OnSceneLoaded: {scene.name}");
            if (scene.name != _gameBoardSceneName) return;
            EventBus.On(GameEvent.CEPChanged, OnPlayerCEPChanged);
            EventBus.On(GameEvent.PlayerEliminated, OnPlayerEliminated);

            // On the server (host), EcopolyNetworkManager.OnNetworkSceneLoadComplete
            // is authoritative and calls InitGame with the network player list.
            // Skipping here prevents a double-init with the wrong player data.
            var net = NetworkManager.Singleton;
            if (net != null && net.IsListening && net.IsServer) return;

            if (_pendingPlayers != null && _pendingPlayers.Count > 0)
            {
                Players = _pendingPlayers;
                InitGame(_pendingPlayers);
                _pendingPlayers = null;
                return;
            }

            if (!IsGameInitialized || Players == null || Players.Count == 0)
            {
                Debug.LogWarning("[GameManager] No pending game setup found. Starting default offline setup.");
                var result = new List<PlayerState>();
                result.Add(new PlayerState
                {
                    PlayerId = 0,
                    AnimalIndex = 0,
                    PlayerName = "Player",
                    IsBot = false,
                    IsEliminated = false,
                    PersonalCEP = 0,
                    BoardPosition = 0,
                    JailTurnsRemaining = 0,
                    IsInJail = false,
                    ConsecutiveDoubles = 0,
                    HasGetOutOfJailCard = false,
                });
                InitGame(BuildOfflinePlayers(result, Constants.MIN_PLAYERS - 1, null));
            }
            else
            {
                InitializeScenePlayers();
            }
        }

        private void SpawnOfflinePlayers(bool onlyBots = false, int botCount = 0)
        {
            // Skip if player GOs are already present in scene (e.g. spawned by PlayTestBootstrapRuntime)
            var existing = FindObjectsByType<Ecopoly.Player.PlayerController>(FindObjectsSortMode.None);
            if (existing != null && existing.Length >= (onlyBots ? botCount : Players.Count))
            {
                Debug.Log("[GameManager] SpawnOfflinePlayers skipped - PlayerController(s) already present in scene.");
                return;
            }

            if (_playerPrefab == null)
            {
                Debug.LogWarning("[GameManager] SpawnOfflinePlayers: no _playerPrefab assigned - skipping spawn.");
                return;
            }

            Debug.Log("[GameManager] Spawning offline players...");

            for (int i = 0; i < (onlyBots ? botCount : Players.Count); i++)
            {
                var obj = Instantiate(_playerPrefab);

                obj.transform.position = new Vector3(0.6f, 0f, 0f);

                var controller = obj.GetComponent<Ecopoly.Player.PlayerController>();
                if (controller != null)
                {
                    controller.PlayerId = Players[i].PlayerId;
                }
                else
                {
                    Debug.LogError("[GameManager] Player prefab missing PlayerController.");
                }
            }
        }

        private void InitializeScenePlayers(int localPlayerIdHint = -1)
        {
            var controllers = FindObjectsByType<Ecopoly.Player.PlayerController>(FindObjectsSortMode.None);
            
            if (controllers == null || controllers.Length == 0)
            {
                Debug.LogWarning("[GameManager] No PlayerController found in scene for initialization.");
                return;
            }

            int localPlayerId;
            if (localPlayerIdHint >= 0 && Players.Any(p => p.PlayerId == localPlayerIdHint))
            {
                // Authoritative hint from the network RPC (client matched via LocalClientId)
                localPlayerId = localPlayerIdHint;
                Debug.Log($"[GameManager] localPlayerId resolved from hint: {localPlayerId}");
            }
            else
            {
                // Fallback: match by lobby player name, then first non-bot
                string localName = LobbyManager.Instance != null ? LobbyManager.Instance.LocalPlayerName : null;
                localPlayerId = !string.IsNullOrWhiteSpace(localName)
                    ? Players.FirstOrDefault(p => string.Equals(p.PlayerName, localName, StringComparison.OrdinalIgnoreCase))?.PlayerId ?? -1
                    : -1;
                if (localPlayerId == -1)
                    localPlayerId = Players.FirstOrDefault(p => !p.IsBot)?.PlayerId ?? 0;
                Debug.Log($"[GameManager] localPlayerId resolved via name/fallback: {localPlayerId} (localName='{localName}')");
            }

            // Robust mapping: first match exact PlayerId, then deterministically
            // assign remaining states to avoid collisions when multiple
            // PlayerControllers share the default serialized ID.
            var availableStates = new List<PlayerState>(Players);
            var controllerList = controllers
                .OrderBy(c => c.PlayerId)
                .ThenBy(c => c.name)
                .ToList();

            foreach (var controller in controllerList)
            {
                var state = availableStates.FirstOrDefault(p => p.PlayerId == controller.PlayerId);
                if (state != null)
                {
                    availableStates.Remove(state);
                    ApplyStateToController(controller, state, localPlayerId);
                }
            }

            foreach (var controller in controllerList)
            {
                if (availableStates.Count == 0) break;
                var alreadyAssigned = Players.Any(p => p.PlayerId == controller.PlayerId
                    && controller.State == p);
                if (alreadyAssigned) continue;

                var state = availableStates[0];
                availableStates.RemoveAt(0);
                ApplyStateToController(controller, state, localPlayerId);
            }

            if (availableStates.Count > 0)
                Debug.LogWarning($"[GameManager] {availableStates.Count} player states could not be assigned to a PlayerController in scene.");

            var huds = FindObjectsByType<Ecopoly.UI.HUDController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var hud in huds)
            {
                if (!hud.gameObject.activeSelf)
                    hud.gameObject.SetActive(true);
                hud.Initialize(localPlayerId);
            }

            var uiManagers = FindObjectsByType<Ecopoly.UI.UIManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var uiManager in uiManagers)
            {
                if (!uiManager.gameObject.activeSelf)
                    uiManager.gameObject.SetActive(true);
                uiManager.Initialize(localPlayerId);
            }

            var gauges = FindObjectsByType<Ecopoly.UI.CEPGaugeUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var gauge in gauges)
            {
                if (!gauge.gameObject.activeSelf)
                    gauge.gameObject.SetActive(true);
                gauge.Initialize(localPlayerId, Players.Count);
            }
        }

        private static readonly Color[] PlayerColors =
        {
            new Color(0.20f, 0.60f, 1.00f), // blue   - player 0
            new Color(1.00f, 0.30f, 0.30f), // red    - player 1
            new Color(0.20f, 0.85f, 0.40f), // green  - player 2
            new Color(1.00f, 0.85f, 0.10f), // yellow - player 3
            new Color(0.85f, 0.20f, 0.85f), // purple - player 4
        };

        private void ApplyStateToController(Ecopoly.Player.PlayerController controller,
            PlayerState state, int localPlayerId)
        {
            controller.PlayerId = state.PlayerId;
            controller.SetPlayerColor(PlayerColors[state.PlayerId % PlayerColors.Length]);
            controller.Initialize(state, state.PlayerId == localPlayerId);
            controller.SetPlayerColor(GetPlayerColor(state.PlayerId));

            var animalPrefab = GetAnimalPrefab(state.AnimalIndex);
            if (animalPrefab != null)
                controller.SetAnimalModel(animalPrefab);

            var botBrain = controller.GetComponent<Ecopoly.AI.BotBrain>();
            if (botBrain != null)
            {
                if (state.IsBot)
                {
                    botBrain.enabled = true;
                    botBrain.Initialize(state);
                }
                else
                {
                    botBrain.enabled = false;
                }
            }
        }

        private static Color GetPlayerColor(int playerId)
            => playerId >= 0 && playerId < PlayerColors.Length ? PlayerColors[playerId] : Color.white;

        private GameObject GetAnimalPrefab(int animalIndex)
            => _animalRoster != null ? _animalRoster.GetPrefab(animalIndex) : null;

        private bool ShouldRunLocalTurnLoop()
        {
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsListening) return true;
            return net.IsServer;
        }

        // --- CEP ---

        private void OnPlayerCEPChanged(object payload)
        {
            RecalculateGlobalCEP();
        }

        public void RecalculateGlobalCEP()
        {
            Debug.Log("[GameManager] Updating global CEP to " + Players.Sum(p => p.PersonalCEP));
            GlobalCEP = Players.Sum(p => p.PersonalCEP);
        }

        private void CheckGlobalThresholds(int oldCEP, int newCEP)
        {
            if (IsGameOver) return;

            int playerIndex = ActivePlayerCount - Constants.MIN_PLAYERS;
            if (playerIndex < 0 || playerIndex >= 3) return;

            int gameOverThreshold = Constants.CEP_THRESHOLDS[playerIndex, 4];
            if (newCEP >= gameOverThreshold)
            {
                TriggerGlobalGameOver();
                return;
            }

            int newLevel = GetIntensityLevel(newCEP, playerIndex);
            if (newLevel != _currentIntensityLevel)
            {
                _currentIntensityLevel = newLevel;
                EventBus.Emit(GameEvent.GlobalCEPThresholdChanged, _currentIntensityLevel);
            }
        }

        private int GetIntensityLevel(int cep, int playerIndex)
        {
            for (int level = 0; level < 4; level++)
            {
                if (cep <= Constants.CEP_THRESHOLDS[playerIndex, level])
                    return level + 1;
            }
            return 4;
        }

        private void TriggerGlobalGameOver()
        {
            IsGameOver = true;
            Debug.Log("[GameManager] GLOBAL GAME OVER - global CEP threshold reached.");
            EventBus.Emit(GameEvent.GlobalGameOver);
        }

        // --- Elimination ---

        private void OnPlayerEliminated(object payload)
        {
            RecalculateGlobalCEP();

            if (ActivePlayerCount == 1 && !IsGameOver)
            {
                var winner = Players.First(p => !p.IsEliminated);
                Debug.Log($"[GameManager] Victory: {winner.PlayerName}");
                EventBus.Emit(GameEvent.GameEnded, winner);
            }
        }

        // --- Access ---

        public PlayerState GetPlayer(int playerId)
            => Players.FirstOrDefault(p => p.PlayerId == playerId);

        public bool TryGetPlayer(int playerId, out PlayerState state)
        {
            state = GetPlayer(playerId);
            return state != null;
        }

        /// <summary>
        /// Client-side only: directly sets the cached global CEP and intensity level
        /// without triggering threshold checks or re-emitting events.
        /// Called by EcopolyNetworkManager.SyncGlobalCEPClientRpc.
        /// </summary>
        public void SetGlobalCEPForSync(int globalCEP, int intensityLevel)
        {
            _globalCEP = globalCEP;
            _currentIntensityLevel = intensityLevel;
        }
    }

    // --- Player state (non-NetworkBehaviour, local side) ---

    [System.Serializable]
    public class PlayerState
    {
        public int PlayerId;
        public int AnimalIndex; // set from lobby selector
        public string PlayerName;
        public bool IsBot;
        public bool IsEliminated;
        public int Money;
        public int PersonalCEP;
        public int BoardPosition;
        public int JailTurnsRemaining;
        public bool IsInJail;
        public int ConsecutiveDoubles;
        public bool HasGetOutOfJailCard;
        public List<string> OwnedPropertyIds = new List<string>();
        public List<string> CardHandIds = new List<string>(); // movement cards in hand
        public Data.BotPersonalityData BotPersonality; // null if human
    }
}
