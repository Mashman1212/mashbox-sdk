#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        // Explicit editor bake only. Never constructed by rendering, OnEnable or import hooks.
        public sealed class SurfaceLodBake : IDisposable
        {
            readonly Mesh source;
            readonly MGTerrainSurfaceLodBuilder.Input input;
            readonly CancellationTokenSource cancellation = new CancellationTokenSource();
            Task<MGTerrainSurfaceLodBuilder.Level[]> task;
            MGTerrainSurfaceLodBuilder.Level[] levels;
            int snapshotStep;
            public SurfaceLodBake(MGTerrain terrain)
            {
                source = terrain.MeshFilter.sharedMesh;
                input = new MGTerrainSurfaceLodBuilder.Input { width = terrain.m_SurfaceGridWidth, height = terrain.m_SurfaceGridHeight,
                    uv = new Vector4[8][], triangles = new int[source.subMeshCount][] };
                if (input.width < 3 || input.height < 3 || !source.isReadable) throw new InvalidOperationException("Bake requires a readable terrain grid.");
            }
            // One vertex stream per editor update instead of a large synchronous snapshot.
            public bool SnapshotNext()
            {
                int step = snapshotStep++;
                if (step == 0) input.vertices = source.vertices;
                else if (step == 1) input.normals = source.normals;
                else if (step == 2) input.tangents = source.tangents;
                else if (step == 3) input.colors = source.colors;
                else if (step < 12) { var values = new List<Vector4>(); source.GetUVs(step - 4, values); input.uv[step - 4] = values.ToArray(); }
                else input.triangles[step - 12] = source.GetTriangles(step - 12);
                if (snapshotStep < 12 + input.triangles.Length) return false;
                var token = cancellation.Token;
                task = Task.Run(() => MGTerrainSurfaceLodBuilder.Build(input, token), token);
                return true;
            }
            public bool Complete => task != null && task.IsCompleted;
            public int LevelCount { get { levels ??= task.GetAwaiter().GetResult(); return levels.Length; } }
            public MGTerrainSurfaceLodAsset.Level CreateLevel(int index)
            {
                var level = levels[index];
                var mesh = new Mesh { name = source.name + " HLOD " + (index + 1),
                    indexFormat = level.sourceIndices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.vertices = Gather(input.vertices, level.sourceIndices);
                mesh.normals = Gather(input.normals, level.sourceIndices);
                mesh.tangents = Gather(input.tangents, level.sourceIndices);
                mesh.colors = Gather(input.colors, level.sourceIndices);
                for (int i = 0; i < 8; i++) if (input.uv[i].Length != 0) mesh.SetUVs(i, Gather(input.uv[i], level.sourceIndices));
                mesh.subMeshCount = level.triangles.Length;
                for (int i = 0; i < level.triangles.Length; i++) mesh.SetTriangles(level.triangles[i], i, false);
                mesh.bounds = source.bounds;
                return new MGTerrainSurfaceLodAsset.Level { Mesh = mesh, Error = level.error, TriangleCount = level.triangleCount };
            }
            public void Dispose()
            {
                cancellation.Cancel(); cancellation.Dispose();
                // Observe faulted workers even when the user cancels or closes the scene.
                if (task != null) _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }
}
#endif
