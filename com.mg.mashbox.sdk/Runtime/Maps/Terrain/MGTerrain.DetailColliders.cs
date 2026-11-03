using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if !UNITY_6000_0_OR_NEWER
// Keep serialized terrain settings loadable; terrain execution requires Unity 6.
using PhysicsMaterial = UnityEngine.PhysicMaterial;
#endif

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [Serializable] public sealed class DetailCapsuleShape
        {
            [Tooltip("Trunk centre in prototype-local units, before painted width/height scaling.")] public Vector3 center = new Vector3(0, 2, 0);
            [Min(.001f)] public float radius = .2f;
            [Min(.002f)] public float height = 4f;
        }
        [Serializable] public sealed class DetailCapsuleOverride
        {
            [Range(0,7)] public int subId;
            public bool enabled = true;
            public DetailCapsuleShape capsule = new DetailCapsuleShape();
        }
        [Serializable] public sealed class DetailCapsuleSettings
        {
            public bool enabled;
            public DetailCapsuleShape capsule = new DetailCapsuleShape();
            [Tooltip("Optional dimensions or collision exclusions for individual painted tree shape IDs (0–7). First matching override wins.")]
            public DetailCapsuleOverride[] subIdOverrides = Array.Empty<DetailCapsuleOverride>();
            public PhysicsMaterial material;
            [Range(0,31)] public int layer;
            public DetailCapsuleShape Shape(int id)
            {
                if (subIdOverrides != null) foreach (var item in subIdOverrides)
                    if (item != null && item.subId == id) return item.enabled ? item.capsule : null;
                return capsule;
            }
        }
        [SerializeField, Tooltip("Optional player/vehicle anchor. If empty, nearby detail collision follows the terrain gameplay camera (Camera.main fallback) in Play Mode.")]
        Transform m_DetailCollisionTarget;
        [SerializeField, Min(10), Tooltip("Physics radius in metres, independent of rendering distance, frustum and occlusion. Use a radius large enough for riding speed and camera offset.")]
        float m_DetailCollisionRadius = 80;
        [SerializeField, Tooltip("Preview nearby trunk capsules around the Scene view camera. Preview capsules do not participate in editor physics or get saved.")]
        bool m_PreviewDetailColliders;
        public bool PreviewDetailColliders => m_PreviewDetailColliders;
        public int ActiveDetailColliderCount { get; private set; }
        public bool HasDetailColliderCache => m_DetailColliderRoot || m_DetailCollisionCells.Count > 0;
        public Transform DetailCollisionTarget { get => m_DetailCollisionTarget; set { m_DetailCollisionTarget = value; m_DetailCollisionDirty = true; } }
        public float DetailCollisionRadius { get => Mathf.Max(10, m_DetailCollisionRadius); set { m_DetailCollisionRadius = Mathf.Max(10, value); m_DetailCollisionDirty = true; } }
        sealed class DetailCollisionInstance
        {
            public Vector3 center; public Quaternion rotation; public float radius, height;
            public int layer; public PhysicsMaterial material; public CapsuleCollider collider;
        }
        sealed class DetailCollisionCell { public int tick; public readonly List<DetailCollisionInstance> instances = new List<DetailCollisionInstance>(); }
        readonly Dictionary<DetailChunkKey, DetailCollisionCell> m_DetailCollisionCells = new Dictionary<DetailChunkKey, DetailCollisionCell>();
        readonly List<DetailChunkKey> m_RemoveDetailCollisionCells = new List<DetailChunkKey>();
        readonly Stack<CapsuleCollider> m_DetailColliderPool = new Stack<CapsuleCollider>();
        Transform m_DetailColliderRoot;
        Camera m_DetailCollisionCamera;
        bool m_DetailCollisionDirty = true, m_HasDetailCollisionFocus;
        Vector3 m_DetailCollisionFocus;
        Matrix4x4 m_DetailCollisionMatrix;
        int m_DetailCollisionTick;
        static readonly Unity.Profiling.ProfilerMarker s_DetailCollisionMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.DetailCollision");
        bool LayerHasCapsules(int index) => (uint)index < m_DensityDetailLayers.Count && m_DensityDetailLayers[index]?.CapsuleCollision?.enabled == true;
        bool PrototypeHasCapsules(Prototype prototype)
        {
            foreach (var layer in m_DensityDetailLayers)
                if (layer?.CapsuleCollision?.enabled == true && (uint)layer.PrototypeIndex < m_Prototypes.Count && m_Prototypes[layer.PrototypeIndex] == prototype) return true;
            return false;
        }
        void FixedUpdate()
        {
#if UNITY_6000_0_OR_NEWER
            if (!Application.isPlaying) return;
            if (m_DetailCollisionTarget) UpdateDetailColliders(m_DetailCollisionTarget.position);
            else
            {
                if (m_CachedGameplayCamera && m_CachedGameplayCamera.isActiveAndEnabled) m_DetailCollisionCamera = m_CachedGameplayCamera;
                else if (!m_DetailCollisionCamera || !m_DetailCollisionCamera.isActiveAndEnabled) m_DetailCollisionCamera = Camera.main;
                if (m_DetailCollisionCamera) UpdateDetailColliders(m_DetailCollisionCamera.transform.position);
                else if (m_DetailCollisionCells.Count > 0) ReleaseDetailColliders();
            }
#endif
        }
        public void InvalidateDetailColliders() { m_DetailCollisionDirty = true; }
        public void ReleaseDetailColliders()
        {
            m_DetailCollisionCells.Clear(); m_DetailColliderPool.Clear(); ActiveDetailColliderCount = 0; m_HasDetailCollisionFocus = false;
            if (m_DetailColliderRoot)
            {
                m_DetailColliderRoot.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(m_DetailColliderRoot.gameObject); else DestroyImmediate(m_DetailColliderRoot.gameObject);
                m_DetailColliderRoot = null;
            }
            m_DetailCollisionDirty = true;
        }
        void RecycleDetailCollider(DetailCollisionInstance instance)
        {
            if (!instance.collider) return;
            instance.collider.enabled = false; instance.collider.gameObject.SetActive(false);
            m_DetailColliderPool.Push(instance.collider); instance.collider = null; ActiveDetailColliderCount--;
        }
        void RecycleDetailCell(DetailCollisionCell cell) { foreach (var instance in cell.instances) RecycleDetailCollider(instance); }
        void ActivateDetailCollider(DetailCollisionInstance instance)
        {
            if (instance.collider) return;
            if (!m_DetailColliderRoot)
            {
                var root = new GameObject(name + " — Detail capsules") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, gameObject.scene); m_DetailColliderRoot = root.transform;
            }
            CapsuleCollider collider = null;
            while (m_DetailColliderPool.Count > 0 && !collider) collider = m_DetailColliderPool.Pop();
            if (!collider)
            {
                var go = new GameObject("Painted tree trunk") { hideFlags = HideFlags.HideAndDontSave };
                go.SetActive(false); go.transform.SetParent(m_DetailColliderRoot, false); collider = go.AddComponent<CapsuleCollider>();
            }
            collider.transform.SetPositionAndRotation(instance.center, instance.rotation);
            collider.gameObject.layer = instance.layer; collider.center = Vector3.zero; collider.direction = 1;
            collider.radius = instance.radius; collider.height = instance.height; collider.sharedMaterial = instance.material;
            collider.isTrigger = false; collider.enabled = Application.isPlaying; collider.gameObject.SetActive(true);
            instance.collider = collider; ActiveDetailColliderCount++;
        }
        public void UpdateDetailColliders(Vector3 focus, bool force = false)
        {
#if UNITY_6000_0_OR_NEWER
            if (!isActiveAndEnabled) return;
            bool any = false;
            foreach (var layer in m_DensityDetailLayers) if (layer?.CapsuleCollision?.enabled == true) { any = true; break; }
            if (!any || !MeshFilter || !MeshFilter.sharedMesh)
            { if (m_DetailColliderRoot) ReleaseDetailColliders(); return; }
            using var marker = s_DetailCollisionMarker.Auto();
            Matrix4x4 matrix = transform.localToWorldMatrix;
            if (matrix != m_DetailCollisionMatrix) m_DetailCollisionDirty = true;
            if (!force && !m_DetailCollisionDirty && m_HasDetailCollisionFocus && (focus - m_DetailCollisionFocus).sqrMagnitude < 16) return;
            if (m_DetailCollisionDirty)
            {
                foreach (var cell in m_DetailCollisionCells.Values) RecycleDetailCell(cell);
                m_DetailCollisionCells.Clear(); m_CachedDetailSurfaceMesh = null; m_CachedDetailSurfaceVertices = null;
            }
            m_DetailCollisionDirty = false; m_DetailCollisionMatrix = matrix; m_DetailCollisionFocus = focus; m_HasDetailCollisionFocus = true;
            int tick = ++m_DetailCollisionTick; Bounds bounds = MeshFilter.sharedMesh.bounds;
            if (bounds.size.x <= 0 || bounds.size.z <= 0) return;
            float radius = DetailCollisionRadius + 8; // movement guard between four-metre refreshes
            Vector3 local = transform.InverseTransformPoint(focus);
            var inverse = transform.worldToLocalMatrix;
            float reachX = new Vector3(inverse.m00, inverse.m01, inverse.m02).magnitude * radius;
            float reachZ = new Vector3(inverse.m20, inverse.m21, inverse.m22).magnitude * radius;
            int cells = Mathf.Clamp(m_DetailChunkCells, 2, 64);
            for (int index = 0; index < m_DensityDetailLayers.Count; index++)
            {
                var layer = m_DensityDetailLayers[index];
                if (layer?.CapsuleCollision?.enabled != true || !layer.RenderingEnabled || layer.PaletteSourceOnly || !layer.DensityMap || !layer.DensityMap.isReadable || (uint)layer.PrototypeIndex >= m_Prototypes.Count) continue;
                var prototype = m_Prototypes[layer.PrototypeIndex]; if (prototype == null) continue;
                // Include trunks offset from the instance pivot, including per-shape offsets.
                float ShapeReach(DetailCapsuleShape shape) => shape == null ? 0 : new Vector2(shape.center.x,shape.center.z).magnitude + Mathf.Max(.001f,shape.radius);
                float padding = ShapeReach(layer.CapsuleCollision.capsule);
                if (layer.CapsuleCollision.subIdOverrides != null) foreach (var item in layer.CapsuleCollision.subIdOverrides)
                    if (item != null && item.enabled) padding = Mathf.Max(padding,ShapeReach(item.capsule));
                padding *= Mathf.Max(Mathf.Abs(layer.MinWidth),Mathf.Abs(layer.MaxWidth)) * (layer.SizeMap ? 4 : 1);
                float layerReachX = reachX + padding, layerReachZ = reachZ + padding;
                int width = layer.DensityMap.width, height = layer.DensityMap.height;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((local.x - layerReachX - bounds.min.x) / bounds.size.x * width / cells) * cells);
                int x1 = Mathf.Min(width - 1, Mathf.FloorToInt((local.x + layerReachX - bounds.min.x) / bounds.size.x * width));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((local.z - layerReachZ - bounds.min.z) / bounds.size.z * height / cells) * cells);
                int z1 = Mathf.Min(height - 1, Mathf.FloorToInt((local.z + layerReachZ - bounds.min.z) / bounds.size.z * height));
                for (int z = z0; z <= z1; z += cells) for (int x = x0; x <= x1; x += cells)
                {
                    var key = new DetailChunkKey(index, x, z, cells, 0);
                    if (!m_DetailCollisionCells.TryGetValue(key, out var cell))
                    {
                        cell = BuildDetailCollisionCell(index, layer, prototype, bounds, cells, x, z, matrix);
                        m_DetailCollisionCells.Add(key, cell);
                    }
                    cell.tick = tick;
                    foreach (var instance in cell.instances)
                    {
                        Vector3 axis = instance.rotation * Vector3.up;
                        Vector3 delta = focus - instance.center;
                        float segment = Mathf.Max(0, instance.height * .5f - instance.radius);
                        Vector3 closest = instance.center + axis * Mathf.Clamp(Vector3.Dot(delta, axis), -segment, segment);
                        if ((focus - closest).sqrMagnitude <= (radius + instance.radius) * (radius + instance.radius)) ActivateDetailCollider(instance);
                        else RecycleDetailCollider(instance);
                    }
                }
            }
            m_RemoveDetailCollisionCells.Clear();
            foreach (var pair in m_DetailCollisionCells) if (pair.Value.tick != tick) m_RemoveDetailCollisionCells.Add(pair.Key);
            foreach (var key in m_RemoveDetailCollisionCells) { RecycleDetailCell(m_DetailCollisionCells[key]); m_DetailCollisionCells.Remove(key); }
            // Retain a small pool for the next boundary crossing, not the whole travelled map.
            while (m_DetailColliderPool.Count > 256)
            {
                var collider = m_DetailColliderPool.Pop(); if (!collider) continue;
                if (Application.isPlaying) Destroy(collider.gameObject); else DestroyImmediate(collider.gameObject);
            }
#endif
        }
        DetailCollisionCell BuildDetailCollisionCell(int layerIndex, DensityDetailLayer layer, Prototype prototype, Bounds bounds, int cells, int firstX, int firstZ, Matrix4x4 terrainMatrix)
        {
            var result = new DetailCollisionCell();
            // Reuse the renderer's density cap, rounding and spawn commands. No GPU readback, mesh combination or render visibility dependency.
            var chunk = BuildDensityDetailChunk(layerIndex, layer, prototype, bounds, cells, firstX, firstZ, 0, 1, true);
            foreach (var spawn in chunk.proceduralSpawns)
            {
                var shape = layer.CapsuleCollision.Shape(spawn.slice); if (shape == null) continue;
                uint cellHash = Hash((uint)layer.Seed ^ (uint)(spawn.x * 73856093) ^ (uint)(spawn.z * 19349663));
                for (int item = 0; item < spawn.count; item++)
                {
                    uint hash = Hash(cellHash ^ (uint)(item * 83492791));
                    float jx = Hash01(hash), jz = Hash01(Hash(hash + 0x9E3779B9u));
                    float size = Mathf.Lerp(Mathf.Lerp(spawn.sizes.x, spawn.sizes.y, jx), Mathf.Lerp(spawn.sizes.z, spawn.sizes.w, jx), jz);
                    float width = Mathf.Lerp(layer.MinWidth, layer.MaxWidth, Hash01(Hash(hash + 0x85EBCA6Bu))) * size;
                    float height = Mathf.Lerp(layer.MinHeight, layer.MaxHeight, Hash01(Hash(hash + 0xC2B2AE35u))) * size;
                    float angle = Hash01(Hash(hash + 0x27D4EB2Fu)) * 360;
                    float u = (spawn.x + jx) / layer.DensityMap.width, v = (spawn.z + jz) / layer.DensityMap.height;
                    var position = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, u), SampleSurfaceHeight(u,v) + layer.YOffset, Mathf.Lerp(bounds.min.z, bounds.max.z, v));
                    Matrix4x4 instance = terrainMatrix * Matrix4x4.TRS(position, Quaternion.Euler(0,angle,0), new Vector3(width,height,width));
                    Vector3 up = instance.MultiplyVector(Vector3.up);
                    float r = Mathf.Max(.001f, shape.radius) * Mathf.Max(instance.MultiplyVector(Vector3.right).magnitude, instance.MultiplyVector(Vector3.forward).magnitude);
                    result.instances.Add(new DetailCollisionInstance { center = instance.MultiplyPoint3x4(shape.center), rotation = up.sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up,up.normalized) : Quaternion.identity,
                        radius = Mathf.Max(.001f,r), height = Mathf.Max(2*r, Mathf.Max(.002f,shape.height) * up.magnitude), layer = Mathf.Clamp(layer.CapsuleCollision.layer,0,31), material = layer.CapsuleCollision.material });
                }
            }
            return result;
        }
#if UNITY_EDITOR
        public void DrawDetailColliderPreview()
        {
            if (Application.isPlaying || !m_PreviewDetailColliders) return;
            using (new UnityEditor.Handles.DrawingScope(new Color(.25f,1,.3f,.85f)))
            foreach (var cell in m_DetailCollisionCells.Values) foreach (var instance in cell.instances)
            {
                if (!instance.collider) continue;
                Vector3 up = instance.rotation * Vector3.up, right = instance.rotation * Vector3.right, forward = instance.rotation * Vector3.forward;
                float half = Mathf.Max(0, instance.height * .5f - instance.radius);
                Vector3 a = instance.center + up*half, b = instance.center - up*half;
                UnityEditor.Handles.DrawWireDisc(a,up,instance.radius); UnityEditor.Handles.DrawWireDisc(b,up,instance.radius);
                UnityEditor.Handles.DrawLine(a + right*instance.radius,b + right*instance.radius); UnityEditor.Handles.DrawLine(a - right*instance.radius,b - right*instance.radius);
                UnityEditor.Handles.DrawLine(a + forward*instance.radius,b + forward*instance.radius); UnityEditor.Handles.DrawLine(a - forward*instance.radius,b - forward*instance.radius);
                UnityEditor.Handles.DrawWireArc(a,forward,right,180,instance.radius); UnityEditor.Handles.DrawWireArc(a,right,-forward,180,instance.radius);
                UnityEditor.Handles.DrawWireArc(b,forward,-right,180,instance.radius); UnityEditor.Handles.DrawWireArc(b,right,forward,180,instance.radius);
            }
        }
#endif
    }
}
