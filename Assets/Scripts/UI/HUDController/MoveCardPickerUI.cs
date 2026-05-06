using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Ecopoly.Utils;
using Ecopoly.Core;
using Ecopoly.Network;

namespace Ecopoly.UI
{
    /// <summary>
    /// Modal panel that lets the local player choose how many spaces to move
    /// when playing a movement card (Bicycle / Car / Plane).
    /// Opened via UIPlayMoveCardRequested; confirmed via TurnManager.PlayMoveCard.
    /// </summary>
    public class MoveCardPickerUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject _panel;

        [Header("Labels")]
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _cepCostText;

        [Header("Step Buttons Container")]
        [SerializeField] private Transform _stepButtonContainer;
        [SerializeField] private GameObject _stepButtonPrefab;

        [Header("Cancel")]
        [SerializeField] private Button _cancelButton;

        private string _pendingCardId;
        private int _localPlayerId = -1;

        // --- Lifecycle
        private void Awake()
        {
            if (_panel != null) _panel.SetActive(false);
            if (_cancelButton != null) _cancelButton.onClick.AddListener(Close);
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.UIPlayMoveCardRequested, OnPlayMoveCardRequested);
            EventBus.On(GameEvent.TurnEnded, OnTurnEnded);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.UIPlayMoveCardRequested, OnPlayMoveCardRequested);
            EventBus.Off(GameEvent.TurnEnded, OnTurnEnded);
        }

        // --- Event Handlers
        private void OnPlayMoveCardRequested(object payload)
        {
            if (!(payload is MoveCardPickerPayload p)) return;
            Open(p);
        }

        private void OnTurnEnded(object _) => Close();

        // --- Open / Close
        private void Open(MoveCardPickerPayload payload)
        {
            _pendingCardId = payload.CardId;
            _localPlayerId = payload.LocalPlayerId;

            if (_titleText != null)
                _titleText.text = string.IsNullOrEmpty(payload.CardDisplayName)
                    ? "Choose distance"
                    : payload.CardDisplayName;

            if (_cepCostText != null)
                _cepCostText.text = payload.CEPCost > 0
                    ? $"+{payload.CEPCost} CEP"
                    : "Free (0 CEP)";

            BuildStepButtons(payload.CardId, payload.MaxSteps);

            if (_panel != null) _panel.SetActive(true);
        }

        private void Close()
        {
            if (_panel != null) _panel.SetActive(false);
            ClearStepButtons();
            _pendingCardId = null;
        }

        // --- Step Buttons
        private void BuildStepButtons(string cardId, int maxSteps)
        {
            ClearStepButtons();
            for (int steps = 1; steps <= maxSteps; steps++)
            {
                var btnObj = Instantiate(_stepButtonPrefab, _stepButtonContainer);
                var label  = btnObj.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = steps.ToString();

                var btn        = btnObj.GetComponent<Button>();
                if (btn == null) btn = btnObj.GetComponentInChildren<Button>();
                int capturedSteps = steps;
                if (btn != null)
                    btn.onClick.AddListener(() => OnStepConfirmed(cardId, capturedSteps));
            }
        }

        private void OnStepConfirmed(string cardId, int steps)
        {
            Close();
            if (EcopolyNetworkManager.Instance != null && !EcopolyNetworkManager.Instance.IsServer)
                EcopolyNetworkManager.Instance.RequestPlayMoveCardServerRpc(cardId, steps, _localPlayerId);
            else
                TurnManager.Instance?.PlayMoveCard(cardId, steps);
        }

        private void ClearStepButtons()
        {
            if (_stepButtonContainer == null) return;
            foreach (Transform child in _stepButtonContainer)
                Destroy(child.gameObject);
        }
    }
}

