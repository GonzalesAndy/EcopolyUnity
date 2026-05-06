using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;
using Ecopoly.Network;
using Ecopoly.Core;
using Ecopoly.Utils;
using Ecopoly.Data;

namespace Ecopoly.UI
{
    /// <summary>
    /// Drives the Lobby scene UI.
    ///
    /// Layout:
    ///   Top bar   — lobby code + copy button + status text
    ///   Left col  — scrollable player list (spawns PRF_PlayerRow per player)
    ///   Right col — bot count slider (host-only) + Ready / Start / Leave buttons
    ///
    /// Host-only elements (Start Game button, bot slider) are hidden for clients.
    /// Polls LobbyManager every _refreshInterval seconds to stay in sync.
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] private TMP_Text  _lobbyCodeText;
        [SerializeField] private Button    _copyCodeButton;
        [SerializeField] private TMP_Text  _statusText;

        [Header("Player List")]
        [SerializeField] private Transform    _playerListRoot;
        [SerializeField] private GameObject   _playerRowPrefab;
        [SerializeField] private GameObject   _botRowPrefab;
        [SerializeField] private GameObject   _botRowClientPrefab;
        [SerializeField] private ScrollRect   _playerListScrollRect;

        [Header("Bot Settings (host only)")]
        [SerializeField] private GameObject _botSettingsPanel;
        [SerializeField] private Slider     _botCountSlider;
        [SerializeField] private TMP_Text   _botCountLabel;
        [SerializeField] private GameObject _ovalWhitePrefab;
        [SerializeField] private GameObject _ovalBluePrefab;

        [Header("Action Buttons")]
        [SerializeField] private Button _readyButton;
        [SerializeField] private Button _startGameButton;
        [SerializeField] private Button _leaveButton;

        [Header("Scenes")]
        [SerializeField] private string _mainMenuSceneName  = "MainMenu";
        [SerializeField] private string _gameBoardSceneName = "GameBoard";

        [Header("Polling")]
        [SerializeField] private float _refreshInterval = 2f;

        // --- State
        private readonly List<GameObject> _spawnedRows       = new List<GameObject>();
        private readonly List<string>     _latestPlayerNames  = new List<string>();
        private bool _ready;
        private bool _loadingGame;
        private int  _botCount;

        // Per-bot selected personality index (index into LoadBotPersonalities())
        private readonly List<int> _botPersonalitySelections = new List<int>();

        // --- Lifecycle
        private void OnEnable()
        {
            if (LobbyManager.Instance == null) return;
            LobbyManager.Instance.OnPlayersUpdated += RefreshPlayerList;
            LobbyManager.Instance.OnGameStarting   += OnGameStarting;
        }

        private void OnDisable()
        {
            if (LobbyManager.Instance == null) return;
            LobbyManager.Instance.OnPlayersUpdated -= RefreshPlayerList;
            LobbyManager.Instance.OnGameStarting   -= OnGameStarting;
        }

        private void Start()
        {
            if (_botCountSlider != null)
            {
                _botCountSlider.maxValue = LobbyManager.MAX_PLAYERS - 1;
                _botCountSlider.wholeNumbers = true;
                _botCountSlider.onValueChanged.AddListener(OnBotSliderChanged);
                if (LobbyManager.Instance.HasLobby) {
                    _botCountSlider.minValue = 0;
                    _botCountSlider.value = 0;
                } else {
                    _botCountSlider.minValue = 2;
                    _botCountSlider.value = 2;
                }
                OnBotSliderChanged(_botCountSlider.value);
            }

            // Seed from cached player names if lobby was already populated
            if (LobbyManager.Instance != null)
            {
                var cached = LobbyManager.Instance.GetCachedPlayerNames();
                if (cached.Count > 0) RefreshPlayerList(cached);
            }

            RefreshHeader();
            RefreshHostElements();
            UpdateReadyButtonLabel();
            StartCoroutine(PollLoop());
        }

        // --- Button handlers
        public void OnReadyClicked()
        {
            _ready = !_ready;
            UpdateReadyButtonLabel();
            SetStatus(_ready ? "Ready!" : string.Empty);
        }

        public void OnStartGameClicked(bool offline = false)
        {
            if (_loadingGame) return;
            if (!offline)
                _ = StartGameFlowAsync();
            else
                _ = StartSoloGameFlowAsync();
        }

        public void OnLeaveClicked()
        {
            if (_loadingGame) return;
            _ = LeaveFlowAsync();
        }

        public void OnCopyCodeClicked()
        {
            if (LobbyManager.Instance == null) return;
            string code = LobbyManager.Instance.CurrentLobbyCode;
            if (!string.IsNullOrWhiteSpace(code))
                GUIUtility.systemCopyBuffer = code;
        }

        // --- Async flows
        private async Task StartGameFlowAsync()
        {
            if (LobbyManager.Instance == null || !LobbyManager.Instance.IsHost) return;

            _loadingGame = true;
            SetStatus("Starting game...");
            SetAllButtonsInteractable(false);
            LobbyManager.Instance.SetBotPersonalities(GetSelectedBotPersonalities());

            // StartGameAsync: creates Relay allocation, publishes the join code to
            // the UGS lobby, configures UnityTransport, and calls StartHost().
            // The host remains in the Lobby scene — EcopolyNetworkManager will
            // call NetworkManager.SceneManager.LoadScene once all clients have
            // connected via Relay, ensuring every client receives the scene-load
            // event while already connected (avoids late-joiner sync issues).
            await LobbyManager.Instance.StartGameAsync();
            SetStatus("Waiting for players...");
        }

        private async Task StartSoloGameFlowAsync()
        {
            if (LobbyManager.Instance == null) return;

            // Solo mode has no UGS lobby, so IsHost is never set — skip that guard.
            _loadingGame = true;
            SetStatus("Starting solo game...");
            SetAllButtonsInteractable(false);

            LobbyManager.Instance.SetBotPersonalities(GetSelectedBotPersonalities());
            Debug.Log($"[LobbyController] Selected bot personalities: {string.Join(", ", GetSelectedBotPersonalities().Select(p => p != null ? p.botName : "null"))}");

            await LobbyManager.Instance.StartSoloGameAsync();
        }

        private async Task LeaveFlowAsync()
        {
            _loadingGame = true;
            SetStatus("Leaving...");
            SetAllButtonsInteractable(false);

            if (LobbyManager.Instance != null)
                await LobbyManager.Instance.LeaveLobbyAsync();

            SceneManager.LoadScene(_mainMenuSceneName);
        }

        // --- Polling
        private IEnumerator PollLoop()
        {
            var wait = new WaitForSeconds(_refreshInterval);
            while (!_loadingGame)
            {
                yield return wait;
                _ = PollTickAsync();
            }
        }

        private async Task PollTickAsync()
        {
            if (_loadingGame) return;
            if (LobbyManager.Instance == null || !LobbyManager.Instance.HasLobby) return;

            // One network call per tick — RefreshLobbyAsync fetches the latest lobby snapshot
            // and fires OnPlayersUpdated, which updates the player list via subscription.
            // HasGameStartedAsync / GetRelayCodeAsync previously each triggered an EXTRA
            // GetLobbyAsync call, hitting UGS rate limits and causing silent failures on the
            // client. Now we read started + relay from the already-fetched data via
            // TryGetCachedGameStart (zero additional network calls).
            await LobbyManager.Instance.RefreshLobbyAsync();

            if (_loadingGame) return; // guard against re-entry after await

            RefreshHeader();
            RefreshHostElements();

            // Clients: check if host started the game using cached lobby data
            if (!LobbyManager.Instance.IsHost)
            {
                if (LobbyManager.Instance.TryGetCachedGameStart(out string relayCode))
                {
                    _loadingGame = true;
                    SetStatus("Joining game...");
                    Debug.Log($"[LobbyController] Game started detected. Joining relay: {relayCode}");

                    // Connect to the host's Relay session. The host is still in the
                    // Lobby scene at this point — it will only load GameBoard once all
                    // expected clients are connected. We queue the player list here so
                    // GameManager._pendingPlayers is set before the NGO scene-load
                    // event unloads this scene.
                    await LobbyManager.Instance.JoinRelayAsync(relayCode);
                    QueueStartupPlayersFromLobby();
                    // GameBoard will be loaded by the server via NetworkManager.SceneManager
                    // once all clients are connected. NGO broadcasts the scene-load event
                    // to all already-connected clients — no manual LoadScene needed here.
                }
            }
        }

        // --- Player list
        private void RefreshPlayerList(List<string> playerNames)
        {
            if (_playerListRoot == null || _playerRowPrefab == null) return;
            if (!LobbyManager.Instance.IsHost)
               _botCount = LobbyManager.Instance.GetBotCount();
            Debug.Log($"[LobbyController] Refreshing player list. Received from LobbyManager: [{string.Join(", ", playerNames)}]. Current bot count: {_botCount}");

            // Bots are never stored in the UGS lobby — always derive them from _botCount.
            // Strip any bot entries from the incoming list so polls can't wipe local bots.
            var humans = playerNames?.Where(n => !n.StartsWith("Bot ")).ToList()
                         ?? new List<string>();

            var fullList = new List<string>(humans);
            for (int i = 0; i < _botCount; i++)
                fullList.Add($"Bot {i + 1}");

            int prevBotCount = _latestPlayerNames.Count(n => n.StartsWith("Bot "));
            int newBotCount  = _botCount;

            _latestPlayerNames.Clear();
            _latestPlayerNames.AddRange(fullList);

            // Reset personality selections only when bot count changes
            if (newBotCount != prevBotCount)
                _botPersonalitySelections.Clear();

            ClearPlayerRows();

            string localName = LobbyManager.Instance != null
                ? LobbyManager.Instance.LocalPlayerName
                : string.Empty;

            foreach (string playerName in _latestPlayerNames)
            {
                bool isBot = playerName.StartsWith("Bot ");
                GameObject prefab = (isBot && _botRowPrefab != null) ? (LobbyManager.Instance.IsHost ? _botRowPrefab : _botRowClientPrefab) : _playerRowPrefab;

                var row = Instantiate(prefab, _playerListRoot);
                _spawnedRows.Add(row);

                // Bot name goes into BotInfo/Text_UserName; player name into Text_UserName
                TMP_Text nameLabel = isBot
                    ? row.transform.Find("BotInfo/Text_UserName")?.GetComponent<TMP_Text>()
                    : row.transform.Find("Text_UserName")?.GetComponent<TMP_Text>();
                nameLabel ??= row.GetComponentInChildren<TMP_Text>();
                if (nameLabel != null) nameLabel.text = playerName;

                if (!isBot && string.Equals(playerName, localName, System.StringComparison.OrdinalIgnoreCase))
                {
                    var img = row.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.12f, 0.55f, 0.25f, 0.85f);
                }
            }

            // Populate oval selectors inside each spawned bot row
            UpdateBotPersonalityRows(_latestPlayerNames);
        }

        private void ClearPlayerRows()
        {
            foreach (var row in _spawnedRows)
                if (row != null) Destroy(row);
            _spawnedRows.Clear();
        }

        // --- Header / host helpers
        private void RefreshHeader()
        {
            if (_lobbyCodeText == null) return;
            string code = LobbyManager.Instance != null ? LobbyManager.Instance.CurrentLobbyCode : null;
            _lobbyCodeText.text = string.IsNullOrWhiteSpace(code) ? "—" : code;
        }

        private void RefreshHostElements()
        {
            bool isHost = LobbyManager.Instance != null && LobbyManager.Instance.IsHost;

            if (_startGameButton != null) _startGameButton.gameObject.SetActive(isHost);
            if (_botSettingsPanel != null) _botSettingsPanel.SetActive(isHost);
            if (_readyButton     != null) _readyButton.gameObject.SetActive(!isHost);
        }

        private void UpdateReadyButtonLabel()
        {
            if (_readyButton == null) return;
            var label = _readyButton.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = _ready ? "Unready" : "Ready";
        }

        // --- Bot slider
        public void OnBotSliderChanged(float value)
        {
            _botCount = Mathf.RoundToInt(value);
            if (_botCountLabel != null) _botCountLabel.text = _botCount.ToString();
            LobbyManager.Instance?.SetBotCount(_botCount);

            // Re-render with the updated bot count; RefreshPlayerList re-synthesizes bots from _botCount.
            RefreshPlayerList(_latestPlayerNames);
        }

        // --- Game startup player queue
        private void QueueStartupPlayersFromLobby()
        {
            if (GameManager.Instance == null) return;

            string localName = LobbyManager.Instance != null
                ? LobbyManager.Instance.LocalPlayerName
                : "Player";
            if (string.IsNullOrWhiteSpace(localName)) localName = "Player";

            var names = _latestPlayerNames.Count > 0
                ? new List<string>(_latestPlayerNames)
                : (LobbyManager.Instance != null
                    ? LobbyManager.Instance.GetCachedPlayerNames()
                    : new List<string>());

            if (!names.Any(n => string.Equals(n, localName, System.StringComparison.OrdinalIgnoreCase)))
                names.Insert(0, localName);

            // Add bots requested by host
            if (LobbyManager.Instance != null && LobbyManager.Instance.IsHost)
            {
                for (int i = 0; i < _botCount; i++)
                    names.Add($"Bot {i + 1}");
            }

            int cap = Mathf.Clamp(names.Count, Constants.MIN_PLAYERS, Constants.MAX_PLAYERS);

            var players = new List<PlayerState>(cap);
            for (int i = 0; i < cap; i++)
            {
                if (i >= names.Count) break;
                bool isBot = names[i].StartsWith("Bot ");
                players.Add(new PlayerState
                {
                    PlayerId     = i,
                    PlayerName   = names[i],
                    IsBot        = isBot,
                    IsEliminated = false,
                    PersonalCEP  = 0,
                    BoardPosition = 0,
                });
            }

            GameManager.Instance.QueueGameStart(players);
        }

        // --- Events
        private void OnGameStarting()
        {
            SetStatus("Game starting...");
        }

        // --- Shared helpers
        private void SetStatus(string message)
        {
            if (_statusText != null) _statusText.text = message;
        }

        private void SetAllButtonsInteractable(bool interactable)
        {
            if (_readyButton     != null) _readyButton.interactable      = interactable;
            if (_startGameButton != null) _startGameButton.interactable  = interactable;
            if (_leaveButton     != null) _leaveButton.interactable      = interactable;
        }

        // --- Bot Personality
        /// <summary>
        /// Called from RefreshPlayerList after bot rows are instantiated.
        /// Finds every Bot_Personalities container in the spawned bot rows and
        /// fills it with one oval per personality (white = unselected, blue = selected).
        /// </summary>
        public void UpdateBotPersonalityRows(List<string> playerNames)
        {
            if (_ovalWhitePrefab == null || _ovalBluePrefab == null) return;

            List<BotPersonalityData> personalities = LoadBotPersonalities();

            // Collect all bot rows in spawn order
            var botRows = _spawnedRows
                .Where(r => r != null)
                .Select(r => r.transform.Find("BotInfo/Bot_Personalities"))
                .Where(t => t != null)
                .ToList();

            // Ensure selection list has one entry per bot row (default: 0)
            while (_botPersonalitySelections.Count < botRows.Count)
                _botPersonalitySelections.Add(0);
            while (_botPersonalitySelections.Count > botRows.Count)
                _botPersonalitySelections.RemoveAt(_botPersonalitySelections.Count - 1);

            for (int botIndex = 0; botIndex < botRows.Count; botIndex++)
            {
                Transform container = botRows[botIndex];
                int currentBotIndex = botIndex; // capture for closure

                // Clear old ovals
                foreach (Transform child in container)
                    Destroy(child.gameObject);

                for (int pIndex = 0; pIndex < personalities.Count; pIndex++)
                {
                    int currentPIndex = pIndex; // capture for closure
                    bool isSelected   = _botPersonalitySelections[currentBotIndex] == currentPIndex;

                    GameObject prefab = isSelected ? _ovalBluePrefab : _ovalWhitePrefab;
                    GameObject oval   = Instantiate(prefab, container);

                    // Label the oval with the personality name
                    var label = oval.GetComponentInChildren<TMP_Text>();
                    if (label != null) label.text = personalities[currentPIndex].botName;

                    // Click to select
                    var btn = oval.GetComponent<Button>();
                    if (btn == null) btn = oval.AddComponent<Button>();
                    btn.onClick.AddListener(() =>
                    {
                        _botPersonalitySelections[currentBotIndex] = currentPIndex;
                        UpdateBotPersonalityRows(_latestPlayerNames);
                    });
                }
            }
        }

        private static List<BotPersonalityData> LoadBotPersonalities()
        {
            var personalities = new List<BotPersonalityData>();

    #if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:BotPersonalityData");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<BotPersonalityData>(path);
                if (asset != null && !personalities.Contains(asset))
                    personalities.Add(asset);
            }
    #endif

            if (personalities.Count == 0)
            {
                var fromResources = Resources.LoadAll<BotPersonalityData>(string.Empty);
                for (int i = 0; i < fromResources.Length; i++)
                {
                    if (fromResources[i] != null && !personalities.Contains(fromResources[i]))
                        personalities.Add(fromResources[i]);
                }
            }

            return personalities;
        }

        private List<BotPersonalityData> GetSelectedBotPersonalities()
        {
            List<BotPersonalityData> personalities = LoadBotPersonalities();
            var result = new List<BotPersonalityData>();

            for (int i = 0; i < _botPersonalitySelections.Count; i++)
            {
                int index = _botPersonalitySelections[i];
                result.Add(index >= 0 && index < personalities.Count ? personalities[index] : null);
            }

            return result;
        }
    }
}

