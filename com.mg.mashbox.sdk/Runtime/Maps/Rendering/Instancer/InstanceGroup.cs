using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace MashBoxSDK.Map.Rendering.Instancer
{
    [AddComponentMenu("MashBox/Maps/Rendering/Instance Group")]
    public class InstanceGroup : MonoBehaviour
    {
        [SerializeField, Tooltip("Cache static visual children and remove their runtime GameObjects after batching. Edit Mode objects are preserved.")]
        bool removeRuntimeChildren = true;

        [Serializable]
        public class CachedRenderer
        {
            public Mesh mesh;
            public Material[] materials;
            public Matrix4x4 localMatrix;
            public Vector3 localCenter, localProbe;
            public int layer;
            public ShadowCastingMode shadows;
            public bool receiveShadows;
            public LightProbeUsage probes;
        }

        // Serialized so a Play Mode script reload cannot lose already removed children.
        [SerializeField, HideInInspector] List<CachedRenderer> cachedRenderers = new();
        public IReadOnlyList<CachedRenderer> CachedRenderers => cachedRenderers;

        public void CommitRuntimeChildren(ICollection<MeshRenderer> batchedRenderers)
        {
            if (!Application.isPlaying || !removeRuntimeChildren) return;
            var managed = new HashSet<MeshRenderer>(batchedRenderers);
            var remove = new List<GameObject>();
            foreach (Transform child in transform)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                var renderers = child.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length == 0) continue;
                bool safe = true;
                foreach (var renderer in renderers)
                    if (!managed.Contains(renderer)) { safe = false; break; }
                // Only discard purely visual branches. Preserve scripts, colliders, nested
                // InstanceGroups and unsupported renderers through the normal source path.
                foreach (var component in child.GetComponentsInChildren<Component>(true))
                    if (!(component is Transform) && !(component is MeshFilter) &&
                        !(component is MeshRenderer) && !(component is LODGroup))
                        { safe = false; break; }
                if (!safe) continue;
                var inverse = transform.worldToLocalMatrix;
                foreach (var renderer in renderers)
                {
                    cachedRenderers.Add(new CachedRenderer {
                        mesh = renderer.GetComponent<MeshFilter>().sharedMesh,
                        materials = renderer.sharedMaterials,
                        localMatrix = inverse * renderer.localToWorldMatrix,
                        localCenter = inverse.MultiplyPoint3x4(renderer.bounds.center),
                        localProbe = inverse.MultiplyPoint3x4(renderer.probeAnchor != null ? renderer.probeAnchor.position : renderer.bounds.center),
                        layer = renderer.gameObject.layer, shadows = renderer.shadowCastingMode,
                        receiveShadows = renderer.receiveShadows, probes = renderer.lightProbeUsage
                    });
                }
                remove.Add(child.gameObject);
            }
            foreach (var child in remove)
            {
                child.SetActive(false);
                Destroy(child);
            }
        }

        void OnEnable() => InstancingManager.Instance.RegisterGroup(this);

        void OnDisable()
        {
            if (InstancingManager.ExistingInstance != null)
                InstancingManager.ExistingInstance.UnregisterGroup(this);
        }

        void OnTransformChildrenChanged() => RefreshInstances();

        /// <summary>Call after changing child transforms, meshes or materials at runtime.</summary>
        [ContextMenu("Refresh Instances")]
        public void RefreshInstances()
        {
            if (InstancingManager.ExistingInstance != null)
                InstancingManager.ExistingInstance.MarkDirty();
        }

        public MeshRenderer[] GetRenderers() => GetComponentsInChildren<MeshRenderer>();
    }
}
