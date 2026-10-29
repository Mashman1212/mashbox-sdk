using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        internal static bool IsHeadgearName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var tokens = name.Split(new[]{'_',' ',':','-','.'}, StringSplitOptions.RemoveEmptyEntries);
            return tokens.Any(t => new[]{"Hat","Hair","Eyes","Eye","Eyewear","Headgear"}.Any(k => string.Equals(t,k,StringComparison.OrdinalIgnoreCase)));
        }
        private int HeadgearAnchorIndex()
        {
            var matches = Enumerable.Range(0,bones.Length).Where(i => StudioSkinningDocument.BoneName(bones[i].name) == "Head").ToArray();
            if(matches.Length != 1) throw new InvalidOperationException("Headgear requires exactly one SDK Head bone.");
            return matches[0];
        }
        private Matrix4x4 HeadgearPreviewMatrix => bones[HeadgearAnchorIndex()].localToWorldMatrix * rest[HeadgearAnchorIndex()].inverse;
        private Vector3 HeadgearPivot => bones[HeadgearAnchorIndex()].position;
        private void AlignImportedHeadgear(StudioSkinningDocument.Part part, GameObject source)
        {
            // Equip slots zero their item root under Head. Rigid source files are
            // head-local; full-rig files identify their original head frame explicitly.
            var heads = source.GetComponentsInChildren<Transform>(true)
                .Where(t => StudioSkinningDocument.BoneName(t.name) == "Head").ToArray();
            if(heads.Length > 1) throw new InvalidOperationException("Headgear source contains multiple Head bones.");
            var sourceHead = heads.Length == 1 ? source.transform.worldToLocalMatrix * heads[0].localToWorldMatrix : Matrix4x4.identity;
            var placement = rest[HeadgearAnchorIndex()] * sourceHead.inverse;
            StudioSkinningDocument.TransformGeometry(part.mesh,placement);
            part.sourceToWorkspace = placement * part.sourceToWorkspace;
        }
        private void AttachHeadgearPreview(GameObject go, Mesh mesh)
        {
            int head = HeadgearAnchorIndex();
            StudioSkinningDocument.TransformGeometry(mesh,rest[head].inverse);
            go.transform.SetParent(bones[head],false);
        }
        private static void MakeHeadgearRigid(StudioSkinningDocument.Part part)
        {
            // SetBoneWeights rejects zero-influence vertices. An empty legacy stream
            // clears both fixed and variable bone-weight storage in Unity.
            part.mesh.boneWeights = Array.Empty<BoneWeight>();
            part.mesh.bindposes = Array.Empty<Matrix4x4>();
            part.bound = false;
            part.stitchedVertices = null; part.stitchedWeights = null; part.stitchedVertexCount = 0;
            part.preserveStitchedBindPose = false;
        }
        private void SaveHeadgearFbxAs()
        {
            if (!HasEditableMesh || !Part.mesh) return;
            if (!StudioFbxDependency.Require()) return;
            EndStroke();
            string path = StudioFileDialogs.SaveAssetInProject("Export rigid headgear",Part.name,"fbx","Save head-local headgear with its pivot locked to Head, without skin weights or a skeleton.");
            if (string.IsNullOrEmpty(path)) return;
            // Keep the existing source and its subasset references intact.
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            ExportHeadgearFbx(Part,path);
            EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path));
        }
        internal void ExportHeadgearFbx(StudioSkinningDocument.Part part, string path)
        {
            StudioFbxDependency.EnsureAvailable();
            var headFrame = rest[HeadgearAnchorIndex()];
            if (part.role != StudioSkinningDocument.MeshRole.Headgear) throw new InvalidOperationException("Select Headgear first.");
            if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path)) throw new InvalidOperationException("Choose a new FBX path.");
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(part.name); Mesh mesh = null;
            SceneManager.MoveGameObjectToScene(root,scene);
            try
            {
                root.hideFlags = HideFlags.HideAndDontSave;
                mesh = Object.Instantiate(part.mesh); mesh.name = part.name; mesh.hideFlags = HideFlags.None;
                mesh.boneWeights = Array.Empty<BoneWeight>();
                mesh.bindposes = Array.Empty<Matrix4x4>();
                // Item origin and axes are Head-local. Equip at local zero without
                // a compensating root transform or a duplicated skeleton.
                StudioSkinningDocument.TransformGeometry(mesh,headFrame.inverse);
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterials = part.materials;
                ExportClothingFbx(Path.GetFullPath(path),root);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!model || model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0 ||
                    model.GetComponentsInChildren<MeshFilter>(true).Length != 1)
                    throw new InvalidOperationException("Headgear FBX did not import as a rigid mesh.");
            }
            finally { Object.DestroyImmediate(root); if(mesh) Object.DestroyImmediate(mesh); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
