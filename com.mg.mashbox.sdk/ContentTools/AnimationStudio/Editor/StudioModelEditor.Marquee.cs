using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        // Clip against the rectangle's world-space volume. This handles perspective,
        // orthographic views and geometry crossing the camera near plane alike.
        internal sealed class MarqueeVolume
        {
            private readonly Plane[] planes;
            private List<Vector3> input = new List<Vector3>(10), output = new List<Vector3>(10);
            internal MarqueeVolume(Camera camera, Ray tl, Ray tr, Ray br, Ray bl, Ray center)
            {
                Plane Side(Ray a, Ray b)
                {
                    var plane = new Plane(a.GetPoint(100), b.GetPoint(100), a.GetPoint(200));
                    if (plane.GetDistanceToPoint(center.GetPoint(1)) < 0) plane.Flip();
                    return plane;
                }
                var forward = camera.transform.forward;
                planes = new[] { Side(tl, tr), Side(tr, br), Side(br, bl), Side(bl, tl),
                    new Plane(forward, camera.transform.position + forward * camera.nearClipPlane),
                    new Plane(-forward, camera.transform.position + forward * camera.farClipPlane) };
            }
            internal bool Segment(Vector3 a, Vector3 b, out Vector3 point)
            {
                foreach (var plane in planes)
                {
                    float da = plane.GetDistanceToPoint(a), db = plane.GetDistanceToPoint(b);
                    if (da < 0 && db < 0) { point = default; return false; }
                    if ((da < 0) != (db < 0))
                    {
                        var crossing = Vector3.LerpUnclamped(a, b, da / (da - db));
                        if (da < 0) a = crossing; else b = crossing;
                    }
                }
                point = (a + b) * .5f; return true;
            }
            internal bool Triangle(Vector3 a, Vector3 b, Vector3 c, out Vector3 point)
            {
                input.Clear(); input.Add(a); input.Add(b); input.Add(c);
                foreach (var plane in planes)
                {
                    output.Clear();
                    var previous = input[input.Count - 1]; float previousDistance = plane.GetDistanceToPoint(previous);
                    foreach (var current in input)
                    {
                        float distance = plane.GetDistanceToPoint(current);
                        if ((distance < 0) != (previousDistance < 0))
                            output.Add(Vector3.LerpUnclamped(previous, current, previousDistance / (previousDistance - distance)));
                        if (distance >= 0) output.Add(current);
                        previous = current; previousDistance = distance;
                    }
                    var swap = input; input = output; output = swap;
                    if (input.Count == 0) { point = default; return false; }
                }
                point = Vector3.zero; foreach (var vertex in input) point += vertex;
                point /= input.Count; return true;
            }
        }
        private static MarqueeVolume MarqueeBounds(SceneView view, Rect rect)
        {
            var center = rect.center;
            rect.width = Mathf.Max(1, rect.width); rect.height = Mathf.Max(1, rect.height); rect.center = center;
            return new MarqueeVolume(view.camera,
            HandleUtility.GUIPointToWorldRay(new Vector2(rect.xMin, rect.yMin)),
            HandleUtility.GUIPointToWorldRay(new Vector2(rect.xMax, rect.yMin)),
            HandleUtility.GUIPointToWorldRay(new Vector2(rect.xMax, rect.yMax)),
            HandleUtility.GUIPointToWorldRay(new Vector2(rect.xMin, rect.yMax)),
            HandleUtility.GUIPointToWorldRay(rect.center));
        }
        private bool ObjectOverlapsMarquee(int index, MarqueeVolume volume)
        {
            var vertices = PickVertices(index); var transform = Transforms[index];
            var world = new Vector3[vertices.Length];
            for (int v = 0; v < world.Length; v++) world[v] = transform.TransformPoint(vertices[v]);
            var triangles = Document.nodes[index].mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                if (volume.Triangle(world[triangles[i]], world[triangles[i + 1]], world[triangles[i + 2]], out _)) return true;
            return false;
        }
    }
}
