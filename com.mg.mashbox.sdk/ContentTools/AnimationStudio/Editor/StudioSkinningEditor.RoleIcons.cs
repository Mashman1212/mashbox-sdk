using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private static void AssignDefaultMeshRole(StudioSkinningDocument.Part part)
        {
            var defaults=AssetDatabase.LoadAssetAtPath<StudioSkinningDefaults>("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if(!defaults || !part.sourceMesh) return;
            bool Contains(GameObject root) => root && (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r=>r.sharedMesh==part.sourceMesh)
                || root.GetComponentsInChildren<MeshFilter>(true).Any(r=>r.sharedMesh==part.sourceMesh));
            if(Contains(defaults.body)) part.role=StudioSkinningDocument.MeshRole.Body;
            else if(Contains(defaults.bust)) part.role=StudioSkinningDocument.MeshRole.Bust;
        }
        private static void DrawMeshRoleRow(Rect row,StudioSkinningDocument.Part part)
        {
            if(part.referenceOnly)
                EditorGUI.DrawRect(row,new Color(.12f,.13f,.14f,1));
            var icon=new Rect(row.x+4,row.center.y-8,16,16);
            GUI.Label(new Rect(row.x+26,row.y,Mathf.Max(0,row.width-48),row.height),
                new GUIContent(part.referenceOnly ? (part.role == StudioSkinningDocument.MeshRole.Body ? "Default Body" : part.role == StudioSkinningDocument.MeshRole.Bust ? "Default Bust" : "Default Pants") : part.name,
                    part.role + (part.referenceOnly ? " — locked default reference: " + part.name : "")),StudioTheme.Styles.label);
            if(part.referenceOnly) GUI.Label(new Rect(row.xMax-20,row.center.y-8,16,16),new GUIContent(EditorGUIUtility.IconContent("LockIcon-On").image,"Locked default reference"));
            GUI.Label(icon,new GUIContent("",part.role.ToString()));
            if(Event.current.type!=EventType.Repaint) return;
            var previous=Handles.color; var matrix=Handles.matrix;
            Handles.BeginGUI(); Handles.matrix=Matrix4x4.identity;
            Vector3 P(float x,float y)=>new Vector3(icon.x+x,icon.y+y,0);
            void Line(params Vector3[] points)=>Handles.DrawAAPolyLine(1.5f,points);
            try
            {
                Handles.color=new Color(.5f,1,.87f);
                if(part.role==StudioSkinningDocument.MeshRole.Clothing)
                {
                    Line(P(5,2),P(1,5),P(3,8),P(5,7),P(5,14),P(11,14),P(11,7),P(13,8),P(15,5),P(11,2));
                    Line(P(5,2),P(6,4),P(10,4),P(11,2));
                }
                else if(part.role==StudioSkinningDocument.MeshRole.Bust)
                {
                    Handles.DrawWireDisc(P(8,4),Vector3.forward,2.6f);
                    Line(P(6,7),P(6,9),P(2,11),P(2,14),P(14,14),P(14,11),P(10,9),P(10,7));
                }
                else
                {
                    Handles.DrawWireDisc(P(8,2.5f),Vector3.forward,1.8f);
                    Line(P(2,6),P(8,5),P(14,6));
                    Line(P(8,5),P(8,10),P(4,15));
                    Line(P(8,10),P(12,15));
                }
            }
            finally { Handles.EndGUI(); Handles.color=previous; Handles.matrix=matrix; }
        }
    }
}
