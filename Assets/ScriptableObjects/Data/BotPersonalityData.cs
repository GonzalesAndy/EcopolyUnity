using UnityEngine;

namespace Ecopoly.Data
{
    [CreateAssetMenu(fileName = "SO_BotPersonality_", menuName = "Ecopoly/BotPersonality")]
    public class BotPersonalityData : ScriptableObject
    {
        public string botName;
        public Sprite avatar;

        [Header("Traits (0=min, 1=max)")]
        [Range(0f, 1f)] public float ecologicalAwareness = 0.5f;
        [Range(0f, 1f)] public float riskTolerance = 0.5f;
        [Range(0f, 1f)] public float aggressiveness = 0.5f;
        [Range(0f, 1f)] public float cooperation = 0.5f;

        [Header("Decision thresholds")]
        [Tooltip("Personal CEP threshold after which bot stops buying")]
        public int cepBuyThreshold = 1000;
        [Tooltip("Minimum money reserve before spending")]
        public int safeMoneyReserve = 300;
        [Tooltip("Delay between bot decisions in seconds")]
        public float decisionDelay = 1.5f;
    }
}
