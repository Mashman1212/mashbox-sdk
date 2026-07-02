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
                SteamAppId = 4068320,
                UnityEditorVersion = "Unknown",
                ModIoApiBase = "https://g-12806.modapi.io/v1"
            }
        };
    }
}
