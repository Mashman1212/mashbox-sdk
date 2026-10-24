using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioDeleteJointChecks
    {
        [MenuItem("MashBox/Animation/Validate skinned joint deletion")]
        static void Run()
        {
            var doc = ScriptableObject.CreateInstance<StudioModelDocument>();
            var mesh = new Mesh { name = "Skin deletion test" };
            Mesh repaired = null;
            try
            {
                doc.nodes.Add(new StudioModelDocument.Node { name = "root" });
                doc.nodes.Add(new StudioModelDocument.Node { name = "spine", parent = 0, position = Vector3.up });
                doc.nodes.Add(new StudioModelDocument.Node { name = "joint1", parent = 1, position = Vector3.up, rotation = Quaternion.Euler(20, 30, 0) });
                doc.nodes.Add(new StudioModelDocument.Node { name = "joint4", parent = 2, position = Vector3.right });
                mesh.vertices = new[] { Vector3.one, Vector3.up, Vector3.right };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
                mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = .3f, boneIndex1 = 1, weight1 = .7f }, 3).ToArray();
                doc.nodes.Add(new StudioModelDocument.Node { name = "pants", mesh = mesh, skinned = true, bones = new[] { 2, 3 }, rootBone = 2, parent = 0 });
                Func<int, Matrix4x4> world = null;
                world = i => { var n = doc.nodes[i]; return (n.parent < 0 ? Matrix4x4.identity : world(n.parent)) * Matrix4x4.TRS(n.position+n.positionOffset,n.rotation,n.scale); };
                var expected = new[] { world(2), world(3) };
                Undo.IncrementCurrentGroup(); doc.DeleteObjects(new[] { 2 });
                var skin = doc.nodes.Single(n => n.skinned); repaired = skin.mesh;
                if (doc.nodes.Count != 3 || skin.rootBone != 1 || skin.bones.Any(b => b != 1)) throw new Exception("Skin did not transfer to surviving spine.");
                for (int b = 0; b < 2; b++)
                {
                    var actual = world(1) * repaired.bindposes[b];
                    for (int c = 0; c < 16; c++) if (Mathf.Abs(actual[c] - expected[b][c]) > .0001f) throw new Exception("Current deformation changed.");
                }
                if (!mesh.boneWeights.SequenceEqual(repaired.boneWeights) || repaired == mesh) throw new Exception("Weights or original mesh changed.");
                Undo.PerformUndo();
                if (doc.nodes.Count != 5 || doc.nodes[4].mesh != mesh) throw new Exception("Undo did not restore original skin.");
                Undo.PerformRedo();
                if (doc.nodes.Count != 3 || doc.nodes[2].mesh != repaired) throw new Exception("Redo did not restore repaired skin.");
                doc.DeleteObjects(new[] { 0 });
                if (doc.nodes.Count != 0) throw new Exception("Whole-model deletion failed.");
                Debug.Log("StudioDeleteJointChecks: PASS — descendant deletion, skin transfer, deformation preservation, unchanged weights, Undo/Redo and whole-model deletion.");
            }
            finally { Undo.ClearUndo(doc); UnityEngine.Object.DestroyImmediate(doc); UnityEngine.Object.DestroyImmediate(mesh); if (repaired) UnityEngine.Object.DestroyImmediate(repaired); }
        }
    }
}
