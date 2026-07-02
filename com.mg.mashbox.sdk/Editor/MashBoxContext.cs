using MashBoxSDK.ContentTools.Editor;
using UnityEditor;

namespace MashBoxSDK.SDKMain
{
    public static class MashBoxContext
    {
        public static string CurrentGame =>
            EditorPrefs.GetString("ModIo.CurrentGame", "None");

        public static string ApiBase =>
            EditorPrefs.GetString("ModIo.ApiBase", "");

        public static bool IsAuthorized =>
            ModIoAuth.IsAuthorizedForCurrentGame();
    }
}