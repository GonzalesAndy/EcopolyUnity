using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Ecopoly.Utils;
using Ecopoly.Core;
using Ecopoly.Cards;
using Ecopoly.Data;

namespace Ecopoly.Core
{
    /// <summary>
    /// Manages the turn cycle: player order, dice rolls, available actions,
    /// doubles, jail. Only one player acts at a time.
    /// </summary>
    public class TurnManager : MonoBehaviour
    {
        public static TurnManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private BoardController _board;

        // --- Turn State ---

        private int _currentPlayerIndex;
        private int _consecutiveDoubles;
        private bool _waitingForPlayerAction;       // pause while player chooses an action
        private bool _waitingForReadyConfirm;       // pause after human turn, before next
        private bool _waitingForRenovationDismiss;  // pause while player interacts with renovation panel
        private bool _waitingForDilemmaVote;        // pause while dilemma voting is in progress
        private bool _dilemmaVoteResult;            // result passed from DilemmaVoteUI
        private bool _turnInProgress;

        // Debug: forced next tile
        private int _forcedNextTile = -1;

        public PlayerState CurrentPlayer
            => GameManager.Instance.Players[_currentPlayerIndex];

        // --- Lifecycle ---

        private void Awake()
        {
            if (Instance != null) { Debug.Log("[TurnManager] Duplicate instance destroyed."); Destroy(gameObject); return; }
            Instance = this;
            Debug.Log($"[TurnManager] Awake. Instance assigned: {gameObject.name}");
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.GameStarted, OnGameStarted);
            EventBus.On(GameEvent.PlayerEliminated, OnPlayerEliminated);
            EventBus.On(GameEvent.PlayerReadyForNextTurn, OnPlayerReadyForNextTurn);
            EventBus.On(GameEvent.DilemmaVoteSubmitted, OnDilemmaVoteSubmitted);
            Debug.Log("[TurnManager] OnEnable: subscribed to GameStarted");
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.GameStarted, OnGameStarted);
            EventBus.Off(GameEvent.PlayerEliminated, OnPlayerEliminated);
            EventBus.Off(GameEvent.PlayerReadyForNextTurn, OnPlayerReadyForNextTurn);
            EventBus.Off(GameEvent.DilemmaVoteSubmitted, OnDilemmaVoteSubmitted);
            Debug.Log("[TurnManager] OnDisable: unsubscribed from GameStarted");
        }

        private void OnGameStarted(object _)
        {
            Debug.Log("[TurnManager] OnGameStarted received. Starting turn loop.");
            _currentPlayerIndex = 0;
            StartCoroutine(BeginTurn());
        }

        // --- Turn Cycle ---

        public IEnumerator BeginTurn()
        {
            if (GameManager.Instance.IsGameOver) yield break;
            Debug.Log($"[TurnManager] BeginTurn for index {_currentPlayerIndex}");

            // Skip eliminated players
            while (CurrentPlayer.IsEliminated)
            {
                _currentPlayerIndex = NextPlayerIndex(_currentPlayerIndex);
            }

            _turnInProgress = true;
            _consecutiveDoubles = CurrentPlayer.ConsecutiveDoubles;
            Debug.Log($"[TurnManager] Emitting TurnStarted for PlayerId={CurrentPlayer.PlayerId} Name={CurrentPlayer.PlayerName} IsBot={CurrentPlayer.IsBot}");
            EventBus.Emit(GameEvent.TurnStarted, CurrentPlayer);

            // Bot: automatic decision
            if (CurrentPlayer.IsBot)
            {
                yield return new WaitForSeconds(
                    CurrentPlayer.BotPersonality?.decisionDelay ?? 0.5f);
                yield return StartCoroutine(ExecuteBotTurn());
            }
            // Human: wait for input (RollDice or PlayMoveCard)
            else
            {
                _waitingForPlayerAction = true;
                // UI will call RollDice() or PlayMoveCard()
            }
        }

        // --- Human Player Actions ---

        /// <summary>Rolls the dice for the current player.</summary>
        public void RollDice()
        {
            if (!_waitingForPlayerAction || CurrentPlayer.IsBot) return;
            _waitingForPlayerAction = false;
            StartCoroutine(ExecuteDiceRoll());
        }

        /// <summary>Plays a movement card (bike/car/plane) instead of rolling dice.</summary>
        public void PlayMoveCard(string cardId, int chosenSteps)
        {
            if (!_waitingForPlayerAction || CurrentPlayer.IsBot) return;
            _waitingForPlayerAction = false;
            StartCoroutine(ExecuteMoveCard(cardId, chosenSteps));
        }

        // --- Dice Logic ---

        private IEnumerator ExecuteDiceRoll()
        {
            int die1 = Random.Range(1, Constants.DICE_SIDES + 1);
            int die2 = Random.Range(1, Constants.DICE_SIDES + 1);
            bool isDouble = die1 == die2;
            int steps = die1 + die2;

            EventBus.Emit(GameEvent.DiceRolled, new DiceRollPayload { Die1 = die1, Die2 = die2 });

            if (CurrentPlayer.IsInJail)
            {
                yield return StartCoroutine(HandleJailRoll(isDouble, steps));
                yield break;
            }

            if (isDouble)
            {
                _consecutiveDoubles++;
                CurrentPlayer.ConsecutiveDoubles = _consecutiveDoubles;
                if (_consecutiveDoubles >= Constants.DOUBLE_JAIL_COUNT)
                {
                    SendCurrentPlayerToJail();
                    yield return StartCoroutine(EndTurn());
                    yield break;
                }
            }
            else
            {
                _consecutiveDoubles = 0;
                CurrentPlayer.ConsecutiveDoubles = 0;
            }

            yield return StartCoroutine(MovePlayer(CurrentPlayer, steps));
            yield return StartCoroutine(ProcessLanding(CurrentPlayer));

            if (IsWaitingForDilemmaVote)
            {
                yield return StartCoroutine(WaitForDilemmaVote());
            }

            yield return StartCoroutine(ResolveBotLandingDecision(CurrentPlayer));

            if (isDouble && !CurrentPlayer.IsInJail)
                yield return StartCoroutine(BeginTurn()); // play again on doubles
            else
                yield return StartCoroutine(EndTurn());
        }

        private IEnumerator ExecuteMoveCard(string cardId, int steps)
        {
            var cardData = CardManager.GetChanceCard(cardId);
            if (cardData != null)
            {
                int cepCost = CardManager.GetMoveCardCEP(cardData);
                if (cepCost > 0)
                {
                    CEPSource source = cardData.cardType == ChanceCardType.MovePlane
                        ? CEPSource.CardAvion
                        : CEPSource.CardVoiture;
                    Player.CEPController.GetForPlayer(CurrentPlayer.PlayerId)
                        ?.AddCEP(cepCost, source);
                }
            }

            CurrentPlayer.CardHandIds.Remove(cardId);
            EventBus.Emit(GameEvent.PlayerHandChanged, CurrentPlayer.PlayerId);
            _consecutiveDoubles = 0;
            CurrentPlayer.ConsecutiveDoubles = 0;

            yield return StartCoroutine(MovePlayer(CurrentPlayer, steps));
            yield return StartCoroutine(ProcessLanding(CurrentPlayer));

            if (IsWaitingForDilemmaVote)
            {
                yield return StartCoroutine(WaitForDilemmaVote());
            }

            yield return StartCoroutine(ResolveBotLandingDecision(CurrentPlayer));
            yield return StartCoroutine(EndTurn());
        }

        // --- Jail ---

        private IEnumerator HandleJailRoll(bool isDouble, int steps)
        {
            if (isDouble)
            {
                ReleaseFromJail(CurrentPlayer);
                yield return StartCoroutine(MovePlayer(CurrentPlayer, steps));
                yield return StartCoroutine(ProcessLanding(CurrentPlayer));
                yield return StartCoroutine(ResolveBotLandingDecision(CurrentPlayer));
                yield return StartCoroutine(EndTurn());
            }
            else
            {
                CurrentPlayer.JailTurnsRemaining--;
                if (CurrentPlayer.JailTurnsRemaining <= 0)
                {
                    // Mandatory bail payment after 3 turns
                    DeductMoney(CurrentPlayer, Constants.JAIL_BAIL_COST);
                    ReleaseFromJail(CurrentPlayer);
                    yield return StartCoroutine(MovePlayer(CurrentPlayer, steps));
                    yield return StartCoroutine(ProcessLanding(CurrentPlayer));
                    yield return StartCoroutine(ResolveBotLandingDecision(CurrentPlayer));
                }
                yield return StartCoroutine(EndTurn());
            }
        }

        public void PayBailToLeaveJail()
        {
            if (!CurrentPlayer.IsInJail) return;
            if (CurrentPlayer.Money < Constants.JAIL_BAIL_COST)
            {
                EventBus.Emit(GameEvent.UINotification, new Ecopoly.Utils.UINotificationPayload { Message = "Not enough money to pay bail.", Color = Color.red, Duration = 3f, Priority = 0, PlayerId = CurrentPlayer.PlayerId });
                return;
            }
            DeductMoney(CurrentPlayer, Constants.JAIL_BAIL_COST);
            ReleaseFromJail(CurrentPlayer);
            // After paying bail, the player rolls dice normally this turn
            _waitingForPlayerAction = true;
        }

        public void UseGetOutOfJailCard()
        {
            if (!CurrentPlayer.IsInJail || !CurrentPlayer.HasGetOutOfJailCard) return;
            CurrentPlayer.HasGetOutOfJailCard = false;
            // Remove the get-out-of-jail card from the hand
            var jailCard = CurrentPlayer.CardHandIds
                .Find(id => { var c = CardManager.GetChanceCard(id); return c != null && c.cardType == ChanceCardType.GetOutOfJail; });
            if (jailCard != null)
            {
                CurrentPlayer.CardHandIds.Remove(jailCard);
                EventBus.Emit(GameEvent.PlayerHandChanged, CurrentPlayer.PlayerId);
            }
            ReleaseFromJail(CurrentPlayer);
            _waitingForPlayerAction = true;
        }

        public void SendCurrentPlayerToJail()
        {
            var p = CurrentPlayer;
            p.IsInJail = true;
            p.JailTurnsRemaining = Constants.MAX_JAIL_TURNS;
            p.ConsecutiveDoubles = 0;
            p.BoardPosition = Constants.JAIL_POSITION;
            // Teleport the pawn immediately to the jail tile.
            EventBus.Emit(GameEvent.PlayerMoved, new PlayerMovePayload
            {
                PlayerId = p.PlayerId,
                NewPosition = Constants.JAIL_POSITION
            });
            EventBus.Emit(GameEvent.PlayerJailed, p.PlayerId);
        }

        private void ReleaseFromJail(PlayerState p)
        {
            p.IsInJail = false;
            p.JailTurnsRemaining = 0;
            EventBus.Emit(GameEvent.PlayerReleasedFromJail, p.PlayerId);
        }

        // --- Forced Move (MoveToTile cards) ---

        /// <summary>
        /// Teleports <paramref name="player"/> to <paramref name="targetPosition"/>,
        /// awards GO money if the path crosses position 0, triggers stable emissions,
        /// and runs the full landing pipeline - identical to a normal dice-move.
        /// Called by CardManager for MoveToTile chance cards.
        /// </summary>
        public IEnumerator ForceMovePlayerToTile(PlayerState player, int targetPosition)
        {
            int startPos = player.BoardPosition;
            int steps = (targetPosition - startPos + Constants.BOARD_SIZE) % Constants.BOARD_SIZE;

            // If steps == 0 the player is already on the target; still process landing.
            if (steps > 0)
            {
                // Walk step-by-step so the pawn animates correctly and GO is detected.
                for (int i = 0; i < steps; i++)
                {
                    player.BoardPosition = (player.BoardPosition + 1) % Constants.BOARD_SIZE;
                    EventBus.Emit(GameEvent.PlayerMoved, new PlayerMovePayload
                    {
                        PlayerId = player.PlayerId,
                        NewPosition = player.BoardPosition,
                        IsFinalStep = (i == steps - 1)
                    });
                    yield return new WaitForSeconds(
                        GameManager.Instance.Settings.pawnMoveStepDuration);

                    // Passing GO
                    if (player.BoardPosition == 0 && startPos != 0)
                    {
                        AddMoney(player, Constants.GO_REWARD);
                        EventBus.Emit(GameEvent.PlayerPassedGo, player.PlayerId);
                        _board.TriggerStableEmissions(player);
                    }
                }
            }

            yield return StartCoroutine(ProcessLanding(player));

            if (IsWaitingForDilemmaVote)
            {
                yield return StartCoroutine(WaitForDilemmaVote());
            }
        }

        // --- Movement ---

        private IEnumerator MovePlayer(PlayerState player, int steps)
        {
            int startPos = player.BoardPosition;
            for (int i = 0; i < steps; i++)
            {
                player.BoardPosition = (player.BoardPosition + 1) % Constants.BOARD_SIZE;
                EventBus.Emit(GameEvent.PlayerMoved, new PlayerMovePayload
                {
                    PlayerId = player.PlayerId,
                    NewPosition = player.BoardPosition,
                    IsFinalStep = (i == steps - 1)
                });
                yield return new WaitForSeconds(
                    GameManager.Instance.Settings.pawnMoveStepDuration);

                // Passing GO
                if (player.BoardPosition == 0 && startPos != 0)
                {
                    AddMoney(player, Constants.GO_REWARD);
                    EventBus.Emit(GameEvent.PlayerPassedGo, player.PlayerId);
                    _board.TriggerStableEmissions(player);
                }
            }
        }

        // --- Landing ---

        private IEnumerator ProcessLanding(PlayerState player)
        {
            EventBus.Emit(GameEvent.PlayerLanded, new PlayerLandedPayload
            {
                PlayerId = player.PlayerId,
                Position = player.BoardPosition
            });
            yield return _board.ProcessTileLanding(player);

            // Debug: if a forced next tile is set, teleport to it now
            if (_forcedNextTile >= 0)
            {
                int targetTile = Mathf.Clamp(_forcedNextTile, 0, Constants.BOARD_SIZE - 1);
                player.BoardPosition = targetTile;
                _forcedNextTile = -1;
                EventBus.Emit(GameEvent.PlayerMoved, new PlayerMovePayload
                {
                    PlayerId = player.PlayerId,
                    NewPosition = targetTile,
                    IsFinalStep = true
                });
                yield return new WaitForSeconds(GameManager.Instance.Settings.pawnMoveStepDuration);
                
                // Process the new tile landing
                EventBus.Emit(GameEvent.PlayerLanded, new PlayerLandedPayload
                {
                    PlayerId = player.PlayerId,
                    Position = player.BoardPosition
                });
                yield return _board.ProcessTileLanding(player);
            }
        }

        // --- End of Turn ---

        private IEnumerator EndTurn()
        {
            _turnInProgress = false;
            EventBus.Emit(GameEvent.TurnEnded, CurrentPlayer);

            if (GameManager.Instance.IsGameOver) yield break;

            // Human player: wait for the "Ready" button click
            if (!CurrentPlayer.IsBot)
            {
                _waitingForReadyConfirm = true;
                while (_waitingForReadyConfirm)
                {
                    yield return null;
                }
            }

            _currentPlayerIndex = NextPlayerIndex(_currentPlayerIndex);
            yield return new WaitForSeconds(0.5f);
            yield return StartCoroutine(BeginTurn());
        }

        /// <summary>Called by the local player's "Ready" button.</summary>
        public void ConfirmReadyForNextTurn()
        {
            if (!_waitingForReadyConfirm) return;
            _waitingForReadyConfirm = false;
            EventBus.Emit(GameEvent.PlayerReadyForNextTurn, CurrentPlayer.PlayerId);
        }

        /// <summary>
        /// Called by RenovationOfferUI when the player closes the panel
        /// (renovate, sell, or skip). Releases the landing coroutine.
        /// </summary>
        public void DismissRenovationOffer()
        {
            _waitingForRenovationDismiss = false;
        }

        /// <summary>
        /// Called by CardManager when a Dilemma card is drawn.
        /// Suspends the turn coroutine until all players vote.
        /// </summary>
        public void BeginDilemmaWait()
        {
            _waitingForDilemmaVote = true;
        }

        /// <summary>True while the dilemma vote panel is open.</summary>
        public bool IsWaitingForDilemmaVote => _waitingForDilemmaVote;

        /// <summary>
        /// Waits until the dilemma vote is resolved.
        /// Called from CardManager as a coroutine.
        /// </summary>
        public IEnumerator WaitForDilemmaVote()
        {
            while (_waitingForDilemmaVote)
                yield return null;
        }

        /// <summary>Returns the result of the last dilemma vote.</summary>
        public bool DilemmaVoteResult => _dilemmaVoteResult;

        private void OnDilemmaVoteSubmitted(object payload)
        {
            if (!(payload is bool result)) return;
            _dilemmaVoteResult = result;
            _waitingForDilemmaVote = false;
        }

        /// <summary>
        /// Called by DilemmaVoteUI when voting closes (for bots / single-player).
        /// </summary>
        public void SubmitDilemmaVote(bool allPaid)
        {
            EventBus.Emit(GameEvent.DilemmaVoteSubmitted, allPaid);
        }

        /// <summary>
        /// Opens the renovation gate so HandlePropertyLanding can wait.
        /// Must be called before emitting UIRenovationRequested.
        /// </summary>
        public void BeginRenovationWait()
        {
            _waitingForRenovationDismiss = true;
        }

        /// <summary>True while the renovation panel is open.</summary>
        public bool IsWaitingForRenovation => _waitingForRenovationDismiss;

        private void OnPlayerReadyForNextTurn(object _)
        {
            // Handled directly via _waitingForReadyConfirm; hook available for future extensions.
        }

        // --- Bot ---

        private IEnumerator ExecuteBotTurn()
        {
            // TurnManager is the single authority for the bot's turn.
            // BotBrain.HandleLanding is called explicitly here after the move and landing
            // pipeline completes, preventing double-execution that occurred when
            // BotBrain listened to PlayerLanded independently.
            _waitingForPlayerAction = false;
            var botBrain = AI.BotBrain.GetForPlayer(CurrentPlayer.PlayerId);

            // If jailed and holding a card, consume it before choosing an action.
            if (CurrentPlayer.IsInJail && CurrentPlayer.HasGetOutOfJailCard)
            {
                bool useJailCard = botBrain == null || botBrain.ShouldUseGetOutOfJailCard();
                if (useJailCard)
                    UseGetOutOfJailCard();
            }

            if (!CurrentPlayer.IsInJail && botBrain != null)
            {
                string moveCardId = botBrain.DecideMoveCardToPlay();
                if (!string.IsNullOrEmpty(moveCardId))
                {
                    int maxSteps = GetMoveCardMaxSteps(moveCardId);
                    if (maxSteps > 0)
                    {
                        int chosenSteps = Mathf.Clamp(botBrain.DecideMoveCardSteps(moveCardId), 1, maxSteps);
                        yield return StartCoroutine(ExecuteMoveCard(moveCardId, chosenSteps));
                        yield break;
                    }
                }
            }

            yield return StartCoroutine(ExecuteDiceRoll());
        }

        private IEnumerator ResolveBotLandingDecision(PlayerState player)
        {
            if (player == null || !player.IsBot) yield break;

            var botBrain = AI.BotBrain.GetForPlayer(player.PlayerId);
            if (botBrain != null)
                yield return StartCoroutine(botBrain.HandleLanding());
        }

        private int GetMoveCardMaxSteps(string cardId)
        {
            var cardData = CardManager.GetChanceCard(cardId);
            if (cardData == null) return 0;

            bool isMoveCard = cardData.cardType == ChanceCardType.MoveVelo
                || cardData.cardType == ChanceCardType.MoveCar
                || cardData.cardType == ChanceCardType.MovePlane;

            return isMoveCard ? Mathf.Max(1, cardData.maxMoveDistance) : 0;
        }

        // --- Helpers ---

        private int NextPlayerIndex(int current)
        {
            var players = GameManager.Instance.Players;
            int next = (current + 1) % players.Count;
            // Find the next non-eliminated player
            int attempts = 0;
            while (players[next].IsEliminated && attempts < players.Count)
            {
                next = (next + 1) % players.Count;
                attempts++;
            }
            return next;
        }

        public void AddMoney(PlayerState player, int amount)
        {
            int oldValue = player.Money;
            player.Money += amount;
            EventBus.Emit(GameEvent.MoneyChanged, new MoneyChangePayload
            {
                PlayerId = player.PlayerId,
                OldValue = oldValue,
                NewValue = player.Money,
                Delta = amount
            });
        }

        public bool DeductMoney(PlayerState player, int amount)
        {
            if (player.Money < amount)
            {
                CheckBankruptcy(player, amount);
                return false;
            }
            int oldValue = player.Money;
            player.Money -= amount;
            EventBus.Emit(GameEvent.MoneyChanged, new MoneyChangePayload
            {
                PlayerId = player.PlayerId,
                OldValue = oldValue,
                NewValue = player.Money,
                Delta = -amount
            });
            return true;
        }

        private void CheckBankruptcy(PlayerState player, int debtAmount)
        {
            if (player.Money < debtAmount)
            {
                EliminatePlayer(player);
                EventBus.Emit(GameEvent.PlayerBankrupt, player.PlayerId);
                EventBus.Emit(GameEvent.PlayerEliminated, player.PlayerId);
            }
        }

        /// <summary>
        /// Marks the player as eliminated and returns all their properties to the bank
        /// at Level 1. Called both on bankruptcy and on CEP-triggered elimination.
        /// </summary>
        private void EliminatePlayer(PlayerState player)
        {
            if (player.IsEliminated) return;
            player.IsEliminated = true;
            _board.ReturnPropertiesToBank(player);
        }

        private void OnPlayerEliminated(object payload)
        {
            // CEP-triggered elimination: the CEPController already set IsEliminated = true
            // before emitting this event. We still need to return properties to the bank.
            if (!(payload is int playerId)) return;
            var player = GameManager.Instance?.GetPlayer(playerId);
            if (player == null) return;
            // EliminatePlayer is idempotent: the IsEliminated guard prevents double-cleanup.
            EliminatePlayer(player);
        }

        // --- Debug Helpers ---

        /// <summary>Debug: force the next player move to end at a specific tile.</summary>
        public void SetForcedNextTile(int tilePosition)
        {
            _forcedNextTile = Mathf.Clamp(tilePosition, 0, Constants.BOARD_SIZE - 1);
        }

        /// <summary>Debug: clear any forced next tile.</summary>
        public void ClearForcedNextTile()
        {
            _forcedNextTile = -1;
        }
    }

    // --- Payloads ---

    public struct DiceRollPayload { public int Die1, Die2; }
    public struct PlayerMovePayload { public int PlayerId, NewPosition; public bool IsFinalStep; }
    public struct PlayerLandedPayload { public int PlayerId, Position; }
}
