using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Ecopoly.Core;
using Ecopoly.Utils;
using Ecopoly.Network;

namespace Ecopoly.UI
{
    public class PropertyOfferUI : MonoBehaviour
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private TextMeshProUGUI _propertyNameText;
        [SerializeField] private TextMeshProUGUI _priceText;
        [SerializeField] private TextMeshProUGUI _cepCostText;
        [SerializeField] private Button _buyButton;
        [SerializeField] private Button _declineButton;

        private int _pendingPlayerId;
        private string _pendingPropertyId;
        private bool _listenersBound;

        private void Awake()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        private void OnEnable()
        {
            BindButtonListeners();
        }

        private void OnDestroy()
        {
            if (_buyButton != null) _buyButton.onClick.RemoveListener(OnBuyClicked);
            if (_declineButton != null) _declineButton.onClick.RemoveListener(Hide);
        }

        private void BindButtonListeners()
        {
            if (_listenersBound) return;
            if (_buyButton != null) _buyButton.onClick.AddListener(OnBuyClicked);
            if (_declineButton != null) _declineButton.onClick.AddListener(Hide);
            _listenersBound = true;
        }

        public void Show(int playerId, string propertyId)
        {
            _pendingPlayerId = playerId;
            _pendingPropertyId = propertyId;

            var prop = BoardController.Instance.GetPropertyData(propertyId);
            if (prop == null)
            {
                Debug.LogWarning($"[PropertyOfferUI] No PropertyData found for id '{propertyId}'. Check SO_BoardConfig.allProperties.");
                return;
            }

            if (_propertyNameText != null) _propertyNameText.text = prop.displayName;
            if (_priceText != null)        _priceText.text = $"Prix : M{prop.purchasePrice}";
            if (_cepCostText != null)      _cepCostText.text = $"+{prop.cepOnPurchase} CEP";

            // Activate the root GO first, then the inner panel.
            gameObject.SetActive(true);
            if (_panel != null) _panel.SetActive(true);
        }

        private void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            gameObject.SetActive(false);
        }

        private void OnBuyClicked()
        {
            Debug.Log($"PropertyOfferUI: OnBuyClicked pendingPlayer={_pendingPlayerId} property={_pendingPropertyId}");
            var player = GameManager.Instance.GetPlayer(_pendingPlayerId);
            if (player == null) return;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
                EcopolyNetworkManager.Instance?.RequestBuyPropertyServerRpc(_pendingPropertyId, _pendingPlayerId);
            else
                BoardController.Instance.BuyProperty(player, _pendingPropertyId);
            Hide();
        }
    }
}
