using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Render assigned materials through the active render pipeline. This helper
    // never edits or owns source materials; diagnostic views supply their own materials.
    internal sealed class StudioMeshMaterialPreview : IDisposable
    {
        internal static Material CheckerMaterial =>
            AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("d2e73f08095e90e44bababe99ee53c76"))
            ?? Resources.Load<Material>("MashBoxStudio_Checkerboard");
        internal static Material DefaultMaterial =>
            AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("410de6c3c565a5b459045246f998f390"))
            ?? Resources.Load<Material>("MashBoxStudio_Default")
            ?? AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        private readonly Dictionary<Material,Material> cache=new Dictionary<Material,Material>();
        internal Material[] Materials(Material[] sources,int count,Material fallback)
        {
            var result=new Material[Mathf.Max(1,count)];
            for(int i=0;i<result.Length;i++)result[i]=sources!=null&&i<sources.Length&&sources[i]?sources[i]:fallback;
            return result;
        }
        internal static Material[] Assign(Material[] materials,int count,int slot,Material value)
        {
            var result=new Material[Mathf.Max(Mathf.Max(1,count),materials?.Length??0)];
            if(materials!=null)Array.Copy(materials,result,materials.Length);
            result[Mathf.Clamp(slot,0,result.Length-1)]=value;return result;
        }
        internal static bool DrawSlots(Material[] materials,int count,bool open,Action<int,Material> assign,bool editable=true)
        {
            open=EditorGUILayout.Foldout(open,new GUIContent("Materials","Material slots used for textured preview and export. Drop a material onto a slot."),true);
            if(open)for(int i=0;i<Mathf.Max(1,count);i++)
            {
                var rect=GUILayoutUtility.GetRect(40,20,GUILayout.ExpandWidth(true));rect.xMin+=16;
                int labelWidth=count>1?48:0;
                if(labelWidth>0){GUI.Label(new Rect(rect.x,rect.y,labelWidth,rect.height),"Slot "+(i+1),StudioTheme.Styles.miniLabel);rect.xMin+=labelWidth;}
                var current=materials!=null&&i<materials.Length?materials[i]:null;
                using(new EditorGUI.DisabledScope(!editable))
                {var next=(Material)EditorGUI.ObjectField(rect,new GUIContent("","Drop a Unity material here"),current,typeof(Material),false);if(next!=current)assign(i,next);}
            }
            return open;
        }
        internal static bool IsMaterialDrag=> (Event.current.type==EventType.DragUpdated||Event.current.type==EventType.DragPerform)&&DragAndDrop.objectReferences.Length==1&&DragAndDrop.objectReferences[0] is Material;
        internal static void Drop(Action<Material> assign)
        {
            DragAndDrop.visualMode=DragAndDropVisualMode.Link;
            if(Event.current.type==EventType.DragPerform){var material=(Material)DragAndDrop.objectReferences[0];DragAndDrop.AcceptDrag();assign(material);}
            Event.current.Use();
        }
        // Only evaluated during material drags. Uses displayed/posed vertices and identifies the actual submesh slot.
        internal static bool Hit(Mesh mesh,Matrix4x4 transform,Ray worldRay,ref float nearest,out int slot)
        {
            slot=-1;if(!mesh)return false;
            var inverse=transform.inverse;var ray=new Ray(inverse.MultiplyPoint3x4(worldRay.origin),inverse.MultiplyVector(worldRay.direction));
            if(!mesh.bounds.IntersectRay(ray))return false;
            var vertices=mesh.vertices;
            for(int s=0;s<mesh.subMeshCount;s++)
            {
                if(mesh.GetTopology(s)!=MeshTopology.Triangles&&mesh.GetTopology(s)!=MeshTopology.Quads)continue;
                var triangles=mesh.GetTriangles(s);
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=vertices[triangles[i]];var ab=vertices[triangles[i+1]]-a;var ac=vertices[triangles[i+2]]-a;
                    var h=Vector3.Cross(ray.direction,ac);float determinant=Vector3.Dot(ab,h);if(Mathf.Abs(determinant)<1e-10f)continue;
                    var t=ray.origin-a;float u=Vector3.Dot(t,h)/determinant;if(u<0||u>1)continue;
                    var q=Vector3.Cross(t,ab);float v=Vector3.Dot(ray.direction,q)/determinant;if(v<0||u+v>1)continue;
                    float distance=Vector3.Dot(ac,q)/determinant;if(distance<0)continue;
                    float worldDistance=Vector3.Distance(worldRay.origin,transform.MultiplyPoint3x4(ray.GetPoint(distance)));
                    if(worldDistance<nearest){nearest=worldDistance;slot=s;}
                }
            }
            return slot>=0;
        }
        internal static void DrawTextureIcon(Rect rect,Color color)
        {
            var old=Handles.color;Handles.color=color;
            Vector3 P(float x,float y)=>new Vector3(rect.center.x+x,rect.center.y+y,0);
            Handles.DrawAAPolyLine(1.4f,P(-8,-7),P(8,-7),P(8,7),P(-8,7),P(-8,-7));
            Handles.DrawAAPolyLine(1.4f,P(-7,5),P(-2,-1),P(2,3),P(5,0),P(8,5));
            Handles.DrawSolidDisc(P(4,-3),Vector3.forward,1.7f);Handles.color=old;
        }
        public void Dispose() { } // Source materials belong to the project, not this preview.
    }
}
