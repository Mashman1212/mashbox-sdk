using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.Painting;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.Spline;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public static class LoftShoulderSeamValidation
    {
        static int checks;

        [MenuItem("MashBox/Validation/Validate Loft Shoulder Seams")]
        public static void Run()
        {
            try
            {
                checks = 0;
                foreach (MultiSplineLoft.NormalMode mode in new[] {
                    MultiSplineLoft.NormalMode.Recalculate, MultiSplineLoft.NormalMode.SmoothedFaces,
                    MultiSplineLoft.NormalMode.LoftGrid })
                    foreach (bool packed in new[] { false, true })
                        Validate(mode, packed);
                Debug.Log("LOFT_SHOULDER_SEAM_PASS: " + checks + " checks");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }

        static void Validate(MultiSplineLoft.NormalMode mode, bool packed)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Shoulder seam validation");
            SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                var loft = root.AddComponent<MultiSplineLoft>();
                loft.AutoRegenerate = false;
                loft.UpdateMeshCollider = false;
                loft.SurfaceNormalMode = mode;
                loft.GeneratePackedUv2 = packed;
                loft.TargetSegmentLength = 2f;
                for (int side = 0; side < 2; side++)
                {
                    var curve = new GameObject("Curve " + side);
                    curve.transform.SetParent(root.transform, false);
                    var container = curve.AddComponent<SplineContainer>();
                    container.Spline = new UnityEngine.Splines.Spline(new[] {
                        new BezierKnot(new float3(side * 4, side * 0.6f, 0)),
                        new BezierKnot(new float3(side * 4, side * 0.6f + 1, 40)),
                        new BezierKnot(new float3(side * 4, side * 0.6f, 80)) });
                    loft.Sources.Add(new MultiSplineLoft.SplineSource { container = container });
                }
                var shoulders = root.AddComponent<LoftShoulderModifier>();
                shoulders.Loft = loft;
                loft.ShoulderModifier = shoulders;
                foreach (LoftShoulderModifier.ShoulderProfile profile in new[] {
                    shoulders.Left, shoulders.Right, shoulders.Start, shoulders.Finish })
                {
                    profile.enabled = true;
                    profile.segments = 4;
                    profile.generateLayerMasks = true;
                    profile.erosionAmplitude = 0.2f;
                    profile.horizontalErosionAmplitude = 0.1f;
                    // A nonzero first profile key must not detach the attachment row.
                    profile.height = AnimationCurve.Linear(0, 0.15f, 1, -1f);
                }
                loft.Regenerate();
                int vertexCount = loft.GeneratedMesh.vertexCount;
                CheckSeams(loft);
                if (packed)
                    Check(loft.GeneratedMesh.uv3.Select(uv => uv.y).Distinct().Count() > 4,
                        "Packed UV case must exercise atlas islands.");

                var paint = root.AddComponent<VertexPaintModifier>();
                paint.LinkToLoft(loft);
                paint.Strokes.Add(new VertexPaintModifier.Stroke {
                    fill = true, color = new Color(0.2f, 0.5f, 0.1f, 1), strength = 1 });
                var sculpt = root.AddComponent<MeshSculptModifier>();
                sculpt.LinkToLoft(loft);
                loft.Regenerate(); // Includes sculpt's RecalculateNormals and the paint pass.
                Check(loft.GeneratedMesh.vertexCount == vertexCount, "Regeneration changed saved vertex numbering.");
                CheckSeams(loft);
                Check(loft.GeneratedMesh.colors[loft.GeneratedMesh.GetTriangles(0)[0]].g > 0.4f,
                    "Paint pass did not execute.");

                Mesh mesh = loft.GeneratedMesh;
                var sourceVertices = mesh.vertices;
                var sourceNormals = mesh.normals;
                var sourceUvs = mesh.uv;
                var sourceColors = mesh.colors;
                var distances = sourceVertices.Select(v => v.z).ToArray();
                var chunks = LoftVisualMeshBuilder.Split(mesh, distances, 20f);
                try
                {
                    Check(chunks.Count > 1, "Visual chunk case must cross a chunk boundary.");
                    Check(chunks.Sum(m => m.triangles.Length) == mesh.triangles.Length,
                        "Welded shoulder triangles were lost when chunking.");
                    foreach (Mesh chunk in chunks)
                    {
                        var verts = chunk.vertices;
                        var normals = chunk.normals;
                        var uvs = chunk.uv;
                        var colors = chunk.colors;
                        for (int i = 0; i < verts.Length; i++)
                            Check(Enumerable.Range(0, mesh.vertexCount).Any(j =>
                                sourceVertices[j].Equals(verts[i]) && sourceNormals[j].Equals(normals[i]) &&
                                sourceUvs[j].Equals(uvs[i]) && sourceColors[j].Equals(colors[i])),
                                "Visual chunk changed seam attributes.");
                    }
                }
                finally { foreach (Mesh chunk in chunks) Object.DestroyImmediate(chunk); }
                Debug.Log("Shoulder seam case passed: " + mode + ", packed=" + packed);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static HashSet<long> Edges(int[] triangles)
        {
            var edges = new HashSet<long>();
            for (int i = 0; i < triangles.Length; i += 3)
                for (int corner = 0; corner < 3; corner++)
                {
                    int a = triangles[i + corner], b = triangles[i + (corner + 1) % 3];
                    edges.Add(((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b));
                }
            return edges;
        }

        static void CheckSeams(MultiSplineLoft loft)
        {
            Mesh mesh = loft.GeneratedMesh;
            Check(mesh.subMeshCount == 5, "Material submeshes must be preserved.");
            var baseEdges = Edges(mesh.GetTriangles(0));
            for (int submesh = 1; submesh < mesh.subMeshCount; submesh++)
            {
                var shared = Edges(mesh.GetTriangles(submesh));
                shared.IntersectWith(baseEdges);
                int expected = submesh <= 2 ? loft.CurrentSamplesAlong - 1 : loft.CurrentSamplesAcross - 1;
                Check(shared.Count == expected,
                    "Shoulder " + submesh + " shares " + shared.Count + "/" + expected + " loft edges.");
                foreach (long edge in shared)
                {
                    int a = (int)(edge >> 32), b = (int)edge;
                    Check(mesh.normals[a].sqrMagnitude > 0.9f && mesh.normals[b].sqrMagnitude > 0.9f,
                        "Shared displacement normals must remain valid after regeneration.");
                    Check(mesh.tangents[a].sqrMagnitude > 0.9f, "Shared tangent is invalid.");
                    if (loft.GeneratePackedUv2 && submesh <= 2)
                        Check(Mathf.Abs(mesh.uv3[a].y - mesh.uv3[b].y) < 0.3f,
                            "A shoulder seam connected different packed UV islands.");
                }
            }
        }
    }
}
