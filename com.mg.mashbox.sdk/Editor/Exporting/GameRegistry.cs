using System;
using System.Collections.Generic;
using UnityEngine;

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
                UnityEditorVersion = "6000.4.10f1",
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

    public static class GameTargetUnityVersionValidator
    {
        public static bool IsValidForPublishing(string gameName, out string message)
        {
            var currentVersion = Application.unityVersion;
            var game = GameRegistry.Find(gameName);

            if (game == null)
            {
                message =
                    $"Publishing is blocked because '{gameName}' is not a registered game target. " +
                    $"Select a supported game target in MashBox Setup and try again. Current Unity Editor: {currentVersion}.";
                return false;
            }

            var requiredVersion = game.UnityEditorVersion?.Trim();
            if (string.IsNullOrEmpty(requiredVersion) ||
                string.Equals(requiredVersion, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                message =
                    $"Publishing to {game.DisplayName} is blocked because its required Unity Editor version has not been configured in the MashBox SDK. " +
                    $"Current Unity Editor: {currentVersion}. Configure GameRegistry.UnityEditorVersion for this target before publishing.";
                return false;
            }

            if (string.Equals(currentVersion, requiredVersion, StringComparison.OrdinalIgnoreCase))
            {
                message = string.Empty;
                return true;
            }

            message =
                $"Publishing to {game.DisplayName} requires Unity Editor {requiredVersion} exactly. " +
                $"This project is running in Unity Editor {currentVersion}. Close the project and reopen it with Unity {requiredVersion}, then publish again.";
            return false;
        }

        public static void ThrowIfInvalidForPublishing(string gameName)
        {
            if (!IsValidForPublishing(gameName, out var message))
                throw new InvalidOperationException(message);
        }
    }
}
