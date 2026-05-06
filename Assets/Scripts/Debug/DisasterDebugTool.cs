using UnityEngine;
using UnityEngine.InputSystem;
using Ecopoly.Cards;
using Ecopoly.Core;

namespace Ecopoly.Tools
{
    /// <summary>
    /// Runtime debug tool for triggering level-4 disaster VFX + weather at the lake center.
    /// Attach to any GameObject in the scene. Designed for cutscene recording.
    ///
    /// Keyboard shortcuts (Play Mode):
    ///   [1] Hurricane   [2] Sandstorm   [3] Wildfire   [4] Lightning   [0] Stop / restore
    /// </summary>
    public class DisasterDebugTool : MonoBehaviour
    {
        [Header("Spawn Reference")]
        [Tooltip("Assign the 'Center of lake' Transform. All VFX spawn here.")]
        [SerializeField] private Transform _lakeCenter;

        [Header("Keyboard Shortcuts")]
        [SerializeField] private Key _hurricaneKey  = Key.Digit1;
        [SerializeField] private Key _sandstormKey  = Key.Digit2;
        [SerializeField] private Key _wildfireKey   = Key.Digit3;
        [SerializeField] private Key _lightningKey  = Key.Digit4;
        [SerializeField] private Key _stopKey       = Key.Digit0;

        [Header("HUD")]
        [Tooltip("Show the shortcut legend on screen during Play Mode.")]
        [SerializeField] private bool _showLegend = true;

        // --- Unity
        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[_hurricaneKey].wasPressedThisFrame)  TriggerHurricane();
            if (kb[_sandstormKey].wasPressedThisFrame)  TriggerSandstorm();
            if (kb[_wildfireKey].wasPressedThisFrame)   TriggerWildfire();
            if (kb[_lightningKey].wasPressedThisFrame)  TriggerLightning();
            if (kb[_stopKey].wasPressedThisFrame)       StopAll();
        }

        private void OnGUI()
        {
            if (!_showLegend) return;

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(10, 10, 260, 130), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUIStyle label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            float x = 18f, y = 14f, h = 22f;

            GUI.Label(new Rect(x, y,      240, h), "[1]  Hurricane L4",  label); y += h;
            GUI.Label(new Rect(x, y,      240, h), "[2]  Sandstorm L4",  label); y += h;
            GUI.Label(new Rect(x, y,      240, h), "[3]  Wildfire L4",   label); y += h;
            GUI.Label(new Rect(x, y,      240, h), "[4]  Lightning L4",  label); y += h;
            GUI.Label(new Rect(x, y,      240, h), "[0]  Stop / Restore", label);
        }

        // --- Public triggers (also available via ContextMenu in Edit Mode)
        [ContextMenu("Trigger Hurricane L4")]
        public void TriggerHurricane()  => Trigger("event_hurricane");

        [ContextMenu("Trigger Sandstorm L4")]
        public void TriggerSandstorm()  => Trigger("event_drought");

        [ContextMenu("Trigger Wildfire L4")]
        public void TriggerWildfire()   => Trigger("event_wildfire");

        [ContextMenu("Trigger Lightning L4")]
        public void TriggerLightning()  => Trigger("event_lightning");

        [ContextMenu("Stop / Restore Weather")]
        public void StopAll()           => WeatherController.Instance?.RestoreDefaultWeather();

        // --- Internal
        private void Trigger(string cardId)
        {
            var resolver = DisasterResolver.Instance;
            if (resolver == null)
            {
                UnityEngine.Debug.LogWarning("[DisasterDebugTool] DisasterResolver not found in scene.");
                return;
            }

            Vector3 pos = _lakeCenter != null ? _lakeCenter.position : Vector3.zero;

            // Both playerWorldPos and lakePos point to the lake center —
            // all VFX spawn there regardless of spawn target type.
            resolver.ApplyDisasterVFXLocal(cardId, 4, pos, pos);
            UnityEngine.Debug.Log($"[DisasterDebugTool] Triggered {cardId} L4 at {pos}");
        }
    }
}

