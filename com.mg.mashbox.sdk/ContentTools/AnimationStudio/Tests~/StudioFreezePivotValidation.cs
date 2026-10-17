using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioFreezePivotValidation
    {
        static readonly List<string> Results = new List<string>();
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static void Near(Vector3 a, Vector3 b, string label) => Check(Vector3.Distance(a,b) < .0002f,label);
        static Vector3[] World(StudioModelEditor editor) => editor.ActiveMesh.vertices.Select(editor.Transforms[0].TransformPoint).ToArray();
        public static void Run()
        {
            StudioModelEditor editor = null; Scene exportScene = default; GameObject imported = null;
            try
            {
                var path = AssetDatabase.GenerateUniqueAssetPath("Assets/Freeze Pivot Test.asset");
                var doc = StudioModelDocument.CreateEmpty(path); doc.AddPrimitive(PrimitiveType.Cube);
                doc.nodes[0].position = new Vector3(4,2,-3); doc.nodes[0].rotation = Quaternion.Euler(20,35,-15); doc.nodes[0].scale = Vector3.one * 2;
                doc.nodes.Add(new StudioModelDocument.Node { name = "Child", parent = 0, position = new Vector3(1,2,3), rotation = Quaternion.Euler(10,20,30) }); doc.Save();
                var disk = File.ReadAllBytes(path);
                editor = new StudioModelEditor(() => {}); editor.Load(doc); editor.SelectObject(0);
                var before = World(editor); var pivot = editor.Transforms[0].position; var child = editor.Transforms[1].localToWorldMatrix;
                Undo.IncrementCurrentGroup(); doc.Freeze(0,editor.Transforms); editor.Changed();
                Near(editor.Transforms[0].position,pivot,"Freeze preserves the current world pivot");
                Near(doc.nodes[0].position,Vector3.zero,"Position channels become zero");
                Check(Quaternion.Angle(doc.nodes[0].rotation,Quaternion.identity) < .001f && doc.nodes[0].scale == Vector3.one,"Rotation and scale freeze to identity");
                Check(World(editor).Zip(before,(a,b) => Vector3.Distance(a,b)).Max() < .0002f,"Freeze preserves every rendered vertex");
                Near(editor.Transforms[1].position,child.GetColumn(3),"Child world position stays unchanged");
                Check(Quaternion.Angle(editor.Transforms[1].rotation,child.rotation) < .001f,"Child world rotation stays unchanged");
                Check(File.ReadAllBytes(path).SequenceEqual(disk),"Freeze does not write the workspace to disk");
                Undo.PerformUndo();
                Check(doc.nodes[0].positionOffset == Vector3.zero && Quaternion.Angle(doc.nodes[0].rotation,Quaternion.Euler(20,35,-15)) < .001f,"Undo restores original channels and offset");
                Near(editor.Transforms[0].position,pivot,"Undo preserves pivot placement"); Undo.PerformRedo();
                Near(doc.nodes[0].position,Vector3.zero,"Redo restores zeroed channels"); Near(editor.Transforms[0].position,pivot,"Redo preserves pivot placement");
                editor.SetObjectChannels(new Vector3(1,0,0),Vector3.zero,Vector3.one,true);
                Near(editor.Transforms[0].position,pivot + Vector3.right,"Position channels move relative to the frozen origin");
                doc.Freeze(0,editor.Transforms); editor.Changed();
                Near(doc.nodes[0].position,Vector3.zero,"Repeated freeze rezeros channels"); pivot += Vector3.right;
                Near(editor.Transforms[0].position,pivot,"Repeated freeze does not jump to the scene origin");
                editor.Load(doc); editor.SelectObject(0);
                Near(editor.Transforms[0].position,pivot,"Preview rebuild restores the frozen placement");
                var snapshot = World(editor);
                doc.Repivot(0,editor.Transforms,pivot + Vector3.up * .3f); editor.Changed();
                Check(World(editor).Zip(snapshot,(a,b) => Vector3.Distance(a,b)).Max() < .0002f,"Pivot adjustment after freezing keeps geometry fixed");
                doc.Freeze(0,editor.Transforms); editor.Changed(); pivot = editor.Transforms[0].position;
                exportScene = EditorSceneManager.NewPreviewScene();
                var nodes = doc.Build(exportScene); var root = StudioModelEditor.ExportRoot(nodes,exportScene);
                Near(root.transform.position,Vector3.zero,"Export copy places its pivot at zero");
                Check(nodes[0].GetComponent<MeshFilter>().sharedMesh.vertices.Select(nodes[0].TransformPoint).Zip(snapshot,(a,b) => Vector3.Distance(a,b-pivot)).Max() < .0002f,"Export geometry is relative to the retained pivot");
                Near(editor.Transforms[0].position,pivot,"Preparing export leaves live workspace placement unchanged");
                string fbx = AssetDatabase.GenerateUniqueAssetPath("Assets/Frozen Pivot Export.fbx");
                StudioModelEditor.ExportTo(Path.GetFullPath(fbx),root); AssetDatabase.ImportAsset(fbx,ImportAssetOptions.ForceSynchronousImport);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx); Check(asset,"FBX exports and reimports successfully");
                imported = Object.Instantiate(asset);
                Near(imported.transform.position,Vector3.zero,"Reimported FBX has its pivot at zero");
                var renderer = imported.GetComponentInChildren<MeshRenderer>();
                var expectedCenter = (snapshot.Aggregate(Vector3.Min) + snapshot.Aggregate(Vector3.Max)) * .5f - pivot;
                Near(renderer.bounds.center,expectedCenter,"FBX geometry retains the expected offset from its pivot");
                // A frozen child under an independently transformed parent uses parent-space offsets.
                doc.nodes[1].scale = Vector3.one; doc.nodes[1].rotation = Quaternion.Euler(15,20,25); editor.Load(doc);
                var childPivot = editor.Transforms[1].position; doc.Freeze(1,editor.Transforms);
                Near(editor.Transforms[1].position,childPivot,"Freezing a child preserves its world pivot");
                Near(doc.nodes[1].position,Vector3.zero,"Child channels zero independently of its parent");
                editor.SelectObject(0);
                typeof(StudioModelEditor).GetField("pivot",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(editor,true);
                var geometry = World(editor); var offset = doc.nodes[0].positionOffset;
                editor.SetObjectChannels(new Vector3(.2f,0,0),Vector3.zero,Vector3.one,true);
                Near(editor.Transforms[0].position,offset + Vector3.right * .2f,"Pivot channel editing respects the frozen origin");
                Check(World(editor).Zip(geometry,(a,b) => Vector3.Distance(a,b)).Max() < .0002f,"Editing frozen pivot channels preserves geometry");
                int second = doc.AddPrimitive(PrimitiveType.Cube); doc.nodes[second].position = new Vector3(9,2,1);
                var multiScene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var multi = doc.Build(multiScene); var spacing = multi[0].position - multi[second].position;
                    var multiRoot = StudioModelEditor.ExportRoot(multi,multiScene,multi[second].position);
                    Near(multi[second].position,Vector3.zero,"Multi-model export uses the active model origin");
                    Near(multi[0].position - multi[second].position,spacing,"Multi-model export preserves relative placement");
                    Check(multi[0].parent == multiRoot.transform && multi[second].parent == multiRoot.transform,"Multi-model export retains distinct model roots");
                }
                finally { EditorSceneManager.ClosePreviewScene(multiScene); }
                File.WriteAllLines("freeze-pivot-result.txt",Results);
            }
            catch(Exception e) { Results.Add("FAIL: " + e); File.WriteAllLines("freeze-pivot-result.txt",Results); }
            finally { if(imported) Object.DestroyImmediate(imported); if(exportScene.IsValid()) EditorSceneManager.ClosePreviewScene(exportScene); editor?.Dispose(); }
            EditorApplication.Exit(Results.Any(r => r.StartsWith("FAIL")) ? 1 : 0);
        }
    }
}
