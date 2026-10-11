using UnityEngine;

public partial class RuntimeMeshCombiner
{
    // A rail can share an ancestor with thousands of batched props without being
    // part of their geometry. Only expose a batch that actually contains the edit.
    public bool ContainsCombinedSource(Transform target)
    {
        foreach (var renderer in sourceRenderers)
            if (renderer && (renderer.transform == target || renderer.transform.IsChildOf(target))) return true;
        return false;
    }
}
