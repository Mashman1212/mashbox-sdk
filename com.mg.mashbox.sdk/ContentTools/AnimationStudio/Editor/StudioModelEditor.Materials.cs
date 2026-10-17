using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool HandleMaterialDrop(SceneView view)
        {
            var evt = Event.current;
            var payload = DragAndDrop.objectReferences;
            int node = -1, slot = -1;
            bool overCanvas = view && (!(view is AnimationStudioViewport studio) ||
                (studio.ModelCanvasRect.Contains(evt.mousePosition) && !studio.IsOverViewportControls(evt.mousePosition)));
            bool supported = payload.Length == 1 && payload[0] is Material && overCanvas &&
                TryMaterialTarget(view.camera, evt.mousePosition, EditorGUIUtility.pixelsPerPoint, out node, out slot);
            DragAndDrop.visualMode = supported ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            if (supported && evt.type == EventType.DragPerform)
            {
                var material = (Material)payload[0];
                DragAndDrop.AcceptDrag();
                Run(() => AssignMaterial(node, slot, material));
            }
            // Consume misses too: Unity must never assign to unrelated main-scene objects.
            evt.Use(); return true;
        }

        internal bool TryMaterialTarget(Camera camera, Vector2 mouse, float dpi, out int nodeIndex, out int materialSlot)
        {
            nodeIndex = materialSlot = -1;
            if (!camera || !Document) return false;
            var ray = camera.ScreenPointToRay(new Vector3(mouse.x * dpi, camera.pixelHeight - mouse.y * dpi));
            float nearest = float.PositiveInfinity;
            for (int n = 0; n < Transforms.Length; n++)
            {
                var mesh = Document.nodes[n].mesh;
                var t = Transforms[n];
                if (!mesh || !t.gameObject.activeInHierarchy) continue;
                var renderer = t.GetComponent<Renderer>();
                if (!renderer || !renderer.enabled || !renderer.bounds.IntersectRay(ray)) continue;
                var vertices = PickVertices(n);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var topology = mesh.GetTopology(sub);
                    if (topology != MeshTopology.Triangles && topology != MeshTopology.Quads) continue;
                    var indices = mesh.GetIndices(sub);
                    int stride = topology == MeshTopology.Quads ? 4 : 3;
                    for (int i = 0; i + stride <= indices.Length; i += stride)
                        for (int corner = 1; corner < stride - 1; corner++)
                        {
                            if (!RayTriangle(ray, t.TransformPoint(vertices[indices[i]]),
                                t.TransformPoint(vertices[indices[i + corner]]), t.TransformPoint(vertices[indices[i + corner + 1]]),
                                out float distance) || distance >= nearest) continue;
                            nearest = distance; nodeIndex = n; materialSlot = sub;
                        }
                }
            }
            return nodeIndex >= 0;
        }

        private void AssignMaterial(int nodeIndex, int slot, Material material)
        {
            var node = Document.nodes[nodeIndex];
            var existing = node.materials ?? Array.Empty<Material>();
            if (slot < existing.Length && existing[slot] == material) return;
            var materials = new Material[Math.Max(existing.Length, node.mesh.subMeshCount)];
            Array.Copy(existing, materials, existing.Length);
            // Fill missing slots without disturbing existing assignments.
            for (int i = existing.Length; i < materials.Length; i++)
                materials[i] = existing.Length > 0 ? existing[existing.Length - 1] : null;
            materials[slot] = material;
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(Document, "Assign material");
            node.materials = materials;
            Transforms[nodeIndex].GetComponent<Renderer>().sharedMaterials = materials;
            ApplyDisplayMaterials();
            Changed(false);
        }
    }
}
