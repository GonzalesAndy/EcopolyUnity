using Unity.Netcode;
using UnityEngine;
using Ecopoly.Core;
using Ecopoly.Utils;

namespace Ecopoly.Network
{
    /// <summary>
    /// Per-player NetworkBehaviour. Synchronizes vital data
    /// (money, CEP, position) in real-time.
    /// </summary>
    public class NetworkPlayerState : NetworkBehaviour
    {
        // Network variables synchronized server -> clients
        public NetworkVariable<int> Money         = new NetworkVariable<int>();
        public NetworkVariable<int> PersonalCEP   = new NetworkVariable<int>();
        public NetworkVariable<int> BoardPosition = new NetworkVariable<int>();
        public NetworkVariable<bool> IsEliminated = new NetworkVariable<bool>();
        public NetworkVariable<bool> IsInJail     = new NetworkVariable<bool>();

        private int _localPlayerId;

        public override void OnNetworkSpawn()
        {
            EcopolyNetworkManager.Instance?.RegisterNetworkPlayer(OwnerClientId, this);

            // Subscribe to changes to update the local GameManager
            Money.OnValueChanged         += OnMoneyChanged;
            PersonalCEP.OnValueChanged   += OnCEPChanged;
            BoardPosition.OnValueChanged += OnPositionChanged;
            IsEliminated.OnValueChanged  += OnEliminatedChanged;
        }

        public override void OnNetworkDespawn()
        {
            Money.OnValueChanged         -= OnMoneyChanged;
            PersonalCEP.OnValueChanged   -= OnCEPChanged;
            BoardPosition.OnValueChanged -= OnPositionChanged;
            IsEliminated.OnValueChanged  -= OnEliminatedChanged;
        }

        public void SetPlayerId(int id) => _localPlayerId = id;

        // --- Sync Callbacks ---

        private void OnMoneyChanged(int oldVal, int newVal)
        {
            var state = GameManager.Instance.GetPlayer(_localPlayerId);
            if (state != null) state.Money = newVal;
        }

        private void OnCEPChanged(int oldVal, int newVal)
        {
            var state = GameManager.Instance.GetPlayer(_localPlayerId);
            if (state != null)
            {
                state.PersonalCEP = newVal;
                EventBus.Emit(GameEvent.CEPChanged,
                    new CEPChangePayload
                    {
                        PlayerId = _localPlayerId,
                        Delta    = newVal - oldVal,
                        NewValue = newVal,
                        Source   = CEPSource.ChanceCard // source unknown on client
                    });
            }
        }

        private void OnPositionChanged(int oldVal, int newVal)
        {
            var state = GameManager.Instance.GetPlayer(_localPlayerId);
            if (state != null) state.BoardPosition = newVal;
        }

        private void OnEliminatedChanged(bool oldVal, bool newVal)
        {
            if (newVal)
                EventBus.Emit(GameEvent.PlayerEliminated, _localPlayerId);
        }

        // --- Server Updates ---

        [ServerRpc]
        public void UpdateMoneyServerRpc(int newValue)
        {
            if (IsServer) Money.Value = newValue;
        }

        [ServerRpc]
        public void UpdateCEPServerRpc(int newValue)
        {
            if (IsServer) PersonalCEP.Value = newValue;
        }

        [ServerRpc]
        public void UpdatePositionServerRpc(int newPosition)
        {
            if (IsServer) BoardPosition.Value = newPosition;
        }
    }
}
