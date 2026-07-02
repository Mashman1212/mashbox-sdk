#if UNITY_EDITOR

using MashBoxSDK.Exporting;

namespace MashBoxSDK.Exporting.Targets
{
    public class ProjectXExportTarget : IExportTarget
    {
        public string Name => "ProjectX";

        public string GetOutputPath()
        {
            return SteamLocator.TryGetGameInstallPath(4068320);
        }

        public void Export(MapExportContext context)
        {
            AssetBundleExporter.RunBuildFromExternalTool(context.OutputPath);
        }
    }
}

#endif
