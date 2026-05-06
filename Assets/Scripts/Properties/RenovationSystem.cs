using System.Collections.Generic;
using UnityEngine;
using Ecopoly.Utils;
using Ecopoly.Data;
using Ecopoly.Core;

namespace Ecopoly.Properties
{
    /// <summary>
    /// Attached to every property BoardTile.
    /// Spawns the correct HouseLevel prefab on purchase/renovation and destroys it on sell/degrade.
    /// _housePrefabsByLevel[0] = HouseLevel1, [1] = HouseLevel2, [2] = HouseLevel3, [3] = HouseLevel4.
    /// The prefab is instantiated at runtime under _buildingAnchor — nothing is pre-spawned.
    /// </summary>
    public class RenovationSystem : MonoBehaviour
    {
        [Header("House Prefabs (index 0 = Level 1, index 3 = Level 4)")]
        [SerializeField] private GameObject[] _housePrefabsByLevel = new GameObject[4];

        [Header("Spawn anchor")]
        [SerializeField] private Transform _buildingAnchor;

        [Header("CEP Indicator")]
        [SerializeField] private Renderer _emissionIndicator;
        [Tooltip("Gradient: red (lv1 = polluting) to green (lv4 = eco)")]
        [SerializeField] private Gradient _emissionColorGradient;

        [Header("Linked Property")]
        [SerializeField] private string _propertyId;

        private int _currentLevel = 0; // 0 = unowned / no model
        private GameObject _activeInstance;

        private void OnEnable()
        {
            EventBus.On(GameEvent.PropertyRenovated, OnRenovated);
            EventBus.On(GameEvent.PropertyDegraded, OnDegraded);
            EventBus.On(GameEvent.PropertyPurchased, OnPurchased);
            EventBus.On(GameEvent.PropertySold, OnSold);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.PropertyRenovated, OnRenovated);
            EventBus.Off(GameEvent.PropertyDegraded, OnDegraded);
            EventBus.Off(GameEvent.PropertyPurchased, OnPurchased);
            EventBus.Off(GameEvent.PropertySold, OnSold);
        }

        // --- Event handlers
        private void OnPurchased(object payload)
        {
            if (!(payload is PropertyEventPayload p) || p.PropertyId != _propertyId) return;
            SpawnLevel(1);
        }

        private void OnRenovated(object payload)
        {
            if (!(payload is RenovationEventPayload r) || r.PropertyId != _propertyId) return;
            SpawnLevel(r.NewLevel);
        }

        private void OnDegraded(object payload)
        {
            if (!(payload is RenovationEventPayload r) || r.PropertyId != _propertyId) return;
            SpawnLevel(r.NewLevel);
        }

        private void OnSold(object payload)
        {
            if (!(payload is PropertyEventPayload p) || p.PropertyId != _propertyId) return;
            DestroyCurrentInstance();
            _currentLevel = 0;
            UpdateEmissionIndicator();
        }

        // --- Spawn logic
        private void SpawnLevel(int level)
        {
            int clamped = Mathf.Clamp(level, Constants.MIN_RENOVATION_LEVEL, Constants.MAX_RENOVATION_LEVEL);
            if (_currentLevel == clamped) return; // already correct model

            DestroyCurrentInstance();

            int prefabIndex = clamped - 1;
            if (prefabIndex < 0 || prefabIndex >= _housePrefabsByLevel.Length
                || _housePrefabsByLevel[prefabIndex] == null)
            {
                Debug.LogWarning($"[RenovationSystem] No prefab assigned for level {clamped} on '{_propertyId}'.");
                _currentLevel = clamped;
                return;
            }

            Transform parent = _buildingAnchor != null ? _buildingAnchor : transform;
            _activeInstance = Instantiate(_housePrefabsByLevel[prefabIndex], parent);
            _activeInstance.transform.localPosition = Vector3.zero;
            // keep x rotation from prefab
                _activeInstance.transform.localRotation = Quaternion.Euler(
                    _housePrefabsByLevel[prefabIndex].transform.localRotation.eulerAngles.x,
                    0f,
                    0f
                );
            // keep the prefab's local scale (allows for different sizes if needed)
                _activeInstance.transform.localScale = _housePrefabsByLevel[prefabIndex].transform.localScale;
            _currentLevel = clamped;
            UpdateEmissionIndicator();
        }

        private void DestroyCurrentInstance()
        {
            if (_activeInstance != null)
            {
                Destroy(_activeInstance);
                _activeInstance = null;
            }
        }

        private void UpdateEmissionIndicator()
        {
            if (_emissionIndicator == null) return;

            if (_currentLevel == 0)
            {
                _emissionIndicator.gameObject.SetActive(false);
                return;
            }

            if (_emissionColorGradient == null)
            {
                _emissionIndicator.gameObject.SetActive(true);
                return;
            }

            // Level 1 = red (most polluting), Level 4 = green (eco)
            float t = (float)(_currentLevel - 1) / (Constants.MAX_RENOVATION_LEVEL - 1);
            Color color = _emissionColorGradient.Evaluate(t);
            _emissionIndicator.material.SetColor("_EmissionColor", color * 2f);
            _emissionIndicator.gameObject.SetActive(true);
        }

        // --- Public API
        public int CurrentLevel => _currentLevel;
        public string PropertyId => _propertyId;

        /// <summary>
        /// Links this component to a property and injects prefab references.
        /// Call before the first event arrives when setting up programmatically.
        /// </summary>
        public void Initialize(string propertyId, Transform buildingAnchor, GameObject[] housePrefabs)
        {
            _propertyId = propertyId;
            _buildingAnchor = buildingAnchor;
            if (housePrefabs != null && housePrefabs.Length > 0)
                _housePrefabsByLevel = housePrefabs;
            DestroyCurrentInstance();
            _currentLevel = 0;
        }
    }

    /// <summary>
    /// Visual component for district buildings (commercial / ecological).
    /// Listens to DistrictBuildingBuilt / DistrictBuildingDestroyed and shows the right mesh.
    /// </summary>
    public class DistrictBuildingComponent : MonoBehaviour
    {
        [SerializeField] private string _groupId;
        [SerializeField] private DistrictBuildingType _buildingType;

        [Header("Visuals")]
        [SerializeField] private GameObject _commercialMesh;
        [SerializeField] private GameObject _ecologicalMesh;
        [SerializeField] private ParticleSystem _buildEffect;

        private void OnEnable()
        {
            EventBus.On(GameEvent.DistrictBuildingBuilt, OnBuilt);
            EventBus.On(GameEvent.DistrictBuildingDestroyed, OnDestroyed);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.DistrictBuildingBuilt, OnBuilt);
            EventBus.Off(GameEvent.DistrictBuildingDestroyed, OnDestroyed);
        }

        private void OnBuilt(object payload)
        {
            if (!(payload is PropertyEventPayload p) || p.PropertyId != _groupId) return;
            gameObject.SetActive(true);
            _commercialMesh?.SetActive(_buildingType == DistrictBuildingType.Commercial);
            _ecologicalMesh?.SetActive(_buildingType == DistrictBuildingType.Ecological);
            _buildEffect?.Play();
        }

        private void OnDestroyed(object payload)
        {
            string groupId = null;
            if (payload is string s)
                groupId = s;
            else if (payload is PropertyEventPayload ep)
                groupId = ep.PropertyId;

            if (groupId == _groupId)
                gameObject.SetActive(false);
        }
    }
}

