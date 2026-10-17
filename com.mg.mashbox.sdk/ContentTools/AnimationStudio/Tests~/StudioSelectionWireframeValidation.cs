using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioSelectionWireframeValidation
    {
        static readonly List<string> Results = new List<string>();
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); }
        static void MatchesRenderer(StudioModelEditor editor, int index, string label)
        {
            var renderer = editor.Transforms[index].GetComponent<MeshRenderer>();
            var mesh = editor.Transforms[index].GetComponent<MeshFilter>().sharedMesh;
            var world = mesh.vertices.Select(renderer.localToWorldMatrix.MultiplyPoint3x4).ToArray();
            var points = editor.SelectionWireframeLines(index).Select(editor.Transforms[index].localToWorldMatrix.MultiplyPoint3x4).ToArray();
            Check(points.Length > 0 && points.All(p => world.Any(v => (p - v).sqrMagnitude < 1e-8f)), label);
        }
        public static void Run()
        {
            StudioModelEditor editor = null;
            try
            {
                var doc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Selection Wireframe.asset"));
                doc.nodes.Add(new StudioModelDocument.Node { name = "Group", rotation = Quaternion.Euler(10, 20, 30), scale = new Vector3(2, 1, .7f) });
                int index = doc.AddPrimitive(PrimitiveType.Cube); doc.nodes[index].parent = 0;
                editor = new StudioModelEditor(() => {}); editor.Load(doc); editor.SelectObject(index);
                var lines = editor.SelectionWireframeLines(index);
                Check(lines.Length == 24, "Cube outline contains its 12 polygon edges, without triangulation diagonals or duplicated UV-seam edges");
                MatchesRenderer(editor, index, "Outline endpoints match rendered vertices under a scaled and rotated parent");
                editor.Transforms[index].localRotation = Quaternion.Euler(23, 40, 16);
                editor.Transforms[index].localScale = new Vector3(.6f, 1.7f, 1.2f);
                editor.Transforms[index].localPosition = new Vector3(1, .2f, -.8f);
                doc.ReadTransform(index, editor.Transforms[index]); editor.Changed(false);
                Check(ReferenceEquals(lines, editor.SelectionWireframeLines(index)), "Moving, rotating and scaling objects reuse the cached outline");
                MatchesRenderer(editor, index, "Rotated nonuniform scale and inherited shear preserve edge alignment");
                var originalWorldLines = lines.Select(editor.Transforms[index].TransformPoint).ToArray();
                Undo.IncrementCurrentGroup(); doc.Repivot(index, editor.Transforms, new Vector3(4, -3, 2)); editor.Changed();
                var pivotLines = editor.SelectionWireframeLines(index).Select(editor.Transforms[index].TransformPoint).ToArray();
                bool sameEdges = originalWorldLines.Length == pivotLines.Length;
                for (int i = 0; i < originalWorldLines.Length; i += 2)
                {
                    bool found = false;
                    for (int j = 0; j < pivotLines.Length; j += 2)
                        if (((originalWorldLines[i] - pivotLines[j]).sqrMagnitude < 1e-8f && (originalWorldLines[i + 1] - pivotLines[j + 1]).sqrMagnitude < 1e-8f) ||
                            ((originalWorldLines[i] - pivotLines[j + 1]).sqrMagnitude < 1e-8f && (originalWorldLines[i + 1] - pivotLines[j]).sqrMagnitude < 1e-8f)) found = true;
                    sameEdges &= found;
                }
                Check(sameEdges, "Moving the pivot leaves the actual world-space edge outline fixed");
                MatchesRenderer(editor, index, "Pivot-adjusted outline matches the mesh");
                Undo.PerformUndo(); MatchesRenderer(editor, index, "Undo restores matching rendered edges");
                Undo.PerformRedo(); MatchesRenderer(editor, index, "Redo restores matching rendered edges");
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(editor.ActiveMesh, "Edit test vertex");
                var vertices = editor.ActiveMesh.vertices; vertices[0] += new Vector3(.2f, .3f, .4f); editor.ActiveMesh.vertices = vertices; editor.ActiveMesh.RecalculateBounds(); editor.Changed();
                MatchesRenderer(editor, index, "Component edits refresh cached outline positions");
                Undo.PerformUndo(); MatchesRenderer(editor, index, "Component Undo refreshes cached outline positions");
                editor.SelectObject(0); editor.SelectObject(index, true);
                Check(editor.SelectedWireframeNodes().SequenceEqual(new[] { index }), "Selected parent and child draw the child mesh only once");
                editor.Transforms[index].gameObject.SetActive(false);
                Check(!editor.SelectedWireframeNodes().Any(), "Hidden children have no selection outline");
                editor.Dispose(); editor = null;

                var skinDoc = StudioModelDocument.CreateEmpty(AssetDatabase.GenerateUniqueAssetPath("Assets/Skin Wireframe.asset"));
                skinDoc.nodes.Add(new StudioModelDocument.Node { name = "Bone" });
                int skinIndex = skinDoc.AddPrimitive(PrimitiveType.Cube); var node = skinDoc.nodes[skinIndex];
                node.skinned = true; node.rootBone = 0; node.bones = new[] { 0 };
                node.mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, node.mesh.vertexCount).ToArray();
                node.mesh.bindposes = new[] { Matrix4x4.identity };
                editor = new StudioModelEditor(() => {}); editor.Load(skinDoc); editor.SelectObject(skinIndex);
                var unposed = (Vector3[])editor.SelectionWireframeLines(skinIndex).Clone();
                editor.Transforms[0].position = Vector3.up;
                var posed = editor.SelectionWireframeLines(skinIndex);
                Check(unposed.Zip(posed, (a, b) => (b - a - Vector3.up).sqrMagnitude < 1e-8f).All(x => x), "Skinned selection outline follows posed vertices");
                int beforeDispose = Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m.name == "MashBox Studio selection preview");
                editor.Dispose(); editor = null;
                Check(beforeDispose > 0 && !Resources.FindObjectsOfTypeAll<Mesh>().Any(m => m.name == "MashBox Studio selection preview"), "Disposal releases cached skin preview meshes");
                File.WriteAllLines("selection-wireframe-result.txt", Results); EditorApplication.Exit(0);
            }
            catch(Exception e) { Results.Add("FAIL: " + e); File.WriteAllLines("selection-wireframe-result.txt", Results); Debug.LogException(e); EditorApplication.Exit(1); }
            finally { editor?.Dispose(); }
        }
    }
}
