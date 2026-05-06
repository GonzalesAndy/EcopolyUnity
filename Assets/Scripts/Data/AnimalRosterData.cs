using UnityEngine;

namespace Ecopoly.Data
{
    /// <summary>
    /// List of animal prefabs assigned to each player slot (0-4).
    /// Create via right-click > Create > Ecopoly > Animal Roster
    /// and place at Assets/Resources/Animals/SO_AnimalRoster.asset
    /// </summary>
    [CreateAssetMenu(fileName = "SO_AnimalRoster", menuName = "Ecopoly/Animal Roster")]
    public class AnimalRosterData : ScriptableObject
    {
        [Tooltip("One prefab per player slot. Index 0 = human player, 1-4 = bots.")]
        public GameObject[] animalPrefabs = new GameObject[5];

        public GameObject GetPrefab(int animalIndex)
        {
            if (animalPrefabs == null || animalPrefabs.Length == 0) return null;
            return animalPrefabs[animalIndex % animalPrefabs.Length];
        }
    }
}
