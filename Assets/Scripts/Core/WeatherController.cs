using System.Collections;
using UnityEngine;
using DistantLands.Cozy;
using DistantLands.Cozy.Data;
using Ecopoly.Utils;

namespace Ecopoly.Core
{
    /// <summary>
    /// Manages CozyWeather transitions triggered by disaster events.
    /// Temporarily sets a disaster weather profile then reverts to the default.
    /// Singleton — scene-bound.
    /// </summary>
    public class WeatherController : MonoBehaviour
    {
        public static WeatherController Instance { get; private set; }

        [Header("Default Weather")]
        [Tooltip("The weather profile restored after a disaster ends. Assign the scene's baseline weather.")]
        [SerializeField] private WeatherProfile _defaultWeatherProfile;

        [Header("Red Ambiance (Wildfire)")]
        [Tooltip("Optional post-process or color-grade volume toggled during wildfire.")]
        [SerializeField] private UnityEngine.Rendering.Volume _wildfireAmbianceVolume;

        private Coroutine _activeWeatherCoroutine;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        // --- Public API
        /// <summary>
        /// Applies a disaster weather profile for <paramref name="duration"/> seconds,
        /// then reverts to the default weather. Interrupts any ongoing transition.
        /// </summary>
        public void ApplyDisasterWeather(WeatherProfile profile, float duration, bool enableRedAmbiance = false)
        {
            if (profile == null) return;

            if (_activeWeatherCoroutine != null)
                StopCoroutine(_activeWeatherCoroutine);

            _activeWeatherCoroutine = StartCoroutine(DisasterWeatherRoutine(profile, duration, enableRedAmbiance));
        }

        /// <summary>
        /// Immediately restores the default weather (e.g. on disaster skip/fast-forward).
        /// </summary>
        public void RestoreDefaultWeather()
        {
            if (_activeWeatherCoroutine != null)
            {
                StopCoroutine(_activeWeatherCoroutine);
                _activeWeatherCoroutine = null;
            }

            SetWeatherInternal(_defaultWeatherProfile);
            SetWildfireAmbiance(false);
        }

        // --- Internal
        private IEnumerator DisasterWeatherRoutine(WeatherProfile profile, float duration, bool redAmbiance)
        {
            SetWeatherInternal(profile);
            SetWildfireAmbiance(redAmbiance);

            yield return new WaitForSeconds(duration);

            SetWeatherInternal(_defaultWeatherProfile);
            SetWildfireAmbiance(false);
            _activeWeatherCoroutine = null;
        }

        private void SetWeatherInternal(WeatherProfile profile)
        {
            if (profile == null) return;

            if (CozyWeather.instance == null)
            {
                Debug.LogWarning("[WeatherController] CozyWeather instance not found.");
                return;
            }

            if (!CozyWeather.instance.GetModule(out CozyWeatherModule weatherModule))
            {
                Debug.LogWarning("[WeatherController] CozyWeatherModule not found.");
                return;
            }

            if (weatherModule.ecosystem == null)
            {
                Debug.LogWarning("[WeatherController] CozyWeather ecosystem is null.");
                return;
            }

            weatherModule.ecosystem.SetWeather(profile);
            Debug.Log($"[WeatherController] Weather set to: {profile.name}");
        }

        private void SetWildfireAmbiance(bool enabled)
        {
            if (_wildfireAmbianceVolume == null) return;
            _wildfireAmbianceVolume.enabled = enabled;
        }
    }
}

