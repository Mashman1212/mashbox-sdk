using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private static void AssignImportedMeshRole(StudioSkinningDocument.Part part)
        {
            if (part.referenceOnly) return;
            bool Contains(string token) =>
                (!string.IsNullOrEmpty(part.name) && part.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                (part.sourceMesh && part.sourceMesh.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0);
            // Run only at import so later manual role changes remain authoritative.
            if (Contains("Bust_")) part.role = StudioSkinningDocument.MeshRole.Bust;
            else if (Contains("_Body")) part.role = StudioSkinningDocument.MeshRole.Body;
        }
        private static void AssignDefaultMeshRole(StudioSkinningDocument.Part part)
        {
            var defaults=StudioDefaultReferences.LoadSkinning("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if(!defaults || !part.sourceMesh) return;
            bool Contains(GameObject root) => root && (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r=>r.sharedMesh==part.sourceMesh)
                || root.GetComponentsInChildren<MeshFilter>(true).Any(r=>r.sharedMesh==part.sourceMesh));
            if(Contains(defaults.body)) part.role=StudioSkinningDocument.MeshRole.Body;
            else if(Contains(defaults.bust)) part.role=StudioSkinningDocument.MeshRole.Bust;
            else if(Contains(defaults.shirt))
            { part.role=StudioSkinningDocument.MeshRole.Clothing; part.defaultReferenceLabel="Default Shirt"; }
        }
        private static void DrawMeshRoleRow(Rect row,StudioSkinningDocument.Part part)
        {
            if(part.referenceOnly)
                EditorGUI.DrawRect(row,new Color(.12f,.13f,.14f,1));
            var icon=new Rect(row.x+4,row.center.y-8,16,16);
            GUI.Label(new Rect(row.x+26,row.y,Mathf.Max(0,row.width-70),row.height),
                new GUIContent(part.referenceOnly ? (!string.IsNullOrEmpty(part.defaultReferenceLabel) ? part.defaultReferenceLabel : part.role == StudioSkinningDocument.MeshRole.Body ? "Default Body" : part.role == StudioSkinningDocument.MeshRole.Bust ? "Default Bust" : "Default Pants") : part.name,
                    part.role + (part.referenceOnly ? " — locked default reference: " + part.name : "")),StudioTheme.Styles.label);
            if(part.referenceOnly) GUI.Label(new Rect(row.xMax-20,row.center.y-8,16,16),new GUIContent(EditorGUIUtility.IconContent("LockIcon-On").image,"Locked default reference"));
            var skinStatus = new Rect(row.xMax-42,row.center.y-8,16,16);
            GUI.Label(skinStatus,new GUIContent("",part.bound ? "Skinned — bound to the SDK skeleton" : "Unskinned — right-click and choose Skin to bind"));
            GUI.Label(icon,new GUIContent("",part.role.ToString()));
            if(Event.current.type!=EventType.Repaint) return;
            var previous=Handles.color; var matrix=Handles.matrix;
            Handles.BeginGUI(); Handles.matrix=Matrix4x4.identity;
            Vector3 P(float x,float y)=>new Vector3(icon.x+x,icon.y+y,0);
            void Line(params Vector3[] points)=>Handles.DrawAAPolyLine(1.5f,points);
            try
            {
                Vector3 S(float x,float y)=>new Vector3(skinStatus.x+x,skinStatus.y+y,0);
                Handles.color=part.bound ? new Color(.5f,1,.87f) : new Color(.55f,.58f,.6f);
                // Two links join when bound; separated halves and a slash when unbound.
                Line(S(6,4),S(3,4),S(1,6),S(1,10),S(3,12),S(6,12));
                Line(S(10,4),S(13,4),S(15,6),S(15,10),S(13,12),S(10,12));
                if(part.bound) Line(S(4,8),S(12,8));
                else Line(S(10,3),S(6,13));
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
