using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace Ecopoly.Core
{
    /// <summary>
    /// Distributes all Tile_XX GameObjects evenly along a SplineContainer.
    /// Tiles are ordered by their BoardTile.Position index (0 → N-1).
    /// Placement can be triggered in Edit Mode via the context menu or
    /// automatically in Play Mode (configurable).
    ///
    /// Each tile is positioned at its normalized spline parameter t = i / tileCount
    /// and oriented so that its forward axis follows the spline tangent.
    /// An optional upright mode keeps tiles world-upright (good for flat boards).
    /// </summary>
    [ExecuteInEditMode]
    public class TileSplinePlacer : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The SplineContainer that defines the board path.")]
        [SerializeField] private SplineContainer _splineContainer;

        [Tooltip("Root transform that contains all Tile_XX GameObjects.")]
        [SerializeField] private Transform _tilesRoot;

        [Header("Orientation")]
        [Tooltip("Keep tiles world-upright (Y = Vector3.up). " +
                 "Disable to align tiles to the spline's natural up vector.")]
        [SerializeField] private bool _keepUpright = true;

        [Tooltip("Additional Y rotation offset applied to every tile after spline alignment. " +
                 "Useful to correct the tile's forward axis if the mesh faces a different direction.")]
        [SerializeField] private float _forwardYOffset = 0f;

        [Header("Placement")]
        [Tooltip("Offset applied in the tile's local space after spline placement.")]
        [SerializeField] private Vector3 _localPositionOffset = Vector3.zero;

        [Tooltip("When true, tiles are placed automatically on Play Mode entry.")]
        [SerializeField] private bool _placeOnStart = false;

        // --- Unity Lifecycle
        private void Start()
        {
            if (!Application.isPlaying) return;
            if (_placeOnStart)
                PlaceTilesAlongSpline();
        }

        // --- Public API
        /// <summary>
        /// Collects all tiles under <see cref="_tilesRoot"/>, sorts them by
        /// <see cref="BoardTile.Position"/>, then places them evenly along the spline.
        /// Safe to call from Edit Mode via the context menu or from code at runtime.
        /// </summary>
        [ContextMenu("Place Tiles Along Spline")]
        public void PlaceTilesAlongSpline()
        {
            if (_splineContainer == null)
            {
                Debug.LogError("[TileSplinePlacer] SplineContainer is not assigned.", this);
                return;
            }

            if (_tilesRoot == null)
            {
                Debug.LogError("[TileSplinePlacer] Tiles Root is not assigned.", this);
                return;
            }

            List<BoardTile> tiles = CollectSortedTiles();
            if (tiles.Count == 0)
            {
                Debug.LogWarning("[TileSplinePlacer] No BoardTile components found under Tiles Root.", this);
                return;
            }

            Spline spline = _splineContainer.Spline;
            Matrix4x4 splineToWorld = _splineContainer.transform.localToWorldMatrix;
            int count = tiles.Count;

            for (int i = 0; i < count; i++)
            {
                // Evenly-spaced normalized t (0..1, closed loop)
                float t = (float)i / count;

                // Evaluate in spline local space, then convert to world space
                float3 localPos = SplineUtility.EvaluatePosition(spline, t);
                float3 localTan = SplineUtility.EvaluateTangent(spline, t);
                float3 localUp  = SplineUtility.EvaluateUpVector(spline, t);

                Vector3 worldPos = splineToWorld.MultiplyPoint3x4(localPos);
                Vector3 worldTan = splineToWorld.MultiplyVector(localTan).normalized;
                Vector3 worldUp  = _keepUpright
                    ? Vector3.up
                    : ((Vector3)splineToWorld.MultiplyVector(localUp)).normalized;

                // Guard degenerate tangent (stationary knot)
                if (worldTan.sqrMagnitude < 0.0001f)
                    worldTan = Vector3.forward;

                Quaternion splineRot = Quaternion.LookRotation(worldTan, worldUp);
                Quaternion finalRot  = splineRot * Quaternion.Euler(0f, _forwardYOffset, 0f);

                Transform tileTf = tiles[i].transform;
                tileTf.position = worldPos + finalRot * _localPositionOffset;
                tileTf.rotation = finalRot;
            }

            Debug.Log($"[TileSplinePlacer] Placed {count} tiles along spline.", this);
        }

        // --- Helpers
        /// <summary>
        /// Returns all <see cref="BoardTile"/> components under <see cref="_tilesRoot"/>,
        /// sorted ascending by their <see cref="BoardTile.Position"/>.
        /// </summary>
        private List<BoardTile> CollectSortedTiles()
        {
            var result = new List<BoardTile>(
                _tilesRoot.GetComponentsInChildren<BoardTile>(includeInactive: true));
            result.Sort((a, b) => a.Position.CompareTo(b.Position));
            return result;
        }
    }
}

