using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(OfflineMeshCombiner))]
public sealed class OfflineMeshCombinerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "bakedObject",
            "bakedMesh",
            "sourceRendererStates");
        serializedObject.ApplyModifiedProperties();

        OfflineMeshCombiner combiner = (OfflineMeshCombiner)target;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Bakes child MeshRenderers into a persistent mesh asset. The scene loads the already-combined renderer, so no mesh combining or model reimporting occurs at runtime.",
            MessageType.Info);

        if (combiner.HasBake)
        {
            string assetPath = AssetDatabase.GetAssetPath(combiner.BakedMesh);
            EditorGUILayout.LabelField("Baked Mesh", combiner.BakedMesh.name);
            EditorGUILayout.LabelField("Mesh Asset", string.IsNullOrEmpty(assetPath) ? "Missing" : assetPath);
        }

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button(combiner.HasBake ? "Rebake Offline Mesh" : "Bake Offline Mesh...", GUILayout.Height(28f)))
                Bake(combiner);

            if (combiner.HasBake)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select Baked Object"))
                    Selection.activeGameObject = combiner.BakedObject;

                if (GUILayout.Button("Remove Bake"))
                    RemoveBake(combiner);
                EditorGUILayout.EndHorizontal();
            }
        }
    }

    private static void Bake(OfflineMeshCombiner combiner)
    {
        string meshAssetPath = GetMeshAssetPath(combiner);
        if (string.IsNullOrEmpty(meshAssetPath))
            return;

        Undo.RecordObject(combiner, "Bake Offline Mesh");
        RestoreSourcesForRebake(combiner);

        List<MeshFilter> initialFilters = GetSourceMeshFilters(combiner);
        if (initialFilters.Count == 0)
        {
            Debug.LogWarning($"OfflineMeshCombiner on '{combiner.name}' found no eligible child MeshRenderers.", combiner);
            return;
        }

        Dictionary<string, bool> importerReadWriteStates = new Dictionary<string, bool>();

        try
        {
            importerReadWriteStates = GetReadWriteStates(initialFilters);
            SetReadWrite(importerReadWriteStates.Keys, true, "Preparing source meshes...");
            // Model imports can replace Mesh instances, so collect the hierarchy again afterwards.
            List<MeshFilter> meshFilters = GetSourceMeshFilters(combiner);
            BuildAndSave(combiner, meshFilters, meshAssetPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, combiner);
            EditorUtility.DisplayDialog(
                "Offline Mesh Bake Failed",
                "The mesh could not be baked. Source renderers were left enabled; see the Console for details.",
                "OK");
            combiner.RestoreRecordedSourceRenderers();
        }
        finally
        {
            if (combiner.RestoreSourceReadWriteAfterBake)
                RestoreReadWrite(importerReadWriteStates);

            EditorUtility.ClearProgressBar();
        }
    }

    private static void BuildAndSave(
        OfflineMeshCombiner combiner,
        List<MeshFilter> meshFilters,
        string meshAssetPath)
    {
        List<Material> orderedMaterials = new List<Material>();
        List<List<CombineInstance>> combinesByMaterial = new List<List<CombineInstance>>();
        List<MeshRenderer> sourceRenderers = new List<MeshRenderer>();
        List<bool> sourceEnabledStates = new List<bool>();
        MeshRenderer rendererTemplate = null;
        int totalVertexCount = 0;
        int sourceSubMeshCount = 0;
        int combinedLightmapIndex = int.MinValue;

        for (int filterIndex = 0; filterIndex < meshFilters.Count; filterIndex++)
        {
            MeshFilter meshFilter = meshFilters[filterIndex];
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            MeshRenderer renderer = meshFilter != null ? meshFilter.GetComponent<MeshRenderer>() : null;

            if (mesh == null || renderer == null)
                continue;

            if (!mesh.isReadable)
            {
                Debug.LogWarning($"Skipping unreadable mesh '{mesh.name}' on '{meshFilter.name}'.", meshFilter);
                continue;
            }

            Material[] materials = renderer.sharedMaterials;
            bool usedRenderer = false;

            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                Material material = subMeshIndex < materials.Length ? materials[subMeshIndex] : null;
                int materialIndex = GetOrAddMaterial(orderedMaterials, combinesByMaterial, material);
                combinesByMaterial[materialIndex].Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = subMeshIndex,
                    transform = combiner.transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix,
                    lightmapScaleOffset = renderer.lightmapScaleOffset
                });
                usedRenderer = true;
                sourceSubMeshCount++;
            }

            if (!usedRenderer)
                continue;

            sourceRenderers.Add(renderer);
            sourceEnabledStates.Add(renderer.enabled);
            if (rendererTemplate == null)
                rendererTemplate = renderer;

            if (combinedLightmapIndex == int.MinValue)
            {
                combinedLightmapIndex = renderer.lightmapIndex;
            }
            else if (combinedLightmapIndex != renderer.lightmapIndex)
            {
                throw new InvalidOperationException(
                    "Source renderers use different Lightmap IDs. A combined MeshRenderer can reference only one lightmap. " +
                    "Place each Lightmap ID under its own OfflineMeshCombiner to preserve baked lighting.");
            }

            totalVertexCount += mesh.vertexCount;
        }

        if (orderedMaterials.Count == 0)
            throw new InvalidOperationException("No readable source mesh submeshes were available to combine.");

        EditorUtility.DisplayProgressBar("Offline Mesh Combiner", "Combining meshes by material...", 0.55f);

        Mesh generatedMesh = new Mesh
        {
            name = $"{combiner.name} Offline Combined Mesh",
            indexFormat = totalVertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        bool hasLightmapData = combinedLightmapIndex >= 0;
        CombineInstance[] materialCombines = BuildMaterialCombines(
            combinesByMaterial,
            orderedMaterials,
            hasLightmapData);

        try
        {
            generatedMesh.CombineMeshes(materialCombines, false, true, false);
            generatedMesh.RecalculateBounds();
        }
        finally
        {
            for (int i = 0; i < materialCombines.Length; i++)
            {
                if (materialCombines[i].mesh != null)
                    UnityEngine.Object.DestroyImmediate(materialCombines[i].mesh);
            }
        }

        if (generatedMesh.vertexCount == 0 || GetIndexCount(generatedMesh) == 0)
        {
            UnityEngine.Object.DestroyImmediate(generatedMesh);
            throw new InvalidOperationException("The generated mesh was empty.");
        }

        Mesh savedMesh = SaveMeshAsset(generatedMesh, meshAssetPath, combiner.BakedMesh);
        GameObject bakedObject = CreateBakedObject(
            combiner,
            savedMesh,
            orderedMaterials,
            rendererTemplate,
            combinedLightmapIndex);

        UnityEngine.Object[] rendererObjects = sourceRenderers.Cast<UnityEngine.Object>().ToArray();
        Undo.RecordObjects(rendererObjects, "Disable Offline Mesh Sources");

        if (combiner.DisableSourceRenderers)
        {
            for (int i = 0; i < sourceRenderers.Count; i++)
                sourceRenderers[i].enabled = false;
        }

        combiner.RecordBake(bakedObject, savedMesh, sourceRenderers, sourceEnabledStates);
        EditorUtility.SetDirty(combiner);
        PrefabUtility.RecordPrefabInstancePropertyModifications(combiner);
        if (combiner.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(combiner.gameObject.scene);
        AssetDatabase.SaveAssets();

        int indexCount = GetIndexCount(savedMesh);
        Debug.Log(
            $"OfflineMeshCombiner baked '{savedMesh.name}' with {savedMesh.vertexCount:N0} vertices, {indexCount / 3:N0} triangles, " +
            $"{savedMesh.subMeshCount} material submeshes from {sourceRenderers.Count:N0} renderers / {sourceSubMeshCount:N0} source submeshes. " +
            "The scene will load this baked mesh without runtime combining.",
            combiner);
    }

    private static List<MeshFilter> GetSourceMeshFilters(OfflineMeshCombiner combiner)
    {
        MeshFilter[] filters = combiner.GetComponentsInChildren<MeshFilter>(combiner.IncludeInactiveChildren);
        List<MeshFilter> results = new List<MeshFilter>(filters.Length);

        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;
            if (!combiner.IncludeRootRenderer && filter.transform == combiner.transform)
                continue;
            if (combiner.BakedObject != null && filter.gameObject == combiner.BakedObject)
                continue;

            OfflineMeshCombiner offlineOwner = filter.GetComponentInParent<OfflineMeshCombiner>();
            if (offlineOwner != combiner)
                continue;
            if (filter.GetComponentInParent<RuntimeMeshCombiner>() != null)
                continue;

            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null)
                continue;
            if (!combiner.IncludeDisabledRenderers && !renderer.enabled)
                continue;

            results.Add(filter);
        }

        return results;
    }

    private static Dictionary<string, bool> GetReadWriteStates(List<MeshFilter> meshFilters)
    {
        Dictionary<string, bool> originalStates = new Dictionary<string, bool>();

        for (int i = 0; i < meshFilters.Count; i++)
        {
            Mesh mesh = meshFilters[i].sharedMesh;
            if (mesh == null || mesh.isReadable)
                continue;

            string path = AssetDatabase.GetAssetPath(mesh);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (string.IsNullOrEmpty(path) || importer == null || originalStates.ContainsKey(path))
                continue;

            originalStates.Add(path, importer.isReadable);
        }

        return originalStates;
    }

    private static void RestoreReadWrite(Dictionary<string, bool> originalStates)
    {
        List<string> pathsToDisable = originalStates
            .Where(pair => !pair.Value)
            .Select(pair => pair.Key)
            .ToList();
        SetReadWrite(pathsToDisable, false, "Restoring source mesh import settings...");
    }

    private static void SetReadWrite(IEnumerable<string> assetPaths, bool readable, string progressMessage)
    {
        List<string> paths = assetPaths.Distinct().ToList();
        if (paths.Count == 0)
            return;

        bool editing = false;
        try
        {
            AssetDatabase.StartAssetEditing();
            editing = true;

            for (int i = 0; i < paths.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Offline Mesh Combiner", progressMessage, (float)i / paths.Count);
                ModelImporter importer = AssetImporter.GetAtPath(paths[i]) as ModelImporter;
                if (importer == null || importer.isReadable == readable)
                    continue;

                importer.isReadable = readable;
                AssetDatabase.WriteImportSettingsIfDirty(paths[i]);
                AssetDatabase.ImportAsset(paths[i], ImportAssetOptions.ForceUpdate);
            }
        }
        finally
        {
            if (editing)
                AssetDatabase.StopAssetEditing();
        }
    }

    private static GameObject CreateBakedObject(
        OfflineMeshCombiner combiner,
        Mesh mesh,
        List<Material> materials,
        MeshRenderer rendererTemplate,
        int lightmapIndex)
    {
        GameObject bakedObject = new GameObject(combiner.CombinedObjectName);
        Undo.RegisterCreatedObjectUndo(bakedObject, "Create Offline Combined Mesh");
        bakedObject.transform.SetParent(combiner.transform, false);
        bakedObject.layer = rendererTemplate != null ? rendererTemplate.gameObject.layer : combiner.gameObject.layer;

        if (rendererTemplate != null)
            GameObjectUtility.SetStaticEditorFlags(bakedObject, GameObjectUtility.GetStaticEditorFlags(rendererTemplate.gameObject));

        MeshFilter filter = bakedObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = bakedObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials.ToArray();
        CopyRendererSettings(rendererTemplate, renderer);
        renderer.lightmapIndex = lightmapIndex;
        if (lightmapIndex >= 0)
            renderer.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);

        if (combiner.AddMeshCollider)
        {
            MeshCollider collider = bakedObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }

        return bakedObject;
    }

    private static void CopyRendererSettings(MeshRenderer source, MeshRenderer target)
    {
        if (source == null || target == null)
            return;

        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;
        target.motionVectorGenerationMode = source.motionVectorGenerationMode;
        target.renderingLayerMask = source.renderingLayerMask;
    }

    private static Mesh SaveMeshAsset(Mesh generatedMesh, string assetPath, Mesh previousMesh)
    {
        string previousPath = previousMesh != null ? AssetDatabase.GetAssetPath(previousMesh) : string.Empty;
        if (!string.IsNullOrEmpty(previousPath))
        {
            EditorUtility.CopySerialized(generatedMesh, previousMesh);
            previousMesh.name = generatedMesh.name;
            EditorUtility.SetDirty(previousMesh);
            UnityEngine.Object.DestroyImmediate(generatedMesh);
            return previousMesh;
        }

        string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
        AssetDatabase.CreateAsset(generatedMesh, uniquePath);
        return generatedMesh;
    }

    private static string GetMeshAssetPath(OfflineMeshCombiner combiner)
    {
        if (combiner.BakedMesh != null)
        {
            string existingPath = AssetDatabase.GetAssetPath(combiner.BakedMesh);
            if (!string.IsNullOrEmpty(existingPath))
                return existingPath;
        }

        string safeName = string.Concat(combiner.gameObject.name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return EditorUtility.SaveFilePanelInProject(
            "Save Offline Combined Mesh",
            $"{safeName}_CombinedMesh",
            "asset",
            "Choose where to save the persistent combined mesh asset.");
    }

    private static void RestoreSourcesForRebake(OfflineMeshCombiner combiner)
    {
        combiner.RestoreRecordedSourceRenderers();

        if (combiner.BakedObject != null)
            Undo.DestroyObjectImmediate(combiner.BakedObject);
    }

    private static void RemoveBake(OfflineMeshCombiner combiner)
    {
        string assetPath = combiner.BakedMesh != null ? AssetDatabase.GetAssetPath(combiner.BakedMesh) : string.Empty;
        if (!EditorUtility.DisplayDialog(
                "Remove Offline Mesh Bake?",
                "This restores the source renderers, removes the generated scene object, and deletes the generated mesh asset.",
                "Remove Bake",
                "Cancel"))
            return;

        Undo.RecordObject(combiner, "Remove Offline Mesh Bake");
        combiner.RestoreRecordedSourceRenderers();

        if (combiner.BakedObject != null)
            Undo.DestroyObjectImmediate(combiner.BakedObject);

        combiner.ClearBakeReferences();
        EditorUtility.SetDirty(combiner);
        if (combiner.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(combiner.gameObject.scene);

        if (!string.IsNullOrEmpty(assetPath))
            AssetDatabase.DeleteAsset(assetPath);
    }

    private static CombineInstance[] BuildMaterialCombines(
        List<List<CombineInstance>> combinesByMaterial,
        List<Material> orderedMaterials,
        bool hasLightmapData)
    {
        CombineInstance[] results = new CombineInstance[orderedMaterials.Count];

        for (int i = 0; i < orderedMaterials.Count; i++)
        {
            List<CombineInstance> sources = combinesByMaterial[i];
            int vertexCount = sources.Sum(source => source.mesh != null ? source.mesh.vertexCount : 0);
            Mesh materialMesh = new Mesh
            {
                name = orderedMaterials[i] != null ? $"{orderedMaterials[i].name} Combined" : "Null Material Combined",
                indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            materialMesh.CombineMeshes(sources.ToArray(), true, true, hasLightmapData);

            results[i] = new CombineInstance
            {
                mesh = materialMesh,
                subMeshIndex = 0,
                transform = Matrix4x4.identity
            };
        }

        return results;
    }

    private static int GetOrAddMaterial(
        List<Material> orderedMaterials,
        List<List<CombineInstance>> combinesByMaterial,
        Material material)
    {
        int index = orderedMaterials.IndexOf(material);
        if (index >= 0)
            return index;

        orderedMaterials.Add(material);
        combinesByMaterial.Add(new List<CombineInstance>());
        return orderedMaterials.Count - 1;
    }

    private static int GetIndexCount(Mesh mesh)
    {
        int count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            count += (int)mesh.GetIndexCount(i);
        return count;
    }
}
