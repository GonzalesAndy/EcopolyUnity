using UnityEngine;

namespace Ecopoly.Core
{
    /// <summary>Component attached to each board tile GameObject.</summary>
    public class BoardTile : MonoBehaviour
    {
        [field: SerializeField] public int Position { get; private set; }
        [SerializeField] private Transform _pawnAnchor; // anchor point for pawns
        [SerializeField] private Transform _buildingAnchor; // anchor point for buildings

        public Vector3 PawnAnchorPosition
            => _pawnAnchor != null ? _pawnAnchor.position : transform.position;
        public Vector3 BuildingAnchorPosition
            => _buildingAnchor != null ? _buildingAnchor.position : transform.position + Vector3.up;
    }
}
