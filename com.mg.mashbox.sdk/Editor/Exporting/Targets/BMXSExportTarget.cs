#if UNITY_EDITOR
namespace MashBoxSDK.Exporting.Targets
{
    public class BMXSExportTarget : IExportTarget
    {
        public string Name => "BMXS";

        public string GetOutputPath()
        {
            return MashBoxSDK.Exporting.SteamLocator.TryGetGameInstallPath(871540);
        }

        public void Export(MapExportContext context)
        {
            AssetBundleExporter.RunBuildFromExternalTool(context.OutputPath);
        }
    }
}

#endif