using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ecopoly.Utils
{
    /// <summary>
    /// Decoupled global event system.
    /// All communication between managers should go through the EventBus.
    /// Usage: EventBus.Emit(GameEvent.CEPChanged, payload);
    ///        EventBus.On(GameEvent.TurnStarted, OnTurnStarted);
    ///        EventBus.Off(GameEvent.TurnStarted, OnTurnStarted);
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<GameEvent, List<Action<object>>> _listeners
            = new Dictionary<GameEvent, List<Action<object>>>();

        public static void On(GameEvent evt, Action<object> callback)
        {
            if (!_listeners.ContainsKey(evt))
                _listeners[evt] = new List<Action<object>>();
            if (!_listeners[evt].Contains(callback))
                _listeners[evt].Add(callback);
        }

        public static void Off(GameEvent evt, Action<object> callback)
        {
            if (_listeners.TryGetValue(evt, out var list))
                list.Remove(callback);
        }

        public static void Emit(GameEvent evt, object payload = null)
        {
            if (!_listeners.TryGetValue(evt, out var list)) return;
            // Snapshot to avoid modification during iteration
            var snapshot = new List<Action<object>>(list);
            foreach (var cb in snapshot)
            {
                try { cb?.Invoke(payload); }
                catch (Exception e) { Debug.LogError($"[EventBus] Exception in listener for {evt}: {e}"); }
            }
        }

        public static void Clear()
        {
            _listeners.Clear();
        }
    }

    public enum GameEvent
    {
        // --- Game Cycle ---
        GameStarted,
        GameEnded,
        TurnStarted,
        TurnEnded,
        DiceRolled,
        PlayerMoved,
        PlayerLanded,

        // --- Player ---
        PlayerEliminated,
        PlayerBankrupt,
        PlayerCEPMaxReached,
        PlayerJailed,
        PlayerReleasedFromJail,
        PlayerPassedGo,
        MoneyChanged,

        // --- CEP ---
        CEPChanged,                 // payload: CEPChangePayload
        GlobalCEPChanged,
        GlobalCEPThresholdChanged,  // payload: int (new intensity level 1-4)
        GlobalGameOver,

        // --- Properties ---
        PropertyPurchased,          // payload: PropertyEventPayload
        PropertySold,
        PropertyRenovated,          // payload: RenovationEventPayload
        PropertyDegraded,
        DistrictBuildingBuilt,
        DistrictBuildingDestroyed,
        RentPaid,                   // payload: RentPayload

        // --- Cards ---
        ChanceCardDrawn,            // payload: ChanceCardData
        EventCardDrawn,             // payload: EventCardData
        DilemmaCardResolved,        // payload: bool (true = all players paid)

        // --- Disasters ---
        DisasterTriggered,          // payload: DisasterEventPayload
        DisasterResolved,

        // --- Camera ---
        CameraSwitched,             // payload: CameraMode

        // --- UI ---
        UICardDisplayRequested,
        UIRenovationRequested,      // payload: RenovationOfferPayload
        UINotification,             // payload: UINotificationPayload
        UIPlayMoveCardRequested,    // payload: MoveCardPickerPayload
        UIDilemmaVoteRequested,     // payload: DilemmaVotePayload

        // --- Dilemma Vote ---
        DilemmaVoteSubmitted,       // payload: bool (true = all paid)
        DilemmaVoteResult,          // payload: bool (true = all paid) - broadcast to close UI on all clients

        // --- Human Turn Confirmation ---
        PlayerReadyForNextTurn,     // payload: int (playerId)

        // --- Card Inventory ---
        PlayerHandChanged,          // payload: int (playerId)

        // --- Properties Panel ---
        UIPropertiesPanelRequested, // payload: null (toggle)
        PropertiesOwnershipChanged, // payload: null (refresh list)

        // --- Dev / Spectator ---
        SpectatorModeStarted,       // payload: null - emitted when launching bots-only from the editor
    }

    // --- Payloads
    public struct CEPChangePayload
    {
        public int PlayerId;
        public int Delta;
        public int NewValue;
        public CEPSource Source;
    }

    public struct MoneyChangePayload
    {
        public int PlayerId;
        public int OldValue;
        public int NewValue;
        public int Delta;
    }

    public struct PropertyEventPayload
    {
        public int PlayerId;
        public string PropertyId;
    }

    public struct RenovationEventPayload
    {
        public int PlayerId;
        public string PropertyId;
        public int OldLevel;
        public int NewLevel;
    }

    public struct RenovationOfferPayload
    {
        public int PlayerId;
        public string PropertyId;
        public int CurrentLevel;
    }

    public struct RentPayload
    {
        public int PayerId;
        public int OwnerId;
        public int Amount;
        public string PropertyId;
    }

    public struct DisasterEventPayload
    {
        public string DisasterId;
        public int IntensityLevel;
        public string[] AffectedTileIds;
    }

    public struct UINotificationPayload
    {
        // The message to display in the HUD notification.
        public string Message;

        // Optional color for the notification text/background. Null uses HUD default.
        public Color? Color;

        // Duration in seconds for how long the notification should stay visible.
        // If 0 or negative, HUD should fall back to its default duration.
        public float Duration;

        // Optional priority for ordering/stacking notifications. Higher = more important.
        public int Priority;

        // Optional player ID for context-aware messages (e.g., property purchases).
        // -1 means no specific player context.
        public int PlayerId;
    }

    public enum CameraMode { FPS, TopDown }

    public struct MoveCardPickerPayload
    {
        public string CardId;
        public int MaxSteps;
        public int CEPCost;
        public string CardDisplayName;
        public int LocalPlayerId;
    }

    public struct DilemmaVotePayload
    {
        public string CardId;
        public string DisplayText;
        public int CostPerPlayer;
        public int CEPEffect;
    }

    public struct DistrictBuildingEventPayload
    {
        public int PlayerId;
        public string GroupId;
        public Ecopoly.Data.DistrictBuildingType BuildingType;
    }

    public enum CEPSource
    {
        PropertyPurchase,
        StableEmission,
        CardVoiture,
        CardAvion,
        ChanceCard,
        EventCard,
        Renovation,
        DistrictBuilding,
        Penalty,
        Bonus,
    }
}

