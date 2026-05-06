using System.Collections.Generic;
using UnityEngine;

namespace Ecopoly.Data
{
    [System.Serializable]
    public class TileConfig
    {
        public int position;
        public TileType tileType;
        public string propertyId;
        public int taxAmount;
        public string displayName;
    }

    public enum TileType
    {
        Property, Station, Go, Jail, GoToJail,
        Chance, Event, Tax, FreeParking
    }

    [CreateAssetMenu(fileName = "SO_BoardConfig", menuName = "Ecopoly/BoardConfig")]
    public class BoardConfig : ScriptableObject
    {
        public List<TileConfig> tiles = new List<TileConfig>(40);
        public List<PropertyData> allProperties;
        public List<DistrictBuildingData> districtBuildings;
    }
}
