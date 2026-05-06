using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Ecopoly.Core;
using Ecopoly.Data;

namespace Ecopoly.Editor
{
    /// <summary>
    /// Editor window that configures players and calls GameManager.InitGame()
    /// directly in Play Mode, bypassing the Bootstrap → MainMenu → Lobby flow.
    /// Open via  Ecopoly > Dev > Game Bootstrap
    /// </summary>
    public class PlayTestBootstrap : EditorWindow
    {
        // ── Constants ────────────────────────────────────────────────────────
        private const string WINDOW_TITLE        = "Game Bootstrap";
        private const string PREFS_PREFIX        = "Ecopoly.PlayTestBootstrap.";
        private const string PREFS_COUNT         = PREFS_PREFIX + "PlayerCount";
        private const string PREFS_NAME          = PREFS_PREFIX + "LocalName";
        private const string PREFS_ALLBOTS       = PREFS_PREFIX + "AllBotsMode";
        private const string PREFS_ALLBOTS_COUNT = PREFS_PREFIX + "AllBotsCount";
        private const string SCENE_NAME          = "GameBoard";
        private const string SCENE_BOTS_ENV      = "GameBoardEnv";
        private const int    MAX_BOT_SLOTS       = 4; // MAX_PLAYERS - 1 human

        // ── Serialized state ─────────────────────────────────────────────────
        private int    _playerCount    = 3;
        private string _localName      = "Player";

        // Bots-only (spectator) mode
        private bool _allBotsMode      = false;
        private int  _allBotsCount     = 4;

        // Per-bot slot data (index 0 = Bot 1 … index 3 = Bot 4)
        private readonly string[]            _botNames        = new string[MAX_BOT_SLOTS];
        private readonly BotPersonalityData[] _botPersonalities = new BotPersonalityData[MAX_BOT_SLOTS];

        // ── Cached assets ────────────────────────────────────────────────────
        private BotPersonalityData[] _availablePersonalities;
        private string[]             _personalityNames;

        // ── Layout ───────────────────────────────────────────────────────────
        private Vector2 _scroll;
        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private bool     _stylesBuilt;

        // ────────────────────────────────────────────────────────────────────
        [MenuItem("Ecopoly/Dev/Game Bootstrap")]
        public static void Open()
            => GetWindow<PlayTestBootstrap>(WINDOW_TITLE);

        // ────────────────────────────────────────────────────────────────────
        private void OnEnable()
        {
            _playerCount  = EditorPrefs.GetInt(PREFS_COUNT, 3);
            _localName    = EditorPrefs.GetString(PREFS_NAME, "Player");
            _allBotsMode  = EditorPrefs.GetBool(PREFS_ALLBOTS, false);
            _allBotsCount = EditorPrefs.GetInt(PREFS_ALLBOTS_COUNT, 4);
            RefreshPersonalities();
            RestoreBotDefaults();
        }

        private void RefreshPersonalities()
        {
            string[] guids = AssetDatabase.FindAssets("t:BotPersonalityData");
            var list = new List<BotPersonalityData>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<BotPersonalityData>(path);
                if (asset != null)
                    list.Add(asset);
            }
            _availablePersonalities = list.ToArray();

            _personalityNames = new string[_availablePersonalities.Length + 1];
            _personalityNames[0] = "— None —";
            for (int i = 0; i < _availablePersonalities.Length; i++)
                _personalityNames[i + 1] = _availablePersonalities[i].botName;
        }

        private void RestoreBotDefaults()
        {
            for (int i = 0; i < MAX_BOT_SLOTS; i++)
            {
                if (string.IsNullOrEmpty(_botNames[i]))
                    _botNames[i] = $"Bot {i + 1}";
            }
        }

        // ────────────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            BuildStyles();
            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawHeader();
            EditorGUILayout.Space(6);
            DrawAllBotsModeSection();
            EditorGUILayout.Space(4);

            if (!_allBotsMode)
            {
                DrawPlayerCountSection();
                EditorGUILayout.Space(4);
                DrawLocalPlayerSection();
                EditorGUILayout.Space(4);
                DrawBotsSection();
            }
            else
            {
                DrawAllBotsSection();
            }

            EditorGUILayout.Space(10);
            DrawLaunchSection();

            GUILayout.EndScrollView();
        }

        // ── Header ───────────────────────────────────────────────────────────
        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Ecopoly — Game Bootstrap", _headerStyle);
            EditorGUILayout.LabelField(
                "Starts the GameBoard scene directly in Play Mode, bypassing Bootstrap / Lobby.",
                EditorStyles.wordWrappedMiniLabel);
        }

        // ── All-bots mode toggle ──────────────────────────────────────────────
        private void DrawAllBotsModeSection()
        {
            DrawSectionLabel("Mode");
            EditorGUI.BeginChangeCheck();
            _allBotsMode = EditorGUILayout.Toggle(
                new GUIContent("Bots-only spectator",
                    "Launch GameBoardEnv with only bots. No human player — just watch them play."),
                _allBotsMode);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetBool(PREFS_ALLBOTS, _allBotsMode);
        }

        // ── All-bots slot configuration ───────────────────────────────────────
        private void DrawAllBotsSection()
        {
            DrawSectionLabel("Bots (spectator mode)");

            EditorGUI.BeginChangeCheck();
            _allBotsCount = EditorGUILayout.IntSlider("Number of bots", _allBotsCount,
                Ecopoly.Utils.Constants.MIN_PLAYERS, Ecopoly.Utils.Constants.MAX_PLAYERS);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetInt(PREFS_ALLBOTS_COUNT, _allBotsCount);

            EditorGUILayout.Space(2);

            for (int i = 0; i < _allBotsCount; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Bot {i + 1}", EditorStyles.boldLabel);

                _botNames[i] = EditorGUILayout.TextField("Name", _botNames[i]);

                int currentIdx = PersonalityIndex(_botPersonalities[i]);
                int newIdx     = EditorGUILayout.Popup("Personality", currentIdx, _personalityNames);
                _botPersonalities[i] = newIdx == 0 ? null : _availablePersonalities[newIdx - 1];

                if (_botPersonalities[i] != null)
                    DrawPersonalityPreview(_botPersonalities[i]);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
        }

        // ── Player count ─────────────────────────────────────────────────────
        private void DrawPlayerCountSection()
        {
            DrawSectionLabel("Players");
            EditorGUI.BeginChangeCheck();
            _playerCount = EditorGUILayout.IntSlider("Total players", _playerCount,
                Ecopoly.Utils.Constants.MIN_PLAYERS, Ecopoly.Utils.Constants.MAX_PLAYERS);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetInt(PREFS_COUNT, _playerCount);
        }

        // ── Local player ─────────────────────────────────────────────────────
        private void DrawLocalPlayerSection()
        {
            DrawSectionLabel("Local player  (human, ID 0)");
            EditorGUI.BeginChangeCheck();
            _localName = EditorGUILayout.TextField("Name", _localName);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PREFS_NAME, _localName);
        }

        // ── Bots ─────────────────────────────────────────────────────────────
        private void DrawBotsSection()
        {
            int botCount = _playerCount - 1;
            if (botCount <= 0) return;

            DrawSectionLabel($"Bots  ({botCount} slot{(botCount > 1 ? "s" : "")})");

            for (int i = 0; i < botCount; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Bot {i + 1}", EditorStyles.boldLabel);

                _botNames[i] = EditorGUILayout.TextField("Name", _botNames[i]);

                // Personality popup
                int currentIdx = PersonalityIndex(_botPersonalities[i]);
                int newIdx = EditorGUILayout.Popup("Personality", currentIdx, _personalityNames);
                _botPersonalities[i] = newIdx == 0 ? null : _availablePersonalities[newIdx - 1];

                if (_botPersonalities[i] != null)
                    DrawPersonalityPreview(_botPersonalities[i]);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
        }

        private void DrawPersonalityPreview(BotPersonalityData p)
        {
            using (new EditorGUI.IndentLevelScope(1))
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.FloatField("Ecological awareness", p.ecologicalAwareness);
                EditorGUILayout.FloatField("Risk tolerance",       p.riskTolerance);
                EditorGUILayout.FloatField("Aggressiveness",       p.aggressiveness);
                EditorGUILayout.FloatField("Cooperation",          p.cooperation);
                EditorGUI.EndDisabledGroup();
            }
        }

        // ── Launch ───────────────────────────────────────────────────────────
        private void DrawLaunchSection()
        {
            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Already in Play Mode.", MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Re-init game now", GUILayout.Height(32)))
                        InjectIntoRunningGame();

                    if (GUILayout.Button("Stop", GUILayout.Height(32)))
                        EditorApplication.ExitPlaymode();
                }
            }
            else
            {
                if (_allBotsMode)
                {
                    EditorGUILayout.HelpBox(
                        $"Will launch '{SCENE_BOTS_ENV}' with {_allBotsCount} bots only — no human player. " +
                        "You are a pure spectator.",
                        MessageType.Info);

                    if (GUILayout.Button("▶  Watch Bots Play (GameBoardEnv)", GUILayout.Height(36)))
                        Launch(SCENE_BOTS_ENV);
                }
                else
                {
                    bool gameBoardLoaded = UnityEngine.SceneManagement.SceneManager
                        .GetActiveScene().name == SCENE_NAME;

                    if (!gameBoardLoaded)
                    {
                        EditorGUILayout.HelpBox(
                            $"Active scene is not '{SCENE_NAME}'. Open it first or the tool will open it for you.",
                            MessageType.Warning);
                    }

                    if (GUILayout.Button("▶  Launch GameBoard", GUILayout.Height(36)))
                        Launch(SCENE_NAME);
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Refresh personalities"))
                        RefreshPersonalities();
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        private void Launch(string targetScene)
        {
            // Save dirty scenes
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            // Switch to target scene if not already there
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != targetScene)
            {
                string[] guids = AssetDatabase.FindAssets($"{targetScene} t:Scene");
                if (guids.Length == 0)
                {
                    Debug.LogError($"[PlayTestBootstrap] Scene '{targetScene}' not found in project.");
                    return;
                }
                string scenePath = AssetDatabase.GUIDToAssetPath(guids[0]);
                EditorSceneManager.OpenScene(scenePath);
            }

            InjectRuntimeBootstrapper();
            EditorApplication.EnterPlaymode();
        }

        // Injects (or refreshes) the PlayTestBootstrapRuntime MonoBehaviour
        // that will call GameManager.InitGame() after Awake.
        private void InjectRuntimeBootstrapper()
        {
            var existing = GameObject.Find("PlayTestBootstrapRuntime");
            if (existing != null)
                DestroyImmediate(existing);

            var go = new GameObject("PlayTestBootstrapRuntime");
            var runtime = go.AddComponent<PlayTestBootstrapRuntime>();
            runtime.Configure(BuildConfig());

            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log("[PlayTestBootstrap] Injected PlayTestBootstrapRuntime.");
        }

        // Calls InitGame on an already-running GameManager (hot-reinit).
        private void InjectIntoRunningGame()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogError("[PlayTestBootstrap] GameManager.Instance is null.");
                return;
            }
            GameManager.Instance.InitGame(BuildConfig().BuildPlayerList());
            Debug.Log("[PlayTestBootstrap] Hot re-init: InitGame called.");
        }

        // ────────────────────────────────────────────────────────────────────
        private PlayTestConfig BuildConfig()
        {
            if (_allBotsMode)
            {
                var cfg = new PlayTestConfig
                {
                    AllBotsMode  = true,
                    TotalPlayers = _allBotsCount,
                };

                cfg.BotSlots = new PlayTestConfig.BotSlot[_allBotsCount];
                for (int i = 0; i < _allBotsCount; i++)
                {
                    cfg.BotSlots[i] = new PlayTestConfig.BotSlot
                    {
                        Name        = string.IsNullOrWhiteSpace(_botNames[i]) ? $"Bot {i + 1}" : _botNames[i],
                        Personality = _botPersonalities[i]
                    };
                }
                return cfg;
            }
            else
            {
                var cfg = new PlayTestConfig
                {
                    LocalPlayerName = string.IsNullOrWhiteSpace(_localName) ? "Player" : _localName,
                    TotalPlayers    = _playerCount,
                    AllBotsMode     = false,
                };

                int botCount = _playerCount - 1;
                cfg.BotSlots = new PlayTestConfig.BotSlot[botCount];
                for (int i = 0; i < botCount; i++)
                {
                    cfg.BotSlots[i] = new PlayTestConfig.BotSlot
                    {
                        Name        = string.IsNullOrWhiteSpace(_botNames[i]) ? $"Bot {i + 1}" : _botNames[i],
                        Personality = _botPersonalities[i]
                    };
                }
                return cfg;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private int PersonalityIndex(BotPersonalityData p)
        {
            if (p == null) return 0;
            for (int i = 0; i < _availablePersonalities.Length; i++)
                if (_availablePersonalities[i] == p) return i + 1;
            return 0;
        }

        private void DrawSectionLabel(string label)
        {
            EditorGUILayout.LabelField(label, _sectionStyle);
            var r = GUILayoutUtility.GetLastRect();
            r.yMin = r.yMax;
            r.height = 1;
            EditorGUI.DrawRect(r, new Color(0.4f, 0.4f, 0.4f, 0.5f));
            EditorGUILayout.Space(2);
        }

        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 14,
                alignment = TextAnchor.MiddleLeft
            };
            _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11
            };
        }
    }
}
