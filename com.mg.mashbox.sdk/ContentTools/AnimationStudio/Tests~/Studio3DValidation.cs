using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class Studio3DValidation
    {
        static readonly List<string> Results = new List<string>();
        static void Check(bool value, string label) { if (!value) throw new Exception(label); Results.Add("PASS: " + label); Debug.Log(Results.Last()); }
        static void Near(Vector3 a, Vector3 b, string label) => Check(Vector3.Distance(a, b) < .0002f, label);
        public static void Show()
        {
            AnimationStudioWindow.Open();
            var window = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            window.GetType().GetMethod("SwitchStudioMode", flags).Invoke(window, new object[] { true });
            var document = AssetDatabase.LoadAssetAtPath<StudioModelDocument>("Assets/Test Model.asset");
            window.GetType().GetMethod("LoadStudioModel", flags).Invoke(window, new object[] { document });
            var editor = (StudioModelEditor)window.GetType().GetField("modelEditor", flags).GetValue(window);
            editor.SelectObject(1);
        }
        public static void Run()
        {
            try
            {
                var source = new GameObject("StudioModel");
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "EditableCube"; cube.transform.SetParent(source.transform, false);
                cube.transform.localPosition = new Vector3(2, 1, 0); cube.transform.localRotation = Quaternion.Euler(15, 25, 0); cube.transform.localScale = Vector3.one * 1.5f;
                var child = new GameObject("Child"); child.transform.SetParent(cube.transform, false); child.transform.localPosition = new Vector3(.25f, 1, .4f);
                var originalMesh = cube.GetComponent<MeshFilter>().sharedMesh;
                var doc = StudioModelDocument.Import(source, AssetDatabase.GenerateUniqueAssetPath("Assets/Test Model.asset"));
                Check(doc.nodes.Count == 3 && doc.nodes[2].parent == 1, "Import preserves child hierarchy");
                Check(doc.nodes[1].mesh != originalMesh && AssetDatabase.IsSubAsset(doc.nodes[1].mesh), "Working copy owns meshes without changing source geometry");
                var editor = new StudioModelEditor(() => {}); editor.Load(doc); editor.SelectObject(1);
                var preview = editor.Transforms; var mesh = doc.nodes[1].mesh;
                var before = mesh.vertices.Select(v => preview[1].TransformPoint(v)).ToArray();
                var childBefore = preview[2].localToWorldMatrix;
                Undo.IncrementCurrentGroup();
                doc.Repivot(1, preview, new Vector3(5, 3, -2));
                for (int i = 0; i < before.Length; i++) Near(before[i], preview[1].TransformPoint(mesh.vertices[i]), "Repivot holds world vertex " + i);
                Near(preview[2].position, childBefore.GetColumn(3), "Repivot holds child world position");
                Undo.PerformUndo(); preview = editor.Transforms;
                Near(preview[1].position, cube.transform.position, "Undo restores object origin");
                Check(mesh.vertices.SequenceEqual(originalMesh.vertices), "Undo restores mesh data");
                Undo.PerformRedo(); preview = editor.Transforms;
                Near(preview[1].position, new Vector3(5, 3, -2), "Redo restores edited pivot");
                before = mesh.vertices.Select(v => preview[1].TransformPoint(v)).ToArray(); childBefore = preview[2].localToWorldMatrix;
                doc.Freeze(1, preview);
                Check(doc.nodes[1].position == Vector3.zero && doc.nodes[1].scale == Vector3.one && Quaternion.Angle(doc.nodes[1].rotation, Quaternion.identity) < .001f, "Freeze resets local transform");
                for (int i = 0; i < before.Length; i++) Near(before[i], preview[1].TransformPoint(mesh.vertices[i]), "Freeze holds world vertex " + i);
                Near(preview[2].position, childBefore.GetColumn(3), "Freeze holds child world position");
                for (int channel = 0; channel < 8; channel++)
                {
                    var uv = Enumerable.Range(0, mesh.vertexCount).Select(i => new Vector2((i % 4) * .2f + channel * .01f, (i / 4) * .1f)).ToArray();
                    StudioModelDocument.SetUVs(mesh, channel, uv, "Test UV set");
                }
                Undo.IncrementCurrentGroup(); var previousUV = mesh.uv;
                StudioModelDocument.SetUVs(mesh, 0, previousUV.Select(v => v + Vector2.one).ToArray(), "Move UVs");
                Undo.PerformUndo(); Check(mesh.uv.SequenceEqual(previousUV), "UV coordinate edit supports Undo");
                doc.Save(); string path = AssetDatabase.GetAssetPath(doc);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                doc = AssetDatabase.LoadAssetAtPath<StudioModelDocument>(path); mesh = doc.nodes[1].mesh;
                for (int i = 0; i < 8; i++) { var uv = new List<Vector2>(); mesh.GetUVs(i, uv); Check(uv.Count == mesh.vertexCount, "UV " + i + " persists in working asset"); }
                editor.Load(doc); editor.SelectObject(1);
                string fbx = Path.GetFullPath("Assets/StudioRoundTrip.fbx");
                StudioModelEditor.ExportTo(fbx, editor.Transforms[0].gameObject);
                AssetDatabase.ImportAsset("Assets/StudioRoundTrip.fbx", ImportAssetOptions.ForceSynchronousImport);
                var imported = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/StudioRoundTrip.fbx");
                Check(imported && imported.transform.Find("EditableCube/Child"), "FBX round-trip preserves hierarchy");
                var exportedMesh = imported.GetComponentInChildren<MeshFilter>().sharedMesh;
                Check(exportedMesh.vertexCount > 0, "FBX round-trip contains edited mesh geometry");
                for (int i = 0; i < 8; i++) { var uv = new List<Vector2>(); exportedMesh.GetUVs(i, uv); Check(uv.Count == exportedMesh.vertexCount, "FBX round-trip preserves UV " + i); }
                Check(StudioModelEditor.RayTriangle(new Ray(new Vector3(0, 0, -2), Vector3.forward), new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(0, 1, 0), out float distance) && Mathf.Abs(distance - 2) < .001f, "Viewport triangle picking returns nearest surface distance");
                var privateFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(StudioModelEditor).GetField("mode", privateFlags).SetValue(editor, StudioModelEditor.ComponentMode.Vertex);
                editor.Components.Add(0);
                Check(editor.SelectedVertices().Count >= 3, "Vertex selection includes coincident vertices across cube seams");
                typeof(StudioModelEditor).GetField("mode", privateFlags).SetValue(editor, StudioModelEditor.ComponentMode.Edge);
                Check(editor.SelectedVertices().Count >= 2, "Edge selection resolves to editable mesh vertices");
                typeof(StudioModelEditor).GetField("mode", privateFlags).SetValue(editor, StudioModelEditor.ComponentMode.Face);
                Check(editor.SelectedVertices().Count >= 3, "Face selection resolves to editable triangle vertices");
                var sparse = Object.Instantiate(originalMesh);
                for (int i = 0; i < 8; i++) sparse.SetUVs(i, new List<Vector2>());
                sparse.SetUVs(4, Enumerable.Repeat(new Vector2(.3f, .7f), sparse.vertexCount).ToArray());
                StudioModelEditor.PadUVChannels(sparse);
                for (int i = 0; i <= 4; i++) { var uv = new List<Vector2>(); sparse.GetUVs(i, uv); Check(uv.Count == sparse.vertexCount, "Sparse export provides UV channel " + i); }
                Object.DestroyImmediate(sparse);
                editor.Dispose(); Object.DestroyImmediate(source);
                var skinRoot = new GameObject("SkinRoot"); var bone = new GameObject("Bone"); bone.transform.SetParent(skinRoot.transform, false);
                var skinObject = new GameObject("SkinMesh"); skinObject.transform.SetParent(skinRoot.transform, false);
                var skin = skinObject.AddComponent<SkinnedMeshRenderer>(); var skinMesh = Object.Instantiate(originalMesh);
                skinMesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, skinMesh.vertexCount).ToArray();
                skinMesh.bindposes = new[] { bone.transform.worldToLocalMatrix * skinObject.transform.localToWorldMatrix };
                skin.sharedMesh = skinMesh; skin.bones = new[] { bone.transform }; skin.rootBone = bone.transform;
                var skinDoc = StudioModelDocument.Import(skinRoot, AssetDatabase.GenerateUniqueAssetPath("Assets/Skin Model.asset"));
                var skinEditor = new StudioModelEditor(() => {}); skinEditor.Load(skinDoc); skinEditor.SelectObject(2);
                Check(skinEditor.CanEditComponents(), "Skinned mesh can edit components in bind pose");
                var bakedBefore = new Mesh(); skinEditor.Transforms[2].GetComponent<SkinnedMeshRenderer>().BakeMesh(bakedBefore);
                skinDoc.Repivot(2, skinEditor.Transforms, new Vector3(2, 1, -1));
                var bakedAfter = new Mesh(); skinEditor.Transforms[2].GetComponent<SkinnedMeshRenderer>().BakeMesh(bakedAfter);
                for (int i = 0; i < bakedBefore.vertexCount; i++) Near(bakedBefore.vertices[i], skinEditor.Transforms[2].TransformPoint(bakedAfter.vertices[i]), "Skin pivot holds baked world vertex " + i);
                Check(skinEditor.CanEditComponents(), "Skin pivot compensation keeps bind-pose component editing valid");
                skinEditor.Transforms[1].localPosition = Vector3.up;
                Check(!skinEditor.CanEditComponents(), "Posed bones block incorrect component handles");
                bool rejected = false; try { skinDoc.Repivot(1, skinEditor.Transforms, Vector3.one); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Bone-origin repivot is explicitly blocked");
                skinEditor.Dispose(); Object.DestroyImmediate(skinRoot); Object.DestroyImmediate(skinMesh); Object.DestroyImmediate(bakedBefore); Object.DestroyImmediate(bakedAfter);
                var window = ScriptableObject.CreateInstance<AnimationStudioWindow>();
                typeof(AnimationStudioWindow).GetMethod("OpenViewport", privateFlags).Invoke(window, null);
                typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", privateFlags).Invoke(window, new object[] { true });
                typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", privateFlags).Invoke(window, new object[] { doc });
                string dataBefore = EditorJsonUtility.ToJson(doc);
                typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", privateFlags).Invoke(window, new object[] { false });
                typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", privateFlags).Invoke(window, new object[] { true });
                Check(dataBefore == EditorJsonUtility.ToJson(doc), "Switching Animation / Modeling preserves working model data");
                foreach (var view in Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>()) view.Close();
                Object.DestroyImmediate(window);
                File.WriteAllLines(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "studio3d-result.txt"), Results);
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
