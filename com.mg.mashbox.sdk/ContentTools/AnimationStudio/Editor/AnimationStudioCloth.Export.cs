using System;
using System.IO;
using System.Linq;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private void ExportClothPrefab()
        {
            if (!ClothEditable) throw new InvalidOperationException("Select an editable garment to export.");
            EndClothStroke();
            var mesh = clothMeshRows[clothRendererIndex].sourceMesh;
            if (!clothProfile || !clothProfile.Matches(mesh))
                throw new InvalidOperationException("Create garment data for this mesh before exporting its cloth setup.");
            var source = clothMeshSources[clothRendererIndex];
            if (!source) throw new InvalidOperationException("The garment source is missing. Reload it before exporting.");
            string name = string.Concat(clothMeshRows[clothRendererIndex].name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = EditorUtility.SaveFilePanelInProject("Export cloth prefab", name + " Cloth", "prefab", "Save the garment and its runtime cloth setup.");
            if (string.IsNullOrEmpty(path)) return;
            // Keep existing prefabs intact; each export is a separate finished garment.
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            string profilePath = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(path, null) + " Data.asset");
            MashBoxClothProfile savedProfile = null;
            bool complete = false;
            bool skinned = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.sharedMesh == mesh);
            using (var export = new AuthoringRig(clothCharacter ? clothCharacter : ResolveSdkSkeleton(), null, skinned ? new[] { source } : Array.Empty<GameObject>()))
            try
            {
                Renderer garment = export.Root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => MashBoxClothSession.GetMesh(r) == mesh);
                if (!skinned)
                {
                    var filter = source.GetComponentsInChildren<MeshFilter>(true).First(f => f.sharedMesh == mesh);
                    var go = new GameObject(name); go.transform.SetParent(export.Root.transform, false);
                    go.transform.localPosition = source.transform.InverseTransformPoint(filter.transform.position);
                    go.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * filter.transform.rotation;
                    go.transform.localScale = (source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix).lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    garment = go.AddComponent<MeshRenderer>();
                }
                if (!garment) throw new InvalidOperationException("The selected garment could not be reconstructed for export.");
                foreach (var renderer in export.Root.GetComponentsInChildren<Renderer>(true))
                    if (renderer != garment) { var filter = renderer.GetComponent<MeshFilter>(); if(filter) DestroyImmediate(filter); DestroyImmediate(renderer); }
                garment.sharedMaterials = ClothAssignedMaterials(clothRendererIndex);
                garment.enabled = true; garment.forceRenderingOff = false; garment.gameObject.SetActive(true);
                export.Root.name = name;
                foreach (var transform in export.Root.GetComponentsInChildren<Transform>(true)) transform.gameObject.hideFlags = HideFlags.None;
                if (export.Animator) export.Animator.enabled = true;
                savedProfile = Instantiate(clothProfile); savedProfile.name = name + " Cloth Data"; savedProfile.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(savedProfile, profilePath);
                var clipping = ClothAssignedClipping(clothRendererIndex);
                if(clipping) garment.gameObject.AddComponent<MashBoxSDK.Culling.ClothingCullingSettings>().profile = clipping;
                var cloth = garment.gameObject.AddComponent<MashBoxCloth>();
                cloth.garment = garment; cloth.profile = savedProfile; cloth.collisionRoot = export.Root.transform;
                var prefab = PrefabUtility.SaveAsPrefabAsset(export.Root, path);
                if (!prefab) throw new InvalidOperationException("Unity could not save the cloth prefab.");
                AssetDatabase.SaveAssetIfDirty(savedProfile);
                complete = true; Selection.activeObject = prefab; EditorGUIUtility.PingObject(prefab);
                clothStatus = "Exported cloth prefab: " + path;
            }
            finally
            {
                if (!complete) { AssetDatabase.DeleteAsset(path); AssetDatabase.DeleteAsset(profilePath); }
                if (savedProfile && !EditorUtility.IsPersistent(savedProfile)) DestroyImmediate(savedProfile);
            }
        }
    }
}
