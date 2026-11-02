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
        internal int[] SplitMeshes(IReadOnlyDictionary<int,HashSet<int>> selection, Action beforeCommit = null, bool generatedBase = false, IReadOnlyDictionary<int,(Vector3 source,Vector3 extracted)> cutPivots = null)
        {
            var replacements = new Dictionary<int,Node>(); var additions = new List<Node>(); var result = new List<int>();
            var allocated = new List<Mesh>();
            void ShiftOrigin(Node node,Vector3 center)
            {
                var points=node.mesh.vertices;
                for(int v=0;v<points.Length;v++)points[v]-=center;
                node.mesh.vertices=points;node.mesh.RecalculateBounds();
                node.position+=node.rotation*Vector3.Scale(node.scale,center);
            }
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
                    // Ordinary Split Selection centers the new object on its bounds;
                    // Bisect supplies the center of each actual section plane.
                    var center = cutPivots!=null && cutPivots.TryGetValue(pair.Key,out var pivots) ? pivots.extracted : extracted.mesh.bounds.center;
                    ShiftOrigin(extracted,center);
                    var remaining = topology.ExtractSplitFaces(source,new HashSet<int>(Enumerable.Range(0,topology.Faces.Count).Where(f => !chosen.Contains(f))),true);
                    allocated.Add(remaining.mesh);
                    if(cutPivots!=null && cutPivots.TryGetValue(pair.Key,out var cut))ShiftOrigin(remaining,cut.source);
                    replacements.Add(pair.Key,remaining);
                    extracted.siblingOrder = -1; result.Add(nodes.Count + additions.Count); additions.Add(extracted);
                }
                if (result.Count == 0) return Array.Empty<int>();
                if (generatedBase)
                    foreach (var node in replacements.Values.Concat(additions))
                    {
                        node.modifiers = null; node.subdivision = new StudioSubdivisionSettings();
                        // Normal-group labels are identities, not allocation sizes.
                        // Compact generated outputs, including older sparse cap labels.
                        if (node.normalGroups != null)
                        {
                            var ids = new Dictionary<int,int>();
                            node.normalGroups = node.normalGroups.Select(id =>
                            { if (!ids.TryGetValue(id, out int dense)) ids.Add(id, dense = ids.Count); return dense; }).ToArray();
                        }
                    }
                beforeCommit?.Invoke();
                foreach (var mesh in allocated) AttachOwnedMesh(mesh);
                Undo.RegisterCompleteObjectUndo(this,"Split mesh selection");
                foreach (var pair in replacements)
                {
                    // Preserve the source node/hierarchy; only Bisect requests a pivot shift.
                    var source = nodes[pair.Key]; var remaining = pair.Value;
                    if(cutPivots!=null && cutPivots.TryGetValue(pair.Key,out var cut))
                    {
                        source.position=remaining.position;
                        foreach(var child in nodes.Where(n=>n.parent==pair.Key))child.position-=cut.source;
                    }
                    source.mesh = remaining.mesh; source.cutPolygons = remaining.cutPolygons;
                    if (generatedBase) { source.modifiers = null; source.subdivision = new StudioSubdivisionSettings(); }
                    source.cutTriangles = remaining.cutTriangles; source.normalGroups = remaining.normalGroups;source.geometryVertices=remaining.geometryVertices;
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
