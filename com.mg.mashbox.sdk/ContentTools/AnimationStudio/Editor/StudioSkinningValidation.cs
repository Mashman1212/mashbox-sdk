using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioSkinningValidation
    {
        private const string Request = "Temp/StudioSkinning.request";
        [InitializeOnLoadMethod] private static void Pending()
        { if (File.Exists(Request)) EditorApplication.update += RunPending; }
        private static void RunPending()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= RunPending; File.Delete(Request);
            try { string result = Run(); File.WriteAllText("Temp/StudioSkinning.result",result); Debug.Log(result); }
            catch (Exception e) { File.WriteAllText("Temp/StudioSkinning.result",e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("MashBox/Dev/Studio Checks/Skinning/Validate skinning and sculpt")]
        private static void Menu() => Debug.Log(Run());
        private static void Require(bool condition,string message) { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(object obj,string field) => obj.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);
        private static void Set(object obj,string field,object value) => obj.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(obj,value);
        private static void Call(object obj,string method,params object[] args) => obj.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(obj,args);
        internal static string Run()
        {
            var mesh = new Mesh(); var doc = ScriptableObject.CreateInstance<StudioSkinningDocument>(); StudioSkinningEditor editor = null;
            string path = null, prefabPath = null, meshPath = null;
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0,1,2 }; mesh.RecalculateNormals();
                var weights = new StudioSkinWeights(mesh);
                for (int i = 0; i < 3; i++) for (int b = 0; b < 6; b++) weights.Values[i].Add(new BoneWeight1 { boneIndex=b,weight=1f/6 });
                weights.Apply(mesh); var loaded = new StudioSkinWeights(mesh);
                Require(loaded.Values[0].Count == 6,"Import truncated variable influences.");
                var locked = new bool[6]; locked[0] = true;
                float lockedBefore = loaded.Weight(0,0); // Unity quantizes stored skin weights.
                loaded.Set(0,1,.7f,locked);
                Require(loaded.Weight(0,0)==lockedBefore,"Painting changed a locked weight.");
                Require(Mathf.Abs(loaded.Values[0].Sum(w=>w.weight)-1)<1e-5f,"Weights are not normalized.");
                mesh.uv = new[] { Vector2.zero,Vector2.right,Vector2.up };
                mesh.AddBlendShapeFrame("Fit",100,new[] { Vector3.up*.1f,Vector3.zero,Vector3.zero },new Vector3[3],new Vector3[3]);
                using (var serialized = new SerializedObject(mesh))
                { serialized.FindProperty("m_IsReadable").boolValue=false; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                var editable = StudioSkinningDocument.CreateEditableCopy(mesh);
                try
                {
                    Require(editable.isReadable && !mesh.isReadable,"Editable copy changed source readability.");
                    Require(new StudioSkinWeights(editable).Values[0].Count==6,"Readable copy lost variable skin influences.");
                    Require(editable.blendShapeCount==1 && editable.GetBlendShapeName(0)=="Fit","Readable copy lost blend shapes.");
                    var shapeDelta = new Vector3[3]; editable.GetBlendShapeFrameVertices(0,0,shapeDelta,null,null);
                    Require(shapeDelta[0]==Vector3.up*.1f && editable.uv[2]==Vector2.up,"Readable copy lost shape deltas or UVs.");
                }
                finally { Object.DestroyImmediate(editable); }
                doc.skeleton = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Skeleton.fbx");
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/SM_Body_01.fbx");
                Require(doc.skeleton && source,"SDK skeleton/body unavailable.");
                var shader = Shader.Find("Hidden/MashBox/SkinWeightPreview"); Require(shader && !ShaderUtil.ShaderHasError(shader),"Weight preview shader failed.");
                var sourceMesh = source.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
                var sourceVertices = sourceMesh.vertices;
                editor = new StudioSkinningEditor(doc,()=>{}); editor.Import(source);
                Require(doc.parts.Count > 0,"Import produced no meshes.");
                if (!doc.parts[doc.selected].bound) Call(editor,"AutoBind");
                var part = doc.parts[doc.selected]; var originalWeights = new StudioSkinWeights(part.mesh);
                string serializedWeights = WeightString(originalWeights);
                var before = part.mesh.vertices; int count = before.Length;
                Set(editor,"frontOnly",false); Set(editor,"radius",.3f); Set(editor,"strength",.5f);
                editor.Tool = StudioSkinningEditor.Brush.Inflate;
                var points = (Vector3[])Get(editor,"deformed");
                Set(editor,"initialPoints",(Vector3[])points.Clone()); Set(editor,"initialVertices",(Vector3[])before.Clone());
                Set(editor,"spatial",new StudioSkinSpatialIndex(points,.3f)); Set(editor,"hitNormal",Vector3.forward);
                Undo.IncrementCurrentGroup();
                int sculptGroup = Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(new Object[] { doc, part.mesh }, "Validate sculpt undo");
                Call(editor,"ApplyBrush",points[0],Vector3.zero,new Event { type=EventType.MouseDrag });
                Require(part.mesh.vertexCount==count,"Sculpt changed vertex count.");
                Require(!part.mesh.vertices.SequenceEqual(before),"Inflate made no edit.");
                Require(WeightString(new StudioSkinWeights(part.mesh))==serializedWeights,"Sculpt changed weights with weight smoothing off.");
                Require(sourceMesh.vertices.SequenceEqual(sourceVertices),"Source model was modified.");
                var sculpted = part.mesh.vertices;
                RequireSculptSeams(before, sculpted);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(sculptGroup); Undo.IncrementCurrentGroup();
                Undo.PerformUndo();
                Require(doc.parts[doc.selected].mesh.vertices.SequenceEqual(before),"Undo did not restore the sculpt geometry.");
                Undo.PerformRedo();
                part = doc.parts[doc.selected];
                Require(part.mesh.vertices.SequenceEqual(sculpted),"Redo did not restore the sculpt geometry.");
                // Pose an actual influence and exercise inverse skinning with Grab.
                var bones = (Transform[])Get(editor,"bones");
                int bone = originalWeights.Values.SelectMany(v=>v).OrderByDescending(w=>w.weight).First().boneIndex;
                bones[bone].localRotation *= Quaternion.Euler(0,0,20); Call(editor,"RefreshSurface");
                points = (Vector3[])Get(editor,"deformed"); var start = (Vector3[])points.Clone();
                Set(editor,"initialPoints",start); Set(editor,"spatial",new StudioSkinSpatialIndex(start,.3f));
                editor.Tool = StudioSkinningEditor.Brush.Grab;
                var delta = new Vector3(.015f,.01f,0);
                Call(editor,"ApplyBrush",start[0],delta,new Event { type=EventType.MouseDrag });
                var matrices = (Matrix4x4[])Get(editor,"skinMatrices");
                var afterWeights = new StudioSkinWeights(part.mesh);
                Vector3 actual = afterWeights.SkinMatrix(0,matrices).MultiplyPoint3x4(part.mesh.vertices[0]);
                Require(Vector3.Distance(actual,start[0]+delta)<.0001f,"Posed Grab did not preserve the requested surface displacement.");
                Require(WeightString(afterWeights)==serializedWeights,"Posed sculpt changed weights.");
                RequireSculptSeams(start: before, result: part.mesh.vertices);
                // Save/reload a disposable asset graph, then build its preview again.
                path = AssetDatabase.GenerateUniqueAssetPath("Assets/__StudioSkinningValidation.asset");
                AssetDatabase.CreateAsset(doc,path); foreach (var p in doc.parts) { p.mesh.hideFlags=HideFlags.None; AssetDatabase.AddObjectToAsset(p.mesh,doc); }
                EditorUtility.SetDirty(doc); AssetDatabase.SaveAssetIfDirty(doc);
                editor.Dispose(); editor=null;
                var saved=AssetDatabase.LoadAssetAtPath<StudioSkinningDocument>(path);
                editor=new StudioSkinningEditor(saved,()=>{});
                Require(WeightString(new StudioSkinWeights(saved.parts[saved.selected].mesh))==serializedWeights,"Save/reopen lost weights.");
                prefabPath = AssetDatabase.GenerateUniqueAssetPath("Assets/__StudioSkinningValidation.prefab");
                meshPath = AssetDatabase.GenerateUniqueAssetPath("Assets/__StudioSkinningValidationMeshes.asset");
                var prefab = editor.ExportToPath(prefabPath,meshPath);
                var exported = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Require(exported.Length == doc.parts.Count(p=>p.bound),"Export lost bound meshes.");
                Require(exported.All(s=>s.sharedMesh && s.bones.All(b=>b) && s.sharedMesh.bindposes.Length==s.bones.Length),"Export lost skeleton references.");
                Require(WeightString(new StudioSkinWeights(exported[doc.selected].sharedMesh))==serializedWeights,"Export changed skin weights.");
                Require(exported[doc.selected].sharedMesh != part.mesh,"Export reused the editable workspace mesh.");
                Call(editor,"AutoBind");
                var deformBones = (bool[])Get(editor,"deformBones");
                Require(new StudioSkinWeights(doc.parts[doc.selected].mesh).Values.SelectMany(v=>v).All(w=>deformBones[w.boneIndex]),"Automatic binding used an animation helper target.");
                var pants = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Human_Pants_Punch_WC.fbx");
                Require(pants,"SDK pants unavailable for Read/Write regression.");
                var pantsImporter = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(pants));
                bool pantsReadable = pantsImporter.isReadable;
                var pantsMesh = pants.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
                bool meshReadable = pantsMesh.isReadable;
                var pantsVertices = pantsMesh.vertices;
                var pantsWeights = new StudioSkinWeights(pantsMesh);
                int previousCount=doc.parts.Count;
                editor.Import(pants);
                var importedPants=doc.parts[previousCount].mesh;
                Require(importedPants.isReadable && importedPants.vertexCount==pantsMesh.vertexCount,"Read/Write-disabled pants did not import as an editable mesh.");
                Require(pantsImporter.isReadable==pantsReadable && pantsMesh.isReadable==meshReadable && pantsMesh.vertices.SequenceEqual(pantsVertices),"Pants import changed the source model.");
                Require(new StudioSkinWeights(importedPants).Values.Select(v=>v.Count).SequenceEqual(pantsWeights.Values.Select(v=>v.Count)),"Pants import lost skin influences.");
                Set(editor,"frontOnly",false); Set(editor,"radius",.3f);
                editor.Tool=StudioSkinningEditor.Brush.Inflate;
                var pantsPoints=(Vector3[])Get(editor,"deformed");
                Set(editor,"initialPoints",(Vector3[])pantsPoints.Clone()); Set(editor,"spatial",new StudioSkinSpatialIndex(pantsPoints,.3f));
                var pantsBefore=importedPants.vertices;
                string pantsSkinBefore=WeightString(new StudioSkinWeights(importedPants));
                Call(editor,"ApplyBrush",pantsPoints[0],Vector3.zero,new Event { type=EventType.MouseDrag });
                Require(!pantsBefore.SequenceEqual(importedPants.vertices) && pantsSkinBefore==WeightString(new StudioSkinWeights(importedPants)),"Imported pants sculpt failed to preserve skinning.");
                RequireSculptSeams(pantsBefore, importedPants.vertices);
                var pantsUV = importedPants.uv; var pantsTriangles = importedPants.triangles;
                foreach (var brush in new[] { StudioSkinningEditor.Brush.Bulge, StudioSkinningEditor.Brush.Grab, StudioSkinningEditor.Brush.SmoothShape })
                {
                    Call(editor,"RefreshSurface"); editor.Tool = brush;
                    var surface = (Vector3[])Get(editor,"deformed");
                    Set(editor,"initialPoints",(Vector3[])surface.Clone()); Set(editor,"spatial",new StudioSkinSpatialIndex(surface,.3f));
                    Call(editor,"ApplyBrush",surface[0],new Vector3(.01f,.02f,0),new Event { type=EventType.MouseDrag });
                    RequireSculptSeams(pantsBefore, importedPants.vertices);
                    Require(importedPants.uv.SequenceEqual(pantsUV) && importedPants.triangles.SequenceEqual(pantsTriangles),"Sculpt changed UVs or render topology.");
                    Require(pantsSkinBefore==WeightString(new StudioSkinWeights(importedPants)),"Seam sculpt changed skin weights.");
                }
                // Spatial work stays local on a 100k-vertex synthetic surface.
                var many = Enumerable.Range(0,100000).Select(i=>new Vector3(i%1000*.01f,i/1000*.01f,0)).ToArray();
                var timer=System.Diagnostics.Stopwatch.StartNew(); var index=new StudioSkinSpatialIndex(many,.05f); var nearby=new List<int>();
                for(int i=0;i<1000;i++) index.Query(new Vector3(3,.5f,0),.05f,nearby);
                timer.Stop(); Require(nearby.Count<1000,"Brush query scanned the whole mesh.");
                return "PASS: readable working copies preserve variable influences, UVs and blend shapes; SDK pants import/sculpt with source Read/Write="+pantsReadable+" unchanged; locked/normalized paint, SDK import/bind, sculpt Undo/Redo, rest and posed sculpt weight preservation, source isolation, save/reopen, prefab export, shader. 100k-vertex index + 1000 local queries: "+timer.ElapsedMilliseconds+" ms; "+nearby.Count+" candidates/query.";
            }
            finally
            {
                editor?.Dispose(); Object.DestroyImmediate(mesh);
                if (path != null) AssetDatabase.DeleteAsset(path);
                else { foreach (var p in doc.parts) if (p.mesh) Object.DestroyImmediate(p.mesh); Object.DestroyImmediate(doc); }
                if (prefabPath != null) AssetDatabase.DeleteAsset(prefabPath);
                if (meshPath != null) AssetDatabase.DeleteAsset(meshPath);
            }
        }
        private static void RequireSculptSeams(Vector3[] start, Vector3[] result)
        {
            var first = new Dictionary<Vector3,int>();
            for (int i=0;i<start.Length;i++)
            {
                if (first.TryGetValue(start[i],out int peer))
                    Require(result[peer].Equals(result[i]), "Sculpt separated a coincident seam vertex.");
                else first.Add(start[i],i);
            }
        }
        private static string WeightString(StudioSkinWeights weights) => string.Join(";",weights.Values.Select(v=>string.Join(",",v.Select(w=>w.boneIndex+":"+w.weight.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))));
    }
}
