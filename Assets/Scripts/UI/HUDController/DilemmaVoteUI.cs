using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Unity.Netcode;
using Ecopoly.Utils;
using Ecopoly.Core;
using Ecopoly.Network;
using Ecopoly.AI;

namespace Ecopoly.UI
{
    /// <summary>
    /// Modal that handles dilemma card voting.
    ///
    /// Multiplayer path (server authority):
    ///   - Each human client casts exactly one vote via SubmitPlayerDilemmaVoteServerRpc.
    ///   - The server tallies human + bot votes and broadcasts the result via
    ///     SyncDilemmaVoteResultClientRpc, which emits DilemmaVoteResult on every client.
    ///   - This component listens for DilemmaVoteResult to close the panel.
    ///
    /// Offline / single-player fallback:
    ///   - Votes are tallied locally (human + bots) and DilemmaVoteResult is emitted
    ///     directly once all votes are in.
    /// </summary>
    public class DilemmaVoteUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject _panel;

        [Header("Labels")]
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _descriptionText;
        [SerializeField] private TextMeshProUGUI _costText;
        [SerializeField] private TextMeshProUGUI _cepEffectText;
        [SerializeField] private TextMeshProUGUI _statusText;

        [Header("Buttons")]
        [SerializeField] private Button _voteYesButton;
        [SerializeField] private Button _voteNoButton;

        // Offline-only vote state
        private int  _totalVoters;
        private int  _yesVotes;
        private int  _noVotes;
        private bool _localVoteCast;

        private void Awake()
        {
            if (_panel != null) _panel.SetActive(false);
            _voteYesButton?.onClick.AddListener(OnVoteYes);
            _voteNoButton?.onClick.AddListener(OnVoteNo);
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.UIDilemmaVoteRequested, OnDilemmaVoteRequested);
            EventBus.On(GameEvent.DilemmaVoteResult,      OnDilemmaVoteResult);
            EventBus.On(GameEvent.TurnEnded,              OnTurnEnded);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.UIDilemmaVoteRequested, OnDilemmaVoteRequested);
            EventBus.Off(GameEvent.DilemmaVoteResult,      OnDilemmaVoteResult);
            EventBus.Off(GameEvent.TurnEnded,              OnTurnEnded);
        }

        // --- Open
        private void OnDilemmaVoteRequested(object payload)
        {
            if (!(payload is DilemmaVotePayload data)) return;
            Open(data);
        }

        private void Open(DilemmaVotePayload data)
        {
            if (!gameObject.activeInHierarchy) gameObject.SetActive(true);

            _localVoteCast = false;
            _yesVotes      = 0;
            _noVotes       = 0;

            // Voter count only used in the offline path
            var players = GameManager.Instance?.Players;
            _totalVoters = 0;
            if (players != null)
                foreach (var p in players)
                    if (!p.IsEliminated) _totalVoters++;
            _totalVoters = Mathf.Max(1, _totalVoters);

            if (_titleText != null)       _titleText.text       = "Collective Dilemma";
            if (_descriptionText != null) _descriptionText.text = data.DisplayText;
            if (_costText != null)        _costText.text        = $"Cost per player: M{data.CostPerPlayer}";
            if (_cepEffectText != null)   _cepEffectText.text   = $"CEP effect: {data.CEPEffect} (shared)";
            if (_statusText != null)      _statusText.text      = "Cast your vote:";

            SetButtonsInteractable(true);

            _panel.SetActive(true);
            _panel.transform.localScale = Vector3.zero;
            _panel.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack);

            // In offline mode, bots vote locally; in multiplayer the server handles them.
            if (!IsNetworkActive())
                StartCoroutine(CollectBotVotesOffline(data.CostPerPlayer));
        }

        // --- Vote buttons
        private void OnVoteYes() => CastLocalVote(true);
        private void OnVoteNo()  => CastLocalVote(false);

        private void CastLocalVote(bool yes)
        {
            if (_localVoteCast) return;
            _localVoteCast = true;
            SetButtonsInteractable(false);

            if (_statusText != null)
                _statusText.text = yes ? "You voted YES. Waiting for others..." : "You voted NO. Waiting for others...";

            if (IsNetworkActive())
            {
                // Multiplayer: send individual vote to server for authoritative tally.
                bool isClient = !NetworkManager.Singleton.IsServer;
                if (isClient)
                    EcopolyNetworkManager.Instance?.SubmitPlayerDilemmaVoteServerRpc(yes);
                else
                    EcopolyNetworkManager.Instance?.RegisterHostDilemmaVote(yes);
            }
            else
            {
                // Offline: tally locally.
                RegisterVoteOffline(yes);
            }
        }

        // --- Offline vote tally
        private void RegisterVoteOffline(bool yes)
        {
            if (yes) _yesVotes++;
            else     _noVotes++;

            int total = _yesVotes + _noVotes;
            if (_statusText != null)
                _statusText.text = $"Votes: {total}/{_totalVoters}";

            if (total >= _totalVoters)
            {
                bool allPaid = (_noVotes == 0);
                // Emit result locally — OnDilemmaVoteResult will handle closing + resolution
                EventBus.Emit(GameEvent.DilemmaVoteResult, allPaid);
                // Also submit to TurnManager to unblock the coroutine
                TurnManager.Instance?.SubmitDilemmaVote(allPaid);
            }
        }

        private IEnumerator CollectBotVotesOffline(int costPerPlayer)
        {
            var players = GameManager.Instance?.Players;
            if (players == null) yield break;

            foreach (var p in players)
            {
                if (p.IsEliminated || !p.IsBot) continue;

                float delay = p.BotPersonality?.decisionDelay ?? 0.8f;
                yield return new WaitForSeconds(delay);

                var botBrain = BotBrain.GetForPlayer(p.PlayerId);
                bool botVote = botBrain != null
                    ? botBrain.DecideDilemmaVote(costPerPlayer)
                    : (p.BotPersonality?.cooperation ?? 0.5f) >= 0.5f;
                RegisterVoteOffline(botVote);

                if (_yesVotes + _noVotes >= _totalVoters) yield break;
            }
        }

        // --- Close (triggered by server broadcast or offline result)
        private void OnDilemmaVoteResult(object payload)
        {
            if (!(payload is bool allPaid)) return;
            StartCoroutine(ShowResultAndClose(allPaid));
        }

        private IEnumerator ShowResultAndClose(bool allPaid)
        {
            if (_statusText != null)
                _statusText.text = allPaid
                    ? "Everyone voted YES! Paying collectively..."
                    : "Not everyone agreed. CEP rises!";

            yield return new WaitForSeconds(1.5f);
            Close();
        }

        private void Close()
        {
            if (_panel == null) return;
            _panel.transform.DOScale(0f, 0.15f).SetEase(Ease.InBack)
                .OnComplete(() => _panel.SetActive(false));
        }

        // --- Utilities
        private static bool IsNetworkActive()
            => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private void SetButtonsInteractable(bool interactable)
        {
            if (_voteYesButton != null) _voteYesButton.interactable = interactable;
            if (_voteNoButton  != null) _voteNoButton.interactable  = interactable;
        }

        private void OnTurnEnded(object _)
        {
            if (_panel != null && _panel.activeSelf) Close();
        }
    }
}

