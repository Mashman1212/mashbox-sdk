using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    /// <summary>Bounded, world-aligned GPU interaction field shared by every terrain tile.</summary>
    [ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(10000)]
    [AddComponentMenu("MashBox/Maps/MG Grass Interaction Map")]
    public sealed class MGGrassInteractionMap : MonoBehaviour
    {
        [Range(64, 2048)] public int resolution = 512;
        [Min(4)] public float worldSize = 64;
        [Range(1, 120)] public float updateRate = 30;
        [Min(0.05f), Tooltip("Seconds for a full-strength impression to recover completely.")]
        public float recoverySeconds = 4;
        [Range(0, 85)] public float maximumBendAngle = 75;
        [Range(0, 0.8f)] public float compression = 0.15f;
        [Min(0.05f), Tooltip("Maximum root/contact height difference. Fade begins at half this distance.")]
        public float heightTolerance = 1.5f;
        [Min(0.01f)] public float edgeFadeMetres = 2;
        [Tooltip("Opt in to painting while editing. Off by default.")]
        public bool previewInEditMode;
        [SerializeField] ComputeShader paintShader;

        /// <summary>Optional source-time clock for frame-stepped capture. Restore after capture.
        /// Repeated timestamps freeze recovery and stamping; advancing timestamps update once,
        /// independent of render waits and the normal update-rate throttle.</summary>
        public static System.Func<double> SimulationTimeProvider { get; set; }
        System.Func<double> previousTimeProvider;
        double previousSimulationTime;

        public static MGGrassInteractionMap Active { get; private set; }
        public RenderTexture InteractionTexture => current;
        public Vector2 WorldMinimum => minimum;
        public float MetresPerTexel => allocatedSize / Mathf.Max(1, allocatedResolution);
        public int LastStampCount { get; private set; }
        public long LastUpdatedTexels { get; private set; }

        static readonly int ActiveRectId = Shader.PropertyToID("_MGGrassInteractionActiveRect");
        RectInt activeRect; // Conservative footprint of all retained stamps, in texture pixels.
        double nextEditorTick;
        static readonly int MapId = Shader.PropertyToID("_MGGrassInteractionMap");
        static readonly int RectId = Shader.PropertyToID("_MGGrassInteractionRect");
        static readonly int ParamsId = Shader.PropertyToID("_MGGrassInteractionParams");
        static readonly int HeightId = Shader.PropertyToID("_MGGrassInteractionHeightOrigin");
        static readonly int EdgeId = Shader.PropertyToID("_MGGrassInteractionEdgeFade");
        RenderTexture current, scratch;
        ComputeShader painter;
        Vector2 minimum;
        float allocatedSize, heightOrigin, elapsed;
        int allocatedResolution, scrollKernel, stampKernel, recoverKernel;
        double previousTime;
        bool clearRequested = true, failed;
        // Reused arrays avoid params-array allocation on every dispatch.
        readonly int[] scroll = new int[4], dispatchRect = new int[4];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGlobals() { SimulationTimeProvider = null; Active = null; Shader.SetGlobalVector(ParamsId, Vector4.zero); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ResetLoadedMaps()
        {
            // Enter Play Mode with both domain and scene reload disabled.
            foreach (var map in Object.FindObjectsByType<MGGrassInteractionMap>(FindObjectsSortMode.None))
                if (map.isActiveAndEnabled) { map.elapsed = 0; map.OnEnable(); }
        }

        void OnEnable()
        {
            previousTime = Time.realtimeSinceStartupAsDouble; failed = false; clearRequested = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
            UnityEditor.EditorApplication.update += EditorTick;
#endif
        }
        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            StopMap();
        }
#if UNITY_EDITOR
        void EditorTick()
        {
            if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || !isActiveAndEnabled) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextEditorTick) return;
            nextEditorTick = now + 1.0 / Mathf.Clamp(updateRate, 1, 120);
            // Paint directly: queuing the player loop also ticks every ExecuteAlways scene component.
            Tick();
            if (Active == this && previewInEditMode) UnityEditor.SceneView.RepaintAll();
        }
#endif
        void StopMap()
        {
            if (Active == this)
            {
                Active = null;
                Shader.SetGlobalVector(ParamsId, Vector4.zero);
                Shader.SetGlobalTexture(MapId, Texture2D.blackTexture);
            }
            Release();
        }
        void OnValidate() { failed = false; }
        [ContextMenu("Clear Interaction Map")]
        public void ClearMap()
        {
            clearRequested = true;
            foreach (var brush in MGGrassInteractor.Instances) if (brush != null) brush.ResetStroke();
        }

        bool Eligible()
        {
            if (!Application.isPlaying && !previewInEditMode) return false;
#if UNITY_EDITOR
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene)) return false;
#endif
            return true;
        }
        void LateUpdate()
        {
            if (Application.isPlaying) Tick();
        }
        void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float delta = Application.isPlaying ? Time.deltaTime : (float)(now - previousTime);
            previousTime = now;
            if (!Eligible() || Camera.main == null) { if (Active == this || current != null) StopMap(); return; }
            if (Active != null && Active != this) return;
            if (failed) return;
            Active = this;
            if (!EnsureResources()) return;
            var timeProvider = SimulationTimeProvider;
            if (timeProvider != null)
            {
                double simulationTime = timeProvider();
                bool changedClock = !ReferenceEquals(previousTimeProvider, timeProvider);
                delta = changedClock ? 0f : (float)System.Math.Max(0, simulationTime - previousSimulationTime);
                if (changedClock) elapsed = 0;
                else if (simulationTime < previousSimulationTime) ClearMap();
                previousSimulationTime = simulationTime;
                previousTimeProvider = timeProvider;
                // Do not repeatedly stamp a stationary pose during capture/IO waits.
                if (!clearRequested && delta <= 0f) { Publish(); return; }
            }
            else if (previousTimeProvider != null)
            {
                previousTimeProvider = null;
                elapsed = 0;
                delta = 0;
            }
            elapsed += Mathf.Max(0, delta);
            if (timeProvider == null && !clearRequested && elapsed < 1f / Mathf.Clamp(updateRate, 1, 120)) { Publish(); return; }
            Profiler.BeginSample("MG Grass Interaction");
            try { UpdateMap(elapsed); }
            finally { Profiler.EndSample(); }
            elapsed = 0;
            Publish();
        }

        bool EnsureResources()
        {
            int size = Mathf.ClosestPowerOfTwo(Mathf.Clamp(resolution, 64, 2048));
            float metres = Mathf.Max(4, worldSize);
            if (current != null && current.IsCreated() && scratch != null && scratch.IsCreated()
                && size == allocatedResolution && Mathf.Approximately(metres, allocatedSize)) return true;
            Release();
            if (!SystemInfo.supportsComputeShaders
                || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                return Fail("Compute shaders and writable ARGBHalf textures are required.");
            var source = paintShader != null ? paintShader : Resources.Load<ComputeShader>("MGGrassInteraction");
            if (source == null) return Fail("MGGrassInteraction.compute is missing.");
            painter = Instantiate(source);
            painter.hideFlags = HideFlags.HideAndDontSave;
            scrollKernel = painter.FindKernel("ScrollRecover");
            stampKernel = painter.FindKernel("Stamp");
            recoverKernel = painter.FindKernel("RecoverRegion");
            allocatedResolution = size;
            allocatedSize = metres;
            current = CreateTexture(size, "MG Grass Interaction");
            scratch = CreateTexture(size, "MG Grass Interaction Scratch");
            if (!current.IsCreated() || !scratch.IsCreated()) return Fail("Could not allocate interaction textures.");
            clearRequested = true;
            return true;
        }
        bool Fail(string reason)
        {
            Debug.LogWarning("MG Grass Interaction: " + reason, this);
            failed = true;
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            Release();
            if (Active == this) Active = null;
            return false;
        }
        static RenderTexture CreateTexture(int size, string label)
        {
            var texture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = label, enableRandomWrite = true, useMipMap = false, autoGenerateMips = false,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            return texture;
        }
        void Release()
        {
            if (current != null) { current.Release(); DisposeObject(current); }
            if (scratch != null) { scratch.Release(); DisposeObject(scratch); }
            if (painter != null) DisposeObject(painter);
            current = scratch = null;
            painter = null;
        }
        static void DisposeObject(Object value)
        { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }

        void UpdateMap(float delta)
        {
            float texel = MetresPerTexel;
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            Vector3 focus = mainCamera.transform.position;
            Vector2 next = new Vector2(Mathf.Floor(focus.x / texel) * texel - allocatedSize * 0.5f,
                Mathf.Floor(focus.z / texel) * texel - allocatedSize * 0.5f);
            scroll[0] = Mathf.Clamp(Mathf.RoundToInt((next.x - minimum.x) / texel), -allocatedResolution, allocatedResolution);
            scroll[1] = Mathf.Clamp(Mathf.RoundToInt((next.y - minimum.y) / texel), -allocatedResolution, allocatedResolution);
            scroll[2] = clearRequested ? 1 : 0;
            if (clearRequested) activeRect = default;
            else activeRect = ShiftFootprint(activeRect, scroll[0], scroll[1], allocatedResolution);
            if (clearRequested) { heightOrigin = focus.y; foreach (var brush in MGGrassInteractor.Instances) if (brush != null) brush.ResetStroke(); }
            painter.SetInt("_Resolution", allocatedResolution);
            painter.SetInts("_Scroll", scroll);
            painter.SetFloat("_Recovery", delta / Mathf.Max(0.05f, recoverySeconds));
            LastUpdatedTexels = 0;
            if (clearRequested || scroll[0] != 0 || scroll[1] != 0)
            {
                painter.SetTexture(scrollKernel, "_Source", current);
                painter.SetTexture(scrollKernel, "_Map", scratch);
                painter.Dispatch(scrollKernel, (allocatedResolution + 7) / 8, (allocatedResolution + 7) / 8, 1);
                var swap = current; current = scratch; scratch = swap;
                LastUpdatedTexels = (long)allocatedResolution * allocatedResolution;
            }
            else if (activeRect.width > 0 && activeRect.height > 0)
            {
                // No scroll: recover only retained stamp bounds, in place, with one writer per texel.
                SetDispatchRectangle(activeRect);
                painter.SetTexture(recoverKernel, "_Map", current);
                painter.Dispatch(recoverKernel, (activeRect.width + 7) / 8, (activeRect.height + 7) / 8, 1);
                LastUpdatedTexels = (long)activeRect.width * activeRect.height;
            }
            minimum = next;
            clearRequested = false;
            painter.SetVector("_MapWorld", new Vector4(minimum.x, minimum.y, texel, heightOrigin));
            painter.SetTexture(stampKernel, "_Map", current);
            LastStampCount = 0;
            foreach (var brush in MGGrassInteractor.Instances)
            {
                if (brush == null || !brush.GetStroke(this, out var from, out var to, out float radius, out var heading, out float blend)) continue;
                RectInt rect = BrushRectangle(from, to, Mathf.Max(radius, texel), minimum, texel, allocatedResolution);
                if (rect.width == 0 || rect.height == 0) continue;
                activeRect = UnionFootprint(activeRect, rect);
                SetDispatchRectangle(rect);
                painter.SetVector("_BrushFrom", new Vector4(from.x, from.y, from.z, radius));
                painter.SetVector("_BrushTo", new Vector4(to.x, to.y, to.z, Mathf.Clamp01(brush.strength)));
                painter.SetVector("_BrushDirection", new Vector4(heading.x, heading.y, blend, Mathf.Max(0.05f, heightTolerance)));
                painter.Dispatch(stampKernel, (rect.width + 7) / 8, (rect.height + 7) / 8, 1);
                LastStampCount++;
                LastUpdatedTexels += (long)rect.width * rect.height;
            }
        }

        void SetDispatchRectangle(RectInt rect)
        {
            dispatchRect[0] = rect.x; dispatchRect[1] = rect.y; dispatchRect[2] = rect.width; dispatchRect[3] = rect.height;
            painter.SetInts("_DispatchRect", dispatchRect);
        }
        public static RectInt ShiftFootprint(RectInt rect, int x, int y, int size)
        {
            if (rect.width <= 0 || rect.height <= 0) return default;
            int x0 = Mathf.Clamp(rect.xMin - x, 0, size), y0 = Mathf.Clamp(rect.yMin - y, 0, size);
            int x1 = Mathf.Clamp(rect.xMax - x, 0, size), y1 = Mathf.Clamp(rect.yMax - y, 0, size);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }
        public static RectInt UnionFootprint(RectInt a, RectInt b)
        {
            if (a.width <= 0 || a.height <= 0) return b;
            return new RectInt(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax) - Mathf.Min(a.xMin, b.xMin),
                Mathf.Max(a.yMax, b.yMax) - Mathf.Min(a.yMin, b.yMin));
        }

        public static RectInt BrushRectangle(Vector3 from, Vector3 to, float radius, Vector2 origin, float texel, int size)
        {
            texel = Mathf.Max(0.0001f, texel);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(from.x, to.x) - radius - origin.x) / texel), 0, size);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(from.z, to.z) - radius - origin.y) / texel), 0, size);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(from.x, to.x) + radius - origin.x) / texel), 0, size);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(from.z, to.z) + radius - origin.y) / texel), 0, size);
            return new RectInt(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
        }
        void Publish()
        {
            Shader.SetGlobalTexture(MapId, current);
            Shader.SetGlobalVector(RectId, new Vector4(minimum.x, minimum.y, 1f / allocatedSize, MetresPerTexel));
            // Expand by one texel to retain bilinear filtering across footprint edges.
            float texel = MetresPerTexel;
            Shader.SetGlobalVector(ActiveRectId, new Vector4(minimum.x + (activeRect.xMin - 1) * texel,
                minimum.y + (activeRect.yMin - 1) * texel, minimum.x + (activeRect.xMax + 1) * texel,
                minimum.y + (activeRect.yMax + 1) * texel));
            Shader.SetGlobalVector(ParamsId, new Vector4(activeRect.width > 0 && activeRect.height > 0 ? 1 : 0, Mathf.Clamp(maximumBendAngle, 0, 85) * Mathf.Deg2Rad,
                Mathf.Clamp(compression, 0, 0.8f), Mathf.Max(0.05f, heightTolerance)));
            Shader.SetGlobalFloat(HeightId, heightOrigin);
            Shader.SetGlobalFloat(EdgeId, Mathf.Max(MetresPerTexel * 2, edgeFadeMetres));
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            Vector3 focus = mainCamera.transform.position;
            Gizmos.DrawWireCube(new Vector3(focus.x, focus.y, focus.z), new Vector3(Mathf.Max(4, worldSize), 0, Mathf.Max(4, worldSize)));
        }
    }
}
