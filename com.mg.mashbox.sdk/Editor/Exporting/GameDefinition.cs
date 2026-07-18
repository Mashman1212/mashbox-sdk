namespace MashBoxSDK.Exporting
{
    public class GameDefinition
    {
        public const long DefaultMapSharedMemoryBudgetBytes = 2L * 1024L * 1024L * 1024L;

        public string DisplayName;
        public string[] Aliases;
        public long SteamAppId;
        public string UnityEditorVersion;
        public string ModIoApiBase;
        public long MapSharedMemoryBudgetBytes = DefaultMapSharedMemoryBudgetBytes;
    }
}
