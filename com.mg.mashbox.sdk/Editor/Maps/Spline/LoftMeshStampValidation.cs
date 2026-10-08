using System;
using MashBoxSDK.Maps.Spline;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Spline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.MapTools
{
    public static class LoftMeshStampValidation
    {
        [MenuItem("MashBox/Validation/Validate Loft Mesh Stamp")]
        public static void Run()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Loft stamp validation") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var mesh = new Mesh();
            try
            {
                CheckSplit(false, TangentMode.Broken);
                CheckSplit(false, TangentMode.AutoSmooth);
                CheckSplit(true, TangentMode.AutoSmooth);
                mesh.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 1, -1),
                    new Vector3(-1, 0, 1), new Vector3(1, 1, 1) };
                mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                mesh.RecalculateBounds();
                CheckRotation(mesh, 30, 0, 0);
                CheckRotation(mesh, 0, 0, -25);
                CheckRotation(mesh, 35, 55, -20);
                CheckRotation(mesh, 125, -40, 160);
                CheckTiltedFootprint(mesh, scene);
                var settings = new LoftMeshStamp.Settings { width = 4, length = 4, height = 2, resolution = 16, blend = .25f };
                var sampler = new LoftMeshStamp.Sampler(mesh, settings);
                Check(Mathf.Abs(sampler.Delta(Vector3.zero) - 1) < .0001f, "Mesh normalization");
                Check(sampler.Delta(new Vector3(3, 0, 0)) == 0, "Footprint exclusion");
                Check(sampler.Delta(new Vector3(1.99f, 0, 0)) < .01f, "Edge blending");
                settings.mode = LoftMeshStamp.Mode.Carve;
                Check(Mathf.Abs(new LoftMeshStamp.Sampler(mesh, settings).Delta(Vector3.zero) + 1) < .0001f, "Carve sign");
                settings.mode = LoftMeshStamp.Mode.FitToMesh;
                settings.position = new Vector3(0, 5, 0);
                Check(Mathf.Abs(new LoftMeshStamp.Sampler(mesh, settings).Delta(new Vector3(0, 2, 0)) - 4) < .0001f, "Fit world elevation");
                settings.mode = LoftMeshStamp.Mode.Add;
                settings.position = Vector3.zero;
                settings.yaw = 90;
                var rotated = new LoftMeshStamp.Sampler(mesh, settings);
                Check(rotated.Delta(new Vector3(0, 0, -1)) > rotated.Delta(new Vector3(0, 0, 1)), "Yaw rotation");
                settings.yaw = 0;

                var container = root.AddComponent<SplineContainer>();
                container.Spline = new Spline(new[] { new float3(0, 0, -10), new float3(0, 0, 10) }, TangentMode.Linear);
                var source = new MultiSplineLoft.SplineSource { container = container };
                var plan = LoftMeshStamp.Prepare(source, new LoftMeshStamp.Sampler(mesh, settings), settings);
                Check(plan.splits.Count > 0 && plan.changed.Count > 0, "Stamp between sparse original knots");
                Check(container.Spline.Count == 2, "Preview does not mutate original");
                Check(math.distance(plan.result[0].Position, container.Spline[0].Position) < .0001f, "Start preserved");
                Check(math.distance(plan.result[plan.result.Count - 1].Position, container.Spline[1].Position) < .0001f, "End preserved");
                float peak = 0;
                for (int i = 0; i < plan.result.Count; i++)
                {
                    var point = plan.result[i].Position;
                    Check(math.all(math.isfinite(point)), "Finite result");
                    Check(Mathf.Abs(point.x) < .00001f, "No lateral movement");
                    if (Mathf.Abs(point.z) >= 2) Check(Mathf.Abs(point.y) < .00001f, "Uncovered knots stay at baseline");
                    peak = Mathf.Max(peak, point.y);
                }
                Check(Mathf.Abs(peak - 1) < .0001f, "Peak height");
                settings.resolution = 32;
                var fine = LoftMeshStamp.Prepare(source, new LoftMeshStamp.Sampler(mesh, settings), settings);
                Check(fine.splits.Count > plan.splits.Count, "Resolution adds local detail");
                settings.strength = 0;
                Check(LoftMeshStamp.Prepare(source, new LoftMeshStamp.Sampler(mesh, settings), settings).changed.Count == 0, "Zero strength is a no-op");
                settings.strength = 1;
                root.transform.SetPositionAndRotation(new Vector3(10, 3, 20), Quaternion.Euler(10, 35, 0));
                root.transform.localScale = new Vector3(2, 3, .5f);
                settings.position = root.transform.position;
                settings.yaw = 35;
                settings.pitch = 20;
                settings.roll = -15;
                var transformedSampler = new LoftMeshStamp.Sampler(mesh, settings);
                var transformed = LoftMeshStamp.Prepare(source, transformedSampler, settings);
                var baseline = new Spline(container.Spline);
                foreach (int index in transformed.splits) LoftMeshStamp.Split(baseline, index);
                foreach (int index in transformed.changed)
                {
                    Vector3 before = root.transform.TransformPoint(baseline[index].Position);
                    Vector3 after = root.transform.TransformPoint(transformed.result[index].Position);
                    Check(Mathf.Abs(after.x - before.x) < .0001f && Mathf.Abs(after.z - before.z) < .0001f, "Transformed loft preserves world XZ");
                    Check(Mathf.Abs(after.y - before.y - transformedSampler.Delta(before)) < .0001f, "Transformed height");
                }
                mesh.UploadMeshData(true);
                settings.roll += .5f; // Force a fresh projection from non-readable mesh data.
                Check(new LoftMeshStamp.Sampler(mesh, settings) != null && !mesh.isReadable, "Imported non-readable mesh");
                var loft = root.AddComponent<MultiSplineLoft>();
                loft.Sources.Add(source);
                var second = new GameObject("Other rail");
                second.transform.SetParent(root.transform, false);
                second.transform.localPosition = Vector3.right;
                var other = second.AddComponent<SplineContainer>();
                other.Spline = new Spline(container.Spline);
                loft.Sources.Add(new MultiSplineLoft.SplineSource { container = other });
                var commit = LoftMeshStamp.Prepare(loft, mesh, settings);
                int originalCount = container.Spline.Count;
                LoftMeshStamp.Apply(loft, commit);
                foreach (var edit in commit)
                {
                    var actual = edit.source.container.Splines[edit.source.splineIndex];
                    Check(actual.Count == edit.result.Count, "Commit knot count equals preview");
                    for (int i = 0; i < actual.Count; i++)
                    {
                        Check(math.distance(actual[i].Position, edit.result[i].Position) < .0001f, "Commit positions equal preview");
                        Check(math.distance(actual[i].TangentIn, edit.result[i].TangentIn) < .0001f &&
                            math.distance(actual[i].TangentOut, edit.result[i].TangentOut) < .0001f, "Commit handles equal preview");
                    }
                }
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Check(container.Spline.Count == originalCount, "Undo restores original knot count");
                Undo.PerformRedo();
                Check(container.Spline.Count > originalCount, "Redo restores stamped knots");
                Debug.Log("Loft mesh stamp PASS: exact subdivision, automatic/closed curves, sparse knots, preview isolation, local resolution, additive/carve/fit modes, pitch/yaw/roll and tilted footprints, blending, transformed splines, non-readable meshes, preview/commit parity, Undo and Redo.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void CheckRotation(Mesh mesh, float pitch, float yaw, float roll)
        {
            foreach (LoftMeshStamp.Mode mode in Enum.GetValues(typeof(LoftMeshStamp.Mode)))
            {
                var settings = new LoftMeshStamp.Settings { width = 6, length = 8, height = 3,
                    position = new Vector3(10, 12, -7), pitch = pitch, yaw = yaw, roll = roll, mode = mode, blend = 0 };
                float sign = mode == LoftMeshStamp.Mode.Carve ? -1 : 1;
                // Interior point of the analytical ramp y=(x+1)/2. Compute its
                // expected rotated world height independently of the sampler.
                Vector3 relative = Quaternion.Euler(pitch, yaw, roll) * new Vector3(.6f, 1.8f * sign, -1);
                Vector3 ghost = settings.MeshToStamp(mesh).MultiplyPoint3x4(new Vector3(.2f, .6f, -.25f));
                Check(Vector3.Distance(relative, ghost) < .0001f, "Rotated ghost transform");
                var sampler = new LoftMeshStamp.Sampler(mesh, settings);
                foreach (float originalHeight in new[] { -30f, 50f })
                {
                    Vector3 point = settings.position + relative;
                    point.y = originalHeight;
                    float expected = mode == LoftMeshStamp.Mode.FitToMesh
                        ? settings.position.y + relative.y - originalHeight : relative.y;
                    Check(Mathf.Abs(sampler.Delta(point) - expected) < .0002f,
                        $"Pitch/yaw/roll projection and height independence: {mode}, {pitch}/{yaw}/{roll}");
                }
                settings.strength = .4f;
                settings.blend = .5f;
                Vector3 probe = settings.position + relative;
                probe.y = settings.position.y;
                Check(Mathf.Abs(sampler.Delta(probe) - relative.y * .4f) < .0002f, "Tilted strength and local footprint blend");
            }
        }

        static void CheckTiltedFootprint(Mesh mesh, UnityEngine.SceneManagement.Scene scene)
        {
            var settings = new LoftMeshStamp.Settings { width = 2, length = 2, height = 12, pitch = 60, blend = 0 };
            Vector3 point = Quaternion.Euler(60, 0, 0) * new Vector3(0, 6, 0);
            var sampler = new LoftMeshStamp.Sampler(mesh, settings);
            Check(Mathf.Abs(point.z) > settings.length / 2, "Test extends beyond the unrotated footprint");
            Check(Mathf.Abs(sampler.Delta(new Vector3(point.x, -100, point.z)) - point.y) < .0001f, "Tilted projected footprint sampled");
            var go = new GameObject("Tilted footprint curve") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            try
            {
                var container = go.AddComponent<SplineContainer>();
                container.Spline = new Spline(new[] { new float3(-5, -100, point.z), new float3(5, -100, point.z) }, TangentMode.Linear);
                var source = new MultiSplineLoft.SplineSource { container = container };
                var plan = LoftMeshStamp.Prepare(source, sampler, settings);
                Check(plan.splits.Count > 0 && plan.changed.Count > 0, "Subdivision follows tilted footprint independently of loft elevation");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static void CheckSplit(bool closed, TangentMode mode)
        {
            var spline = new Spline(new[] { new float3(-2, 1, 0), new float3(0, 3, 4), new float3(4, -1, 9) }, mode, closed);
            int index = closed ? spline.Count - 1 : 0;
            BezierCurve original = spline.GetCurve(index);
            LoftMeshStamp.Split(spline, index);
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                float3 expected = CurveUtility.EvaluatePosition(original, t);
                float3 actual = t <= .5f ? CurveUtility.EvaluatePosition(spline.GetCurve(index), t * 2)
                    : CurveUtility.EvaluatePosition(spline.GetCurve(index + 1), (t - .5f) * 2);
                Check(math.distance(expected, actual) < .00001f, "Exact de Casteljau subdivision: " + mode + ", closed=" + closed);
            }
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Loft mesh stamp validation failed: " + label);
        }
    }
}
