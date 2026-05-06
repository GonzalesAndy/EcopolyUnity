using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Unity.Netcode;
using Ecopoly.Utils;
using Ecopoly.Core;
using Ecopoly.Data;
using Ecopoly.Network;

namespace Ecopoly.UI
{
    public class HUDController : MonoBehaviour
    {
        public static HUDController Instance { get; private set; }

        [Header("Money")]
        [SerializeField] private TextMeshProUGUI _moneyText;
        [SerializeField] private TextMeshProUGUI _playerNameText;

        [Header("Dice")]
        [SerializeField] private TextMeshProUGUI _diceResultText;
        [SerializeField] private GameObject _rollDiceButton;
        [SerializeField] private GameObject _dicePanel;

        [Header("Turn")]
        [SerializeField] private TextMeshProUGUI _turnInfoText;
        [SerializeField] private GameObject _actionPanel; // Buy, renovate, sell.
        [SerializeField] private GameObject _readyButton; // "Ready" button to end a human turn.

        [Header("Card Inventory")]
        [SerializeField] private CardInventoryUI _cardInventory;

        [Header("Action Buttons")]
        [SerializeField] private Button _switchCameraButton;
        [SerializeField] private Button _jailPayButton;
        [SerializeField] private Button _propertiesButton;

        [Header("Properties Panel")]
        [SerializeField] private PropertiesPanelUI _propertiesPanel;

        [Header("Notification")]
        [SerializeField] private GameObject _notificationPanel;
        [SerializeField] private TextMeshProUGUI _notificationText;

        [Header("Online Players")]
        [SerializeField] private Transform _playerListRoot;
        [SerializeField] private GameObject _playerRowPrefab;

        private int _localPlayerId;
        private bool _isInitialized;
        private Coroutine _notificationCoroutine;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;

            if (_notificationPanel != null) _notificationPanel.SetActive(false);

            // Wire button clicks in code so no Inspector persistent calls are needed
            _switchCameraButton?.onClick.AddListener(OnSwitchCameraClicked);
            _jailPayButton?.onClick.AddListener(OnPayBailClicked);
            _propertiesButton?.onClick.AddListener(OnPropertiesClicked);

            if (_rollDiceButton != null)
                _rollDiceButton.GetComponent<Button>()?.onClick.AddListener(OnRollDiceClicked);

            if (_readyButton != null)
                _readyButton.GetComponent<Button>()?.onClick.AddListener(OnReadyClicked);
        }

        private void OnEnable()
        {
            Debug.Log("HUDController: OnEnable");
            EventBus.On(GameEvent.TurnStarted,    OnTurnStarted);
            EventBus.On(GameEvent.TurnEnded,      OnTurnEnded);
            EventBus.On(GameEvent.DiceRolled,     OnDiceRolled);
            EventBus.On(GameEvent.RentPaid,        OnRentPaid);
            EventBus.On(GameEvent.PlayerPassedGo, OnPlayerPassedGo);
            EventBus.On(GameEvent.PlayerJailed, OnPlayerJailed);
            EventBus.On(GameEvent.PlayerReleasedFromJail, OnPlayerReleasedFromJail);
            EventBus.On(GameEvent.PlayerBankrupt, OnPlayerBankrupt);
            EventBus.On(GameEvent.PlayerCEPMaxReached, OnPlayerCEPMaxReached);
            EventBus.On(GameEvent.DilemmaCardResolved, OnDilemmaCardResolved);
            EventBus.On(GameEvent.DisasterResolved, OnDisasterResolved);
            EventBus.On(GameEvent.MoneyChanged, OnMoneyChanged);
            EventBus.On(GameEvent.UINotification, OnNotification);
            EventBus.On(GameEvent.CameraSwitched, OnCameraSwitched);
            EventBus.On(GameEvent.PropertyPurchased, OnPropertyPurchased);
        }

        private void OnDisable()
        {
            Debug.Log("HUDController: OnDisable");
            EventBus.Off(GameEvent.TurnStarted,    OnTurnStarted);
            EventBus.Off(GameEvent.TurnEnded,      OnTurnEnded);
            EventBus.Off(GameEvent.DiceRolled,     OnDiceRolled);
            EventBus.Off(GameEvent.RentPaid,        OnRentPaid);
            EventBus.Off(GameEvent.PlayerPassedGo, OnPlayerPassedGo);
            EventBus.Off(GameEvent.PlayerJailed, OnPlayerJailed);
            EventBus.Off(GameEvent.PlayerReleasedFromJail, OnPlayerReleasedFromJail);
            EventBus.Off(GameEvent.PlayerBankrupt, OnPlayerBankrupt);
            EventBus.Off(GameEvent.PlayerCEPMaxReached, OnPlayerCEPMaxReached);
            EventBus.Off(GameEvent.DilemmaCardResolved, OnDilemmaCardResolved);
            EventBus.Off(GameEvent.DisasterResolved, OnDisasterResolved);
            EventBus.Off(GameEvent.MoneyChanged, OnMoneyChanged);
            EventBus.Off(GameEvent.UINotification, OnNotification);
            EventBus.Off(GameEvent.CameraSwitched, OnCameraSwitched);
            EventBus.Off(GameEvent.PropertyPurchased, OnPropertyPurchased);
        }

        public bool HasReadyButton => _readyButton != null;

        public void Initialize(int localPlayerId)
        {
            _localPlayerId = localPlayerId;
            _isInitialized = true;
            RefreshMoney();
            RefreshPlayerList();
            // Reset text-only fields so no placeholder text shows on clients
            // before the first TurnStarted event arrives from the server.
            if (_turnInfoText != null) _turnInfoText.text = "Waiting for game to start...";
            if (_diceResultText != null) _diceResultText.text = "";
            // Action panel stays visible — it always shows camera toggle + conditional jail/cards
            _jailPayButton?.gameObject.SetActive(false);
            _cardInventory?.Initialize(localPlayerId);
            _propertiesPanel?.Initialize(localPlayerId);
        }

        private void OnTurnStarted(object payload)
        {
            Debug.Log($"HUDController: OnTurnStarted payload={payload?.GetType().Name}");
            if (!_isInitialized) return;
            if (!(payload is PlayerState ps)) return;
            bool isLocal = ps.PlayerId == _localPlayerId;

            _turnInfoText.text = isLocal ? "Your turn!" : $"{ps.PlayerName}'s turn";
            _rollDiceButton.SetActive(isLocal);
            _jailPayButton?.gameObject.SetActive(isLocal && ps.IsInJail);
            if (_readyButton != null) _readyButton.SetActive(false);
            RefreshMoney();
        }

        private void OnTurnEnded(object payload)
        {
            Debug.Log("HUDController: OnTurnEnded");
            _rollDiceButton.SetActive(false);
            _jailPayButton?.gameObject.SetActive(false);
            // Action panel stays visible (camera toggle is always usable)

            // Show the "Ready" button only for the local human player
            bool isLocalHumanTurn = payload is PlayerState ps
                && ps.PlayerId == _localPlayerId
                && !ps.IsBot;
            if (_readyButton != null) _readyButton.SetActive(isLocalHumanTurn);

            RefreshMoney();
        }

        private void OnDiceRolled(object payload)
        {
            Debug.Log($"HUDController: OnDiceRolled payload={payload?.GetType().Name}");
            if (!(payload is DiceRollPayload roll)) return;
            _diceResultText.text = $"{roll.Die1}  +  {roll.Die2}  =  {roll.Die1 + roll.Die2}";
            _dicePanel.SetActive(true);
            // Hide the roll button immediately — the turn is in progress; TurnEnded will show Ready.
            if (_rollDiceButton != null) _rollDiceButton.SetActive(false);
        }

        private void OnRentPaid(object payload)
        {
            Debug.Log($"HUDController: OnRentPaid payload={payload?.GetType().Name}");
            if (!(payload is RentPayload rent)) return;
            EventBus.Emit(GameEvent.UINotification, new Ecopoly.Utils.UINotificationPayload { Message = $"Rent paid: M{rent.Amount}", Color = Color.red, Duration = 3f, Priority = 0, PlayerId = rent.PayerId });
            if (rent.PayerId == _localPlayerId)
                RefreshMoney();
        }

        private void OnNotification(object payload)
        {
            Debug.Log($"HUDController: OnNotification payload={payload?.GetType().Name}");
            if (payload is Ecopoly.Utils.UINotificationPayload p)
            {
                // Empty message => hide any active notification immediately.
                if (string.IsNullOrEmpty(p.Message))
                {
                    if (_notificationCoroutine != null) { StopCoroutine(_notificationCoroutine); _notificationCoroutine = null; }
                    _notificationPanel.SetActive(false);
                    return;
                }

                // Format the message based on player context if PlayerId is set
                string displayMessage = p.Message;
                if (p.PlayerId >= 0)
                {
                    var player = GameManager.Instance?.GetPlayer(p.PlayerId);
                    bool isLocal = p.PlayerId == _localPlayerId;
                    string playerName = player?.PlayerName ?? "Player";
                    
                    // Build verb/subject appropriate to the message type
                    if (displayMessage.StartsWith("Passed GO"))
                        displayMessage = isLocal ? $"You passed GO: +M200" : $"{playerName} passed GO: +M200";
                    else if (displayMessage == "has been jailed")
                        displayMessage = isLocal ? "You have been jailed" : $"{playerName} has been jailed";
                    else if (displayMessage == "released from jail")
                        displayMessage = isLocal ? "You were released from jail" : $"{playerName} was released from jail";
                    else if (displayMessage == "is bankrupt")
                        displayMessage = isLocal ? "You are bankrupt" : $"{playerName} is bankrupt";
                    else if (displayMessage.StartsWith("eliminated"))
                        displayMessage = isLocal ? $"You were {displayMessage}" : $"{playerName} was {displayMessage}";
                    else if (displayMessage.StartsWith("Rent paid"))
                    {
                        // Extract amount from "Rent paid: M{amount}"
                        int colonIdx = displayMessage.IndexOf(':');
                        string amount = colonIdx >= 0 ? displayMessage.Substring(colonIdx) : "";
                        displayMessage = isLocal ? $"You paid rent{amount}" : $"{playerName} paid rent{amount}";
                    }
                    else if (displayMessage.StartsWith("Tax:") || displayMessage.StartsWith("Lucky draw"))
                        displayMessage = isLocal ? $"You: {p.Message}" : $"{playerName}: {p.Message}";
                    else if (displayMessage.StartsWith("Not enough money"))
                        displayMessage = isLocal ? "You don't have enough money to pay bail." : $"{playerName} doesn't have enough money to pay bail.";
                    else
                        // Generic fallback: just prepend player name
                        displayMessage = isLocal ? $"You: {p.Message}" : $"{playerName}: {p.Message}";
                }

                if (_notificationCoroutine != null) StopCoroutine(_notificationCoroutine);
                _notificationCoroutine = StartCoroutine(DisplayNotification(displayMessage, p.Color ?? Color.white, p.Duration > 0f ? p.Duration : 3f));
            }
        }

        private void OnPlayerPassedGo(object payload)
        {
            Debug.Log($"HUDController: OnPlayerPassedGo payload={payload}");
            if (!(payload is int playerId)) return;
            EventBus.Emit(GameEvent.UINotification, new Ecopoly.Utils.UINotificationPayload { Message = "Passed GO: +M200", Color = Color.green, Duration = 3f, Priority = 0, PlayerId = playerId });
            if (playerId == _localPlayerId)
                RefreshMoney();
        }

        private void OnPlayerJailed(object payload)
        {
            Debug.Log($"HUDController: OnPlayerJailed payload={payload}");
            if (!(payload is int playerId)) return;
            EventBus.Emit(GameEvent.UINotification, new Ecopoly.Utils.UINotificationPayload { Message = "has been jailed", Color = new Color(1f, 0.65f, 0f), Duration = 3f, Priority = 0, PlayerId = playerId });
        }

        private void OnPlayerReleasedFromJail(object payload)
        {
            Debug.Log($"HUDController: OnPlayerReleasedFromJail payload={payload}");
            if (!(payload is int playerId)) return;
            EventBus.Emit(GameEvent.UINotification, new Ecopoly.Utils.UINotificationPayload { Message = "released from jail", Color = Color.cyan, Duration = 3f, Priority = 0, PlayerId = playerId });
        }

        private void OnPlayerBankrupt(object payload)
        {
            Debug.Log($"HUDController: OnPlayerBankrupt payload={payload}");
            if (!(payload is int playerId)) return;
            EventBus.Emit(GameEvent.UINotification, new Ecopoly.Utils.UINotificationPayload { Message = "is bankrupt", Color = Color.red, Duration = 3f, Priority = 0, PlayerId = playerId });
        }

        private void OnPlayerCEPMaxReached(object payload)
        {
            Debug.Log($"HUDController: OnPlayerCEPMaxReached payload={payload}");
            if (!(payload is int playerId)) return;
            EventBus.Emit(GameEvent.UINotification, new Ecopoly.Utils.UINotificationPayload { Message = "eliminated (CEP max)", Color = Color.red, Duration = 3f, Priority = 0, PlayerId = playerId });
        }

        private void OnDilemmaCardResolved(object payload)
        {
            Debug.Log($"HUDController: OnDilemmaCardResolved payload={payload}");
            if (!(payload is bool paidByAll)) return;
            EventBus.Emit(GameEvent.UINotification, new Ecopoly.Utils.UINotificationPayload {
                Message = paidByAll ? "Dilemma resolved: collective effort succeeded" : "Dilemma failed: global CEP increases",
                Color = paidByAll ? Color.green : Color.red,
                Duration = 3f,
                Priority = 0
            });
        }

        private void OnDisasterResolved(object payload)
        {
            Debug.Log($"HUDController: OnDisasterResolved payload={payload}");
            if (!(payload is string disasterId) || string.IsNullOrWhiteSpace(disasterId)) return;
            EventBus.Emit(GameEvent.UINotification, new Ecopoly.Utils.UINotificationPayload { Message = $"Disaster resolved: {disasterId}", Color = null, Duration = 3f, Priority = 0 });
        }

        private void OnMoneyChanged(object payload)
        {
            Debug.Log($"HUDController: OnMoneyChanged payload={payload?.GetType().Name}");
            if (!(payload is MoneyChangePayload change)) return;
            if (change.PlayerId == _localPlayerId)
                RefreshMoney();
            // Always refresh the player list so all rows stay current.
            RefreshPlayerList();
        }

        private void OnPropertyPurchased(object payload)
        {
            RefreshPlayerList();

            if (!(payload is PropertyEventPayload p)) return;
            var gameManager = GameManager.Instance;
            var boardController = BoardController.Instance;
            var buyer = gameManager != null ? gameManager.GetPlayer(p.PlayerId) : null;
            var prop   = boardController != null ? boardController.GetPropertyData(p.PropertyId) : null;
            if (buyer == null || prop == null) return;

            bool isLocal = p.PlayerId == _localPlayerId;
            Color col = isLocal ? Color.green : Color.yellow;
            // Send property name and buyer ID; let OnNotification handle perspective-aware formatting
            EventBus.Emit(GameEvent.UINotification, new Ecopoly.Utils.UINotificationPayload { 
                Message = $"{prop.displayName} — M{prop.purchasePrice}", 
                Color = col, 
                Duration = 3f, 
                Priority = 0,
                PlayerId = p.PlayerId  // Include the buyer's ID for context-aware display
            });
        }

        private void OnCameraSwitched(object payload)
        {
            Debug.Log($"HUDController: OnCameraSwitched payload={payload}");
            if (_switchCameraButton == null) return;
            if (payload is CameraMode mode)
            {
                var label = _switchCameraButton.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                    label.text = mode == CameraMode.FPS ? "Top-down" : "FPS View";
            }
        }

        private void OnPropertiesClicked()
        {
            EventBus.Emit(GameEvent.UIPropertiesPanelRequested);
        }

        private void RefreshMoney()
        {
            var gameManager = GameManager.Instance;
            if (gameManager == null) return;
            var state = gameManager.GetPlayer(_localPlayerId);
            if (state == null) return;
            _moneyText.text = $"M {state.Money:N0}";
            _playerNameText.text = state.PlayerName;
        }

        public void ShowActionPanel()
        {
            // Panel is always visible; this is a no-op kept for call-site compatibility.
        }

        private IEnumerator DisplayNotification(string message, Color color, float duration)
        {
            _notificationText.text = message;
            _notificationText.color = color;
            _notificationPanel.SetActive(true);
            _notificationPanel.transform.DOScale(1.05f, 0.1f).SetLoops(2, LoopType.Yoyo);
            yield return new WaitForSeconds(duration);
            _notificationPanel.SetActive(false);
        }

        private void RefreshPlayerList()
        {
            if (_playerListRoot == null || _playerRowPrefab == null) return;
            var gameManager = GameManager.Instance;
            if (gameManager == null) return;

            foreach (Transform child in _playerListRoot)
                Destroy(child.gameObject);

            foreach (var p in gameManager.Players)
            {
                var row = Instantiate(_playerRowPrefab, _playerListRoot);
                // Reset anchored position so VerticalLayoutGroup can control layout.
                var rt = row.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchoredPosition = Vector2.zero;
                    rt.localScale = Vector3.one;
                }
                var label = row.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                    label.text = $"{p.PlayerName}  M{p.Money:N0}";
            }
        }

        public void OnRollDiceClicked()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
                EcopolyNetworkManager.Instance?.RequestDiceRollServerRpc();
            else
                TurnManager.Instance.RollDice();
        }
        public void OnReadyClicked()
        {
            if (_readyButton != null) _readyButton.SetActive(false);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
                EcopolyNetworkManager.Instance?.RequestReadyForNextTurnServerRpc(_localPlayerId);
            else if (TurnManager.Instance != null)
                TurnManager.Instance.ConfirmReadyForNextTurn();
        }
        public void OnSwitchCameraClicked() => Camera.CameraController.Instance.ToggleMode();
        public void OnPayBailClicked()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
                EcopolyNetworkManager.Instance?.RequestPayBailServerRpc(_localPlayerId);
            else
                TurnManager.Instance.PayBailToLeaveJail();
        }
    }
}
