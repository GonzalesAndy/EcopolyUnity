using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Ecopoly.Network;
using Ecopoly.Core;
using Ecopoly.Utils;

namespace Ecopoly.UI
{
    /// <summary>
    /// Drives the Main Menu flow:
    ///  - Solo game
    ///  - Create multiplayer lobby
    ///  - Join lobby by code
    ///  - Quit
    ///
    /// Depends on LobbyManager singleton (Bootstrap scene) for UGS operations.
    /// All network calls are async; the _busy flag prevents double-submissions.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        // --- Inspector references
        [Header("Panels")]
        [SerializeField] private GameObject _playPanel;
        [SerializeField] private GameObject _joinLobbyPanel;

        [Header("Inputs")]
        [SerializeField] private TMP_InputField _lobbyCodeInput;
        [SerializeField] private TMP_InputField _playerNameInput;

        [Header("Feedback")]
        [SerializeField] private TMP_Text  _statusText;
        [SerializeField] private GameObject _loadingOverlay;

        [Header("Lobby Code Display")]
        [SerializeField] private GameObject _lobbyCodeDisplayPanel;
        [SerializeField] private TMP_Text   _lobbyCodeDisplayText;
        [SerializeField] private Button     _copyCodeButton;

        [Header("Buttons")]
        [SerializeField] private Button _btnPlay;
        [SerializeField] private Button _btnSolo;
        [SerializeField] private Button _btnCreateLobby;
        [SerializeField] private Button _btnJoinLobby;
        [SerializeField] private Button _btnQuit;

        [Header("Scenes")]
        [SerializeField] private string _lobbySceneName = "Lobby";
        [SerializeField] private string _soloLobbySceneName = "Lobby_solo";
        [SerializeField] private string _gameBoardSceneName = "GameBoard";

        // --- State
        private bool _busy;

        // --- Lifecycle
        private void Start()
        {
            HidePlayPanel();
            HideJoinPanel();
            HideLobbyCodeDisplay();
            HideLoadingOverlay();
            SetStatus(string.Empty);
            InitializeDefaultPlayerName();
            EnsureLobbyManagerInitialized();
        }

        private void OnEnable()
        {
            // LobbyManager may not exist yet when OnEnable fires before Start.
            // The subscription is also done at the end of EnsureLobbyManagerInitialized
            // after the instance has been guaranteed to exist.
            if (LobbyManager.Instance != null)
                LobbyManager.Instance.OnInitialized += OnLobbyManagerInitialized;
        }

        private void OnDisable()
        {
            if (LobbyManager.Instance != null)
                LobbyManager.Instance.OnInitialized -= OnLobbyManagerInitialized;
        }

        // --- Button handlers
        public void OnPlayClicked()
        {
            if (_busy) return;
            ShowPlayPanel();
        }

        public void OnPlayCancelClicked()
        {
            HidePlayPanel();
            SetStatus(string.Empty);
        }

        public void OnSoloClicked()
        {
            if (_busy) return;

            /*
            if (GameManager.Instance != null)
            {
                var players = GameManager.BuildOfflinePlayers(GetPlayerName(), Constants.MIN_PLAYERS);
                GameManager.Instance.QueueGameStart(players);
            }

            SceneManager.LoadScene(_gameBoardSceneName);
            */
            _ = CreateSoloLobbyFlowAsync();
        }

        public void OnCreateLobbyClicked()
        {
            if (_busy) return;
            _ = CreateLobbyFlowAsync();
        }

        public void OnJoinLobbyClicked()
        {
            if (_busy) return;
            HidePlayPanel();
            ShowJoinPanel();
        }

        public void OnJoinConfirmClicked()
        {
            if (_busy) return;
            _ = JoinLobbyFlowAsync();
        }

        public void OnJoinCancelClicked()
        {
            HideJoinPanel();
            ShowPlayPanel();
            SetStatus(string.Empty);
        }

        public void OnCopyCodeClicked()
        {
            if (LobbyManager.Instance == null) return;
            string code = LobbyManager.Instance.CurrentLobbyCode;
            if (!string.IsNullOrWhiteSpace(code))
                GUIUtility.systemCopyBuffer = code;
        }

        public void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // --- Async flows
        private async Task CreateLobbyFlowAsync()
        {
            _busy = true;
            ShowLoadingOverlay();
            SetStatus("Creating lobby...");
            SetMainButtonsInteractable(false);

            string playerName = GetPlayerName();
            string lobbyName  = $"{playerName}'s Lobby";

            string code = await LobbyManager.Instance.CreateLobbyAsync(lobbyName, playerName);
            HideLoadingOverlay();

            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatus("Failed to create lobby. Check your connection.");
                SetMainButtonsInteractable(true);
                _busy = false;
                return;
            }

            SetStatus($"Lobby created!");
            ShowLobbyCodeDisplay(code);
            // Give the player a moment to see/copy the code before transitioning
            await Task.Delay(1500);
            SceneManager.LoadScene(_lobbySceneName);
        }

        private async Task CreateSoloLobbyFlowAsync()
        {
            _busy = true;
            ShowLoadingOverlay();
            SetStatus("Creating solo lobby...");
            SetMainButtonsInteractable(false);

            string playerName = GetPlayerName();
            string lobbyName  = $"{playerName}'s Lobby";

            string code = await LobbyManager.Instance.CreateSoloLobbyAsync(lobbyName, playerName);
            HideLoadingOverlay();

            SetStatus($"Solo lobby created!");
            SceneManager.LoadScene(_soloLobbySceneName);
        }

        private async Task JoinLobbyFlowAsync()
        {
            string code = _lobbyCodeInput != null ? _lobbyCodeInput.text.Trim().ToUpperInvariant() : string.Empty;
            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatus("Enter a lobby code.");
                return;
            }

            _busy = true;
            ShowLoadingOverlay();
            SetStatus("Joining lobby...");

            string playerName = GetPlayerName();
            bool joined = await LobbyManager.Instance.JoinLobbyAsync(code, playerName);
            HideLoadingOverlay();

            if (!joined)
            {
                SetStatus("Join failed. Check the code and try again.");
                _busy = false;
                return;
            }

            SetStatus("Joined lobby!");
            await Task.Delay(500);
            SceneManager.LoadScene(_lobbySceneName);
        }

        // --- UGS guard
        /// <summary>
        /// Guarantees a LobbyManager exists and starts UGS initialization.
        /// When the Bootstrap scene ran, the persistent singleton is already present.
        /// When entering Play Mode directly from the MainMenu scene (editor workflow),
        /// we create one on the fly so multiplayer still works.
        /// </summary>
        private void EnsureLobbyManagerInitialized()
        {
            if (LobbyManager.Instance == null)
            {
                Debug.Log("[MainMenuController] LobbyManager not found — creating one for this session.");
                var go = new GameObject("LobbyManager [auto]");
                go.AddComponent<LobbyManager>();
                // Instance is set synchronously inside LobbyManager.Awake()
            }

            // Subscribe now that the instance is guaranteed to exist
            // (OnEnable fired before Start so the first subscribe may have been skipped)
            LobbyManager.Instance.OnInitialized -= OnLobbyManagerInitialized;
            LobbyManager.Instance.OnInitialized += OnLobbyManagerInitialized;

            if (LobbyManager.Instance.IsInitialized)
            {
                // Bootstrap already ran through the full init sequence
                OnLobbyManagerInitialized();
                return;
            }

            SetMultiplayerButtonsInteractable(false);
            SetStatus("Connecting to services...");
            _ = LobbyManager.Instance.InitializeAsync();
        }

        private void OnLobbyManagerInitialized()
        {
            SetMultiplayerButtonsInteractable(true);
            SetStatus(string.Empty);
        }

        // --- UI helpers
        private void InitializeDefaultPlayerName()
        {
            if (_playerNameInput != null && string.IsNullOrWhiteSpace(_playerNameInput.text))
                _playerNameInput.text = $"Player{Random.Range(1000, 9999)}";
        }

        private string GetPlayerName()
        {
            string name = _playerNameInput != null ? _playerNameInput.text.Trim() : string.Empty;
            return string.IsNullOrWhiteSpace(name) ? $"Player{Random.Range(1000, 9999)}" : name;
        }

        private void SetStatus(string message)
        {
            if (_statusText != null) _statusText.text = message;
        }

        private void ShowPlayPanel()
        {
            if (_playPanel != null) _playPanel.SetActive(true);
        }

        private void HidePlayPanel()
        {
            if (_playPanel != null) _playPanel.SetActive(false);
        }

        private void ShowJoinPanel()
        {
            if (_joinLobbyPanel != null) _joinLobbyPanel.SetActive(true);
        }

        private void HideJoinPanel()
        {
            if (_joinLobbyPanel != null) _joinLobbyPanel.SetActive(false);
        }

        private void ShowLobbyCodeDisplay(string code)
        {
            if (_lobbyCodeDisplayPanel != null) _lobbyCodeDisplayPanel.SetActive(true);
            if (_lobbyCodeDisplayText != null)  _lobbyCodeDisplayText.text = code;
        }

        private void HideLobbyCodeDisplay()
        {
            if (_lobbyCodeDisplayPanel != null) _lobbyCodeDisplayPanel.SetActive(false);
        }

        private void ShowLoadingOverlay()
        {
            if (_loadingOverlay != null) _loadingOverlay.SetActive(true);
        }

        private void HideLoadingOverlay()
        {
            if (_loadingOverlay != null) _loadingOverlay.SetActive(false);
        }

        private void SetMainButtonsInteractable(bool interactable)
        {
            if (_btnPlay != null)        _btnPlay.interactable        = interactable;
            if (_btnSolo != null)        _btnSolo.interactable        = interactable;
            if (_btnCreateLobby != null) _btnCreateLobby.interactable = interactable;
            if (_btnJoinLobby != null)   _btnJoinLobby.interactable   = interactable;
            if (_btnQuit != null)        _btnQuit.interactable         = interactable;
        }

        private void SetMultiplayerButtonsInteractable(bool interactable)
        {
            if (_btnCreateLobby != null) _btnCreateLobby.interactable = interactable;
            if (_btnJoinLobby != null)   _btnJoinLobby.interactable   = interactable;
        }
    }
}

