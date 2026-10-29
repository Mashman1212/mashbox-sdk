using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.AnimationStudio
{
    // The asset owns every editable mesh. Preview objects are disposable views of this data.
    public sealed partial class StudioModelDocument : ScriptableObject
    {
        [Serializable]
        public sealed class Node
        {
            public string name;
            public string anchorType;
            public string sourcePath;
            public int parent = -1;
            public int siblingOrder = -1;
            public Vector3 position, scale = Vector3.one;
            // Parent-space placement of the frozen channel origin. Zero for existing assets.
            public Vector3 positionOffset;
            public Quaternion rotation = Quaternion.identity;
            [SerializeField] private Vector3 rotationChannels;
            [SerializeField] private bool rotationChannelsSet;
            internal Vector3 RotationChannels
            {
                get
                {
                    // A numeric edit owns its Euler representation, including turns
                    // beyond 360 degrees. Reconcile only after an external rotation.
                    if (!rotationChannelsSet || !SameRotation(Quaternion.Euler(rotationChannels), rotation))
                    { rotationChannels = rotation.eulerAngles; rotationChannelsSet = true; }
                    return rotationChannels;
                }
                set { rotationChannels = value; rotationChannelsSet = true; rotation = Quaternion.Euler(value); }
            }
            internal static bool SameRotation(Quaternion a, Quaternion b)
            {
                // Quaternion.Angle deliberately treats very small angles as zero.
                // Compare components so fine gizmo changes still refresh the hint.
                var difference = new Vector4(a.x-b.x, a.y-b.y, a.z-b.z, a.w-b.w);
                var opposite = new Vector4(a.x+b.x, a.y+b.y, a.z+b.z, a.w+b.w);
                return Mathf.Min(difference.sqrMagnitude, opposite.sqrMagnitude) < 1e-12f;
            }
            public Quaternion pivotRotation = Quaternion.identity;
            public Mesh mesh;
            [SerializeReference] public StudioBlendShape blendShape;
            public int[] cutPolygons, cutTriangles;
            public int[] normalGroups;
            public Material[] materials = Array.Empty<Material>();
            public int previewTextureSlice;
            public bool vertexAOBaked;
            public bool skinned, active = true;
            public int[] bones = Array.Empty<int>();
            public int rootBone = -1;
        }
        public string sourcePath;
        public StudioLightingSettings lighting = new StudioLightingSettings();
        public List<Node> nodes = new List<Node>();

        public static StudioModelDocument Import(GameObject source, string assetPath)
        {
            if (!source) throw new ArgumentException("Choose a model first.");
            var captured = CaptureNodes(source);
            var doc = CreateInstance<StudioModelDocument>();
            doc.sourcePath = AssetDatabase.GetAssetPath(source);
            doc.nodes = captured;
            try
            {
                AssetDatabase.CreateAsset(doc, assetPath);
                foreach (var node in doc.nodes) if (node.mesh) AssetDatabase.AddObjectToAsset(node.mesh, doc);
                EditorUtility.SetDirty(doc); AssetDatabase.SaveAssets();
                return doc;
            }
            catch
            {
                if (AssetDatabase.Contains(doc)) AssetDatabase.DeleteAsset(assetPath);
                else { foreach (var n in doc.nodes) if (n.mesh) DestroyImmediate(n.mesh); DestroyImmediate(doc); }
                throw;
            }
        }

        public int AppendModel(GameObject source)
        {
            if (!HasStorage) throw new InvalidOperationException("Save the workspace before adding a model.");
            var captured = CaptureNodes(source); int first = nodes.Count;
            captured[0].name = ObjectNames.GetUniqueName(nodes.Where(n => n.parent < 0).Select(n => n.name).ToArray(), captured[0].name);
            foreach (var node in captured)
            {
                if (node.parent >= 0) node.parent += first;
                for (int i = 0; i < node.bones.Length; i++) if (node.bones[i] >= 0) node.bones[i] += first;
                if (node.rootBone >= 0) node.rootBone += first;
            }
            try
            {
                foreach (var node in captured) if (node.mesh) AttachOwnedMesh(node.mesh);
                nodes.AddRange(captured); EditorUtility.SetDirty(this);
                // Import establishes a document baseline. Older document snapshots omit the
                // appended nodes, so retire only this document's incompatible history.
                Undo.ClearUndo(this);
                StudioSelectionUndoState.ImportBaseline(this);
                return first;
            }
            catch
            {
                if (nodes.Count > first) nodes.RemoveRange(first, nodes.Count - first);
                foreach (var node in captured) if (node.mesh) DestroyImmediate(node.mesh, true);
                throw;
            }
        }

        private static List<Node> CaptureNodes(GameObject source)
        {
            if (!source) throw new ArgumentException("Choose a model first.");
            var captured = new List<Node>();
            var transforms = source.GetComponentsInChildren<Transform>(true);
            try
            {
                foreach (var t in transforms)
                {
                    var skin = t.GetComponent<SkinnedMeshRenderer>();
                    var filter = t.GetComponent<MeshFilter>();
                    var renderer = t.GetComponent<Renderer>();
                    var mesh = skin ? skin.sharedMesh : filter ? filter.sharedMesh : null;
                    var node = new Node { name = t.name, parent = Array.IndexOf(transforms, t.parent),
                        position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
                        active = t.gameObject.activeSelf, skinned = skin,
                        materials = renderer ? renderer.sharedMaterials : Array.Empty<Material>() };
                    captured.Add(node);
                    if (!mesh && StudioAnchorCatalog.Contains(t.name)) node.anchorType = t.name;
                    if (mesh)
                    {
                        node.mesh = StudioWorkspaceText.ReadableSnapshot(mesh); node.mesh.name = mesh.name + " Editable";
                        // Fail before saving if an importer cannot supply CPU geometry.
                        var vertices = node.mesh.vertices;
                        CaptureMeshTopology(node, autoQuad: true);
                    }
                    if (skin)
                    {
                        node.bones = skin.bones.Select(b => Array.IndexOf(transforms, b)).ToArray();
                        node.rootBone = Array.IndexOf(transforms, skin.rootBone);
                        if (node.bones.Any(b => b < 0)) throw new InvalidOperationException("Import the complete model root so all skin bones are included.");
                    }
                }
                captured[0].sourcePath = AssetDatabase.GetAssetPath(source);
                return captured;
            }
            catch
            {
                foreach (var node in captured) if (node.mesh) DestroyImmediate(node.mesh);
                throw;
            }
        }

        public Transform[] Build(Scene scene)
        {
            var result = new Transform[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (string.IsNullOrEmpty(node.anchorType) && !node.mesh && StudioAnchorCatalog.Contains(node.name)) node.anchorType = node.name;
                if (!string.IsNullOrEmpty(node.anchorType)) node.name = node.anchorType;
                var go = new GameObject(string.IsNullOrEmpty(node.anchorType) ? node.name : node.anchorType); SceneManager.MoveGameObjectToScene(go, scene);
                result[i] = go.transform;
                ApplyTransform(i, result[i]);
                go.SetActive(node.active);
                if (!node.mesh) continue;
                Renderer renderer;
                if (node.skinned) renderer = go.AddComponent<SkinnedMeshRenderer>();
                else { go.AddComponent<MeshFilter>().sharedMesh = node.mesh; renderer = go.AddComponent<MeshRenderer>(); }
                renderer.sharedMaterials = node.materials;
            }
            // Reparenting can place a parent after its children in storage. Create all
            // transforms first, then establish links while retaining authored locals.
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].parent >= 0) result[i].SetParent(result[nodes[i].parent], false);
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i]; if (!node.skinned || !node.mesh) continue;
                var skin = result[i].GetComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = node.mesh;
                skin.bones = node.bones.Select(b => b >= 0 ? result[b] : null).ToArray();
                skin.rootBone = node.rootBone >= 0 ? result[node.rootBone] : null;
                skin.localBounds = node.mesh.bounds; skin.updateWhenOffscreen = true;
            }
            ApplyHierarchyOrder(result);
            return result;
        }
        public void ApplyTransform(int index, Transform target)
        {
            var n = nodes[index]; target.localPosition = n.positionOffset + n.position; target.localRotation = n.rotation; target.localScale = n.scale;
            TransformUtils.SetInspectorRotation(target, n.RotationChannels);
            target.localRotation = n.rotation; // Preserve the stored quaternion exactly.
        }
        public void ReadTransform(int index, Transform source)
        {
            var n = nodes[index]; n.position = source.localPosition - n.positionOffset;
            if (!Node.SameRotation(n.rotation, source.localRotation)) n.RotationChannels = TransformUtils.GetInspectorRotation(source);
            n.rotation = source.localRotation; n.scale = source.localScale;
            EditorUtility.SetDirty(this);
        }
        public bool IsBone(int index) => nodes.Any(n => n.skinned && (n.rootBone == index || n.bones.Contains(index)));

        // Move the object's origin while holding geometry and children in world space.
        internal void ValidateRepivot(int index, Transform[] transforms, Vector3 worldPosition)
        {
            if (IsBone(index)) throw new InvalidOperationException("Bone origins define the skin rig. Use Move for bones; pivot editing is available on mesh objects and groups.");
            if(nodes[index].blendShape!=null) throw new InvalidOperationException("Bake Current Pose before editing a blend shape pivot.");
            var t = transforms[index];
            // Translation only: retain the exact linear transform, including any inherited shear.
            var matrix = t.localToWorldMatrix; matrix.SetColumn(3, new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, 1));
            ValidateCompensation(index, transforms, matrix);
        }
        public void Repivot(int index, Transform[] transforms, Vector3 worldPosition)
        {
            ValidateRepivot(index, transforms, worldPosition);
            var t = transforms[index];
            var matrix = t.localToWorldMatrix; matrix.SetColumn(3, new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, 1));
            Compensate(index, transforms, matrix, () => t.position = worldPosition, "Move object pivot");
        }
        public void Freeze(int index, Transform[] transforms)
        {
            if (IsBone(index)) throw new InvalidOperationException("Freeze is unavailable on skin bones.");
            if(nodes[index].blendShape!=null) throw new InvalidOperationException("Bake Current Pose before freezing a blend shape.");
            var t = transforms[index];
            var placement = t.localPosition;
            var matrix = (t.parent ? t.parent.localToWorldMatrix : Matrix4x4.identity) * Matrix4x4.Translate(placement);
            Compensate(index, transforms, matrix, () => {
                nodes[index].positionOffset = placement;
                t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            }, "Freeze transforms");
        }
        private void Compensate(int index, Transform[] transforms, Matrix4x4 newWorld, Action change, string label)
        {
            var t = transforms[index];
            ValidateCompensation(index, transforms, newWorld);
            var childIndices = Enumerable.Range(0, nodes.Count).Where(i => nodes[i].parent == index).ToArray();
            var childMatrices = childIndices.Select(i => newWorld.inverse * transforms[i].localToWorldMatrix).ToArray();
            Undo.RegisterCompleteObjectUndo(this, label);
            var mesh = nodes[index].mesh;
            if (mesh)
            {
                Undo.RegisterCompleteObjectUndo(mesh, label);
                TransformMesh(mesh, newWorld.inverse * t.localToWorldMatrix);
            }
            change(); ReadTransform(index, t);
            for (int j = 0; j < childIndices.Length; j++)
            {
                var child = transforms[childIndices[j]]; var m = childMatrices[j];
                child.localPosition = m.GetColumn(3);
                var scale = new Vector3(((Vector3)m.GetColumn(0)).magnitude, ((Vector3)m.GetColumn(1)).magnitude, ((Vector3)m.GetColumn(2)).magnitude);
                if (m.determinant < 0) scale.x = -scale.x;
                child.localRotation = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
                child.localScale = scale; ReadTransform(childIndices[j], child);
            }
        }
        private void ValidateCompensation(int index, Transform[] transforms, Matrix4x4 newWorld)
        {
            if (Mathf.Abs(newWorld.determinant) < 1e-8f) throw new InvalidOperationException("Cannot edit a pivot under a zero scale.");
            var childIndices = Enumerable.Range(0, nodes.Count).Where(i => nodes[i].parent == index).ToArray();
            var childMatrices = childIndices.Select(i => newWorld.inverse * transforms[i].localToWorldMatrix).ToArray();
            foreach (var m in childMatrices)
            {
                var x = ((Vector3)m.GetColumn(0)).normalized; var y = ((Vector3)m.GetColumn(1)).normalized; var z = ((Vector3)m.GetColumn(2)).normalized;
                if (Mathf.Abs(Vector3.Dot(x, y)) + Mathf.Abs(Vector3.Dot(x, z)) + Mathf.Abs(Vector3.Dot(y, z)) > .001f)
                    throw new InvalidOperationException("Freezing this non-uniform scale would shear children. Freeze the mesh before rotating its children.");
            }
        }
        private static void TransformMesh(Mesh mesh, Matrix4x4 matrix)
        {
            var vertices = mesh.vertices; var normals = mesh.normals; var tangents = mesh.tangents;
            var normalMatrix = matrix.inverse.transpose;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            for (int i = 0; i < tangents.Length; i++) { var v = matrix.MultiplyVector(tangents[i]).normalized; tangents[i] = new Vector4(v.x, v.y, v.z, tangents[i].w * (matrix.determinant < 0 ? -1 : 1)); }
            var shapes = new List<(string name, float weight, Vector3[] v, Vector3[] n, Vector3[] t)>();
            for (int s = 0; s < mesh.blendShapeCount; s++) for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++)
            {
                var v = new Vector3[vertices.Length]; var n = new Vector3[vertices.Length]; var tan = new Vector3[vertices.Length];
                mesh.GetBlendShapeFrameVertices(s, f, v, n, tan);
                for (int i = 0; i < v.Length; i++) { v[i] = matrix.MultiplyVector(v[i]); n[i] = normalMatrix.MultiplyVector(n[i]); tan[i] = matrix.MultiplyVector(tan[i]); }
                shapes.Add((mesh.GetBlendShapeName(s), mesh.GetBlendShapeFrameWeight(s, f), v, n, tan));
            }
            mesh.vertices = vertices; mesh.normals = normals; mesh.tangents = tangents;
            var bindposes = mesh.bindposes;
            for (int i = 0; i < bindposes.Length; i++) bindposes[i] = bindposes[i] * matrix.inverse;
            mesh.bindposes = bindposes;
            mesh.ClearBlendShapes(); foreach (var s in shapes) mesh.AddBlendShapeFrame(s.name, s.weight, s.v, s.n, s.t);
            if (matrix.determinant < 0) for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                var tris = mesh.GetTriangles(sub); for (int i = 0; i < tris.Length; i += 3) { int a = tris[i]; tris[i] = tris[i + 1]; tris[i + 1] = a; }
                mesh.SetTriangles(tris, sub);
            }
            mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
        }

        public void Save()
        {
            if (!string.IsNullOrEmpty(workspaceFile)) { SaveWorkspaceFile(); return; }
            foreach (var mesh in nodes.Select(n => n.mesh).Where(m => m).Distinct())
            {
                if (AssetDatabase.Contains(this) && !AssetDatabase.Contains(mesh)) AssetDatabase.AddObjectToAsset(mesh, this);
                EditorUtility.SetDirty(mesh);
            }
            EditorUtility.SetDirty(this); AssetDatabase.SaveAssetIfDirty(this);
        }
        public static void SetUVs(Mesh mesh, int channel, IList<Vector2> values, string label)
        {
            if (channel < 0 || channel > 7 || values.Count != mesh.vertexCount) throw new ArgumentException("UV sets must contain one coordinate per vertex (UV 0–7).");
            Undo.RegisterCompleteObjectUndo(mesh, label); mesh.SetUVs(channel, values.ToList()); EditorUtility.SetDirty(mesh);
        }
    }
}
