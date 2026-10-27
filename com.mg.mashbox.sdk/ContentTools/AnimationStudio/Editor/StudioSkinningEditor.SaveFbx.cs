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
        internal void SaveSelectedFbx()
        {
            if(Part != null && Part.role == StudioSkinningDocument.MeshRole.Headgear) { SaveHeadgearFbxAs(); return; }
            if(HasTutorial){SaveSelectedFbxAs();return;}
            if (!StudioFbxDependency.Require()) return;
            EndStroke();
            if (IsReferencePart) throw new InvalidOperationException("The default body and bust are reference-only. Select clothing to save.");
            if (Part == null || !Part.bound) throw new InvalidOperationException("Select a skinned clothing mesh first.");
            string sourcePath = AssetDatabase.GetAssetPath(Part.sourceMesh);
            if (!Part.sourceMesh || !string.Equals(Path.GetExtension(sourcePath), ".fbx", StringComparison.OrdinalIgnoreCase))
            { SaveSelectedFbxAs(); return; }
            var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(Part.sourceMesh));
            if (!FindOriginalMeshRenderer(sourceModel, Part) || FindOriginalMeshRenderer(sourceModel, Part) is MeshRenderer)
            { SaveSelectedFbxAs(); return; }
            SavePartFbx(Part);
        }
#if MashBoxDev
        private void SaveReferenceFbx(StudioSkinningDocument.Part part, bool saveAs)
        {
            if (!Document.parts.Contains(part) || !part.referenceOnly || !part.bound)
                throw new InvalidOperationException("Select a skinned default part to export.");
            if (!StudioFbxDependency.Require()) return;
            EndStroke();
            if(saveAs) SavePartFbxAs(part);
            else SavePartFbx(part);
        }
        internal void SaveDefaultBustFbx()
        {
            var defaults = StudioDefaultReferences.LoadSkinning("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if (!defaults || !defaults.bust) throw new InvalidOperationException("Assign a default bust first.");
            var meshes = new HashSet<Mesh>(defaults.bust.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh));
            var parts = Document.parts.Where(p => p.referenceOnly && meshes.Contains(p.sourceMesh)).ToArray();
            if (parts.Length != 1) throw new InvalidOperationException("Use Save FBX on the individual bust row when the default bust has multiple meshes. Load the default bust first if none is present.");
            SaveDefaultBustFbx(parts[0]);
        }
        private void SaveDefaultBustFbx(StudioSkinningDocument.Part part)
        {
            if (!StudioFbxDependency.Require()) return;
            EndStroke();
            var defaults = StudioDefaultReferences.LoadSkinning("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if (!Document.parts.Contains(part) || !part.referenceOnly || !part.bound || !part.sourceMesh || !defaults || !defaults.bust ||
                !defaults.bust.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.sharedMesh == part.sourceMesh))
                throw new InvalidOperationException("Select the loaded default bust to save its FBX.");
            SavePartFbx(part);
        }
#endif
        internal void SaveSelectedFbxAs()
        {
            if(Part != null && Part.role == StudioSkinningDocument.MeshRole.Headgear) { SaveHeadgearFbxAs(); return; }
            if (!StudioFbxDependency.Require()) return;
            EndStroke();
            if (!CanUnskinSelected) throw new InvalidOperationException("Select a skinned editable mesh first.");
            SavePartFbxAs(Part);
        }
        private void SavePartFbxAs(StudioSkinningDocument.Part part)
        {
            string sourceAsset=AssetDatabase.GetAssetPath(part.sourceMesh);
            string suggested=AssetDatabase.GenerateUniqueAssetPath("Assets/"+Path.GetFileNameWithoutExtension(part.name)+".fbx");
            while (true)
            {
                string path=EditorUtility.SaveFilePanelInProject("Save FBX As",Path.GetFileNameWithoutExtension(suggested),"fbx","Save a new FBX, or choose the linked source to update it.");
                if (string.IsNullOrEmpty(path)) return;
                bool exists=File.Exists(Path.GetFullPath(path))||File.Exists(Path.GetFullPath(path)+".meta");
                if (!exists) { SavePartFbx(part,path); return; }
                bool sameSource=string.Equals(path.Replace('\\','/'),sourceAsset.Replace('\\','/'),StringComparison.OrdinalIgnoreCase);
                var sourceModel=sameSource?AssetDatabase.LoadAssetAtPath<GameObject>(sourceAsset):null;
                if(!HasTutorial&&sameSource&&FindOriginalMeshRenderer(sourceModel,part) is SkinnedMeshRenderer)
                {
                    // Use the source-update path so its backup and reference checks remain intact.
                    SavePartFbx(part);return;
                }
                string copy=AssetDatabase.GenerateUniqueAssetPath(path);
                int choice=EditorUtility.DisplayDialogComplex("FBX filename already exists",
                    "This filename is already in use. Save your edited mesh as "+Path.GetFileName(copy)+" instead? The existing file will stay unchanged.",
                    "Save New Copy","Cancel","Choose Another Name");
                if(choice==1)return;
                if(choice==0){SavePartFbx(part,copy);return;}
                suggested=copy;
            }
        }
        private void SavePartFbx(StudioSkinningDocument.Part part, string saveAsPath = null)
        {
            if(HasTutorial && string.IsNullOrEmpty(saveAsPath))
                throw new InvalidOperationException("Practice keeps source assets unchanged. Select the editable garment and use Save FBX As.");
            StudioFbxDependency.EnsureAvailable();
            string assetPath = AssetDatabase.GetAssetPath(part.sourceMesh);
            bool saveAs = !string.IsNullOrEmpty(saveAsPath);
            if (!saveAs && !string.Equals(Path.GetExtension(assetPath), ".fbx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected mesh did not come from an FBX. Save FBX needs the original clothing model.");
            string sourcePath = saveAs ? null : PhysicalFbxPath(assetPath);
            string destination = saveAs ? Path.GetFullPath(saveAsPath) : sourcePath;
            string importPath = saveAs ? saveAsPath : assetPath;
            if (!saveAs && AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().Any(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)))
                throw new InvalidOperationException("This FBX contains animation clips. Saving clothing over an animated FBX is not supported; no file was changed.");
            var source = saveAs ? null : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            var sourceIds = (saveAs ? Array.Empty<Object>() : AssetDatabase.LoadAllAssetsAtPath(assetPath)).Where(a => a && !a.name.StartsWith("__preview__", StringComparison.Ordinal)).Select(AssetIdentity).ToArray();
            var sourceRenderer = saveAs ? null : FindOriginalMeshRenderer(source, part);
            if (!saveAs && !sourceRenderer)
                throw new InvalidOperationException("The original clothing renderer changed. No file was changed; import its current version before saving.");
            string exportMeshName = part.role.ToString();
            bool renameMesh = !saveAs && (sourceRenderer.name != exportMeshName || part.sourceMesh.name != exportMeshName);
            var legacyRoot = source ? LegacyExportContentRoot(source.transform) : null;
            bool repairLegacy = source && legacyRoot != source.transform;
            // Removing an ancestor intentionally changes hierarchy-node IDs. Continue
            // protecting all mesh/material subasset references during this migration;
            // ordinary overwrite saves retain the stricter whole-asset identity check.
            if (repairLegacy || renameMesh)
                sourceIds = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                    .Where(a => a && !(a is GameObject) && !(a is Component) && !a.name.StartsWith("__preview__", StringComparison.Ordinal))
                    .Select(AssetIdentity).ToArray();
            string rendererPath = saveAs ? exportMeshName : AnimationUtility.CalculateTransformPath(sourceRenderer.transform, source.transform);
            if (sourceRenderer is MeshRenderer && !saveAs)
                throw new InvalidOperationException("Use Save FBX As to save this newly skinned mesh as a new model.");
            string originalName = saveAs || renameMesh ? exportMeshName : part.sourceMesh.name;
            string originalGuid = null; long originalId = 0;
            if (!saveAs) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(part.sourceMesh, out originalGuid, out originalId);
            if (renameMesh)
            {
                string oldIdentity = AssetIdentity(part.sourceMesh);
                string newIdentity = typeof(Mesh).FullName + "|" + exportMeshName + "|" + originalGuid + "|" + originalId;
                sourceIds = sourceIds.Select(identity => identity == oldIdentity ? newIdentity : identity).ToArray();
            }
            string work = Path.GetFullPath("Library/MashBox/SkinningFbx/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string staged = Path.Combine(work, Path.GetFileName(destination));
            string backup = Path.Combine(work, "Original.fbx");
            string metaBackup = Path.Combine(work, "Original.fbx.meta");
            string swap = destination + ".studio-" + Guid.NewGuid().ToString("N") + ".tmp";
            var exportPose = CapturePartExportPose(part);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null; Mesh edited = null;
            var readableCopies = new List<Mesh>();
            bool replaced = false;
            try
            {
                // A new file is built entirely from the edited workspace and SDK rig.
                // Source lookup is only needed when preserving an existing FBX in place.
                if (saveAs)
                {
                    root = new GameObject(Path.GetFileNameWithoutExtension(saveAsPath));
                    var clothing = new GameObject(rendererPath);
                    clothing.transform.SetParent(root.transform, false);
                    var renderer = clothing.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMaterials = part.materials;
                    renderer.bones = Array.Empty<Transform>();
                    renderer.rootBone = root.transform;
                }
                else { root = Object.Instantiate(source); root.name = source.name; }
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
                PoseExportSkeleton(root,exportPose);
                var targetTransform = rendererPath.Length == 0 ? root.transform : root.transform.Find(rendererPath);
                var target = targetTransform.GetComponent<SkinnedMeshRenderer>();
                if (!target)
                {
                    // Convert only the isolated export clone, never the imported asset.
                    var staticRenderer = targetTransform.GetComponent<MeshRenderer>();
                    var materials = staticRenderer.sharedMaterials;
                    Object.DestroyImmediate(staticRenderer);
                    var filter = targetTransform.GetComponent<MeshFilter>();
                    if (filter) Object.DestroyImmediate(filter);
                    target = targetTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                    target.sharedMaterials = materials;
                    target.bones = Array.Empty<Transform>();
                }
                // Other meshes in the source file also need consistent bind matrices
                // when their shared skeleton changes pose.
                foreach(var other in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(other==target || !other.sharedMesh) continue;
                    var otherWeights = new StudioSkinWeights(other.sharedMesh);
                    otherWeights.LimitToFour(); otherWeights.Apply(other.sharedMesh);
                    var oldBind=other.sharedMesh.bindposes;
                    if(oldBind.Length!=other.bones.Length) throw new InvalidOperationException("A source mesh has an invalid bone/bind-pose mapping.");
                    var matrices=other.bones.Select((b,i)=>other.transform.worldToLocalMatrix*b.localToWorldMatrix*oldBind[i]).ToArray();
                    BakeExportGeometry(other.sharedMesh,matrices);
                    other.sharedMesh.bindposes=other.bones.Select(b=>b.worldToLocalMatrix*other.transform.localToWorldMatrix).ToArray();
                }
                edited = Object.Instantiate(part.mesh); edited.name = originalName; edited.hideFlags = HideFlags.None;
                BakePartForTPose(edited,exportPose);
                var exportMeshToWorkspace = root.transform.worldToLocalMatrix * target.transform.localToWorldMatrix;
                StudioSkinningDocument.TransformGeometry(edited, exportMeshToWorkspace.inverse);
                var skin = new StudioSkinWeights(edited);
                var exportBones = ResolveExportBones(root,target,skin,exportPose);
                var targetBones = new List<Transform>(target.bones);
                var bindposes = new List<Matrix4x4>(saveAs ? Array.Empty<Matrix4x4>() : part.sourceMesh.bindposes);
                var mapping = Enumerable.Repeat(-1, bones.Length).ToArray();
                foreach (int b in skin.Values.SelectMany(v => v).Select(w => w.boneIndex).Distinct())
                {
                    var exportBone = exportBones[b];
                    int index = targetBones.IndexOf(exportBone);
                    if (index < 0) { index = targetBones.Count; targetBones.Add(exportBone); bindposes.Add(Matrix4x4.identity); }
                    while (bindposes.Count <= index) bindposes.Add(Matrix4x4.identity);
                    bindposes[index] = exportBone.worldToLocalMatrix * target.transform.localToWorldMatrix;
                    mapping[b] = index;
                }
                for(int i=0;i<targetBones.Count;i++)
                    bindposes[i]=targetBones[i].worldToLocalMatrix*target.transform.localToWorldMatrix;
                skin.Remap(mapping); skin.Apply(edited); edited.bindposes = bindposes.ToArray();
                target.sharedMesh = edited; target.bones = targetBones.ToArray();
                if (!root.GetComponent<Renderer>())
                {
                    // Do not let skin dependencies pull the temporary file wrapper
                    // into the FBX when exporting its children as model roots.
                    int hipsIndex = Array.FindIndex(bones, b => StudioSkinningDocument.BoneName(b.name) == "Hips");
                    target.rootBone = hipsIndex >= 0 ? exportBones[hipsIndex] : targetBones.FirstOrDefault();
                }
                var exportRoot = repairLegacy ? LegacyExportContentRoot(root.transform).gameObject : root;
                if (repairLegacy || renameMesh)
                {
                    target.name = exportMeshName;
                    rendererPath = AnimationUtility.CalculateTransformPath(target.transform, exportRoot.transform);
                    // The importer maps the new role name to the existing mesh ID.
                }
                ExportClothingFbx(staged, exportRoot, !exportRoot.GetComponent<Renderer>());
                if (!File.Exists(staged) || new FileInfo(staged).Length == 0) throw new IOException("FBX export produced no data.");
                if (saveAs)
                {
                    File.Copy(staged,destination,false); replaced=true;

                }
                else
                {
                    File.Copy(destination, backup);
                    if (File.Exists(destination + ".meta")) File.Copy(destination + ".meta", metaBackup);
                    File.Copy(staged, swap);
                    File.Replace(swap, destination, null); replaced = true;
                }
                if (renameMesh) PreserveRenamedMeshId(importPath, exportMeshName, originalId);
                AssetDatabase.ImportAsset(importPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (saveAs && AssetImporter.GetAtPath(importPath) is ModelImporter newImporter)
                {
                    // Keep the exported root on future imports/exports so paths stay stable.
                    newImporter.preserveHierarchy = true;
                    newImporter.SaveAndReimport();
                }
                PreserveExportedMeshData(importPath,part.preserveStitchedBindPose);
                var savedModel = AssetDatabase.LoadAssetAtPath<GameObject>(importPath);
                if (savedModel && savedModel.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.probeAnchor != savedModel.transform))
                    throw new InvalidOperationException("The exported FBX did not retain its root light-probe anchors.");
                var savedIds = new HashSet<string>(AssetDatabase.LoadAllAssetsAtPath(importPath).Where(a => a).Select(AssetIdentity));
                var missingIds = sourceIds.Where(identity => !savedIds.Contains(identity)).ToArray();
                if (!saveAs && missingIds.Length > 0)
                    throw new InvalidOperationException("The FBX round trip would change existing model/subasset references: " + string.Join("; ",missingIds.Take(8)));
                var savedRenderer = FindExportedRenderer(savedModel,rendererPath,target.name,edited.name);
                if (!savedRenderer || !savedRenderer.sharedMesh) throw new InvalidOperationException("The exported FBX has no matching skinned clothing renderer. Imported renderers: " +
                    (savedModel ? string.Join(", ",savedModel.GetComponentsInChildren<Renderer>(true).Select(r=>AnimationUtility.CalculateTransformPath(r.transform,savedModel.transform)+" ("+r.GetType().Name+")")) : "model import failed"));
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(savedRenderer.sharedMesh, out string guid, out long id);
                if (!saveAs && (guid != originalGuid || id != originalId)) throw new InvalidOperationException("The FBX round trip would change the clothing mesh reference.");
                // Inspect a detached readable copy; keep the source import settings intact.
                var check = StudioSkinningDocument.CreateEditableCopy(savedRenderer.sharedMesh);
                try
                {
                    if (check.subMeshCount != edited.subMeshCount || check.blendShapeCount != edited.blendShapeCount)
                        throw new InvalidOperationException($"The FBX round trip changed submeshes ({edited.subMeshCount} to {check.subMeshCount}) or blend shapes ({edited.blendShapeCount} to {check.blendShapeCount}).");
                    if (check.bounds.size != edited.bounds.size && (check.bounds.size-edited.bounds.size).magnitude > .001f)
                        throw new InvalidOperationException("The FBX round trip changed the clothing scale.");
                    if (!new StudioSkinWeights(check).HasWeights) throw new InvalidOperationException("The FBX round trip lost skin weights.");
                    if(part.preserveStitchedBindPose) ValidateStitchedBindMatrices(edited,target.bones,check,savedRenderer.bones);
                    else ValidateExportRestPose(check,savedRenderer);
                    try { ValidateExportTriangles(edited,target.bones,check,savedRenderer.bones); }
                    catch
                    {
                        if(File.Exists(destination+".meta")) File.Copy(destination+".meta",Path.Combine(work,"Imported.fbx.meta"),true);
                        WriteExportDiagnostics(Path.Combine(work,"expected.txt"),edited,target.bones);
                        WriteExportDiagnostics(Path.Combine(work,"imported.txt"),check,savedRenderer.bones);
                        throw;
                    }
                }
                finally { Object.DestroyImmediate(check); }
                part.sourceMesh = savedRenderer.sharedMesh;
                part.sourceRendererPath = AnimationUtility.CalculateTransformPath(savedRenderer.transform,savedModel.transform);
                part.sourceToWorkspace = savedModel.transform.worldToLocalMatrix * savedRenderer.transform.localToWorldMatrix;
                EditorUtility.SetDirty(Document);
                EditorGUIUtility.PingObject(savedModel);
                lastView?.ShowNotification(new GUIContent("Saved " + Path.GetFileName(destination)));
                Debug.Log(saveAs ? "Saved clothing copy to " + importPath + ". Future Save FBX uses this copy; original unchanged." : "Saved selected clothing to " + assetPath + ". Original FBX backup: " + backup);
            }
            catch (Exception e)
            {
                if (replaced)
                {
                    if(saveAs)
                    {
                        AssetDatabase.DeleteAsset(importPath);
                        if(File.Exists(destination)) File.Delete(destination);
                        if(File.Exists(destination+".meta")) File.Delete(destination+".meta");
                        throw new InvalidOperationException(e.GetBaseException().Message+" The unsuccessful copy was removed; the original and workspace edits are unchanged.",e);
                    }
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
        private static void WriteExportDiagnostics(string path,Mesh mesh,Transform[] rig)
        {
            var culture=System.Globalization.CultureInfo.InvariantCulture;
            var skin=new StudioSkinWeights(mesh); var positions=mesh.vertices;
            using(var writer=new StreamWriter(path))
                for(int i=0;i<positions.Length;i++)
                {
                    var p=positions[i];
                    writer.Write(p.x.ToString("R",culture)+","+p.y.ToString("R",culture)+","+p.z.ToString("R",culture));
                    foreach(var w in skin.Values[i]) writer.Write("|"+ExportBoneSignature(rig[w.boneIndex])+"="+w.weight.ToString("R",culture));
                    writer.WriteLine();
                }
        }
        private static void ValidateStitchedBindMatrices(Mesh expected,Transform[] expectedBones,Mesh actual,Transform[] actualBones)
        {
            var binds=expected.bindposes;var imported=actual.bindposes;
            for(int i=0;i<actualBones.Length;i++)
            {
                int index=Array.FindIndex(expectedBones,b=>b && b.name==actualBones[i].name);
                if(index<0 || i>=imported.Length || index>=binds.Length)
                    throw new InvalidOperationException("The FBX changed a stitched skin bone.");
                for(int k=0;k<16;k++)if(Mathf.Abs(imported[i][k]-binds[index][k])>.0001f)
                    throw new InvalidOperationException("The FBX changed the stitched body's bind matrices.");
            }
        }
        private static void PreserveExportedMeshData(string path,bool stitched=false)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer) throw new InvalidOperationException("The saved FBX has no model importer.");
            GameObject displayBody=null;
            if(stitched)
            {
                var defaults=StudioDefaultReferences.LoadSkinning("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
                displayBody=defaults?defaults.body:null;
                if(!displayBody)throw new InvalidOperationException("The default stitch body is unavailable. Restore it before export.");
            }
            bool poseChanged=StudioStitchedEditorPose.Configure(importer,displayBody);
            bool probeAnchorsChanged=StudioSkinningProbeAnchors.Configure(importer);
            const string skeletonPath = "Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Skeleton.fbx";
            var avatar = AssetDatabase.LoadAllAssetsAtPath(skeletonPath).OfType<Avatar>()
                .FirstOrDefault(a => a.name == "SkeletonAvatar");
            if (!avatar || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException("The SDK SkeletonAvatar is unavailable or invalid. Reimport the SDK Skeleton before saving clothing.");
            // These are authored data now. Recalculating normals or limiting the
            // influences would silently discard sculpt/stitch/weight edits.
            bool changed = poseChanged || probeAnchorsChanged || importer.importAnimation || importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar != avatar ||
                importer.importNormals != ModelImporterNormals.Import ||
                importer.importBlendShapeNormals != ModelImporterNormals.Import ||
                importer.skinWeights != ModelImporterSkinWeights.Standard ||
                importer.maxBonesPerVertex != 4 || importer.minBoneWeight != .001f ||
                importer.meshCompression != ModelImporterMeshCompression.Off;
            if (!changed) return;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapeNormals = ModelImporterNormals.Import;
            importer.skinWeights = ModelImporterSkinWeights.Standard;
            importer.maxBonesPerVertex = 4;
            // Use the importer-supported minimum; the FBX itself retains all weights.
            // Validation accounts for pruning and renormalization at this cutoff.
            importer.minBoneWeight = .001f;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
        private static string[] TriangleSignatures(Mesh mesh, Transform[] rig, string channel = null)
        {
            var points = mesh.vertices; var uv = mesh.uv; var skin = new StudioSkinWeights(mesh);
            var normals = mesh.normals;
            var signatures = new string[points.Length];
            for (int v = 0; v < points.Length; v++)
            {
                var p = points[v]; var t = uv.Length == points.Length ? uv[v] : Vector2.zero;
                string influences = string.Join(",", skin.Values[v].Where(w => w.weight > .0005f).Select(w =>
                    (w.boneIndex >= 0 && w.boneIndex < rig.Length && rig[w.boneIndex] ? ExportBoneSignature(rig[w.boneIndex]) : "missing") + ":" + Mathf.RoundToInt(w.weight * 1000)).OrderBy(s => s, StringComparer.Ordinal));
                signatures[v] = channel == null || channel == "geometry" ? QuantizedVector(p,10000) : "";
                if(channel == null || channel == "UVs") signatures[v] += "|UV:"+Mathf.RoundToInt(t.x*10000)+","+Mathf.RoundToInt(t.y*10000);
                if(channel == null || channel == "skin influences") signatures[v] += "|W:"+influences;
                if((channel == null || channel == "normals") && normals.Length == points.Length) signatures[v] += "|N:"+QuantizedVector(normals[v],1000);
            }
            // Blend shape deltas must follow the same corners after reindexing.
            for(int shape=0;(channel == null || channel == "blend shapes") && shape<mesh.blendShapeCount;shape++)
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
        private static Renderer FindOriginalMeshRenderer(GameObject root, StudioSkinningDocument.Part part)
        {
            if (!root || !part.sourceMesh) return null;
            Mesh MeshOf(Renderer renderer) => renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (part.sourceRendererPath != null)
            {
                var node = part.sourceRendererPath.Length == 0 ? root.transform : root.transform.Find(part.sourceRendererPath);
                var exact = node ? node.GetComponent<Renderer>() : null;
                if (exact && MeshOf(exact) == part.sourceMesh) return exact;
            }
            // Older workspaces did not store paths for static imports. Recover only
            // an unambiguous match to the original mesh, never by vertex count/name.
            var matches = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && MeshOf(r) == part.sourceMesh).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        private static SkinnedMeshRenderer FindSourceRenderer(GameObject root, string path)
        {
            if (!root || path == null) return null;
            var transform = path.Length == 0 ? root.transform : root.transform.Find(path);
            return transform ? transform.GetComponent<SkinnedMeshRenderer>() : null;
        }
        private static string ExportBoneSignature(Transform bone)
        {
            // FBX filename wrappers are not part of the skeleton's identity.
            var anchor=bone;
            while(anchor && StudioSkinningDocument.BoneName(anchor.name)!="Hips") anchor=anchor.parent;
            return anchor ? anchor.name+"/"+AnimationUtility.CalculateTransformPath(bone,anchor) : bone.name;
        }
        private static SkinnedMeshRenderer FindExportedRenderer(GameObject root,string oldPath,string rendererName,string meshName)
        {
            if(!root) return null;
            var exact=FindSourceRenderer(root,oldPath);
            if(exact && exact.sharedMesh) return exact;
            var candidates=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.sharedMesh).ToArray();
            var named=candidates.Where(r=>r.name==rendererName && r.sharedMesh.name==meshName).ToArray();
            if(named.Length==1) return named[0];
            named=candidates.Where(r=>r.name==rendererName).ToArray();
            if(named.Length==1) return named[0];
            named=candidates.Where(r=>r.sharedMesh.name==meshName).ToArray();
            if(named.Length==1) return named[0];
            // Geometry/UV/weight and bind checks below still verify this candidate.
            return candidates.Length==1 ? candidates[0] : null;
        }
        private static void PreserveRenamedMeshId(string path, string meshName, long meshId)
        {
            var importer = AssetImporter.GetAtPath(path);
            var serialized = new SerializedObject(importer);
            var table = serialized.FindProperty("m_InternalIDToNameTable");
            if (table == null) throw new InvalidOperationException("Unity cannot preserve this mesh reference while renaming. The original FBX will be restored.");
            SerializedProperty entry = null;
            for (int i = 0; i < table.arraySize; i++)
            {
                var candidate = table.GetArrayElementAtIndex(i);
                var key = candidate.FindPropertyRelative("first");
                if (key.FindPropertyRelative("first").intValue == 43 && key.FindPropertyRelative("second").longValue == meshId)
                { entry = candidate; break; }
            }
            if (entry == null) { int index = table.arraySize; table.InsertArrayElementAtIndex(index); entry = table.GetArrayElementAtIndex(index); }
            var pair = entry.FindPropertyRelative("first");
            pair.FindPropertyRelative("first").intValue = 43; // Unity Mesh class ID.
            pair.FindPropertyRelative("second").longValue = meshId;
            entry.FindPropertyRelative("second").stringValue = meshName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }
        private static Transform LegacyExportContentRoot(Transform root)
        {
            var current = root;
            while (!current.GetComponent<Renderer>() && current.childCount == 1)
            {
                var child = current.GetChild(0);
                if (child.name != root.name || child.GetComponent<Renderer>()) break;
                current = child;
            }
            return current;
        }
        private static void ExportClothingFbx(string path, GameObject root, bool exportChildren = false)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter")).FirstOrDefault(t => t != null);
            var optionsType = type?.Assembly.GetType("UnityEditor.Formats.Fbx.Exporter.ExportModelOptions");
            // Unity adds a file-named wrapper when importing multiple FBX roots.
            // Exporting that wrapper duplicates the mesh name and causes the
            // exporter to suffix the actual node (_1), breaking its local ID.
            bool fileWrapper = exportChildren || !root.GetComponent<Renderer>() && (
                (root.transform.childCount > 1 && root.transform.Cast<Transform>().Any(t => t.name == root.name && t.GetComponent<Renderer>())) ||
                (root.transform.childCount == 1 && root.transform.GetChild(0).name == root.name));
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
