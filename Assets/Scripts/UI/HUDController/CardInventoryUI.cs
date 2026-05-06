using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Ecopoly.Utils;
using Ecopoly.Core;
using Ecopoly.Data;
using Ecopoly.Cards;
using Ecopoly.Network;

namespace Ecopoly.UI
{
    /// <summary>
    /// Renders the local player's card hand inside the Action_Panel.
    /// Shows movement cards and the Get-Out-Of-Jail card.
    /// Only visible when it is the local human player's turn and they have cards.
    /// </summary>
    public class CardInventoryUI : MonoBehaviour
    {
        [Header("Hand Container")]
        [SerializeField] private Transform _cardSlotContainer;
        [SerializeField] private GameObject _cardSlotPrefab;

        [Header("Section Root")]
        [Tooltip("The root GameObject that wraps the entire inventory section (title + slots). Shown/hidden as a unit.")]
        [SerializeField] private GameObject _inventorySection;

        private int _localPlayerId = -1;
        private bool _isLocalTurn;

        // --- Lifecycle
        private void OnEnable()
        {
            EventBus.On(GameEvent.TurnStarted,      OnTurnStarted);
            EventBus.On(GameEvent.TurnEnded,        OnTurnEnded);
            EventBus.On(GameEvent.PlayerHandChanged, OnHandChanged);
            EventBus.On(GameEvent.PlayerReleasedFromJail, OnHandChanged);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.TurnStarted,      OnTurnStarted);
            EventBus.Off(GameEvent.TurnEnded,        OnTurnEnded);
            EventBus.Off(GameEvent.PlayerHandChanged, OnHandChanged);
            EventBus.Off(GameEvent.PlayerReleasedFromJail, OnHandChanged);
        }

        // --- Public API
        public void Initialize(int localPlayerId)
        {
            _localPlayerId = localPlayerId;
            SetInventoryVisible(false);
        }

        /// <summary>Force-refreshes the card slots from the current player state. Called when the popup is opened manually.</summary>
        public void ForceRefresh()
        {
            var player = GameManager.Instance?.GetPlayer(_localPlayerId);
            if (player != null)
                RefreshHand(player);
        }

        // --- Event Handlers
        private void OnTurnStarted(object payload)
        {
            if (!(payload is PlayerState ps)) return;
            _isLocalTurn = ps.PlayerId == _localPlayerId && !ps.IsBot;
            if (_isLocalTurn)
                RefreshHand(ps);
            else
                SetInventoryVisible(false);
        }

        private void OnTurnEnded(object payload)
        {
            _isLocalTurn = false;
            SetInventoryVisible(false);
        }

        private void OnHandChanged(object payload)
        {
            if (!_isLocalTurn) return;
            if (!(payload is int playerId) || playerId != _localPlayerId) return;
            var player = GameManager.Instance?.GetPlayer(_localPlayerId);
            if (player != null) RefreshHand(player);
        }

        // --- Rendering
        private void RefreshHand(PlayerState player)
        {
            ClearSlots();

            var handIds = player.CardHandIds;
            bool hasCards = handIds != null && handIds.Count > 0;
            SetInventoryVisible(hasCards);
            if (!hasCards) return;

            foreach (var cardId in handIds)
            {
                var cardData = CardManager.GetChanceCard(cardId);
                if (cardData == null) continue;

                bool isMovementCard = cardData.cardType == ChanceCardType.MoveVelo
                    || cardData.cardType == ChanceCardType.MoveCar
                    || cardData.cardType == ChanceCardType.MovePlane;

                bool isJailCard = cardData.cardType == ChanceCardType.GetOutOfJail;

                if (!isMovementCard && !isJailCard) continue;

                var slot = Instantiate(_cardSlotPrefab, _cardSlotContainer);
                ConfigureSlot(slot, cardData, isMovementCard, isJailCard);
            }
        }

        private void ConfigureSlot(GameObject slot, ChanceCardData card, bool isMovement, bool isJail)
        {
            // Label
            var label = slot.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                if (isMovement)
                {
                    int cep = CardManager.GetMoveCardCEP(card);
                    string icon = card.cardType switch
                    {
                        ChanceCardType.MoveVelo  => "Bicycle",
                        ChanceCardType.MoveCar   => "Car",
                        ChanceCardType.MovePlane => "Plane",
                        _                        => "Move"
                    };
                    string cepStr = cep > 0 ? $" (+{cep} CEP)" : " (Free)";
                    label.text = $"{icon}\nUp to {card.maxMoveDistance}{cepStr}";
                }
                else
                {
                    label.text = "Get Out\nof Jail";
                }
            }

            // Button interaction
            var btn = slot.GetComponent<Button>();
            if (btn == null) btn = slot.GetComponentInChildren<Button>();
            if (btn != null)
            {
                string capturedId = card.cardId;
                if (isMovement)
                {
                    int maxSteps = card.maxMoveDistance;
                    int cepCost  = CardManager.GetMoveCardCEP(card);
                    string name  = card.displayText;
                    btn.onClick.AddListener(() => OnMovementCardClicked(capturedId, maxSteps, cepCost, name));
                }
                else
                {
                    btn.onClick.AddListener(OnJailCardClicked);
                }
            }
        }

        // --- Button Callbacks
        private void OnMovementCardClicked(string cardId, int maxSteps, int cepCost, string displayName)
        {
            // Emit event; MoveCardPickerUI modal listens and opens the step selector
            EventBus.Emit(GameEvent.UIPlayMoveCardRequested, new MoveCardPickerPayload
            {
                CardId          = cardId,
                MaxSteps        = maxSteps,
                CEPCost         = cepCost,
                CardDisplayName = displayName,
                LocalPlayerId   = _localPlayerId
            });
        }

        private void OnJailCardClicked()
        {
            if (EcopolyNetworkManager.Instance != null && !EcopolyNetworkManager.Instance.IsServer)
                EcopolyNetworkManager.Instance.RequestUseGetOutOfJailCardServerRpc(_localPlayerId);
            else
                TurnManager.Instance?.UseGetOutOfJailCard();
            // Hand will be refreshed via PlayerHandChanged + PlayerReleasedFromJail events
        }

        // --- Helpers
        private void ClearSlots()
        {
            if (_cardSlotContainer == null) return;
            foreach (Transform child in _cardSlotContainer)
                Destroy(child.gameObject);
        }

        private void SetInventoryVisible(bool visible)
        {
            if (_inventorySection != null)
                _inventorySection.SetActive(visible);
        }
    }
}

