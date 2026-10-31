using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MG.GodSurface
{
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("MashBox/God Surface/God Prop Root")]
    public sealed partial class MGSurfaceWorld : MonoBehaviour
    {
        [Min(1)] public float cellSize = 64;
        [Min(1), Tooltip("Cull every registered prop in a cell at this distance in metres from the camera to the nearest point on its bounds. Applies even to props without LODGroups, and to shadow rendering.")]
        public float cellDrawDistance = 400;
        [Tooltip("Drag prop roots from anywhere in this scene. Only listed hierarchies are registered; an empty list renders nothing.")]
        public Transform[] roots = Array.Empty<Transform>();
        [Tooltip("Normal renderers remain active in edit mode unless this explicit preview is enabled.")]
        public bool previewInEditor;
        public bool renderInPlayMode = true;
        [Tooltip("Show occupied cell bounds in the Scene view, regardless of selection or the global Gizmos toggle.")]
        public bool drawCellBounds;
        public enum LodSelection { PerProp, PerCellFamily }
        [Tooltip("Per Cell Family switches matching LOD mesh sets together within each cell. The nearest family bounds and largest member determine detail, preserving nearby geometry.")]
        public LodSelection lodSelection = LodSelection.PerCellFamily;
        public int CellLodFamilyCount => cellLods.Count;
        public int LastLodEvaluations { get; private set; }
        public MGSurfaceLibrary surfaceLibrary;
#if UNITY_EDITOR
        [Tooltip("Cook and save this scene before MashBox bundle builds, using the bake settings below.")]
        public bool autoCookOnBundleBuild;
        public int cookResolution = 1024;
        public bool cookCompress = true;
        public bool cookVertexAO;
        [Range(16, 1024)] public int cookAOSamples = 256;
        [Range(.001f, 1)] public float cookAOReach = .12f;
#endif
        [Tooltip("Explicitly supported DOTS-instanced shader names. Unlisted shaders retain normal rendering.")]
        public string[] supportedShaders = { "HDRP/Lit", "MGShaders/HDRP/Lit/MG_Lit_Basic" };
        int registeredProps;
        public int InstanceCount => registeredProps;
        public int GeometryInstanceCount => entries.Count;
        public int CellCount => cells.Count;
        public int PrototypeCount => meshIDs.Count;
        public int DrawGroupCount => groups.Count;
        public int FallbackCount { get; private set; }
        public int LastVisibleInstances { get; private set; }
        public int LastVisibleCells { get; private set; }
        public int LastDrawCommands { get; private set; }
        public string LastError { get; private set; }
        public long InstanceBufferBytes { get; private set; }
        public bool IsRendering => brg != null;
        public IReadOnlyDictionary<string, int> FallbackReasons => fallbackReasons;
        public IReadOnlyList<Bounds> CellBounds => debugBounds;
#if UNITY_EDITOR
        static readonly List<MGSurfaceWorld> activeEditorWorlds = new List<MGSurfaceWorld>();
        public static IReadOnlyList<MGSurfaceWorld> ActiveEditorWorlds => activeEditorWorlds;
        public int CellBoundsVersion { get; private set; }
        public enum CellDebugState { Unknown, Visible, PartiallyDistanceCulled, DistanceCulled, NotDrawn }
        readonly Dictionary<int, CellDebugState[]> cellDebugViews = new Dictionary<int, CellDebugState[]>();
        // Request only the Scene view cameras being inspected. Reflection/shadow views cannot overwrite them.
        public CellDebugState[] GetCellDebugStates(int cameraInstanceID)
        {
            if (!cellDebugViews.TryGetValue(cameraInstanceID, out var states) || states.Length != cells.Count)
            {
                if (cellDebugViews.Count >= 16) cellDebugViews.Clear();
                states = new CellDebugState[cells.Count]; cellDebugViews[cameraInstanceID] = states;
            }
            return states;
        }
#endif

        internal sealed class Entry
        {
            public MeshRenderer renderer;
            public Mesh mesh;
            public Matrix4x4 matrix;
            public Bounds bounds;
            public int[] groups;
            public float[] surfaceIDs;
            public float geometryID = -1;
            public int propIndex;
            public int drawCount = 1; // First piece owns its contiguous GPU instance span; remaining pieces use zero.
            public int lod = -1, lodSet = -1;
            public bool previousForceOff;
        }
        sealed class CellData
        {
            public Bounds bounds;
            public readonly List<int> instances = new List<int>();
            public readonly Dictionary<string, int> lodFamilies = new Dictionary<string, int>();
        }
        sealed class LodData
        {
            public LODGroup source;
            public Vector3 center;
            public float size;
            public float[] thresholds;
            public string family;
            public int cellLod = -1;
        }
        sealed class CellLodData
        {
            public Bounds bounds;
            public float size;
            public float[] thresholds;
        }
        struct GroupKey : IEquatable<GroupKey>
        {
            public Mesh mesh;
            public Material material;
            public int submesh, layer;
            public uint renderingLayers;
            public ShadowCastingMode shadows;
            public bool receive, flipped;
            public MotionVectorGenerationMode motion;
            public bool Equals(GroupKey b) => mesh == b.mesh && material == b.material && submesh == b.submesh && layer == b.layer &&
                renderingLayers == b.renderingLayers && shadows == b.shadows && receive == b.receive && flipped == b.flipped && motion == b.motion;
            public override bool Equals(object obj) => obj is GroupKey key && Equals(key);
            public override int GetHashCode() => HashCode.Combine(mesh, material, submesh, layer, renderingLayers, shadows, receive, HashCode.Combine(flipped, motion));
        }
        sealed class GroupData
        {
            public GroupKey key;
            public BatchMeshID mesh;
            public BatchMaterialID material;
        }
        readonly List<Entry> entries = new List<Entry>();
        readonly List<int> drawEntries = new List<int>();
        readonly List<CellData> cells = new List<CellData>();
        readonly List<Bounds> debugBounds = new List<Bounds>();
        readonly List<LodData> lods = new List<LodData>();
        readonly List<CellLodData> cellLods = new List<CellLodData>();
        readonly List<GroupData> groups = new List<GroupData>();
        readonly Dictionary<string, int> fallbackReasons = new Dictionary<string, int>();
        readonly Dictionary<Mesh, BatchMeshID> meshIDs = new Dictionary<Mesh, BatchMeshID>();
        readonly Dictionary<Material, BatchMaterialID> materialIDs = new Dictionary<Material, BatchMaterialID>();
        readonly List<Material> ownedMaterials = new List<Material>();
        readonly HashSet<Transform> dirtyTransforms = new HashSet<Transform>();
        bool dirty = true;
        float lodBias = 1;
        bool WantsRendering => Application.isPlaying ? renderInPlayMode : previewInEditor;
        float builtCellSize = -1;
        MGSurfaceLibrary builtLibrary;
        Transform[] builtRoots;
        string[] builtShaders;
        bool builtPreview, builtPlay;
        static bool Same<T>(T[] a, T[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
            return true;
        }
        void OnEnable()
        {
            if (gameObject.scene.isLoaded && !IsSceneManager) { enabled = false; return; }
            dirty = true;
#if UNITY_EDITOR
            if (!activeEditorWorlds.Contains(this)) activeEditorWorlds.Add(this);
#endif
        }
        void OnDisable()
        {
#if UNITY_EDITOR
            activeEditorWorlds.Remove(this);
#endif
            Release();
        }
        void OnDestroy()
        {
            OnDisable();
            if (sceneManagers.TryGetValue(gameObject.scene.handle, out var owner) && owner == this) sceneManagers.Remove(gameObject.scene.handle);
        }
        void OnValidate()
        {
            cellSize = Mathf.Max(1, cellSize); cellDrawDistance = Mathf.Max(1, cellDrawDistance);
            // Debug colours and per-view culling settings do not invalidate meshes, matrices or lighting.
            if (builtCellSize != cellSize || builtLibrary != surfaceLibrary || builtPreview != previewInEditor || builtPlay != renderInPlayMode
                || !Same(builtRoots, roots) || !Same(builtShaders, supportedShaders)) dirty = true;
        }
        public void RequestRebuild() => dirty = true;
        public void NotifyTransformChanged(Transform changed) { if (changed) dirtyTransforms.Add(changed); }

        void LateUpdate()
        {
            if (!gameObject.scene.isLoaded) return;
            if (!IsSceneManager) { enabled = false; if (IsRendering) Release(); return; }
            if (!WantsRendering) { if (IsRendering) Release(); return; }
            lodBias = QualitySettings.lodBias;
            if (dirty) Rebuild();
            else if (dirtyTransforms.Count > 0) UpdateTransforms();
        }

        public IEnumerable<MeshRenderer> EnumerateRenderers()
        {
            var seen = new HashSet<MeshRenderer>();
            if (!IsSceneManager) yield break;
            var scan = roots ?? Array.Empty<Transform>();
            foreach (var root in scan)
            {
                if (!root || root.gameObject.scene != gameObject.scene) continue;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (seen.Add(r)) yield return r;
            }
        }

        public string UnsupportedReason(MeshRenderer r, bool checkStaticBatch = true)
        {
            if (!r || !r.enabled || !r.gameObject.activeInHierarchy) return "Inactive or disabled";
            if (r.forceRenderingOff) return "Already hidden by another system";
            var filter = r.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh || filter.sharedMesh.vertexCount == 0) return "Missing mesh";
            if (checkStaticBatch && r.isPartOfStaticBatch) return "Already statically batched";
            if (r.HasPropertyBlock()) return "Material property block";
            if (r.lightmapIndex >= 0 && r.lightmapIndex < 65534) return "Baked lightmap (fallback)";
            if (r.realtimeLightmapIndex >= 0 && r.realtimeLightmapIndex < 65534) return "Realtime lightmap (fallback)";
            if (r.lightProbeUsage == LightProbeUsage.UseProxyVolume || r.lightProbeUsage == LightProbeUsage.CustomProvided) return "Custom probe lighting (fallback)";
            if (r.reflectionProbeUsage != ReflectionProbeUsage.Off && r.probeAnchor) return "Custom probe anchor (fallback)";
            var lod = r.GetComponentInParent<LODGroup>();
            if (lod && lod.enabled && lod.fadeMode != LODFadeMode.None) return "LOD crossfade (fallback)";
            var mats = r.sharedMaterials;
            if (mats.Length != filter.sharedMesh.subMeshCount) return "Material/submesh count mismatch";
            foreach (var m in mats)
            {
                if (!m || !m.shader || !m.shader.isSupported) return "Missing or unsupported shader";
                if (m.shader.name == MGSurfaceLibrary.ShaderName)
                {
                    if (!surfaceLibrary || !surfaceLibrary.TryResolve(m, out _, out _)) return "Surface material missing from assigned library";
                }
                else if (Array.IndexOf(supportedShaders ?? Array.Empty<string>(), m.shader.name) < 0) return "Shader not on pilot allowlist";
                if (m.renderQueue >= 2501 || (m.HasProperty("_SurfaceType") && m.GetFloat("_SurfaceType") > 0)) return "Transparent surface (fallback)";
            }
            for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                if (filter.sharedMesh.GetTopology(i) != MeshTopology.Triangles) return "Non-triangle topology";
            if (Mathf.Abs(r.localToWorldMatrix.determinant) < 1e-10f) return "Singular transform";
            return null;
        }

        [ContextMenu("Rebuild Spatial Registry")]
        public void Rebuild()
        {
            using var sample = rebuildMarker.Auto(); using var timing = new MeasuredScope(this, true);

            Release(); dirty = false; LastError = null;
            builtCellSize = cellSize; builtLibrary = surfaceLibrary; builtPreview = previewInEditor; builtPlay = renderInPlayMode;
            builtRoots = roots == null ? null : (Transform[])roots.Clone();
            builtShaders = supportedShaders == null ? null : (string[])supportedShaders.Clone();
            if (!IsSceneManager) { LastError = "Only one God Prop Root is allowed per scene. Use the existing manager."; return; }
            if (!isActiveAndEnabled || !WantsRendering) return;
            if (!SystemInfo.supportsInstancing || GraphicsSettings.currentRenderPipeline == null ||
                BatchRendererGroup.BufferTarget != BatchBufferTarget.RawBuffer)
            { LastError = "This pilot requires an SRP and raw-buffer BatchRendererGroup support. Normal rendering remains active."; return; }
            try
            {
                brg = new BatchRendererGroup(Cull, IntPtr.Zero);
                var groupMap = new Dictionary<GroupKey, int>();
                var lodMap = new Dictionary<LODGroup, int>();
                int Register(GroupKey key)
                {
                    var mesh = key.mesh; var renderMaterial = key.material;
                    if (!groupMap.TryGetValue(key, out int index))
                    {
                        index = groups.Count; groupMap.Add(key, index);
                        if (!meshIDs.TryGetValue(mesh, out var meshID)) { meshID = brg.RegisterMesh(mesh); meshIDs.Add(mesh, meshID); }
                        if (!materialIDs.TryGetValue(renderMaterial, out var materialID))
                        {
                            // Never modify source assets just to enable instancing.
                            var copy = new Material(renderMaterial) { enableInstancing = true, hideFlags = HideFlags.HideAndDontSave };
                            if (surfaceLibrary && surfaceLibrary.geometry && copy.HasProperty("_MGGeometryVertices")) surfaceLibrary.geometry.Bind(copy);
                            ownedMaterials.Add(copy); materialID = brg.RegisterMaterial(copy); materialIDs.Add(renderMaterial, materialID);
                        }
                        groups.Add(new GroupData { key = key, mesh = meshID, material = materialID });
                    }
                    return index;
                }
                foreach (var r in EnumerateRenderers())
                {
                    string reason = UnsupportedReason(r);
                    if (reason != null) { FallbackCount++; fallbackReasons.TryGetValue(reason, out int count); fallbackReasons[reason] = count + 1; continue; }
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    var e = new Entry { renderer = r, mesh = mesh, matrix = r.localToWorldMatrix, previousForceOff = r.forceRenderingOff };
                    e.bounds = MGSurfaceMath.TransformBounds(mesh.bounds, e.matrix);
                    e.propIndex = registeredProps++;
                    var geometry = surfaceLibrary ? surfaceLibrary.geometry : null;
                    MGSurfaceGeometry.Shape shape = null;
                    var materials = r.sharedMaterials;
                    bool morph = geometry && geometry.IsValid && geometry.TryGet(mesh, out shape) && shape.parts.Length == materials.Length && materials.All(m => surfaceLibrary.TryResolve(m, out _, out _));
                    e.groups = new int[materials.Length]; e.surfaceIDs = new float[materials.Length];
                    for (int part = 0; !morph && part < materials.Length; part++)
                    {
                        Material renderMaterial = materials[part];
                        if (surfaceLibrary && surfaceLibrary.TryResolve(materials[part], out var shared, out float surfaceID))
                        { renderMaterial = shared; e.surfaceIDs[part] = surfaceID; }
                        var key = new GroupKey { mesh = mesh, material = renderMaterial, submesh = part, layer = r.gameObject.layer,
                            renderingLayers = r.renderingLayerMask, shadows = r.shadowCastingMode, receive = r.receiveShadows,
                            flipped = e.matrix.determinant < 0, motion = r.motionVectorGenerationMode };
                        int index = Register(key);
                        e.groups[part] = index;
                    }
                    var source = r.GetComponentInParent<LODGroup>();
                    if (source && source.enabled)
                    {
                        var levels = source.GetLODs();
                        for (int level = 0; level < levels.Length; level++)
                            if (Array.IndexOf(levels[level].renderers, r) >= 0) { e.lod = level; break; }
                        if (e.lod >= 0)
                        {
                            if (!lodMap.TryGetValue(source, out int index))
                            {
                                index = lods.Count; lodMap.Add(source, index);
                                var thresholds = new float[levels.Length]; for (int i = 0; i < levels.Length; i++) thresholds[i] = levels[i].screenRelativeTransitionHeight;
                                lods.Add(new LodData { source = source, thresholds = thresholds, family = LodFamily(levels) });
                            }
                            e.lodSet = index;
                        }
                    }
                    if (morph)
                    {
                        for (int part = 0; part < materials.Length; part++)
                        {
                            surfaceLibrary.TryResolve(materials[part], out var shared, out float surfaceID);
                            var key = new GroupKey { mesh = geometry.carrier, material = shared, submesh = 0, layer = r.gameObject.layer,
                                renderingLayers = r.renderingLayerMask, shadows = r.shadowCastingMode, receive = r.receiveShadows,
                                flipped = e.matrix.determinant < 0, motion = r.motionVectorGenerationMode };
                            int group = Register(key);
                            int firstPiece = entries.Count;
                            foreach (int id in shape.parts[part].chunks)
                                entries.Add(new Entry { renderer = r, mesh = mesh, matrix = e.matrix, bounds = e.bounds, previousForceOff = e.previousForceOff,
                                    groups = new[] { group }, surfaceIDs = new[] { surfaceID }, geometryID = id, propIndex = e.propIndex, lod = e.lod, lodSet = e.lodSet, drawCount = 0 });
                            if (entries.Count > firstPiece) entries[firstPiece].drawCount = entries.Count - firstPiece;
                        }
                    }
                    else entries.Add(e);
                }
                if (entries.Count == 0) { brg.Dispose(); brg = null; return; }
                for (int i = 0; i < entries.Count; i++) if (entries[i].drawCount > 0) drawEntries.Add(i);
                UpdateLods(); BuildCells(); CreateBuffer();
                // Only hide originals after the entire registry and buffer were successfully built.
                foreach (var e in entries) e.renderer.forceRenderingOff = true;
            }
            catch (Exception ex) { Release(); LastError = ex.Message; Debug.LogException(ex, this); }
        }

        static string LodFamily(LOD[] levels)
        {
            // Stable within this registry: different textures may share a geometry LOD family.
            var key = new System.Text.StringBuilder();
            foreach (var level in levels)
            {
                key.Append(level.screenRelativeTransitionHeight.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':');
                var meshes = new List<int>();
                foreach (var renderer in level.renderers)
                {
                    var filter = renderer ? renderer.GetComponent<MeshFilter>() : null;
                    meshes.Add(filter && filter.sharedMesh ? filter.sharedMesh.GetInstanceID() : 0);
                }
                meshes.Sort();
                foreach (int mesh in meshes) key.Append(mesh).Append(',');
                key.Append(';');
            }
            return key.ToString();
        }
        void UpdateLods()
        {
            foreach (var lod in lods)
            {
                if (!lod.source) continue;
                var t = lod.source.transform; lod.center = t.TransformPoint(lod.source.localReferencePoint);
                var scale = t.lossyScale; lod.size = lod.source.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            }
        }
        void BuildCells()
        {
#if UNITY_EDITOR
            cellDebugViews.Clear(); CellBoundsVersion++;
#endif
            cells.Clear(); debugBounds.Clear(); cellLods.Clear(); var map = new Dictionary<Vector2Int, CellData>();
            foreach (var lod in lods) lod.cellLod = -1;
            int instanceIndex = 0;
            foreach (int gpuIndex in drawEntries)
            {
                var e = entries[gpuIndex];
                // Every level of a prop belongs to its LODGroup's cell, even with offset child pivots.
                var lod = e.lodSet >= 0 ? lods[e.lodSet] : null;
                var key = MGSurfaceMath.Cell(lod != null ? lod.center : (Vector3)e.matrix.GetColumn(3), cellSize);
                if (!map.TryGetValue(key, out var cell)) { cell = new CellData { bounds = e.bounds }; map.Add(key, cell); cells.Add(cell); }
                else cell.bounds.Encapsulate(e.bounds);
                cell.instances.Add(instanceIndex++);
                if (lod != null)
                {
                    if (!cell.lodFamilies.TryGetValue(lod.family, out int family))
                    {
                        family = cellLods.Count; cell.lodFamilies.Add(lod.family, family);
                        cellLods.Add(new CellLodData { bounds = e.bounds, size = lod.size, thresholds = lod.thresholds });
                    }
                    var shared = cellLods[family]; shared.bounds.Encapsulate(e.bounds); shared.size = Mathf.Max(shared.size, lod.size);
                    lod.cellLod = family;
                }
            }
            var total = cells[0].bounds;
            foreach (var cell in cells) { debugBounds.Add(cell.bounds); total.Encapsulate(cell.bounds); }
            brg.SetGlobalBounds(total);
            BuildCullingData();
        }
        void UpdateTransforms()
        {
            // Only moving prefab roots are polled by MGSurfaceProp; static scene transforms are never scanned each frame.
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i]; if (!e.renderer) { dirty = true; continue; }
                foreach (var root in dirtyTransforms)
                {
                    if (!root || (e.renderer.transform != root && !e.renderer.transform.IsChildOf(root))) continue;
                    var matrix = e.renderer.localToWorldMatrix;
                    if (Mathf.Abs(matrix.determinant) < 1e-10f || (matrix.determinant < 0) != (e.matrix.determinant < 0)) { dirty = true; break; }
                    e.matrix = matrix; e.bounds = MGSurfaceMath.TransformBounds(e.mesh.bounds, matrix); UploadMatrix(i, matrix); break;
                }
            }
            dirtyTransforms.Clear(); UpdateLods(); BuildCells(); UpdateLighting();
        }
        public void Release()
        {
            ReleaseCullingData();
            foreach (var e in entries) if (e.renderer) e.renderer.forceRenderingOff = e.previousForceOff;
            brg?.Dispose(); brg = null; buffer?.Dispose(); buffer = null;
            foreach (var m in ownedMaterials) if (m) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
            ownedMaterials.Clear(); materialIDs.Clear(); meshIDs.Clear(); entries.Clear(); drawEntries.Clear(); cells.Clear(); debugBounds.Clear(); groups.Clear(); lods.Clear(); cellLods.Clear();
            fallbackReasons.Clear(); FallbackCount = 0; InstanceBufferBytes = 0; dirtyTransforms.Clear();
            registeredProps = 0;
            LastVisibleInstances = LastVisibleCells = LastDrawCommands = LastLodEvaluations = 0;
#if UNITY_EDITOR
            cellDebugViews.Clear(); CellBoundsVersion++;
#endif
        }

    }
}
