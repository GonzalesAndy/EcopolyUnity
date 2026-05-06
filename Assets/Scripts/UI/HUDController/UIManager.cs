using UnityEngine;
using TMPro;
using Ecopoly.Utils;
using Ecopoly.Core;
using Ecopoly.Data;

namespace Ecopoly.UI
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Panels")]
        [SerializeField] private CardDisplayUI _cardDisplay;
        [SerializeField] private PropertyOfferUI _propertyOffer;
        [SerializeField] private RenovationOfferUI _renovationOffer;
        [SerializeField] private DilemmaVoteUI _dilemmaVote;
        [SerializeField] private GameObject _gameOverPanel;
        [SerializeField] private TextMeshProUGUI _gameOverText;
        [SerializeField] private GameObject _victoryPanel;
        [SerializeField] private TextMeshProUGUI _victoryText;

        private int _localPlayerId = -1;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>
        /// Called by GameManager after player initialization so UIManager knows
        /// which player is local and only shows interactive popups to them.
        /// </summary>
        public void Initialize(int localPlayerId)
        {
            _localPlayerId = localPlayerId;
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.ChanceCardDrawn,        OnChanceCardDrawn);
            EventBus.On(GameEvent.EventCardDrawn,         OnEventCardDrawn);
            EventBus.On(GameEvent.UICardDisplayRequested, OnCardDisplayRequested);
            EventBus.On(GameEvent.UIRenovationRequested,  OnRenovationRequested);
            EventBus.On(GameEvent.GlobalGameOver,         OnGlobalGameOver);
            EventBus.On(GameEvent.GameEnded,              OnGameEnded);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.ChanceCardDrawn,        OnChanceCardDrawn);
            EventBus.Off(GameEvent.EventCardDrawn,         OnEventCardDrawn);
            EventBus.Off(GameEvent.UICardDisplayRequested, OnCardDisplayRequested);
            EventBus.Off(GameEvent.UIRenovationRequested,  OnRenovationRequested);
            EventBus.Off(GameEvent.GlobalGameOver,         OnGlobalGameOver);
            EventBus.Off(GameEvent.GameEnded,              OnGameEnded);
        }

        private void OnChanceCardDrawn(object payload)
        {
            Debug.Log($"UIManager: OnChanceCardDrawn payload={payload?.GetType().Name}");
            if (payload is ChanceCardData card)
            {
                // Dilemma cards open their own dedicated voting modal (DilemmaVoteUI).
                // Skip the generic card display so both UIs don't appear simultaneously.
                if (card.cardType == ChanceCardType.Dilemma) return;

                if (_cardDisplay == null)
                {
                    Debug.LogWarning("UIManager: _cardDisplay is null — cannot show chance card.");
                    return;
                }
                _cardDisplay.ShowChanceCard(card);
            }
        }

        private void OnEventCardDrawn(object payload)
        {
            Debug.Log($"UIManager: OnEventCardDrawn payload={payload?.GetType().Name}");
            if (payload is EventCardData card)
            {
                if (_cardDisplay == null)
                {
                    Debug.LogError("UIManager: _cardDisplay is null — cannot show event card. Assign CardDisplayUI in the Inspector.");
                    return;
                }
                int intensity = GameManager.Instance != null ? GameManager.Instance.CurrentIntensityLevel : 1;
                Debug.Log($"UIManager: Showing event card '{card.cardId}' at intensity {intensity}");
                _cardDisplay.ShowEventCard(card, intensity);
            }
        }

        private void OnCardDisplayRequested(object payload)
        {
            Debug.Log($"UIManager: OnCardDisplayRequested payload={payload?.GetType().Name}");
            if (payload is PropertyOfferPayload offer)
            {
                if (_propertyOffer == null)
                {
                    Debug.LogWarning("UIManager: _propertyOffer is null — cannot show property offer UI.");
                    return;
                }

                bool offerIsForBot = false;
                if (GameManager.Instance != null)
                {
                    var target = GameManager.Instance.GetPlayer(offer.PlayerId);
                    offerIsForBot = target != null && target.IsBot;
                }

                // Only show the interactive buy/decline popup to the local human player.
                // Bots handle their own purchase decisions in BotBrain.
                if (_localPlayerId >= 0 && offer.PlayerId != _localPlayerId) return;
                if (_localPlayerId < 0 && offerIsForBot) return;

                _propertyOffer.Show(offer.PlayerId, offer.PropertyId);
            }
        }

        private void OnRenovationRequested(object payload)
        {
            Debug.Log($"UIManager: OnRenovationRequested payload={payload?.GetType().Name}");
            if (!(payload is RenovationOfferPayload offer)) return;

            // Only show to the local human player who owns the property
            if (_localPlayerId >= 0 && offer.PlayerId != _localPlayerId) return;

            var target = GameManager.Instance?.GetPlayer(offer.PlayerId);
            if (target != null && target.IsBot) return; // bots decide in BotBrain

            if (_renovationOffer == null)
            {
                Debug.LogWarning("UIManager: _renovationOffer is null — cannot show renovation offer UI.");
                return;
            }

            _renovationOffer.Show(offer.PlayerId, offer.PropertyId, offer.CurrentLevel);
        }

        private void OnGlobalGameOver(object _)
        {
            Debug.Log("UIManager: OnGlobalGameOver");
            _gameOverPanel.SetActive(true);
            _gameOverText.text = "GLOBAL CATASTROPHE\nThe emissions threshold has been exceeded.\nEveryone loses.";
        }

        private void OnGameEnded(object payload)
        {
            Debug.Log($"UIManager: OnGameEnded payload={payload?.GetType().Name}");
            if (payload is PlayerState winner)
            {
                _victoryPanel.SetActive(true);
                _victoryText.text = $"{winner.PlayerName} has won!";
            }
        }
    }
}
