using UnityEngine;
using DistantLands.Cozy.Data;

namespace Ecopoly.Data
{
    public enum DisasterType
    {
        Hurricane, Drought, Wildfire, Lightning
    }

    public enum DisasterSpawnTarget
    {
        None,
        LakeCenter,
        TriggeringPlayer
    }

    [System.Serializable]
    public class DisasterLevelEffect
    {
        [TextArea(2, 3)]
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

        [Header("Primary VFX (per level)")]
        [Tooltip("Prefab instantiated at spawnTarget position. Leave null for levels with no prefab.")]
        public GameObject vfxPrefab;

        [Tooltip("Where to spawn the primary VFX prefab for this level.")]
        public DisasterSpawnTarget spawnTarget = DisasterSpawnTarget.LakeCenter;

        [Header("Secondary VFX (optional — e.g. fire ring at player + tornado at lake)")]
        [Tooltip("Optional second prefab spawned at secondSpawnTarget. Leave null to skip.")]
        public GameObject secondVfxPrefab;

        [Tooltip("Where to spawn the secondary VFX prefab for this level.")]
        public DisasterSpawnTarget secondSpawnTarget = DisasterSpawnTarget.TriggeringPlayer;

        [Header("Weather per level")]
        [Tooltip("CozyWeather WeatherProfile to activate during this disaster. Leave null to skip weather change.")]
        public WeatherProfile weatherProfile;

        [Tooltip("Duration in seconds to hold the disaster weather before reverting.")]
        public float weatherDuration = 15f;
    }

    [CreateAssetMenu(fileName = "SO_EventCard_", menuName = "Ecopoly/EventCard")]
    public class EventCardData : ScriptableObject
    {
        public string cardId;
        public DisasterType disasterType;
        [TextArea(1, 2)]
        public string cardTitle;
        public Sprite illustration;

        [Tooltip("Effects by intensity level [0]=L1 ... [3]=L4")]
        public DisasterLevelEffect[] effectsByLevel = new DisasterLevelEffect[4];

        [Header("Audio")]
        public string fmodEventPath;
    }
}
