using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ecopoly.Data;
using Ecopoly.Utils;
using UnityEditor;
using UnityEngine;

namespace Ecopoly.EditorTools
{
    public static class EcopolyDataImporter
    {
        private const string SourceRoot = "Assets/ScriptableObjects/Templates/ImportData";
        private const string GeneratedPropertiesRoot = "Assets/ScriptableObjects/Generated/Properties";
        private const string GeneratedDistrictRoot = "Assets/ScriptableObjects/Generated/DistrictBuildings";
        private const string GeneratedBotsRoot = "Assets/Resources/Bots";
        private const string ResourcesSettingsAssetPath = "Assets/Resources/Settings/GameSettings.asset";
        private const string ResourcesBoardAssetPath = "Assets/Resources/Board/BoardConfig.asset";
        private const string ResourcesChanceRoot = "Assets/Resources/Cards/Chance";
        private const string ResourcesEventRoot = "Assets/Resources/Cards/Event";

        private const string GameSettingsJsonPath = SourceRoot + "/game_settings.json";
        private const string PropertiesJsonPath = SourceRoot + "/properties.json";
        private const string DistrictJsonPath = SourceRoot + "/district_buildings.json";
        private const string BotsJsonPath = SourceRoot + "/bot_personalities.json";
        private const string BoardJsonPath = SourceRoot + "/board.json";
        private const string ChanceJsonPath = SourceRoot + "/chance_cards.json";
        private const string EventJsonPath = SourceRoot + "/event_cards.json";

        [MenuItem("Tools/Ecopoly/Data/Import All From JSON")]
        public static void ImportAll()
        {
            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    Debug.LogError("[EcopolyDataImporter] Unity is compiling/updating. Wait for compile to finish, then run import again.");
                    return;
                }

                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Debug.LogError("[EcopolyDataImporter] Import is disabled in Play Mode.");
                    return;
                }

                EnsureFolders();
                ValidateSourceFilesExist();

                var settingsAsset = ImportGameSettings();
                var propertyAssets = ImportProperties();
                var districtAssets = ImportDistrictBuildings();
                var botAssets = ImportBotPersonalities();
                var boardAsset = ImportBoardConfig(propertyAssets, districtAssets);
                var chanceAssets = ImportChanceCards();
                var eventAssets = ImportEventCards();

                ValidateBoardReferences(boardAsset, propertyAssets);
                ValidateChanceDeckSize(chanceAssets);
                ValidateEventCards(eventAssets);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[EcopolyDataImporter] Import complete. Settings={(settingsAsset != null ? 1 : 0)}, Properties={propertyAssets.Count}, DistrictBuildings={districtAssets.Count}, Bots={botAssets.Count}, Chance={chanceAssets.Count}, Event={eventAssets.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EcopolyDataImporter] Import failed: {ex}");
            }
        }

        [MenuItem("Tools/Ecopoly/Data/Open Import Data Folder")]
        public static void OpenImportFolder()
        {
            EnsureFolders();
            EditorUtility.RevealInFinder(SourceRoot);
        }

        [MenuItem("Tools/Ecopoly/Data/Rebuild Generated Assets (Delete + Import)")]
        public static void RebuildGeneratedAssets()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Debug.LogError("[EcopolyDataImporter] Unity is compiling/updating. Wait for compile to finish, then run rebuild again.");
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[EcopolyDataImporter] Rebuild is disabled in Play Mode.");
                return;
            }

            try
            {
                EnsureFolders();

                SafeDeleteAsset(ResourcesSettingsAssetPath);
                SafeDeleteAsset(ResourcesBoardAssetPath);
                SafeDeleteFolderContents(ResourcesChanceRoot);
                SafeDeleteFolderContents(ResourcesEventRoot);
                SafeDeleteFolderContents(GeneratedPropertiesRoot);
                SafeDeleteFolderContents(GeneratedDistrictRoot);
                SafeDeleteFolderContents(GeneratedBotsRoot);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                ImportAll();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EcopolyDataImporter] Rebuild failed: {ex}");
            }
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/ScriptableObjects");
            EnsureFolder("Assets/ScriptableObjects/Generated");
            EnsureFolder(GeneratedPropertiesRoot);
            EnsureFolder(GeneratedDistrictRoot);
            EnsureFolder(GeneratedBotsRoot);

            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Settings");
            EnsureFolder("Assets/Resources/Board");
            EnsureFolder("Assets/Resources/Cards");
            EnsureFolder(ResourcesChanceRoot);
            EnsureFolder(ResourcesEventRoot);

            EnsureFolder("Assets/ScriptableObjects/Templates");
            EnsureFolder(SourceRoot);
        }

        private static GameSettings ImportGameSettings()
        {
            var data = ReadJson<GameSettingsImportFile>(GameSettingsJsonPath);
            var entry = data.settings;
            if (entry == null)
                throw new InvalidDataException($"[EcopolyDataImporter] '{GameSettingsJsonPath}' must contain 'settings'.");

            return CreateOrUpdateAsset<GameSettings>(ResourcesSettingsAssetPath, so =>
            {
                so.startingMoney = entry.startingMoney;
                so.voiceMaxDistance = entry.voiceMaxDistance;
                so.voiceMinDistance = entry.voiceMinDistance;
                so.cameraSwitchBlend = entry.cameraSwitchBlend;
                so.pawnMoveStepDuration = entry.pawnMoveStepDuration;
                so.disasterVFXDuration = entry.disasterVFXDuration;
            });
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            var folder = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent ?? "Assets", folder);
        }

        private static Dictionary<string, PropertyData> ImportProperties()
        {
            var data = ReadJson<PropertyImportFile>(PropertiesJsonPath);
            var result = new Dictionary<string, PropertyData>(StringComparer.OrdinalIgnoreCase);

            if (data.properties == null)
                throw new InvalidDataException($"[EcopolyDataImporter] '{PropertiesJsonPath}' must contain 'properties' array.");
            if (data.properties.Count == 0)
                throw new InvalidDataException($"[EcopolyDataImporter] '{PropertiesJsonPath}' has an empty 'properties' array.");

            foreach (var entry in data.properties)
            {
                if (string.IsNullOrWhiteSpace(entry.propertyId))
                {
                    Debug.LogWarning("[EcopolyDataImporter] Skipping property without propertyId.");
                    continue;
                }

                var assetPath = $"{GeneratedPropertiesRoot}/SO_PropertyData_{SanitizeFileName(entry.propertyId)}.asset";
                var asset = CreateOrUpdateAsset<PropertyData>(assetPath, so =>
                {
                    so.propertyId = entry.propertyId.Trim();
                    so.displayName = entry.displayName ?? entry.propertyId;
                    so.groupId = entry.groupId ?? string.Empty;
                    so.groupColor = ParseColor(entry.groupColorHex, Color.white);

                    so.purchasePrice = entry.purchasePrice;
                    so.baseRent = entry.baseRent;
                    so.monopolyRent = entry.monopolyRent;

                    so.cepOnPurchase = entry.cepOnPurchase;
                    so.stableEmissionsPerLevel = NormalizeArray(entry.stableEmissionsPerLevel, 4);

                    so.renovationCosts = NormalizeArray(entry.renovationCosts, 3);
                    so.renovationCEPCosts = NormalizeArray(entry.renovationCEPCosts, 3);
                });

                result[asset.propertyId] = asset;
            }

            return result;
        }

        private static Dictionary<string, DistrictBuildingData> ImportDistrictBuildings()
        {
            var data = ReadJson<DistrictBuildingImportFile>(DistrictJsonPath);
            var result = new Dictionary<string, DistrictBuildingData>(StringComparer.OrdinalIgnoreCase);

            if (data.districtBuildings == null)
                throw new InvalidDataException($"[EcopolyDataImporter] '{DistrictJsonPath}' must contain 'districtBuildings' array.");
            if (data.districtBuildings.Count == 0)
                throw new InvalidDataException($"[EcopolyDataImporter] '{DistrictJsonPath}' has an empty 'districtBuildings' array.");

            foreach (var entry in data.districtBuildings)
            {
                if (string.IsNullOrWhiteSpace(entry.buildingId))
                {
                    Debug.LogWarning("[EcopolyDataImporter] Skipping district building without buildingId.");
                    continue;
                }

                var assetPath = $"{GeneratedDistrictRoot}/SO_DistrictBuildingData_{SanitizeFileName(entry.buildingId)}.asset";
                var asset = CreateOrUpdateAsset<DistrictBuildingData>(assetPath, so =>
                {
                    so.buildingId = entry.buildingId.Trim();
                    so.displayName = entry.displayName ?? entry.buildingId;
                    so.cost = entry.cost;
                    so.rentBonus = entry.rentBonus;
                    so.cepReductionPerTurn = entry.cepReductionPerTurn;
                    so.buildingType = ParseEnum(entry.buildingType, DistrictBuildingType.Commercial);
                });

                result[asset.buildingId] = asset;
            }

            return result;
        }

        private static Dictionary<string, BotPersonalityData> ImportBotPersonalities()
        {
            var data = ReadJson<BotPersonalityImportFile>(BotsJsonPath);
            var result = new Dictionary<string, BotPersonalityData>(StringComparer.OrdinalIgnoreCase);

            if (data.bots == null)
                throw new InvalidDataException($"[EcopolyDataImporter] '{BotsJsonPath}' must contain 'bots' array.");
            if (data.bots.Count == 0)
                throw new InvalidDataException($"[EcopolyDataImporter] '{BotsJsonPath}' has an empty 'bots' array.");

            foreach (var entry in data.bots)
            {
                if (string.IsNullOrWhiteSpace(entry.botName))
                {
                    Debug.LogWarning("[EcopolyDataImporter] Skipping bot personality without botName.");
                    continue;
                }

                var id = string.IsNullOrWhiteSpace(entry.botId) ? entry.botName : entry.botId;
                var assetPath = $"{GeneratedBotsRoot}/SO_BotPersonality_{SanitizeFileName(id)}.asset";
                var asset = CreateOrUpdateAsset<BotPersonalityData>(assetPath, so =>
                {
                    so.botName = entry.botName.Trim();
                    so.ecologicalAwareness = Mathf.Clamp01(entry.ecologicalAwareness);
                    so.riskTolerance = Mathf.Clamp01(entry.riskTolerance);
                    so.aggressiveness = Mathf.Clamp01(entry.aggressiveness);
                    so.cooperation = Mathf.Clamp01(entry.cooperation);
                    so.cepBuyThreshold = entry.cepBuyThreshold;
                    so.safeMoneyReserve = entry.safeMoneyReserve;
                    so.decisionDelay = entry.decisionDelay;
                });

                result[id] = asset;
            }

            return result;
        }

        private static BoardConfig ImportBoardConfig(
            Dictionary<string, PropertyData> propertyAssets,
            Dictionary<string, DistrictBuildingData> districtAssets)
        {
            var data = ReadJson<BoardImportFile>(BoardJsonPath);

            if (data.tiles == null)
                throw new InvalidDataException($"[EcopolyDataImporter] '{BoardJsonPath}' must contain 'tiles' array.");
            if (data.tiles.Count == 0)
                throw new InvalidDataException($"[EcopolyDataImporter] '{BoardJsonPath}' has an empty 'tiles' array.");

            var board = CreateOrUpdateAsset<BoardConfig>(ResourcesBoardAssetPath, so =>
            {
                so.tiles = data.tiles
                    .OrderBy(t => t.position)
                    .Select(t => new TileConfig
                    {
                        position = t.position,
                        tileType = ParseEnum(t.tileType, TileType.FreeParking),
                        propertyId = t.propertyId,
                        taxAmount = t.taxAmount,
                        displayName = t.displayName ?? string.Empty
                    })
                    .ToList();

                so.allProperties = propertyAssets.Values
                    .OrderBy(p => p.propertyId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                so.districtBuildings = districtAssets.Values
                    .OrderBy(d => d.buildingId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });

            if (board.tiles.Count != Constants.BOARD_SIZE)
            {
                Debug.LogWarning($"[EcopolyDataImporter] Board tile count is {board.tiles.Count}, expected {Constants.BOARD_SIZE}.");
            }

            return board;
        }

        private static List<ChanceCardData> ImportChanceCards()
        {
            var data = ReadJson<ChanceCardImportFile>(ChanceJsonPath);
            var imported = new List<ChanceCardData>();

            if (data.cards == null)
                throw new InvalidDataException($"[EcopolyDataImporter] '{ChanceJsonPath}' must contain 'cards' array.");
            if (data.cards.Count == 0)
                throw new InvalidDataException($"[EcopolyDataImporter] '{ChanceJsonPath}' has an empty 'cards' array.");

            foreach (var entry in data.cards)
            {
                if (string.IsNullOrWhiteSpace(entry.cardId))
                {
                    Debug.LogWarning("[EcopolyDataImporter] Skipping chance card without cardId.");
                    continue;
                }

                var assetPath = $"{ResourcesChanceRoot}/SO_ChanceCard_{SanitizeFileName(entry.cardId)}.asset";
                var asset = CreateOrUpdateAsset<ChanceCardData>(assetPath, so =>
                {
                    so.cardId = entry.cardId.Trim();
                    so.cardType = ParseEnum(entry.cardType, ChanceCardType.ReceiveMoney);
                    so.displayText = entry.displayText ?? string.Empty;

                    so.moneyAmount = entry.moneyAmount;
                    so.cepAmount = entry.cepAmount;
                    so.maxMoveDistance = entry.maxMoveDistance;
                    so.targetTilePosition = entry.targetTilePosition;
                    so.dilemmaCostPerPlayer = entry.dilemmaCostPerPlayer;
                    so.dilemmaCEPEffect = entry.dilemmaCEPEffect;
                    so.conditionalCEPThreshold = entry.conditionalCEPThreshold;
                    so.conditionalMoneyBelow = entry.conditionalMoneyBelow;
                    so.conditionalMoneyAbove = entry.conditionalMoneyAbove;
                });

                imported.Add(asset);
            }

            return imported;
        }

        private static List<EventCardData> ImportEventCards()
        {
            var data = ReadJson<EventCardImportFile>(EventJsonPath);
            var imported = new List<EventCardData>();

            if (data.cards == null)
                throw new InvalidDataException($"[EcopolyDataImporter] '{EventJsonPath}' must contain 'cards' array.");
            if (data.cards.Count == 0)
                throw new InvalidDataException($"[EcopolyDataImporter] '{EventJsonPath}' has an empty 'cards' array.");

            foreach (var entry in data.cards)
            {
                if (string.IsNullOrWhiteSpace(entry.cardId))
                {
                    Debug.LogWarning("[EcopolyDataImporter] Skipping event card without cardId.");
                    continue;
                }

                var assetPath = $"{ResourcesEventRoot}/SO_EventCard_{SanitizeFileName(entry.cardId)}.asset";
                var asset = CreateOrUpdateAsset<EventCardData>(assetPath, so =>
                {
                    so.cardId = entry.cardId.Trim();
                    so.disasterType = ParseEnum(entry.disasterType, DisasterType.Hurricane);
                    so.cardTitle = entry.cardTitle ?? entry.cardId;
                    so.fmodEventPath = entry.fmodEventPath ?? string.Empty;

                    var sourceEffects = entry.effectsByLevel ?? new List<DisasterLevelEffectImport>();
                    so.effectsByLevel = new DisasterLevelEffect[4];
                    for (int i = 0; i < so.effectsByLevel.Length; i++)
                    {
                        var src = i < sourceEffects.Count ? sourceEffects[i] : new DisasterLevelEffectImport();
                        so.effectsByLevel[i] = new DisasterLevelEffect
                        {
                            description = src.description ?? string.Empty,
                            moneyPerHouse = src.moneyPerHouse,
                            moneyFlat = src.moneyFlat,
                            levelReduction = src.levelReduction,
                            targetLevelCap = src.targetLevelCap,
                            affectsNeighbors = src.affectsNeighbors,
                            affectsAll = src.affectsAll,
                            isGlobalDisaster = src.isGlobalDisaster,
                            freeUpgradeReward = src.freeUpgradeReward,
                            freeUpgradeRequiredLevel = src.freeUpgradeRequiredLevel,
                        };
                    }
                });

                imported.Add(asset);
            }

            return imported;
        }

        private static void ValidateBoardReferences(
            BoardConfig board,
            Dictionary<string, PropertyData> properties)
        {
            var duplicates = board.tiles
                .GroupBy(t => t.position)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            if (duplicates.Length > 0)
                Debug.LogWarning($"[EcopolyDataImporter] Duplicate tile positions: {string.Join(", ", duplicates)}");

            foreach (var tile in board.tiles)
            {
                if (tile.tileType != TileType.Property && tile.tileType != TileType.Station) continue;
                if (string.IsNullOrWhiteSpace(tile.propertyId))
                {
                    Debug.LogWarning($"[EcopolyDataImporter] Tile {tile.position} ({tile.displayName}) is missing propertyId.");
                    continue;
                }

                if (!properties.ContainsKey(tile.propertyId))
                    Debug.LogWarning($"[EcopolyDataImporter] Tile {tile.position} references unknown propertyId '{tile.propertyId}'.");
            }
        }

        private static void ValidateChanceDeckSize(List<ChanceCardData> chanceCards)
        {
            if (chanceCards.Count != Constants.CHANCE_DECK_SIZE)
                Debug.LogWarning($"[EcopolyDataImporter] Chance deck has {chanceCards.Count} cards, expected {Constants.CHANCE_DECK_SIZE}.");

            ValidateChanceComposition(chanceCards);

            var duplicates = chanceCards
                .GroupBy(c => c.cardId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            if (duplicates.Length > 0)
                Debug.LogWarning($"[EcopolyDataImporter] Duplicate chance card ids: {string.Join(", ", duplicates)}");
        }

        private static void ValidateEventCards(List<EventCardData> eventCards)
        {
            var duplicates = eventCards
                .GroupBy(c => c.cardId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            if (duplicates.Length > 0)
                Debug.LogWarning($"[EcopolyDataImporter] Duplicate event card ids: {string.Join(", ", duplicates)}");

            foreach (var card in eventCards)
            {
                if (card.effectsByLevel == null || card.effectsByLevel.Length != 4)
                    Debug.LogWarning($"[EcopolyDataImporter] Event card '{card.cardId}' must have 4 intensity effects.");
            }
        }

        private static void ValidateChanceComposition(List<ChanceCardData> chanceCards)
        {
            var expectedCounts = new Dictionary<ChanceCardType, int>
            {
                { ChanceCardType.MoveVelo, 2 },
                { ChanceCardType.MoveCar, 2 },
                { ChanceCardType.MovePlane, 2 },
                { ChanceCardType.Tax, 3 },
                { ChanceCardType.Dilemma, 3 },
                { ChanceCardType.PersonalCEPUp, 2 },
                { ChanceCardType.PersonalCEPDown, 2 },
                { ChanceCardType.GoToJail, 2 },
                { ChanceCardType.BuildingDegraded, 2 },
                { ChanceCardType.MoveToTile, 3 },
                { ChanceCardType.GetOutOfJail, 2 },
                { ChanceCardType.GlobalCEPUp, 1 },
                { ChanceCardType.ReceiveMoney, 1 },
                { ChanceCardType.ConditionalMoney, 1 },
                { ChanceCardType.Reparations, 1 },
                { ChanceCardType.DistrictBuildingDestroyed, 1 },
            };

            var actualCounts = chanceCards
                .GroupBy(c => c.cardType)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var expected in expectedCounts)
            {
                actualCounts.TryGetValue(expected.Key, out int actual);
                if (actual != expected.Value)
                {
                    Debug.LogWarning($"[EcopolyDataImporter] Chance composition mismatch for {expected.Key}: expected {expected.Value}, got {actual}.");
                }
            }
        }

        private static void ValidateSourceFilesExist()
        {
            var requiredPaths = new[]
            {
                GameSettingsJsonPath,
                PropertiesJsonPath,
                DistrictJsonPath,
                BotsJsonPath,
                BoardJsonPath,
                ChanceJsonPath,
                EventJsonPath,
            };

            foreach (var path in requiredPaths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"[EcopolyDataImporter] Missing required file: {path}", path);
            }
        }

        private static T ReadJson<T>(string assetPath)
        {
            if (!File.Exists(assetPath))
                throw new FileNotFoundException($"[EcopolyDataImporter] Missing file: {assetPath}", assetPath);

            var json = File.ReadAllText(assetPath);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException($"[EcopolyDataImporter] JSON file is empty: {assetPath}");

            try
            {
                var parsed = JsonUtility.FromJson<T>(json);
                if (parsed == null)
                    throw new InvalidDataException($"[EcopolyDataImporter] Failed to parse JSON: {assetPath}");
                return parsed;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"[EcopolyDataImporter] Invalid JSON in {assetPath}.", ex);
            }
        }

        private static T CreateOrUpdateAsset<T>(string assetPath, Action<T> apply) where T : ScriptableObject
        {
            EnsureParentFolderExists(assetPath);

            if (NeedsAssetRecreation<T>(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.Refresh();
            }

            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            apply(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static bool NeedsAssetRecreation<T>(string assetPath) where T : ScriptableObject
        {
            if (!File.Exists(assetPath)) return false;

            var typedAsset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (typedAsset != null)
            {
                var typedSo = new SerializedObject(typedAsset);
                var typedScriptProp = typedSo.FindProperty("m_Script");
                if (typedScriptProp == null || typedScriptProp.objectReferenceValue == null)
                {
                    Debug.LogWarning($"[EcopolyDataImporter] Recreating broken asset (missing script): {assetPath}");
                    return true;
                }

                return false;
            }

            var genericAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (genericAsset == null)
            {
                Debug.LogWarning($"[EcopolyDataImporter] Recreating unreadable asset: {assetPath}");
                return true;
            }

            var genericSo = new SerializedObject(genericAsset);
            var genericScriptProp = genericSo.FindProperty("m_Script");
            if (genericScriptProp == null || genericScriptProp.objectReferenceValue == null)
            {
                Debug.LogWarning($"[EcopolyDataImporter] Recreating asset with null script reference: {assetPath}");
                return true;
            }

            Debug.LogWarning($"[EcopolyDataImporter] Recreating asset with unexpected type: {assetPath}");
            return true;
        }

        private static void EnsureParentFolderExists(string assetPath)
        {
            var parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
        }

        private static void SafeDeleteAsset(string assetPath)
        {
            if (File.Exists(assetPath))
                AssetDatabase.DeleteAsset(assetPath);
        }

        private static void SafeDeleteFolderContents(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath)) return;

            var guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == folderPath) continue;
                AssetDatabase.DeleteAsset(path);
            }
        }

        private static int[] NormalizeArray(int[] values, int expectedLength)
        {
            var normalized = new int[expectedLength];
            if (values == null) return normalized;

            for (int i = 0; i < expectedLength && i < values.Length; i++)
                normalized[i] = values[i];

            return normalized;
        }

        private static Color ParseColor(string htmlColor, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(htmlColor)) return fallback;
            return ColorUtility.TryParseHtmlString(htmlColor, out var color) ? color : fallback;
        }

        private static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct
        {
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out TEnum parsed))
                return parsed;
            return fallback;
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        }

        [Serializable]
        private class PropertyImportFile
        {
            public List<PropertyImportEntry> properties = new List<PropertyImportEntry>();
        }

        [Serializable]
        private class GameSettingsImportFile
        {
            public GameSettingsImportEntry settings = new GameSettingsImportEntry
            {
                startingMoney = 1500,
                voiceMaxDistance = 8f,
                voiceMinDistance = 1f,
                cameraSwitchBlend = 0.5f,
                pawnMoveStepDuration = 0.2f,
                disasterVFXDuration = 4f,
            };
        }

        [Serializable]
        private class GameSettingsImportEntry
        {
            public int startingMoney = 1500;
            public float voiceMaxDistance = 8f;
            public float voiceMinDistance = 1f;
            public float cameraSwitchBlend = 0.5f;
            public float pawnMoveStepDuration = 0.2f;
            public float disasterVFXDuration = 4f;
        }

        [Serializable]
        private class PropertyImportEntry
        {
            public string propertyId;
            public string displayName;
            public string groupId;
            public string groupColorHex;
            public int purchasePrice;
            public int baseRent;
            public int monopolyRent;
            public int cepOnPurchase;
            public int[] stableEmissionsPerLevel;
            public int[] renovationCosts;
            public int[] renovationCEPCosts;
        }

        [Serializable]
        private class DistrictBuildingImportFile
        {
            public List<DistrictBuildingImportEntry> districtBuildings = new List<DistrictBuildingImportEntry>();
        }

        [Serializable]
        private class DistrictBuildingImportEntry
        {
            public string buildingId;
            public string displayName;
            public string buildingType;
            public int cost;
            public int rentBonus;
            public int cepReductionPerTurn;
        }

        [Serializable]
        private class BotPersonalityImportFile
        {
            public List<BotPersonalityImportEntry> bots = new List<BotPersonalityImportEntry>();
        }

        [Serializable]
        private class BotPersonalityImportEntry
        {
            public string botId;
            public string botName;
            public float ecologicalAwareness;
            public float riskTolerance;
            public float aggressiveness;
            public float cooperation;
            public int cepBuyThreshold;
            public int safeMoneyReserve;
            public float decisionDelay;
        }

        [Serializable]
        private class BoardImportFile
        {
            public List<TileImportEntry> tiles = new List<TileImportEntry>();
        }

        [Serializable]
        private class TileImportEntry
        {
            public int position;
            public string tileType;
            public string propertyId;
            public int taxAmount;
            public string displayName;
        }

        [Serializable]
        private class ChanceCardImportFile
        {
            public List<ChanceCardImportEntry> cards = new List<ChanceCardImportEntry>();
        }

        [Serializable]
        private class ChanceCardImportEntry
        {
            public string cardId;
            public string cardType;
            public string displayText;
            public int moneyAmount;
            public int cepAmount;
            public int maxMoveDistance;
            public int targetTilePosition;
            public int dilemmaCostPerPlayer;
            public int dilemmaCEPEffect;
            public int conditionalCEPThreshold;
            public int conditionalMoneyBelow;
            public int conditionalMoneyAbove;
        }

        [Serializable]
        private class EventCardImportFile
        {
            public List<EventCardImportEntry> cards = new List<EventCardImportEntry>();
        }

        [Serializable]
        private class EventCardImportEntry
        {
            public string cardId;
            public string disasterType;
            public string cardTitle;
            public string vfxAddressableKey;
            public string fmodEventPath;
            public List<DisasterLevelEffectImport> effectsByLevel;
        }

        [Serializable]
        private class DisasterLevelEffectImport
        {
            public string description;
            public int moneyPerHouse;
            public int moneyFlat;
            public int levelReduction;
            public int targetLevelCap;
            public bool affectsNeighbors;
            public bool affectsAll;
            public bool isGlobalDisaster;
            public bool freeUpgradeReward;
            public int freeUpgradeRequiredLevel;
        }
    }
}
