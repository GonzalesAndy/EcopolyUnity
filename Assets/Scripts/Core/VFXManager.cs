using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Ecopoly.Utils;

namespace Ecopoly.Core
{
    /// <summary>
    /// VFX manager. Subscribes to disaster events
    /// and instantiates VFX prefabs via Addressables.
    /// VFX are pooled for recurring effects.
    /// </summary>
    public class VFXManager : MonoBehaviour
    {
        public static VFXManager Instance { get; private set; }

        [Header("Persistent VFX (non-Addressables)")]
        [Tooltip("Ambient particles per CEP intensity level")]
        [SerializeField] private ParticleSystem[] _ambientParticlesByLevel;

        // Pool of active Addressables handles (for correct release)
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> _loadedHandles
            = new Dictionary<string, AsyncOperationHandle<GameObject>>();

        // Active VFX instances (key = Unity instanceId)
        private readonly Dictionary<int, Coroutine> _activeVFXCoroutines
            = new Dictionary<int, Coroutine>();

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.DisasterTriggered, OnDisasterTriggered);
            EventBus.On(GameEvent.GlobalCEPThresholdChanged, OnThresholdChanged);
            EventBus.On(GameEvent.PropertyRenovated, OnPropertyRenovated);
            EventBus.On(GameEvent.PropertyPurchased, OnPropertyPurchased);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.DisasterTriggered, OnDisasterTriggered);
            EventBus.Off(GameEvent.GlobalCEPThresholdChanged, OnThresholdChanged);
            EventBus.Off(GameEvent.PropertyRenovated, OnPropertyRenovated);
            EventBus.Off(GameEvent.PropertyPurchased, OnPropertyPurchased);
        }

        // --- Disasters ---

        private void OnDisasterTriggered(object payload)
        {
            if (!(payload is DisasterEventPayload disaster)) return;

            // The VFX prefab is determined by DisasterResolver via EventCardData.
            // Here we trigger additional effects (camera shake, flash, etc.)
            StartCoroutine(CameraShake(disaster.IntensityLevel));
        }

        private IEnumerator CameraShake(int intensity)
        {
            float duration = 0.3f * intensity;
            float magnitude = 0.05f * intensity;
            var cam = Camera.CameraController.Instance;
            if (cam == null) yield break;

            float elapsed = 0f;
            Vector3 originalPos = cam.transform.localPosition;

            while (elapsed < duration)
            {
                float x = Random.Range(-1f, 1f) * magnitude;
                float y = Random.Range(-1f, 1f) * magnitude;
                cam.transform.localPosition = new Vector3(originalPos.x + x, originalPos.y + y, originalPos.z);
                elapsed += Time.deltaTime;
                yield return null;
            }
            cam.transform.localPosition = originalPos;
        }

        // --- Ambiance Levels ---

        private void OnThresholdChanged(object payload)
        {
            if (!(payload is int level)) return;
            UpdateAmbientParticles(level);
        }

        private void UpdateAmbientParticles(int level)
        {
            for (int i = 0; i < _ambientParticlesByLevel.Length; i++)
            {
                if (_ambientParticlesByLevel[i] == null) continue;
                bool shouldPlay = (i < level);
                if (shouldPlay && !_ambientParticlesByLevel[i].isPlaying)
                    _ambientParticlesByLevel[i].Play();
                else if (!shouldPlay && _ambientParticlesByLevel[i].isPlaying)
                    _ambientParticlesByLevel[i].Stop();
            }
        }

        // --- Renovation / Purchase ---

        private void OnPropertyRenovated(object payload)
        {
            if (!(payload is RenovationEventPayload reno)) return;
            var boardController = BoardController.Instance;
            if (boardController == null) return;
            var tile = boardController.GetTile(GetPositionForProperty(reno.PropertyId));
            if (tile == null) return;

            // Small green particle burst to indicate renovation
            SpawnSimpleVFX("VFX_Renovation", tile.BuildingAnchorPosition, duration: 1.5f);
        }

        private void OnPropertyPurchased(object payload)
        {
            if (!(payload is PropertyEventPayload purchase)) return;
            var pos = GetTileWorldPosition(purchase.PropertyId);
            SpawnSimpleVFX("VFX_Purchase", pos, duration: 1f);
        }

        // --- Addressable VFX Spawn ---

        public void SpawnVFXAddressable(string key, Vector3 position, float duration)
        {
            StartCoroutine(SpawnVFXCoroutine(key, position, duration));
        }

        private IEnumerator SpawnVFXCoroutine(string key, Vector3 position, float duration)
        {
            var handle = Addressables.InstantiateAsync(key, position, Quaternion.identity);
            yield return handle;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogWarning($"[VFX] Unable to load: {key}");
                yield break;
            }

            var instance = handle.Result;
            yield return new WaitForSeconds(duration);

            Addressables.ReleaseInstance(instance);
        }

        // --- Simple VFX Spawn (internal pool) ---

        private void SpawnSimpleVFX(string key, Vector3 position, float duration)
        {
            StartCoroutine(SpawnVFXCoroutine(key, position, duration));
        }

        // --- Helpers ---

        private int GetPositionForProperty(string propertyId)
        {
            var gameManager = GameManager.Instance;
            if (gameManager == null || gameManager.BoardConfig == null) return 0;
            var config = gameManager.BoardConfig;
            foreach (var tile in config.tiles)
                if (tile.propertyId == propertyId) return tile.position;
            return 0;
        }

        private Vector3 GetTileWorldPosition(string propertyId)
        {
            int pos = GetPositionForProperty(propertyId);
            var boardController = BoardController.Instance;
            if (boardController == null) return Vector3.zero;
            var tile = boardController.GetTile(pos);
            return tile != null ? tile.transform.position : Vector3.zero;
        }

        private void OnDestroy()
        {
            // Release all active Addressables handles
            foreach (var handle in _loadedHandles.Values)
                if (handle.IsValid()) Addressables.Release(handle);
            _loadedHandles.Clear();
        }
    }
}
