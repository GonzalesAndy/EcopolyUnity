using UnityEngine;
using UnityEngine.UI;

namespace Ecopoly.UI
{
    /// <summary>
    /// Bridges the HUD's Button_Inventory to the CardInventoryPopupUI panel.
    /// Attaches to the same GameObject as HUDController; wired in the Inspector.
    /// </summary>
    public class CardInventoryButtonUI : MonoBehaviour
    {
        [Header("Button")]
        [SerializeField] private Button _inventoryButton;

        [Header("Popup")]
        [SerializeField] private GameObject _inventoryPopup;

        [Header("Inventory Data Source")]
        [SerializeField] private CardInventoryUI _cardInventoryUI;

        private void Awake()
        {
            if (_inventoryButton != null)
                _inventoryButton.onClick.AddListener(ToggleInventory);

            if (_inventoryPopup != null)
                _inventoryPopup.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_inventoryButton != null)
                _inventoryButton.onClick.RemoveListener(ToggleInventory);
        }

        private void ToggleInventory()
        {
            if (_inventoryPopup == null) return;
            bool opening = !_inventoryPopup.activeSelf;
            _inventoryPopup.SetActive(opening);

            // Refresh card slots whenever the popup is opened so they reflect the current hand.
            if (opening && _cardInventoryUI != null)
                _cardInventoryUI.ForceRefresh();
        }

        /// <summary>Force-closes the popup (e.g. at turn end).</summary>
        public void CloseInventory()
        {
            if (_inventoryPopup != null)
                _inventoryPopup.SetActive(false);
        }
    }
}
