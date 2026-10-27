using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed class StudioSkinningDocument : ScriptableObject
    {
        public enum MeshRole { Clothing, Bust, Body, Headgear }
        [Serializable] public sealed class StitchWeights
        {
            public int vertex;
            public int[] bones;
            public float[] weights;
        }
        [Serializable] public sealed class Part
        {
            public string name;
            public Mesh mesh;
            public Material[] materials;
            public bool bound, visible = true;
            public bool referenceOnly;
            public bool preserveStitchedBindPose;
            // Actual stitched seam indices, including UV-split duplicates. Serialized
            // with the workspace and Undo; bind-pose provenance alone is not a stitch.
            public int[] stitchedVertices;
            public int stitchedVertexCount;
            public StitchWeights[] stitchedWeights;
            public bool HasStitchWeightLock => bound && mesh && stitchedVertexCount == mesh.vertexCount && stitchedVertices != null && stitchedVertices.Length > 0;
            public bool IsStitchVertex(int vertex) => HasStitchWeightLock && Array.BinarySearch(stitchedVertices,vertex) >= 0;
            internal void CaptureStitchWeights()
            {
                var skin = new StudioSkinWeights(mesh);
                stitchedWeights = stitchedVertices.Select(v => new StitchWeights {
                    vertex=v, bones=skin.Values[v].Select(w=>w.boneIndex).ToArray(),
                    weights=skin.Values[v].Select(w=>w.weight).ToArray() }).ToArray();
            }
            internal void RestoreStitchWeights(StudioSkinWeights skin)
            {
                if (!HasStitchWeightLock || stitchedWeights == null) return;
                foreach(var seam in stitchedWeights)
                {
                    var values=skin.Values[seam.vertex]; values.Clear();
                    for(int i=0;i<seam.bones.Length;i++)
                        values.Add(new BoneWeight1 {boneIndex=seam.bones[i],weight=seam.weights[i]});
                }
            }
            public string defaultReferenceLabel;
            public MeshRole role;
            public Quaternion objectRotation = Quaternion.identity;
            public Mesh sourceMesh;
            public string sourceRendererPath;
            public Matrix4x4 sourceToWorkspace = Matrix4x4.identity;
        }
        public GameObject skeleton;
        public List<Part> parts = new List<Part>();
        public bool dirty;
        [HideInInspector] public string tutorialId;
        [HideInInspector] public List<string> tutorialActions = new List<string>();
        public int sculptSymmetryAxis; // 0 off; 1 X; 2 Y; 3 Z
        public bool sculptSymmetryLocal;
        public bool grabVolume = true;
        public int selected;
        public string[] bonePaths;

        internal static string EnsureWorkspaceFolder()
        {
            const string path = "Assets/Workspaces";
            if (!AssetDatabase.IsValidFolder(path))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Application.dataPath, "Workspaces"));
                AssetDatabase.Refresh();
            }
            return path;
        }

        internal bool SaveDocument()
        {
            string path = AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject("Save skinning workspace", "Skinning Workspace", "asset", "Save editable geometry and skin weights inside Assets.", EnsureWorkspaceFolder());
                if (string.IsNullOrEmpty(path)) return false;
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                hideFlags = HideFlags.None; AssetDatabase.CreateAsset(this, path);
            }
            foreach (var part in parts)
                if (part.mesh && !EditorUtility.IsPersistent(part.mesh))
                { part.mesh.hideFlags = HideFlags.None; AssetDatabase.AddObjectToAsset(part.mesh, this); }
            dirty = false; EditorUtility.SetDirty(this); AssetDatabase.SaveAssetIfDirty(this);
            return true;
        }

        internal static string BoneName(string name) => name.Split(':').Last();
        internal static Mesh CreateEditableCopy(Mesh original)
        {
            // Model Read/Write controls runtime CPU retention. The editor still
            // owns the imported mesh data; change only our detached working copy.
            // Cloning preserves every vertex stream, variable skin influence,
            // submesh and blend shape without rebuilding or reimporting the source.
            var copy = Instantiate(original);
            try
            {
                copy.name = original.name + " Skin Edit";
                copy.hideFlags = HideFlags.HideAndDontSave;
                if (!copy.isReadable)
                {
                    using (var serialized = new SerializedObject(copy))
                    {
                        var readable = serialized.FindProperty("m_IsReadable");
                        if (readable == null)
                            throw new InvalidOperationException("Unity could not prepare an editable copy of " + original.name + ".");
                        readable.boolValue = true;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                if (!copy.isReadable || copy.vertices.Length != original.vertexCount)
                    throw new InvalidOperationException("Unity could not read the mesh data for " + original.name + ". Try reimporting its source model and dropping it again.");
                return copy;
            }
            catch { DestroyImmediate(copy); throw; }
        }
        internal void Import(GameObject source, Transform[] bones, Matrix4x4[] rest)
        {
            if (!source) throw new ArgumentException("Drop a model or prefab from the Project browser.");
            var additions = new List<Part>();
            try
            {
                foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
                {
                    var skin = renderer as SkinnedMeshRenderer;
                    var filter = renderer.GetComponent<MeshFilter>();
                    var original = skin ? skin.sharedMesh : filter ? filter.sharedMesh : null;
                    if (!original) continue;
                    var copy = CreateEditableCopy(original);
                    var part = new Part { name = renderer.name, mesh = copy, materials = renderer.sharedMaterials };
                    additions.Add(part);
                    var matrix = source.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    part.sourceMesh = original;
                    part.sourceToWorkspace = matrix;
                    var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(original));
                    if (sourceModel)
                    {
                        var matches = sourceModel.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh == original).ToArray();
                        if (matches.Length == 1) part.sourceRendererPath = AnimationUtility.CalculateTransformPath(matches[0].transform, sourceModel.transform);
                    }
                    TransformGeometry(copy, matrix);
                    var imported = new StudioSkinWeights(copy);
                    if (skin && imported.HasWeights)
                    {
                        var sourceBones = skin.bones;
                        var map = new int[sourceBones.Length];
                        bool compatible = true;
                        for (int b = 0; b < map.Length; b++)
                        {
                            var matches = sourceBones[b] ? Enumerable.Range(0, bones.Length).Where(i => BoneName(bones[i].name) == BoneName(sourceBones[b].name)).ToArray() : Array.Empty<int>();
                            map[b] = matches.Length == 1 ? matches[0] : -1;
                        }
                        foreach (var list in imported.Values)
                            foreach (var weight in list)
                                if (weight.weight > 0 && (weight.boneIndex >= map.Length || map[weight.boneIndex] < 0)) compatible = false;
                        if (compatible)
                        {
                            var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(original)) as ModelImporter;
                            var workspaceAvatar=skeleton?skeleton.GetComponent<Animator>()?.avatar:null;
                            // Copy From Other explicitly identifies the same rig.
                            // Its displayed pose must not be baked into the binding,
                            // regardless of whether it is a locked reference or a copy.
                            bool sharedAvatar=workspaceAvatar && importer &&
                                importer.animationType==ModelImporterAnimationType.Human &&
                                importer.avatarSetup==ModelImporterAvatarSetup.CopyFromOther && importer.sourceAvatar==workspaceAvatar;
                            bool preservedBinding=sharedAvatar || StudioStitchedEditorPose.HasPreservedBinding(original);
                            // Preserve each influence and imported bind transform. A
                            // corrected bind matrix compensates different source rest axes.
                            var binds = rest.Select(r => r.inverse).ToArray();
                            var oldBind = copy.bindposes;
                            for (int b = 0; b < map.Length; b++) if (map[b] >= 0 && b < oldBind.Length && sourceBones[b])
                            {
                                var sourceRest = source.transform.worldToLocalMatrix * sourceBones[b].localToWorldMatrix;
                                // Our exported display pose is not its bind pose.
                                // Reapplying source-pose compensation breaks the stitched seam.
                                binds[map[b]] = preservedBinding ? oldBind[b] * matrix.inverse
                                    : rest[map[b]].inverse * sourceRest * oldBind[b] * matrix.inverse;
                            }
                            imported.Remap(map); imported.Apply(copy); copy.bindposes = binds; part.bound = true;
                            part.preserveStitchedBindPose=preservedBinding;
                        }
                    }
                }
                if (additions.Count == 0) throw new InvalidOperationException("This object contains no meshes. Drop a model or a prefab containing a mesh.");
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(this, "Import skinning meshes");
                foreach (var part in additions)
                    if (EditorUtility.IsPersistent(this)) { part.mesh.hideFlags = HideFlags.None; AssetDatabase.AddObjectToAsset(part.mesh, this); }
                parts.AddRange(additions); selected = parts.Count - additions.Count; dirty = true;
                EditorUtility.SetDirty(this);
            }
            catch { foreach (var part in additions) if (!EditorUtility.IsPersistent(part.mesh)) DestroyImmediate(part.mesh); throw; }
        }

        internal static void TransformGeometry(Mesh mesh, Matrix4x4 matrix)
        {
            var vertices = mesh.vertices; var normals = mesh.normals; var tangents = mesh.tangents;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            var normalMatrix = matrix.inverse.transpose;
            for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            for (int i = 0; i < tangents.Length; i++) { var t = matrix.MultiplyVector(tangents[i]).normalized; tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w); }
            var shapes = new List<(string name, float weight, Vector3[] v, Vector3[] n, Vector3[] t)>();
            for (int s = 0; s < mesh.blendShapeCount; s++) for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++)
            {
                var v = new Vector3[vertices.Length]; var n = new Vector3[v.Length]; var t = new Vector3[v.Length];
                mesh.GetBlendShapeFrameVertices(s, f, v, n, t);
                for (int i = 0; i < v.Length; i++) { v[i] = matrix.MultiplyVector(v[i]); n[i] = normalMatrix.MultiplyVector(n[i]); t[i] = matrix.MultiplyVector(t[i]); }
                shapes.Add((mesh.GetBlendShapeName(s), mesh.GetBlendShapeFrameWeight(s, f), v, n, t));
            }
            mesh.vertices = vertices; mesh.normals = normals; mesh.tangents = tangents;
            mesh.ClearBlendShapes(); foreach (var shape in shapes) mesh.AddBlendShapeFrame(shape.name, shape.weight, shape.v, shape.n, shape.t);
            if (matrix.determinant < 0)
                for (int s = 0; s < mesh.subMeshCount; s++)
                { var tris = mesh.GetTriangles(s); for (int i = 0; i < tris.Length; i += 3) { int x = tris[i]; tris[i] = tris[i + 1]; tris[i + 1] = x; } mesh.SetTriangles(tris, s); }
            mesh.RecalculateBounds();
        }
    }
}
