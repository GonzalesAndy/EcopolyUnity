using UnityEngine;
using Ecopoly.Data;

namespace Ecopoly.Data
{
    /// <summary>
    /// Per-type decoration configuration for a specific tile category.
    /// </summary>
    [System.Serializable]
    public class DecorationConfig
    {
        [Tooltip("Which tile type this config applies to.")]
        public TileType tileType;

        [Tooltip("Prefab to instantiate on this tile type.")]
        public GameObject prefab;

        [Tooltip("Uniform scale applied to the spawned prefab.")]
        public float scale = 1f;

        [Tooltip("Additional Y-axis rotation offset in degrees applied on top of the look-at rotation.")]
        public float yAngleOffset = 0f;

        [Tooltip("Local position offset applied after the anchor placement.")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("When true, places the prefab at Anchor_Pawn instead of Anchor_Building (ignores facing logic).")]
        public bool useAnchorPawn = false;
    }

    /// <summary>
    /// ScriptableObject that holds decoration configs for all relevant tile types.
    /// Create via Assets → Create → Ecopoly → TileDecorationSettings.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_TileDecorationSettings", menuName = "Ecopoly/TileDecorationSettings")]
    public class TileDecorationSettings : ScriptableObject
    {
        [Tooltip("One entry per tile type that should receive a decoration prefab.")]
        public DecorationConfig[] decorations = new DecorationConfig[0];

        /// <summary>Returns the config for a given tile type, or null if none is defined.</summary>
        public DecorationConfig GetConfig(TileType type)
        {
            foreach (var config in decorations)
                if (config.tileType == type)
                    return config;
            return null;
        }
    }
}
