using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;
using Ecopoly.Utils;
using Ecopoly.Core;
using Ecopoly.Player;

namespace Ecopoly.Camera
{
    /// <summary>
    /// Handles switching between the FPS camera (Cinemachine Virtual Camera)
    /// and the top-down orthographic camera (Cinemachine Virtual Camera).
    /// Attached to the CameraRig in the GameBoard scene.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        public static CameraController Instance { get; private set; }

        [Header("Cinemachine Cameras")]
        [SerializeField] private CinemachineCamera _fpsCam;
        [SerializeField] private CinemachineCamera _topDownCam;

        [Header("Top-down View Parameters")]
        [SerializeField] private float _topDownZoomMin  = 10f;
        [SerializeField] private float _topDownZoomMax  = 30f;
        [SerializeField] private float _topDownZoomSpeed = 2f;
        [SerializeField] private float _movingTargetZoom = 12f;
        [SerializeField] private Transform _topDownTrackingProxy;
        [SerializeField] private float _proxyMoveSmoothTime = 0.2f;

        [Header("Input")]
        [SerializeField] private Key _switchKey = Key.Tab;

        private CameraMode _currentMode = CameraMode.FPS;
        private float _currentZoom;
        private bool _isFocusingMovingPawn;
        private int _focusedPlayerId = -1;
        private Transform _focusedPawn;
        private Transform _defaultTopDownFollow;
        private Transform _defaultTopDownLookAt;
        private Vector3 _proxyVelocity;
        private Coroutine _releaseFocusCoroutine;
        private CinemachineCamera _localPlayerFpsCamera;

        // Cached map built lazily: playerId -> pawn Transform
        private readonly System.Collections.Generic.Dictionary<int, Transform> _pawnCache
            = new System.Collections.Generic.Dictionary<int, Transform>();
        private readonly System.Collections.Generic.Dictionary<int, PlayerController> _playerControllerCache
            = new System.Collections.Generic.Dictionary<int, PlayerController>();
        private readonly System.Collections.Generic.Dictionary<int, CinemachineCamera> _playerFpsCameraCache
            = new System.Collections.Generic.Dictionary<int, CinemachineCamera>();

        private UnityEngine.Camera _mainCamera;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            _mainCamera = UnityEngine.Camera.main;
            _currentZoom = (_topDownZoomMin + _topDownZoomMax) / 2f;
            _defaultTopDownFollow = _topDownCam != null ? _topDownCam.Follow : null;
            _defaultTopDownLookAt = _topDownCam != null ? _topDownCam.LookAt : null;
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.PlayerMoved, OnPlayerMoved);
            EventBus.On(GameEvent.PlayerLanded, OnPlayerLanded);
            EventBus.On(GameEvent.PlayerJailed, OnPlayerJailed);
            EventBus.On(GameEvent.TurnStarted, OnTurnStarted);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.PlayerMoved, OnPlayerMoved);
            EventBus.Off(GameEvent.PlayerLanded, OnPlayerLanded);
            EventBus.Off(GameEvent.PlayerJailed, OnPlayerJailed);
            EventBus.Off(GameEvent.TurnStarted, OnTurnStarted);

            if (_releaseFocusCoroutine != null)
            {
                StopCoroutine(_releaseFocusCoroutine);
                _releaseFocusCoroutine = null;
            }
        }

        private void Start()
        {
            SetMode(CameraMode.FPS, instant: true);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current[_switchKey].wasPressedThisFrame)
                ToggleMode();

            if (_currentMode == CameraMode.TopDown)
                UpdateTopDownTrackingProxy();
        }

        // --- Mode Toggle ---

        public void ToggleMode()
            => SetMode(_currentMode == CameraMode.FPS ? CameraMode.TopDown : CameraMode.FPS);

        public void SetMode(CameraMode mode, bool instant = false)
        {
            _currentMode = mode;

            RefreshCameraPriorities();

            if (mode != CameraMode.FPS)
            {
                ApplyTopDownFollowTarget(_isFocusingMovingPawn ? _focusedPawn : null);
            }

            if (instant)
            {
                // Force immediate transition via the CinemachineBrain
                var brain = UnityEngine.Camera.main?.GetComponent<CinemachineBrain>();
                if (brain != null) brain.ManualUpdate();
            }

            EventBus.Emit(GameEvent.CameraSwitched, mode);
        }

        private void RefreshCameraPriorities()
        {
            bool isFPS = _currentMode == CameraMode.FPS;

            if (_topDownCam != null)
                _topDownCam.Priority = isFPS ? 0 : 20;

            if (_fpsCam != null)
                _fpsCam.Priority = (isFPS && _localPlayerFpsCamera == null) ? 20 : 0;

            foreach (var entry in _playerFpsCameraCache)
            {
                if (entry.Value != null)
                    entry.Value.Priority = 0;
            }

            if (isFPS && _localPlayerFpsCamera != null)
                _localPlayerFpsCamera.Priority = 30;
        }

        private void OnPlayerMoved(object payload)
        {
            if (!(payload is PlayerMovePayload move)) return;

            if (_releaseFocusCoroutine != null)
            {
                StopCoroutine(_releaseFocusCoroutine);
                _releaseFocusCoroutine = null;
            }

            FocusOnPlayer(move.PlayerId);
        }

        private void OnPlayerLanded(object payload)
        {
            if (!(payload is PlayerLandedPayload landed)) return;
            if (!_isFocusingMovingPawn || landed.PlayerId != _focusedPlayerId) return;

            if (_releaseFocusCoroutine != null)
                StopCoroutine(_releaseFocusCoroutine);
            _releaseFocusCoroutine = StartCoroutine(ReleaseFocusWhenPawnStops(landed.PlayerId));
        }

        private void OnPlayerJailed(object payload)
        {
            if (!(payload is int playerId)) return;

            if (_releaseFocusCoroutine != null)
            {
                StopCoroutine(_releaseFocusCoroutine);
                _releaseFocusCoroutine = null;
            }

            FocusOnPlayer(playerId);
        }

        private void OnTurnStarted(object payload)
        {
            if (!(payload is PlayerState player)) return;
            if (!player.IsInJail) return;
            FocusOnPlayer(player.PlayerId);
        }

        private void FocusOnPlayer(int playerId)
        {
            Transform pawn = ResolvePawnTransform(playerId);
            if (pawn == null) return;

            _isFocusingMovingPawn = true;
            _focusedPlayerId = playerId;
            _focusedPawn = pawn;

            if (_currentMode == CameraMode.TopDown)
                ApplyTopDownFollowTarget(_focusedPawn);
        }

        private System.Collections.IEnumerator ReleaseFocusWhenPawnStops(int playerId)
        {
            PlayerController controller = ResolvePlayerController(playerId);
            int safeFrameCount = 0;

            while (controller != null && controller.IsMoving && safeFrameCount < 600)
            {
                safeFrameCount++;
                yield return null;
            }

            Transform pawn = ResolvePawnTransform(playerId);
            if (_topDownTrackingProxy != null && pawn != null)
            {
                // Ensure the proxy reaches the exact final tile before releasing focus.
                _topDownTrackingProxy.position = pawn.position;
                _proxyVelocity = Vector3.zero;
            }

            _isFocusingMovingPawn = false;
            _focusedPlayerId = -1;
            _focusedPawn = null;
            _releaseFocusCoroutine = null;

            if (_currentMode == CameraMode.TopDown)
                ApplyTopDownFollowTarget(null);
        }

        private void ApplyTopDownFollowTarget(Transform followTarget)
        {
            if (_topDownCam == null) return;

            // Preferred behavior: keep Cinemachine bound to one persistent proxy target.
            if (_topDownTrackingProxy != null)
            {
                _topDownCam.Follow = _topDownTrackingProxy;
                _topDownCam.LookAt = _topDownTrackingProxy;
                return;
            }

            if (followTarget != null)
            {
                _topDownCam.Follow = followTarget;
                _topDownCam.LookAt = followTarget;
            }
            else
            {
                _topDownCam.Follow = _defaultTopDownFollow;
                _topDownCam.LookAt = _defaultTopDownLookAt;
            }
        }

        private void UpdateTopDownTrackingProxy()
        {
            if (_topDownTrackingProxy == null) return;

            Transform target = null;
            if (_isFocusingMovingPawn && _focusedPawn != null)
                target = _focusedPawn;
            else if (_defaultTopDownFollow != null)
                target = _defaultTopDownFollow;

            if (target == null) return;

            _topDownTrackingProxy.position = Vector3.SmoothDamp(
                _topDownTrackingProxy.position,
                target.position,
                ref _proxyVelocity,
                Mathf.Max(0.01f, _proxyMoveSmoothTime));
        }

        private Transform ResolvePawnTransform(int playerId)
        {
            if (_pawnCache.TryGetValue(playerId, out Transform cached) && cached != null)
                return cached;

            // Cache miss: scan once and populate
            var players = FindObjectsOfType<PlayerController>();
            foreach (var player in players)
            {
                if (player == null) continue;
                int id = player.State != null ? player.State.PlayerId : player.PlayerId;
                _pawnCache[id] = player.PawnTransform;
            }

            return _pawnCache.TryGetValue(playerId, out Transform found) ? found : null;
        }

        private PlayerController ResolvePlayerController(int playerId)
        {
            if (_playerControllerCache.TryGetValue(playerId, out PlayerController cached) && cached != null)
                return cached;

            var players = FindObjectsOfType<PlayerController>();
            foreach (var player in players)
            {
                if (player == null) continue;
                int id = player.State != null ? player.State.PlayerId : player.PlayerId;
                _playerControllerCache[id] = player;
            }

            return _playerControllerCache.TryGetValue(playerId, out PlayerController found) ? found : null;
        }

        /// <summary>
        /// Registers a pawn transform directly into the cache.
        /// Called by PlayerController.Initialize for instant resolution.
        /// </summary>
        public void RegisterPawn(int playerId, Transform pawn)
        {
            _pawnCache[playerId] = pawn;

            PlayerController controller = pawn != null ? pawn.GetComponentInParent<PlayerController>() : null;
            if (controller != null)
                _playerControllerCache[playerId] = controller;
        }

        /// <summary>
        /// Registers a per-player FPS Cinemachine camera.
        /// Only the local player's camera is activated when entering FPS mode.
        /// </summary>
        public void RegisterPlayerFPSCamera(int playerId, CinemachineCamera fpsCamera, bool isLocalPlayer)
        {
            if (fpsCamera == null) return;

            _playerFpsCameraCache[playerId] = fpsCamera;

            if (isLocalPlayer)
                _localPlayerFpsCamera = fpsCamera;

            RefreshCameraPriorities();
        }

        // --- Pawn Follow (FPS) ---

        /// <summary>
        /// Makes the FPS camera follow the local player's pawn.
        /// Called by PlayerController during initialization.
        /// FPS auto-tracking is disabled by design.
        /// </summary>
        public void SetFPSFollowTarget(Transform target)
        {
        }

        // --- Properties ---

        public CameraMode CurrentMode => _currentMode;
        public bool IsTopDown => _currentMode == CameraMode.TopDown;
    }
}
