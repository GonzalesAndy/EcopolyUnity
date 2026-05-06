using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ecopoly.Utils;
using Ecopoly.Data;

namespace Ecopoly.Core
{
    /// <summary>
    /// Represents and manages the 3D game board.
    /// Responsibilities: reference 3D tiles, trigger tile effects,
    /// handle stable emissions on passing GO.
    /// </summary>
    public class BoardController : MonoBehaviour
    {
        public static BoardController Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private BoardConfig _config;
        [SerializeField] private Transform _tilesRoot;

        // Runtime tile array (index = board position 0-39)
        private BoardTile[] _tiles = new BoardTile[Constants.BOARD_SIZE];

        // Fast propertyId -> PropertyData lookup
        private Dictionary<string, PropertyData> _propertyDict
            = new Dictionary<string, PropertyData>();

        // Dictionary propertyId -> owning player (playerId, -1 = none)
        private Dictionary<string, int> _ownerMap = new Dictionary<string, int>();

        // Dictionary propertyId -> current renovation level
        private Dictionary<string, int> _renovationLevels = new Dictionary<string, int>();

        // Dictionary groupId -> active DistrictBuilding (null = none)
        private Dictionary<string, DistrictBuildingData> _districtBuildings
            = new Dictionary<string, DistrictBuildingData>();

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;

            if (_config == null)
                _config = Resources.Load<BoardConfig>(Constants.SO_BOARD_CONFIG);

            if (_config != null)
                InitializeBoard();
            else
                Debug.LogError("[BoardController] Missing BoardConfig. Assign _config or create Resources/Board/BoardConfig.asset.");
        }

        // --- Initialization ---

        private void InitializeBoard()
        {
            // Index 3D tiles by position
            var tiles = _tilesRoot != null
                ? _tilesRoot.GetComponentsInChildren<BoardTile>()
                : FindObjectsOfType<BoardTile>();
            foreach (var tile in tiles)
                if (tile.Position >= 0 && tile.Position < Constants.BOARD_SIZE)
                    _tiles[tile.Position] = tile;

            // Index property data
            foreach (var prop in _config.allProperties)
            {
                _propertyDict[prop.propertyId] = prop;
                _ownerMap[prop.propertyId] = -1;
                _renovationLevels[prop.propertyId] = 1; // default level 1
            }
        }

        // --- Tile Landing ---

        public IEnumerator ProcessTileLanding(PlayerState player)
        {
            var tileConfig = _config.tiles[player.BoardPosition];
            var tile = _tiles[player.BoardPosition];

            switch (tileConfig.tileType)
            {
                case TileType.Property:
                    EventBus.Emit(GameEvent.UINotification,
                        new Ecopoly.Utils.UINotificationPayload { Message = $"Landed on {tileConfig.displayName}", Color = null, Duration = 3f, Priority = 0, PlayerId = player.PlayerId });
                    yield return StartCoroutine(HandlePropertyLanding(player, tileConfig));
                    break;
                case TileType.Station:
                    yield return StartCoroutine(HandleStationLanding(player, tileConfig));
                    break;
                case TileType.Chance:
                    Cards.CardManager.Instance.DrawChanceCard(player);
                    break;
                case TileType.Event:
                    Cards.CardManager.Instance.DrawEventCard(player);
                    break;
                case TileType.Tax:
                    TurnManager.Instance.DeductMoney(player, tileConfig.taxAmount);
                    EventBus.Emit(GameEvent.UINotification,
                        new Ecopoly.Utils.UINotificationPayload { Message = $"Tax: pay M{tileConfig.taxAmount}", Color = null, Duration = 3f, Priority = 0, PlayerId = player.PlayerId });
                    break;
                case TileType.GoToJail:
                    TurnManager.Instance.SendCurrentPlayerToJail();
                    break;
                case TileType.Go:
                    // GO bonus is already handled in MovePlayer when passing the tile
                    break;
                case TileType.Jail:
                case TileType.FreeParking:
                    // Neutral tiles: no action
                    break;
            }
            yield return null;
        }

        // --- Properties ---

        private IEnumerator HandlePropertyLanding(PlayerState player, TileConfig tile)
        {
            string pid = tile.propertyId;

            if (string.IsNullOrEmpty(pid))
            {
                Debug.LogWarning($"[BoardController] HandlePropertyLanding: TileConfig at position {player.BoardPosition} has no propertyId. Check SO_BoardConfig.tiles.");
                yield break;
            }

            if (!_ownerMap.TryGetValue(pid, out int ownerId))
            {
                Debug.LogWarning($"[BoardController] HandlePropertyLanding: propertyId '{pid}' not found in _ownerMap. Ensure it is listed in SO_BoardConfig.allProperties.");
                yield break;
            }

            if (ownerId == -1)
            {
                // Offer purchase to the player (UI -> PlayerController)
                Debug.Log($"[BoardController] Unowned property '{pid}' - emitting UICardDisplayRequested for player {player.PlayerId}");
                EventBus.Emit(GameEvent.UICardDisplayRequested,
                    new PropertyOfferPayload { PlayerId = player.PlayerId, PropertyId = pid });
            }
            else if (ownerId != player.PlayerId)
            {
                // Pay rent
                int rent = CalculateRent(pid, ownerId);
                var owner = GameManager.Instance.GetPlayer(ownerId);
                if (owner == null)
                {
                    Debug.LogWarning($"[BoardController] Owner {ownerId} for property '{pid}' not found in GameManager.Players.");
                    yield break;
                }
                TurnManager.Instance.DeductMoney(player, rent);
                TurnManager.Instance.AddMoney(owner, rent);
                EventBus.Emit(GameEvent.RentPaid, new RentPayload
                {
                    PayerId = player.PlayerId,
                    OwnerId = ownerId,
                    Amount = rent,
                    PropertyId = pid
                });
            }
            else
            {
                // Player landed on their own property: offer renovation.
                // Bots handle renovation themselves in BotBrain.DecideRenovations,
                // which is called by ResolveBotLandingDecision immediately after
                // ProcessLanding returns — no UI gate needed.
                if (!player.IsBot)
                {
                    int currentLevel = _renovationLevels[pid];
                    TurnManager.Instance.BeginRenovationWait();
                    EventBus.Emit(GameEvent.UIRenovationRequested, new RenovationOfferPayload
                    {
                        PlayerId = player.PlayerId,
                        PropertyId = pid,
                        CurrentLevel = currentLevel
                    });
                    yield return new WaitUntil(() => !TurnManager.Instance.IsWaitingForRenovation);
                }
            }
            yield return null;
        }

        private IEnumerator HandleStationLanding(PlayerState player, TileConfig tile)
        {
            int ownerId = _ownerMap.TryGetValue(tile.propertyId, out int id) ? id : -1;
            if (ownerId == -1)
            {
                EventBus.Emit(GameEvent.UICardDisplayRequested,
                    new PropertyOfferPayload { PlayerId = player.PlayerId, PropertyId = tile.propertyId });
            }
            else if (ownerId != player.PlayerId)
            {
                int stationsOwned = CountStationsOwnedBy(ownerId);
                int rent = Constants.STATION_RENTS[Mathf.Clamp(stationsOwned, 0, 4)];
                var owner = GameManager.Instance.GetPlayer(ownerId);
                TurnManager.Instance.DeductMoney(player, rent);
                TurnManager.Instance.AddMoney(owner, rent);
            }
            yield return null;
        }

        // --- Rent Calculation ---

        public int CalculateRent(string propertyId, int ownerId)
        {
            if (!_propertyDict.TryGetValue(propertyId, out var prop)) return 0;
            var owner = GameManager.Instance.GetPlayer(ownerId);

            int baseRent = prop.baseRent;

            // Monopoly bonus
            bool hasMonopoly = HasMonopoly(ownerId, prop.groupId);
            if (hasMonopoly) baseRent = prop.monopolyRent;

            // Commercial district building bonus
            if (_districtBuildings.TryGetValue(prop.groupId, out var building)
                && building != null
                && building.buildingType == DistrictBuildingType.Commercial)
            {
                baseRent += building.rentBonus;
            }

            return baseRent;
        }

        // --- Purchase ---

        public bool BuyProperty(PlayerState player, string propertyId)
        {
            if (!_propertyDict.TryGetValue(propertyId, out var prop)) return false;
            if (_ownerMap[propertyId] != -1) return false;
            if (player.Money < prop.purchasePrice) return false;

            TurnManager.Instance.DeductMoney(player, prop.purchasePrice);
            _ownerMap[propertyId] = player.PlayerId;
            player.OwnedPropertyIds.Add(propertyId);

            // Immediate CEP on purchase
            Player.CEPController.GetForPlayer(player.PlayerId)
                ?.AddCEP(prop.cepOnPurchase, CEPSource.PropertyPurchase);

            EventBus.Emit(GameEvent.PropertyPurchased,
                new PropertyEventPayload { PlayerId = player.PlayerId, PropertyId = propertyId });
            return true;
        }

        /// <summary>
        /// Debug only: forcibly assigns a property to a player for free, stripping it
        /// from any current owner. Does NOT deduct money or add CEP.
        /// </summary>
        public bool ForceAssignProperty(PlayerState player, string propertyId)
        {
            if (!_propertyDict.ContainsKey(propertyId)) return false;

            // Strip from current owner if any
            int currentOwner = _ownerMap.TryGetValue(propertyId, out int oid) ? oid : -1;
            if (currentOwner != -1)
            {
                var prev = GameManager.Instance?.GetPlayer(currentOwner);
                prev?.OwnedPropertyIds.Remove(propertyId);
            }

            _ownerMap[propertyId] = player.PlayerId;
            if (!player.OwnedPropertyIds.Contains(propertyId))
                player.OwnedPropertyIds.Add(propertyId);

            EventBus.Emit(GameEvent.PropertyPurchased,
                new PropertyEventPayload { PlayerId = player.PlayerId, PropertyId = propertyId });
            return true;
        }

        // --- Elimination ---

        /// <summary>
        /// Returns all properties owned by the eliminated player to the bank:
        /// clears ownership, resets renovation to Level 1, destroys any district
        /// building whose monopoly was broken, and clears the player's ownership list.
        /// Called by TurnManager when a player is eliminated (bankruptcy or CEP max).
        /// </summary>
        public void ReturnPropertiesToBank(PlayerState eliminatedPlayer)
        {
            // Collect group IDs that may lose their monopoly so we check each once.
            var affectedGroups = new HashSet<string>();

            foreach (string pid in eliminatedPlayer.OwnedPropertyIds)
            {
                if (!_propertyDict.TryGetValue(pid, out var prop)) continue;

                _ownerMap[pid] = -1;
                _renovationLevels[pid] = 1;
                affectedGroups.Add(prop.groupId);

                EventBus.Emit(GameEvent.PropertySold,
                    new PropertyEventPayload
                    {
                        PlayerId = eliminatedPlayer.PlayerId,
                        PropertyId = pid
                    });
            }

            eliminatedPlayer.OwnedPropertyIds.Clear();

            // Destroy district buildings whose monopoly was broken.
            foreach (string groupId in affectedGroups)
                CheckDistrictBuildingOnMonopolyLost(eliminatedPlayer.PlayerId, groupId);
        }

        // --- Sell ---

        public bool SellProperty(PlayerState player, string propertyId)
        {
            if (!_propertyDict.TryGetValue(propertyId, out var prop)) return false;
            if (_ownerMap[propertyId] != player.PlayerId) return false;

            int sellPrice = Mathf.FloorToInt(prop.purchasePrice * Constants.SELL_RATIO);
            TurnManager.Instance.AddMoney(player, sellPrice);
            _ownerMap[propertyId] = -1;
            _renovationLevels[propertyId] = 1;
            player.OwnedPropertyIds.Remove(propertyId);

            // Destroy district building if monopoly is lost
            CheckDistrictBuildingOnMonopolyLost(player.PlayerId, prop.groupId);

            EventBus.Emit(GameEvent.PropertySold,
                new PropertyEventPayload { PlayerId = player.PlayerId, PropertyId = propertyId });
            return true;
        }

        // --- Renovation ---

        public bool RenovateProperty(PlayerState player, string propertyId)
        {
            if (!_propertyDict.TryGetValue(propertyId, out var prop)) return false;
            if (_ownerMap[propertyId] != player.PlayerId) return false;

            int currentLevel = _renovationLevels[propertyId];
            if (currentLevel >= Constants.MAX_RENOVATION_LEVEL) return false;

            int costIndex = currentLevel - 1; // 0-based
            int cost = prop.renovationCosts[costIndex];
            int cepCost = prop.renovationCEPCosts[costIndex];

            if (player.Money < cost) return false;

            TurnManager.Instance.DeductMoney(player, cost);
            _renovationLevels[propertyId] = currentLevel + 1;
            Player.CEPController.GetForPlayer(player.PlayerId)
                ?.AddCEP(cepCost, CEPSource.Renovation);

            EventBus.Emit(GameEvent.PropertyRenovated, new RenovationEventPayload
            {
                PlayerId = player.PlayerId,
                PropertyId = propertyId,
                OldLevel = currentLevel,
                NewLevel = currentLevel + 1
            });
            return true;
        }

        // --- Degradation (disasters) ---

        public void DegradeProperty(string propertyId, int levels = 1)
        {
            if (!_renovationLevels.ContainsKey(propertyId)) return;
            int current = _renovationLevels[propertyId];
            int newLevel = Mathf.Max(Constants.MIN_RENOVATION_LEVEL, current - levels);

            _renovationLevels[propertyId] = newLevel;

            int ownerId = _ownerMap[propertyId];
            EventBus.Emit(GameEvent.PropertyDegraded, new RenovationEventPayload
            {
                PlayerId = ownerId,
                PropertyId = propertyId,
                OldLevel = current,
                NewLevel = newLevel
            });
        }

        public void SetPropertyLevelCap(string propertyId, int maxLevel)
        {
            if (!_renovationLevels.ContainsKey(propertyId)) return;
            int current = _renovationLevels[propertyId];
            if (current > maxLevel)
            {
                _renovationLevels[propertyId] = maxLevel;
                int ownerId = _ownerMap[propertyId];
                EventBus.Emit(GameEvent.PropertyDegraded, new RenovationEventPayload
                {
                    PlayerId = ownerId, PropertyId = propertyId,
                    OldLevel = current, NewLevel = maxLevel
                });
            }
        }

        // --- District Buildings ---

        public bool BuildDistrictBuilding(PlayerState player, string groupId,
            DistrictBuildingData building)
        {
            if (!HasMonopoly(player.PlayerId, groupId)) return false;
            if (_districtBuildings.ContainsKey(groupId) && _districtBuildings[groupId] != null)
                return false; // a building already exists
            if (player.Money < building.cost) return false;

            TurnManager.Instance.DeductMoney(player, building.cost);
            _districtBuildings[groupId] = building;
            EventBus.Emit(GameEvent.DistrictBuildingBuilt,
                new PropertyEventPayload { PlayerId = player.PlayerId, PropertyId = groupId });
            return true;
        }

        private void CheckDistrictBuildingOnMonopolyLost(int playerId, string groupId)
        {
            if (_districtBuildings.TryGetValue(groupId, out var b) && b != null)
            {
                if (!HasMonopoly(playerId, groupId))
                {
                    _districtBuildings[groupId] = null;
                    EventBus.Emit(GameEvent.DistrictBuildingDestroyed, groupId);
                }
            }
        }

        public string GetFirstDistrictBuildingGroupIdForPlayer(int playerId)
        {
            foreach (var kvp in _districtBuildings)
            {
                if (kvp.Value == null) continue;
                if (!HasMonopoly(playerId, kvp.Key)) continue;
                return kvp.Key;
            }

            return null;
        }

        // --- Stable Emissions (passing GO) ---

        public void TriggerStableEmissions(PlayerState player)
        {
            foreach (string pid in player.OwnedPropertyIds)
            {
                if (!_propertyDict.TryGetValue(pid, out var prop)) continue;
                int level = _renovationLevels[pid];
                int emission = prop.stableEmissionsPerLevel[level - 1];

                // Ecological district building reduction
                if (_districtBuildings.TryGetValue(prop.groupId, out var building)
                    && building?.buildingType == DistrictBuildingType.Ecological)
                {
                    int propsInGroup = CountPropertiesInGroup(player.PlayerId, prop.groupId);
                    emission = Mathf.Max(0,
                        emission - (building.cepReductionPerTurn / Mathf.Max(1, propsInGroup)));
                }

                if (emission > 0)
                    Player.CEPController.GetForPlayer(player.PlayerId)
                        ?.AddCEP(emission, CEPSource.StableEmission);
            }
        }

        // --- Helpers ---

        public bool HasMonopoly(int playerId, string groupId)
        {
            var groupProps = _config.allProperties
                .Where(p => p.groupId == groupId)
                .Select(p => p.propertyId);
            return groupProps.All(pid =>
                _ownerMap.TryGetValue(pid, out int owner) && owner == playerId);
        }

        public int GetRenovationLevel(string propertyId)
            => _renovationLevels.TryGetValue(propertyId, out int l) ? l : 1;

        public int GetOwner(string propertyId)
            => _ownerMap.TryGetValue(propertyId, out int id) ? id : -1;

        /// <summary>
        /// Client-side only: directly sets the owner in the local _ownerMap without
        /// any gameplay side-effects. Called by EcopolyNetworkManager ClientRpcs to keep
        /// the client's ownership state in sync with the authoritative server state.
        /// </summary>
        public void SetOwnerForSync(string propertyId, int ownerId)
        {
            if (_ownerMap.ContainsKey(propertyId))
                _ownerMap[propertyId] = ownerId;
        }

        /// <summary>
        /// Client-side only: directly sets a property's renovation level without gameplay
        /// side-effects. Called by EcopolyNetworkManager ClientRpcs (PropertyDegraded).
        /// </summary>
        public void SetRenovationLevelForSync(string propertyId, int level)
        {
            if (_renovationLevels.ContainsKey(propertyId))
                _renovationLevels[propertyId] = level;
        }

        public BoardTile GetTile(int position)
            => position >= 0 && position < Constants.BOARD_SIZE ? _tiles[position] : null;

        public TileConfig GetTileConfig(int position)
            => _config.tiles[position];

        public PropertyData GetPropertyData(string propertyId)
            => _propertyDict.TryGetValue(propertyId, out var d) ? d : null;

        /// <summary>
        /// Returns all property groups, each with their ordered PropertyData list.
        /// Groups are keyed by groupId. Only color properties are included (no stations).
        /// </summary>
        public Dictionary<string, List<PropertyData>> GetAllPropertiesGrouped()
        {
            var result = new Dictionary<string, List<PropertyData>>();
            foreach (var prop in _config.allProperties)
            {
                if (!result.ContainsKey(prop.groupId))
                    result[prop.groupId] = new List<PropertyData>();
                result[prop.groupId].Add(prop);
            }
            return result;
        }

        /// <summary>Returns the active DistrictBuildingData for a group, or null if none.</summary>
        public DistrictBuildingData GetDistrictBuilding(string groupId)
            => _districtBuildings.TryGetValue(groupId, out var b) ? b : null;

        public string[] GetTileIdsInNeighborhood(int centerPosition, bool includeCenter = true)
        {
            var result = new List<string>();
            int[] positions = includeCenter
                ? new[] { centerPosition - 1, centerPosition, centerPosition + 1 }
                : new[] { centerPosition - 1, centerPosition + 1 };

            foreach (int pos in positions)
            {
                int wrapped = ((pos % Constants.BOARD_SIZE) + Constants.BOARD_SIZE)
                    % Constants.BOARD_SIZE;
                var cfg = _config.tiles[wrapped];
                if (!string.IsNullOrEmpty(cfg.propertyId))
                    result.Add(cfg.propertyId);
            }
            return result.ToArray();
        }

        private int CountStationsOwnedBy(int playerId)
        {
            return _config.tiles.Count(t =>
                t.tileType == TileType.Station
                && _ownerMap.TryGetValue(t.propertyId, out int owner)
                && owner == playerId);
        }

        private int CountPropertiesInGroup(int playerId, string groupId)
        {
            return _config.allProperties.Count(p =>
                p.groupId == groupId
                && _ownerMap.TryGetValue(p.propertyId, out int owner)
                && owner == playerId);
        }
    }

    public struct PropertyOfferPayload
    {
        public int PlayerId;
        public string PropertyId;
    }
}
