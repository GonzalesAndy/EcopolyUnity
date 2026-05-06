using System.Collections.Generic;
using UnityEngine;
using Ecopoly.Utils;
using Ecopoly.Data;
using Ecopoly.Core;

namespace Ecopoly.Cards
{
    /// <summary>
    /// Manages both card decks (Chance and Event).
    /// Shuffles, draws, and dispatches to the appropriate handlers.
    /// </summary>
    public class CardManager : MonoBehaviour
    {
        public static CardManager Instance { get; private set; }

        [Header("Decks")]
        [SerializeField] private List<ChanceCardData> _chanceCards;
        [SerializeField] private List<EventCardData> _eventCards;

        private Queue<ChanceCardData> _chanceDeck  = new Queue<ChanceCardData>();
        private Queue<EventCardData>  _eventDeck   = new Queue<EventCardData>();

        // Get out of jail cards currently held by players (removed from the deck)
        private readonly HashSet<string> _jailFreeCardsInPlay = new HashSet<string>();

        // Debug: forced next card draws
        private ChanceCardData _forcedNextChanceCard = null;
        private EventCardData _forcedNextEventCard = null;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            EnsureCardsLoaded();

            RebuildDecks();
        }

        public static ChanceCardData GetChanceCard(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId)) return null;

            if (Instance != null)
            {
                Instance.EnsureCardsLoaded();
                var card = Instance.FindChanceCard(cardId);
                if (card != null) return card;
            }

            foreach (var card in Resources.LoadAll<ChanceCardData>(Constants.SO_CHANCE_CARDS_FOLDER))
            {
                if (card != null && card.cardId == cardId)
                    return card;
            }

            return null;
        }

        public static EventCardData GetEventCard(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId)) return null;

            if (Instance != null)
            {
                Instance.EnsureCardsLoaded();
                var card = Instance.FindEventCard(cardId);
                if (card != null) return card;
            }

            foreach (var card in Resources.LoadAll<EventCardData>(Constants.SO_EVENT_CARDS_FOLDER))
            {
                if (card != null && card.cardId == cardId)
                    return card;
            }

            return null;
        }

        private void EnsureCardsLoaded()
        {
            if (_chanceCards == null || _chanceCards.Count == 0)
                _chanceCards = new List<ChanceCardData>(Resources.LoadAll<ChanceCardData>(Constants.SO_CHANCE_CARDS_FOLDER));

            if (_eventCards == null || _eventCards.Count == 0)
                _eventCards = new List<EventCardData>(Resources.LoadAll<EventCardData>(Constants.SO_EVENT_CARDS_FOLDER));
        }

        private ChanceCardData FindChanceCard(string cardId)
        {
            if (_chanceCards == null) return null;
            foreach (var card in _chanceCards)
            {
                if (card != null && card.cardId == cardId)
                    return card;
            }

            return null;
        }

        private EventCardData FindEventCard(string cardId)
        {
            if (_eventCards == null) return null;
            foreach (var card in _eventCards)
            {
                if (card != null && card.cardId == cardId)
                    return card;
            }

            return null;
        }

        // --- Deck Building ---

        private void RebuildDecks()
        {
            _chanceDeck  = BuildShuffledQueue(_chanceCards);
            _eventDeck   = BuildShuffledQueue(_eventCards);
        }

        private Queue<T> BuildShuffledQueue<T>(List<T> source) where T : ScriptableObject
        {
            var list = new List<T>(source.Count);
            foreach (var item in source)
            {
                if (item != null) list.Add(item);
            }
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return new Queue<T>(list);
        }

        // --- Chance Draw ---

        public void DrawChanceCard(PlayerState player)
        {
            ChanceCardData card;
            
            // If a forced card is set, use it and clear it
            if (_forcedNextChanceCard != null)
            {
                card = _forcedNextChanceCard;
                _forcedNextChanceCard = null;
            }
            else
            {
                if (_chanceDeck.Count == 0) RebuildDecks();
                card = _chanceDeck.Dequeue();

                // Return to the bottom of the deck (except get out of jail cards)
                if (card != null && card.cardType != ChanceCardType.GetOutOfJail)
                    _chanceDeck.Enqueue(card);
            }

            if (card == null)
            {
                Debug.LogError("[CardManager] DrawChanceCard: dequeued a null ChanceCardData. Check for missing SO assets or null slots in the Inspector list.");
                return;
            }

            EventBus.Emit(GameEvent.ChanceCardDrawn, card);
            ResolveChanceCard(player, card);
        }

        /// <summary>Debug: force a specific chance card to be drawn for a player.</summary>
        public void ForceDrawChanceCard(PlayerState player, ChanceCardData card)
        {
            if (card == null || player == null) return;
            EventBus.Emit(GameEvent.ChanceCardDrawn, card);
            ResolveChanceCard(player, card);
        }

        // --- Event Draw ---

        public void DrawEventCard(PlayerState player)
        {
            EventCardData card;
            
            // If a forced card is set, use it and clear it
            if (_forcedNextEventCard != null)
            {
                card = _forcedNextEventCard;
                _forcedNextEventCard = null;
            }
            else
            {
                if (_eventDeck.Count == 0) RebuildDecks();
                card = _eventDeck.Dequeue();
                _eventDeck.Enqueue(card);
            }

            if (card == null)
            {
                Debug.LogError("[CardManager] DrawEventCard: dequeued a null EventCardData. Check for missing SO assets or null slots in the Inspector list.");
                return;
            }

            EventBus.Emit(GameEvent.EventCardDrawn, card);
            DisasterResolver.Instance.Resolve(player, card);
        }

        /// <summary>Debug: force a specific event card to be resolved for a player.</summary>
        public void ForceDrawEventCard(PlayerState player, EventCardData card)
        {
            if (card == null || player == null) return;
            EventBus.Emit(GameEvent.EventCardDrawn, card);
            DisasterResolver.Instance?.Resolve(player, card);
        }

        // --- Chance Card Resolution ---

        private void ResolveChanceCard(PlayerState player, ChanceCardData card)
        {
            var tm = TurnManager.Instance;
            switch (card.cardType)
            {
                case ChanceCardType.MoveVelo:
                case ChanceCardType.MoveCar:
                case ChanceCardType.MovePlane:
                    // Add to player hand: CEP cost is applied when the card is played
                    player.CardHandIds.Add(card.cardId);
                    EventBus.Emit(GameEvent.PlayerHandChanged, player.PlayerId);
                    break;

                case ChanceCardType.Tax:
                    if (!tm.DeductMoney(player, card.moneyAmount))
                        EventBus.Emit(GameEvent.UINotification,
                            new Ecopoly.Utils.UINotificationPayload { Message = $"Tax: M{card.moneyAmount} - insufficient funds, bankrupt!", Color = Color.red, Duration = 4f, Priority = 0, PlayerId = player.PlayerId });
                    break;

                case ChanceCardType.ReceiveMoney:
                    tm.AddMoney(player, card.moneyAmount);
                    EventBus.Emit(GameEvent.UINotification,
                        new Ecopoly.Utils.UINotificationPayload { Message = $"Lucky draw: +M{card.moneyAmount}", Color = Color.green, Duration = 3f, Priority = 0, PlayerId = player.PlayerId });
                    break;

                case ChanceCardType.GoToJail:
                    tm.SendCurrentPlayerToJail();
                    break;

                case ChanceCardType.GetOutOfJail:
                    player.HasGetOutOfJailCard = true;
                    player.CardHandIds.Add(card.cardId);
                    EventBus.Emit(GameEvent.PlayerHandChanged, player.PlayerId);
                    break;

                case ChanceCardType.MoveToTile:
                    // Route through TurnManager's full movement pipeline so that
                    // passing GO, stable emissions, and landing effects all fire
                    // exactly as they do for a normal dice move.
                    StartCoroutine(
                        TurnManager.Instance.ForceMovePlayerToTile(
                            player, card.targetTilePosition));
                    break;

                case ChanceCardType.Dilemma:
                    ResolveDilemma(card);
                    break;

                case ChanceCardType.PersonalCEPUp:
                    Player.CEPController.GetForPlayer(player.PlayerId)
                        ?.AddCEP(Mathf.Abs(card.cepAmount), CEPSource.ChanceCard);
                    break;

                case ChanceCardType.PersonalCEPDown:
                    Player.CEPController.GetForPlayer(player.PlayerId)
                        ?.ReduceCEP(Mathf.Abs(card.cepAmount), CEPSource.Bonus);
                    break;

                case ChanceCardType.GlobalCEPUp:
                    // Distribute evenly across all active players, at least 1 CEP each
                    int activePlayers = Mathf.Max(1, GameManager.Instance.ActivePlayerCount);
                    int cepPerPlayer = Mathf.Max(1,
                        Mathf.RoundToInt((float)card.cepAmount / activePlayers));
                    foreach (var p in GameManager.Instance.Players)
                        if (!p.IsEliminated)
                            Player.CEPController.GetForPlayer(p.PlayerId)
                                ?.AddCEP(cepPerPlayer, CEPSource.ChanceCard);
                    break;

                case ChanceCardType.BuildingDegraded:
                    ResolveBuildingDegraded(player, card);
                    break;

                case ChanceCardType.ConditionalMoney:
                    ResolveConditionalMoney(player, card);
                    break;

                case ChanceCardType.Reparations:
                    ResolveReparations(player, card);
                    break;

                case ChanceCardType.DistrictBuildingDestroyed:
                    ResolveDistrictBuildingDestroyed(player);
                    break;
            }
        }

        // --- Specific Resolutions ---

        private void ResolveDilemma(ChanceCardData card)
        {
            var tm = TurnManager.Instance;
            tm?.BeginDilemmaWait();

            EventBus.Emit(GameEvent.UIDilemmaVoteRequested, new Ecopoly.Utils.DilemmaVotePayload
            {
                CardId        = card.cardId,
                DisplayText   = card.displayText,
                CostPerPlayer = card.dilemmaCostPerPlayer,
                CEPEffect     = card.dilemmaCEPEffect,
            });

            // Wait for the vote result then apply effects
            if (tm != null)
                StartCoroutine(WaitAndApplyDilemma(card, tm));
        }

        private System.Collections.IEnumerator WaitAndApplyDilemma(ChanceCardData card, TurnManager tm)
        {
            yield return tm.WaitForDilemmaVote();
            ResolveDilemmaVote(card, tm.DilemmaVoteResult);
        }

        public void ResolveDilemmaVote(ChanceCardData card, bool paidByAll)
        {
            int activePlayers = Mathf.Max(1, GameManager.Instance.ActivePlayerCount);
            int cepShare      = Mathf.Max(1, Mathf.RoundToInt((float)card.dilemmaCEPEffect / activePlayers));

            if (paidByAll)
            {
                // Each player pays money and global CEP is reduced proportionally
                foreach (var p in GameManager.Instance.Players)
                {
                    if (p.IsEliminated) continue;
                    TurnManager.Instance.DeductMoney(p, card.dilemmaCostPerPlayer);
                    Player.CEPController.GetForPlayer(p.PlayerId)?.ReduceCEP(cepShare, CEPSource.Bonus);
                }
            }
            else
            {
                // Vote failed: global CEP rises proportionally
                foreach (var p in GameManager.Instance.Players)
                {
                    if (p.IsEliminated) continue;
                    Player.CEPController.GetForPlayer(p.PlayerId)?.AddCEP(cepShare, CEPSource.Penalty);
                }
            }

            EventBus.Emit(GameEvent.DilemmaCardResolved, paidByAll);
        }

        private void ResolveBuildingDegraded(PlayerState player, ChanceCardData card)
        {
            // Pay money or downgrade one property of your choice
            EventBus.Emit(GameEvent.UICardDisplayRequested,
                new BuildingDegradedPayload { PlayerId = player.PlayerId, Card = card });
        }

        private void ResolveConditionalMoney(PlayerState player, ChanceCardData card)
        {
            if (player.PersonalCEP < card.conditionalCEPThreshold)
                TurnManager.Instance.AddMoney(player, card.conditionalMoneyBelow);
            else
                TurnManager.Instance.DeductMoney(player, card.conditionalMoneyAbove);
        }

        private void ResolveReparations(PlayerState player, ChanceCardData card)
        {
            // Pay other players proportionally to personal CEP
            int repAmount = Mathf.FloorToInt(player.PersonalCEP / 100f) * 10;
            foreach (var p in GameManager.Instance.Players)
            {
                if (p.PlayerId == player.PlayerId || p.IsEliminated) continue;
                TurnManager.Instance.DeductMoney(player, repAmount);
                TurnManager.Instance.AddMoney(p, repAmount);
            }
        }

        private void ResolveDistrictBuildingDestroyed(PlayerState player)
        {
            var groupId = BoardController.Instance?.GetFirstDistrictBuildingGroupIdForPlayer(player.PlayerId);
            if (string.IsNullOrEmpty(groupId)) return;

            EventBus.Emit(GameEvent.DistrictBuildingDestroyed,
                groupId);
        }

        // --- Helpers ---

        public static int GetMoveCardCEP(ChanceCardData card)
        {
            if (card == null) return 0;
            return card.cardType switch
            {
                ChanceCardType.MoveVelo  => Mathf.Max(0, card.cepAmount),
                ChanceCardType.MoveCar   => Mathf.Max(0, card.cepAmount),
                ChanceCardType.MovePlane => Mathf.Max(0, card.cepAmount),
                _ => 0
            };
        }

        /// <summary>Debug: set the next card draw to be a specific card.</summary>
        public void SetForcedNextCard(ScriptableObject card, bool isChance)
        {
            if (isChance)
                _forcedNextChanceCard = card as ChanceCardData;
            else
                _forcedNextEventCard = card as EventCardData;
        }

        /// <summary>Debug: clear any forced next card draws.</summary>
        public void ClearForcedCard()
        {
            _forcedNextChanceCard = null;
            _forcedNextEventCard = null;
        }
    }

    public struct BuildingDegradedPayload
    {
        public int PlayerId;
        public ChanceCardData Card;
    }
}
