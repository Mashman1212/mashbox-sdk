
namespace MashBoxSDK.EditorResources
{
    public static class MashBoxEditorResources
    {
        private const string ROOT = "Packages/com.mg.mashbox.sdk/EditorResources/";

        public const string HEADER = ROOT + "SDK_Header.png";
        public const string SCRIPT_HEADER = ROOT + "Script_Header.png";
        public const string BMXS = ROOT + "BMXS_Logo.png";
        public const string SCOOTX = ROOT + "ScootX_Logo.png";
        public const string MODIO = ROOT + "modio_Logo.png";


        public static string GetGameLogo(string displayName)
        {
            return $"{ROOT}{displayName}_Logo.png";
        }
    }
}
