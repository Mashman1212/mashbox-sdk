#if UNITY_EDITOR
using System.Collections.Generic;

namespace MashBoxSDK.Exporting
{
    public static class GameTargetResolver
    {
        public static List<GameTarget> GetAllGames()
        {
            var result = new List<GameTarget>();

            foreach (var def in GameRegistry.Games)
            {
                var install = SteamLocator.TryGetGameInstallPath(def.SteamAppId);
                var streaming = string.IsNullOrEmpty(install)
                    ? null
                    : StreamingAssetsResolver.TryResolve(install);

                result.Add(new GameTarget
                {
                    Definition = def,
                    InstallPath = install,                  // null if not installed
                    StreamingAssetsPath = streaming         // null if not resolved
                });
            }

            return result;
        }

        public static string GetStreamingAssetsPath(GameTarget game)
        {
            if (game == null) return null;
            return game.StreamingAssetsPath;
        }
    }
}

#endif