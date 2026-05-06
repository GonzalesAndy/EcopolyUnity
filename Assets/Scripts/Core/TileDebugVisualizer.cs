using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Ecopoly.Data;

namespace Ecopoly.Core
{
    /// <summary>
    /// Styles each tile's PRF_DebugCube as a Monopoly-inspired card:
    ///   - Lower band (~25 % of height) filled with the tile's group color.
    ///   - Upper body (~75 % of height) in white, with a TextMeshPro label on top.
    /// Runs once on Start; no runtime overhead afterwards.
    /// </summary>
    public class TileDebugVisualizer : MonoBehaviour
    {
        internal const string DebugCubeName      = "PRF_DebugCube";
        internal const string AnchorBuildingName = "Anchor_Building";
        internal const string ColorBandChildName = "ColorBand";
        internal const string WhiteBodyChildName = "WhiteBody";
        internal const string LabelChildName     = "Label";
        internal const string BoardConfigPath    = "Board/BoardConfig";
        private const float  ColorBandFraction       = 0.25f;
        private const float  LabelFontSize           = 1f;   // TMP font size in world-space units
        private const float  LabelHeightAboveTop     = 0.01f;

        // --- Colors for non-property tile types
        private static readonly Color ColorGo          = new Color(1.00f, 0.85f, 0.00f); // gold
        private static readonly Color ColorJail        = new Color(0.80f, 0.80f, 0.80f); // light grey
        private static readonly Color ColorGoToJail    = new Color(1.00f, 0.45f, 0.00f); // orange-red
        private static readonly Color ColorFreeParking = new Color(0.20f, 0.75f, 0.20f); // green
        private static readonly Color ColorChance      = new Color(0.94f, 0.55f, 0.14f); // amber
        private static readonly Color ColorEvent       = new Color(0.20f, 0.55f, 0.90f); // blue
        private static readonly Color ColorTax         = new Color(0.60f, 0.10f, 0.10f); // dark red
        private static readonly Color ColorStation     = new Color(0.20f, 0.20f, 0.20f); // near-black
        private static readonly Color ColorWhite       = Color.white;

        [Header("References")]
        [Tooltip("Root transform that contains all Tile_XX GameObjects.")]
        [SerializeField] private Transform _tilesRoot;

        private BoardConfig _boardConfig;

        // Fast propertyId → PropertyData lookup built in Awake
        private Dictionary<string, PropertyData> _propertyLookup = new();

        private void Awake()  => LoadConfig();

        private void Start()
        {
            if (_tilesRoot == null || _boardConfig == null) return;
            ApplyToAllTiles();
        }

        // --- Public API (editor button + runtime)
        /// <summary>
        /// Loads config and bakes all tile visuals. Safe in both Edit and Play Mode.
        /// Called by TileDebugVisualizerEditor's "Bake" button.
        /// </summary>
        public void Bake()
        {
            LoadConfig();
            if (_boardConfig == null)
            {
                Debug.LogError("[TileDebugVisualizer] Could not load BoardConfig from Resources.");
                return;
            }
            if (_tilesRoot == null)
            {
                Debug.LogError("[TileDebugVisualizer] _tilesRoot is not assigned.");
                return;
            }
            ApplyToAllTiles();
        }

        /// <summary>
        /// Removes all baked children and re-enables the original PRF_DebugCube renderers.
        /// Called by TileDebugVisualizerEditor's "Clear" button.
        /// </summary>
        public void Clear()
        {
            if (_tilesRoot == null) return;
            foreach (Transform tile in _tilesRoot)
            {
                Transform debugCube = tile.Find(DebugCubeName);
                if (debugCube != null)
                {
                    SmartDestroyNamedChild(debugCube, ColorBandChildName);
                    SmartDestroyNamedChild(debugCube, WhiteBodyChildName);
                    var r = debugCube.GetComponent<MeshRenderer>();
                    if (r != null) r.enabled = true;
                }
                SmartDestroyNamedChild(tile, LabelChildName);
            }
        }

        /// <summary>
        /// Bakes visuals with real instanced materials, then removes this component
        /// so the scene is self-contained and needs no TileDebugVisualizer at runtime.
        /// Only callable from the editor; the component is gone after this returns.
        /// </summary>
        public void BakePermanentAndRemove()
        {
            Bake();
#if UNITY_EDITOR
            DestroyImmediate(this);
#endif
        }

        // --- Config loading
        private void LoadConfig()
        {
            _boardConfig = Resources.Load<BoardConfig>(BoardConfigPath);
            if (_boardConfig == null) return;
            _propertyLookup.Clear();
            foreach (var prop in _boardConfig.allProperties)
                if (prop != null)
                    _propertyLookup[prop.propertyId] = prop;
        }

        // --- Main pass
        /// <summary>Iterates every TileConfig and styles its debug cube.</summary>
        private void ApplyToAllTiles()
        {
            foreach (var tileConfig in _boardConfig.tiles)
            {
                string tileName = $"Tile_{tileConfig.position:D2}";
                Transform tile = _tilesRoot.Find(tileName);
                if (tile == null)
                {
                    Debug.LogWarning($"[TileDebugVisualizer] '{tileName}' not found under Tiles_Root.");
                    continue;
                }

                Transform debugCube = tile.Find(DebugCubeName);
                if (debugCube == null) continue;

                Transform anchorBuilding = tile.Find(AnchorBuildingName);

                (Color bandColor, string label, bool isProperty) = ResolveVisuals(tileConfig);
                StylizeCube(debugCube, tile, anchorBuilding, bandColor, label, isProperty);
            }
        }

        // --- Visual resolution
        /// <summary>
        /// Returns the band color, display label, and whether the tile is a property.
        /// Property tiles get the band+white split; all other tile types are colored entirely.
        /// </summary>
        private (Color color, string label, bool isProperty) ResolveVisuals(TileConfig tile)
        {
            switch (tile.tileType)
            {
                case TileType.Property:
                case TileType.Station:
                    if (!string.IsNullOrEmpty(tile.propertyId) &&
                        _propertyLookup.TryGetValue(tile.propertyId, out var prop))
                        return (prop.groupColor, prop.displayName, true);
                    return (ColorWhite, tile.displayName, true);

                case TileType.Go:          return (ColorGo,          tile.displayName,                          false);
                case TileType.Jail:        return (ColorJail,         tile.displayName,                          false);
                case TileType.GoToJail:    return (ColorGoToJail,     tile.displayName,                          false);
                case TileType.FreeParking: return (ColorFreeParking,  tile.displayName,                          false);
                case TileType.Chance:      return (ColorChance,       tile.displayName,                          false);
                case TileType.Event:       return (ColorEvent,        tile.displayName,                          false);
                case TileType.Tax:         return (ColorTax,          $"{tile.displayName}\nM{tile.taxAmount}",  false);
                default:                   return (ColorWhite,        tile.displayName,                          false);
            }
        }

        // --- Cube styling
        /// <summary>
        /// Styles the PRF_DebugCube based on whether the tile is a property or not.
        ///
        /// Property tiles  → band + white body split along the dominant axis of Anchor_Building.
        ///                   The color band is always placed on the Anchor_Building side.
        /// Non-property tiles → entire cube filled with the tile color.
        ///
        /// A TMP label is always placed above the white body (or the full cube for
        /// non-property tiles), parented to the tile to avoid non-uniform scale issues.
        /// </summary>
        private void StylizeCube(Transform root, Transform tile, Transform anchorBuilding,
                                 Color bandColor, string label, bool isProperty)
        {
            var originalRenderer = root.GetComponent<MeshRenderer>();
            Material baseMaterial = originalRenderer != null ? originalRenderer.sharedMaterial : null;
            if (originalRenderer != null) originalRenderer.enabled = false;

            SmartDestroyNamedChild(root, ColorBandChildName);
            SmartDestroyNamedChild(root, WhiteBodyChildName);
            SmartDestroyNamedChild(tile, LabelChildName);

            // --- Determine the dominant axis of Anchor_Building
            // Different board sides use X or Z for the building offset.
            // We find which axis has the larger absolute magnitude and split along it.
            Vector3 anchorLocal = anchorBuilding != null
                ? anchorBuilding.localPosition
                : new Vector3(0f, 0f, -0.306f);

            bool splitOnX = Mathf.Abs(anchorLocal.x) > Mathf.Abs(anchorLocal.z);
            float anchorOffset = splitOnX ? anchorLocal.x : anchorLocal.z;
            float bandSign     = anchorOffset < 0f ? -1f : 1f;

            Vector3 labelCenterLocal;
            Vector2 labelSize;

            if (isProperty)
            {
                float bandF = ColorBandFraction;      // 0.25
                float bodyF = 1f - ColorBandFraction; // 0.75

                // Pivot positions along the dominant axis.
                float bandCenter = bandSign * (0.5f - bandF * 0.5f);
                float bodyCenter = -bandSign * (0.5f - bodyF * 0.5f);

                // --- Color band
                var band = CreateChildCube(root, ColorBandChildName, baseMaterial);
                band.localScale    = splitOnX
                    ? new Vector3(bandF, 1f, 1f)
                    : new Vector3(1f, 1f, bandF);
                band.localPosition = splitOnX
                    ? new Vector3(bandCenter, 0f, 0f)
                    : new Vector3(0f, 0f, bandCenter);
                SetRendererColor(band.GetComponent<MeshRenderer>(), bandColor);

                // --- White body
                var body = CreateChildCube(root, WhiteBodyChildName, baseMaterial);
                body.localScale    = splitOnX
                    ? new Vector3(bodyF, 1f, 1f)
                    : new Vector3(1f, 1f, bodyF);
                body.localPosition = splitOnX
                    ? new Vector3(bodyCenter, 0f, 0f)
                    : new Vector3(0f, 0f, bodyCenter);
                SetRendererColor(body.GetComponent<MeshRenderer>(), ColorWhite);

                // Label sits above the center of the white body.
                labelCenterLocal = splitOnX
                    ? new Vector3(bodyCenter, 0.5f, 0f)
                    : new Vector3(0f, 0.5f, bodyCenter);

                float worldBody = splitOnX
                    ? Mathf.Abs(root.lossyScale.x) * bodyF
                    : Mathf.Abs(root.lossyScale.z) * bodyF;
                float worldCross = splitOnX
                    ? Mathf.Abs(root.lossyScale.z)
                    : Mathf.Abs(root.lossyScale.x);
                labelSize = new Vector2(worldCross, worldBody);
            }
            else
            {
                // --- Full-color tile
                var full = CreateChildCube(root, ColorBandChildName, baseMaterial);
                full.localScale    = Vector3.one;
                full.localPosition = Vector3.zero;
                SetRendererColor(full.GetComponent<MeshRenderer>(), bandColor);

                // Label centered on the full tile.
                labelCenterLocal = new Vector3(0f, 0.5f, 0f);
                labelSize = new Vector2(
                    Mathf.Abs(root.lossyScale.x),
                    Mathf.Abs(root.lossyScale.z));
            }

            // --- TMP label
            Vector3 labelWorldPos = root.TransformPoint(labelCenterLocal) + Vector3.up * LabelHeightAboveTop;

            var labelGO = new GameObject(LabelChildName);
            labelGO.layer = LayerMask.NameToLayer("Board");
            labelGO.transform.SetParent(tile, worldPositionStays: false);
            labelGO.transform.position = labelWorldPos;

            // LookRotation(Vector3.up, labelUp):
            //   local Z → world +Y  → label is perfectly flat (X = 90° exact, no float drift).
            //   local Y → toward Anchor_Building in XZ → text top faces the outer board edge.
            // Zeroing Y before normalizing removes tilt from any accumulated tile rotation.
            Vector3 anchorLocalXZ       = new Vector3(anchorLocal.x, 0f, anchorLocal.z);
            Vector3 towardBuildingLocal = anchorLocalXZ.sqrMagnitude > 0.0001f
                ? anchorLocalXZ.normalized
                : -Vector3.forward;
            Vector3 towardBuildingWorld = tile.TransformDirection(towardBuildingLocal);
            towardBuildingWorld.y       = 0f;
            towardBuildingWorld         = towardBuildingWorld.sqrMagnitude > 0.0001f
                ? towardBuildingWorld.normalized
                : Vector3.forward;
            labelGO.transform.rotation  = Quaternion.LookRotation(Vector3.up, towardBuildingWorld);

            var tmp = labelGO.AddComponent<TextMeshPro>();
            tmp.text               = label;
            tmp.fontSize           = LabelFontSize;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.color              = isProperty ? Color.black : Color.white;
            tmp.enableWordWrapping = true;
            tmp.rectTransform.sizeDelta = labelSize;
        }

        // --- Helpers
        private static Transform CreateChildCube(Transform parent, string childName, Material baseMaterial)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = childName;
            go.layer = LayerMask.NameToLayer("Board");
            go.transform.SetParent(parent, false);

            // Remove the collider added by CreatePrimitive — the parent already has one
            var col = go.GetComponent<Collider>();
            if (col != null) SmartDestroy(col);

            // Reuse the base material so both sub-cubes use the same shader
            if (baseMaterial != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = baseMaterial;

            return go.transform;
        }

        /// <summary>
        /// Creates a real instanced material with the baked color so it serializes
        /// into the scene and survives without this component present at runtime.
        /// </summary>
        private static void SetRendererColor(MeshRenderer renderer, Color color)
        {
            if (renderer == null) return;
            // Clone the shared material so colors are fully serialized into the scene.
            var mat = renderer.sharedMaterial != null
                ? new Material(renderer.sharedMaterial)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            // _BaseColor is the URP property; set both for broadest shader support.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            renderer.sharedMaterial = mat;
        }

        private static void SmartDestroyNamedChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child != null) SmartDestroy(child.gameObject);
        }

        /// <summary>Uses DestroyImmediate in Edit Mode, Destroy in Play Mode.</summary>
        private static void SmartDestroy(Object obj)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(obj);
                return;
            }
#endif
            Destroy(obj);
        }
    }
}

