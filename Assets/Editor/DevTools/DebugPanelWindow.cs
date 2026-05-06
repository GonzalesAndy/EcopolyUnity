using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Ecopoly.Core;
using Ecopoly.Utils;
using Ecopoly.Cards;
using Ecopoly.Data;

namespace Ecopoly.DevTools.Editor
{
    public class DebugPanelWindow : EditorWindow
    {
        private int _toolbarIndex = 0;
        private string[] _tabs = { "Player", "Cards", "Events", "Forced Next", "State" };

        // Player tab fields
        private int _selectedPlayerIdx = 0;
        private int _moneyField = 0;
        private int _cepField = 0;
        private int _posField = 0;
        private string _propIdField = "";
        private string[] _allPropertyIds = new string[0];
        private int _propDropdownIdx = 0;

        // Cards tab fields
        private List<ChanceCardData> _chanceCards = new List<ChanceCardData>();
        private List<EventCardData> _eventCards = new List<EventCardData>();
        private int _cardsTargetPlayerIdx = 0;
        private int _chanceCardIdx = 0;
        private int _eventCardIdx = 0;

        // Forced Next tab fields
        private int _forcedCardType = 0; // 0 = Chance, 1 = Event
        private int _forcedChanceCardIdx = 0;
        private int _forcedEventCardIdx = 0;
        private int _forcedNextTile = 0;

        // Events
        private int _eventIdx = 0;
        private string _eventPayload = "";

        // State view
        private Vector2 _stateScroll;
        private string _stateText = "";

        private static readonly (string label, GameEvent ev, string hint)[] _eventDefs =
        {
            ("TurnStarted",             GameEvent.TurnStarted,            "No payload needed"),
            ("TurnEnded",               GameEvent.TurnEnded,              "No payload needed"),
            ("GameStarted",             GameEvent.GameStarted,            "No payload needed"),
            ("GlobalGameOver",          GameEvent.GlobalGameOver,         "No payload needed"),
            ("PlayerEliminated(id)",    GameEvent.PlayerEliminated,       "Payload: player ID (int)"),
            ("PlayerJailed(id)",        GameEvent.PlayerJailed,           "Payload: player ID (int)"),
            ("PlayerReleasedFromJail",  GameEvent.PlayerReleasedFromJail, "Payload: player ID (int)"),
            ("PlayerPassedGo(id)",      GameEvent.PlayerPassedGo,         "Payload: player ID (int)"),
            ("GlobalCEPChanged(n)",     GameEvent.GlobalCEPChanged,       "Payload: int (new global CEP)"),
            ("GlobalCEPThreshold(lvl)", GameEvent.GlobalCEPThresholdChanged, "Payload: int (1-4)"),
            ("DisasterTriggered",       GameEvent.DisasterTriggered,      "Payload: none (fires raw)"),
            ("MoneyChanged",            GameEvent.MoneyChanged,           "Payload: player ID (int)"),
        };

        [MenuItem("Ecopoly/Dev/Debug Panel Window")]
        public static void ShowWindow()
        {
            var w = GetWindow<DebugPanelWindow>("Ecopoly Debug");
            w.minSize = new Vector2(520, 320);
            w.LoadCards();
            w.UpdateStateText();
        }

        private void OnEnable()
        {
            LoadCards();
            LoadProperties();
            UpdateStateText();
        }

        private void LoadCards()
        {
            _chanceCards = new List<ChanceCardData>(
                Resources.LoadAll<ChanceCardData>(Constants.SO_CHANCE_CARDS_FOLDER));
            _eventCards = new List<EventCardData>(
                Resources.LoadAll<EventCardData>(Constants.SO_EVENT_CARDS_FOLDER));
        }

        private void LoadProperties()
        {
            var cfg = Resources.Load<BoardConfig>(Constants.SO_BOARD_CONFIG);
            if (cfg != null && cfg.allProperties != null)
            {
                _allPropertyIds = cfg.allProperties.Select(p => p.propertyId).ToArray();
            }
            else
            {
                _allPropertyIds = new string[0];
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            _toolbarIndex = GUILayout.Toolbar(_toolbarIndex, _tabs);
            EditorGUILayout.Space();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to interact with runtime game state.", MessageType.Info);
            }

            switch (_toolbarIndex)
            {
                case 0: DrawPlayerTab(); break;
                case 1: DrawCardsTab(); break;
                case 2: DrawEventsTab(); break;
                case 3: DrawForcedNextTab(); break;
                case 4: DrawStateTab(); break;
            }
        }

        private void DrawPlayerTab()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                EditorGUILayout.LabelField("GameManager not available.");
                return;
            }

            var names = gm.Players.Select(p => $"[{p.PlayerId}] {p.PlayerName}").ToArray();
            _selectedPlayerIdx = Mathf.Clamp(_selectedPlayerIdx, 0, Mathf.Max(0, names.Length - 1));
            _selectedPlayerIdx = EditorGUILayout.Popup("Player", _selectedPlayerIdx, names);
            var ps = gm.Players[_selectedPlayerIdx];

            EditorGUILayout.BeginHorizontal();
            _moneyField = EditorGUILayout.IntField("Money", _moneyField == 0 ? ps.Money : _moneyField);
            if (GUILayout.Button("Set", GUILayout.Width(60)))
            {
                int delta = _moneyField - ps.Money;
                if (delta >= 0) TurnManager.Instance?.AddMoney(ps, delta);
                else            TurnManager.Instance?.DeductMoney(ps, -delta);
            }
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                TurnManager.Instance?.AddMoney(ps, _moneyField);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _cepField = EditorGUILayout.IntField("CEP", _cepField == 0 ? ps.PersonalCEP : _cepField);
            if (GUILayout.Button("Set", GUILayout.Width(60)))
            {
                int amount = Mathf.Clamp(_cepField, 0, Constants.MAX_PERSONAL_CEP);
                var ctrl = Player.CEPController.GetForPlayer(ps.PlayerId);
                if (ctrl != null)
                {
                    int delta = amount - ps.PersonalCEP;
                    if (delta > 0) ctrl.AddCEP(delta, CEPSource.Bonus);
                    else           ctrl.ReduceCEP(-delta, CEPSource.Bonus);
                }
            }
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                var ctrl = Player.CEPController.GetForPlayer(ps.PlayerId);
                if (ctrl != null)
                {
                    if (_cepField >= 0) ctrl.AddCEP(_cepField, CEPSource.Bonus);
                    else                ctrl.ReduceCEP(-_cepField, CEPSource.Bonus);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _posField = EditorGUILayout.IntField("Position", _posField == 0 ? ps.BoardPosition : _posField);
            if (GUILayout.Button("Teleport", GUILayout.Width(100)))
            {
                int pos = ((_posField % Constants.BOARD_SIZE) + Constants.BOARD_SIZE) % Constants.BOARD_SIZE;
                ps.BoardPosition = pos;
                EventBus.Emit(GameEvent.PlayerMoved, new PlayerMovePayload { PlayerId = ps.PlayerId, NewPosition = pos, IsFinalStep = true });
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Send To Jail"))
            {
                ps.IsInJail = true;
                ps.JailTurnsRemaining = 3;
                ps.BoardPosition = Constants.JAIL_POSITION;
                EventBus.Emit(GameEvent.PlayerJailed, ps.PlayerId);
                EventBus.Emit(GameEvent.PlayerMoved, new PlayerMovePayload { PlayerId = ps.PlayerId, NewPosition = Constants.JAIL_POSITION, IsFinalStep = true });
            }
            if (GUILayout.Button("Free From Jail"))
            {
                ps.IsInJail = false;
                ps.JailTurnsRemaining = 0;
                EventBus.Emit(GameEvent.PlayerReleasedFromJail, ps.PlayerId);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (_allPropertyIds.Length == 0)
            {
                EditorGUILayout.HelpBox("No BoardConfig properties found in Resources. Ensure SO_BoardConfig is present.", MessageType.Warning);
            }
            else
            {
                _propDropdownIdx = Mathf.Clamp(_propDropdownIdx, 0, _allPropertyIds.Length - 1);
                _propDropdownIdx = EditorGUILayout.Popup("Property", _propDropdownIdx, _allPropertyIds);
                string selectedPid = _allPropertyIds[_propDropdownIdx];

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Give", GUILayout.Width(60)))
                {
                    var bc = BoardController.Instance;
                    if (bc == null)
                    {
                        EditorUtility.DisplayDialog("Give Property", "BoardController not available in the running scene.", "OK");
                    }
                    else
                    {
                        bool ok = bc.ForceAssignProperty(ps, selectedPid);
                        if (ok) EditorUtility.DisplayDialog("Give Property", $"Assigned '{selectedPid}' to {ps.PlayerName}.", "OK");
                        else    EditorUtility.DisplayDialog("Give Property", $"Failed to assign '{selectedPid}'.", "OK");
                    }
                }

                if (GUILayout.Button("Strip", GUILayout.Width(60)))
                {
                    bool ok = ps.OwnedPropertyIds.Remove(selectedPid);
                    if (ok)
                    {
                        EventBus.Emit(GameEvent.PropertySold, new PropertyEventPayload { PlayerId = ps.PlayerId, PropertyId = selectedPid });
                        EditorUtility.DisplayDialog("Strip Property", $"Removed '{selectedPid}' from {ps.PlayerName}.", "OK");
                    }
                    else EditorUtility.DisplayDialog("Strip Property", $"{ps.PlayerName} did not own '{selectedPid}'.", "OK");
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Give Get-Out-Of-Jail Card"))
            {
                ps.HasGetOutOfJailCard = true;
                ps.CardHandIds.Add("get_out_jail_01");
                EventBus.Emit(GameEvent.PlayerHandChanged, ps.PlayerId);
            }
        }

        private void DrawCardsTab()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                EditorGUILayout.LabelField("GameManager not available.");
                return;
            }

            var names = gm.Players.Select(p => $"[{p.PlayerId}] {p.PlayerName}").ToArray();
            if (names.Length == 0)
            {
                EditorGUILayout.HelpBox("No players available — game not started.", MessageType.Warning);
                return;
            }
            _cardsTargetPlayerIdx = Mathf.Clamp(_cardsTargetPlayerIdx, 0, names.Length - 1);
            _cardsTargetPlayerIdx = EditorGUILayout.Popup("Target Player", _cardsTargetPlayerIdx, names);
            var ps = gm.Players[_cardsTargetPlayerIdx];

            EditorGUILayout.LabelField("Chance Cards", EditorStyles.boldLabel);
            var chanceNames = _chanceCards.Select(c => $"[{c.cardType}] {c.cardId}").ToArray();
            if (chanceNames.Length == 0)
            {
                EditorGUILayout.HelpBox("No Chance cards loaded.", MessageType.Warning);
            }
            else
            {
                _chanceCardIdx = Mathf.Clamp(_chanceCardIdx, 0, chanceNames.Length - 1);
                _chanceCardIdx = EditorGUILayout.Popup("Chance", _chanceCardIdx, chanceNames);
                if (GUILayout.Button("Force Draw Chance"))
                {
                    var card = _chanceCards[_chanceCardIdx];
                    CardManager.Instance?.ForceDrawChanceCard(ps, card);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Event Cards", EditorStyles.boldLabel);
            var eventNames = _eventCards.Select(c => c.cardId).ToArray();
            if (eventNames.Length == 0)
            {
                EditorGUILayout.HelpBox("No Event cards loaded.", MessageType.Warning);
            }
            else
            {
                _eventCardIdx = Mathf.Clamp(_eventCardIdx, 0, eventNames.Length - 1);
                _eventCardIdx = EditorGUILayout.Popup("Event", _eventCardIdx, eventNames);
                if (GUILayout.Button("Force Draw Event"))
                {
                    var card = _eventCards[_eventCardIdx];
                    CardManager.Instance?.ForceDrawEventCard(ps, card);
                }
            }
        }

        private void DrawEventsTab()
        {
            var eventLabels = _eventDefs.Select(e => e.label).ToArray();
            _eventIdx = Mathf.Clamp(_eventIdx, 0, eventLabels.Length - 1);
            _eventIdx = EditorGUILayout.Popup("Event", _eventIdx, eventLabels);
            EditorGUILayout.LabelField(_eventDefs[_eventIdx].hint);
            _eventPayload = EditorGUILayout.TextField("Payload (int)", _eventPayload);
            if (GUILayout.Button("Fire Event"))
            {
                object payload = null;
                if (!string.IsNullOrEmpty(_eventPayload) && int.TryParse(_eventPayload, out int v)) payload = v;
                EventBus.Emit(_eventDefs[_eventIdx].ev, payload);
            }
        }

        private void DrawForcedNextTab()
        {
            EditorGUILayout.LabelField("Force Next Card Draw", EditorStyles.boldLabel);
            
            var cardTypeOptions = new string[] { "Chance", "Event" };
            _forcedCardType = EditorGUILayout.Popup("Card Type", _forcedCardType, cardTypeOptions);
            
            if (_forcedCardType == 0) // Chance
            {
                var chanceNames = _chanceCards.Select(c => $"[{c.cardType}] {c.cardId}").ToArray();
                if (chanceNames.Length == 0)
                {
                    EditorGUILayout.HelpBox("No Chance cards loaded.", MessageType.Warning);
                }
                else
                {
                    _forcedChanceCardIdx = Mathf.Clamp(_forcedChanceCardIdx, 0, chanceNames.Length - 1);
                    _forcedChanceCardIdx = EditorGUILayout.Popup("Chance Card", _forcedChanceCardIdx, chanceNames);
                    if (GUILayout.Button("Set Forced Next Chance Card"))
                    {
                        var card = _chanceCards[_forcedChanceCardIdx];
                        CardManager.Instance?.SetForcedNextCard(card, isChance: true);
                        EditorUtility.DisplayDialog("Forced Card", $"Next Chance card draw will be: {card.cardId}", "OK");
                    }
                }
            }
            else // Event
            {
                var eventNames = _eventCards.Select(c => c.cardId).ToArray();
                if (eventNames.Length == 0)
                {
                    EditorGUILayout.HelpBox("No Event cards loaded.", MessageType.Warning);
                }
                else
                {
                    _forcedEventCardIdx = Mathf.Clamp(_forcedEventCardIdx, 0, eventNames.Length - 1);
                    _forcedEventCardIdx = EditorGUILayout.Popup("Event Card", _forcedEventCardIdx, eventNames);
                    if (GUILayout.Button("Set Forced Next Event Card"))
                    {
                        var card = _eventCards[_forcedEventCardIdx];
                        CardManager.Instance?.SetForcedNextCard(card, isChance: false);
                        EditorUtility.DisplayDialog("Forced Card", $"Next Event card draw will be: {card.cardId}", "OK");
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Force Next Player Movement", EditorStyles.boldLabel);
            _forcedNextTile = EditorGUILayout.IntSlider("Target Tile (0-39)", _forcedNextTile, 0, 39);
            EditorGUILayout.HelpBox("The next time a player moves (dice roll), they will be moved to this tile after landing resolution.", MessageType.Info);
            
            if (GUILayout.Button("Set Forced Next Tile"))
            {
                TurnManager.Instance?.SetForcedNextTile(_forcedNextTile);
                string tileName = GetTileName(_forcedNextTile);
                EditorUtility.DisplayDialog("Forced Tile", $"Next player movement destination set to: Tile {_forcedNextTile} ({tileName})", "OK");
            }

            if (GUILayout.Button("Clear Forced Next Values"))
            {
                CardManager.Instance?.ClearForcedCard();
                TurnManager.Instance?.ClearForcedNextTile();
                EditorUtility.DisplayDialog("Cleared", "All forced next values cleared.", "OK");
            }
        }

        private string GetTileName(int tilePosition)
        {
            var cfg = Resources.Load<BoardConfig>(Constants.SO_BOARD_CONFIG);
            if (cfg != null && tilePosition >= 0 && tilePosition < cfg.allProperties.Count)
            {
                return cfg.allProperties[tilePosition].propertyId;
            }
            return "Unknown";
        }

        private void DrawStateTab()
        {
            if (GUILayout.Button("Refresh State")) UpdateStateText();
            _stateScroll = EditorGUILayout.BeginScrollView(_stateScroll);
            EditorGUILayout.TextArea(_stateText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void UpdateStateText()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                _stateText = "GameManager not ready. Enter Play mode and run the scene.";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Turn: {(TurnManager.Instance != null ? TurnManager.Instance.CurrentPlayer?.PlayerName ?? "?" : "TM=null")}");
            sb.AppendLine($"GlobalCEP: {gm.GlobalCEP}  Intensity: {gm.CurrentIntensityLevel}");
            sb.AppendLine($"Active players: {gm.ActivePlayerCount}/{gm.Players.Count}");
            sb.AppendLine("─────────────────────────────");

            foreach (var p in gm.Players)
            {
                string status = p.IsEliminated ? "[ELIM]" : p.IsInJail ? "[JAIL]" : "[OK]";
                sb.AppendLine($"{status} {p.PlayerName} (id={p.PlayerId}{(p.IsBot ? ", BOT" : "")})");
                sb.AppendLine($"  M={p.Money}  CEP={p.PersonalCEP}  Pos={p.BoardPosition}");
                sb.AppendLine($"  Properties({p.OwnedPropertyIds.Count}): {string.Join(", ", p.OwnedPropertyIds.Take(8))}{(p.OwnedPropertyIds.Count > 8 ? "…" : "")}\n  Cards: {string.Join(", ", p.CardHandIds)}");
            }
            _stateText = sb.ToString();
        }
    }
}
