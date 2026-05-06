using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Ecopoly.Core;
using Ecopoly.Utils;
using Ecopoly.Network;
using Ecopoly.Data;

namespace Ecopoly.UI
{
    /// <summary>
    /// Modal displayed when the local player lands on their own property.
    /// Shows renovation options (jump to any level) and a sell option.
    /// </summary>
    public class RenovationOfferUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject _panel;

        [Header("Info")]
        [SerializeField] private TextMeshProUGUI _propertyNameText;
        [SerializeField] private TextMeshProUGUI _currentLevelText;

        [Header("Level Buttons")]
        [Tooltip("Four buttons for Level 1→2, 2→3, 3→4. Each shown/hidden based on current level and budget.")]
        [SerializeField] private Button[] _renovateLevelButtons = new Button[3]; // upgrade to lvl 2,3,4
        [SerializeField] private TextMeshProUGUI[] _renovateLevelLabels = new TextMeshProUGUI[3];

        [Header("Actions")]
        [SerializeField] private Button _sellButton;
        [SerializeField] private TextMeshProUGUI _sellPriceText;
        [SerializeField] private Button _closeButton;

        private int _pendingPlayerId;
        private string _pendingPropertyId;
        private int _currentLevel;
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
            if (_closeButton != null) _closeButton.onClick.RemoveListener(Hide);
            if (_sellButton != null) _sellButton.onClick.RemoveListener(OnSellClicked);
            for (int i = 0; i < _renovateLevelButtons.Length; i++)
            {
                if (_renovateLevelButtons[i] == null) continue;
                _renovateLevelButtons[i].onClick.RemoveAllListeners();
            }
        }

        private void BindButtonListeners()
        {
            if (_listenersBound) return;
            _listenersBound = true;

            if (_closeButton != null) _closeButton.onClick.AddListener(Hide);
            if (_sellButton != null) _sellButton.onClick.AddListener(OnSellClicked);

            for (int i = 0; i < _renovateLevelButtons.Length; i++)
            {
                if (_renovateLevelButtons[i] == null) continue;
                int targetLevel = i + 2; // button[0]=upgrade to 2, [1]=to 3, [2]=to 4
                _renovateLevelButtons[i].onClick.AddListener(() => OnRenovateToLevelClicked(targetLevel));
            }
        }

        /// <summary>
        /// Opens the panel for the given player and property.
        /// </summary>
        public void Show(int playerId, string propertyId, int currentLevel)
        {
            _pendingPlayerId = playerId;
            _pendingPropertyId = propertyId;
            _currentLevel = currentLevel;

            var prop = BoardController.Instance.GetPropertyData(propertyId);
            if (prop == null)
            {
                Debug.LogWarning($"[RenovationOfferUI] No PropertyData found for id '{propertyId}'.");
                return;
            }

            var player = GameManager.Instance.GetPlayer(playerId);
            if (player == null) return;

            if (_propertyNameText != null)
                _propertyNameText.text = prop.displayName;

            if (_currentLevelText != null)
                _currentLevelText.text = $"Current Level : {currentLevel} / {Constants.MAX_RENOVATION_LEVEL}";

            // Sell price
            int sellPrice = Mathf.FloorToInt(prop.purchasePrice * Constants.SELL_RATIO);
            if (_sellPriceText != null)
                _sellPriceText.text = $"Sell - M{sellPrice}";

            // Configure renovation level buttons
            // _renovateLevelButtons[i] upgrades to level i+2
            for (int i = 0; i < _renovateLevelButtons.Length; i++)
            {
                if (_renovateLevelButtons[i] == null) continue;

                int targetLevel = i + 2;
                bool alreadyAtOrAbove = currentLevel >= targetLevel;

                // Each upgrade step costs from costIndex = (step-1) where step goes 1→2, 2→3, 3→4
                // To jump multiple levels, sum all intermediate renovation costs
                int totalCost = 0;
                int totalCEP = 0;
                bool affordable = true;

                if (!alreadyAtOrAbove)
                {
                    for (int step = currentLevel; step < targetLevel; step++)
                    {
                        int costIndex = step - 1;
                        if (costIndex < prop.renovationCosts.Length)
                            totalCost += prop.renovationCosts[costIndex];
                        if (costIndex < prop.renovationCEPCosts.Length)
                            totalCEP += prop.renovationCEPCosts[costIndex];
                    }
                    affordable = player.Money >= totalCost;
                }

                _renovateLevelButtons[i].gameObject.SetActive(!alreadyAtOrAbove);
                _renovateLevelButtons[i].interactable = affordable;

                if (_renovateLevelLabels != null && i < _renovateLevelLabels.Length && _renovateLevelLabels[i] != null)
                {
                    if (alreadyAtOrAbove)
                        _renovateLevelLabels[i].text = $"Level {targetLevel}";
                    else
                        _renovateLevelLabels[i].text = $"→ Level {targetLevel}  M{totalCost}  +{totalCEP} CEP";
                }
            }

            gameObject.SetActive(true);
            if (_panel != null) _panel.SetActive(true);
        }

        private void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            gameObject.SetActive(false);
            // Release the landing coroutine in TurnManager so the turn can continue.
            bool isNetworked = NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsListening
                && !NetworkManager.Singleton.IsServer;
            if (isNetworked)
                EcopolyNetworkManager.Instance?.RequestDismissRenovationServerRpc(_pendingPlayerId);
            else
                TurnManager.Instance?.DismissRenovationOffer();
        }

        // --- Button callbacks
        /// <summary>Renovates directly to the chosen level, stepping through intermediate costs.</summary>
        private void OnRenovateToLevelClicked(int targetLevel)
        {
            var player = GameManager.Instance.GetPlayer(_pendingPlayerId);
            if (player == null) { Hide(); return; }

            bool isNetworked = NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsListening
                && !NetworkManager.Singleton.IsServer;

            // Step through each renovation from current to target
            for (int step = _currentLevel; step < targetLevel; step++)
            {
                if (isNetworked)
                    EcopolyNetworkManager.Instance?.RequestRenovatePropertyServerRpc(_pendingPropertyId, _pendingPlayerId);
                else
                    BoardController.Instance.RenovateProperty(player, _pendingPropertyId);
            }

            Hide();
        }

        private void OnSellClicked()
        {
            var player = GameManager.Instance.GetPlayer(_pendingPlayerId);
            if (player == null) { Hide(); return; }

            bool isNetworked = NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsListening
                && !NetworkManager.Singleton.IsServer;

            if (isNetworked)
                EcopolyNetworkManager.Instance?.RequestSellPropertyServerRpc(_pendingPropertyId, _pendingPlayerId);
            else
                BoardController.Instance.SellProperty(player, _pendingPropertyId);

            Hide();
        }
    }
}

