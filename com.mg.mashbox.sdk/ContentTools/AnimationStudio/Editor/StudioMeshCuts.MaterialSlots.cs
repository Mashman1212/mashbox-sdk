using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        internal sealed class MaterialSlotIndexEdit
        {
            internal Mesh Mesh;
            internal int[][] Before, After;
            internal int[] Polygons, Triangles, FaceSlots;
            internal int SourceCount;
            internal bool Changed;
            internal void Apply(bool restore = false)
            {
                var indices = restore ? Before : After; var bounds = Mesh.bounds;
                Mesh.subMeshCount = indices.Length;
                for (int sub = 0; sub < indices.Length; sub++) Mesh.SetTriangles(indices[sub], sub, false);
                Mesh.bounds = bounds;
            }
        }
        internal static MaterialSlotIndexEdit PrepareMaterialSlotIndices(StudioModelDocument.Node node, int slot, IEnumerable<int> selection,
            bool remove, int minimumCount, IList<Face> faces = null, List<int>[] faceTriangles = null)
        {
            if (slot < 0) throw new InvalidOperationException("Material slot ID must be nonnegative.");
            var mesh = node.mesh;
            var before = Enumerable.Range(0, mesh.subMeshCount).Select(mesh.GetTriangles).ToArray();
            int sourceCount = Math.Max(before.Length, minimumCount), count = remove ? sourceCount - 1 : Math.Max(before.Length, slot + 1);
            var edit = new MaterialSlotIndexEdit { Mesh = mesh, Before = before, SourceCount = sourceCount };
            if (remove && sourceCount <= 1) return edit;
            if (remove && slot >= sourceCount) throw new InvalidOperationException("That material slot no longer exists.");
            if (faces == null)
            {
                if (node.cutPolygons != null && node.cutTriangles != null && node.cutTriangles.SequenceEqual(before.SelectMany(t => t)))
                {
                    var parsed = new List<Face>(); var data = node.cutPolygons;
                    for (int i = 0; i < data.Length;) { int sub = data[i++], size = data[i++]; var vertices = new List<int>(size); for (int c = 0; c < size; c++) vertices.Add(data[i++]); parsed.Add(new Face { Submesh = sub, Vertices = vertices }); }
                    faces = parsed;
                }
                else faces = new StudioMeshCuts(node).Faces;
            }
            var selected = selection == null ? null : new HashSet<int>(selection);
            if (selected != null && selected.Any(f => f < 0 || f >= faces.Count)) throw new InvalidOperationException("The face selection changed; select the faces again.");
            bool whole = selected == null || selected.Count == faces.Count;
            if (!remove && count == before.Length && (whole ? faces.All(f => f.Submesh == slot) : selected.All(f => faces[f].Submesh == slot))) return edit;
            int Slot(int old) => remove ? old == slot ? 0 : old > slot ? old - 1 : old : slot;
            var output = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
            if (remove || whole || selected.Count == 0)
            {
                for (int sub = 0; sub < before.Length; sub++) output[remove || whole ? Slot(sub) : sub].AddRange(before[sub]);
            }
            else
            {
                var source = faceTriangles ?? MapSourceFaceTriangles(mesh, faces);
                for (int f = 0; f < faces.Count; f++) output[selected.Contains(f) ? slot : faces[f].Submesh].AddRange(source[f]);
            }
            var metadata = new List<int>(); edit.FaceSlots = new int[faces.Count];
            for (int f = 0; f < faces.Count; f++)
            {
                var face = faces[f]; int material = remove || whole || selected.Contains(f) ? Slot(face.Submesh) : face.Submesh;
                edit.FaceSlots[f] = material; metadata.Add(material); metadata.Add(face.Vertices.Count); metadata.AddRange(face.Vertices);
            }
            edit.After = output.Select(list => list.ToArray()).ToArray(); edit.Polygons = metadata.ToArray();
            edit.Triangles = edit.After.SelectMany(t => t).ToArray(); edit.Changed = true;
            return edit;
        }

        internal bool RemoveMaterialSlot(StudioModelDocument.Node node, int slot, int minimumCount = 0)
        {
            var mesh = node.mesh; int count = Math.Max(mesh.subMeshCount, minimumCount);
            if (count <= 1) return false;
            if (slot < 0 || slot >= count) throw new InvalidOperationException("That material slot no longer exists.");
            var output = Enumerable.Range(0, count - 1).Select(_ => new List<int>()).ToArray();
            int Remap(int id) => id == slot ? 0 : id > slot ? id - 1 : id;
            for (int sub = 0; sub < mesh.subMeshCount; sub++) output[Remap(sub)].AddRange(mesh.GetTriangles(sub));
            var polygons = new List<int>();
            foreach (var face in Faces) { polygons.Add(Remap(face.Submesh)); polygons.Add(face.Vertices.Count); polygons.AddRange(face.Vertices); }
            var bounds = mesh.bounds; mesh.subMeshCount = count - 1;
            for (int sub = 0; sub < output.Length; sub++) mesh.SetTriangles(output[sub], sub, false);
            mesh.bounds = bounds; node.cutPolygons = polygons.ToArray(); node.cutTriangles = mesh.triangles;
            return true;
        }

        internal bool AssignMaterialSlot(StudioModelDocument.Node node, int slot, IEnumerable<int> selection = null, List<int>[] faceTriangles = null)
        {
            if (slot < 0) throw new InvalidOperationException("Material slot ID must be nonnegative.");
            var selected = selection == null ? new HashSet<int>(Enumerable.Range(0, Faces.Count)) : new HashSet<int>(selection);
            if (selected.Any(i => i < 0 || i >= Faces.Count)) throw new InvalidOperationException("The face selection changed; select the faces again.");
            var mesh = node.mesh; int count = Math.Max(mesh.subMeshCount, slot + 1);
            if (count == mesh.subMeshCount && selected.All(f => Faces[f].Submesh == slot)) return false;
            bool whole = selected.Count == Faces.Count;
            if (whole || selected.Count == 0)
            {
                var lists = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
                for (int sub = 0; sub < mesh.subMeshCount; sub++) lists[whole ? slot : sub].AddRange(mesh.GetTriangles(sub));
                var metadata = new List<int>();
                foreach (var face in Faces) { metadata.Add(whole ? slot : face.Submesh); metadata.Add(face.Vertices.Count); metadata.AddRange(face.Vertices); }
                var originalBounds = mesh.bounds; mesh.subMeshCount = count;
                for (int sub = 0; sub < count; sub++) mesh.SetTriangles(lists[sub], sub, false);
                mesh.bounds = originalBounds; node.cutPolygons = metadata.ToArray(); node.cutTriangles = mesh.triangles;
                return true;
            }
            var source = faceTriangles ?? BevelSourceTriangles(mesh);
            var triangles = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
            var polygons = new List<int>();
            for (int f = 0; f < Faces.Count; f++)
            {
                var face = Faces[f]; int material = selected.Contains(f) ? slot : face.Submesh;
                triangles[material].AddRange(source[f]);
                polygons.Add(material); polygons.Add(face.Vertices.Count); polygons.AddRange(face.Vertices);
            }
            var bounds = mesh.bounds;
            mesh.subMeshCount = count;
            for (int sub = 0; sub < count; sub++) mesh.SetTriangles(triangles[sub], sub, false);
            mesh.bounds = bounds;
            node.cutPolygons = polygons.ToArray(); node.cutTriangles = mesh.triangles;
            return true;
        }
    }
}
