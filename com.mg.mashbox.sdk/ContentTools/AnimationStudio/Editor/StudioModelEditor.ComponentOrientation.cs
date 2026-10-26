using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool componentFrameValid, componentFrameWeld;
        private Mesh componentFrameMesh;
        private int componentFrameNode;
        private ComponentMode componentFrameMode;
        private Matrix4x4 componentFrameMatrix;
        private Quaternion componentFrame;
        private readonly HashSet<int> componentFrameSelection = new HashSet<int>();

        private Quaternion ComponentOrientation()
        {
            var matrix = Transforms[Active].localToWorldMatrix;
            if (componentFrameValid && componentFrameNode == Active && componentFrameMesh == ActiveMesh && componentFrameMode == mode &&
                componentFrameWeld == weld && componentFrameMatrix == matrix && componentFrameSelection.SetEquals(Components)) return componentFrame;
            componentFrameNode = Active; componentFrameMesh = ActiveMesh; componentFrameMode = mode; componentFrameMatrix = matrix; componentFrameWeld = weld;
            componentFrameSelection.Clear(); componentFrameSelection.UnionWith(Components);
            componentFrame = CalculateComponentOrientation(matrix);
            componentFrameValid = true;
            return componentFrame;
        }

        private List<StudioMeshCuts.Face> componentFaceUseSource;
        private int[] componentFaceOffsets, componentFaceUses;
        private IEnumerable<int> ComponentOrientationFaces(List<StudioMeshCuts.Face> faces)
        {
            if (mode==ComponentMode.Face) return Components.OrderBy(i=>i);
            if (!ReferenceEquals(componentFaceUseSource,faces))
            {
                componentFaceUseSource=faces;
                componentFaceOffsets=new int[ActiveMesh.vertexCount+1];
                foreach (var face in faces) foreach (int vertex in face.Vertices) componentFaceOffsets[vertex+1]++;
                for (int i=1;i<componentFaceOffsets.Length;i++) componentFaceOffsets[i]+=componentFaceOffsets[i-1];
                componentFaceUses=new int[componentFaceOffsets[componentFaceOffsets.Length-1]];
                var next=(int[])componentFaceOffsets.Clone();
                for (int f=0;f<faces.Count;f++) foreach (int vertex in faces[f].Vertices) componentFaceUses[next[vertex]++]=f;
            }
            var candidates=new HashSet<int>();
            var incidentVertices=SelectedVertices();
            // Orientation has always included coincident face seams, even when
            // the transform's weld setting is disabled.
            ExpandWeldedSelection(incidentVertices,ComponentPositions());
            foreach (int vertex in incidentVertices)
                for (int i=componentFaceOffsets[vertex];i<componentFaceOffsets[vertex+1];i++) candidates.Add(componentFaceUses[i]);
            return candidates.OrderBy(i=>i);
        }
        private Quaternion CalculateComponentOrientation(Matrix4x4 matrix)
        {
            var fallback = ObjectPivotOrientation;
            IList<Vector3> vertices = Document.nodes[Active].skinned ? PickVertices(Active) : ComponentPositions();
            var faces = Faces();
            var selectedPoints = new HashSet<Vector3>();
            if (mode != ComponentMode.Face)
                foreach (int i in SelectedVertices()) selectedPoints.Add(vertices[i]);
            var edges = mode == ComponentMode.Edge ? Edges() : null;
            var chosenEdges = edges == null ? new List<(int a, int b)>() :
                Components.Where(i => i >= 0 && i < edges.Count).OrderBy(i => i).Select(i => edges[i]).ToList();
            var normal = Vector3.zero; var tangent = Vector3.zero; float longest = 0;
            foreach (int f in ComponentOrientationFaces(faces))
            {
                if (f<0 || f>=faces.Count) continue;
                var face = faces[f];
                if (face.Vertices.Count < 3) continue;
                bool included = mode == ComponentMode.Face ? Components.Contains(f) : false;
                if (mode == ComponentMode.Vertex)
                    included = face.Vertices.Any(i => selectedPoints.Contains(vertices[i]));
                if (mode == ComponentMode.Edge)
                {
                    // Compare positions so UV/hard-normal seams do not omit adjoining faces.
                    for (int k = 0; k < face.Vertices.Count && !included; k++)
                    {
                        var a = vertices[face.Vertices[k]]; var b = vertices[face.Vertices[(k + 1) % face.Vertices.Count]];
                        included = chosenEdges.Any(e => (vertices[e.a] == a && vertices[e.b] == b) || (vertices[e.a] == b && vertices[e.b] == a));
                    }
                }
                if (!included) continue;
                // Compute area-weighted normals from world geometry. This also handles
                // nonuniform scale, mirrored transforms and warped polygon selections.
                var origin = matrix.MultiplyPoint3x4(vertices[face.Vertices[0]]);
                for (int k = 1; k + 1 < face.Vertices.Count; k++)
                    normal += Vector3.Cross(matrix.MultiplyPoint3x4(vertices[face.Vertices[k]]) - origin,
                        matrix.MultiplyPoint3x4(vertices[face.Vertices[k + 1]]) - origin);
                for (int k = 0; k < face.Vertices.Count; k++)
                {
                    var direction = matrix.MultiplyVector(vertices[face.Vertices[(k + 1) % face.Vertices.Count]] - vertices[face.Vertices[k]]);
                    if (direction.sqrMagnitude > longest * 1.0001f) { tangent = direction; longest = direction.sqrMagnitude; }
                }
            }
            if (mode == ComponentMode.Edge)
            {
                var direction = Vector3.zero; var reference = Vector3.zero;
                foreach (var edge in chosenEdges)
                {
                    var delta = matrix.MultiplyVector(vertices[edge.b] - vertices[edge.a]);
                    if (reference.sqrMagnitude < 1e-12f) reference = delta;
                    if (Vector3.Dot(delta, reference) < 0) delta = -delta;
                    direction += delta;
                }
                if (UsableComponentDirection(direction))
                    return PivotFrame(direction, UsableComponentDirection(normal) ? normal : fallback * Vector3.up);
            }
            // Opposing surfaces or degenerate geometry have no unique local frame.
            if (!UsableComponentDirection(normal)) return fallback;
            return PivotFrame(normal, UsableComponentDirection(tangent) ? tangent : fallback * Vector3.up);
        }

        private static bool UsableComponentDirection(Vector3 value) =>
            value.sqrMagnitude > 1e-12f && !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);
    }
}
