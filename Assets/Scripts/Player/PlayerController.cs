using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using Ecopoly.Utils;
using Ecopoly.Core;

namespace Ecopoly.Player
{
    /// <summary>
    /// Main player component. Coordinates 3D pawn movement,
    /// the FPS camera and interactions with the board.
    /// </summary>
    [RequireComponent(typeof(CEPController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] public int PlayerId;

        [Header("References")]
        [SerializeField] private Transform _pawnTransform;   // mesh du pion
        [SerializeField] private Animator _pawnAnimator;

        [Header("Visual")]
        [SerializeField] private Renderer _pawnRenderer;
        [SerializeField] private Color _playerColor = Color.white;
        private Transform _modelSlot;
        private bool _hasAnimalModel;

        [Header("Floating Name (TopDown)")]
        [SerializeField] private TMPro.TextMeshPro _nameLabel;

        [Header("FPS View")]
        [SerializeField] private Transform _fpsViewAnchor;
        [SerializeField] private CinemachineCamera _fpsPlayerCamera;
        [SerializeField] private float _fpsLookSensitivity = 1.5f;
        [SerializeField] private float _fpsMinPitch = -80f;
        [SerializeField] private float _fpsMaxPitch = 80f;
        [SerializeField] private Vector3 _fpsAnchorLocalOffset = new Vector3(0f, 1.55f, 0f);

        // --- Runtime
        private PlayerState _state;
        private CEPController _cep;
        private bool _isMoving;
        private Coroutine _moveCoroutine;
        private float _fpsYaw;
        private float _fpsPitch;

        public bool IsLocalPlayer { get; private set; }

        // --- Lifecycle
        private void Awake()
        {
            _cep = GetComponent<CEPController>();
            if (_pawnTransform == null)
                _pawnTransform = transform;
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.PlayerMoved, OnPlayerMoved);
            EventBus.On(GameEvent.PlayerLanded, OnPlayerLanded);
            EventBus.On(GameEvent.CameraSwitched, OnCameraSwitched);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.PlayerMoved, OnPlayerMoved);
            EventBus.Off(GameEvent.PlayerLanded, OnPlayerLanded);
            EventBus.Off(GameEvent.CameraSwitched, OnCameraSwitched);
        }

        private void Update()
        {
            if (!IsLocalPlayer) return;

            bool isFPS = Ecopoly.Camera.CameraController.Instance == null
                || Ecopoly.Camera.CameraController.Instance.CurrentMode == CameraMode.FPS;

            UpdateFPSCursorState(isFPS);

            if (!isFPS || IsAltPressed())
                return;

            HandleLocalFPSLook();
        }

        public void Initialize(PlayerState state, bool isLocal)
        {
            _state = state;
            IsLocalPlayer = isLocal;
            _cep.Initialize(state.PlayerId, state);

            ApplyPlayerColor();
            if (_nameLabel != null)
                _nameLabel.text = state.PlayerName;

            StartCoroutine(SnapToStartPosition(state.BoardPosition));

            // Register this pawn as the FPS follow target for the local player.
            if (Ecopoly.Camera.CameraController.Instance != null)
            {
                Transform pawn = _pawnTransform != null ? _pawnTransform : transform;
                // Always register in the cache so top-down tracking works for all players.
                Ecopoly.Camera.CameraController.Instance.RegisterPawn(state.PlayerId, pawn);
            }

            EnsureFPSCameraRig();
            if (Ecopoly.Camera.CameraController.Instance != null)
            {
                Ecopoly.Camera.CameraController.Instance.RegisterPlayerFPSCamera(
                    state.PlayerId,
                    _fpsPlayerCamera,
                    isLocal);
            }

            if (isLocal)
            {
                SyncLookAnglesFromAnchor();

                bool isFPS = Ecopoly.Camera.CameraController.Instance == null
                    || Ecopoly.Camera.CameraController.Instance.CurrentMode == CameraMode.FPS;

                if (_pawnRenderer != null)
                    _pawnRenderer.enabled = !isFPS;

                UpdateFPSCursorState(isFPS);
            }
        }

        public void SetPlayerColor(Color color)
        {
            _playerColor = color;
            ApplyPlayerColor();
        }

        public void SetAnimalModel(GameObject animalPrefab)
        {
            if (animalPrefab == null) return;

            _hasAnimalModel = true;

            // Hide the capsule permanently.
            if (_pawnRenderer != null)
                _pawnRenderer.enabled = false;

            // Create the model slot if missing (child of _pawnTransform).
            if (_modelSlot == null)
            {
                var slotGO = new GameObject("ModelSlot");
                _modelSlot = slotGO.transform;
                Transform parent = _pawnTransform != null ? _pawnTransform : transform;
                _modelSlot.SetParent(parent, false);
                _modelSlot.localPosition = Vector3.zero;
                _modelSlot.localRotation = Quaternion.identity;
                _modelSlot.localScale = Vector3.one * 4f;
                _modelSlot.gameObject.layer = parent.gameObject.layer;
            }

            // Remove the previous animal model if one exists.
            for (int i = _modelSlot.childCount - 1; i >= 0; i--)
                Destroy(_modelSlot.GetChild(i).gameObject);

            // Keep _pawnTransform uniform to avoid deforming the animal model.
            if (_pawnTransform != null)
            {
                _pawnTransform.localScale = Vector3.one;
                _pawnTransform.localPosition = Vector3.zero;
            }

            // Instantiate the animal model.
            var animal = Instantiate(animalPrefab, _modelSlot);
            animal.transform.localPosition = Vector3.zero;
            animal.transform.localScale = Vector3.one * 0.3f; // Tune this based on board scale.
            // Orientation fix: most Quirky Series FBX models face +Z.
            // If the model faces the wrong direction, change 0f to 180f below.
            animal.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            animal.gameObject.layer = _modelSlot.gameObject.layer;

            // Disable LODGroup: at board scale it can cull the model too early.
            var lodGroup = animal.GetComponentInChildren<LODGroup>();
            if (lodGroup != null)
                lodGroup.enabled = false;

            // Cache the animal Animator for Walk/Idle triggers.
            _pawnAnimator = animal.GetComponentInChildren<Animator>();
        }

        private void ApplyPlayerColor()
        {
            if (_pawnRenderer != null)
                _pawnRenderer.material.color = _playerColor;
        }

        // --- 3D Movement
        /// <summary>
        /// Called by TurnManager after BoardPosition is updated.
        /// Moves the pawn to the indicated tile.
        /// </summary>
        private IEnumerator SnapToStartPosition(int boardPosition)
        {
            yield return null;
            if (_pawnTransform == null || BoardController.Instance == null) yield break;
            var tile = BoardController.Instance.GetTile(boardPosition);
            if (tile == null) yield break;
            _pawnTransform.position = tile.PawnAnchorPosition + GetPawnOffset(boardPosition);
        }

        private void OnPlayerLanded(object payload)
        {
            if (!(payload is PlayerLandedPayload landed)) return;
            if (_state == null || _pawnTransform == null) return;
            if (_state.BoardPosition != landed.Position) return;

            var tile = BoardController.Instance?.GetTile(landed.Position);
            if (tile == null) return;

            Vector3 newPos = tile.PawnAnchorPosition + GetPawnOffset(landed.Position);
            if (Vector3.Distance(_pawnTransform.position, newPos) > 0.01f)
                StartCoroutine(SmoothReposition(newPos));
        }

        private IEnumerator SmoothReposition(Vector3 target)
        {
            Vector3 start = _pawnTransform.position;
            float elapsed = 0f;
            while (elapsed < 0.25f)
            {
                elapsed += Time.deltaTime;
                _pawnTransform.position = Vector3.Lerp(start, target, elapsed / 0.25f);
                yield return null;
            }
            _pawnTransform.position = target;
        }

        private Vector3 GetPawnOffset(int tilePosition, bool passing = false)
        {
            if (GameManager.Instance == null) return Vector3.zero;

            int myId = _state != null ? _state.PlayerId : PlayerId;
            var onSameTile = GameManager.Instance.Players
                .Where(p => !p.IsEliminated && p.BoardPosition == tilePosition)
                .OrderBy(p => p.PlayerId)
                .ToList();

            int myIndex = onSameTile.FindIndex(p => p.PlayerId == myId);
            int total = onSameTile.Count;
            if (total <= 1) return Vector3.zero;

            float radius = 0.3f;
            Vector3 perp = Vector3.Cross(GetBoardDirection(tilePosition), Vector3.up).normalized;

            if (passing)
            {
                // While passing through: sidestep if one pawn is stationary, center if 2+.
                int stationary = total - 1; // Other pawns are stationary.
                if (stationary >= 2) return Vector3.zero;
                // One stationary pawn: pass on the right side.
                return perp * radius;
            }

            // Landing with 2 players: side by side, perpendicular to movement.
            if (total == 2)
                return perp * radius * (myIndex == 0 ? -1f : 1f);

            // 3+ joueurs : cercle
            float angle = 2f * Mathf.PI * myIndex / total;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        private void FaceboardDirection(int tilePosition)
        {
            if (_modelSlot == null) return;
            Vector3 dir = GetBoardDirection(tilePosition);
            if (dir.sqrMagnitude > 0.01f)
                _modelSlot.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        private Vector3 GetBoardDirection(int tilePosition)
        {
            if (BoardController.Instance == null) return Vector3.forward;
            var current = BoardController.Instance.GetTile(tilePosition);
            var next = BoardController.Instance.GetTile((tilePosition + 1) % Constants.BOARD_SIZE);
            if (current == null || next == null) return Vector3.forward;
            Vector3 dir = next.PawnAnchorPosition - current.PawnAnchorPosition;
            dir.y = 0f;
            return dir.sqrMagnitude > 0f ? dir.normalized : Vector3.forward;
        }

        public IEnumerator MoveToBoardPosition(int position, bool passing = false)
        {
            _isMoving = true;
            SetAnimationState("Walk");

            if (_pawnTransform == null)
                _pawnTransform = transform;

            BoardTile tile = BoardController.Instance.GetTile(position);
            if (tile == null)
            {
                _isMoving = false;
                yield break;
            }

            Vector3 target = tile.PawnAnchorPosition + GetPawnOffset(position, passing);
            float duration = GameManager.Instance != null && GameManager.Instance.Settings != null
                ? GameManager.Instance.Settings.pawnMoveStepDuration
                : 0.25f;
            float elapsed = 0f;
            Vector3 start = _pawnTransform.position;

            // Light jump arc
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float arc = Mathf.Sin(t * Mathf.PI) * 0.15f; // Jump arc height.
                _pawnTransform.position = Vector3.Lerp(start, target, t)
                    + Vector3.up * arc;
                yield return null;
            }

            _pawnTransform.position = target;
            FaceboardDirection(position);
            SetAnimationState("Idle");
            _isMoving = false;
        }

        private void SetAnimationState(string state)
        {
            if (_pawnAnimator != null)
                _pawnAnimator.SetTrigger(state);
        }

        private void OnPlayerMoved(object payload)
        {
            if (!(payload is PlayerMovePayload move)) return;

            int myId = _state != null ? _state.PlayerId : PlayerId;

            if (move.PlayerId == myId)
            {
                if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
                _moveCoroutine = StartCoroutine(MoveToBoardPosition(move.NewPosition, passing: !move.IsFinalStep));
                return;
            }

            // Another player moved and may have left my tile.
            if (_state == null || _pawnTransform == null) return;
            var myTile = BoardController.Instance?.GetTile(_state.BoardPosition);
            if (myTile == null) return;
            Vector3 newPos = myTile.PawnAnchorPosition + GetPawnOffset(_state.BoardPosition);
            if (Vector3.Distance(_pawnTransform.position, newPos) > 0.01f)
                StartCoroutine(SmoothReposition(newPos));
        }

        private void EnsureFPSCameraRig()
        {
            if (_fpsViewAnchor == null)
            {
                var anchorGO = new GameObject("FPS_ViewAnchor");
                _fpsViewAnchor = anchorGO.transform;

                Transform parent = _pawnTransform != null ? _pawnTransform : transform;
                _fpsViewAnchor.SetParent(parent, false);
                _fpsViewAnchor.localPosition = _fpsAnchorLocalOffset;
                _fpsViewAnchor.localRotation = Quaternion.identity;
            }

            if (_fpsPlayerCamera == null)
            {
                var camGO = new GameObject("FPS_PlayerCamera");
                camGO.transform.SetParent(_fpsViewAnchor, false);
                camGO.transform.localPosition = Vector3.zero;
                camGO.transform.localRotation = Quaternion.identity;
                _fpsPlayerCamera = camGO.AddComponent<CinemachineCamera>();
            }
        }

        private void HandleLocalFPSLook()
        {
            if (_fpsViewAnchor == null || Mouse.current == null)
                return;

            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            float yawDelta = mouseDelta.x * _fpsLookSensitivity * 0.05f;
            float pitchDelta = mouseDelta.y * _fpsLookSensitivity * 0.05f;

            _fpsYaw += yawDelta;
            _fpsPitch -= pitchDelta;
            _fpsPitch = Mathf.Clamp(_fpsPitch, _fpsMinPitch, _fpsMaxPitch);

            _fpsViewAnchor.localRotation = Quaternion.Euler(_fpsPitch, _fpsYaw, 0f);
        }

        private void SyncLookAnglesFromAnchor()
        {
            if (_fpsViewAnchor == null) return;

            Vector3 euler = _fpsViewAnchor.localEulerAngles;
            _fpsPitch = NormalizeAngle(euler.x);
            _fpsYaw = NormalizeAngle(euler.y);
        }

        private void OnCameraSwitched(object payload)
        {
            if (!IsLocalPlayer || !(payload is CameraMode mode))
                return;

            bool isFPS = mode == CameraMode.FPS;
            UpdateFPSCursorState(isFPS);

            // Re-enable the capsule only when no animal model is replacing it.
            if (_pawnRenderer != null && !_hasAnimalModel)
                _pawnRenderer.enabled = !isFPS;
        }

        private void UpdateFPSCursorState(bool isFPS)
        {
            if (!isFPS)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            bool unlockForUI = IsAltPressed();
            Cursor.lockState = unlockForUI ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = unlockForUI;
        }

        private static bool IsAltPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            return keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
        }

        private static float NormalizeAngle(float angle)
        {
            while (angle > 180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            return angle;
        }

        // --- Accessor properties
        public PlayerState State => _state;
        public bool IsMoving => _isMoving;
        public Transform PawnTransform => _pawnTransform != null ? _pawnTransform : transform;
    }

    /// <summary>
    /// Gère le déplacement physique FPS du joueur humain local (WASD/souris).
    /// Attaché uniquement au joueur local. N'affecte que la caméra, pas le pion.
    /// En mode "FPS libre sur le plateau", le joueur peut se promener librement
    /// en dehors de son tour. Le pion reste sur sa case.
    /// </summary>
    public class FPSPlayerMovement : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _walkSpeed    = 5f;
        [SerializeField] private float _lookSensitivity = 1.5f;
        [SerializeField] private float _cameraHeight = 1.6f; // eye height in Unity units

        [Header("References")]
        [SerializeField] private Transform _cameraRig;  // Parent transform of the FPS camera.
        [SerializeField] private CharacterController _characterController;

        private float _xRotation;
        private bool _isFreelookEnabled = true; // disabled during turn animations

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            EventBus.On(GameEvent.TurnStarted, OnTurnStarted);
            EventBus.On(GameEvent.TurnEnded, OnTurnEnded);
            EventBus.On(GameEvent.CameraSwitched, OnCameraSwitched);
        }

        private void OnDestroy()
        {
            EventBus.Off(GameEvent.TurnStarted, OnTurnStarted);
            EventBus.Off(GameEvent.TurnEnded, OnTurnEnded);
            EventBus.Off(GameEvent.CameraSwitched, OnCameraSwitched);
        }

        private void Update()
        {
            if (!_isFreelookEnabled) return;
            HandleLook();
            HandleMovement();
        }

        private void HandleLook()
        {
            Vector2 mouseDelta = Mouse.current != null
                ? Mouse.current.delta.ReadValue()
                : Vector2.zero;
            float mouseX = mouseDelta.x * _lookSensitivity * 0.05f;
            float mouseY = mouseDelta.y * _lookSensitivity * 0.05f;

            _xRotation -= mouseY;
            _xRotation = Mathf.Clamp(_xRotation, -80f, 80f);
            _cameraRig.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
            transform.Rotate(Vector3.up * mouseX);
        }

        private void HandleMovement()
        {
            float h = 0f;
            float v = 0f;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) h -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) v -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) v += 1f;
            }

            Vector3 direction = transform.right * h + transform.forward * v;
            direction.y = -1f; // Basic gravity.

            _characterController.Move(direction * _walkSpeed * Time.deltaTime);
        }

        private void OnTurnStarted(object payload)
        {
            // Restrict free movement while the player must act
            // (optional: can remain free at all times)
        }

        private void OnTurnEnded(object payload)
        {
            _isFreelookEnabled = true;
        }

        private void OnCameraSwitched(object payload)
        {
            if (payload is CameraMode mode)
            {
                bool isFPS = mode == CameraMode.FPS;
                _isFreelookEnabled = isFPS;
                _characterController.enabled = isFPS;
                Cursor.lockState = isFPS ? CursorLockMode.Locked : CursorLockMode.None;
            }
        }
    }
}

