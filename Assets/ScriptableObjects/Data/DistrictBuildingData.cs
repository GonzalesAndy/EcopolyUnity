using UnityEngine;

namespace Ecopoly.Data
{
    public enum DistrictBuildingType { Commercial, Ecological }

    [CreateAssetMenu(fileName = "SO_DistrictBuildingData_", menuName = "Ecopoly/DistrictBuildingData")]
    public class DistrictBuildingData : ScriptableObject
    {
        public string buildingId;
        public string displayName;
        public DistrictBuildingType buildingType;
        public int cost;
        [Tooltip("Commercial: fixed rent bonus per property in district")]
        public int rentBonus;
        [Tooltip("Ecological: stable CEP reduction per turn across district")]
        public int cepReductionPerTurn;
        public GameObject prefab;
    }
}
