using System.Collections.Generic;
using Ecopoly.Core;
using Ecopoly.Data;

/// <summary>
/// Plain data container holding the player configuration chosen in the
/// PlayTestBootstrap EditorWindow. Passed to PlayTestBootstrapRuntime at
/// scene-injection time, then used to call GameManager.InitGame().
/// Lives in runtime assembly so it's accessible from both Editor and runtime.
/// </summary>
public class PlayTestConfig
{
    public string    LocalPlayerName;
    public int       TotalPlayers;
    public BotSlot[] BotSlots = System.Array.Empty<BotSlot>();

    /// <summary>
    /// When true, all players are bots — no human slot is created.
    /// The viewer is a pure spectator.
    /// </summary>
    public bool AllBotsMode;

    public class BotSlot
    {
        public string             Name;
        public BotPersonalityData Personality;
    }

    /// <summary>
    /// Builds the List&lt;PlayerState&gt; expected by GameManager.InitGame().
    /// In AllBotsMode every slot is a bot; otherwise slot 0 is the human local player.
    /// </summary>
    public List<PlayerState> BuildPlayerList()
    {
        var players = new List<PlayerState>(TotalPlayers);

        if (!AllBotsMode)
        {
            // Human player always at index 0
            players.Add(new PlayerState
            {
                PlayerId            = 0,
                AnimalIndex         = 0,
                PlayerName          = LocalPlayerName,
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

        // Bot players
        int botCount = AllBotsMode
            ? System.Math.Min(BotSlots.Length, TotalPlayers)
            : System.Math.Min(BotSlots.Length, TotalPlayers - 1);

        for (int i = 0; i < botCount; i++)
        {
            BotSlot slot = BotSlots[i];
            int playerId = AllBotsMode ? i : i + 1;
            players.Add(new PlayerState
            {
                PlayerId            = playerId,
                AnimalIndex         = playerId % 5,   // distribute animals across the roster
                PlayerName          = slot.Name,
                IsBot               = true,
                BotPersonality      = slot.Personality,
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
}
