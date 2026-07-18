using System.Collections.Generic;
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class RuntimeMeshCombiner : MonoBehaviour
{
    [SerializeField] private bool combineOnAwake = true;
    [SerializeField] private bool includeInactiveChildren;
    [SerializeField] private bool includeDisabledRenderers;
    [SerializeField] private bool includeRootRenderer;
    [SerializeField] private bool disableSourceRenderers = true;
    [SerializeField] private bool addMeshCollider;
#if UNITY_EDITOR
    [SerializeField] [FormerlySerializedAs("enableReadWriteInEditorBeforeCombine")] private bool enableReadWriteOnValidate = true;
    private static readonly HashSet<RuntimeMeshCombiner> PendingReadWriteCombiners = new HashSet<RuntimeMeshCombiner>();
    private static readonly HashSet<RuntimeMeshCombiner> ForcedReadWriteCombiners = new HashSet<RuntimeMeshCombiner>();
    private static bool readWriteBatchScheduled;
    private static bool processingReadWriteBatch;
#endif
    [SerializeField] private string combinedObjectName = "Combined Mesh";

    private readonly List<MeshRenderer> sourceRenderers = new List<MeshRenderer>();
    private readonly List<bool> sourceRendererEnabledStates = new List<bool>();
    private GameObject combinedObject;
    private Mesh combinedMesh;
    private MeshRenderer sourceRendererTemplate;

    private void Awake()
    {
        if (combineOnAwake)
            Combine();
    }

    private void OnDestroy()
    {
        if (combinedMesh != null)
            DestroyObject(combinedMesh);
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && enableReadWriteOnValidate && !processingReadWriteBatch)
            QueueEnableReadWriteOnChildMeshAssets(false);
#endif
    }

    [ContextMenu("Combine Now")]
    public void Combine()
    {
        RestoreSourceRenderers();
        ClearCombinedObject();
        sourceRendererTemplate = null;

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(includeInactiveChildren);

        List<Material> orderedMaterials = new List<Material>();
        List<List<CombineInstance>> combineInstancesByMaterial = new List<List<CombineInstance>>();
        int vertexCount = 0;
        int sourceMeshCount = 0;
        int sourceSubMeshCount = 0;

        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];

            if (!ShouldUseMeshFilter(meshFilter))
                continue;

            Mesh mesh = meshFilter.sharedMesh;
            MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();

#if UNITY_EDITOR
            if (!mesh.isReadable && !Application.isPlaying)
                mesh = ReloadMeshFromAssetIfReadable(mesh, meshFilter);
#endif

            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"Skipping '{mesh.name}' on '{meshFilter.name}' because the mesh is not readable. Enable Read/Write on the mesh import settings to combine it.",
                    meshFilter);
                continue;
            }

            Material[] materials = meshRenderer.sharedMaterials;
            int subMeshCount = mesh.subMeshCount;
            bool usedRenderer = false;

            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                Material material = subMeshIndex < materials.Length ? materials[subMeshIndex] : null;
                int materialIndex = GetOrAddMaterialIndex(orderedMaterials, combineInstancesByMaterial, material);
                List<CombineInstance> materialCombines = combineInstancesByMaterial[materialIndex];

                materialCombines.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = subMeshIndex,
                    transform = transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix
                });
                usedRenderer = true;
                sourceSubMeshCount++;
            }

            if (!usedRenderer)
                continue;

            vertexCount += mesh.vertexCount;
            sourceMeshCount++;
            sourceRenderers.Add(meshRenderer);
            sourceRendererEnabledStates.Add(meshRenderer.enabled);

            if (sourceRendererTemplate == null)
                sourceRendererTemplate = meshRenderer;
        }

        if (orderedMaterials.Count == 0)
        {
            Debug.LogWarning($"RuntimeMeshCombiner on '{name}' found no valid readable child meshes to combine.", this);
            return;
        }

        combinedMesh = new Mesh
        {
            name = $"{name} Combined Mesh",
            indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        CombineInstance[] materialCombineInstances = BuildMaterialCombineInstances(combineInstancesByMaterial, orderedMaterials);
        combinedMesh.CombineMeshes(materialCombineInstances, false, true, false);
        combinedMesh.RecalculateBounds();

        for (int i = 0; i < materialCombineInstances.Length; i++)
            DestroyObject(materialCombineInstances[i].mesh);

        int combinedIndexCount = GetCombinedIndexCount(combinedMesh);

        if (combinedMesh.vertexCount == 0 || combinedIndexCount == 0)
        {
            Debug.LogWarning(
                $"RuntimeMeshCombiner on '{name}' built an empty mesh from {sourceMeshCount} source meshes / {sourceSubMeshCount} submeshes. Source renderers were left enabled.",
                this);
            DestroyObject(combinedMesh);
            combinedMesh = null;
            RestoreSourceRenderers();
            return;
        }

        CreateCombinedObject(orderedMaterials);

        Debug.Log(
            $"RuntimeMeshCombiner on '{name}' built '{combinedMesh.name}' with {combinedMesh.vertexCount} vertices, {combinedIndexCount / 3} triangles, {combinedMesh.subMeshCount} material submeshes from {sourceMeshCount} source meshes / {sourceSubMeshCount} source submeshes.",
            this);

        if (disableSourceRenderers && combinedObject != null && combinedMesh != null && combinedMesh.vertexCount > 0)
        {
            for (int i = 0; i < sourceRenderers.Count; i++)
            {
                if (sourceRenderers[i] != null)
                    sourceRenderers[i].enabled = false;
            }
        }
    }

    [ContextMenu("Restore Source Renderers")]
    public void RestoreSourceRenderers()
    {
        for (int i = 0; i < sourceRenderers.Count; i++)
        {
            if (sourceRenderers[i] != null)
                sourceRenderers[i].enabled = i < sourceRendererEnabledStates.Count
                    ? sourceRendererEnabledStates[i]
                    : true;
        }

        sourceRenderers.Clear();
        sourceRendererEnabledStates.Clear();
    }

#if UNITY_EDITOR
    [ContextMenu("Enable Read/Write On Child Mesh Assets")]
    public void EnableReadWriteOnChildMeshAssets()
    {
        QueueEnableReadWriteOnChildMeshAssets(true);
    }

    private void QueueEnableReadWriteOnChildMeshAssets(bool force)
    {
        if (this == null)
            return;

        PendingReadWriteCombiners.Add(this);
        if (force)
            ForcedReadWriteCombiners.Add(this);
        if (readWriteBatchScheduled)
            return;

        readWriteBatchScheduled = true;
        EditorApplication.delayCall += ProcessPendingReadWriteBatch;
    }

    private static void ProcessPendingReadWriteBatch()
    {
        readWriteBatchScheduled = false;
        if (processingReadWriteBatch)
            return;

        RuntimeMeshCombiner[] combiners = PendingReadWriteCombiners
            .Where(combiner => combiner != null &&
                               (combiner.enableReadWriteOnValidate || ForcedReadWriteCombiners.Contains(combiner)))
            .ToArray();
        PendingReadWriteCombiners.Clear();
        ForcedReadWriteCombiners.Clear();

        if (combiners.Length == 0)
            return;

        HashSet<string> modelAssetPaths = new HashSet<string>();
        int unreadableSceneMeshCount = 0;

        for (int combinerIndex = 0; combinerIndex < combiners.Length; combinerIndex++)
        {
            RuntimeMeshCombiner combiner = combiners[combinerIndex];
            MeshFilter[] meshFilters = combiner.GetComponentsInChildren<MeshFilter>(combiner.includeInactiveChildren);

            for (int meshIndex = 0; meshIndex < meshFilters.Length; meshIndex++)
            {
                MeshFilter meshFilter = meshFilters[meshIndex];

                if (!combiner.ShouldUseMeshFilter(meshFilter))
                    continue;

                Mesh mesh = meshFilter.sharedMesh;

                if (mesh == null || mesh.isReadable)
                    continue;

                string assetPath = AssetDatabase.GetAssetPath(mesh);

                if (string.IsNullOrEmpty(assetPath))
                {
                    unreadableSceneMeshCount++;
                    continue;
                }

                modelAssetPaths.Add(assetPath);
            }
        }

        Dictionary<string, ModelImporter> importersToUpdate = new Dictionary<string, ModelImporter>();
        foreach (string assetPath in modelAssetPaths)
        {
            ModelImporter modelImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;

            if (modelImporter == null)
            {
                Debug.LogWarning(
                    $"RuntimeMeshCombiner could not enable Read/Write because '{assetPath}' is not imported by a ModelImporter.");
                continue;
            }

            if (modelImporter.isReadable)
                continue;

            importersToUpdate.Add(assetPath, modelImporter);
        }

        if (importersToUpdate.Count > 0)
        {
            processingReadWriteBatch = true;
            bool assetEditingStarted = false;
            try
            {
                AssetDatabase.StartAssetEditing();
                assetEditingStarted = true;

                foreach (KeyValuePair<string, ModelImporter> entry in importersToUpdate)
                {
                    entry.Value.isReadable = true;
                    AssetDatabase.WriteImportSettingsIfDirty(entry.Key);
                    AssetDatabase.ImportAsset(entry.Key, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                if (assetEditingStarted)
                    AssetDatabase.StopAssetEditing();

                processingReadWriteBatch = false;
            }

            Debug.Log(
                $"RuntimeMeshCombiner enabled Read/Write on {importersToUpdate.Count} unique model asset(s) in one batched import for {combiners.Length} combiner(s).");
        }

        if (unreadableSceneMeshCount > 0)
        {
            Debug.LogWarning(
                $"RuntimeMeshCombiner found {unreadableSceneMeshCount} unreadable scene/procedural mesh(es) without an import asset path, so Read/Write could not be changed automatically.");
        }
    }

    private static Mesh ReloadMeshFromAssetIfReadable(Mesh mesh, MeshFilter meshFilter)
    {
        if (mesh == null || mesh.isReadable)
            return mesh;

        string assetPath = AssetDatabase.GetAssetPath(mesh);

        if (string.IsNullOrEmpty(assetPath))
            return mesh;

        Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().ToArray();

        for (int i = 0; i < meshes.Length; i++)
        {
            Mesh candidate = meshes[i];

            if (candidate == null || candidate.name != mesh.name || !candidate.isReadable)
                continue;

            if (meshFilter != null)
                meshFilter.sharedMesh = candidate;

            return candidate;
        }

        return mesh;
    }
#endif

    private bool ShouldUseMeshFilter(MeshFilter meshFilter)
    {
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return false;

        if (!includeRootRenderer && meshFilter.transform == transform)
            return false;

        if (combinedObject != null && meshFilter.gameObject == combinedObject)
            return false;

        RuntimeMeshCombiner owningCombiner = meshFilter.GetComponentInParent<RuntimeMeshCombiner>();

        if (owningCombiner != null && owningCombiner != this)
            return false;

        MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();

        if (meshRenderer == null)
            return false;

        if (!includeDisabledRenderers && !meshRenderer.enabled)
            return false;

        return true;
    }

    private void CreateCombinedObject(List<Material> materials)
    {
        combinedObject = new GameObject(combinedObjectName);
        combinedObject.transform.SetParent(transform, false);
        combinedObject.layer = sourceRendererTemplate != null
            ? sourceRendererTemplate.gameObject.layer
            : gameObject.layer;

        MeshFilter meshFilter = combinedObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = combinedMesh;

        MeshRenderer meshRenderer = combinedObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = materials.ToArray();
        ApplyRendererSettings(meshRenderer);

        if (!addMeshCollider)
            return;

        MeshCollider meshCollider = combinedObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = combinedMesh;
    }

    private void ClearCombinedObject()
    {
        if (combinedMesh != null)
        {
            DestroyObject(combinedMesh);
            combinedMesh = null;
        }

        Transform existingCombinedObject = transform.Find(combinedObjectName);

        if (existingCombinedObject != null)
            DestroyObject(existingCombinedObject.gameObject);

        combinedObject = null;
        sourceRendererTemplate = null;
    }

    private void ApplyRendererSettings(MeshRenderer targetRenderer)
    {
        if (targetRenderer == null || sourceRendererTemplate == null)
            return;

        targetRenderer.shadowCastingMode = sourceRendererTemplate.shadowCastingMode;
        targetRenderer.receiveShadows = sourceRendererTemplate.receiveShadows;
        targetRenderer.lightProbeUsage = sourceRendererTemplate.lightProbeUsage;
        targetRenderer.reflectionProbeUsage = sourceRendererTemplate.reflectionProbeUsage;
        targetRenderer.motionVectorGenerationMode = sourceRendererTemplate.motionVectorGenerationMode;
        targetRenderer.renderingLayerMask = sourceRendererTemplate.renderingLayerMask;
    }

    private static CombineInstance[] BuildMaterialCombineInstances(
        List<List<CombineInstance>> combinesByMaterial,
        List<Material> orderedMaterials)
    {
        CombineInstance[] materialCombines = new CombineInstance[orderedMaterials.Count];

        for (int i = 0; i < orderedMaterials.Count; i++)
        {
            Material material = orderedMaterials[i];
            List<CombineInstance> sourceCombines = combinesByMaterial[i];
            int vertexCount = GetSourceVertexCount(sourceCombines);
            Mesh materialMesh = new Mesh
            {
                name = material != null ? $"{material.name} Combined" : "Null Material Combined",
                indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            materialMesh.CombineMeshes(sourceCombines.ToArray(), true, true, false);

            materialCombines[i] = new CombineInstance
            {
                mesh = materialMesh,
                subMeshIndex = 0,
                transform = Matrix4x4.identity
            };
        }

        return materialCombines;
    }

    private static int GetOrAddMaterialIndex(
        List<Material> orderedMaterials,
        List<List<CombineInstance>> combinesByMaterial,
        Material material)
    {
        for (int i = 0; i < orderedMaterials.Count; i++)
        {
            if (orderedMaterials[i] == material)
                return i;
        }

        orderedMaterials.Add(material);
        combinesByMaterial.Add(new List<CombineInstance>());
        return orderedMaterials.Count - 1;
    }

    private static int GetSourceVertexCount(List<CombineInstance> sourceCombines)
    {
        int vertexCount = 0;

        for (int i = 0; i < sourceCombines.Count; i++)
        {
            Mesh mesh = sourceCombines[i].mesh;

            if (mesh != null)
                vertexCount += mesh.vertexCount;
        }

        return vertexCount;
    }

    private static int GetCombinedIndexCount(Mesh mesh)
    {
        if (mesh == null)
            return 0;

        int indexCount = 0;

        for (int i = 0; i < mesh.subMeshCount; i++)
            indexCount += (int)mesh.GetIndexCount(i);

        return indexCount;
    }

    private static void DestroyObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
