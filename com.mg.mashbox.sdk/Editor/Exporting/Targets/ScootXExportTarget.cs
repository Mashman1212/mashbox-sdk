#if UNITY_EDITOR

using MashBoxSDK.Exporting;

namespace MashBoxSDK.Exporting.Targets
{
    public class ScootXExportTarget : IExportTarget
    {
        public string Name => "ScootX";

        public string GetOutputPath()
        {
            return SteamLocator.TryGetGameInstallPath(3800340); 
        }

        public void Export(MapExportContext context)
        {
            AssetBundleExporter.RunBuildFromExternalTool(context.OutputPath);
        }
    }
}

#endif