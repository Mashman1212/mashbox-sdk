using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioMaterialSlotValidation
    {
        public static void Run()
        {
            if (!Application.dataPath.EndsWith("ThemeIsolationProject/Assets")) return;
            var log = new List<string>(); StudioModelEditor editor = null;
            var doc = ScriptableObject.CreateInstance<StudioModelDocument>();
            var mesh = new Mesh(); Material red = null, green = null;
            void Check(bool ok, string label) { if (!ok) throw new Exception(label); log.Add("PASS " + label); }
            try
            {
                mesh.vertices = new[] {new Vector3(-1,-.5f,0),new Vector3(-1,.5f,0),new Vector3(0,.5f,0),new Vector3(0,-.5f,0),new Vector3(0,-.5f,0),new Vector3(0,.5f,0),new Vector3(1,.5f,0),new Vector3(1,-.5f,0)};
                mesh.triangles = new[] {0,1,2,0,2,3,4,5,6,4,6,7}; mesh.uv = mesh.vertices.Select(p => new Vector2(p.x,p.y)).ToArray(); mesh.RecalculateNormals();
                red = new Material(Shader.Find("Unlit/Color")) { color = Color.red }; green = new Material(red) { color = Color.green };
                var node = new StudioModelDocument.Node {name="Slot check",mesh=mesh,materials=new[]{red,green},cutTriangles=mesh.triangles,cutPolygons=new[]{0,4,0,1,2,3,0,4,4,5,6,7}};doc.nodes.Add(node);
                var positions=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;
                editor = new StudioModelEditor(()=>{}); editor.Load(doc); editor.SelectObject(0); editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face); editor.Components.Add(1);
                typeof(StudioModelEditor).GetMethod("AssignSelectionMaterialSlot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,new object[]{1});
                var faces=new StudioMeshCuts(node).Faces;
                Check(faces[0].Submesh==0&&faces[1].Submesh==1&&faces.All(f=>f.Vertices.Count==4),"Selected face assigned; neighboring quad retained");
                Check(positions.SequenceEqual(mesh.vertices)&&normals.SequenceEqual(mesh.normals)&&uv.SequenceEqual(mesh.uv),"Positions, UVs and normals unchanged");
                Check(editor.Components.Contains(1)&&node.modifiers.operations.Last().kind=="MaterialSlot","Face selection and compact modifier history retained");
                Capture(node,log);
                UnityEditor.Undo.FlushUndoRecordObjects(); StudioUndo.IncrementCurrentGroup(); StudioUndo.PerformUndo();
                node=doc.nodes[0]; Check(new StudioMeshCuts(node).Faces.All(f=>f.Submesh==0),"Undo restores face slot IDs");
                StudioUndo.PerformRedo(); node=doc.nodes[0]; Check(new StudioMeshCuts(node).Faces[1].Submesh==1,"Redo restores assignment");
                editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);
                typeof(StudioModelEditor).GetMethod("AssignSelectionMaterialSlot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,new object[]{2});
                Check(new StudioMeshCuts(node).Faces.All(f=>f.Submesh==2)&&mesh.GetTriangles(2).Length==12&&node.materials.Length>=3,"Object assignment moves all faces and grows slots");
                typeof(StudioModelEditor).GetMethod("AddMaterialSlot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,null);
                Check(mesh.subMeshCount==4&&mesh.GetTriangles(3).Length==0&&new StudioMeshCuts(node).Faces.All(f=>f.Submesh==2),"Add Slot creates an empty slot without reassigning faces");
                typeof(StudioModelEditor).GetMethod("RemoveMaterialSlot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,new object[]{2});
                Check(mesh.subMeshCount==3&&node.materials.Length==3&&new StudioMeshCuts(node).Faces.All(f=>f.Submesh==0),"Removing an occupied slot retains faces at Slot 0");
                var replay=StudioModifierEvaluator.Evaluate(node.modifiers);
                Check(replay.mesh.subMeshCount==3&&new StudioMeshCuts(replay).Faces.All(f=>f.Submesh==0),"Compact slot history replays assignment, addition and removal");
                Object.DestroyImmediate(replay.mesh);
                typeof(StudioModelEditor).GetMethod("RemoveMaterialSlot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,new object[]{0});
                typeof(StudioModelEditor).GetMethod("RemoveMaterialSlot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,new object[]{0});
                typeof(StudioModelEditor).GetMethod("RemoveMaterialSlot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,new object[]{0});
                Check(mesh.subMeshCount==1&&new StudioMeshCuts(node).Faces.Count==2,"The final slot cannot be removed");
            }
            catch(Exception e){log.Add("FAIL "+e);}
            finally{editor?.Dispose();Object.DestroyImmediate(mesh);Object.DestroyImmediate(red);Object.DestroyImmediate(green);Object.DestroyImmediate(doc);}
            File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath,"../../material-slot-result.txt")),log);
        }
        private static void Capture(StudioModelDocument.Node node,List<string> log)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var root=new GameObject("Slots");var view=new GameObject("Camera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(view,scene);
            root.layer=31;root.AddComponent<MeshFilter>().sharedMesh=node.mesh;root.AddComponent<MeshRenderer>().sharedMaterials=node.materials;
            var camera=view.AddComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.cullingMask=1<<31;camera.transform.position=new Vector3(0,0,-2);camera.transform.LookAt(Vector3.zero);camera.fieldOfView=40;camera.aspect=2;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var rt=new RenderTexture(256,128,24);rt.Create();camera.targetTexture=rt;var old=RenderTexture.active;
            var pipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
            try{UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;camera.Render();RenderTexture.active=rt;var image=new Texture2D(256,128,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,256,128),0,0);image.Apply();var a=image.GetPixel(80,64);var b=image.GetPixel(176,64);if(!(a.r>.7f&&a.g<.2f&&b.g>.7f&&b.r<.2f))throw new Exception("Renderer did not show red and green face slots");log.Add("PASS Actual renderer shows separate material slots");File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../material-slots.png")),image.EncodeToPNG());Object.DestroyImmediate(image);}
            finally{UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=quality;RenderTexture.active=old;Object.DestroyImmediate(rt);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
