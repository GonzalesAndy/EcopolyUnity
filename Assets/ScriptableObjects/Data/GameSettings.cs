using UnityEngine;

namespace Ecopoly.Data
{
    [CreateAssetMenu(fileName = "SO_GameSettings", menuName = "Ecopoly/GameSettings")]
    public class GameSettings : ScriptableObject
    {
        [Header("Starting money")]
        public int startingMoney = 1500;

        [Header("Voice chat")]
        public float voiceMaxDistance = 8f;
        public float voiceMinDistance = 1f;

        [Header("Camera")]
        public float cameraSwitchBlend = 0.5f;

        [Header("Animation")]
        [Tooltip("Pawn move step duration in seconds")]
        public float pawnMoveStepDuration = 0.2f;

        [Header("Disaster VFX")]
        public float disasterVFXDuration = 4f;
    }
}
