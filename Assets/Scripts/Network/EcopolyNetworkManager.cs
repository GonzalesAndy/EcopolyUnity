using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ecopoly.Cards;
using Ecopoly.Data;
using Ecopoly.Utils;
using Ecopoly.Core;
using Ecopoly.AI;

namespace Ecopoly.Network
{
    /// <summary>
    /// Ecopoly NetworkManager.
    /// Wraps Unity Netcode for GameObjects.
    /// Present in the Bootstrap scene and persistent.
    /// Synchronizes game state between clients.
    /// </summary>
    public class EcopolyNetworkManager : NetworkBehaviour
    {
        public static EcopolyNetworkManager Instance { get; private set; }

        [Header("Prefabs")]
        [SerializeField] private GameObject _playerNetworkPrefab;

        private int               _expectedPlayerCount = -1;
        private List<PlayerState> _pendingPlayerList;
        private bool              _gameBoardReady      = false;
        private bool              _gameBoardLoadRequested = false;

        // Clients that connected before GameBoard loaded — spawn them once scene is ready
        private readonly List<ulong> _pendingClientSpawns = new List<ulong>();

        private Dictionary<ulong, NetworkPlayerState> _networkPlayers
            = new Dictionary<ulong, NetworkPlayerState>();

        // Maps clientId → assigned PlayerId (index in the player list).
        private readonly Dictionary<ulong, int> _clientIdToPlayerId = new Dictionary<ulong, int>();

        private const string GameBoardSceneName = "GameBoardEnv";

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void PrepareGameStart(List<PlayerState> players, int botCount)
        {
            _pendingPlayerList = players;
            // All entries in the list come from the UGS lobby (real humans).
            // BuildNetworkPlayerList currently marks non-host players as bots,
            // but every lobby member still connects as a network client.
            // Use the raw total so we wait for every human before loading GameBoard.
            _expectedPlayerCount = players.Count - botCount;
            _gameBoardReady      = false;
            _gameBoardLoadRequested = false;
            _pendingClientSpawns.Clear();
            _clientIdToPlayerId.Clear();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // OnLoadEventCompleted fires once on the server when ALL connected clients
                // have finished loading the scene. More reliable than OnLoadComplete
                // (which fires per-client) for triggering spawn + game init.
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnAllClientsSceneLoaded;

                NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
                EventBus.On(GameEvent.TurnStarted,            OnTurnStarted);
                EventBus.On(GameEvent.TurnEnded,              OnTurnEnded);
                EventBus.On(GameEvent.DiceRolled,             OnDiceRolled);
                EventBus.On(GameEvent.PlayerMoved,            OnPlayerMoved);
                EventBus.On(GameEvent.PlayerLanded,           OnPlayerLanded);
                EventBus.On(GameEvent.PlayerJailed,           OnPlayerJailed);
                EventBus.On(GameEvent.PlayerReleasedFromJail, OnPlayerReleasedFromJail);
                EventBus.On(GameEvent.PlayerPassedGo,         OnPlayerPassedGo);
                EventBus.On(GameEvent.MoneyChanged,           OnMoneyChanged);
                EventBus.On(GameEvent.CEPChanged,             OnCEPChanged);
                EventBus.On(GameEvent.PropertyPurchased,      OnPropertyPurchased);
                EventBus.On(GameEvent.PropertyRenovated,      OnPropertyRenovated);
                EventBus.On(GameEvent.ChanceCardDrawn,        OnChanceCardDrawn);
                EventBus.On(GameEvent.EventCardDrawn,         OnEventCardDrawn);
                EventBus.On(GameEvent.PlayerHandChanged,      OnPlayerHandChanged);
                EventBus.On(GameEvent.UICardDisplayRequested, OnUICardDisplayRequested);
                EventBus.On(GameEvent.UIRenovationRequested,  OnUIRenovationRequested);
                EventBus.On(GameEvent.UINotification,         OnUINotification);
                EventBus.On(GameEvent.GameEnded,                 OnGameEnded);
                EventBus.On(GameEvent.GlobalGameOver,            OnGlobalGameOver);
                EventBus.On(GameEvent.UIDilemmaVoteRequested,    OnUIDilemmaVoteRequested);
                EventBus.On(GameEvent.PropertySold,              OnPropertySold);
                EventBus.On(GameEvent.PropertyDegraded,          OnPropertyDegraded);
                EventBus.On(GameEvent.GlobalCEPChanged,          OnGlobalCEPChanged);
                EventBus.On(GameEvent.GlobalCEPThresholdChanged, OnGlobalCEPThresholdChanged);
                EventBus.On(GameEvent.PlayerEliminated,          OnPlayerEliminated);
                EventBus.On(GameEvent.PlayerCEPMaxReached,       OnPlayerCEPMaxReached);
                EventBus.On(GameEvent.PlayerBankrupt,            OnPlayerBankrupt);
                EventBus.On(GameEvent.DistrictBuildingBuilt,     OnDistrictBuildingBuilt);
                EventBus.On(GameEvent.DistrictBuildingDestroyed, OnDistrictBuildingDestroyed);
                EventBus.On(GameEvent.DilemmaCardResolved,       OnDilemmaCardResolved);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                if (NetworkManager.Singleton?.SceneManager != null)
                    NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnAllClientsSceneLoaded;

                NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
                EventBus.Off(GameEvent.TurnStarted,            OnTurnStarted);
                EventBus.Off(GameEvent.TurnEnded,              OnTurnEnded);
                EventBus.Off(GameEvent.DiceRolled,             OnDiceRolled);
                EventBus.Off(GameEvent.PlayerMoved,            OnPlayerMoved);
                EventBus.Off(GameEvent.PlayerLanded,           OnPlayerLanded);
                EventBus.Off(GameEvent.PlayerJailed,           OnPlayerJailed);
                EventBus.Off(GameEvent.PlayerReleasedFromJail, OnPlayerReleasedFromJail);
                EventBus.Off(GameEvent.PlayerPassedGo,         OnPlayerPassedGo);
                EventBus.Off(GameEvent.MoneyChanged,           OnMoneyChanged);
                EventBus.Off(GameEvent.CEPChanged,             OnCEPChanged);
                EventBus.Off(GameEvent.PropertyPurchased,      OnPropertyPurchased);
                EventBus.Off(GameEvent.PropertyRenovated,      OnPropertyRenovated);
                EventBus.Off(GameEvent.ChanceCardDrawn,        OnChanceCardDrawn);
                EventBus.Off(GameEvent.EventCardDrawn,         OnEventCardDrawn);
                EventBus.Off(GameEvent.PlayerHandChanged,      OnPlayerHandChanged);
                EventBus.Off(GameEvent.UICardDisplayRequested, OnUICardDisplayRequested);
                EventBus.Off(GameEvent.UIRenovationRequested,  OnUIRenovationRequested);
                EventBus.Off(GameEvent.UINotification,         OnUINotification);
                EventBus.Off(GameEvent.GameEnded,                 OnGameEnded);
                EventBus.Off(GameEvent.GlobalGameOver,            OnGlobalGameOver);
                EventBus.Off(GameEvent.UIDilemmaVoteRequested,    OnUIDilemmaVoteRequested);
                EventBus.Off(GameEvent.PropertySold,              OnPropertySold);
                EventBus.Off(GameEvent.PropertyDegraded,          OnPropertyDegraded);
                EventBus.Off(GameEvent.GlobalCEPChanged,          OnGlobalCEPChanged);
                EventBus.Off(GameEvent.GlobalCEPThresholdChanged, OnGlobalCEPThresholdChanged);
                EventBus.Off(GameEvent.PlayerEliminated,          OnPlayerEliminated);
                EventBus.Off(GameEvent.PlayerCEPMaxReached,       OnPlayerCEPMaxReached);
                EventBus.Off(GameEvent.PlayerBankrupt,            OnPlayerBankrupt);
                EventBus.Off(GameEvent.DistrictBuildingBuilt,     OnDistrictBuildingBuilt);
                EventBus.Off(GameEvent.DistrictBuildingDestroyed, OnDistrictBuildingDestroyed);
                EventBus.Off(GameEvent.DilemmaCardResolved,       OnDilemmaCardResolved);
            }
        }

        // --- Scene loading
        // OnLoadEventCompleted fires once on the server after ALL connected clients
        // have finished loading the scene. clientsTimedOut lists any that failed.
        private void OnAllClientsSceneLoaded(
            string sceneName,
            LoadSceneMode loadSceneMode,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            if (!IsServer || sceneName != GameBoardSceneName) return;

            if (clientsTimedOut != null && clientsTimedOut.Count > 0)
                Debug.LogWarning($"[Network] {clientsTimedOut.Count} client(s) timed out loading {sceneName}.");

            _gameBoardReady = true;

            // Safety net: spawn any clients that connected after TryLoadGameBoard ran
            // but before OnLoadEventCompleted fired (should normally be empty).
            foreach (ulong pendingId in _pendingClientSpawns)
                SpawnPlayerForClient(pendingId);
            _pendingClientSpawns.Clear();

            // Spawn network player objects for every client that finished loading.
            foreach (ulong id in clientsCompleted)
                SpawnPlayerForClient(id);

            // Capture list before clearing — InitGame is deferred to a coroutine.
            var players = _pendingPlayerList;
            _pendingPlayerList   = null;
            _expectedPlayerCount = -1;

            if (players == null || players.Count == 0)
            {
                Debug.LogWarning("[EcopolyNetworkManager] OnAllClientsSceneLoaded received empty player list. Skipping InitGame.");
                return;
            }

            // Ensure GameManager exists before attempting to initialise.
            if (GameManager.Instance == null)
            {
                Debug.Log("[EcopolyNetworkManager] GameManager missing before InitGame; creating runtime instance.");
                Ecopoly.Core.GameManager.EnsureInstanceExists();
            }
            StartCoroutine(InitGameWhenReady(players));
        }

        // Wait up to a short timeout for GameManager to be available, then initialise the game.
        // Give the newly-loaded scene a few frames to run Awake on its objects, then
        // search for GameManager via Instance and by scanning inactive objects. If
        // still not found, subscribe to SceneManager.sceneLoaded as a fallback so we
        // can retry once more when Unity reports a scene load.
        private IEnumerator InitGameWhenReady(List<PlayerState> players)
        {
            const float kTimeout = 10f;
            float elapsed = 0f;

            // Allow the scene to run Awake/OnEnable on newly-instantiated objects.
            for (int i = 0; i < 3; ++i)
                yield return null;

            GameManager gm = null;
            while (elapsed < kTimeout)
            {
                gm = GameManager.Instance;
                if (gm == null)
                {
                    try
                    {
                        gm = FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
                    }
                    catch
                    {
                        // Older Unity versions may not support FindFirstObjectByType with
                        // the FindObjectsInactive enum; fall back to FindObjectOfType.
                        gm = FindObjectOfType<GameManager>(true);
                    }
                }

                if (gm != null) break;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (gm == null)
            {
                Debug.LogWarning("[EcopolyNetworkManager] InitGameWhenReady timed out — GameManager not found. Will retry on next SceneManager.sceneLoaded event.");

                // Fallback: try once more when any scene is loaded. This covers cases
                // where GameManager is created slightly later or by a scene-activation
                // callback that raced with our coroutine.
                void OnSceneLoaded(Scene scene, LoadSceneMode mode)
                {
                    var gm2 = GameManager.Instance;
                    if (gm2 == null)
                    {
                        try
                        {
                            gm2 = FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
                        }
                        catch
                        {
                            gm2 = FindObjectOfType<GameManager>(true);
                        }
                    }

                    if (gm2 != null)
                    {
                        Debug.Log("[EcopolyNetworkManager] GameManager found after sceneLoaded; initialising game.");
                        SceneManager.sceneLoaded -= OnSceneLoaded;
                        StartCoroutine(InitGameWhenReady(players));
                    }
                }

                SceneManager.sceneLoaded += OnSceneLoaded;
                yield break;
            }

            Debug.Log($"[Network] Initialising game with {players?.Count ?? 0} player(s).");

            // Broadcast the player list to clients BEFORE starting the server turn loop.
            // This ensures clients have populated their GameManager.Players before any
            // TurnStarted / PlayerMoved RPCs arrive.
            if (players != null && players.Count > 0)
            {
                // Build a reverse map: playerId → clientId
                var playerIdToClient = new Dictionary<int, ulong>();
                foreach (var kvp in _clientIdToPlayerId)
                    playerIdToClient[kvp.Value] = kvp.Key;

                var parts = new string[players.Count];
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    ulong cid = playerIdToClient.TryGetValue(p.PlayerId, out var id) ? id : ulong.MaxValue;
                    parts[i] = $"{p.PlayerId}|{p.PlayerName}|{(p.IsBot ? 1 : 0)}|{cid}|{p.AnimalIndex}";
                }
                SyncInitGameClientRpc(string.Join("\n", parts));

                // Give clients time to process the init RPC and run their own InitGame
                // before we emit GameStarted → TurnStarted → TurnManager RPCs.
                // Clients wait 5 frames internally; we wait 15 to cover slower machines.
                for (int i = 0; i < 15; i++)
                    yield return null;
            }

            gm.InitGame(players);

            // Notify all clients that the game has started so local systems
            // (AudioManager, etc.) that listen to GameStarted can react.
            SyncGameStartedClientRpc();
        }

        // --- Connection
        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[Network] Client connected: {clientId}. Connected: {NetworkManager.Singleton.ConnectedClients.Count}/{_expectedPlayerCount}");

            if (_gameBoardReady)
            {
                // Late joiner after scene already loaded — spawn immediately.
                SpawnPlayerForClient(clientId);
                return;
            }

            // Accumulate until all expected players are connected, then trigger
            // the scene load. NGO broadcasts it to every already-connected client
            // simultaneously, avoiding the late-joiner sync problem.
            _pendingClientSpawns.Add(clientId);
            TryLoadGameBoard();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"[Network] Client disconnected: {clientId}");
            _networkPlayers.Remove(clientId);
        }

        private void SpawnPlayerForClient(ulong clientId)
        {
            if (_networkPlayers.ContainsKey(clientId)) return; // guard against double-spawn

            // Pre-register with null so repeated calls are blocked even before
            // NetworkPlayerState.OnNetworkSpawn has a chance to register itself.
            _networkPlayers[clientId] = null;

            var playerObj = Instantiate(_playerNetworkPrefab);

            // Assign playerId by insertion order (matches _pendingPlayerList index).
            int playerId = _clientIdToPlayerId.Count;
            _clientIdToPlayerId[clientId] = playerId;

            // Position players slightly apart for identification in the scene.
            // Note: NetworkObjects CANNOT be parented under a non-NetworkObject at
            // spawn time. Players_Root is a plain Transform, so we do NOT parent here —
            // spawned objects live at scene root as required by NGO.
            playerObj.transform.localPosition = new Vector3(playerId * 0.6f, 0f, 0f);

            var netObj = playerObj.GetComponent<NetworkObject>();
            netObj.SpawnAsPlayerObject(clientId);

            // Stamp the PlayerId on the server-side PlayerController immediately.
            var pc = playerObj.GetComponent<Ecopoly.Player.PlayerController>();
            if (pc != null)
                pc.PlayerId = playerId;

            var networkPlayerState = playerObj.GetComponent<NetworkPlayerState>();
            if (networkPlayerState != null)
                _networkPlayers[clientId] = networkPlayerState;

            Debug.Log($"[Network] Spawned PRF_Player for clientId={clientId}, playerId={playerId}.");
        }

        private void TryLoadGameBoard()
        {
            if (_pendingPlayerList == null || _expectedPlayerCount <= 0) return;
            if (_gameBoardReady) return; // already loaded
            if (_gameBoardLoadRequested) return;

            // ConnectedClients on the host includes the host itself (clientId 0).
            if (NetworkManager.Singleton.ConnectedClients.Count < _expectedPlayerCount) return;

            Debug.Log($"[Network] All {_expectedPlayerCount} players connected. Loading {GameBoardSceneName} via NGO SceneManager.");
            var status = NetworkManager.Singleton.SceneManager.LoadScene(GameBoardSceneName, LoadSceneMode.Single);
            if (status == SceneEventProgressStatus.Started || status == SceneEventProgressStatus.SceneEventInProgress)
            {
                _gameBoardLoadRequested = true;
            }
            else
            {
                Debug.LogError($"[EcopolyNetworkManager] NetworkSceneManager.LoadScene failed with status: {status}");
            }
        }

        public void RegisterNetworkPlayer(ulong clientId, NetworkPlayerState playerState)
        {
            _networkPlayers[clientId] = playerState;
        }

        // --- Sync RPCs
        private bool ShouldIgnoreLocalEcho()
            => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        /// <summary>
        /// Server → all clients: initializes the game with the authoritative player list.
        /// Each entry encodes "playerId|playerName|isBot|clientId" joined by newlines.
        /// </summary>
        [ClientRpc]
        public void SyncInitGameClientRpc(string encodedPlayers)
        {
            if (ShouldIgnoreLocalEcho()) return;
            Debug.Log("[EcopolyNetworkManager] SyncInitGameClientRpc received on client.");
            StartCoroutine(InitClientGameWhenReady(encodedPlayers.Split('\n')));
        }

        private IEnumerator InitClientGameWhenReady(string[] encodedPlayers)
        {
            // Wait a few frames for NGO-spawned PlayerController objects to be active.
            for (int i = 0; i < 5; i++)
                yield return null;

            if (GameManager.Instance == null)
                GameManager.EnsureInstanceExists();

            var players = new List<PlayerState>(encodedPlayers.Length);

            // Build clientId→playerId map from encoded data
            var clientIdToPlayerId = new Dictionary<ulong, int>();
            foreach (var entry in encodedPlayers)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                var parts = entry.Split('|');
                if (parts.Length < 4) continue;
                int pid         = int.Parse(parts[0]);
                int animalIndex = parts.Length >= 5 && int.TryParse(parts[4], out int ai) ? ai : 0;
                players.Add(new PlayerState
                {
                    PlayerId    = pid,
                    PlayerName  = parts[1],
                    IsBot       = parts[2] == "1",
                    AnimalIndex = animalIndex,
                });
                if (ulong.TryParse(parts[3], out ulong ownerClientId))
                    clientIdToPlayerId[ownerClientId] = pid;
            }

            // Stamp PlayerId on every spawned PlayerController by matching NetworkObject.OwnerClientId
            var controllers = FindObjectsByType<Ecopoly.Player.PlayerController>(FindObjectsSortMode.None);
            foreach (var ctrl in controllers)
            {
                var netObj = ctrl.GetComponent<NetworkObject>();
                if (netObj != null && clientIdToPlayerId.TryGetValue(netObj.OwnerClientId, out int pid))
                    ctrl.PlayerId = pid;
            }

            // Resolve the local player's PlayerId from this client's own LocalClientId.
            // This is the authoritative mapping — avoids name-matching ambiguity.
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            int localPlayerIdHint = clientIdToPlayerId.TryGetValue(localClientId, out int localPid) ? localPid : -1;
            Debug.Log($"[EcopolyNetworkManager] Client LocalClientId={localClientId} → localPlayerIdHint={localPlayerIdHint}");

            var gm = GameManager.Instance;
            if (gm != null)
            {
                Debug.Log($"[EcopolyNetworkManager] Client InitGame with {players.Count} player(s). localPlayerIdHint={localPlayerIdHint}");
                gm.InitGame(players, localPlayerIdHint);
            }
            else
                Debug.LogWarning("[EcopolyNetworkManager] SyncInitGameClientRpc: GameManager not ready on client.");
        }

        private void OnTurnStarted(object payload)
        {
            if (payload is PlayerState player)
            {
                Debug.Log($"[EcopolyNetworkManager] OnTurnStarted (server) for PlayerId={player.PlayerId} Name={player.PlayerName}");
                SyncTurnStartedClientRpc(player.PlayerId);
            }
        }

        private void OnTurnEnded(object payload)
        {
            if (payload is PlayerState player)
                SyncTurnEndedClientRpc(player.PlayerId);
        }

        private void OnDiceRolled(object payload)
        {
            if (payload is DiceRollPayload roll)
                SyncDiceRollClientRpc(roll.Die1, roll.Die2);
        }

        private void OnPlayerMoved(object payload)
        {
            if (payload is PlayerMovePayload move)
                SyncPlayerMovedClientRpc(move.PlayerId, move.NewPosition);
        }

        private void OnPlayerLanded(object payload)
        {
            if (payload is PlayerLandedPayload landed)
                SyncPlayerLandedClientRpc(landed.PlayerId, landed.Position);
        }

        private void OnPlayerJailed(object payload)
        {
            if (payload is int playerId)
                SyncPlayerJailedClientRpc(playerId);
        }

        private void OnPlayerReleasedFromJail(object payload)
        {
            if (payload is int playerId)
                SyncPlayerReleasedFromJailClientRpc(playerId);
        }

        private void OnPlayerPassedGo(object payload)
        {
            if (payload is int playerId)
                SyncPlayerPassedGoClientRpc(playerId);
        }

        private void OnMoneyChanged(object payload)
        {
            if (payload is MoneyChangePayload money)
                SyncMoneyChangedClientRpc(money.PlayerId, money.NewValue);
        }

        private void OnCEPChanged(object payload)
        {
            if (payload is CEPChangePayload cep)
                SyncCEPChangedClientRpc(cep.PlayerId, cep.NewValue);
        }

        private void OnPropertyPurchased(object payload)
        {
            if (payload is PropertyEventPayload purchase)
                SyncPropertyPurchasedClientRpc(purchase.PlayerId, purchase.PropertyId);
        }

        private void OnPropertyRenovated(object payload)
        {
            if (payload is RenovationEventPayload r)
                SyncPropertyRenovatedClientRpc(r.PlayerId, r.PropertyId, r.OldLevel, r.NewLevel);
        }

        private void OnChanceCardDrawn(object payload)
        {
            if (payload is ChanceCardData card)
                SyncChanceCardDrawnClientRpc(card.cardId);
        }

        private void OnPlayerHandChanged(object payload)
        {
            if (!(payload is int playerId)) return;
            var player = GameManager.Instance?.GetPlayer(playerId);
            if (player == null) return;
            string encoded = player.CardHandIds.Count > 0
                ? string.Join("\n", player.CardHandIds)
                : string.Empty;
            SyncPlayerHandChangedClientRpc(playerId, encoded);
        }

        private void OnUIDilemmaVoteRequested(object payload)
        {
            if (!(payload is DilemmaVotePayload data)) return;
            // Broadcast UI to all non-host clients
            SyncDilemmaVoteRequestedClientRpc(data.CardId, data.DisplayText, data.CostPerPlayer, data.CEPEffect);
            // Begin server-side vote tally (includes bot simulation)
            BeginDilemmaVoteTally(data);
        }

        private void OnEventCardDrawn(object payload)
        {
            if (payload is EventCardData card)
                SyncEventCardDrawnClientRpc(card.cardId);
        }

        private void OnUICardDisplayRequested(object payload)
        {
            if (payload is PropertyOfferPayload offer)
                SyncUICardDisplayRequestedClientRpc(offer.PlayerId, offer.PropertyId);
        }

        private void OnUIRenovationRequested(object payload)
        {
            if (payload is RenovationOfferPayload offer)
                SyncUIRenovationRequestedClientRpc(offer.PlayerId, offer.PropertyId, offer.CurrentLevel);
        }

        private void OnUINotification(object payload)
        {
            if (payload is UINotificationPayload notification)
                SyncUINotificationClientRpc(notification.Message, notification.Color.HasValue, notification.Color ?? Color.white, notification.Duration, notification.Priority, notification.PlayerId);
        }

        private void OnGameEnded(object payload)
        {
            if (payload is PlayerState winner)
                SyncGameEndedClientRpc(winner.PlayerId);
        }

        private void OnGlobalGameOver(object _)
        {
            SyncGlobalGameOverClientRpc();
        }

        // --- New server-side handlers
        private void OnPropertySold(object payload)
        {
            if (payload is PropertyEventPayload p)
                SyncPropertySoldClientRpc(p.PlayerId, p.PropertyId);
        }

        private void OnPropertyDegraded(object payload)
        {
            if (payload is RenovationEventPayload r)
                SyncPropertyDegradedClientRpc(r.PlayerId, r.PropertyId, r.OldLevel, r.NewLevel);
        }

        private void OnGlobalCEPChanged(object payload)
        {
            if (payload is int globalCEP)
            {
                int intensityLevel = GameManager.Instance != null
                    ? GameManager.Instance.CurrentIntensityLevel
                    : 1;
                SyncGlobalCEPClientRpc(globalCEP, intensityLevel);
            }
        }

        private void OnGlobalCEPThresholdChanged(object payload)
        {
            // Piggy-backed into SyncGlobalCEPClientRpc via OnGlobalCEPChanged
            // (both fire together from GameManager.CheckGlobalThresholds).
            // No separate RPC needed — the intensity is included in SyncGlobalCEPClientRpc.
        }

        private void OnPlayerEliminated(object payload)
        {
            if (payload is int playerId)
                SyncPlayerEliminatedClientRpc(playerId);
        }

        private void OnPlayerCEPMaxReached(object payload)
        {
            if (payload is int playerId)
                SyncPlayerCEPMaxReachedClientRpc(playerId);
        }

        private void OnPlayerBankrupt(object payload)
        {
            if (payload is int playerId)
                SyncPlayerBankruptClientRpc(playerId);
        }

        private void OnDistrictBuildingBuilt(object payload)
        {
            if (payload is PropertyEventPayload p)
                SyncDistrictBuildingBuiltClientRpc(p.PlayerId, p.PropertyId);
        }

        private void OnDistrictBuildingDestroyed(object payload)
        {
            // BoardController emits a raw groupId string for this event.
            if (payload is string groupId)
                SyncDistrictBuildingDestroyedClientRpc(groupId);
        }

        private void OnDilemmaCardResolved(object payload)
        {
            if (payload is bool paidByAll)
                SyncDilemmaCardResolvedClientRpc(paidByAll);
        }

        // --- New ClientRpcs
        /// <summary>Server → all: a property was sold or returned to the bank.</summary>
        [ClientRpc]
        public void SyncPropertySoldClientRpc(int playerId, string propertyId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var bc = BoardController.Instance;
            if (bc != null) bc.SetOwnerForSync(propertyId, -1);
            var player = GameManager.Instance?.GetPlayer(playerId);
            player?.OwnedPropertyIds.Remove(propertyId);
            EventBus.Emit(GameEvent.PropertySold,
                new PropertyEventPayload { PlayerId = playerId, PropertyId = propertyId });
        }

        /// <summary>Server → all: a property's renovation level dropped.</summary>
        [ClientRpc]
        public void SyncPropertyDegradedClientRpc(int playerId, string propertyId, int oldLevel, int newLevel)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var bc = BoardController.Instance;
            if (bc != null) bc.SetRenovationLevelForSync(propertyId, newLevel);
            EventBus.Emit(GameEvent.PropertyDegraded,
                new RenovationEventPayload { PlayerId = playerId, PropertyId = propertyId, OldLevel = oldLevel, NewLevel = newLevel });
        }

        /// <summary>Server → all: global CEP total and current intensity level.</summary>
        [ClientRpc]
        public void SyncGlobalCEPClientRpc(int globalCEP, int intensityLevel)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var gm = GameManager.Instance;
            int previousIntensity = gm != null ? gm.CurrentIntensityLevel : -1;
            if (gm != null) gm.SetGlobalCEPForSync(globalCEP, intensityLevel);
            EventBus.Emit(GameEvent.GlobalCEPChanged, globalCEP);
            // Only fire threshold event when the level actually changes to avoid
            // playing the threshold-changed audio clip on every CEP update.
            if (intensityLevel != previousIntensity)
                EventBus.Emit(GameEvent.GlobalCEPThresholdChanged, intensityLevel);
        }

        /// <summary>Server → all: a player was eliminated (bankruptcy or CEP max).</summary>
        [ClientRpc]
        public void SyncPlayerEliminatedClientRpc(int playerId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var player = GameManager.Instance?.GetPlayer(playerId);
            if (player != null) player.IsEliminated = true;
            EventBus.Emit(GameEvent.PlayerEliminated, playerId);
        }

        /// <summary>Server → all: a player hit the personal CEP maximum.</summary>
        [ClientRpc]
        public void SyncPlayerCEPMaxReachedClientRpc(int playerId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.PlayerCEPMaxReached, playerId);
        }

        /// <summary>Server → all: a player went bankrupt (money &lt; 0).</summary>
        [ClientRpc]
        public void SyncPlayerBankruptClientRpc(int playerId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.PlayerBankrupt, playerId);
        }

        /// <summary>Server → all: a district building was constructed.</summary>
        [ClientRpc]
        public void SyncDistrictBuildingBuiltClientRpc(int playerId, string groupId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.DistrictBuildingBuilt,
                new PropertyEventPayload { PlayerId = playerId, PropertyId = groupId });
        }

        /// <summary>Server → all: a district building was destroyed (monopoly lost).</summary>
        [ClientRpc]
        public void SyncDistrictBuildingDestroyedClientRpc(string groupId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.DistrictBuildingDestroyed, groupId);
        }

        /// <summary>Server → all: a dilemma card was fully resolved.</summary>
        [ClientRpc]
        public void SyncDilemmaCardResolvedClientRpc(bool paidByAll)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.DilemmaCardResolved, paidByAll);
        }

        /// <summary>Server → all: a player purchased a property.</summary>
        [ClientRpc]
        public void SyncPropertyPurchasedClientRpc(int playerId, string propertyId)
        {
            if (ShouldIgnoreLocalEcho()) return;

            // Mutate client-side BoardController ownership map and player state so that
            // PropertiesPanelUI.GetOwner() and OwnedPropertyIds reflect reality on clients.
            var gm = GameManager.Instance;
            var player = gm?.GetPlayer(playerId);
            var bc = BoardController.Instance;
            if (bc != null)
                bc.SetOwnerForSync(propertyId, playerId);
            if (player != null && !player.OwnedPropertyIds.Contains(propertyId))
                player.OwnedPropertyIds.Add(propertyId);

            EventBus.Emit(GameEvent.PropertyPurchased,
                new PropertyEventPayload { PlayerId = playerId, PropertyId = propertyId });
        }

        [ClientRpc]
        public void SyncPropertyRenovatedClientRpc(int playerId, string propertyId, int oldLevel, int newLevel)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.PropertyRenovated,
                new RenovationEventPayload { PlayerId = playerId, PropertyId = propertyId, OldLevel = oldLevel, NewLevel = newLevel });
        }

        [ClientRpc]
        public void SyncChanceCardDrawnClientRpc(string cardId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var card = CardManager.GetChanceCard(cardId);
            if (card != null)
                EventBus.Emit(GameEvent.ChanceCardDrawn, card);
        }

        /// <summary>Server → all: a player's card hand changed (card added or removed).</summary>
        [ClientRpc]
        public void SyncPlayerHandChangedClientRpc(int playerId, string encodedCardIds)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var player = GameManager.Instance?.GetPlayer(playerId);
            if (player == null) return;
            player.CardHandIds.Clear();
            if (!string.IsNullOrEmpty(encodedCardIds))
                player.CardHandIds.AddRange(encodedCardIds.Split('\n'));
            EventBus.Emit(GameEvent.PlayerHandChanged, playerId);
        }

        [ClientRpc]
        public void SyncEventCardDrawnClientRpc(string cardId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var card = CardManager.GetEventCard(cardId);
            if (card != null)
                EventBus.Emit(GameEvent.EventCardDrawn, card);
            else
                Debug.LogError($"[EcopolyNetworkManager] SyncEventCardDrawnClientRpc: EventCard '{cardId}' not found on client. Check that SO_EventCards are in Resources/{Ecopoly.Utils.Constants.SO_EVENT_CARDS_FOLDER}.");
        }

        [ClientRpc]
        public void SyncUICardDisplayRequestedClientRpc(int playerId, string propertyId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.UICardDisplayRequested,
                new PropertyOfferPayload { PlayerId = playerId, PropertyId = propertyId });
        }

        [ClientRpc]
        public void SyncUIRenovationRequestedClientRpc(int playerId, string propertyId, int currentLevel)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.UIRenovationRequested,
                new RenovationOfferPayload { PlayerId = playerId, PropertyId = propertyId, CurrentLevel = currentLevel });
        }

        [ClientRpc]
        public void SyncUINotificationClientRpc(string message, bool hasColor, Color color, float duration, int priority, int playerId = -1)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.UINotification, new UINotificationPayload
            {
                Message = message,
                Color = hasColor ? color : null,
                Duration = duration,
                Priority = priority,
                PlayerId = playerId,
            });
        }

        /// <summary>Server → all: a player's CEP changed.</summary>
        [ClientRpc]
        public void SyncCEPChangedClientRpc(int playerId, int newCEP)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var gameManager = GameManager.Instance;
            var player = gameManager != null ? gameManager.GetPlayer(playerId) : null;
            if (player != null) player.PersonalCEP = newCEP;
            EventBus.Emit(GameEvent.CEPChanged,
                new CEPChangePayload { PlayerId = playerId, NewValue = newCEP });
        }

        [ClientRpc]
        public void SyncMoneyChangedClientRpc(int playerId, int newMoney)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var gameManager = GameManager.Instance;
            var player = gameManager != null ? gameManager.GetPlayer(playerId) : null;
            if (player != null) player.Money = newMoney;
            EventBus.Emit(GameEvent.MoneyChanged,
                new MoneyChangePayload { PlayerId = playerId, NewValue = newMoney });
        }

        [ClientRpc]
        public void SyncTurnStartedClientRpc(int playerId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var gameManager = GameManager.Instance;
            var player = gameManager != null ? gameManager.GetPlayer(playerId) : null;
            if (player == null)
                player = new PlayerState { PlayerId = playerId, PlayerName = $"Player {playerId}" };
            if (player != null) EventBus.Emit(GameEvent.TurnStarted, player);
        }

        [ClientRpc]
        public void SyncTurnEndedClientRpc(int playerId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var gameManager = GameManager.Instance;
            var player = gameManager != null ? gameManager.GetPlayer(playerId) : null;
            if (player == null)
                player = new PlayerState { PlayerId = playerId, PlayerName = $"Player {playerId}" };
            if (player != null) EventBus.Emit(GameEvent.TurnEnded, player);
        }

        [ClientRpc]
        public void SyncPlayerMovedClientRpc(int playerId, int newPosition)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var gameManager = GameManager.Instance;
            var player = gameManager != null ? gameManager.GetPlayer(playerId) : null;
            if (player != null) player.BoardPosition = newPosition;
            EventBus.Emit(GameEvent.PlayerMoved,
                new PlayerMovePayload { PlayerId = playerId, NewPosition = newPosition });
        }

        [ClientRpc]
        public void SyncPlayerLandedClientRpc(int playerId, int position)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.PlayerLanded,
                new PlayerLandedPayload { PlayerId = playerId, Position = position });
        }

        [ClientRpc]
        public void SyncPlayerJailedClientRpc(int playerId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var gameManager = GameManager.Instance;
            var player = gameManager != null ? gameManager.GetPlayer(playerId) : null;
            if (player != null)
            {
                player.IsInJail = true;
                player.JailTurnsRemaining = Constants.MAX_JAIL_TURNS;
                player.BoardPosition = Constants.JAIL_POSITION;
            }
            EventBus.Emit(GameEvent.PlayerJailed, playerId);
        }

        [ClientRpc]
        public void SyncPlayerReleasedFromJailClientRpc(int playerId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var gameManager = GameManager.Instance;
            var player = gameManager != null ? gameManager.GetPlayer(playerId) : null;
            if (player != null)
            {
                player.IsInJail = false;
                player.JailTurnsRemaining = 0;
            }
            EventBus.Emit(GameEvent.PlayerReleasedFromJail, playerId);
        }

        [ClientRpc]
        public void SyncPlayerPassedGoClientRpc(int playerId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.PlayerPassedGo, playerId);
        }

        [ClientRpc]
        public void SyncGameEndedClientRpc(int winnerPlayerId)
        {
            if (ShouldIgnoreLocalEcho()) return;
            var gameManager = GameManager.Instance;
            var winner = gameManager != null ? gameManager.GetPlayer(winnerPlayerId) : null;
            if (winner != null)
                EventBus.Emit(GameEvent.GameEnded, winner);
        }

        [ClientRpc]
        public void SyncGlobalGameOverClientRpc()
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.GlobalGameOver);
        }

        /// <summary>Server → all clients: game has started. Triggers local audio and UI systems.</summary>
        [ClientRpc]
        public void SyncGameStartedClientRpc()
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.GameStarted);
        }

        /// <summary>Server → all: dice roll result.</summary>
        [ClientRpc]
        public void SyncDiceRollClientRpc(int die1, int die2)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.DiceRolled,
                new DiceRollPayload { Die1 = die1, Die2 = die2 });
        }

        /// <summary>Client → Server: request to roll dice.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestDiceRollServerRpc()
        {
            if (IsServer) TurnManager.Instance.RollDice();
        }

        /// <summary>Client → Server: play a movement card (bicycle/car/plane) with a chosen step count.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestPlayMoveCardServerRpc(string cardId, int chosenSteps, int playerId)
        {
            var current = TurnManager.Instance?.CurrentPlayer;
            if (current != null && current.PlayerId == playerId && !current.IsBot)
                TurnManager.Instance.PlayMoveCard(cardId, chosenSteps);
        }

        /// <summary>Client → Server: use a Get Out of Jail card from the player's inventory.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestUseGetOutOfJailCardServerRpc(int playerId)
        {
            var current = TurnManager.Instance?.CurrentPlayer;
            if (current != null && current.PlayerId == playerId && !current.IsBot)
                TurnManager.Instance.UseGetOutOfJailCard();
        }

        /// <summary>Client → Server: buy property request.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestBuyPropertyServerRpc(string propertyId, int playerId)
        {
            var player = GameManager.Instance.GetPlayer(playerId);
            if (player != null)
                BoardController.Instance.BuyProperty(player, propertyId);
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestPayBailServerRpc(int playerId)
        {
            var current = TurnManager.Instance?.CurrentPlayer;
            if (current != null && current.PlayerId == playerId)
                TurnManager.Instance.PayBailToLeaveJail();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestReadyForNextTurnServerRpc(int playerId)
        {
            var current = TurnManager.Instance?.CurrentPlayer;
            if (current != null && current.PlayerId == playerId && !current.IsBot)
                TurnManager.Instance.ConfirmReadyForNextTurn();
        }

        /// <summary>Client → Server: client dismissed the renovation UI; release the server wait.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestDismissRenovationServerRpc(int playerId)
        {
            var current = TurnManager.Instance?.CurrentPlayer;
            if (current != null && current.PlayerId == playerId)
                TurnManager.Instance.DismissRenovationOffer();
        }

        /// <summary>Client → Server: request to renovate a property.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestRenovatePropertyServerRpc(string propertyId, int playerId)
        {
            var player = GameManager.Instance.GetPlayer(playerId);
            if (player != null)
                BoardController.Instance.RenovateProperty(player, propertyId);
        }

        /// <summary>Client → Serveur : vente d'une propriété.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestSellPropertyServerRpc(string propertyId, int playerId)
        {
            var player = GameManager.Instance.GetPlayer(playerId);
            if (player != null)
                BoardController.Instance.SellProperty(player, propertyId);
        }

        /// <summary>Client → Server: request to build a district building.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestBuildDistrictBuildingServerRpc(string groupId, int playerId, bool isCommercial)
        {
            var player = GameManager.Instance?.GetPlayer(playerId);
            var bc = BoardController.Instance;
            if (player == null || bc == null) return;

            string buildingType = isCommercial ? "commercial" : "ecological";
            var building = Resources.FindObjectsOfTypeAll<Ecopoly.Data.DistrictBuildingData>()
                .FirstOrDefault(b =>
                    b.buildingType == (isCommercial
                        ? Ecopoly.Data.DistrictBuildingType.Commercial
                        : Ecopoly.Data.DistrictBuildingType.Ecological)
                    && b.buildingId != null && b.buildingId.Contains(groupId));

            if (building == null)
            {
                Debug.LogError($"[Network] DistrictBuildingData not found for group={groupId} type={buildingType}");
                return;
            }

            bc.BuildDistrictBuilding(player, groupId, building);
        }

        /// <summary>Server → all clients: a dilemma vote has been requested.</summary>
        [ClientRpc]
        public void SyncDilemmaVoteRequestedClientRpc(string cardId, string displayText, int costPerPlayer, int cepEffect)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.UIDilemmaVoteRequested, new DilemmaVotePayload
            {
                CardId        = cardId,
                DisplayText   = displayText,
                CostPerPlayer = costPerPlayer,
                CEPEffect     = cepEffect,
            });
        }

        // --- Dilemma vote – per-player tally
        private int _dilemmaYesVotes;
        private int _dilemmaNoVotes;
        private int _dilemmaExpectedVoters;
        private bool _dilemmaVoteOpen;

        /// <summary>
        /// Called on the server when a dilemma is triggered to set up per-player tallying
        /// and immediately simulate bot votes.
        /// </summary>
        public void BeginDilemmaVoteTally(DilemmaVotePayload payload)
        {
            if (!IsServer) return;

            _dilemmaYesVotes = 0;
            _dilemmaNoVotes  = 0;
            _dilemmaVoteOpen = true;

            var players = GameManager.Instance?.Players;
            if (players == null) { _dilemmaExpectedVoters = 1; return; }

            // Count only active players
            int humanCount = 0;
            int botCount   = 0;
            foreach (var p in players)
            {
                if (p.IsEliminated) continue;
                if (p.IsBot) botCount++;
                else         humanCount++;
            }
            _dilemmaExpectedVoters = Mathf.Max(1, humanCount + botCount);

            // Bots vote immediately on the server
            StartCoroutine(SimulateBotVotes(players, payload.CostPerPlayer));
        }

        private IEnumerator SimulateBotVotes(System.Collections.Generic.List<PlayerState> players, int costPerPlayer)
        {
            foreach (var p in players)
            {
                if (p.IsEliminated || !p.IsBot) continue;
                float delay = p.BotPersonality?.decisionDelay ?? 0.8f;
                yield return new WaitForSeconds(delay);
                var botBrain = BotBrain.GetForPlayer(p.PlayerId);
                bool vote = botBrain != null
                    ? botBrain.DecideDilemmaVote(costPerPlayer)
                    : (p.BotPersonality?.cooperation ?? 0.5f) >= 0.5f;
                TallyVote(vote);
            }
        }

        private void TallyVote(bool yes)
        {
            if (!IsServer || !_dilemmaVoteOpen) return;

            if (yes) _dilemmaYesVotes++;
            else     _dilemmaNoVotes++;

            int total = _dilemmaYesVotes + _dilemmaNoVotes;
            Debug.Log($"[EcopolyNetworkManager] Dilemma vote tally: {total}/{_dilemmaExpectedVoters}");

            if (total >= _dilemmaExpectedVoters)
                ResolveDilemmaVote();
        }

        private void ResolveDilemmaVote()
        {
            _dilemmaVoteOpen = false;
            bool allPaid = (_dilemmaNoVotes == 0);

            // Tell all non-host clients to close their UI and display the outcome
            SyncDilemmaVoteResultClientRpc(allPaid);

            // Emit locally so the host's UI also closes
            EventBus.Emit(GameEvent.DilemmaVoteResult, allPaid);

            // Unblock the server-side turn coroutine
            TurnManager.Instance?.SubmitDilemmaVote(allPaid);
        }

        /// <summary>Client → Server: a non-host client submits their individual vote.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void SubmitPlayerDilemmaVoteServerRpc(bool voted)
        {
            TallyVote(voted);
        }

        /// <summary>
        /// Called on the host/server path (no RPC needed) to register the host player's vote.
        /// </summary>
        public void RegisterHostDilemmaVote(bool voted)
        {
            TallyVote(voted);
        }

        /// <summary>Server → all clients: the vote is complete, close the UI and show the result.</summary>
        [ClientRpc]
        public void SyncDilemmaVoteResultClientRpc(bool allPaid)
        {
            if (ShouldIgnoreLocalEcho()) return;
            EventBus.Emit(GameEvent.DilemmaVoteResult, allPaid);
        }

        /// <summary>
        /// Server → all clients: spawn disaster VFX and apply weather change locally.
        /// <paramref name="cardId"/> identifies the EventCardData to look up.
        /// <paramref name="level"/> is the current intensity level (1-4).
        /// <paramref name="playerWorldPos"/> is the triggering player's world position.
        /// <paramref name="lakePos"/> is the board lake center world position.
        /// </summary>
        [ClientRpc]
        public void SyncDisasterVFXClientRpc(string cardId, int level,
            Vector3 playerWorldPos, Vector3 lakePos)
        {
            // Host already applied this inside DisasterResolver.Resolve on the server path.
            // Non-host clients apply it here.
            if (ShouldIgnoreLocalEcho()) return;

            var resolver = Cards.DisasterResolver.Instance;
            if (resolver == null)
            {
                Debug.LogWarning("[EcopolyNetworkManager] DisasterResolver not found on client.");
                return;
            }

            resolver.ApplyDisasterVFXLocal(cardId, level, playerWorldPos, lakePos);
        }
    }

}

