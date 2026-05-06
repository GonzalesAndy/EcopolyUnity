using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Ecopoly.Utils;
using Ecopoly.Data;
using Ecopoly.Core;
using Ecopoly.Cards;

namespace Ecopoly.AI
{
    /// <summary>
    /// Main bot brain. Subscribes to turn events
    /// and delegates decision logic to BotDecisionTree.
    /// </summary>
    [RequireComponent(typeof(Player.PlayerController))]
    public class BotBrain : MonoBehaviour 
    {
        // Static registry: playerId -> BotBrain
        private static readonly Dictionary<int, BotBrain> _registry
            = new Dictionary<int, BotBrain>();

        public static BotBrain GetForPlayer(int playerId)
            => _registry.TryGetValue(playerId, out var b) ? b : null;

        private Player.PlayerController _controller;
        private BotDecisionTree _tree;
        private PlayerState _state;
        private BotPersonalityData _personality;

        private void Awake()
        {
            _controller = GetComponent<Player.PlayerController>();
            _tree = new BotDecisionTree();
        }

        private void OnDestroy()
        {
            if (_state != null)
                _registry.Remove(_state.PlayerId);
        }

        public void Initialize(PlayerState state)
        {
            _state = state;
            _personality = state.BotPersonality;
            if (_tree != null) _tree.Initialize(state, _personality);
            _registry[state.PlayerId] = this;
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.UICardDisplayRequested, OnUICardDisplayRequested);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.UICardDisplayRequested, OnUICardDisplayRequested);
        }

        /// <summary>
        /// Returns this bot's vote for a collective dilemma.
        /// True = pay collectively, false = refuse collective payment.
        /// </summary>
        public bool DecideDilemmaVote(int costPerPlayer)
        {
            return _tree != null && _tree.ShouldPayDilemma(costPerPlayer);
        }

        public bool ShouldUseGetOutOfJailCard()
        {
            return _tree != null && _tree.ShouldUseGetOutOfJailCard();
        }

        public string DecideMoveCardToPlay()
        {
            return _tree != null ? _tree.ChooseMoveCard() : null;
        }

        public int DecideMoveCardSteps(string cardId)
        {
            return _tree != null ? _tree.ChooseMoveCardSteps(cardId) : 1;
        }

        // --- Landing ---

        /// <summary>
        /// Called explicitly by TurnManager after the bot's move and landing resolves.
        /// Not event-driven to prevent a second execution path alongside TurnManager.
        /// </summary>
        public IEnumerator HandleLanding()
        {
            yield return StartCoroutine(DecideOnLanding());
        }

        private IEnumerator DecideOnLanding()
        {
            yield return new WaitForSeconds(_personality?.decisionDelay ?? 1f);

            if (_state == null || BoardController.Instance == null) yield break;

            string purchasedPropertyId = null;

            // Property purchase
            var tileConfig = BoardController.Instance.GetTileConfig(_state.BoardPosition);
            if (tileConfig.tileType == TileType.Property
                && !string.IsNullOrEmpty(tileConfig.propertyId)
                && BoardController.Instance.GetOwner(tileConfig.propertyId) == -1)
            {
                bool shouldBuy = _tree.ShouldBuyProperty(tileConfig.propertyId);
                if (shouldBuy)
                {
                    bool bought = BoardController.Instance.BuyProperty(_state, tileConfig.propertyId);
                    if (bought)
                        purchasedPropertyId = tileConfig.propertyId;
                }
            }

            string landedPropertyId =
                tileConfig.tileType == TileType.Property ? tileConfig.propertyId : null;
            yield return StartCoroutine(DecideRenovations(landedPropertyId, purchasedPropertyId));
        }

        private IEnumerator DecideRenovations(string landedPropertyId, string skipPropertyId = null)
        {
            if (string.IsNullOrEmpty(landedPropertyId))
            {
                if (TurnManager.Instance != null) TurnManager.Instance.DismissRenovationOffer();
                yield break;
            }

            if (BoardController.Instance.GetOwner(landedPropertyId) != _state.PlayerId)
            {
                if (TurnManager.Instance != null) TurnManager.Instance.DismissRenovationOffer();
                yield break;
            }

            if (!string.IsNullOrEmpty(skipPropertyId) && landedPropertyId == skipPropertyId)
            {
                if (TurnManager.Instance != null) TurnManager.Instance.DismissRenovationOffer();
                yield break;
            }

            bool shouldRenovate = _tree.ShouldRenovate(landedPropertyId);
            if (shouldRenovate)
            {
                BoardController.Instance.RenovateProperty(_state, landedPropertyId);
                yield return new WaitForSeconds(0.3f);
            }

            // After the bot decision (renovate or skip), release the renovation wait
            if (TurnManager.Instance != null)
                TurnManager.Instance.DismissRenovationOffer();
        }

        // --- Dilemmas ---

        private void OnUICardDisplayRequested(object payload)
        {
            if (payload is Cards.BuildingDegradedPayload bdp && bdp.PlayerId == _state.PlayerId)
            {
                bool payMoney = _tree.ShouldPayInsteadOfDegrade(bdp.Card.moneyAmount);
                if (payMoney)
                    TurnManager.Instance.DeductMoney(_state, bdp.Card.moneyAmount);
                else
                {
                    string cheapestProp = _tree.GetCheapestPropertyToDegrade();
                    if (!string.IsNullOrEmpty(cheapestProp))
                        BoardController.Instance.DegradeProperty(cheapestProp, 1);
                    else
                        TurnManager.Instance.DeductMoney(_state, bdp.Card.moneyAmount);
                }
                return;
            }

        }
    }

    // ---

    /// <summary>
    /// Bot decision tree. All choice logic lives here.
    /// Plain class, instantiated directly by BotBrain, not a MonoBehaviour component.
    /// </summary>
    public class BotDecisionTree
    {
        private PlayerState _state;
        private BotPersonalityData _personality;

        public void Initialize(PlayerState state, BotPersonalityData personality)
        {
            _state = state;
            _personality = personality;
        }

        // --- Purchase ---

        public bool ShouldBuyProperty(string propertyId)
        {
            var prop = BoardController.Instance.GetPropertyData(propertyId);
            if (prop == null) return false;

            if (_personality == null)
            {
                Debug.LogWarning($"[BotDecisionTree] No BotPersonalityData assigned for player {_state?.PlayerId}. Using default buy logic.");
                // Default: buy if we can afford it with a small reserve.
                return _state != null && _state.Money - prop.purchasePrice >= 200;
            }

            // Do not buy if funds are insufficient (respect safe reserve)
            if (_state.Money - prop.purchasePrice < _personality.safeMoneyReserve) return false;

            // Do not buy if personal CEP would exceed the buy threshold
            if (_state.PersonalCEP + prop.cepOnPurchase > _personality.cepBuyThreshold) return false;

            // Purchase probability according to risk tolerance
            float buyChance = Mathf.Lerp(0.4f, 0.95f, _personality.riskTolerance);
            return Random.value < buyChance;
        }

        // --- Renovation ---

        public bool ShouldRenovate(string propertyId)
        {
            if (_personality == null) return false;
            var prop = BoardController.Instance.GetPropertyData(propertyId);
            if (prop == null) return false;

            int level = BoardController.Instance.GetRenovationLevel(propertyId);
            if (level >= Constants.MAX_RENOVATION_LEVEL) return false;

            int costIndex = level - 1;
            int cost = prop.renovationCosts[costIndex];
            int cepCost = prop.renovationCEPCosts[costIndex];

            if (_state.Money - cost < _personality.safeMoneyReserve) return false;

            // An eco-aware bot renovates more often to reduce emissions
            float ecoBonus = _personality.ecologicalAwareness * 0.4f;
            float renovateChance = Mathf.Lerp(0.2f, 0.7f, _personality.riskTolerance) + ecoBonus;
            return Random.value < renovateChance;
        }

        // --- Move Cards ---

        public string ChooseMoveCard()
        {
            if (_state == null || _state.CardHandIds == null || _state.CardHandIds.Count == 0) return null;

            var moveCards = new List<string>();
            foreach (var cardId in _state.CardHandIds)
            {
                var card = CardManager.GetChanceCard(cardId);
                if (card == null) continue;
                if (card.cardType == ChanceCardType.MoveVelo
                    || card.cardType == ChanceCardType.MoveCar
                    || card.cardType == ChanceCardType.MovePlane)
                {
                    moveCards.Add(cardId);
                }
            }

            if (moveCards.Count == 0) return null;

            // Play a card only if there is a clear advantage
            float useChance = Mathf.Lerp(0.3f, 0.7f, _personality?.riskTolerance ?? 0.5f);
            if (Random.value > useChance) return null;

            // Risky bots prefer the longest movement card in hand
            if ((_personality?.riskTolerance ?? 0.5f) > 0.6f)
            {
                string bestCard = moveCards[0];
                int bestMax = GetMaxStepsForCard(bestCard);
                for (int i = 1; i < moveCards.Count; i++)
                {
                    int candidateMax = GetMaxStepsForCard(moveCards[i]);
                    if (candidateMax > bestMax)
                    {
                        bestMax = candidateMax;
                        bestCard = moveCards[i];
                    }
                }
                return bestCard;
            }

            return moveCards[Random.Range(0, moveCards.Count)];
        }

        public int ChooseMoveCardSteps(string cardId)
        {
            // A risky bot goes to the maximum to maximize purchase opportunities;
            // a cautious bot picks randomly within range.
            int maxSteps = GetMaxStepsForCard(cardId);
            if (maxSteps <= 1) return 1;

            float risk = _personality?.riskTolerance ?? 0.5f;
            if (risk > 0.6f) return maxSteps;
            return Random.Range(1, maxSteps + 1);
        }

        public bool ShouldUseGetOutOfJailCard()
        {
            if (_state == null) return false;
            return _state.IsInJail && _state.HasGetOutOfJailCard;
        }

        // --- Dilemma ---

        public bool ShouldPayDilemma(int costPerPlayer)
        {
            if (_personality == null) return false;
            if (_state.Money - costPerPlayer < _personality.safeMoneyReserve) return false;
            float payChance = Mathf.Lerp(0.2f, 0.9f, _personality.cooperation);
            return Random.value < payChance;
        }

        // --- Degrade vs Payment ---

        public bool ShouldPayInsteadOfDegrade(int amount)
        {
            if (_state.Money < amount) return false;
            // An eco-aware bot prefers to pay rather than degrade a property
            float payChance = Mathf.Lerp(0.3f, 0.8f, _personality?.ecologicalAwareness ?? 0.5f);
            return Random.value < payChance;
        }

        public string GetCheapestPropertyToDegrade()
        {
            // Find the property with the lowest level to minimize loss
            string bestId = null;
            int bestLevel = int.MaxValue;
            foreach (string pid in _state.OwnedPropertyIds)
            {
                int level = BoardController.Instance.GetRenovationLevel(pid);
                if (level > Constants.MIN_RENOVATION_LEVEL && level < bestLevel)
                {
                    bestLevel = level;
                    bestId = pid;
                }
            }
            return bestId;
        }

        // --- Helpers ---

        private int GetMaxStepsForCard(string cardId)
        {
            var card = CardManager.GetChanceCard(cardId);
            if (card == null) return 1;
            return Mathf.Max(1, card.maxMoveDistance);
        }
    }
}
