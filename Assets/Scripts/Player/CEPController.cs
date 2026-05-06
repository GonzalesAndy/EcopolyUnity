using System.Collections.Generic;
using UnityEngine;
using Ecopoly.Utils;

namespace Ecopoly.Player
{
    /// <summary>
    /// Manages a player's personal CEP counter.
    /// Must be on the same GameObject as the PlayerController.
    /// </summary>
    public class CEPController : MonoBehaviour
    {
        // Static registry: playerId -> instance
        private static readonly Dictionary<int, CEPController> _registry
            = new Dictionary<int, CEPController>();

        public static CEPController GetForPlayer(int playerId)
            => _registry.TryGetValue(playerId, out var c) ? c : null;

        public static void ClearRegistry() => _registry.Clear();

        [Header("Player Reference")]
        [SerializeField] private int _playerId;

        private Core.PlayerState _state;

        // --- Read ---

        public int PersonalCEP => _state?.PersonalCEP ?? 0;
        public float NormalizedPersonalCEP
            => Mathf.Clamp01((float)PersonalCEP / Constants.MAX_PERSONAL_CEP);

        public void Initialize(int playerId, Core.PlayerState state)
        {
            _playerId = playerId;
            _state = state;
            _registry[playerId] = this;
        }

        private void OnDestroy()
        {
            if (_registry.ContainsKey(_playerId))
                _registry.Remove(_playerId);
        }

        // --- Modification ---

        /// <summary>Add CEP (positive = increase).</summary>
        public void AddCEP(int delta, CEPSource source)
        {
            if (_state == null || _state.IsEliminated) return;
            if (delta == 0) return;

            int oldValue = _state.PersonalCEP;
            _state.PersonalCEP = Mathf.Clamp(_state.PersonalCEP + delta, 0, Constants.MAX_PERSONAL_CEP);
            int actualDelta = _state.PersonalCEP - oldValue;

            EventBus.Emit(GameEvent.CEPChanged, new CEPChangePayload
            {
                PlayerId = _playerId,
                Delta = actualDelta,
                NewValue = _state.PersonalCEP,
                Source = source
            });

            // Check for CEP-triggered elimination
            if (_state.PersonalCEP >= Constants.MAX_PERSONAL_CEP)
            {
                Debug.Log($"[CEP] Player {_playerId} eliminated - max CEP reached.");
                _state.IsEliminated = true;
                EventBus.Emit(GameEvent.PlayerCEPMaxReached, _playerId);
                EventBus.Emit(GameEvent.PlayerEliminated, _playerId);
            }
        }

        /// <summary>Reduce CEP (ecological bonus).</summary>
        public void ReduceCEP(int amount, CEPSource source)
            => AddCEP(-amount, source);
    }
}
