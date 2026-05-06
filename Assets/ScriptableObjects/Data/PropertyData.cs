using UnityEngine;

namespace Ecopoly.Data
{
    [CreateAssetMenu(fileName = "SO_PropertyData_", menuName = "Ecopoly/PropertyData")]
    public class PropertyData : ScriptableObject
    {
        [Header("Identite")]
        public string propertyId;
        public string displayName;
        public Color groupColor;
        public string groupId;

        [Header("Finances")]
        public int purchasePrice;
        public int baseRent;
        [Tooltip("Rent if player owns full monopoly without district building")]
        public int monopolyRent;

        [Header("CEP")]
        public int cepOnPurchase;
        [Tooltip("Stable CEP emitted per turn by renovation level [0]=lvl1, [3]=lvl4")]
        public int[] stableEmissionsPerLevel = new int[4];

        [Header("Renovation")]
        [Tooltip("Money cost per renovation [0]=lvl1->2, [1]=lvl2->3, [2]=lvl3->4")]
        public int[] renovationCosts = new int[3];
        [Tooltip("CEP added during each renovation")]
        public int[] renovationCEPCosts = new int[3];

        [Header("Visual")]
        public GameObject tilePrefab;
        public GameObject[] houseModelsByLevel;
    }
}
