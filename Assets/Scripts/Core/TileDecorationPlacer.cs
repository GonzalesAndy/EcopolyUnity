using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Ecopoly.Data;
using Ecopoly.Utils;

namespace Ecopoly.Core
{
    /// <summary>
    /// Automatically spawns decoration prefabs on each board tile at startup,
    /// positioning them at Anchor_Building (facing Anchor_Pawn) or Anchor_Pawn.
    /// Exposes an in-game IMGUI debug panel (visible in Play Mode) with per-type
    /// sliders for scale, Y-angle offset, and position offset.
    /// </summary>
    public class TileDecorationPlacer : MonoBehaviour
    {
        private const string AnchorBuildingName = "Anchor_Building";
        private const string AnchorPawnName = "Anchor_Pawn";
        private const string BoardConfigResourcePath = "Board/BoardConfig";

        [Header("References")]
        [Tooltip("Root transform that contains all Tile_XX GameObjects.")]
        [SerializeField] private Transform _tilesRoot;

        [Tooltip("Decoration settings ScriptableObject.")]
        [SerializeField] private TileDecorationSettings _settings;

        [Header("Debug Panel")]
        private const Key _debugToggleKey = Key.O;

        // Runtime: spawned decoration per tile position
        private readonly Dictionary<int, GameObject> _spawnedDecorations = new Dictionary<int, GameObject>();

        // Live config copies for debug sliders (mirrors _settings.decorations at runtime)
        private DecorationConfig[] _liveConfigs;

        private BoardConfig _boardConfig;

        // --- IMGUI debug panel state
        private bool _debugPanelOpen = false;
        private Vector2 _scrollPos;
        private Rect _windowRect = new Rect(10f, 10f, 340f, 460f);
        private int _selectedTypeIndex = 0;

        // --- Unity Lifecycle
        private void Awake()
        {
            _boardConfig = Resources.Load<BoardConfig>(BoardConfigResourcePath);
            if (_boardConfig == null)
                Debug.LogError("[TileDecorationPlacer] Could not load BoardConfig from Resources.");
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.PropertyPurchased, OnPropertyPurchased);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.PropertyPurchased, OnPropertyPurchased);
        }

        private void Start()
        {
            if (_settings == null)
            {
                Debug.LogError("[TileDecorationPlacer] TileDecorationSettings not assigned.");
                return;
            }

            // Deep-copy configs so runtime slider edits don't mutate the SO asset
            _liveConfigs = DeepCopyConfigs(_settings.decorations);

            PlaceAllDecorations();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_debugToggleKey].wasPressedThisFrame)
                _debugPanelOpen = !_debugPanelOpen;
        }

        private void OnGUI()
        {
            if (!_debugPanelOpen) return;

            _windowRect = GUI.Window(
                id: 9901,
                clientRect: _windowRect,
                func: DrawDebugWindow,
                text: $"Tile Decorations – Debug  [{_debugToggleKey} to close]"
            );
        }

        // --- Event Handlers
        private void OnPropertyPurchased(object payload)
        {
            if (!(payload is PropertyEventPayload purchase)) return;
            HideDecorationForProperty(purchase.PropertyId);
        }

        /// <summary>Deactivates the decoration on the tile that owns the given propertyId.</summary>
        private void HideDecorationForProperty(string propertyId)
        {
            if (_boardConfig == null || string.IsNullOrEmpty(propertyId)) return;

            foreach (var tileConfig in _boardConfig.tiles)
            {
                if (tileConfig.propertyId != propertyId) continue;

                if (_spawnedDecorations.TryGetValue(tileConfig.position, out GameObject decoration) && decoration != null)
                    decoration.SetActive(false);

                return;
            }

            Debug.LogWarning($"[TileDecorationPlacer] No tile found for propertyId '{propertyId}'.");
        }

        // --- Placement
        /// <summary>Destroys all existing decoration instances and re-spawns them.</summary>
        private void PlaceAllDecorations()
        {
            DestroyAllDecorations();

            if (_tilesRoot == null || _boardConfig == null || _liveConfigs == null) return;

            foreach (var tileConfig in _boardConfig.tiles)
            {
                DecorationConfig decConfig = GetLiveConfig(tileConfig.tileType);
                if (decConfig == null || decConfig.prefab == null) continue;

                string tileName = $"Tile_{tileConfig.position:D2}";
                Transform tileTransform = _tilesRoot.Find(tileName);
                if (tileTransform == null)
                {
                    Debug.LogWarning($"[TileDecorationPlacer] Could not find child '{tileName}' under Tiles_Root.");
                    continue;
                }

                Transform anchorBuilding = tileTransform.Find(AnchorBuildingName);
                Transform anchorPawn = tileTransform.Find(AnchorPawnName);

                if (anchorBuilding == null || anchorPawn == null)
                {
                    Debug.LogWarning($"[TileDecorationPlacer] '{tileName}' is missing Anchor_Building or Anchor_Pawn.");
                    continue;
                }

                SpawnDecoration(tileConfig.position, decConfig, anchorBuilding, anchorPawn);
            }
        }

        private void SpawnDecoration(int position, DecorationConfig config, Transform anchorBuilding, Transform anchorPawn)
        {
            Transform spawnAnchor = config.useAnchorPawn ? anchorPawn : anchorBuilding;

            // Capture the prefab's original transforms before instantiation so we can
            // use them as bases and treat config values as multipliers/offsets, not replacements.
            Vector3 prefabBaseScale = config.prefab.transform.localScale;
            Quaternion prefabBaseRotation = config.prefab.transform.localRotation;

            GameObject instance = Instantiate(config.prefab, spawnAnchor.position, Quaternion.identity, spawnAnchor);
            instance.name = $"{config.prefab.name}_Tile{position:D2}";

            // Orientation: face Anchor_Pawn from Anchor_Building (or identity for pawn anchor).
            // Composition order (right-to-left): prefab base → y offset → look-at.
            // This preserves the artist's baked axes while still aiming the model correctly.
            Quaternion yOffsetRot = Quaternion.Euler(0f, config.yAngleOffset, 0f);
            if (!config.useAnchorPawn)
            {
                Vector3 towardsPawn = anchorPawn.position - anchorBuilding.position;
                towardsPawn.y = 0f; // keep upright
                if (towardsPawn.sqrMagnitude > 0.0001f)
                {
                    Quaternion lookRot = Quaternion.LookRotation(towardsPawn, Vector3.up);
                    instance.transform.rotation = lookRot * yOffsetRot * prefabBaseRotation;
                }
                else
                {
                    instance.transform.localRotation = yOffsetRot * prefabBaseRotation;
                }
            }
            else
            {
                instance.transform.localRotation = yOffsetRot * prefabBaseRotation;
            }

            // Position offset (local to the anchor)
            instance.transform.position += spawnAnchor.TransformDirection(config.positionOffset);

            // Scale: multiply the prefab's baked-in scale by the config factor so we
            // never discard what the artist set on the original asset.
            instance.transform.localScale = prefabBaseScale * config.scale;

            _spawnedDecorations[position] = instance;
        }

        private void DestroyAllDecorations()
        {
            foreach (var kvp in _spawnedDecorations)
                if (kvp.Value != null)
                    Destroy(kvp.Value);
            _spawnedDecorations.Clear();
        }

        // --- Live Config Helpers
        private DecorationConfig GetLiveConfig(TileType type)
        {
            if (_liveConfigs == null) return null;
            foreach (var c in _liveConfigs)
                if (c.tileType == type) return c;
            return null;
        }

        private static DecorationConfig[] DeepCopyConfigs(DecorationConfig[] source)
        {
            if (source == null) return null;
            var copy = new DecorationConfig[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var src = source[i];
                copy[i] = new DecorationConfig
                {
                    tileType = src.tileType,
                    prefab = src.prefab,
                    scale = src.scale,
                    yAngleOffset = src.yAngleOffset,
                    positionOffset = src.positionOffset,
                    useAnchorPawn = src.useAnchorPawn,
                };
            }
            return copy;
        }

        // --- IMGUI Debug Window
        private void DrawDebugWindow(int windowId)
        {
            if (_liveConfigs == null || _liveConfigs.Length == 0)
            {
                GUILayout.Label("No decoration configs loaded.");
                GUI.DragWindow();
                return;
            }

            // Type selector tabs
            string[] typeNames = new string[_liveConfigs.Length];
            for (int i = 0; i < _liveConfigs.Length; i++)
                typeNames[i] = _liveConfigs[i].tileType.ToString();
            _selectedTypeIndex = GUILayout.SelectionGrid(_selectedTypeIndex, typeNames, 2);

            GUILayout.Space(6f);

            if (_selectedTypeIndex < 0 || _selectedTypeIndex >= _liveConfigs.Length)
            {
                GUI.DragWindow();
                return;
            }

            DecorationConfig cfg = _liveConfigs[_selectedTypeIndex];

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            // Scale (multiplier on top of the prefab's baked-in scale)
            GUILayout.Label($"Scale Multiplier: {cfg.scale:F2}x");
            cfg.scale = GUILayout.HorizontalSlider(cfg.scale, 0.01f, 5f);

            GUILayout.Space(4f);

            // Y angle offset
            GUILayout.Label($"Y Angle Offset: {cfg.yAngleOffset:F1}°");
            cfg.yAngleOffset = GUILayout.HorizontalSlider(cfg.yAngleOffset, -180f, 180f);

            GUILayout.Space(4f);

            // Position offset X
            GUILayout.Label($"Offset X: {cfg.positionOffset.x:F3}");
            cfg.positionOffset.x = GUILayout.HorizontalSlider(cfg.positionOffset.x, -2f, 2f);

            // Position offset Y
            GUILayout.Label($"Offset Y: {cfg.positionOffset.y:F3}");
            cfg.positionOffset.y = GUILayout.HorizontalSlider(cfg.positionOffset.y, -2f, 2f);

            // Position offset Z
            GUILayout.Label($"Offset Z: {cfg.positionOffset.z:F3}");
            cfg.positionOffset.z = GUILayout.HorizontalSlider(cfg.positionOffset.z, -2f, 2f);

            GUILayout.EndScrollView();

            GUILayout.Space(6f);

            if (GUILayout.Button("Re-apply All Decorations"))
                PlaceAllDecorations();

            GUILayout.Space(2f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Config to Console"))
                LogCurrentConfig(cfg);
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        /// <summary>Logs the current config values to Unity Console so they can be copied back to the SO.</summary>
        private void LogCurrentConfig(DecorationConfig cfg)
        {
            Debug.Log(
                $"[TileDecorationPlacer] {cfg.tileType} config:\n" +
                $"  scale={cfg.scale:F4}\n" +
                $"  yAngleOffset={cfg.yAngleOffset:F4}\n" +
                $"  positionOffset=({cfg.positionOffset.x:F4}, {cfg.positionOffset.y:F4}, {cfg.positionOffset.z:F4})"
            );
        }
    }
}

