using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool meshChannelsExpanded = true;
        private void DrawMeshChannels()
        {
            if (!Document || Active < 0) return;
            var node = Document.nodes[Active];
            GUILayout.Space(4);
            var rect = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.DrawRect(new Rect(rect.x - 4, rect.y, rect.width + 8, rect.height), Color.Lerp(StudioTheme.Current.Panel, StudioTheme.Current.Button, .5f));
            meshChannelsExpanded = DrawChannelCategoryFoldout(rect, meshChannelsExpanded, new GUIContent("Mesh", EditorGUIUtility.IconContent("Mesh Icon").image));
            if (!meshChannelsExpanded) return;
            using (new EditorGUILayout.VerticalScope(StudioTheme.Styles.channelTransformInset))
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Mesh", node.mesh, typeof(Mesh), false);
                if (!node.mesh) EditorGUILayout.LabelField("Empty object (no mesh)");
                else if (GUILayout.Button(new GUIContent("Remove Mesh", "Remove geometry from this object, keeping its transform and children. Supports Undo.")))
                {
                    int index = Active;
                    Run(() => RemoveObjectMesh(index));
                    GUIUtility.ExitGUI();
                }
            }
        }
        internal void RemoveObjectMesh(int index)
        {
            if (!Document || index < 0 || index >= Document.nodes.Count || !Document.nodes[index].mesh) return;
            FinishChannelEdit(); FinishToolsForSwitch(); StopCutTool(); StopTargetWeld(); EndPivotSnap();
            Document.RemoveMesh(index);
            mode = ComponentMode.Object; pivot = false; Components.Clear();
            Rebuild(); SelectObject(index); Changed();
        }
    }
}
