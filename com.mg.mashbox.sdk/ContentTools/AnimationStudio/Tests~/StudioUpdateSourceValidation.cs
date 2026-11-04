using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioUpdateSourceValidation
    {
        static string Out(string n)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../UpdateSource/"+n));
        [InitializeOnLoadMethod]static void Boot(){if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.update+=RunNext;}
        static void RunNext(){EditorApplication.update-=RunNext;Run();}
        static Mesh Cube(){return StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out _);}
        static GameObject Model(string name,Mesh mesh)
        {var go=new GameObject(name);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>();return go;}
        static void Edit(StudioModelDocument.Node n,float factor)
        {n.mesh.vertices=n.mesh.vertices.Select(v=>v*factor).ToArray();n.mesh.RecalculateBounds();EditorUtility.SetDirty(n.mesh);}
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets"))return;
            File.Delete(Out("request.txt"));var log=new List<string>();
            void Check(bool value,string name){if(!value)throw new Exception(name);log.Add("PASS "+name);}
            StudioModelDocument doc=null;StudioModelEditor editor=null;
            try
            {
                string folder="Assets/SourceUpdate_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
                string meshPath=folder+"/SourceMesh.asset";var sourceMesh=Cube();sourceMesh.name="SourceMesh";AssetDatabase.CreateAsset(sourceMesh,meshPath);AssetDatabase.SaveAssetIfDirty(sourceMesh);string meshGuid=AssetDatabase.AssetPathToGUID(meshPath);
                doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.SetWorkspacePath(StudioModelDocument.NewWorkspacePath("UpdateSourceValidation"));editor=new StudioModelEditor(()=>{});editor.Load(doc);
                editor.ImportDroppedSource(sourceMesh,editor.Load);int meshRoot=editor.Active;
                Check(doc.nodes[meshRoot].sourcePath==meshPath&&doc.nodes[meshRoot].sourceMeshAsset,"Standalone Mesh drop remembers the actual source asset");
                Edit(doc.nodes[meshRoot],1.5f);var beforeLive=doc.nodes[meshRoot].mesh.vertices;editor.UpdateSource(meshRoot);
                Check(AssetDatabase.AssetPathToGUID(meshPath)==meshGuid&&Mathf.Abs(sourceMesh.bounds.size.x-1.5f)<1e-5f,"Mesh update preserves GUID and overwrites current geometry");
                Check(doc.nodes[meshRoot].mesh.vertices.SequenceEqual(beforeLive),"Source update leaves live workspace geometry unchanged");
                string prefabPath=folder+"/SourcePrefab.prefab";var go=Model("SourcePrefab",sourceMesh);go.AddComponent<BoxCollider>();var child=Model("RetainedChild",sourceMesh);child.transform.SetParent(go.transform,false);child.transform.localPosition=Vector3.up*2;child.AddComponent<MeshCollider>().sharedMesh=sourceMesh;var otherChild=Model("OtherChild",sourceMesh);otherChild.transform.SetParent(go.transform,false);otherChild.AddComponent<SphereCollider>();
                PrefabUtility.SaveAsPrefabAsset(go,prefabPath);UnityEngine.Object.DestroyImmediate(go);string prefabGuid=AssetDatabase.AssetPathToGUID(prefabPath);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);editor.ImportDroppedSource(prefab,editor.Load);int prefabRoot=editor.Active;int prefabChild=Enumerable.Range(0,doc.nodes.Count).Single(i=>doc.nodes[i].parent==prefabRoot&&doc.nodes[i].name=="RetainedChild");
                Check(editor.SourceRootFor(prefabChild)==prefabRoot&&doc.nodes[prefabChild].sourceNodePath=="0","Imported prefab child resolves its remembered file root and node path");
                Edit(doc.nodes[prefabChild],2);doc.nodes[prefabChild].name="RenamedChild";doc.nodes[prefabChild].position=Vector3.up*3;int otherIndex=Enumerable.Range(0,doc.nodes.Count).Single(i=>doc.nodes[i].parent==prefabRoot&&doc.nodes[i].name=="OtherChild");doc.nodes[prefabChild].siblingOrder=1;doc.nodes[otherIndex].siblingOrder=0;var selection=editor.Active;editor.UpdateSource(prefabChild);
                prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);var updatedChild=prefab.transform.Find("RenamedChild");
                Check(prefab&&prefab.GetComponent<BoxCollider>()&&updatedChild&&updatedChild.GetComponent<MeshCollider>(),"Prefab update retains existing collider components across node rename");
                Check(doc.nodes[prefabChild].sourceNodePath=="1"&&doc.nodes[otherIndex].sourceNodePath=="0","Successful prefab update refreshes source paths after sibling reorder");var storedMesh=updatedChild.GetComponent<MeshFilter>().sharedMesh;string storePath=Path.ChangeExtension(prefabPath,null)+".StudioMeshes.asset";
                Check(AssetDatabase.GetAssetPath(storedMesh)==storePath&&Mathf.Abs(storedMesh.bounds.size.x-3)<1e-5f,"Prefab mesh references persist in its owned companion asset");
                Check(updatedChild.localPosition==Vector3.up*3&&updatedChild.GetComponent<MeshCollider>().sharedMesh==storedMesh,"Prefab transforms and matching MeshCollider follow edited geometry");
                Check(AssetDatabase.AssetPathToGUID(prefabPath)==prefabGuid&&editor.Active==selection,"Prefab source GUID and Studio selection remain unchanged");
                string meshStoreGuid=AssetDatabase.AssetPathToGUID(storePath);AssetDatabase.TryGetGUIDAndLocalFileIdentifier(storedMesh,out string storedGuid,out long storedId);Edit(doc.nodes[prefabChild],.5f);editor.UpdateSource(prefabRoot);
                storedMesh=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).transform.Find("RenamedChild").GetComponent<MeshFilter>().sharedMesh;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(storedMesh,out string nextGuid,out long nextId);
                Check(meshStoreGuid==AssetDatabase.AssetPathToGUID(storePath)&&storedGuid==nextGuid&&storedId==nextId&&Mathf.Abs(storedMesh.bounds.size.x-1.5f)<1e-5f,"Repeated prefab update retains companion GUID/local mesh identity");
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).transform.Find("RenamedChild").GetComponent<MeshCollider>()&&AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).transform.Find("OtherChild").GetComponent<SphereCollider>(),"Repeated update after reorder keeps components on the correct nodes");string workspace=StudioModelDocument.NewWorkspacePath("SourceProvenanceRoundtrip");StudioWorkspaceText.Write(doc,workspace);var restored=StudioWorkspaceText.Read(workspace);
                try{Check(restored.nodes[prefabRoot].sourcePath==prefabPath&&restored.nodes[prefabChild].sourceNodePath=="1"&&restored.nodes[meshRoot].sourceMeshAsset,"Workspace text roundtrip retains source provenance");}
                finally{foreach(var m in restored.nodes.Select(n=>n.mesh).Where(m=>m).Distinct())UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(restored);}
                Check(editor.HierarchyVisibilityMenu(prefabChild).Items.Any(i=>i.Text=="Update Source"&&i.Enabled),"Hierarchy context exposes Update Source for an imported child");
                Check(StudioFbxDependency.Available,"Actual FBX Exporter API is available for source roundtrip");
                string fbxPath=folder+"/SourceModel.fbx";var fbx=Model("FbxRoot",sourceMesh);var fbxChild=Model("FbxChild",sourceMesh);fbxChild.transform.SetParent(fbx.transform,false);fbxChild.transform.localPosition=Vector3.right*2;
                try{StudioModelEditor.ExportTo(Path.GetFullPath(fbxPath),fbx);}finally{UnityEngine.Object.DestroyImmediate(fbx);}
                AssetDatabase.ImportAsset(fbxPath,ImportAssetOptions.ForceSynchronousImport);var importer=(ModelImporter)AssetImporter.GetAtPath(fbxPath);importer.isReadable=true;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();string fbxGuid=AssetDatabase.AssetPathToGUID(fbxPath);
                editor.ImportDroppedSource(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath),editor.Load);int fbxRoot=editor.Active;int fbxMesh=doc.WithDescendants(new[]{fbxRoot}).First(i=>doc.nodes[i].mesh);Edit(doc.nodes[fbxMesh],1.2f);editor.SelectObject(meshRoot);int active=editor.Active;editor.UpdateSource(fbxMesh);
                var reimported=AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                Check(reimported.GetComponentsInChildren<MeshFilter>(true).Length==2,"FBX overwrite contains the source branch only, excluding other workspace models");
                Check(Mathf.Abs(reimported.GetComponentsInChildren<MeshFilter>(true).Max(f=>f.sharedMesh.bounds.size.x)-doc.WithDescendants(new[]{fbxRoot}).Where(i=>doc.nodes[i].mesh).Max(i=>doc.nodes[i].mesh.bounds.size.x))<1e-4f,"Reimported FBX contains the edited evaluated mesh dimensions");
                Check(AssetDatabase.AssetPathToGUID(fbxPath)==fbxGuid&&((ModelImporter)AssetImporter.GetAtPath(fbxPath)).materialImportMode==ModelImporterMaterialImportMode.None,"FBX overwrite retains GUID and importer material policy");
                Check(editor.Active==active&&doc.nodes[meshRoot].sourcePath==meshPath,"FBX update does not replace Studio selection or unrelated provenance");
                byte[] bytes=File.ReadAllBytes(Path.GetFullPath(fbxPath));string previous=doc.nodes[fbxRoot].sourcePath;doc.nodes[fbxRoot].sourcePath=folder+"/missing.fbx";bool rejected=false;try{editor.UpdateSource(fbxMesh);}catch(InvalidOperationException){rejected=true;}doc.nodes[fbxRoot].sourcePath=previous;
                Check(rejected&&bytes.SequenceEqual(File.ReadAllBytes(Path.GetFullPath(fbxPath))),"Missing source fails before writing another file");
                string external=Out("External_"+Guid.NewGuid().ToString("N")+".fbx");File.Copy(Path.GetFullPath(fbxPath),external);
                var method=typeof(StudioModelEditor).GetMethod("ImportPath",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                method.Invoke(editor,new object[]{external,(Action<StudioModelDocument>)editor.Load});int externalRoot=editor.Active;
                Check(doc.nodes[externalRoot].sourcePath==external,"External FBX import remembers the original file rather than its Unity cache");
                string cache="Assets/Studio Imports/"+Path.GetFileName(external);var cacheBytes=File.ReadAllBytes(Path.GetFullPath(cache));var externalBytes=File.ReadAllBytes(external);
                int externalMesh=doc.WithDescendants(new[]{externalRoot}).First(i=>doc.nodes[i].mesh);Edit(doc.nodes[externalMesh],1.1f);editor.UpdateSource(externalMesh);
                Check(!externalBytes.SequenceEqual(File.ReadAllBytes(external))&&cacheBytes.SequenceEqual(File.ReadAllBytes(Path.GetFullPath(cache))),"External source overwrite leaves the cached import file untouched");
                log.Add("ASSETS "+folder);
            }
            catch(Exception e){log.Add("FAIL "+e);}
            finally{editor?.Dispose();if(doc){foreach(var m in doc.nodes.Select(n=>n.mesh).Where(m=>m).Distinct())UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(doc);}File.WriteAllLines(Out("result.txt"),log);}
        }
    }
}
