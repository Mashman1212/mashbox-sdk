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
            bool Matches(string name,string role) => !string.IsNullOrEmpty(name) &&
                (name.Equals(role,System.StringComparison.OrdinalIgnoreCase) ||
                 name.IndexOf("_"+role,System.StringComparison.OrdinalIgnoreCase)>=0 ||
                 name.IndexOf(role+"_",System.StringComparison.OrdinalIgnoreCase)>=0);
            bool Contains(string role) => Matches(part.name,role) || (part.sourceMesh && Matches(part.sourceMesh.name,role));
            // Run only at import so later manual role changes remain authoritative.
            if (Contains("Bust")) part.role = StudioSkinningDocument.MeshRole.Bust;
            else if (Contains("Body")) part.role = StudioSkinningDocument.MeshRole.Body;
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
            GUI.Label(new Rect(row.x+26,row.y,Mathf.Max(0,row.width-(part.HasStitchWeightLock ? 92 : 70)),row.height),
                new GUIContent(part.referenceOnly ? (!string.IsNullOrEmpty(part.defaultReferenceLabel) ? part.defaultReferenceLabel : part.role == StudioSkinningDocument.MeshRole.Body ? "Default Body" : part.role == StudioSkinningDocument.MeshRole.Bust ? "Default Bust" : "Default Pants") : part.name,
                    part.role + (part.referenceOnly ? " — locked default reference: " + part.name : "")),StudioTheme.Styles.label);
            if(part.referenceOnly) GUI.Label(new Rect(row.xMax-20,row.center.y-8,16,16),new GUIContent(EditorGUIUtility.IconContent("LockIcon-On").image,"Locked default reference"));
            if (part.HasStitchWeightLock)
            {
                var stitchRect = new Rect(row.xMax-64,row.center.y-8,16,16);
                var previousColor = GUI.color;
                GUI.color = new Color(.5f,1,.87f);
                GUI.Label(stitchRect,new GUIContent(EditorGUIUtility.IconContent("LockIcon-On").image,
                    "Stitch weight lock — " + part.stitchedVertices.Length + " seam vertices protected from painting and smoothing. The rest of the bust remains paintable."));
                GUI.color = previousColor;
            }
            var skinStatus = new Rect(row.xMax-42,row.center.y-8,16,16);
            StudioSkinStatusIcon.Draw(skinStatus,part.bound,part.bound ? "Skinned — bound to the SDK skeleton" : "Unskinned — right-click and choose Skin to bind");
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
    internal static class StudioSkinStatusIcon
    {
        internal static bool HasWeights(Mesh mesh)
        {
            if (!mesh) return false;
            using (var weights = mesh.GetAllBoneWeights()) return weights.Length > 0;
        }
        internal static void Draw(Rect rect, bool skinned, string tooltip)
        {
            GUI.Label(rect,new GUIContent("",tooltip));
            if(Event.current.type!=EventType.Repaint) return;
            var previous=Handles.color; var matrix=Handles.matrix;
            Handles.BeginGUI(); Handles.matrix=Matrix4x4.identity;
            try
            {
                Vector3 P(float x,float y)=>new Vector3(rect.x+x,rect.y+y,0);
                void Line(params Vector3[] points)=>Handles.DrawAAPolyLine(1.5f,points);
                Handles.color=skinned ? new Color(.5f,1,.87f) : new Color(.55f,.58f,.6f);
                Line(P(6,4),P(3,4),P(1,6),P(1,10),P(3,12),P(6,12));
                Line(P(10,4),P(13,4),P(15,6),P(15,10),P(13,12),P(10,12));
                if(skinned) Line(P(4,8),P(12,8));
                else Line(P(10,3),P(6,13));
            }
            finally { Handles.EndGUI(); Handles.color=previous; Handles.matrix=matrix; }
        }
    }
}
