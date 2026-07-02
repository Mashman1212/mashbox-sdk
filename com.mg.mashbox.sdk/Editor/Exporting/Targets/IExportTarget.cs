#if UNITY_EDITOR

namespace MashBoxSDK.Exporting
{
    public interface IExportTarget
    {
        string Name { get; }
        string GetOutputPath();
        void Export(MapExportContext context);
    }
}


#endif