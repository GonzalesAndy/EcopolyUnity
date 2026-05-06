using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Ecopoly.Data;

namespace Ecopoly.UI
{
    public class CardDisplayUI : MonoBehaviour
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _descriptionText;
        [SerializeField] private Image _illustration;
        [SerializeField] private Image _cardBackground;
        [SerializeField] private TextMeshProUGUI _intensityLabel;
        [SerializeField] private Button _closeButton;

        [Header("Couleurs par type")]
        [SerializeField] private Color _chanceColor = new Color(1f, 0.9f, 0.3f);
        [SerializeField] private Color _eventColor  = new Color(0.8f, 0.2f, 0.2f);
        private bool _closeListenerBound;

        private void Awake()
        {
            BindCloseListener();
            if (_panel != null)
                _panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
                _closeButton.onClick.RemoveListener(Hide);
        }

        private void BindCloseListener()
        {
            if (_closeListenerBound) return;
            if (_closeButton != null)
                _closeButton.onClick.AddListener(Hide);
            _closeListenerBound = true;
        }

        public void ShowChanceCard(ChanceCardData card)
        {
            if (!gameObject.activeInHierarchy)
                gameObject.SetActive(true);
            if (_titleText != null) _titleText.text = "Luck Card";
            if (_descriptionText != null) _descriptionText.text = card.displayText;
            if (_intensityLabel != null) _intensityLabel.gameObject.SetActive(false);
            if (card.illustration != null && _illustration != null) _illustration.sprite = card.illustration;
            if (_cardBackground != null) _cardBackground.color = _chanceColor;
            if (_panel == null) return;
            _panel.SetActive(true);
            _panel.transform.localScale = Vector3.zero;
            _panel.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack);
        }

        public void ShowEventCard(EventCardData card, int intensityLevel)
        {
            if (!gameObject.activeInHierarchy)
                gameObject.SetActive(true);
            if (_titleText != null) _titleText.text = card.cardTitle;
            var effect = card.effectsByLevel[Mathf.Clamp(intensityLevel - 1, 0, 3)];
            if (_descriptionText != null) _descriptionText.text = effect.description;
            if (_intensityLabel != null)
            {
                _intensityLabel.gameObject.SetActive(true);
                _intensityLabel.text = $"Intensity Level : {intensityLevel}";
            }
            if (card.illustration != null && _illustration != null) _illustration.sprite = card.illustration;
            if (_cardBackground != null) _cardBackground.color = _eventColor;
            if (_panel == null) return;
            _panel.SetActive(true);
            _panel.transform.localScale = Vector3.zero;
            _panel.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack);
        }

        public void Hide()
        {
            if (_panel == null) return;
            _panel.transform.DOScale(0f, 0.15f).SetEase(Ease.InBack)
                .OnComplete(() => _panel.SetActive(false));
        }
    }
}
