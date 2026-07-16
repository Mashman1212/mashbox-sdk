using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class RuntimeMeshCombiner : MonoBehaviour
{
    [SerializeField] private bool combineOnAwake = true;
    [SerializeField] private bool includeInactiveChildren;
    [SerializeField] private bool includeDisabledRenderers;
    [SerializeField] private bool includeRootRenderer;
    [SerializeField] private bool disableSourceRenderers = true;
    [SerializeField] private bool addMeshCollider;
    [SerializeField] private string combinedObjectName = "Combined Mesh";

    private readonly List<MeshRenderer> sourceRenderers = new List<MeshRenderer>();
    private readonly List<bool> sourceRendererEnabledStates = new List<bool>();
    private GameObject combinedObject;
    private Mesh combinedMesh;

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

    [ContextMenu("Combine Now")]
    public void Combine()
    {
        RestoreSourceRenderers();
        ClearCombinedObject();

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(includeInactiveChildren);
        Dictionary<Material, List<CombineInstance>> combinesByMaterial = new Dictionary<Material, List<CombineInstance>>();
        List<Material> orderedMaterials = new List<Material>();
        int vertexCount = 0;

        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];

            if (!ShouldUseMeshFilter(meshFilter))
                continue;

            Mesh mesh = meshFilter.sharedMesh;
            MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
            Material[] materials = meshRenderer.sharedMaterials;
            int subMeshCount = Mathf.Min(mesh.subMeshCount, materials.Length);
            bool usedRenderer = false;

            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                Material material = materials[subMeshIndex];

                if (material == null)
                    continue;

                if (!combinesByMaterial.TryGetValue(material, out List<CombineInstance> combines))
                {
                    combines = new List<CombineInstance>();
                    combinesByMaterial.Add(material, combines);
                    orderedMaterials.Add(material);
                }

                combines.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = subMeshIndex,
                    transform = transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix
                });
                usedRenderer = true;
            }

            if (!usedRenderer)
                continue;

            vertexCount += mesh.vertexCount;
            sourceRenderers.Add(meshRenderer);
            sourceRendererEnabledStates.Add(meshRenderer.enabled);
        }

        if (orderedMaterials.Count == 0)
            return;

        CombineInstance[] materialCombines = new CombineInstance[orderedMaterials.Count];

        for (int i = 0; i < orderedMaterials.Count; i++)
        {
            Mesh materialMesh = new Mesh
            {
                name = $"{combinedObjectName} Material {i:00}",
                indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            materialMesh.CombineMeshes(combinesByMaterial[orderedMaterials[i]].ToArray(), true, true, false);

            materialCombines[i] = new CombineInstance
            {
                mesh = materialMesh,
                subMeshIndex = 0,
                transform = Matrix4x4.identity
            };
        }

        combinedMesh = new Mesh
        {
            name = $"{name} Combined Mesh",
            indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        combinedMesh.CombineMeshes(materialCombines, false, false, false);
        combinedMesh.RecalculateBounds();

        for (int i = 0; i < materialCombines.Length; i++)
            DestroyObject(materialCombines[i].mesh);

        CreateCombinedObject(orderedMaterials);

        if (disableSourceRenderers)
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

        MeshFilter meshFilter = combinedObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = combinedMesh;

        MeshRenderer meshRenderer = combinedObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = materials.ToArray();

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
