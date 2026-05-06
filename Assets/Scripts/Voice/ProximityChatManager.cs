using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Vivox;
using Ecopoly.Utils;
using Ecopoly.Core;
using Ecopoly.Network;

namespace Ecopoly.Voice
{
    /// <summary>
    /// Manages Vivox proximity voice chat.
    ///
    /// Lifecycle:
    ///   1. InitializeAsync()  — called once after UGS is ready (BootstrapController).
    ///   2. LoginAsync()       — called when the local player identity is known.
    ///   3. JoinChannelAsync() — called when the game session starts.
    ///   4. RegisterPawn()     — called for every player pawn (local + remote).
    ///   5. ShutdownAsync()    — called on scene exit or game over.
    ///
    /// In TopDown camera mode Vivox handles full-volume broadcast natively.
    /// In FPS mode the positional channel rolls off audio with distance via Set3DPosition.
    /// </summary>
    public class ProximityChatManager : MonoBehaviour
    {
        public static ProximityChatManager Instance { get; private set; }

        // --- Inspector
        [Header("Vivox Channel")]
        [Tooltip("Shared channel name — identical for every player in the match.")]
        [SerializeField] private string _channelName = "EcopolyProximity";

        [Header("3D Audio Properties")]
        [Tooltip("Max distance (metres) at which a speaker can be heard.")]
        [SerializeField] private int _audibleDistance        = 32;
        [Tooltip("Distance within which voice is heard at original volume.")]
        [SerializeField] private int _conversationalDistance = 4;
        [Tooltip("Fade intensity — higher = steeper roll-off.")]
        [SerializeField] private float _audioFadeIntensity  = 1.0f;
        [SerializeField] private AudioFadeModel _audioFadeModel = AudioFadeModel.InverseByDistance;

        [Header("Position Update")]
        [Tooltip("Seconds between Set3DPosition pushes to Vivox SDK.")]
        [SerializeField] private float _updateInterval = 0.2f;

        [Header("Development")]
        [Tooltip("Disable Vivox Acoustic Echo Cancellation. Required when testing with Multiplayer Play Mode (multiple instances share the same mic/speakers). Disable in production builds.")]
        [SerializeField] private bool _disableEchoCancellation = false;

        // --- State
        private readonly Dictionary<int, Transform> _pawnTransforms = new Dictionary<int, Transform>();
        private int       _localPlayerId = -1;
        private Transform _localPawn;
        private bool      _isTopDownMode;
        private float     _updateTimer;
        private bool      _isInChannel;
        private bool      _isMuted;
        private bool      _vivoxReady;   // true once InitializeAsync succeeded

        // --- Lifecycle
        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        private async void Start()
        {
            var lobby = LobbyManager.Instance;

            if (lobby == null)
            {
                Debug.LogWarning("[Voice] LobbyManager not found. Vivox will not be initialized.");
                return;
            }

            if (lobby.IsInitialized)
            {
                await RunVivoxStartupAsync(lobby.LocalPlayerId);
            }
            else
            {
                lobby.OnInitialized += OnLobbyInitialized;
            }
        }

        /// <summary>
        /// Full Vivox startup sequence: initialize → login → join channel.
        /// Guards against double-calls and propagates failure at every step.
        /// </summary>
        private async Task RunVivoxStartupAsync(string playerId)
        {
            if (_vivoxReady)
            {
                // Already initialized — just ensure we are logged in and in the channel.
                await LoginAsync(playerId);
                await JoinChannelAsync();
                return;
            }

            bool ok = await InitializeAsync();
            if (!ok) return;

            _vivoxReady = true;
            await LoginAsync(playerId);
            await JoinChannelAsync();
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.CameraSwitched,   OnCameraSwitched);
            EventBus.On(GameEvent.PlayerEliminated, OnPlayerEliminated);
            EventBus.On(GameEvent.GameEnded,        OnGameEnded);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.CameraSwitched,   OnCameraSwitched);
            EventBus.Off(GameEvent.PlayerEliminated, OnPlayerEliminated);
            EventBus.Off(GameEvent.GameEnded,        OnGameEnded);
        }

        private void Update()
        {
            // Only push position updates in FPS mode with a registered local pawn
            if (!_isInChannel || _isTopDownMode || _localPawn == null) return;
            if (VivoxService.Instance == null) return;

            _updateTimer += Time.deltaTime;
            if (_updateTimer < _updateInterval) return;
            _updateTimer = 0f;

            VivoxService.Instance.Set3DPosition(_localPawn.gameObject, _channelName);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // Unsubscribe in case we're destroyed before LobbyManager fires
            if (LobbyManager.Instance != null)
                LobbyManager.Instance.OnInitialized -= OnLobbyInitialized;

            _ = ShutdownAsync();
        }

        // --- Public API
        /// <summary>
        /// Initializes the Vivox service. Call once after UnityServices.InitializeAsync().
        /// Returns true if Vivox is available and initialized successfully.
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            // VivoxService.Instance is populated by the UGS SDK after UnityServices.InitializeAsync().
            // In a standalone build it can take longer than in-editor — poll up to 10 s before giving up.
            int retries = 0;
            const int maxRetries = 50;          // 50 × 200 ms = 10 s
            while (VivoxService.Instance == null && retries < maxRetries)
            {
                await System.Threading.Tasks.Task.Delay(200);
                retries++;
            }

            if (VivoxService.Instance == null)
            {
                Debug.LogError("[Voice] VivoxService not available after 10 s. " +
                    "Ensure the Vivox package is installed, UGS is initialized, and Vivox credentials are set " +
                    "either in Project Settings > Services > Vivox or via SO_VivoxSettings.");
                return false;
            }

            try
            {
                await VivoxService.Instance.InitializeAsync();

                if (_disableEchoCancellation)
                {
                    VivoxService.Instance.DisableAcousticEchoCancellation();
                    Debug.Log("[Voice] AEC disabled (development mode).");
                }

                Debug.Log("[Voice] Vivox initialized.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Voice] Vivox InitializeAsync failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Logs the local user into Vivox. Requires UGS Authentication to be complete.
        /// </summary>
        public async Task LoginAsync(string displayName)
        {
            if (VivoxService.Instance == null) return;
            if (VivoxService.Instance.IsLoggedIn)
            {
                Debug.Log("[Voice] Already logged in to Vivox — skipping.");
                return;
            }

            try
            {
                var options = new LoginOptions { DisplayName = displayName };
                await VivoxService.Instance.LoginAsync(options);
                Debug.Log($"[Voice] Vivox login OK — '{displayName}'.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Voice] Vivox LoginAsync failed: {e.Message}");
            }
        }

        /// <summary>
        /// Joins the shared positional proximity channel.
        /// Call after LoginAsync() once the game scene is ready.
        /// </summary>
        public async Task JoinChannelAsync()
        {
            if (VivoxService.Instance == null) return;
            if (!VivoxService.Instance.IsLoggedIn)
            {
                Debug.LogError("[Voice] Cannot join channel — not logged in to Vivox.");
                return;
            }

            if (_isInChannel)
            {
                Debug.Log("[Voice] Already in proximity channel — skipping.");
                return;
            }

            try
            {
                var props = new Channel3DProperties(
                    _audibleDistance,
                    _conversationalDistance,
                    _audioFadeIntensity,
                    _audioFadeModel
                );

                await VivoxService.Instance.JoinPositionalChannelAsync(
                    _channelName,
                    ChatCapability.AudioOnly,
                    props
                );

                _isInChannel = true;
                Debug.Log($"[Voice] Joined positional channel '{_channelName}'.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Voice] JoinPositionalChannelAsync failed: {e.Message}");
            }
        }

        /// <summary>
        /// Registers a player pawn. Call after JoinChannelAsync() for every spawned pawn.
        /// </summary>
        public void RegisterPawn(int playerId, Transform pawnTransform, bool isLocal)
        {
            _pawnTransforms[playerId] = pawnTransform;

            if (isLocal)
            {
                _localPlayerId = playerId;
                _localPawn     = pawnTransform;
                Debug.Log($"[Voice] Local pawn registered — playerId={playerId}.");
            }
        }

        /// <summary>Unregisters a player pawn (e.g. on elimination or disconnect).</summary>
        public void UnregisterPawn(int playerId)
        {
            _pawnTransforms.Remove(playerId);
            if (playerId == _localPlayerId)
                _localPawn = null;
        }

        /// <summary>
        /// Mutes or unmutes the local microphone for all channels.
        /// </summary>
        public void SetLocalMute(bool muted)
        {
            if (VivoxService.Instance == null || !VivoxService.Instance.IsLoggedIn) return;

            _isMuted = muted;
            if (muted)
                VivoxService.Instance.MuteInputDevice();
            else
                VivoxService.Instance.UnmuteInputDevice();

            Debug.Log($"[Voice] Local mic muted = {muted}.");
        }

        /// <summary>
        /// Sets the local hearing volume for a specific remote participant.
        /// <paramref name="vivoxPlayerId"/> is VivoxParticipant.PlayerId (Vivox account ID, not game int ID).
        /// <paramref name="normalizedVolume"/> is clamped [0, 1] and mapped to Vivox range [-50, 50].
        /// </summary>
        public void SetRemoteParticipantVolume(string vivoxPlayerId, float normalizedVolume)
        {
            if (VivoxService.Instance == null || !_isInChannel || string.IsNullOrEmpty(vivoxPlayerId)) return;
            if (!VivoxService.Instance.ActiveChannels.TryGetValue(_channelName, out var participants)) return;

            foreach (var participant in participants)
            {
                if (participant.PlayerId == vivoxPlayerId && !participant.IsSelf)
                {
                    int vivoxVolume = Mathf.RoundToInt(Mathf.Lerp(-50f, 50f, Mathf.Clamp01(normalizedVolume)));
                    participant.SetLocalVolume(vivoxVolume);
                    return;
                }
            }
        }

        /// <summary>
        /// Leaves all Vivox channels and logs out. Called automatically on GameEnded / OnDestroy.
        /// </summary>
        public async Task ShutdownAsync()
        {
            if (VivoxService.Instance == null || !VivoxService.Instance.IsLoggedIn) return;

            try
            {
                if (_isInChannel)
                {
                    await VivoxService.Instance.LeaveAllChannelsAsync();
                    _isInChannel = false;
                    Debug.Log("[Voice] Left all Vivox channels.");
                }

                await VivoxService.Instance.LogoutAsync();
                Debug.Log("[Voice] Vivox logged out.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Voice] ShutdownAsync error: {e.Message}");
            }

            _pawnTransforms.Clear();
            _localPawn     = null;
            _localPlayerId = -1;
        }

        // --- Query helpers
        public bool IsMuted     => _isMuted;
        public bool IsInChannel => _isInChannel;

        // --- EventBus handlers
        private async void OnLobbyInitialized()
        {
            LobbyManager.Instance.OnInitialized -= OnLobbyInitialized;
            await RunVivoxStartupAsync(LobbyManager.Instance.LocalPlayerId);
        }

        private void OnCameraSwitched(object payload)
        {
            if (payload is CameraMode mode)
                _isTopDownMode = mode == CameraMode.TopDown;
        }

        private void OnPlayerEliminated(object payload)
        {
            if (payload is int playerId)
                UnregisterPawn(playerId);
        }

        private void OnGameEnded(object payload)
        {
            _ = ShutdownAsync();
        }
    }
}

