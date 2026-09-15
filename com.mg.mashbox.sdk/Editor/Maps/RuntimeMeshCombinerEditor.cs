using MashBoxSDK.EditorResources;
using UnityEditor;

[CustomEditor(typeof(RuntimeMeshCombiner))]
public sealed class RuntimeMeshCombinerEditor : Editor
{
    private readonly MeshCombinerMaterialPreview materialPreview = new MeshCombinerMaterialPreview();

    public override bool RequiresConstantRepaint() => true;

    public override void OnInspectorGUI()
    {
        MashBoxInspectorHeaderUtility.DrawScriptHeader();
        bool settingsChanged = DrawDefaultInspector();
        RuntimeMeshCombiner combiner = (RuntimeMeshCombiner)target;
        materialPreview.Draw(combiner.GetMaterialPreviewMeshFilters, true, settingsChanged);
    }
}
