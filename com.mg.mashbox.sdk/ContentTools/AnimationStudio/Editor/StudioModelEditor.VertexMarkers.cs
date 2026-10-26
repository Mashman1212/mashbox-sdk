using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private GraphicsBuffer markerPositions, markerSelection;
        private Material markerMaterial;
        private int[] markerSelectionIds = Array.Empty<int>();
        private int markerSelectionCount;
        private static readonly int MarkerPositions = Shader.PropertyToID("_MarkerPositions"),
            MarkerIds = Shader.PropertyToID("_MarkerIds"), MarkerMatrix = Shader.PropertyToID("_MarkerObjectToWorld"),
            MarkerViewport = Shader.PropertyToID("_MarkerViewport"), MarkerColor = Shader.PropertyToID("_MarkerColor"),
            MarkerSize = Shader.PropertyToID("_MarkerSize"), MarkerMode = Shader.PropertyToID("_MarkerMode"),
            MarkerHover = Shader.PropertyToID("_MarkerHover");
#if MASHBOX_STUDIO_TESTS
        internal bool ForceLegacyVertexMarkers;
        internal long VertexMarkerUploadBytes;
        internal int VertexMarkerSelectionUploads;
#endif
        private bool UseGpuVertexMarkers
        {
            get
            {
#if MASHBOX_STUDIO_TESTS
                if (ForceLegacyVertexMarkers) return false;
#endif
                if (SystemInfo.graphicsShaderLevel < 45) return false;
                if (!markerMaterial)
                {
                    var shader = Shader.Find("Hidden/MashBox/ModelerVertexMarkers");
                    if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) return false;
                    markerMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                return true;
            }
        }
        private void ReleaseVertexMarkers()
        {
            markerPositions?.Dispose(); markerSelection?.Dispose();
            markerPositions = markerSelection = null;
            markerSelectionIds = Array.Empty<int>(); markerSelectionCount = 0;
        }
        private void EnsureVertexMarkers()
        {
            if (markerPositions != null || componentPositions.Length == 0) return;
            markerPositions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, componentPositions.Length, 12);
            markerPositions.SetData(componentPositions);
            markerSelection = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            markerSelectionIds = new int[1];
#if MASHBOX_STUDIO_TESTS
            VertexMarkerUploadBytes += componentPositions.Length * 12L;
#endif
        }
        private void UpdateVertexMarkerSelection()
        {
            int count = 0;
            foreach (int point in Components)
                if (point >= 0 && point < componentPositions.Length) count++;
            if (count > markerSelectionIds.Length)
            {
                markerSelectionIds = new int[Mathf.NextPowerOfTwo(count)];
                markerSelection?.Dispose();
                markerSelection = new GraphicsBuffer(GraphicsBuffer.Target.Structured, markerSelectionIds.Length, 4);
            }
            markerSelectionCount = 0;
            foreach (int point in Components)
                if (point >= 0 && point < componentPositions.Length) markerSelectionIds[markerSelectionCount++] = point;
            if (count == 0 || markerSelection == null) return;
            markerSelection.SetData(markerSelectionIds, 0, 0, count);
#if MASHBOX_STUDIO_TESTS
            VertexMarkerSelectionUploads++;
#endif
        }
        private void UploadMarkerRange(int first, int count)
        {
            if (markerPositions == null || count <= 0) return;
            markerPositions.SetData(componentPositions, first, first, count);
#if MASHBOX_STUDIO_TESTS
            VertexMarkerUploadBytes += count * 12L;
#endif
        }
        // Explicit indices are sorted once when a Move gesture is captured. Merge
        // nearby runs and bound submission count for fragmented large selections.
        private void UploadChangedMarkerPoints(int[] points)
        {
            if (markerPositions == null || points.Length == 0) return;
            int first = points[0], last = first, uploads = 0;
            for (int i = 1; i < points.Length; i++)
            {
                int next = points[i];
                if (next - last > 16 && uploads < 63)
                { UploadMarkerRange(first, last - first + 1); first = next; uploads++; }
                last = next;
            }
            UploadMarkerRange(first, last - first + 1);
        }
        private void DrawVertexMarkers(Camera camera, Matrix4x4 matrix)
        {
            if (markerPositions == null) return;
            markerMaterial.SetBuffer(MarkerPositions, markerPositions);
            markerMaterial.SetBuffer(MarkerIds, markerSelection);
            markerMaterial.SetMatrix(MarkerMatrix, matrix);
            markerMaterial.SetVector(MarkerViewport, new Vector4(camera.pixelWidth, camera.pixelHeight, 0, 0));
            markerMaterial.SetFloat(OverlayDepth, (float)(displayXray ? CompareFunction.Always : CompareFunction.LessEqual));
            void Draw(int count, int mode, Color color, float size)
            {
                if (count <= 0) return;
                markerMaterial.SetInt(MarkerMode, mode); markerMaterial.SetColor(MarkerColor, color);
                markerMaterial.SetFloat(MarkerSize, size * EditorGUIUtility.pixelsPerPoint);
                if (!markerMaterial.SetPass(0)) return;
                Graphics.DrawProceduralNow(MeshTopology.Triangles, 6, count);
#if MASHBOX_STUDIO_TESTS
                ComponentOverlayDrawCalls++;
#endif
            }
            Draw(componentPositions.Length, 0, VertexMarkerColor, VertexMarkerSize);
            markerMaterial.SetInt(MarkerHover, drawnHover);
            if (drawnHover >= 0) Draw(1, 2, new Color(1, .95f, .5f, displayXray ? 1 : .8f), 10);
            Draw(markerSelectionCount, 1, new Color(1, .65f, .15f, displayXray ? 1 : .9f), SelectedVertexMarkerSize);
        }
    }
}
