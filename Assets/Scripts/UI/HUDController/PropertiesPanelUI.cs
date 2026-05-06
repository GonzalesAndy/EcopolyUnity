using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Unity.Netcode;
using Ecopoly.Core;
using Ecopoly.Data;
using Ecopoly.Utils;
using Ecopoly.Network;

namespace Ecopoly.UI
{
    /// <summary>
    /// Properties panel — one horizontal row per color group.
    ///
    /// Layout per group row:
    ///   [Color cell 90px fixed] | [Card] | [Card] | [Card]  (cards expand equally)
    ///
    /// Each card:
    ///   ▬▬▬▬ 4 px color stripe
    ///   Property Name  (bold)
    ///   Owner Name     (player color, or "—")
    ///
    /// CRITICAL: all LayoutGroups use childControlWidth/Height = true so Unity reads
    /// LayoutElement values and sets child RectTransform sizes. Dynamically-created GOs
    /// added via new GameObject() default to collapsed anchors (set in NewGO) so the
    /// layout group, not the anchor system, drives all sizing.
    /// </summary>
    public class PropertiesPanelUI : MonoBehaviour
    {
        public static PropertiesPanelUI Instance { get; private set; }

        // --- Inspector
        [Header("Root")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private Transform  _contentRoot;

        [Header("Player glow toggles")]
        [SerializeField] private Transform  _glowButtonsRoot;
        [SerializeField] private GameObject _glowButtonPrefab;

        [Header("Animation")]
        [SerializeField] private float _slideInDuration = 0.2f;

        [Header("Theme — Font Sizes")]
        [SerializeField] private float _fontSizeGroupLabel   = 15f;
        [SerializeField] private float _fontSizeCardTitle    = 14f;
        [SerializeField] private float _fontSizeCardOwner    = 12f;
        [SerializeField] private float _fontSizeBuildBtn     = 13f;
        [SerializeField] private float _fontSizeBuildActive  = 13f;

        [Header("Theme — Colors (Light / Green)")]
        [SerializeField] private Color _rowBgEven        = new Color(0.96f, 0.98f, 0.96f, 1f);
        [SerializeField] private Color _rowBgOdd         = new Color(0.90f, 0.95f, 0.90f, 1f);
        [SerializeField] private Color _cardBg           = new Color(1.00f, 1.00f, 1.00f, 1f);
        [SerializeField] private Color _dividerColor     = new Color(0.72f, 0.88f, 0.72f, 1f);
        [SerializeField] private Color _cardSepColor     = new Color(0.80f, 0.90f, 0.80f, 1f);
        [SerializeField] private Color _cardTitleColor   = new Color(0.08f, 0.20f, 0.08f, 1f);
        [SerializeField] private Color _cardOwnerDefault = new Color(0.45f, 0.50f, 0.45f, 1f);
        [SerializeField] private Color _buildSectionBg   = new Color(0.88f, 0.96f, 0.88f, 1f);
        [SerializeField] private Color _buildActiveLbl   = new Color(0.10f, 0.50f, 0.20f, 1f);
        [SerializeField] private Color _btnCommercial    = new Color(0.13f, 0.38f, 0.70f, 1f);
        [SerializeField] private Color _btnEcological    = new Color(0.12f, 0.55f, 0.22f, 1f);

        // Legacy prefab refs — kept so Inspector doesn't lose existing bindings
        [HideInInspector] [SerializeField] private GameObject _groupHeaderPrefab;
        [HideInInspector] [SerializeField] private GameObject _propertyRowPrefab;
        [HideInInspector] [SerializeField] private GameObject _buildSectionPrefab;

        // --- Runtime
        private int  _localPlayerId = -1;
        private bool _isOpen;
        private readonly Dictionary<int, bool>              _glowActive   = new Dictionary<int, bool>();
        private readonly Dictionary<string, List<CardInst>> _cardsByGroup = new Dictionary<string, List<CardInst>>();

        // --- Lifecycle
        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            if (_panel != null) _panel.SetActive(false);
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.UIPropertiesPanelRequested, OnToggleRequested);
            EventBus.On(GameEvent.PropertyPurchased,          OnOwnershipChanged);
            EventBus.On(GameEvent.PropertySold,               OnOwnershipChanged);
            EventBus.On(GameEvent.DistrictBuildingBuilt,      OnOwnershipChanged);
            EventBus.On(GameEvent.DistrictBuildingDestroyed,  OnOwnershipChanged);
            EventBus.On(GameEvent.PropertiesOwnershipChanged, OnOwnershipChanged);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.UIPropertiesPanelRequested, OnToggleRequested);
            EventBus.Off(GameEvent.PropertyPurchased,          OnOwnershipChanged);
            EventBus.Off(GameEvent.PropertySold,               OnOwnershipChanged);
            EventBus.Off(GameEvent.DistrictBuildingBuilt,      OnOwnershipChanged);
            EventBus.Off(GameEvent.DistrictBuildingDestroyed,  OnOwnershipChanged);
            EventBus.Off(GameEvent.PropertiesOwnershipChanged, OnOwnershipChanged);
        }

        public void Initialize(int localPlayerId) => _localPlayerId = localPlayerId;

        // --- Open / Close
        private void OnToggleRequested(object _) => Toggle();

        public void Toggle() { if (_isOpen) Close(); else Open(); }

        public void Open()
        {
            _isOpen = true;
            Rebuild();
            _panel.SetActive(true);
            _panel.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
            _panel.transform.DOScale(Vector3.one, _slideInDuration).SetEase(Ease.OutBack);
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
        }

        public void Close()
        {
            _isOpen = false;
            _panel.transform.DOScale(new Vector3(0.92f, 0.92f, 1f), _slideInDuration * 0.7f)
                .SetEase(Ease.InBack)
                .OnComplete(() => _panel.SetActive(false));
        }

        private void OnOwnershipChanged(object _) { if (_isOpen) Rebuild(); }

        // --- Rebuild
        private void Rebuild()
        {
            foreach (Transform child in _contentRoot) Destroy(child.gameObject);
            _cardsByGroup.Clear();

            var bc = BoardController.Instance;
            var gm = GameManager.Instance;
            if (bc == null || gm == null) return;

            var groups = bc.GetAllPropertiesGrouped()
                .OrderBy(kvp => GetHue(kvp.Value[0].groupColor))
                .ToList();

            bool first = true;
            foreach (var kvp in groups)
            {
                if (!first) AddDivider();
                first = false;

                bool localMonopoly = _localPlayerId >= 0 && bc.HasMonopoly(_localPlayerId, kvp.Key);
                var  building      = bc.GetDistrictBuilding(kvp.Key);

                AddGroupRow(kvp.Key, kvp.Value, localMonopoly, building, bc, gm);

                if (localMonopoly)
                    AddBuildSection(kvp.Key, kvp.Value[0].groupColor, building, bc, gm);
            }

            RebuildGlowButtons(gm);
        }

        // --- Group row
        private void AddGroupRow(string groupId, List<PropertyData> props,
            bool localMonopoly, DistrictBuildingData building,
            BoardController bc, GameManager gm)
        {
            Color color = props[0].groupColor;

            // Alternate row background for readability
            int rowIndex = _contentRoot.childCount;
            Color rowBg = (rowIndex % 2 == 0) ? _rowBgEven : _rowBgOdd;

            // Row root
            var row = NewGO("Row_" + groupId, _contentRoot);
            AddImg(row, rowBg);
            var rowLE = row.AddComponent<LayoutElement>();
            rowLE.minHeight       = 80;
            rowLE.preferredHeight = 80;
            rowLE.flexibleHeight  = 0;
            var rowHLG = row.AddComponent<HorizontalLayoutGroup>();
            rowHLG.spacing                = 0;
            rowHLG.childAlignment         = TextAnchor.MiddleLeft;
            rowHLG.childControlWidth      = true;
            rowHLG.childForceExpandWidth  = false;
            rowHLG.childControlHeight     = true;
            rowHLG.childForceExpandHeight = true;

            // Color label cell — uses the group color but lightened for the light theme
            Color cellBg = new Color(
                Mathf.Lerp(color.r, 1f, 0.30f),
                Mathf.Lerp(color.g, 1f, 0.30f),
                Mathf.Lerp(color.b, 1f, 0.30f),
                1f);
            var cell = NewGO("ColorCell", row.transform);
            AddImg(cell, cellBg);
            var cellLE = cell.AddComponent<LayoutElement>();
            cellLE.minWidth       = 96;
            cellLE.preferredWidth = 96;
            cellLE.flexibleWidth  = 0;
            var cellVLG = cell.AddComponent<VerticalLayoutGroup>();
            cellVLG.padding               = new RectOffset(9, 6, 10, 6);
            cellVLG.spacing               = 4;
            cellVLG.childAlignment        = TextAnchor.UpperLeft;
            cellVLG.childControlWidth     = true;
            cellVLG.childForceExpandWidth = true;
            cellVLG.childControlHeight    = true;
            cellVLG.childForceExpandHeight = false;

            AddLabel(cell.transform, CapFirst(groupId), _fontSizeGroupLabel, FontStyles.Bold,
                GetContrastColor(cellBg), minH: 22, flexH: 0, wrap: TextWrappingModes.NoWrap);

            string badge = building != null
                ? (building.buildingType == DistrictBuildingType.Commercial ? "🏢" : "♻️")
                : (localMonopoly ? "★" : "");
            if (!string.IsNullOrEmpty(badge))
                AddLabel(cell.transform, badge, _fontSizeGroupLabel, FontStyles.Normal,
                    building != null ? GetContrastColor(cellBg) : new Color(0.70f, 0.40f, 0.00f, 1f),
                    minH: 20, flexH: 0);

            // Left color accent separator (vivid group color stripe)
            AddVSep(row.transform, new Color(color.r, color.g, color.b, 0.85f), 3);

            // Property cards
            var cards = new List<CardInst>();
            for (int i = 0; i < props.Count; i++)
            {
                if (i > 0) AddVSep(row.transform, _cardSepColor, 1);
                cards.Add(AddPropertyCard(row.transform, props[i], color, bc, gm));
            }
            _cardsByGroup[groupId] = cards;
        }

        // --- Property card
        private CardInst AddPropertyCard(Transform parent, PropertyData prop,
            Color groupColor, BoardController bc, GameManager gm)
        {
            int   ownerId    = bc.GetOwner(prop.propertyId);
            var   owner      = ownerId >= 0 ? gm.GetPlayer(ownerId) : null;
            Color ownerColor = owner != null ? PlayerColor(owner.PlayerId) : _cardOwnerDefault;

            // Card root
            var card    = NewGO("Card_" + prop.propertyId, parent);
            var cardImg = AddImg(card, _cardBg);
            var cardLE  = card.AddComponent<LayoutElement>();
            cardLE.minWidth       = 90;
            cardLE.preferredWidth = 120;
            cardLE.flexibleWidth  = 1;
            var cardVLG = card.AddComponent<VerticalLayoutGroup>();
            cardVLG.spacing               = 0;
            cardVLG.childAlignment        = TextAnchor.UpperLeft;
            cardVLG.childControlWidth     = true;
            cardVLG.childForceExpandWidth = true;
            cardVLG.childControlHeight    = true;
            cardVLG.childForceExpandHeight = false;

            // 5 px top stripe
            var stripe   = NewGO("Stripe", card.transform);
            AddImg(stripe, new Color(groupColor.r, groupColor.g, groupColor.b, 1f));
            var stripeLE = stripe.AddComponent<LayoutElement>();
            stripeLE.minHeight       = 5;
            stripeLE.preferredHeight = 5;
            stripeLE.flexibleHeight  = 0;

            // Content (fills remaining height)
            var content    = NewGO("Content", card.transform);
            var contentLE  = content.AddComponent<LayoutElement>();
            contentLE.minHeight       = 30;
            contentLE.preferredHeight = 50;
            contentLE.flexibleHeight  = 1;
            var contentVLG = content.AddComponent<VerticalLayoutGroup>();
            contentVLG.padding               = new RectOffset(9, 6, 8, 4);
            contentVLG.spacing               = 4;
            contentVLG.childAlignment        = TextAnchor.UpperLeft;
            contentVLG.childControlWidth     = true;
            contentVLG.childForceExpandWidth = true;
            contentVLG.childControlHeight    = true;
            contentVLG.childForceExpandHeight = false;

            AddLabel(content.transform, prop.displayName, _fontSizeCardTitle, FontStyles.Bold,
                _cardTitleColor, minH: 20, flexH: 0, wrap: TextWrappingModes.NoWrap);
            AddLabel(content.transform, owner != null ? owner.PlayerName : "—",
                _fontSizeCardOwner, FontStyles.Normal, ownerColor, minH: 16, flexH: 0,
                wrap: TextWrappingModes.NoWrap);

            return new CardInst { CardImg = cardImg, DefaultBg = cardImg.color, OwnerId = ownerId };
        }

        // --- Build section
        private void AddBuildSection(string groupId, Color groupColor,
            DistrictBuildingData existing, BoardController bc, GameManager gm)
        {
            bool built = existing != null;

            var section = NewGO("Build_" + groupId, _contentRoot);
            AddImg(section, _buildSectionBg);
            var le = section.AddComponent<LayoutElement>();
            le.minHeight       = built ? 38 : 50;
            le.preferredHeight = built ? 38 : 50;
            le.flexibleHeight  = 0;
            var hlg = section.AddComponent<HorizontalLayoutGroup>();
            hlg.padding               = new RectOffset(102, 10, 6, 6);
            hlg.spacing               = 8;
            hlg.childAlignment        = TextAnchor.MiddleLeft;
            hlg.childControlWidth     = true;
            hlg.childForceExpandWidth  = false;
            hlg.childControlHeight     = true;
            hlg.childForceExpandHeight = true;

            if (built)
            {
                string icon = existing.buildingType == DistrictBuildingType.Commercial ? "🏢" : "♻️";
                var lbl = NewGO("ActiveLabel", section.transform);
                lbl.AddComponent<LayoutElement>().flexibleWidth = 1;
                var tmp = lbl.AddComponent<TextMeshProUGUI>();
                tmp.text      = $"{icon} {existing.displayName} — active";
                tmp.fontSize  = _fontSizeBuildActive;
                tmp.fontStyle = FontStyles.Italic;
                tmp.color     = _buildActiveLbl;
                tmp.raycastTarget = false;
            }
            else
            {
                var commSO  = FindBuildingData(groupId, true);
                var ecoSO   = FindBuildingData(groupId, false);
                int commCost = commSO != null ? commSO.cost : 200;
                int ecoCost  = ecoSO  != null ? ecoSO.cost  : 250;
                int money    = gm.GetPlayer(_localPlayerId)?.Money ?? 0;

                MakeBuildBtn(section.transform, $"🏢 Commercial  M{commCost}",
                    _btnCommercial, money >= commCost,
                    () => OnBuildClicked(groupId, true));
                MakeBuildBtn(section.transform, $"♻️ Ecological  M{ecoCost}",
                    _btnEcological, money >= ecoCost,
                    () => OnBuildClicked(groupId, false));
            }
        }

        private void MakeBuildBtn(Transform parent, string label, Color bg,
            bool interactable, System.Action onClick)
        {
            Color c = interactable ? bg : new Color(bg.r * 0.55f, bg.g * 0.55f, bg.b * 0.55f, 0.6f);
            var go = NewGO("BuildBtn", parent);
            AddImg(go, c);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth       = 160; le.preferredWidth  = 175; le.flexibleWidth  = 0;
            le.minHeight      = 30;  le.preferredHeight = 30;  le.flexibleHeight = 0;
            var btn = go.AddComponent<Button>();
            btn.interactable = interactable;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var lblGO = NewGO("Label", go.transform);
            var rt = lblGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
            var tmp = lblGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = label; tmp.fontSize = _fontSizeBuildBtn;
            tmp.color     = interactable ? Color.white : new Color(0.70f, 0.70f, 0.70f, 1f);
            tmp.alignment = TextAlignmentOptions.Center; tmp.raycastTarget = false;
        }

        private void OnBuildClicked(string groupId, bool isCommercial)
        {
            if (_localPlayerId < 0) return;
            bool net = NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer;

            if (net)
                EcopolyNetworkManager.Instance?.RequestBuildDistrictBuildingServerRpc(groupId, _localPlayerId, isCommercial);
            else
            {
                var player   = GameManager.Instance?.GetPlayer(_localPlayerId);
                var building = FindBuildingData(groupId, isCommercial);
                if (player != null && building != null)
                    BoardController.Instance?.BuildDistrictBuilding(player, groupId, building);
            }
            Rebuild();
        }

        // --- Glow toggles
        private void RebuildGlowButtons(GameManager gm)
        {
            if (_glowButtonsRoot == null || _glowButtonPrefab == null) return;
            foreach (Transform child in _glowButtonsRoot) Destroy(child.gameObject);

            foreach (var player in gm.Players)
            {
                if (player.IsEliminated) continue;
                Color  pc    = PlayerColor(player.PlayerId);
                var    btnGO = Instantiate(_glowButtonPrefab, _glowButtonsRoot);
                var    lbl   = btnGO.GetComponentInChildren<TextMeshProUGUI>();
                if (lbl != null) lbl.text = player.PlayerName;
                var bg = btnGO.GetComponent<Image>();
                if (bg != null) bg.color = new Color(pc.r, pc.g, pc.b, 0.2f);
                var outline = btnGO.transform.Find("Outline")?.GetComponent<Image>();
                if (outline != null) outline.color = pc;
                int pid = player.PlayerId;
                if (!_glowActive.ContainsKey(pid)) _glowActive[pid] = false;
                btnGO.GetComponent<Button>()?.onClick.AddListener(() => ToggleGlow(pid, pc));
            }
        }

        private void ToggleGlow(int playerId, Color pc)
        {
            _glowActive[playerId] = !_glowActive.GetValueOrDefault(playerId, false);
            bool on = _glowActive[playerId];
            Color glowBg = on
                ? new Color(pc.r, pc.g, pc.b, 0.5f)
                : _cardBg;

            foreach (var cards in _cardsByGroup.Values)
                foreach (var card in cards)
                    if (card.OwnerId == playerId && card.CardImg != null)
                        card.CardImg.color = on ? glowBg : card.DefaultBg;
        }

        // --- Procedural UI helpers
        /// <summary>
        /// Creates a GO with a RectTransform anchored at (0,0)→(0,0) — collapsed,
        /// so the parent LayoutGroup (not the anchor system) controls its size.
        /// </summary>
        private static GameObject NewGO(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt       = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot     = new Vector2(0f, 1f);
            rt.sizeDelta = Vector2.zero;
            return go;
        }

        private static Image AddImg(GameObject go, Color color)
        {
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static void AddVSep(Transform parent, Color color, int widthPx)
        {
            var go = NewGO("VSep", parent);
            AddImg(go, color);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = widthPx; le.preferredWidth = widthPx; le.flexibleWidth = 0;
        }

        private static void AddLabel(Transform parent, string text, float size,
            FontStyles style, Color color, float minH, float flexH,
            TextWrappingModes wrap = TextWrappingModes.Normal)
        {
            var go  = NewGO("Lbl", parent);
            var le  = go.AddComponent<LayoutElement>();
            le.minHeight = minH; le.preferredHeight = minH;
            le.flexibleHeight = flexH; le.flexibleWidth = 1;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text             = text;
            tmp.fontSize         = size;
            tmp.fontStyle        = style;
            tmp.color            = color;
            tmp.alignment        = TextAlignmentOptions.Left;
            tmp.textWrappingMode = wrap;
            tmp.raycastTarget    = false;
        }

        private void AddDivider()
        {
            var go = NewGO("Divider", _contentRoot);
            AddImg(go, _dividerColor);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 2; le.preferredHeight = 2; le.flexibleHeight = 0;
        }

        // --- Utilities
        private static float GetHue(Color c)
        {
            Color.RGBToHSV(c, out float h, out _, out _);
            return h;
        }

        private static Color GetContrastColor(Color bg)
        {
            float lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return lum > 0.45f ? new Color(0.06f, 0.06f, 0.06f) : Color.white;
        }

        private static string CapFirst(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        private static readonly Color[] _palette =
        {
            new Color(0.22f, 0.62f, 1.00f),
            new Color(1.00f, 0.45f, 0.20f),
            new Color(0.22f, 0.90f, 0.45f),
            new Color(0.95f, 0.25f, 0.42f),
        };
        private static Color PlayerColor(int id) => _palette[id % _palette.Length];

        private static DistrictBuildingData FindBuildingData(string groupId, bool commercial)
        {
            var type = commercial ? DistrictBuildingType.Commercial : DistrictBuildingType.Ecological;
            return Resources.FindObjectsOfTypeAll<DistrictBuildingData>()
                .FirstOrDefault(b => b.buildingType == type
                    && b.buildingId != null && b.buildingId.Contains(groupId));
        }

        // --- Inner
        private class CardInst
        {
            public Image CardImg;
            public Color DefaultBg;
            public int   OwnerId;
        }
    }
}

