using System;
using System.Collections.Generic;

namespace MashBoxSDK.Exporting
{
    public static class GameRegistry
    {
        public static readonly List<GameDefinition> Games = new()
        {
            new GameDefinition
            {
                DisplayName = "BMXS",
                Aliases = new[] { "BMX Streets" },
                SteamAppId = 871540,
                UnityEditorVersion = "2022.3.62f2",
                ModIoApiBase = "https://g-2835.modapi.io/v1"
            },
            new GameDefinition
            {
                DisplayName = "ScootX",
                SteamAppId = 3800340,
                UnityEditorVersion = "2022.3.62f2",
                ModIoApiBase = "https://g-10073.modapi.io/v1"
            },
            new GameDefinition
            {
                DisplayName = "ProjectX",
                Aliases = new[] { "Project X" },
                SteamAppId = 4068320,
                UnityEditorVersion = "Unknown",
                ModIoApiBase = "https://g-12806.modapi.io/v1",
                MapSharedMemoryBudgetBytes = 4L * 1024L * 1024L * 1024L
            }
        };

        public static GameDefinition Find(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return null;

            foreach (var game in Games)
            {
                if (string.Equals(game.DisplayName, gameName, StringComparison.OrdinalIgnoreCase))
                    return game;

                if (game.Aliases == null)
                    continue;

                foreach (var alias in game.Aliases)
                {
                    if (string.Equals(alias, gameName, StringComparison.OrdinalIgnoreCase))
                        return game;
                }
            }

            return null;
        }
    }
}
