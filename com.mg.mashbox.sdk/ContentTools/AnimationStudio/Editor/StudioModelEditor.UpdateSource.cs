using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal int SourceRootFor(int index)
        {
            if (!Document) return -1;
            var visited = new HashSet<int>();
            for (int at=index; at>=0 && at<Document.nodes.Count && visited.Add(at); at=Document.nodes[at].parent)
                if (!string.IsNullOrEmpty(Document.nodes[at].sourcePath)) return at;
            return -1;
        }
        private static string SourceFullPath(string path)
        {
            string normalized=path.Replace((char)92,'/');
            if(!Path.IsPathRooted(path)&&(normalized.StartsWith("Assets/",StringComparison.Ordinal)||normalized.StartsWith("Packages/",StringComparison.Ordinal)))
                return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath),normalized));
            return Path.GetFullPath(path);
        }
        private static string SourceProjectPath(string path)
        {
            string full=SourceFullPath(path).Replace((char)92,'/');
            string prefix=Path.GetDirectoryName(Application.dataPath).Replace((char)92,'/').TrimEnd('/')+"/";
            return full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)?full.Substring(prefix.Length):"";
        }
        private string UpdateSourceProblem(int root)
        {
            if (root<0) return "This item has no imported source file.";
            string path=Document.nodes[root].sourcePath;
            if (!File.Exists(SourceFullPath(path))) return "The remembered source file no longer exists: "+path;
            string ext=Path.GetExtension(path).ToLowerInvariant();
            if (Document.nodes[root].sourceMeshAsset && ext!=".asset") return "Update Source supports standalone Mesh assets, not meshes embedded in another model file. Drop its complete model root instead.";
            if (ext!=".fbx" && ext!=".prefab" && ext!=".asset") return "Update Source supports FBX, Unity prefab and standalone Mesh asset files.";
            if ((File.GetAttributes(SourceFullPath(path))&FileAttributes.ReadOnly)!=0) return "The source file is read-only: "+path;
            if (ext!=".fbx")
            {
                string asset=SourceProjectPath(path);
                if (!asset.StartsWith("Assets/",StringComparison.Ordinal)) return "Prefab and Mesh source updates require a writable asset under Assets.";
                if (ext==".asset" && !(AssetDatabase.LoadMainAssetAtPath(asset) is Mesh)) return "The source asset is not a standalone Mesh.";
                if (ext==".prefab" && !AssetDatabase.LoadAssetAtPath<GameObject>(asset)) return "The source prefab could not be loaded.";
            }
            return null;
        }
        private void AddUpdateSourceMenu(StudioMenu menu,int index)
        {
            int root=SourceRootFor(index);
            if(root<0)return;
            string problem=UpdateSourceProblem(root);
            var label=new GUIContent("Update Source",problem??("Overwrite "+Document.nodes[root].sourcePath+" with this imported model's current Studio geometry."));
            if(problem==null)menu.AddItem(label,false,()=>Run(()=>UpdateSource(index)));
            else menu.AddDisabledItem(label);
            menu.AddSeparator("");
        }
        internal void UpdateSource(int index)
        {
            int root=SourceRootFor(index);string problem=UpdateSourceProblem(root);
            if(problem!=null)throw new InvalidOperationException(problem);
            FinishLiveTool();FlushModifierEdits();
            root=SourceRootFor(index);problem=UpdateSourceProblem(root);
            if(problem!=null)throw new InvalidOperationException(problem);
            string path=Document.nodes[root].sourcePath;
            switch(Path.GetExtension(path).ToLowerInvariant())
            {
                case ".fbx":UpdateSourceFbx(root,path);break;
                case ".prefab":UpdateSourcePrefab(root,SourceProjectPath(path));break;
                case ".asset":UpdateSourceMesh(root,SourceProjectPath(path));break;
            }
            NotifyFile(path,"updated");repaint();
        }
        private static void ImportUpdatedSource(string path)
        {
            string relative=SourceProjectPath(path);
            if(relative.StartsWith("Assets/",StringComparison.Ordinal)||relative.StartsWith("Packages/",StringComparison.Ordinal))
                AssetDatabase.ImportAsset(relative,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        }
        private void UpdateSourceFbx(int root,string path)
        {
            path=SourceFullPath(path);
            string token=Guid.NewGuid().ToString("N");
            string staging=Path.Combine(Path.GetDirectoryName(path),"."+Path.GetFileNameWithoutExtension(path)+"-studio-"+token+".fbx");
            string backup=staging+".backup";bool replaced=false;
            try
            {
                ExportFile(staging,false,root);
                if(new FileInfo(staging).Length==0)throw new IOException("The exported FBX is empty; source was not updated.");
                File.Replace(staging,path,backup);replaced=true;
                ImportUpdatedSource(path);
            }
            catch
            {
                if(replaced&&File.Exists(backup)){File.Copy(backup,path,true);ImportUpdatedSource(path);}
                throw;
            }
            finally
            {
                foreach(string file in new[]{staging,staging+".meta",backup})if(File.Exists(file))File.Delete(file);
            }
        }
        private void UpdateSourceMesh(int root,string path)
        {
            var branch=Document.WithDescendants(new[]{root});
            if(branch.Count!=1 || !Document.nodes[root].mesh)
                throw new InvalidOperationException("A standalone Mesh source requires one mesh node without a child hierarchy. Export a model instead.");
            var target=AssetDatabase.LoadMainAssetAtPath(path) as Mesh;
            if(!target)throw new InvalidOperationException("The source asset is not a standalone Mesh.");
            var before=UnityEngine.Object.Instantiate(target);string name=target.name;
            try
            {
                StudioMeshToolPreview.CopyMesh(Document.nodes[root].mesh,target);target.name=name;
                EditorUtility.SetDirty(target);AssetDatabase.SaveAssetIfDirty(target);
            }
            catch
            {
                StudioMeshToolPreview.CopyMesh(before,target);target.name=name;
                EditorUtility.SetDirty(target);AssetDatabase.SaveAssetIfDirty(target);throw;
            }
            finally{UnityEngine.Object.DestroyImmediate(before);}
        }
        private string SourceBranchPath(int index,int root)
        {
            var parts=new List<string>();
            for(int at=index;at!=root;at=Document.nodes[at].parent)
            {
                int parent=Document.nodes[at].parent;
                var siblings=Enumerable.Range(0,Document.nodes.Count).Where(i=>Document.nodes[i].parent==parent)
                    .OrderBy(i=>Document.nodes[i].siblingOrder<0?i:Document.nodes[i].siblingOrder).ThenBy(i=>i).ToArray();
                parts.Add(Array.IndexOf(siblings,at).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            parts.Reverse();return string.Join("/",parts);
        }
        private void UpdateSourcePrefab(int root,string path)
        {
            var included=Document.WithDescendants(new[]{root});
            foreach(int i in included)
                if(Document.nodes[i].skinned && (Document.nodes[i].bones.Any(b=>b>=0&&!included.Contains(b)) || Document.nodes[i].rootBone>=0&&!included.Contains(Document.nodes[i].rootBone)))
                    throw new InvalidOperationException("The source prefab's skin depends on a bone outside its imported branch.");
            string meshesPath=Path.ChangeExtension(path,null)+".StudioMeshes.asset";
            string owner="MashBoxStudio.SourceMeshes:"+AssetDatabase.AssetPathToGUID(path);
            bool hasMeshes=included.Any(i=>Document.nodes[i].mesh),exists=File.Exists(Path.GetFullPath(meshesPath));
            if(hasMeshes&&exists&&AssetImporter.GetAtPath(meshesPath)?.userData!=owner)
                throw new InvalidOperationException("The companion mesh asset path is already used by another asset: "+meshesPath);
            byte[] prefabBackup=File.ReadAllBytes(SourceFullPath(path));
            byte[] meshesBackup=exists?File.ReadAllBytes(Path.GetFullPath(meshesPath)):null;
            GameObject contents=null;bool createdMeshes=false;
            try
            {
                contents=PrefabUtility.LoadPrefabContents(path);
                var old=contents.GetComponentsInChildren<Transform>(true);
                var byPath=old.ToDictionary(t=>StudioModelDocument.SourceTransformPath(t,contents.transform));
                var mapped=new Dictionary<int,Transform>{{root,contents.transform}};var used=new HashSet<Transform>{contents.transform};
                foreach(int i in included.Where(i=>i!=root))
                {
                    var node=Document.nodes[i];string key=node.sourceNodePath??SourceBranchPath(i,root);
                    if(!byPath.TryGetValue(key,out var transform)||used.Contains(transform))
                    {var go=new GameObject(node.name);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,contents.scene);transform=go.transform;}
                    mapped[i]=transform;used.Add(transform);
                }
                foreach(int i in included)
                {
                    var node=Document.nodes[i];var t=mapped[i];t.name=node.name;
                    if(i!=root)t.SetParent(mapped[node.parent],false);
                    Document.ApplyTransform(i,t);t.gameObject.SetActive(node.active);
                }
                foreach(var t in old.Reverse())if(t&&!used.Contains(t))UnityEngine.Object.DestroyImmediate(t.gameObject);
                foreach(var parent in mapped.Values)
                {
                    int at=0;
                    foreach(int i in included.Where(i=>i!=root&&mapped[Document.nodes[i].parent]==parent)
                        .OrderBy(i=>Document.nodes[i].siblingOrder<0?i:Document.nodes[i].siblingOrder).ThenBy(i=>i))mapped[i].SetSiblingIndex(at++);
                }
                var stored=exists?AssetDatabase.LoadAllAssetsAtPath(meshesPath).OfType<Mesh>().ToDictionary(m=>m.name):new Dictionary<string,Mesh>();
                var copies=new Dictionary<Mesh,Mesh>();
                foreach(int i in included)
                {
                    var node=Document.nodes[i];var go=mapped[i].gameObject;var oldMesh=go.GetComponent<MeshFilter>()?.sharedMesh??go.GetComponent<SkinnedMeshRenderer>()?.sharedMesh;
                    if(!node.mesh)
                    {
                        if(go.TryGetComponent<MeshFilter>(out var filter))UnityEngine.Object.DestroyImmediate(filter);
                        if(go.TryGetComponent<Renderer>(out var renderer))UnityEngine.Object.DestroyImmediate(renderer);
                        continue;
                    }
                    if(!copies.TryGetValue(node.mesh,out var copy))
                    {
                        string key="StudioMesh:"+SourceBranchPath(i,root);
                        if(!stored.TryGetValue(key,out copy))
                        {
                            copy=UnityEngine.Object.Instantiate(node.mesh);copy.name=key;copy.hideFlags=HideFlags.None;
                            if(!exists&&!createdMeshes){AssetDatabase.CreateAsset(copy,meshesPath);createdMeshes=true;var importer=AssetImporter.GetAtPath(meshesPath);importer.userData=owner;AssetDatabase.WriteImportSettingsIfDirty(meshesPath);}
                            else AssetDatabase.AddObjectToAsset(copy,meshesPath);
                            stored[key]=copy;
                        }
                        else{StudioMeshToolPreview.CopyMesh(node.mesh,copy);copy.name=key;}
                        EditorUtility.SetDirty(copy);copies[node.mesh]=copy;
                    }
                    if(node.skinned)
                    {
                        if(go.TryGetComponent<MeshFilter>(out var filter))UnityEngine.Object.DestroyImmediate(filter);
                        if(go.TryGetComponent<MeshRenderer>(out var renderer))UnityEngine.Object.DestroyImmediate(renderer);
                        var skin=go.GetComponent<SkinnedMeshRenderer>()??go.AddComponent<SkinnedMeshRenderer>();
                        skin.sharedMesh=copy;skin.sharedMaterials=node.materials;skin.bones=node.bones.Select(b=>b>=0?mapped[b]:null).ToArray();skin.rootBone=node.rootBone>=0?mapped[node.rootBone]:null;skin.localBounds=copy.bounds;
                    }
                    else
                    {
                        if(go.TryGetComponent<SkinnedMeshRenderer>(out var skin))UnityEngine.Object.DestroyImmediate(skin);
                        (go.GetComponent<MeshFilter>()??go.AddComponent<MeshFilter>()).sharedMesh=copy;
                        (go.GetComponent<MeshRenderer>()??go.AddComponent<MeshRenderer>()).sharedMaterials=node.materials;
                    }
                    foreach(var collider in go.GetComponents<MeshCollider>())if(collider.sharedMesh==oldMesh)collider.sharedMesh=copy;
                }
                foreach(var mesh in copies.Values)AssetDatabase.SaveAssetIfDirty(mesh);
                PrefabUtility.SaveAsPrefabAsset(contents,path,out bool success);
                if(!success)throw new IOException("Unity could not save the source prefab.");
                // Subsequent updates resolve against the file's new hierarchy,
                // including inserted, reordered and reparented Studio nodes.
                foreach(int i in included)Document.nodes[i].sourceNodePath=StudioModelDocument.SourceTransformPath(mapped[i],contents.transform);
                EditorUtility.SetDirty(Document);WorkspaceSaveStateChanged?.Invoke();
            }
            catch
            {
                File.WriteAllBytes(SourceFullPath(path),prefabBackup);
                if(meshesBackup!=null){File.WriteAllBytes(Path.GetFullPath(meshesPath),meshesBackup);ImportUpdatedSource(meshesPath);}
                else if(createdMeshes)AssetDatabase.DeleteAsset(meshesPath);
                ImportUpdatedSource(path);throw;
            }
            finally{if(contents)PrefabUtility.UnloadPrefabContents(contents);}
        }
    }
}
