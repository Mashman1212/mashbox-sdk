using System.Collections.Generic;
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public partial class RuntimeMeshCombiner : MonoBehaviour
{
    [SerializeField] private bool combineOnAwake = true;
    [SerializeField] private bool includeInactiveChildren;
    [SerializeField] private bool includeDisabledRenderers;
    [SerializeField] private bool includeRootRenderer;
    [SerializeField] private bool disableSourceRenderers = true;
    [SerializeField] private bool addMeshCollider;
    [Tooltip("For multi-material meshes, draw shadows using a separate single-submesh proxy. Cutout, transparency and custom vertex deformation are not preserved by the proxy.")]
    [SerializeField] private bool combineShadowSubmeshes = true;
    [Tooltip("Optional opaque shadow material. If empty, use a supported opaque pipeline shader. Keep that shader included in player builds.")]
    [SerializeField] private Material shadowMaterial;
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
    private Mesh combinedShadowMesh;
    private Material generatedShadowMaterial;
    private MeshRenderer sourceRendererTemplate;

    public static bool UseCombinedRendering { get; private set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRenderingMode() => UseCombinedRendering = true;

    // Process-local A/B switch: no rebuild, no scene/network changes, and no
    // missing background. Retained special-case renderers are never touched.
    public static void SetUseCombinedRendering(bool combined)
    {
        UseCombinedRendering = combined;
        foreach (RuntimeMeshCombiner combiner in FindObjectsByType<RuntimeMeshCombiner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            combiner.SetCombinedVisibility(combined && combiner.enabled);
    }

    private void SetCombinedVisibility(bool combined)
    {
        if (combinedObject == null || sourceRenderers.Count == 0) return;
        combinedObject.SetActive(combined);
        for (int i = 0; i < sourceRenderers.Count; i++)
            if (sourceRenderers[i] != null)
                sourceRenderers[i].enabled = combined && disableSourceRenderers ? false : sourceRendererEnabledStates[i];
    }

    private void OnEnable() => SetCombinedVisibility(UseCombinedRendering);
    private void OnDisable() => SetCombinedVisibility(false);

    private void Awake()
    {
        if (combineOnAwake)
            Combine();
    }

    private void OnDestroy()
    {
        ClearSpatialMeshes();
        if (combinedMesh != null)
            DestroyObject(combinedMesh);
        ClearShadowResources();
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

        if (useSpatialChunks)
        {
            CombineSpatially();
            return;
        }

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(includeInactiveChildren);

        List<Material> orderedMaterials = new List<Material>();
        List<List<CombineInstance>> combineInstancesByMaterial = new List<List<CombineInstance>>();
        int vertexCount = 0;
        int sourceMeshCount = 0;
        int unreadableMeshCount = 0;
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
                unreadableMeshCount++;
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
                // CombineMeshes copies source vertices for each submesh instance.
                vertexCount += mesh.vertexCount;
            }

            if (!usedRenderer)
                continue;

            sourceMeshCount++;
            sourceRenderers.Add(meshRenderer);
            sourceRendererEnabledStates.Add(meshRenderer.enabled);

            if (sourceRendererTemplate == null)
                sourceRendererTemplate = meshRenderer;
        }

        if (unreadableMeshCount > 0)
            Debug.LogWarning($"RuntimeMeshCombiner on '{name}' skipped {unreadableMeshCount} unreadable source renderer(s); their original renderers remain unchanged. Prepare child mesh Read/Write data in the editor before building.", this);

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
        SetCombinedVisibility(UseCombinedRendering && enabled);
    }

    [ContextMenu("Restore Source Renderers")]
    public void RestoreSourceRenderers()
    {
        if (combinedObject != null)
            combinedObject.SetActive(false);
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
    // Read-only inspection: do not combine, restore renderers or reimport assets.
    public List<MeshFilter> GetMaterialPreviewMeshFilters()
    {
        List<MeshFilter> results = new List<MeshFilter>();
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(includeInactiveChildren))
        {
            if (ShouldUseMeshFilter(filter, true))
                results.Add(filter);
        }
        return results;
    }

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

        if (EditorApplication.isPlayingOrWillChangePlaymode || combiners.Length == 0)
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
                    if (!EnableReadWriteOnSerializedMesh(mesh))
                        unreadableSceneMeshCount++;
                    else if (combiner.gameObject.scene.IsValid())
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(combiner.gameObject.scene);
                    continue;
                }

                if (AssetImporter.GetAtPath(assetPath) is ModelImporter)
                    modelAssetPaths.Add(assetPath);
                else if (!EnableReadWriteOnSerializedMesh(mesh))
                    unreadableSceneMeshCount++;
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
                $"RuntimeMeshCombiner found {unreadableSceneMeshCount} unreadable mesh(es) that could not be prepared automatically. Recreate their CPU mesh data before building.");
        }
    }

    // Native .asset and scene-embedded meshes have no ModelImporter. Preserve
    // their serialized CPU data before it can be discarded by a player build.
    public static bool EnableReadWriteOnSerializedMesh(Mesh mesh)
    {
        if (mesh == null || mesh.isReadable) return mesh != null;
        string path = AssetDatabase.GetAssetPath(mesh);
        if (!string.IsNullOrEmpty(path) && !path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
            return false;
        SerializedObject serialized = new SerializedObject(mesh);
        SerializedProperty readable = serialized.FindProperty("m_IsReadable");
        if (readable == null) return false;
        readable.boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mesh);
        return mesh.isReadable;
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

    private bool ShouldUseMeshFilter(MeshFilter meshFilter, bool preview = false)
    {
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return false;

        // Lofts own their visual sections and their per-camera distance culling.
        // Combining their sources/chunks would bypass that ownership and can race
        // the loft's LateUpdate, producing missing or duplicate trail geometry.
        if (meshFilter.GetComponentInParent<MashBoxSDK.Maps.Spline.MultiSplineLoft>(true) != null ||
            meshFilter.GetComponentInParent<MashBoxSDK.Maps.Spline.LoftVisualChunks>(true) != null)
            return false;

        if (!includeRootRenderer && meshFilter.transform == transform)
            return false;

        if (combinedObject != null && meshFilter.transform.IsChildOf(combinedObject.transform))
            return false;

        RuntimeMeshCombiner owningCombiner = meshFilter.GetComponentInParent<RuntimeMeshCombiner>();

        if (owningCombiner != null && owningCombiner != this)
            return false;

        MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();

        if (meshRenderer == null)
            return false;

        bool enabled = meshRenderer.enabled;
        if (preview)
        {
            int sourceIndex = sourceRenderers.IndexOf(meshRenderer);
            if (sourceIndex >= 0 && sourceIndex < sourceRendererEnabledStates.Count)
                enabled = sourceRendererEnabledStates[sourceIndex];
        }

        if (!includeDisabledRenderers && !enabled)
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
        TryCreateShadowProxy(meshRenderer, materials);

        if (!addMeshCollider)
            return;

        MeshCollider meshCollider = combinedObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = combinedMesh;
    }

    private void ClearCombinedObject()
    {
        ClearSpatialMeshes();
        ClearShadowResources();
        if (combinedMesh != null)
        {
            DestroyObject(combinedMesh);
            combinedMesh = null;
        }

        Transform existingCombinedObject = transform.Find(combinedObjectName);

        if (existingCombinedObject != null)
        {
            // Destroy is deferred in Play Mode. Do not collect yesterday's output
            // as a source when Combine is called again in the same frame.
            existingCombinedObject.gameObject.SetActive(false);
            existingCombinedObject.SetParent(null, false);
            DestroyObject(existingCombinedObject.gameObject);
        }

        combinedObject = null;
        sourceRendererTemplate = null;
    }

    private void ClearShadowResources()
    {
        if (combinedShadowMesh != null)
            DestroyObject(combinedShadowMesh);
        if (generatedShadowMaterial != null)
            DestroyObject(generatedShadowMaterial);
        combinedShadowMesh = null;
        generatedShadowMaterial = null;
    }

    private void TryCreateShadowProxy(MeshRenderer visibleRenderer, List<Material> materials)
    {
        if (!combineShadowSubmeshes || combinedMesh.subMeshCount <= 1 ||
            visibleRenderer.shadowCastingMode == ShadowCastingMode.Off)
            return;

        // Flatten the output of the existing combiner. It already inherits the
        // first source's renderer settings and does not copy property blocks;
        // differences among the original sources must not veto this proxy.

        Material template = shadowMaterial;
        if (template == null)
        {
            // Referencing a source shader avoids relying on Shader.Find in a
            // stripped player when a stock pipeline material is already present.
            foreach (Material material in materials)
            {
                if (material != null && IsSupportedShadowShader(material.shader))
                {
                    template = material;
                    break;
                }
            }
        }

        Shader shader = template != null ? template.shader : FindShadowShader();
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning($"RuntimeMeshCombiner on '{name}' could not find a supported shadow shader. Assign Shadow Material; original shadows were retained.", this);
            return;
        }

        generatedShadowMaterial = template != null ? new Material(template) : new Material(shader);
        generatedShadowMaterial.name = $"{name} Combined Shadow Material";
        generatedShadowMaterial.DisableKeyword("_ALPHATEST_ON");
        generatedShadowMaterial.DisableKeyword("_ALPHABLEND_ON");
        generatedShadowMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        generatedShadowMaterial.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        generatedShadowMaterial.DisableKeyword("_VERTEX_DISPLACEMENT");
        generatedShadowMaterial.DisableKeyword("_PIXEL_DISPLACEMENT");
        generatedShadowMaterial.DisableKeyword("_TESSELLATION_DISPLACEMENT");
        generatedShadowMaterial.DisableKeyword("_DEPTHOFFSET_ON");
        SetMaterialFloat(generatedShadowMaterial, "_AlphaCutoffEnable", 0);
        SetMaterialFloat(generatedShadowMaterial, "_AlphaClip", 0);
        SetMaterialFloat(generatedShadowMaterial, "_SurfaceType", 0);
        SetMaterialFloat(generatedShadowMaterial, "_Surface", 0);
        SetMaterialFloat(generatedShadowMaterial, "_Mode", 0);
        SetMaterialFloat(generatedShadowMaterial, "_DisplacementMode", 0);
        SetMaterialFloat(generatedShadowMaterial, "_ZWrite", 1);
        SetMaterialFloat(generatedShadowMaterial, "_SrcBlend", (float)BlendMode.One);
        SetMaterialFloat(generatedShadowMaterial, "_DstBlend", (float)BlendMode.Zero);
        generatedShadowMaterial.renderQueue = (int)RenderQueue.Geometry;
        generatedShadowMaterial.SetShaderPassEnabled("ShadowCaster", true);

        if (visibleRenderer.shadowCastingMode == ShadowCastingMode.TwoSided)
        {
            if (!generatedShadowMaterial.HasProperty("_CullMode") && !generatedShadowMaterial.HasProperty("_Cull"))
            {
                ClearShadowResources();
                Debug.LogWarning($"RuntimeMeshCombiner on '{name}' retained two-sided shadows because the proxy shader has no culling control.", this);
                return;
            }
            SetMaterialFloat(generatedShadowMaterial, "_CullMode", (float)CullMode.Off);
            SetMaterialFloat(generatedShadowMaterial, "_CullModeForward", (float)CullMode.Off);
            SetMaterialFloat(generatedShadowMaterial, "_Cull", (float)CullMode.Off);
        }

        List<int> triangles = new List<int>();
        for (int i = 0; i < combinedMesh.subMeshCount; i++)
        {
            if (combinedMesh.GetTopology(i) != MeshTopology.Triangles)
            {
                ClearShadowResources();
                Debug.LogWarning($"RuntimeMeshCombiner on '{name}' retained original shadows because a submesh is not triangles.", this);
                return;
            }
            triangles.AddRange(combinedMesh.GetTriangles(i));
        }

        // Copy vertex buffers once, then flatten only the index buffer. Using
        // CombineMeshes per submesh would duplicate the entire vertex buffer.
        combinedShadowMesh = Instantiate(combinedMesh);
        combinedShadowMesh.name = $"{name} Combined Shadow Mesh";
        combinedShadowMesh.subMeshCount = 1;
        combinedShadowMesh.SetTriangles(triangles, 0, false);
        combinedShadowMesh.bounds = combinedMesh.bounds;

        GameObject shadowObject = new GameObject("Combined Shadows");
        shadowObject.transform.SetParent(combinedObject.transform, false);
        shadowObject.layer = combinedObject.layer;
        shadowObject.AddComponent<MeshFilter>().sharedMesh = combinedShadowMesh;
        MeshRenderer shadowRenderer = shadowObject.AddComponent<MeshRenderer>();
        shadowRenderer.sharedMaterial = generatedShadowMaterial;
        ApplyRendererSettings(shadowRenderer);
        shadowRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        shadowRenderer.receiveShadows = false;
        if (visibleRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
            visibleRenderer.enabled = false;
        visibleRenderer.shadowCastingMode = ShadowCastingMode.Off;

        Debug.Log($"RuntimeMeshCombiner on '{name}' reduced combined shadow material submeshes from {combinedMesh.subMeshCount} to 1; triangle count is unchanged. This is a submission-count reduction, not a measured GPU speedup.", this);
    }

    private static bool IsSupportedShadowShader(Shader shader)
    {
        if (shader == null)
            return false;
        return shader.name == "HDRP/Lit" || shader.name == "HDRP/Unlit" ||
               shader.name == "Universal Render Pipeline/Lit" ||
               shader.name == "Universal Render Pipeline/Unlit" || shader.name == "Standard";
    }

    private static Shader FindShadowShader()
    {
        if (GraphicsSettings.currentRenderPipeline == null)
            return Shader.Find("Standard");
        string pipeline = GraphicsSettings.currentRenderPipeline.GetType().Name;
        if (pipeline.Contains("HDRenderPipeline"))
            return Shader.Find("HDRP/Lit");
        if (pipeline.Contains("UniversalRenderPipeline"))
            return Shader.Find("Universal Render Pipeline/Lit");
        return null;
    }

    private static void SetMaterialFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
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
