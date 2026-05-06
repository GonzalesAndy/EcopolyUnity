namespace Ecopoly.Utils
{
    /// <summary>
    /// Project-wide global constants.
    /// No gameplay numeric values should be hardcoded elsewhere.
    /// Gameplay values (CEP, prices, rents) live in ScriptableObjects.
    /// </summary>
    public static class Constants
    {
        // --- Gameplay ---
        public const int MIN_PLAYERS          = 3;
        public const int MAX_PLAYERS          = 5;
        public const int BOARD_SIZE           = 40;      // number of tiles
        public const int GO_REWARD            = 200;     // money received when passing GO
        public const int JAIL_POSITION        = 10;      // index of Jail tile
        public const int GO_TO_JAIL_POSITION  = 30;      // index of "Go to Jail" tile
        public const int MAX_JAIL_TURNS       = 3;
        public const int JAIL_BAIL_COST       = 50;
        public const int MAX_PERSONAL_CEP     = 1500;

        // --- CEP thresholds per player count [index = player count - 3] ---
        // Layout: [level1_max, level2_max, level3_max, level4_max, gameOver]
        public static readonly int[,] CEP_THRESHOLDS = new int[,]
        {
            // 3 players
            { 999, 1699, 2199, 2499, 2500 },
            // 4 players
            { 1349, 2319, 2999, 3400, 3401 },
            // 5 players
            { 1679, 2859, 3719, 4200, 4201 },
        };

        // --- Renovation ---
        public const int MIN_RENOVATION_LEVEL = 1;
        public const int MAX_RENOVATION_LEVEL = 4;

        // --- Dice ---
        public const int DICE_SIDES           = 6;
        public const int DOUBLE_JAIL_COUNT    = 3;   // consecutive doubles before jail

        // --- Cards ---
        public const int CHANCE_DECK_SIZE     = 30;
        public const int MOVE_CARDS_PER_TYPE  = 2;   // 2 bike, 2 car, 2 plane

        // --- Stations ---
        public static readonly int[] STATION_RENTS = { 0, 25, 50, 100, 200 };
        // index = number of stations owned by the same player

        // --- Property Sale ---
        public const float SELL_RATIO         = 0.5f; // 50% of purchase price

        // --- Starting Money ---
        public const int STARTING_MONEY       = 1500;

        // --- Camera ---
        public const float CAMERA_SWITCH_BLEND    = 0.5f;  // seconds
        public const float FPS_CAMERA_HEIGHT      = 0.05f; // pawn eye height in Unity units
        public const float TOPDOWN_CAMERA_HEIGHT  = 20f;

        // --- Voice Chat ---
        public const float VOICE_MAX_DISTANCE     = 8f;    // Unity units
        public const float VOICE_MIN_DISTANCE     = 1f;

        // --- Layers ---
        public const string LAYER_BOARD           = "Board";
        public const string LAYER_PLAYERS         = "Players";
        public const string LAYER_UI_WORLD        = "WorldUI";

        // --- Tags ---
        public const string TAG_BOARD_TILE        = "BoardTile";
        public const string TAG_PLAYER_PAWN       = "PlayerPawn";

        // --- Addressables Keys ---
        public const string VFX_KEY_HURRICANE     = "VFX_Hurricane";
        public const string VFX_KEY_FLOOD         = "VFX_Flood";
        public const string VFX_KEY_FIRE          = "VFX_Fire";
        public const string VFX_KEY_DROUGHT       = "VFX_Drought";
        public const string VFX_KEY_HEATWAVE      = "VFX_Heatwave";
        public const string VFX_KEY_LIGHTNING     = "VFX_Lightning";
        public const string VFX_KEY_EARTHQUAKE    = "VFX_Earthquake";
        public const string VFX_KEY_VOLCANO       = "VFX_Volcano";
        public const string VFX_KEY_COASTAL_FLOOD = "VFX_CoastalFlood";

        // --- ScriptableObject Resource Paths ---
        public const string SO_GAME_SETTINGS      = "Settings/GameSettings";
        public const string SO_BOARD_CONFIG       = "Board/BoardConfig";
        public const string SO_CHANCE_CARDS_FOLDER = "Cards/Chance";
        public const string SO_EVENT_CARDS_FOLDER  = "Cards/Event";
    }
}
