using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ecopoly.Utils;
using Ecopoly.Data;
using Ecopoly.Core;
using Ecopoly.Network;

namespace Ecopoly.Cards
{
    /// <summary>
    /// Resolves the mechanical and visual effects of Event (disaster) cards
    /// according to the global intensity level.
    ///
    /// Per-level VFX and CozyWeather profiles are defined directly on
    /// <see cref="EventCardData"/> via <see cref="DisasterLevelEffect"/>.
    ///
    /// VFX spawning and weather changes are applied locally on every client
    /// via <see cref="EcopolyNetworkManager.SyncDisasterVFXClientRpc"/>.
    /// </summary>
    public class DisasterResolver : MonoBehaviour
    {
        public static DisasterResolver Instance { get; private set; }

        [Header("Lake / Board Center Spawn")]
        [Tooltip("World-space position of the lake center used for prefab spawns tagged 'LakeCenter'.")]
        [SerializeField] private Transform _lakeCenterTransform;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        // --- Public entry point
        /// <summary>
        /// Resolves mechanical effects on the server and broadcasts VFX/weather to all clients.
        /// Called by <see cref="CardManager"/> on the server only.
        /// </summary>
        public void Resolve(PlayerState triggeringPlayer, EventCardData card)
        {
            if (triggeringPlayer == null || card == null)
            {
                Debug.LogError("[DisasterResolver] Resolve called with null player or card — aborting.");
                return;
            }

            if (card.effectsByLevel == null || card.effectsByLevel.Length == 0)
            {
                Debug.LogError($"[DisasterResolver] EventCard '{card.cardId}' has no effectsByLevel configured — aborting.");
                return;
            }

            int level      = GameManager.Instance.CurrentIntensityLevel;
            int levelIndex = Mathf.Clamp(level - 1, 0, card.effectsByLevel.Length - 1);
            var effect     = card.effectsByLevel[levelIndex];

            // --- Mechanical effects (server-authoritative)
            string[] affectedPropertyIds = GetAffectedPropertyIds(triggeringPlayer.BoardPosition, effect);
            ApplyEffect(card.disasterType, effect, affectedPropertyIds, level, triggeringPlayer);

            // --- Notify all clients: spawn VFX + change weather
            Vector3 playerWorldPos = GetPlayerWorldPosition(triggeringPlayer.BoardPosition);
            Vector3 lakePos        = _lakeCenterTransform != null
                ? _lakeCenterTransform.position
                : Vector3.zero;

            // Apply locally on the host (the ClientRpc is filtered out by ShouldIgnoreLocalEcho)
            ApplyDisasterVFXLocal(card.cardId, level, playerWorldPos, lakePos);

            // Broadcast to all non-host clients
            EcopolyNetworkManager.Instance?.SyncDisasterVFXClientRpc(
                card.cardId,
                level,
                playerWorldPos,
                lakePos);

            // --- Game event
            EventBus.Emit(GameEvent.DisasterTriggered, new DisasterEventPayload
            {
                DisasterId      = card.cardId,
                IntensityLevel  = level,
                AffectedTileIds = affectedPropertyIds
            });
        }

        // --- Called locally on every client (including host)
        /// <summary>
        /// Spawns VFX prefabs and triggers weather change for this client.
        /// Invoked by the ClientRpc in <see cref="EcopolyNetworkManager"/>.
        /// </summary>
        public void ApplyDisasterVFXLocal(string cardId, int level, Vector3 playerWorldPos, Vector3 lakePos)
        {
            var card = CardManager.GetEventCard(cardId);
            if (card == null)
            {
                Debug.LogWarning($"[DisasterResolver] Card not found: {cardId}");
                return;
            }

            int levelIndex = Mathf.Clamp(level - 1, 0, card.effectsByLevel.Length - 1);
            var effect     = card.effectsByLevel[levelIndex];

            StartCoroutine(SpawnAndPlayDisaster(effect, playerWorldPos, lakePos, card.disasterType));
        }

        // --- Mechanical effect application (server)
        private void ApplyEffect(DisasterType type, DisasterLevelEffect effect,
            string[] affectedIds, int intensityLevel, PlayerState triggeringPlayer)
        {
            var board   = BoardController.Instance;
            var tm      = TurnManager.Instance;
            var players = GameManager.Instance.Players;

            bool hasAnyMechanic = effect.moneyFlat > 0 || effect.moneyPerHouse > 0
                || effect.levelReduction > 0 || effect.targetLevelCap > 0
                || effect.freeUpgradeReward || effect.isGlobalDisaster;

            // --- No mechanical effect (e.g. Lightning L1: atmosphere only)
            if (!hasAnyMechanic)
            {
                NotifyPlayer(triggeringPlayer.PlayerId,
                    $"{type}: atmospheric warning — no penalty this time.",
                    new Color(0.8f, 0.8f, 0.2f), 4f);
                return;
            }

            // --- Global disaster: every active player pays moneyPerHouse for each property
            if (effect.isGlobalDisaster)
            {
                foreach (var player in players.Where(p => !p.IsEliminated))
                {
                    int total = ComputePerHousePenalty(player, effect, type);
                    if (total > 0)
                    {
                        tm.DeductMoney(player, total);
                        NotifyPlayer(player.PlayerId,
                            $"Global {type}! Pay M{total} ({effect.moneyPerHouse} per property).",
                            Color.red, 5f);
                    }
                }
                return;
            }

            // --- Flat money on triggering player (no property required)
            if (effect.moneyFlat > 0 && !effect.affectsNeighbors)
            {
                bool exempt = CheckExemptionForPlayer(type, effect, triggeringPlayer);
                if (exempt)
                {
                    NotifyPlayer(triggeringPlayer.PlayerId,
                        $"{type}: your eco-renovation shields you! (exempt from M{effect.moneyFlat} penalty)",
                        Color.green, 4f);
                }
                else
                {
                    tm.DeductMoney(triggeringPlayer, effect.moneyFlat);
                    NotifyPlayer(triggeringPlayer.PlayerId,
                        $"{type}: pay M{effect.moneyFlat}.", Color.red, 4f);
                }
                return;
            }

            // --- Per-house penalty on triggering player's own portfolio
            if (effect.moneyPerHouse > 0 && !effect.affectsNeighbors && !effect.isGlobalDisaster)
            {
                int total = ComputePerHousePenalty(triggeringPlayer, effect, type);
                if (total > 0)
                {
                    tm.DeductMoney(triggeringPlayer, total);
                    NotifyPlayer(triggeringPlayer.PlayerId,
                        $"{type}: pay M{total} ({effect.moneyPerHouse} per property).", Color.red, 4f);
                }
                else
                {
                    NotifyPlayer(triggeringPlayer.PlayerId,
                        $"{type}: no owned properties — no penalty.", new Color(0.8f, 0.8f, 0.2f), 3f);
                }
                return;
            }

            // --- Neighbor-spread flat money
            if (effect.affectsNeighbors && effect.moneyFlat > 0)
            {
                bool anyAffected = false;
                foreach (string pid in affectedIds)
                {
                    int ownerId = board.GetOwner(pid);
                    if (ownerId == -1) continue;
                    var owner = GameManager.Instance.GetPlayer(ownerId);
                    if (owner == null || owner.IsEliminated) continue;
                    tm.DeductMoney(owner, effect.moneyFlat);
                    NotifyPlayer(owner.PlayerId,
                        $"{type}: nearby disaster! Pay M{effect.moneyFlat}.", Color.red, 4f);
                    anyAffected = true;
                }
                if (!anyAffected)
                    NotifyPlayer(triggeringPlayer.PlayerId,
                        $"{type}: no neighboring properties affected.", new Color(0.8f, 0.8f, 0.2f), 3f);
                return;
            }

            // --- Property-targeted effects: level reduction / cap / free upgrade
            // Uses neighbor tiles when affectsNeighbors, else triggering player's portfolio.
            string[] targetIds = affectedIds.Length > 0
                ? affectedIds
                : triggeringPlayer.OwnedPropertyIds.ToArray();

            if (targetIds.Length == 0)
            {
                NotifyPlayer(triggeringPlayer.PlayerId,
                    $"{type}: no properties affected.", new Color(0.8f, 0.8f, 0.2f), 3f);
                return;
            }

            foreach (string pid in targetIds)
            {
                int ownerId = board.GetOwner(pid);
                if (ownerId == -1) continue;
                var owner = GameManager.Instance.GetPlayer(ownerId);
                if (owner == null || owner.IsEliminated) continue;

                int currentLevel = board.GetRenovationLevel(pid);

                if (effect.levelReduction > 0)
                {
                    board.DegradeProperty(pid, effect.levelReduction);
                    NotifyPlayer(owner.PlayerId,
                        $"{type}: property '{pid}' degraded by {effect.levelReduction} level(s)!",
                        Color.red, 5f);
                }

                if (effect.targetLevelCap > 0)
                    board.SetPropertyLevelCap(pid, effect.targetLevelCap);

                if (effect.moneyPerHouse > 0)
                {
                    int amount = effect.moneyPerHouse;
                    if (type == DisasterType.Hurricane && currentLevel >= Constants.MAX_RENOVATION_LEVEL)
                        amount /= 2;
                    tm.DeductMoney(owner, amount);
                    NotifyPlayer(owner.PlayerId,
                        $"{type}: pay M{amount} for property '{pid}'.", Color.red, 4f);
                }

                if (effect.freeUpgradeReward && currentLevel == effect.freeUpgradeRequiredLevel)
                {
                    board.RenovateProperty(owner, pid);
                    NotifyPlayer(owner.PlayerId,
                        $"{type}: free renovation on '{pid}'!", Color.green, 4f);
                }
            }
        }

        /// <summary>Computes total per-house penalty for a player across their portfolio.</summary>
        private int ComputePerHousePenalty(PlayerState player, DisasterLevelEffect effect, DisasterType type)
        {
            int total = 0;
            foreach (string pid in player.OwnedPropertyIds)
            {
                int lvl    = BoardController.Instance.GetRenovationLevel(pid);
                int amount = effect.moneyPerHouse;
                if (type == DisasterType.Hurricane && lvl >= Constants.MAX_RENOVATION_LEVEL)
                    amount /= 2;
                total += amount;
            }
            return total;
        }

        /// <summary>
        /// Returns true if the triggering player is exempt from the flat money penalty.
        /// Sandstorm (Drought) exempts players who own at least one property at level 3+.
        /// </summary>
        private bool CheckExemptionForPlayer(DisasterType type, DisasterLevelEffect effect, PlayerState player)
        {
            if (type != DisasterType.Drought) return false;
            var board = BoardController.Instance;
            foreach (string pid in player.OwnedPropertyIds)
            {
                if (board.GetRenovationLevel(pid) >= 3)
                    return true;
            }
            return false;
        }

        private string[] GetAffectedPropertyIds(int boardPosition, DisasterLevelEffect effect)
        {
            if (effect.isGlobalDisaster)
                return GameManager.Instance.BoardConfig.allProperties
                    .Select(p => p.propertyId).ToArray();

            if (effect.affectsNeighbors)
                return BoardController.Instance.GetTileIdsInNeighborhood(boardPosition, true);

            // For single-tile effects, return the property at the current tile (may be empty
            // for event tiles — callers must handle the triggering-player fallback).
            var cfg = BoardController.Instance.GetTileConfig(boardPosition);
            if (!string.IsNullOrEmpty(cfg.propertyId))
                return new[] { cfg.propertyId };
            return System.Array.Empty<string>();
        }

        // --- Helpers
        private static void NotifyPlayer(int playerId, string message, Color color, float duration)
        {
            EventBus.Emit(GameEvent.UINotification, new UINotificationPayload
            {
                PlayerId = playerId,
                Message  = message,
                Color    = color,
                Duration = duration,
                Priority = 1
            });
        }

        // --- VFX + Weather coroutine (local per client)
        private IEnumerator SpawnAndPlayDisaster(DisasterLevelEffect effect,
            Vector3 playerWorldPos, Vector3 lakePos, DisasterType disasterType)
        {
            // Spawn primary VFX
            GameObject primaryInstance = SpawnVFX(effect.vfxPrefab, effect.spawnTarget, playerWorldPos, lakePos);

            // Spawn secondary VFX (e.g. fire ring at player while tornado spawns at lake)
            GameObject secondaryInstance = SpawnVFX(effect.secondVfxPrefab, effect.secondSpawnTarget, playerWorldPos, lakePos);

            // Apply CozyWeather profile (if any)
            bool isWildfire = disasterType == DisasterType.Wildfire;
            if (effect.weatherProfile != null && WeatherController.Instance != null)
            {
                WeatherController.Instance.ApplyDisasterWeather(
                    effect.weatherProfile,
                    effect.weatherDuration,
                    enableRedAmbiance: isWildfire);
            }

            // VFX lifetime matches the weather duration so prefabs stay visible
            // for the full disaster effect. Fall back to GameSettings if not set.
            float duration = effect.weatherDuration > 0f
                ? effect.weatherDuration
                : (GameManager.Instance != null
                    ? GameManager.Instance.Settings.disasterVFXDuration
                    : 4f);
            yield return new WaitForSeconds(duration);

            if (primaryInstance != null)   Destroy(primaryInstance);
            if (secondaryInstance != null) Destroy(secondaryInstance);

            EventBus.Emit(GameEvent.DisasterResolved, string.Empty);
        }

        private GameObject SpawnVFX(GameObject prefab, DisasterSpawnTarget target,
            Vector3 playerWorldPos, Vector3 lakePos)
        {
            if (prefab == null || target == DisasterSpawnTarget.None)
                return null;

            Vector3 spawnPos = target switch
            {
                DisasterSpawnTarget.TriggeringPlayer => playerWorldPos + Vector3.up * 0.5f,
                DisasterSpawnTarget.LakeCenter       => lakePos + Vector3.up * 0.5f,
                _                                    => Vector3.zero
            };

            return Instantiate(prefab, spawnPos, Quaternion.identity);
        }

        private Vector3 GetPlayerWorldPosition(int boardPosition)
        {
            var tile = BoardController.Instance?.GetTile(boardPosition);
            return tile != null ? tile.transform.position : Vector3.zero;
        }
    }
}

