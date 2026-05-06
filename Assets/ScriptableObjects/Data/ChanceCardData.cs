using UnityEngine;

namespace Ecopoly.Data
{
    public enum ChanceCardType
    {
        MoveVelo, MoveCar, MovePlane,
        Tax, GoToJail, GetOutOfJail, MoveToTile,
        Dilemma, PersonalCEPUp, PersonalCEPDown,
        GlobalCEPUp, ReceiveMoney, BuildingDegraded,
        ConditionalMoney, Reparations, DistrictBuildingDestroyed
    }

    [CreateAssetMenu(fileName = "SO_ChanceCard_", menuName = "Ecopoly/ChanceCard")]
    public class ChanceCardData : ScriptableObject
    {
        public string cardId;
        public ChanceCardType cardType;
        [TextArea(2, 4)]
        public string displayText;
        public Sprite illustration;

        [Header("Values")]
        public int moneyAmount;
        public int cepAmount;
        public int maxMoveDistance;
        public int targetTilePosition;
        [Tooltip("Dilemma: cost per player")]
        public int dilemmaCostPerPlayer;
        public int dilemmaCEPEffect;
        [Tooltip("Conditional: receive below threshold, pay above")]
        public int conditionalCEPThreshold;
        public int conditionalMoneyBelow;
        public int conditionalMoneyAbove;
    }
}
