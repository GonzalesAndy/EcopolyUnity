using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Ecopoly.Utils;
using Ecopoly.Core;

namespace Ecopoly.UI
{
    public class CEPGaugeUI : MonoBehaviour
    {
        [Header("Jauge globale")]
        [SerializeField] private Slider _globalSlider;
        [SerializeField] private TextMeshProUGUI _globalCEPText;
        [SerializeField] private TextMeshProUGUI _levelText;
        [SerializeField] private Image _globalFill;

        [Header("Jauge personnelle")]
        [SerializeField] private Slider _personalSlider;
        [SerializeField] private TextMeshProUGUI _personalCEPText;
        [SerializeField] private Image _personalFill;

        [Header("Couleurs par niveau")]
        [SerializeField] private Color _level1Color = Color.green;
        [SerializeField] private Color _level2Color = Color.yellow;
        [SerializeField] private Color _level3Color = new Color(1f, 0.5f, 0f);
        [SerializeField] private Color _level4Color = Color.red;

        private int _localPlayerId;

        private void OnEnable()
        {
            Debug.Log("CEPGaugeUI: OnEnable");
            EventBus.On(GameEvent.GlobalCEPChanged, OnGlobalCEPChanged);
            EventBus.On(GameEvent.GlobalCEPThresholdChanged, OnThresholdChanged);
            EventBus.On(GameEvent.CEPChanged, OnPersonalCEPChanged);
        }

        private void OnDisable()
        {
            Debug.Log("CEPGaugeUI: OnDisable");
            EventBus.Off(GameEvent.GlobalCEPChanged, OnGlobalCEPChanged);
            EventBus.Off(GameEvent.GlobalCEPThresholdChanged, OnThresholdChanged);
            EventBus.Off(GameEvent.CEPChanged, OnPersonalCEPChanged);
        }

        public void Initialize(int localPlayerId, int playerCount)
        {
            _localPlayerId = localPlayerId;
            RefreshFromGameManager();
        }

        /// <summary>Pulls the current CEP state from GameManager and syncs the UI immediately.</summary>
        private void RefreshFromGameManager()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            // Global gauge — drive slider with raw CEP scaled to slider's own maxValue
            int globalCEP = gm.GlobalCEP;
            int maxCEP = GetCurrentMaxCEP();
            if (_globalSlider != null)
                _globalSlider.value = ScaleToSlider(_globalSlider, globalCEP, maxCEP);
            if (_globalCEPText != null)
                _globalCEPText.text = $"{globalCEP} / {maxCEP} CEP";

            // Intensity level colour
            Color levelColor = gm.CurrentIntensityLevel switch
            {
                1 => _level1Color,
                2 => _level2Color,
                3 => _level3Color,
                _ => _level4Color
            };
            if (_globalFill != null) _globalFill.color = levelColor;
            if (_levelText != null) _levelText.text = $"Level {gm.CurrentIntensityLevel}";

            // Personal gauge
            var localState = gm.GetPlayer(_localPlayerId);
            if (localState != null)
            {
                float personalNorm = (float)localState.PersonalCEP / Constants.MAX_PERSONAL_CEP;
                if (_personalSlider != null)
                    _personalSlider.value = ScaleToSlider(_personalSlider, localState.PersonalCEP, Constants.MAX_PERSONAL_CEP);
                if (_personalCEPText != null)
                    _personalCEPText.text = $"{localState.PersonalCEP} CEP";
                Color pc = personalNorm < 0.5f ? _level1Color
                         : personalNorm < 0.75f ? _level2Color
                         : personalNorm < 0.9f  ? _level3Color
                         : _level4Color;
                if (_personalFill != null) _personalFill.color = pc;
            }
        }

        private void OnGlobalCEPChanged(object payload)
        {
            Debug.Log($"CEPGaugeUI: OnGlobalCEPChanged payload={payload}");
            if (!(payload is int globalCEP)) return;
            int maxCEP = GetCurrentMaxCEP();
            _globalSlider.DOValue(ScaleToSlider(_globalSlider, globalCEP, maxCEP), 0.3f);
            _globalCEPText.text = $"{globalCEP} / {maxCEP} CEP";
        }

        private void OnThresholdChanged(object payload)
        {
            Debug.Log($"CEPGaugeUI: OnThresholdChanged payload={payload}");
            if (!(payload is int level)) return;
            Color targetColor = level switch
            {
                1 => _level1Color,
                2 => _level2Color,
                3 => _level3Color,
                4 => _level4Color,
                _ => _level4Color
            };
            _globalFill.DOColor(targetColor, 0.5f);
            _levelText.text = $"Level {level}";
        }

        private void OnPersonalCEPChanged(object payload)
        {
            Debug.Log($"CEPGaugeUI: OnPersonalCEPChanged payload={payload?.GetType().Name}");
            if (!(payload is CEPChangePayload change) || change.PlayerId != _localPlayerId) return;
            float personalNorm = (float)change.NewValue / Constants.MAX_PERSONAL_CEP;
            _personalSlider.DOValue(ScaleToSlider(_personalSlider, change.NewValue, Constants.MAX_PERSONAL_CEP), 0.3f);
            _personalCEPText.text = $"{change.NewValue} CEP";

            Color c = personalNorm < 0.5f ? _level1Color
                    : personalNorm < 0.75f ? _level2Color
                    : personalNorm < 0.9f  ? _level3Color
                    : _level4Color;
            _personalFill.DOColor(c, 0.3f);
        }

        /// <summary>
        /// Converts a raw CEP value to the slider's value range, regardless of whether
        /// the slider uses 0–1 (normalized) or 0–maxValue (absolute) scale.
        /// </summary>
        private static float ScaleToSlider(Slider slider, int cepValue, int cepMax)
        {
            if (cepMax <= 0) return slider.minValue;
            float t = Mathf.Clamp01((float)cepValue / cepMax);
            return Mathf.Lerp(slider.minValue, slider.maxValue, t);
        }

        private int GetCurrentMaxCEP()
        {
            int playerIndex = GameManager.Instance.ActivePlayerCount - Constants.MIN_PLAYERS;
            if (playerIndex < 0 || playerIndex >= 3) return 2500;
            return Constants.CEP_THRESHOLDS[playerIndex, 4];
        }
    }
}
