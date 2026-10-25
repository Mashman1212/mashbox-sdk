using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private static string PhysicalFbxPath(string assetPath)
        {
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal)) return Path.GetFullPath(assetPath);
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (package == null || (package.source != UnityEditor.PackageManager.PackageSource.Local && package.source != UnityEditor.PackageManager.PackageSource.Embedded))
                throw new InvalidOperationException("This source is in a read-only package. Move the clothing into Assets or a local SDK package before saving it.");
            return Path.GetFullPath(Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length).TrimStart('/')));
        }
        private void LinkSelectedFbx()
        {
            string file = EditorUtility.OpenFilePanel("Locate the original clothing FBX (one time)", Application.dataPath, "fbx");
            if (string.IsNullOrEmpty(file)) return;
            string path = FileUtil.GetProjectRelativePath(file);
            if (string.IsNullOrEmpty(path))
                foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                {
                    string root = Path.GetFullPath(package.resolvedPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    { path = package.assetPath + "/" + Path.GetFullPath(file).Substring(root.Length).Replace('\\','/'); break; }
                }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!model) throw new InvalidOperationException("Choose the original FBX already imported in this project or the SDK.");
            var matches = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name == Part.name && r.sharedMesh && r.sharedMesh.vertexCount == Part.mesh.vertexCount).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("This FBX does not uniquely match the selected clothing. No file was changed.");
            Undo.RecordObject(Document, "Link clothing source FBX");
            Part.sourceMesh = matches[0].sharedMesh;
            Part.sourceRendererPath = AnimationUtility.CalculateTransformPath(matches[0].transform, model.transform);
            Part.sourceToWorkspace = model.transform.worldToLocalMatrix * matches[0].transform.localToWorldMatrix;
            EditorUtility.SetDirty(Document);
        }
        internal void SaveSelectedFbx()
        {
            EndStroke();
            if (IsReferencePart) throw new InvalidOperationException("The default body and bust are reference-only. Select clothing to save.");
            if (Part == null || !Part.bound) throw new InvalidOperationException("Select a skinned clothing mesh first.");
            if (!Part.sourceMesh) LinkSelectedFbx();
            if (!Part.sourceMesh) return;
            SavePartFbx(Part);
        }
#if MashBoxDev
        internal void SaveDefaultBustFbx()
        {
            var defaults = AssetDatabase.LoadAssetAtPath<StudioSkinningDefaults>("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if (!defaults || !defaults.bust) throw new InvalidOperationException("Assign a default bust first.");
            var meshes = new HashSet<Mesh>(defaults.bust.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh));
            var parts = Document.parts.Where(p => p.referenceOnly && meshes.Contains(p.sourceMesh)).ToArray();
            if (parts.Length != 1) throw new InvalidOperationException("Use Save FBX on the individual bust row when the default bust has multiple meshes. Load the default bust first if none is present.");
            SaveDefaultBustFbx(parts[0]);
        }
        private void SaveDefaultBustFbx(StudioSkinningDocument.Part part)
        {
            EndStroke();
            var defaults = AssetDatabase.LoadAssetAtPath<StudioSkinningDefaults>("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if (!Document.parts.Contains(part) || !part.referenceOnly || !part.bound || !part.sourceMesh || !defaults || !defaults.bust ||
                !defaults.bust.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.sharedMesh == part.sourceMesh))
                throw new InvalidOperationException("Select the loaded default bust to save its FBX.");
            SavePartFbx(part);
        }
#endif
        private void SavePartFbx(StudioSkinningDocument.Part part)
        {
            string assetPath = AssetDatabase.GetAssetPath(part.sourceMesh);
            if (!string.Equals(Path.GetExtension(assetPath), ".fbx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected mesh did not come from an FBX. Save FBX needs the original clothing model.");
            string destination = PhysicalFbxPath(assetPath);
            if (AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().Any(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)))
                throw new InvalidOperationException("This FBX contains animation clips. Saving clothing over an animated FBX is not supported; no file was changed.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var sourceIds = AssetDatabase.LoadAllAssetsAtPath(assetPath).Where(a => a && !a.name.StartsWith("__preview__", StringComparison.Ordinal)).Select(AssetIdentity).ToArray();
            var sourceRenderer = FindSourceRenderer(source, part.sourceRendererPath);
            if (!sourceRenderer || sourceRenderer.sharedMesh != part.sourceMesh)
                throw new InvalidOperationException("The original clothing renderer changed. No file was changed; import its current version before saving.");
            string originalName = part.sourceMesh.name;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(part.sourceMesh, out string originalGuid, out long originalId);
            string work = Path.GetFullPath("Library/MashBox/SkinningFbx/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string staged = Path.Combine(work, Path.GetFileName(destination));
            string backup = Path.Combine(work, "Original.fbx");
            string metaBackup = Path.Combine(work, "Original.fbx.meta");
            string swap = destination + ".studio-" + Guid.NewGuid().ToString("N") + ".tmp";
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null; Mesh edited = null;
            var readableCopies = new List<Mesh>();
            bool replaced = false;
            try
            {
                root = Object.Instantiate(source); root.name = source.name;
                SceneManager.MoveGameObjectToScene(root, scene);
                // The FBX exporter also reads meshes other than the edited one.
                // Make detached CPU-readable copies without changing import settings.
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.sharedMesh)
                    {
                        var copy = StudioSkinningDocument.CreateEditableCopy(renderer.sharedMesh);
                        copy.name = renderer.sharedMesh.name; readableCopies.Add(copy); renderer.sharedMesh = copy;
                    }
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh)
                    {
                        var copy = StudioSkinningDocument.CreateEditableCopy(filter.sharedMesh);
                        copy.name = filter.sharedMesh.name; readableCopies.Add(copy); filter.sharedMesh = copy;
                    }
                var target = FindSourceRenderer(root, part.sourceRendererPath);
                edited = Object.Instantiate(part.mesh); edited.name = originalName; edited.hideFlags = HideFlags.None;
                StudioSkinningDocument.TransformGeometry(edited, part.sourceToWorkspace.inverse);
                var skin = new StudioSkinWeights(edited);
                var all = root.GetComponentsInChildren<Transform>(true);
                var targetBones = new List<Transform>(target.bones);
                var bindposes = new List<Matrix4x4>(part.sourceMesh.bindposes);
                var mapping = Enumerable.Repeat(-1, bones.Length).ToArray();
                foreach (int b in skin.Values.SelectMany(v => v).Where(w => w.weight > 0).Select(w => w.boneIndex).Distinct())
                {
                    var matches = all.Where(t => StudioSkinningDocument.BoneName(t.name) == StudioSkinningDocument.BoneName(bones[b].name)).ToArray();
                    if (matches.Length != 1) throw new InvalidOperationException("The source FBX has no unique bone for " + bones[b].name + ". No file was changed.");
                    int index = targetBones.IndexOf(matches[0]);
                    if (index < 0) { index = targetBones.Count; targetBones.Add(matches[0]); bindposes.Add(Matrix4x4.identity); }
                    while (bindposes.Count <= index) bindposes.Add(Matrix4x4.identity);
                    var sourceRest = root.transform.worldToLocalMatrix * matches[0].localToWorldMatrix;
                    bindposes[index] = part.referenceOnly ? part.mesh.bindposes[b] * part.sourceToWorkspace
                        : sourceRest.inverse * rest[b] * part.mesh.bindposes[b] * part.sourceToWorkspace;
                    mapping[b] = index;
                }
                skin.Remap(mapping); skin.Apply(edited); edited.bindposes = bindposes.ToArray();
                target.sharedMesh = edited; target.bones = targetBones.ToArray();
                ExportClothingFbx(staged, root);
                if (!File.Exists(staged) || new FileInfo(staged).Length == 0) throw new IOException("FBX export produced no data.");
                File.Copy(destination, backup);
                if (File.Exists(destination + ".meta")) File.Copy(destination + ".meta", metaBackup);
                File.Copy(staged, swap);
                File.Replace(swap, destination, null); replaced = true;
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var savedModel = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                var savedIds = new HashSet<string>(AssetDatabase.LoadAllAssetsAtPath(assetPath).Where(a => a).Select(AssetIdentity));
                var missingIds = sourceIds.Where(identity => !savedIds.Contains(identity)).ToArray();
                if (missingIds.Length > 0)
                    throw new InvalidOperationException("The FBX round trip would change existing model/subasset references: " + string.Join("; ",missingIds.Take(8)));
                var savedRenderer = FindSourceRenderer(savedModel, part.sourceRendererPath);
                if (!savedRenderer || !savedRenderer.sharedMesh) throw new InvalidOperationException("The exported clothing could not be reimported.");
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(savedRenderer.sharedMesh, out string guid, out long id);
                if (guid != originalGuid || id != originalId) throw new InvalidOperationException("The FBX round trip would change the clothing mesh reference.");
                // Inspect a detached readable copy; keep the source import settings intact.
                var check = StudioSkinningDocument.CreateEditableCopy(savedRenderer.sharedMesh);
                try
                {
                    if (check.subMeshCount != edited.subMeshCount || check.blendShapeCount != edited.blendShapeCount)
                        throw new InvalidOperationException($"The FBX round trip changed submeshes ({edited.subMeshCount} to {check.subMeshCount}) or blend shapes ({edited.blendShapeCount} to {check.blendShapeCount}).");
                    if (check.bounds.size != edited.bounds.size && (check.bounds.size-edited.bounds.size).magnitude > .001f)
                        throw new InvalidOperationException("The FBX round trip changed the clothing scale.");
                    if (!new StudioSkinWeights(check).HasWeights) throw new InvalidOperationException("The FBX round trip lost skin weights.");
                    if (!TriangleSignatures(check, savedRenderer.bones).SequenceEqual(TriangleSignatures(edited, target.bones)))
                        throw new InvalidOperationException($"The FBX round trip changed triangle geometry, normals, UVs, blend shapes or skin influences (vertices {edited.vertexCount} to {check.vertexCount}).");
                }
                finally { Object.DestroyImmediate(check); }
                part.sourceMesh = savedRenderer.sharedMesh; EditorUtility.SetDirty(Document);
                EditorGUIUtility.PingObject(savedModel);
                lastView?.ShowNotification(new GUIContent("Saved " + Path.GetFileName(destination)));
                Debug.Log("Saved selected clothing to " + assetPath + ". Original FBX backup: " + backup);
            }
            catch (Exception e)
            {
                if (replaced)
                {
                    File.Copy(backup, swap, true); File.Replace(swap, destination, null);
                    if (File.Exists(metaBackup)) File.Copy(metaBackup, destination + ".meta", true);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    throw new InvalidOperationException(e.GetBaseException().Message + " The original FBX was restored; your Studio edits are still open.", e);
                }
                throw;
            }
            finally
            {
                if (File.Exists(swap)) File.Delete(swap);
                if (root) Object.DestroyImmediate(root);
                if (edited) Object.DestroyImmediate(edited);
                foreach (var copy in readableCopies) if (copy) Object.DestroyImmediate(copy);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static string AssetIdentity(Object asset)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id);
            return asset.GetType().FullName + "|" + asset.name + "|" + guid + "|" + id;
        }
        private static string[] TriangleSignatures(Mesh mesh, Transform[] rig)
        {
            var points = mesh.vertices; var uv = mesh.uv; var skin = new StudioSkinWeights(mesh);
            var normals = mesh.normals;
            var signatures = new string[points.Length];
            for (int v = 0; v < points.Length; v++)
            {
                var p = points[v]; var t = uv.Length == points.Length ? uv[v] : Vector2.zero;
                string influences = string.Join(",", skin.Values[v].Where(w => w.weight > .0005f).Select(w =>
                    (w.boneIndex >= 0 && w.boneIndex < rig.Length && rig[w.boneIndex] ? AnimationUtility.CalculateTransformPath(rig[w.boneIndex], rig[w.boneIndex].root) : "missing") + ":" + Mathf.RoundToInt(w.weight * 1000)).OrderBy(s => s, StringComparer.Ordinal));
                signatures[v] = Mathf.RoundToInt(p.x*10000)+","+Mathf.RoundToInt(p.y*10000)+","+Mathf.RoundToInt(p.z*10000)+"|"+Mathf.RoundToInt(t.x*10000)+","+Mathf.RoundToInt(t.y*10000)+"|"+influences;
                if(normals.Length == points.Length) signatures[v] += "|N:"+QuantizedVector(normals[v],1000);
            }
            // Blend shape deltas must follow the same corners after reindexing.
            for(int shape=0;shape<mesh.blendShapeCount;shape++)
                for(int frame=0;frame<mesh.GetBlendShapeFrameCount(shape);frame++)
                {
                    var delta=new Vector3[points.Length]; var dn=new Vector3[points.Length]; var dt=new Vector3[points.Length];
                    mesh.GetBlendShapeFrameVertices(shape,frame,delta,dn,dt);
                    string key="|S:"+mesh.GetBlendShapeName(shape)+":"+mesh.GetBlendShapeFrameWeight(shape,frame).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                    for(int v=0;v<points.Length;v++) signatures[v]+=key+":"+QuantizedVector(delta[v],10000)+":"+QuantizedVector(dn[v],1000)+":"+QuantizedVector(dt[v],1000);
                }
            var triangles=new List<string>();
            for(int submesh=0;submesh<mesh.subMeshCount;submesh++)
            {
                if(mesh.GetTopology(submesh)!=MeshTopology.Triangles) throw new InvalidOperationException("FBX validation requires triangle submeshes.");
                var indices=mesh.GetTriangles(submesh);
                for(int i=0;i<indices.Length;i+=3)
                {
                    string a=signatures[indices[i]],b=signatures[indices[i+1]],c=signatures[indices[i+2]];
                    // Canonical cyclic rotation preserves winding and triangle
                    // multiplicity while ignoring vertex order/splits/welds.
                    string first=a+"/"+b+"/"+c, second=b+"/"+c+"/"+a, third=c+"/"+a+"/"+b;
                    string canonical=StringComparer.Ordinal.Compare(first,second)<=0 ? first : second;
                    if(StringComparer.Ordinal.Compare(third,canonical)<0) canonical=third;
                    triangles.Add(submesh+":"+canonical);
                }
            }
            triangles.Sort(StringComparer.Ordinal); return triangles.ToArray();
        }
        private static string QuantizedVector(Vector3 value,float scale) => Mathf.RoundToInt(value.x*scale)+","+Mathf.RoundToInt(value.y*scale)+","+Mathf.RoundToInt(value.z*scale);
        private static SkinnedMeshRenderer FindSourceRenderer(GameObject root, string path)
        {
            if (!root || path == null) return null;
            var transform = path.Length == 0 ? root.transform : root.transform.Find(path);
            return transform ? transform.GetComponent<SkinnedMeshRenderer>() : null;
        }
        private static void ExportClothingFbx(string path, GameObject root)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter")).FirstOrDefault(t => t != null);
            var optionsType = type?.Assembly.GetType("UnityEditor.Formats.Fbx.Exporter.ExportModelOptions");
            // Unity adds a file-named wrapper when importing multiple FBX roots.
            // Exporting that wrapper duplicates the mesh name and causes the
            // exporter to suffix the actual node (_1), breaking its local ID.
            bool fileWrapper = !root.GetComponent<Renderer>() && root.transform.childCount > 1 &&
                root.transform.Cast<Transform>().Any(t => t.name == root.name && t.GetComponent<Renderer>());
            var method = optionsType == null ? null : type.GetMethod(fileWrapper ? "ExportObjects" : "ExportObject", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), fileWrapper ? typeof(Object[]) : typeof(Object), optionsType }, null);
            if (method == null) throw new InvalidOperationException("Install Unity's FBX Exporter package to save clothing as FBX.");
            var options = Activator.CreateInstance(optionsType);
            optionsType.GetProperty("UseMayaCompatibleNames")?.SetValue(options, false);
            optionsType.GetProperty("PreserveImportSettings")?.SetValue(options, true);
            var include = optionsType.GetProperty("ModelAnimIncludeOption"); include.SetValue(options, Enum.Parse(include.PropertyType, "Model"));
            var position = optionsType.GetProperty("ObjectPosition"); position.SetValue(options, Enum.Parse(position.PropertyType, "WorldAbsolute"));
            Object[] roots = fileWrapper ? root.transform.Cast<Transform>().Select(t => (Object)t.gameObject).ToArray() : null;
            // Detach the original roots while preserving their world transforms,
            // so exporter dependency discovery cannot reintroduce the wrapper.
            if (fileWrapper) foreach (GameObject child in roots) child.transform.SetParent(null,true);
            string result;
            try { result = method.Invoke(null, new object[] { path, fileWrapper ? (object)roots : root, options }) as string; }
            finally { if (fileWrapper) foreach (GameObject child in roots) if (child) child.transform.SetParent(root.transform,true); }
            if (string.IsNullOrEmpty(result)) throw new IOException("FBX export failed. Check the Console.");
        }
    }
}
