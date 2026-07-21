using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class OfflineMeshCombiner : MonoBehaviour
{
    [Serializable]
    private struct SourceRendererState
    {
        public MeshRenderer renderer;
        public bool enabled;

        public SourceRendererState(MeshRenderer renderer, bool enabled)
        {
            this.renderer = renderer;
            this.enabled = enabled;
        }
    }

    [SerializeField, Tooltip("Include child GameObjects that are currently inactive when baking.")]
    private bool includeInactiveChildren;
    [SerializeField, Tooltip("Include disabled MeshRenderers. Their original enabled state is restored when the bake is removed.")]
    private bool includeDisabledRenderers;
    [SerializeField, Tooltip("Include a MeshFilter and MeshRenderer attached to this same GameObject.")]
    private bool includeRootRenderer;
    [SerializeField, Tooltip("Disable the source MeshRenderers after a successful bake.")]
    private bool disableSourceRenderers = true;
    [SerializeField, Tooltip("Add a MeshCollider that uses the generated combined mesh.")]
    private bool addMeshCollider;
    [SerializeField, Tooltip("Return model importer Read/Write settings to their original values after baking. This avoids extra runtime mesh memory.")]
    private bool restoreSourceReadWriteAfterBake = true;
    [SerializeField, Tooltip("Name of the generated child GameObject in the scene.")]
    private string combinedObjectName = "Offline Combined Mesh";

    [SerializeField, HideInInspector] private GameObject bakedObject;
    [SerializeField, HideInInspector] private Mesh bakedMesh;
    [SerializeField, HideInInspector] private List<SourceRendererState> sourceRendererStates = new List<SourceRendererState>();

    public bool IncludeInactiveChildren => includeInactiveChildren;
    public bool IncludeDisabledRenderers => includeDisabledRenderers;
    public bool IncludeRootRenderer => includeRootRenderer;
    public bool DisableSourceRenderers => disableSourceRenderers;
    public bool AddMeshCollider => addMeshCollider;
    public bool RestoreSourceReadWriteAfterBake => restoreSourceReadWriteAfterBake;
    public string CombinedObjectName => string.IsNullOrWhiteSpace(combinedObjectName)
        ? "Offline Combined Mesh"
        : combinedObjectName;
    public GameObject BakedObject => bakedObject;
    public Mesh BakedMesh => bakedMesh;
    public bool HasBake => bakedObject != null && bakedMesh != null;

#if UNITY_EDITOR
    public void RestoreRecordedSourceRenderers()
    {
        for (int i = 0; i < sourceRendererStates.Count; i++)
        {
            SourceRendererState state = sourceRendererStates[i];
            if (state.renderer != null)
                state.renderer.enabled = state.enabled;
        }

        sourceRendererStates.Clear();
    }

    public void RecordBake(
        GameObject generatedObject,
        Mesh generatedMesh,
        IList<MeshRenderer> sourceRenderers,
        IList<bool> sourceEnabledStates)
    {
        bakedObject = generatedObject;
        bakedMesh = generatedMesh;
        sourceRendererStates.Clear();

        for (int i = 0; i < sourceRenderers.Count; i++)
        {
            MeshRenderer renderer = sourceRenderers[i];
            bool wasEnabled = i < sourceEnabledStates.Count && sourceEnabledStates[i];
            sourceRendererStates.Add(new SourceRendererState(renderer, wasEnabled));
        }
    }

    public void ClearBakeReferences()
    {
        bakedObject = null;
        bakedMesh = null;
        sourceRendererStates.Clear();
    }
#endif
}
