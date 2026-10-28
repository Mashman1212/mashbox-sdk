using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        internal int[] SplitMeshes(IReadOnlyDictionary<int,HashSet<int>> selection)
        {
            var replacements = new Dictionary<int,Node>(); var additions = new List<Node>(); var result = new List<int>();
            var allocated = new List<Mesh>();
            try
            {
                foreach (var pair in selection.OrderBy(p => p.Key))
                {
                    if (pair.Key < 0 || pair.Key >= nodes.Count) throw new InvalidOperationException("The selected mesh no longer exists.");
                    var source = nodes[pair.Key];
                    if (!source.mesh || IsBone(pair.Key) || !string.IsNullOrEmpty(source.anchorType))
                        throw new InvalidOperationException("Split Selection requires ordinary mesh objects, not bones or anchors.");
                    var topology = new StudioMeshCuts(source); topology.ValidateSplitSource(source);
                    var chosen = new HashSet<int>(pair.Value.Where(f => f >= 0 && f < topology.Faces.Count));
                    if (chosen.Count == 0) continue;
                    if (chosen.Count == topology.Faces.Count)
                        throw new InvalidOperationException("Split Selection would leave '" + source.name + "' with an empty mesh. Leave at least one face on every source mesh.");
                    var extracted = topology.ExtractSplitFaces(source,chosen,false); allocated.Add(extracted.mesh);
                    extracted.name = ObjectNames.GetUniqueName(nodes.Where(n => n.parent == source.parent).Concat(additions).Concat(replacements.Values)
                        .Where(n => n.parent == source.parent).Select(n => n.name).ToArray(),source.name + " Split");
                    extracted.mesh.name = extracted.name;
                    // Shift only the extracted origin; its rendered surface stays fixed.
                    var center = extracted.mesh.bounds.center;
                    var points = extracted.mesh.vertices;
                    for (int v = 0; v < points.Length; v++) points[v] -= center;
                    extracted.mesh.vertices = points; extracted.mesh.RecalculateBounds();
                    extracted.position += extracted.rotation * Vector3.Scale(extracted.scale,center);
                    var remaining = topology.ExtractSplitFaces(source,new HashSet<int>(Enumerable.Range(0,topology.Faces.Count).Where(f => !chosen.Contains(f))),true);
                    allocated.Add(remaining.mesh); replacements.Add(pair.Key,remaining);
                    extracted.siblingOrder = -1; result.Add(nodes.Count + additions.Count); additions.Add(extracted);
                }
                if (result.Count == 0) return Array.Empty<int>();
                foreach (var mesh in allocated) AttachOwnedMesh(mesh);
                Undo.RegisterCompleteObjectUndo(this,"Split mesh selection");
                foreach (var pair in replacements)
                {
                    // The source node, transform, pivot, hierarchy and other metadata
                    // stay untouched. Only replace its reduced surface data.
                    var source = nodes[pair.Key]; var remaining = pair.Value;
                    source.mesh = remaining.mesh; source.cutPolygons = remaining.cutPolygons;
                    source.cutTriangles = remaining.cutTriangles; source.normalGroups = remaining.normalGroups;
                }
                nodes.AddRange(additions);
                EditorUtility.SetDirty(this);
                // Old and new mesh objects remain available for native history.
                return result.ToArray();
            }
            catch { foreach (var mesh in allocated) if (mesh) DestroyImmediate(mesh,true); throw; }
        }
    }
}
