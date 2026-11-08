using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [Serializable] public sealed class StudioSymmetryDeformation
    {
        public Matrix4x4 reflection;
        public int[] mappings;
        public int vertexCount;
        internal bool HasMappings => mappings != null && mappings.Length > 0;
        internal void Apply(Vector3[] values)
        {
            if(!HasMappings)return; // Unity can materialize an empty inline record after native Undo.
            if(mappings.Length%3!=0)throw new InvalidOperationException("The saved symmetry correspondence is incomplete.");
            if(values.Length!=vertexCount)throw new InvalidOperationException("The symmetry deformation requires its original vertex layout.");
            for(int i=0;i<mappings.Length;i+=3)
            {
                var value=values[mappings[i]];int mode=mappings[i+2];
                values[mappings[i+1]]=mode==0?value:mode==1?reflection.MultiplyPoint3x4(value):(value+reflection.MultiplyPoint3x4(value))*.5f;
            }
        }
    }
    // Captured mesh-local reflection and geometric correspondence. Render seams
    // remain aliases; explicitly detached coincident vertices are never welded.
    internal sealed class StudioMeshSymmetry
    {
        internal readonly Matrix4x4 Reflection;
        internal readonly int[] Geometry, Partner;
        internal readonly float Tolerance;
        private readonly Vector3[] framePositions;
        private readonly Dictionary<int, List<int>> aliases = new Dictionary<int, List<int>>();
        private readonly int axis;
        private readonly Vector3 origin;
        private readonly Matrix4x4 frame;

        internal StudioMeshSymmetry(Vector3[] positions, int[] identities, Matrix4x4 localToWorld,
            Bounds bounds, bool world, int axis)
        {
            this.axis = Mathf.Clamp(axis, 0, 2);
            frame = world ? localToWorld : Matrix4x4.identity;
            if (world && (float.IsNaN(frame.determinant) || float.IsInfinity(frame.determinant) || Mathf.Abs(frame.determinant) < 1e-12f))
                throw new InvalidOperationException("World symmetry requires a finite, invertible object transform.");
            origin = world ? Vector3.zero : bounds.center;
            var mirror = Matrix4x4.identity; mirror[this.axis, this.axis] = -1;
            Reflection = frame.inverse * Matrix4x4.Translate(origin) * mirror * Matrix4x4.Translate(-origin) * frame;
            framePositions = positions.Select(p => frame.MultiplyPoint3x4(p) - origin).ToArray();
            var extent = new Bounds(framePositions.Length == 0 ? Vector3.zero : framePositions[0], Vector3.zero);
            foreach (var p in framePositions) extent.Encapsulate(p);
            Tolerance = Mathf.Max(1e-6f, extent.size.magnitude * 1e-5f);
            Geometry = new int[positions.Length]; Partner = Enumerable.Repeat(-1, positions.Length).ToArray();
            var legacy = new Dictionary<Vector3, int>();
            bool explicitIds = identities != null && identities.Length == positions.Length;
            for (int i = 0; i < positions.Length; i++)
            {
                int id;
                if (explicitIds) id = identities[i];
                else if (!legacy.TryGetValue(positions[i], out id)) { id = legacy.Count; legacy.Add(positions[i], id); }
                Geometry[i] = id;
                if (!aliases.TryGetValue(id, out var list)) aliases[id] = list = new List<int>();
                list.Add(i);
            }
            var cells = new Dictionary<(long, long, long), List<int>>();
            (long, long, long) Cell(Vector3 p) => ((long)Math.Floor(p.x / Tolerance), (long)Math.Floor(p.y / Tolerance), (long)Math.Floor(p.z / Tolerance));
            foreach (var list in aliases.Values)
            {
                int i = list[0]; var key = Cell(framePositions[i]);
                if (!cells.TryGetValue(key, out var values)) cells[key] = values = new List<int>();
                values.Add(i);
            }
            foreach (var list in aliases.Values)
            {
                int i = list[0]; var p = framePositions[i];
                if (Mathf.Abs(p[this.axis]) <= Tolerance)
                { foreach (int v in list) Partner[v] = i; continue; }
                p[this.axis] = -p[this.axis]; var key = Cell(p); int candidate = -1; bool ambiguous = false;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    if (cells.TryGetValue((key.Item1+x, key.Item2+y, key.Item3+z), out var values)) foreach (int v in values)
                        if ((framePositions[v]-p).sqrMagnitude <= Tolerance*Tolerance)
                        { if (candidate >= 0) ambiguous = true; candidate = v; }
                if (!ambiguous) foreach (int v in list) Partner[v] = candidate;
            }
            // Correspondence must be reciprocal, including ambiguous coincident shells.
            var raw = (int[])Partner.Clone();
            for (int i = 0; i < Partner.Length; i++)
                if (raw[i] >= 0 && (raw[raw[i]] < 0 || Geometry[raw[raw[i]]] != Geometry[i])) Partner[i] = -1;
        }
        internal Vector3 Reflect(Vector3 p) => Reflection.MultiplyPoint3x4(p);
        internal float Side(Vector3 p) => (frame.MultiplyPoint3x4(p)-origin)[axis];
        internal int MirroredGeometry(int vertex)
        {
            if (vertex < 0 || vertex >= Partner.Length || Partner[vertex] < 0)
                throw new InvalidOperationException("Symmetry needs an unambiguous matching component on the opposite side of this mesh.");
            return Geometry[Partner[vertex]];
        }
        internal HashSet<int> ExpandVertices(IEnumerable<int> selection)
        {
            var result = new HashSet<int>(selection);
            foreach (int i in selection.ToArray()) foreach (int v in aliases[MirroredGeometry(i)]) result.Add(v);
            return result;
        }
        internal sealed class Deformation
        {
            private readonly StudioMeshSymmetry symmetry;
            private readonly List<(int driver, int[] followers, bool center)> pairs = new List<(int, int[], bool)>();
            internal readonly int[] Affected;
            internal Deformation(StudioMeshSymmetry symmetry, IEnumerable<int> selection)
            {
                this.symmetry = symmetry;
                var chosen = new HashSet<int>(selection); var seen = new HashSet<int>(); var affected = new HashSet<int>(chosen);
                foreach (int v in chosen.OrderBy(v => v))
                {
                    int a = symmetry.Geometry[v], b = symmetry.MirroredGeometry(v);
                    if (!seen.Add(a)) continue; seen.Add(b);
                    int driver = v;
                    var own = symmetry.aliases[a]; var other = symmetry.aliases[b];
                    // A two-sided selection has one deterministic driver per pair.
                    if (a != b && other.Any(chosen.Contains) && symmetry.framePositions[v][symmetry.axis] < 0)
                    { driver = other.First(chosen.Contains); var swap = own; own = other; other = swap; }
                    var followers = a == b ? own : own.Concat(other).ToList();
                    affected.UnionWith(followers); pairs.Add((driver, followers.ToArray(), a == b));
                }
                Affected = affected.OrderBy(v => v).ToArray();
            }
            internal void Apply(Vector3[] values)
            {
                foreach (var pair in pairs)
                {
                    var value = values[pair.driver];
                    if (pair.center) value = (value + symmetry.Reflect(value)) * .5f;
                    int id = symmetry.Geometry[pair.driver];
                    foreach (int v in pair.followers)
                        values[v] = pair.center || symmetry.Geometry[v] == id ? value : symmetry.Reflect(value);
                }
            }
            internal StudioSymmetryDeformation Record()
            {
                var map=new List<int>();
                foreach(var pair in pairs)foreach(int follower in pair.followers)
                    map.AddRange(new[]{pair.driver,follower,pair.center?2:symmetry.Geometry[follower]==symmetry.Geometry[pair.driver]?0:1});
                return new StudioSymmetryDeformation{reflection=symmetry.Reflection,vertexCount=symmetry.Geometry.Length,mappings=map.ToArray()};
            }
        }
        internal Deformation Capture(IEnumerable<int> selection) => new Deformation(this, selection);
        internal static StudioMeshCuts.MergeResult MergeGroups(StudioModelDocument.Node node,int[] primary,int[] mirrored)
        {
            var geometry=StudioSubdivisionData.Geometry(node);
            var mirrorIds=new HashSet<int>(mirrored.Select(i=>geometry[i]));
            var first=new StudioMeshCuts(node).MergeVertices(node,primary,true,0,Matrix4x4.identity);
            geometry=StudioSubdivisionData.Geometry(node);
            var firstIds=new HashSet<int>(first?.Selection.Select(i=>geometry[i])??Enumerable.Empty<int>());
            var remaining=Enumerable.Range(0,geometry.Length).Where(i=>mirrorIds.Contains(geometry[i])).ToArray();
            var second=new StudioMeshCuts(node).MergeVertices(node,remaining,true,0,Matrix4x4.identity);
            if(first==null)return second;
            geometry=StudioSubdivisionData.Geometry(node);
            if(second!=null){first.Removed+=second.Removed;firstIds.UnionWith(second.Selection.Select(i=>geometry[i]));}
            first.Selection=Enumerable.Range(0,geometry.Length).Where(i=>firstIds.Contains(geometry[i])).ToArray();
            return first;
        }
    }
}
