using System.Collections.Generic;
using UnityEngine;
using Ecopoly.Core;
using Ecopoly.Player;
using Ecopoly.Data;
using Ecopoly.Utils;

/// <summary>
/// Injected into the GameBoard scene by PlayTestBootstrap (Editor window) before
/// entering Play Mode. Spawns one PRF_Player per configured slot, then calls
/// GameManager.InitGame() — bypassing the Bootstrap → Lobby flow entirely.
///
/// All configuration is stored as [SerializeField] fields so it survives Unity's
/// domain reload when transitioning from Edit Mode to Play Mode.
/// </summary>
public class PlayTestBootstrapRuntime : MonoBehaviour
{
    // --- Player colours (by slot index)
    private static readonly Color[] PlayerColors =
    {
        new Color(0.20f, 0.60f, 1.00f), // 0  blue
        new Color(1.00f, 0.30f, 0.30f), // 1  red
        new Color(0.30f, 1.00f, 0.40f), // 2  green
        new Color(1.00f, 0.85f, 0.10f), // 3  yellow
        new Color(0.80f, 0.30f, 1.00f), // 4  purple
    };

    private const string PLAYER_PREFAB_ASSETS_PATH = "Assets/Prefabs/Players/PRF_Player.prefab";
    private const string PLAYERS_ROOT_NAME          = "Players_Root";

    // --- Serialized config — survives Edit→Play domain reload
    [SerializeField] private bool   _allBotsMode;
    [SerializeField] private int    _totalPlayers;
    [SerializeField] private string _localPlayerName;
    [SerializeField] private string[]             _botNames;
    [SerializeField] private BotPersonalityData[] _botPersonalities;

    // --- Called by PlayTestBootstrap (Editor window)
    public void Configure(PlayTestConfig config)
    {
        _allBotsMode      = config.AllBotsMode;
        _totalPlayers     = config.TotalPlayers;
        _localPlayerName  = config.LocalPlayerName;

        int slotCount     = config.BotSlots?.Length ?? 0;
        _botNames         = new string[slotCount];
        _botPersonalities = new BotPersonalityData[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            _botNames[i]         = config.BotSlots[i].Name;
            _botPersonalities[i] = config.BotSlots[i].Personality;
        }
    }

    // --- Entry point
    private void Start()
    {
        var net = Unity.Netcode.NetworkManager.Singleton;
        if (net != null && net.IsListening)
        {
            Debug.Log("[PlayTestBootstrapRuntime] Live network session detected — skipping PlayTest bootstrap.");
            Destroy(gameObject);
            return;
        }

        DisableNetwork();

        if (_allBotsMode)
            EnterSpectatorMode();
        else
            UnlockCursor();

        var gm = EnsureGameManager();
        if (gm == null)
        {
            Debug.LogError("[PlayTestBootstrapRuntime] Could not find or create GameManager. Aborting.");
            return;
        }

        var players = BuildPlayerList();
        EnsureBotPersonalities(players);

        // Clear stale player GOs from a previous play session.
        var rootGO = GameObject.Find(PLAYERS_ROOT_NAME);
        if (rootGO != null)
        {
            for (int i = rootGO.transform.childCount - 1; i >= 0; i--)
                Destroy(rootGO.transform.GetChild(i).gameObject);
        }

        // Spawn GOs first so InitializeScenePlayers can find and fully configure them
        // (animal model, BotBrain.Initialize, camera registration).
        // GameManager.SpawnOfflinePlayers detects existing controllers and skips.
        SpawnPlayerObjects(players);

        gm.InitGame(players);

        Debug.Log($"[PlayTestBootstrapRuntime] InitGame — {BuildSummary(players)}");
    }

    // --- Build player list from serialized fields
    private List<PlayerState> BuildPlayerList()
    {
        var players  = new List<PlayerState>(_totalPlayers);
        int slotCount = _botNames?.Length ?? 0;

        if (!_allBotsMode)
        {
            players.Add(new PlayerState
            {
                PlayerId            = 0,
                AnimalIndex         = 0,
                PlayerName          = string.IsNullOrWhiteSpace(_localPlayerName) ? "Player" : _localPlayerName,
                IsBot               = false,
                IsEliminated        = false,
                PersonalCEP         = 0,
                BoardPosition       = 0,
                JailTurnsRemaining  = 0,
                IsInJail            = false,
                ConsecutiveDoubles  = 0,
                HasGetOutOfJailCard = false,
            });
        }

        int botCount = _allBotsMode
            ? System.Math.Min(slotCount, _totalPlayers)
            : System.Math.Min(slotCount, _totalPlayers - 1);

        for (int i = 0; i < botCount; i++)
        {
            int playerId = _allBotsMode ? i : i + 1;
            players.Add(new PlayerState
            {
                PlayerId            = playerId,
                AnimalIndex         = playerId % 5,
                PlayerName          = (i < slotCount && !string.IsNullOrWhiteSpace(_botNames[i]))
                                          ? _botNames[i]
                                          : $"Bot {i + 1}",
                IsBot               = true,
                BotPersonality      = (i < (_botPersonalities?.Length ?? 0)) ? _botPersonalities[i] : null,
                IsEliminated        = false,
                PersonalCEP         = 0,
                BoardPosition       = 0,
                JailTurnsRemaining  = 0,
                IsInJail            = false,
                ConsecutiveDoubles  = 0,
                HasGetOutOfJailCard = false,
            });
        }

        return players;
    }

    // --- Spawn one PlayerController GO per player slot
    private static void SpawnPlayerObjects(List<PlayerState> players)
    {
        var rootGO = GameObject.Find(PLAYERS_ROOT_NAME);
        Transform root = rootGO != null ? rootGO.transform : new GameObject(PLAYERS_ROOT_NAME).transform;

        GameObject prefab = LoadPlayerPrefab();
        if (prefab == null)
        {
            Debug.LogError($"[PlayTestBootstrapRuntime] PRF_Player prefab not found at '{PLAYER_PREFAB_ASSETS_PATH}'.");
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        for (int i = 0; i < players.Count; i++)
        {
            var go = Instantiate(prefab, root);
            go.name = $"Player_{players[i].PlayerId}_{players[i].PlayerName}";
            go.transform.localPosition = new Vector3(i * 0.1f, 0f, 0f);

            var pc = go.GetComponent<PlayerController>();
            if (pc != null)
            {
                pc.PlayerId = players[i].PlayerId;
                pc.SetPlayerColor(PlayerColors[i % PlayerColors.Length]);
            }

            var bot = go.GetComponent<Ecopoly.AI.BotBrain>();
            if (bot != null)
                bot.enabled = players[i].IsBot;
        }

        Debug.Log($"[PlayTestBootstrapRuntime] Spawned {players.Count} player GO(s) under '{PLAYERS_ROOT_NAME}'.");
    }

    private static GameObject LoadPlayerPrefab()
    {
#if UNITY_EDITOR
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PREFAB_ASSETS_PATH);
        if (asset != null) return asset;
#endif
        return Resources.Load<GameObject>("Players/PRF_Player");
    }

    // --- Bot personalities
    private static void EnsureBotPersonalities(List<PlayerState> players)
    {
        if (players == null || players.Count == 0) return;

        var available = LoadAvailableBotPersonalities();
        if (available.Count == 0)
        {
            Debug.LogWarning("[PlayTestBootstrapRuntime] No BotPersonalityData assets found. Bots will use default AI behavior.");
            return;
        }

        int assigned = 0;
        foreach (var p in players)
        {
            if (!p.IsBot || p.BotPersonality != null) continue;
            p.BotPersonality = available[assigned % available.Count];
            assigned++;
        }

        if (assigned > 0)
            Debug.Log($"[PlayTestBootstrapRuntime] Auto-assigned personality to {assigned} bot(s).");
    }

    private static List<BotPersonalityData> LoadAvailableBotPersonalities()
    {
        var personalities = new List<BotPersonalityData>();

#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:BotPersonalityData");
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<BotPersonalityData>(path);
            if (asset != null && !personalities.Contains(asset))
                personalities.Add(asset);
        }
#endif

        if (personalities.Count == 0)
        {
            foreach (var asset in Resources.LoadAll<BotPersonalityData>(string.Empty))
                if (asset != null && !personalities.Contains(asset))
                    personalities.Add(asset);
        }

        return personalities;
    }

    // --- GameManager bootstrap
    private static GameManager EnsureGameManager()
    {
        if (GameManager.Instance != null) return GameManager.Instance;

        var existing = FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Debug.Log("[PlayTestBootstrapRuntime] Found in-scene GameManager.");
            return existing;
        }

        var gm = new GameObject("GameManager [PlayTest]").AddComponent<GameManager>();
        Debug.Log("[PlayTestBootstrapRuntime] Created GameManager at runtime.");
        return gm;
    }

    // --- Cursor
    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private static void EnterSpectatorMode()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        EventBus.Emit(GameEvent.SpectatorModeStarted);
        Debug.Log("[PlayTestBootstrapRuntime] Spectator mode active — all bots, no human player.");
    }

    // --- Network
    private void DisableNetwork()
    {
        foreach (var nm in FindObjectsByType<Unity.Netcode.NetworkManager>(FindObjectsSortMode.None))
        {
            nm.gameObject.SetActive(false);
            Debug.Log("[PlayTestBootstrapRuntime] NetworkManager disabled.");
        }
    }

    // --- Debug hotkeys
#if ENABLE_INPUT_SYSTEM
    private void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        if (kb.rKey.wasPressedThisFrame && TurnManager.Instance != null)
        {
            TurnManager.Instance.RollDice();
            Debug.Log("[PlayTestBootstrapRuntime] R → RollDice()");
        }

        if (kb.escapeKey.wasPressedThisFrame)
            UnlockCursor();
    }
#endif

    // --- Logging
    private static string BuildSummary(List<PlayerState> players)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in players)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(p.IsBot ? $"[Bot] {p.PlayerName}" : $"[Human] {p.PlayerName}");
        }
        return sb.ToString();
    }
}

