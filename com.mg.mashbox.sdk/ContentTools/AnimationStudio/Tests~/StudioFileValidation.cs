using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioFileValidation
    {
        static readonly List<string> results = new List<string>();
        static void Check(bool condition,string message) { if(!condition) throw new Exception(message); results.Add("PASS: " + message); }
        public static void Run()
        {
            StudioModelEditor editor = null;
            int exit = 0;
            try
            {
                string folder = "Assets/Files " + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
                string originalPath = folder + "/Original.asset", copyPath = folder + "/Copy.asset";
                var doc = StudioModelDocument.CreateEmpty(originalPath);
                doc.AddPrimitive(PrimitiveType.Cube); doc.AddPrimitive(PrimitiveType.Cube); doc.AddPrimitive(PrimitiveType.Sphere);
                doc.nodes[0].name="SelectedParent"; doc.nodes[0].position=new Vector3(2,3,4);
                doc.nodes[1].name="SelectedChild"; doc.nodes[1].parent=0; doc.nodes[1].position=Vector3.up*2;
                doc.nodes[2].name="Unrelated"; doc.nodes[2].position=Vector3.right*7;
                doc.Save();
                var before = File.ReadAllBytes(originalPath);
                doc.nodes[0].positionOffset = new Vector3(1,0,0);
                doc.nodes[0].pivotRotation = Quaternion.Euler(10,20,30);
                doc.lighting.keyIntensity=3;
                var mesh = doc.nodes[0].mesh;
                var vertices = mesh.vertices; vertices[0] += Vector3.up*.2f; mesh.vertices=vertices;
                EditorUtility.SetDirty(mesh); EditorUtility.SetDirty(doc);
                var copy = doc.SaveAs(copyPath);
                Check(File.ReadAllBytes(originalPath).SequenceEqual(before),"Save As leaves original workspace on disk untouched");
                Check(copy != doc && copy.nodes.Count==3,"Save As creates a complete independent workspace");
                Check(copy.nodes[0].mesh != mesh && copy.nodes[0].mesh.vertices[0]==vertices[0],"Save As includes unsaved mesh edits in independent mesh assets");
                Check(AssetDatabase.GetAssetPath(copy.nodes[0].mesh)==copyPath,"Saved copy owns its meshes");
                Check(copy.nodes[0].positionOffset==doc.nodes[0].positionOffset && copy.nodes[0].pivotRotation==doc.nodes[0].pivotRotation,"Save As retains frozen origins and pivots");
                Check(copy.lighting.keyIntensity==3 && !ReferenceEquals(copy.lighting,doc.lighting),"Save As copies lighting settings independently");
                Check(copy.nodes[0].materials[0]==doc.nodes[0].materials[0],"Save As retains material asset assignments");
                copy.nodes[0].position=Vector3.zero; copy.lighting.keyIntensity=7;
                Check(doc.nodes[0].position==new Vector3(2,3,4) && doc.lighting.keyIntensity==3,"Editing a saved copy leaves original node data unchanged");
                bool rejected=false;
                try { doc.SaveAs(copyPath); } catch(InvalidOperationException) { rejected=true; }
                Check(rejected,"Save As cannot silently replace an existing workspace");
                editor = new StudioModelEditor(()=>{}); editor.Load(doc);
                Check(!editor.CanExportSelection,"Export Selection is disabled with no object selected");
                editor.SelectObject(0); editor.SelectObject(1,true);
                string selectedPath=folder+"/Selected.fbx";
                editor.ExportFile(Path.GetFullPath(selectedPath),true);
                AssetDatabase.ImportAsset(selectedPath,ImportAssetOptions.ForceSynchronousImport);
                var selected=AssetDatabase.LoadAssetAtPath<GameObject>(selectedPath);
                Check(selected && selected.GetComponentsInChildren<MeshFilter>(true).Length==2,"Export Selection includes parent and child once and excludes unrelated objects");
                Check(selected.GetComponentsInChildren<Transform>(true).All(t=>t.name!="Unrelated"),"Unselected hierarchy entries are absent from selection export");
                editor.SelectObject(1);
                string childPath=folder+"/Child.fbx";
                editor.ExportFile(Path.GetFullPath(childPath),true);
                AssetDatabase.ImportAsset(childPath,ImportAssetOptions.ForceSynchronousImport);
                var child=AssetDatabase.LoadAssetAtPath<GameObject>(childPath);
                Check(child && child.GetComponentsInChildren<MeshFilter>(true).Length==1,"A child object can be exported without its parent or siblings");
                var exportMesh=child.GetComponentInChildren<MeshFilter>();
                Check(exportMesh.transform.position.sqrMagnitude<.0001f,"Single-object export uses its current pivot as the origin");
                string allPath=folder+"/All.fbx";
                editor.ExportFile(Path.GetFullPath(allPath),false);
                AssetDatabase.ImportAsset(allPath,ImportAssetOptions.ForceSynchronousImport);
                var all=AssetDatabase.LoadAssetAtPath<GameObject>(allPath);
                Check(all && all.GetComponentsInChildren<MeshFilter>(true).Length==3,"Export All includes all workspace models");
                Check(editor.Transforms[1].parent==editor.Transforms[0] && doc.nodes[1].parent==0,"Exports leave the live hierarchy intact");
                Check(File.ReadAllBytes(originalPath).SequenceEqual(before),"Export does not save outstanding workspace edits");
                doc.Save();
                Check(!File.ReadAllBytes(originalPath).SequenceEqual(before),"Explicit Save persists outstanding edits");
                AssetDatabase.ImportAsset(copyPath,ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var reopened=AssetDatabase.LoadAssetAtPath<StudioModelDocument>(copyPath);
                Check(reopened && reopened.nodes[0].mesh && reopened.nodes[0].mesh.vertices[0]==vertices[0],"Saved workspace reopens with edited mesh data");
                // A selected skin retains required bones, but does not drag in unrelated renderers.
                doc.nodes[1].skinned=true; doc.nodes[1].bones=new[]{2}; doc.nodes[1].rootBone=2;
                var scene=EditorSceneManager.NewPreviewScene();
                try
                {
                    var nodes=doc.Build(scene); editor.FilterExportSelection(nodes);
                    var skin=nodes[1].GetComponent<SkinnedMeshRenderer>();
                    Check(skin.bones[0] && skin.rootBone==nodes[2],"Selection export keeps required skeleton references");
                    Check(!nodes[2].GetComponent<Renderer>() && !nodes[0],"Skeleton dependencies exclude unrelated geometry");
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            catch(Exception error) { results.Add("FAIL: " + error); exit=1; }
            finally { editor?.Dispose(); }
            File.WriteAllLines("file-menu-result.txt",results);
            EditorApplication.Exit(exit);
        }
    }
}
