using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Spline;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Spline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.MapTools
{
    // Editor-only authoring: the resulting shape lives in the original spline knots.
    [InitializeOnLoad]
    internal static class LoftMeshStamp
    {
        static readonly HashSet<MultiSplineLoft> editedLofts = new HashSet<MultiSplineLoft>();
        static LoftMeshStampProjection cachedProjection;

        static LoftMeshStamp()
        {
            Undo.undoRedoPerformed += () => EditorApplication.delayCall += RefreshEditedLofts;
            EditorApplication.projectChanged += () => cachedProjection = null;
        }

        static void RefreshEditedLofts()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            editedLofts.RemoveWhere(loft => loft == null);
            foreach (var loft in editedLofts) loft.Regenerate();
        }

        internal static void TrackUndo(MultiSplineLoft loft) => editedLofts.Add(loft);

        internal enum Mode { Add, Carve, FitToMesh }

        [Serializable]
        internal sealed class Settings
        {
            public Vector3 position;
            public float yaw;
            public float pitch, roll;
            public float width = 6f, length = 10f, height = 2f;
            public float strength = 1f, blend = .2f;
            public int resolution = 16;
            public Mode mode;

            public Quaternion Rotation => Quaternion.Euler(pitch, yaw, roll);

            // Shared by the wire preview and the projected triangle sampler.
            internal Matrix4x4 MeshToStamp(Mesh mesh)
            {
                Bounds bounds = mesh.bounds;
                Vector3 scale = new Vector3(width / Mathf.Max(.00001f, bounds.size.x),
                    height / Mathf.Max(.00001f, bounds.size.y), length / Mathf.Max(.00001f, bounds.size.z));
                if (mode == Mode.Carve) scale.y = -scale.y;
                return Matrix4x4.TRS(Vector3.zero, Rotation, scale)
                    * Matrix4x4.Translate(-new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            }
        }

        internal sealed class Sampler
        {
            readonly LoftMeshStampProjection projection;
            readonly Settings settings;

            internal Sampler(Mesh mesh, Settings settings)
            {
                if (mesh == null) throw new ArgumentException("Choose a stamp mesh.");
                if (!math.all(math.isfinite((float3)settings.position)) || !math.isfinite(settings.yaw)
                    || !math.isfinite(settings.pitch) || !math.isfinite(settings.roll)
                    || !math.isfinite(settings.width) || !math.isfinite(settings.length) || !math.isfinite(settings.height)
                    || settings.width < .01f || settings.length < .01f || settings.height < 0
                    || !math.isfinite(settings.strength) || settings.strength < 0 || settings.strength > 1
                    || !math.isfinite(settings.blend) || settings.blend < 0 || settings.blend > 1
                    || settings.resolution < 4 || settings.resolution > 64)
                    throw new ArgumentException("Use finite stamp dimensions, strength/blend from 0 to 1, and knot resolution from 4 to 64.");
                if (mesh.bounds.size.x < .0001f || mesh.bounds.size.z < .0001f)
                    throw new ArgumentException("The stamp needs a surface spanning its local X and Z axes.");
                this.settings = settings;
                Matrix4x4 matrix = settings.MeshToStamp(mesh);
                bool carve = settings.mode == Mode.Carve;
                if (cachedProjection == null || !cachedProjection.Matches(mesh, matrix, carve))
                    cachedProjection = new LoftMeshStampProjection(mesh, matrix, carve);
                projection = cachedProjection;
            }

            internal float Delta(Vector3 world)
            {
                Vector3 p = world - settings.position;
                if (!projection.TryHeight(p.x, p.z, out float h, out Vector2 footprint)) return 0;
                float edge = 1f - Mathf.Max(Mathf.Abs(footprint.x), Mathf.Abs(footprint.y));
                float weight = settings.blend <= 0 ? 1 : Mathf.SmoothStep(0, 1, edge / settings.blend);
                float delta = h;
                if (settings.mode == Mode.FitToMesh) delta += settings.position.y - world.y;
                return delta * weight * settings.strength;
            }

            internal bool Intersects(BezierCurve curve, Transform transform)
            {
                Vector3 a = transform.TransformPoint(curve.P0) - settings.position;
                var bounds = new Bounds(a, Vector3.zero);
                bounds.Encapsulate(transform.TransformPoint(curve.P1) - settings.position);
                bounds.Encapsulate(transform.TransformPoint(curve.P2) - settings.position);
                bounds.Encapsulate(transform.TransformPoint(curve.P3) - settings.position);
                Bounds footprint = projection.Bounds;
                return bounds.min.x <= footprint.max.x && bounds.max.x >= footprint.min.x
                    && bounds.min.z <= footprint.max.z && bounds.max.z >= footprint.min.z;
            }
        }

        internal sealed class Plan
        {
            internal MultiSplineLoft.SplineSource source;
            internal Spline result;
            internal Matrix4x4 transformMatrix;
            internal readonly List<int> splits = new List<int>();
            internal readonly List<int> changed = new List<int>();
        }

        // Exact de Casteljau split. Broken handles retain the shape even when the
        // input used automatic or mirrored handles. Container notifications retain knot links.
        internal static void Split(Spline spline, int curveIndex)
        {
            int next = (curveIndex + 1) % spline.Count;
            var a = spline[curveIndex];
            var b = spline[next];
            CurveUtility.Split(spline.GetCurve(curveIndex), .5f, out var left, out var right);
            spline.SetTangentMode(curveIndex, TangentMode.Broken);
            spline.SetTangentMode(next, TangentMode.Broken);
            a.TangentOut = math.mul(math.inverse(a.Rotation), left.P1 - left.P0);
            b.TangentIn = math.mul(math.inverse(b.Rotation), right.P2 - right.P3);
            spline[curveIndex] = a;
            spline[next] = b;
            spline.Insert(curveIndex + 1, new BezierKnot(left.P3, left.P2 - left.P3,
                right.P1 - right.P0, quaternion.identity), TangentMode.Broken);
        }

        internal static Plan Prepare(MultiSplineLoft.SplineSource source, Sampler sampler, Settings settings)
        {
            var plan = new Plan { source = source, result = new Spline(source.container.Splines[source.splineIndex]) };
            Spline spline = plan.result;
            Transform transform = source.container.transform;
            plan.transformMatrix = transform.localToWorldMatrix;
            if (Mathf.Abs(plan.transformMatrix.determinant) < .00000001f)
                throw new InvalidOperationException("Source spline transforms must not have a zero scale.");
            float spacing = Mathf.Min(settings.width, settings.length) / Mathf.Max(2, settings.resolution - 1);
            // Subdivide only intersecting control hulls; this also catches stamps
            // between widely spaced original knots and curved paths entering twice.
            for (int i = 0; i < (spline.Closed ? spline.Count : spline.Count - 1);)
            {
                BezierCurve curve = spline.GetCurve(i);
                float length = Vector3.Distance(transform.TransformPoint(curve.P0), transform.TransformPoint(curve.P1))
                    + Vector3.Distance(transform.TransformPoint(curve.P1), transform.TransformPoint(curve.P2))
                    + Vector3.Distance(transform.TransformPoint(curve.P2), transform.TransformPoint(curve.P3));
                if (length > spacing && sampler.Intersects(curve, transform))
                {
                    if (plan.splits.Count >= 4096) throw new InvalidOperationException("Stamp would add over 4096 knots to one curve. Lower the knot resolution or increase the stamp size.");
                    Split(spline, i);
                    plan.splits.Add(i);
                }
                else i++;
            }

            // Compute all offsets from the unmodified subdivided spline, so a
            // knot's result cannot depend on the iteration order.
            var result = new BezierKnot[spline.Count];
            var freeze = new HashSet<int>();
            for (int i = 0; i < spline.Count; i++)
            {
                var knot = spline[i];
                Vector3 world = transform.TransformPoint(knot.Position);
                float delta = sampler.Delta(world);
                result[i] = knot;
                Vector3 worldIn = transform.TransformVector(math.rotate(knot.Rotation, knot.TangentIn));
                Vector3 worldOut = transform.TransformVector(math.rotate(knot.Rotation, knot.TangentOut));
                float inDelta = Derivative(sampler, world, worldIn);
                float outDelta = Derivative(sampler, world, worldOut);
                if (Mathf.Abs(delta) < .000001f && Mathf.Abs(inDelta) < .000001f && Mathf.Abs(outDelta) < .000001f) continue;
                // Fit the derivative as well as the height, retaining authored XZ handles.
                worldIn.y += inDelta;
                worldOut.y += outDelta;
                knot.Position = transform.InverseTransformPoint(world + Vector3.up * delta);
                knot.TangentIn = math.mul(math.inverse(knot.Rotation), (float3)transform.InverseTransformVector(worldIn));
                knot.TangentOut = math.mul(math.inverse(knot.Rotation), (float3)transform.InverseTransformVector(worldOut));
                result[i] = knot;
                plan.changed.Add(i);
                freeze.Add(i);
                if (i > 0 || spline.Closed) freeze.Add((i - 1 + spline.Count) % spline.Count);
                if (i + 1 < spline.Count || spline.Closed) freeze.Add((i + 1) % spline.Count);
            }
            // Neighbouring auto handles must not change as a side effect of a move.
            foreach (int i in freeze) spline.SetTangentMode(i, TangentMode.Broken);
            foreach (int i in freeze) spline[i] = result[i];
            return plan;
        }

        static float Derivative(Sampler sampler, Vector3 position, Vector3 tangent)
        {
            const float step = .001f;
            return (sampler.Delta(position + tangent * step) - sampler.Delta(position - tangent * step)) / (2 * step);
        }

        internal static List<Plan> Prepare(MultiSplineLoft loft, Mesh mesh, Settings settings)
        {
            if (loft == null || EditorUtility.IsPersistent(loft)) throw new ArgumentException("Choose a scene loft.");
            var sampler = new Sampler(mesh, settings);
            var plans = new List<Plan>();
            var visited = new HashSet<Spline>();
            foreach (var source in loft.Sources)
            {
                if (source?.IsValid != true) continue;
                Spline spline = source.container.Splines[source.splineIndex];
                if (!visited.Add(spline)) continue;
                // Linked endpoints can move other splines outside the preview.
                // Keep those joins intact; the user can unlink deliberately first.
                for (int i = 0; i < spline.Count; i++)
                    if (Mathf.Abs(sampler.Delta(source.container.transform.TransformPoint(spline[i].Position))) > .000001f
                        && source.container.KnotLinkCollection.TryGetKnotLinks(new SplineKnotIndex(source.splineIndex, i), out var links)
                        && links.Count > 1)
                        throw new InvalidOperationException("The stamp would move a linked knot. Move the stamp away from the join or unlink that knot first.");
                Plan plan = Prepare(source, sampler, settings);
                if (plan.changed.Count > 0) plans.Add(plan);
            }
            return plans;
        }

        internal static void Apply(MultiSplineLoft loft, List<Plan> plans)
        {
            if (plans.Count == 0) return;
            TrackUndo(loft);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Loft Mesh Stamp");
            var objects = new HashSet<UnityEngine.Object> { loft };
            foreach (var plan in plans) objects.Add(plan.source.container);
            var targets = new UnityEngine.Object[objects.Count];
            objects.CopyTo(targets);
            Undo.RegisterCompleteObjectUndo(targets, "Loft Mesh Stamp");
            try
            {
                foreach (var plan in plans)
                {
                    Spline spline = plan.source.container.Splines[plan.source.splineIndex];
                    foreach (int split in plan.splits) Split(spline, split);
                    // Set modes before positions, then copy the saved handles. This
                    // prevents auto tangents recalculating neighbouring untouched spans.
                    for (int i = 0; i < spline.Count; i++)
                        spline.SetTangentMode(i, plan.result.GetTangentMode(i));
                    for (int i = 0; i < spline.Count; i++)
                        if (plan.result.GetTangentMode(i) == TangentMode.Broken) spline[i] = plan.result[i];
                }
                foreach (var target in targets)
                {
                    EditorUtility.SetDirty(target);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                }
                loft.Regenerate();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(loft.gameObject.scene);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                loft.Regenerate();
                throw;
            }
        }
    }
}
