using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

// Disposable BmxAnimatorValidation host supplies model import/rig preview adapters.
// Assembly creation, joint binding, live solving, take evaluation and baking are production code.
public static class StudioBmxAnimatorValidation
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/BmxAnimatorValidation/Assets"))
            throw new InvalidOperationException("Use the disposable BmxAnimatorValidation project.");
        try
        {
            var parts = AuthoringRig.BuildFullBmxOutfit();
            foreach (var name in new[] { "Grip", "Bar End", "CrankArm", "Pedal", "Rim", "Spokes", "Nipples", "Tire" })
                Check(parts.Count(p => string.Equals(p.source.name.Split('_')[1], name, StringComparison.OrdinalIgnoreCase)) == 2, "Expected a pair: " + name);
            Check(parts.Count(p => p.source.name.Contains("Stem Bolt")) == 6, "Six stem bolts");
            Check(!parts.Any(p => p.source.name.Contains("_Mag_")), "Exclude mag wheels");
            Check(parts.Count(p => p.joint == "LeftPedal_Joint") == 1 && parts.Count(p => p.joint == "RightPedal_Joint") == 1, "Independent pedals");
            var frame = parts.Single(p => p.source.name.Contains("_Frame_"));
            Check(Vector3.Distance(frame.position, new Vector3(0, .6245f, .3144f)) < 1e-5f, "Modeler frame rest placement");
            var skeleton = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Bike Skeleton.fbx");
            using (var rig = new AuthoringRig(skeleton, null))
            {
                int count = rig.Bones.Length;
                rig.BuildVehicleParts(parts);
                var visuals = rig.Root.GetComponentsInChildren<Transform>().Where(t => t.name.EndsWith(" · Preview")).ToArray();
                Check(visuals.Length == parts.Count && rig.Bones.Length == count, "Stable bone paths and complete assembly");
                var bars = visuals.Single(t => t.name.Contains("_Bars_"));
                var rear = visuals.Single(t => t.name.Contains("_Rear Hub_"));
                var before = bars.position; var fixedRear = rear.position;
                var headset = visuals.Single(t => t.name.Contains("_Headset_"));
                var headsetPosition = headset.position;
                var headsetRest = headset.localRotation;
                var barsJoint = rig.Bones.Single(b => b.name.Split(':').Last() == "Bars_Joint");
                barsJoint.localRotation *= Quaternion.Euler(0, 30, 0);
                rig.Apply(rig.Capture());
                Check(Vector3.Distance(headset.position, headsetPosition) < 1e-5f, "Headset rotates at its SDK anchor without orbiting the skeleton pivot");
                Check(Quaternion.Angle(headset.localRotation, headsetRest * Quaternion.Euler(0, 30, 0)) < .01f, "Headset follows local yaw after snapping");
                Check(Vector3.Distance(before, bars.position) > .001f, "Steering drives bars");
                Check(Vector3.Distance(fixedRear, rear.position) < 1e-5f, "Steering leaves rear wheel alone");
                Check(Vector3.Distance(barsJoint.position, headset.position) < 1e-5f, "Bars joint follows headset position");
                var headsetSocket = headset.parent;
                var socketRotation = headsetSocket.localRotation;
                var socketPosition = headsetSocket.localPosition;
                headsetSocket.localRotation *= Quaternion.Euler(12, 0, 9);
                headsetSocket.localPosition += new Vector3(.01f, .02f, -.01f);
                rig.Apply(rig.Capture());
                Check(Vector3.Angle(barsJoint.up, headset.up) < .01f, "Bars pitch and roll follow tilted headset");
                Check(Vector3.Distance(barsJoint.position, headset.position) < 1e-5f, "Bars follow moved headset socket");
                var steeringRotation = headset.localRotation;
                var barsRotation = barsJoint.rotation;
                for (int repeat = 0; repeat < 20; repeat++) rig.Apply(rig.Capture());
                Check(Quaternion.Angle(headset.localRotation, steeringRotation) < .01f && Quaternion.Angle(barsJoint.rotation, barsRotation) < .01f,
                    "Headset tilt preserves steering yaw without feedback across captures");
                headsetSocket.localRotation = socketRotation; headsetSocket.localPosition = socketPosition;
                rig.Apply(rig.Capture());
                var bb = visuals.Single(t => t.name.Contains("_BB_"));
                var bbRest = bb.localRotation; var bbPosition = bb.position;
                var drive = rig.Bones.Single(b => b.name.Split(':').Last() == "DriveTrain_Joint");
                Check(Vector3.Distance(drive.position, bb.position) < 1e-5f, "Drivetrain joint follows BB position");
                Check(Vector3.Angle(bb.forward, drive.forward) < .01f, "BB starts pitch-aligned to drivetrain despite SDK frame rest tilt");
                drive.localRotation *= Quaternion.Euler(40, 0, 0);
                rig.Apply(rig.Capture());
                Check(Quaternion.Angle(bb.localRotation, bbRest * Quaternion.Euler(40, 0, 0)) < .01f && Vector3.Distance(bb.position, bbPosition) < 1e-5f, "BB follows local pitch at its anchor");
                foreach (string side in new[] { "Left", "Right" })
                {
                    int partIndex = parts.FindIndex(p => p.source.name.Contains("_Pedal_") && p.joint == side + "Pedal_Joint");
                    var built = (System.Collections.Generic.List<GameObject>)typeof(AuthoringRig).GetField("vehicleVisuals", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(rig);
                    var pedal = built[partIndex].transform;
                    var pedalRest = pedal.localRotation;
                    var pedalJoint = rig.Bones.Single(b => b.name.Split(':').Last() == side + "Pedal_Joint");
                    pedalJoint.localRotation *= Quaternion.Euler(-40, 0, 0);
                    var expectedRotation = pedalJoint.localRotation;
                    rig.Apply(rig.Capture());
                    Check(Quaternion.Angle(pedal.localRotation, pedalRest * Quaternion.Euler(-40, 0, 0)) < .01f, side + " pedal follows its own local pitch");
                    Check(Vector3.Distance(pedalJoint.position, pedal.parent.position) < 1e-5f, side + " pedal joint follows its own crank arm's pedal anchor");
                    Check(Quaternion.Angle(pedalJoint.localRotation, expectedRotation) < .01f, "Position constraint preserves pedal rotation");
                }
                drive.localRotation *= Quaternion.Euler(140, 0, 0);
                rig.Apply(rig.Capture());
                Check(Quaternion.Angle(bb.localRotation, bbRest * Quaternion.Euler(180, 0, 0)) < .01f, "Crank pitch crosses 90 degrees without an Euler flip");
                var socket = bb.parent;
                var socketRest = socket.localRotation;
                socket.localRotation *= Quaternion.Euler(13, 0, 0);
                rig.Apply(rig.Capture());
                Check(Vector3.Angle(bb.forward, drive.forward) < .01f, "BB compensates socket pitch without moving off its anchor");
                Check(Vector3.Distance(bb.position, socket.position) < 1e-5f, "Pitch alignment preserves socket position");
                socket.localRotation = socketRest;
                var stablePose = rig.Capture();
                var stablePosition = drive.position;
                for (int repeat = 0; repeat < 20; repeat++) rig.Apply(stablePose);
                Check(Vector3.Distance(drive.position, stablePosition) < 1e-5f, "Repeated evaluation has no position feedback or drift");
                rig.BuildVehicleParts(parts);
                Check(rig.Root.GetComponentsInChildren<Transform>().Count(t => t.name.EndsWith(" · Preview")) == parts.Count, "Rebuild does not duplicate parts");
                Check(rig.Bones.Any(b => b.name == "PIDBalanceControl"), "Existing balance channel present");
            }
            var source = new GameObject("Channels");
            new GameObject("RightHand_IKTarget_ID").transform.SetParent(source.transform);
            new GameObject("RightHand_IKWeight").transform.SetParent(source.transform);
            using (var rig = new AuthoringRig(source, null))
            {
                var take = ScriptableObject.CreateInstance<AnimationTake>();
                take.bonePaths = rig.Paths; take.lastFrame = 10; take.interpolation = PoseInterpolation.Linear;
                var first = rig.Rest.Copy(0); var last = first.Copy(10);
                last.bones[1].position.y = .4f; last.bones[2].position.y = 1;
                take.SetKey(first, 0); take.SetKey(last, 10);
                Check(take.Evaluate(5).bones[1].position.y == 0 && take.Evaluate(10).bones[1].position.y == .4f, "IDs switch at the key");
                Check(Mathf.Approximately(take.Evaluate(5).bones[2].position.y, .5f), "Weights interpolate");
                var sparse = first.Copy(5); sparse.bones[2].position.y = .7f;
                take.SetChannelKey(sparse, 5, new[] { 0, 0, 1 }, new int[4]);
                Check(take.Evaluate(9).bones[1].position.y == 0, "Sparse keys preserve target holds");
                var layerFirst = first.Copy(0); layerFirst.bones[1].position.y = .1f;
                var layerLast = last.Copy(10);
                layerFirst.boneChannels = layerLast.boneChannels = new[] { 0, 1, 0 };
                take.layers.Add(new StudioAnimationLayer { bones = new System.Collections.Generic.List<string> { rig.Paths[1] },
                    keys = new System.Collections.Generic.List<PoseKey> { layerFirst, layerLast } });
                Check(Mathf.Approximately(take.Evaluate(9).bones[1].position.y, .1f), "Correction layer holds its target ID");
                take.layers.Clear();
                var clip = AnimationClipBaker.Bake(take, rig, false);
                var binding = AnimationUtility.GetCurveBindings(clip).Single(b => b.path == "RightHand_IKTarget_ID" && b.propertyName == "m_LocalPosition.y");
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                Check(StudioRigChannels.DecodeTargetId(curve.Evaluate(9.9f / take.frameRate)) == 0, "Exported target does not blend early");
                Check(StudioRigChannels.DecodeTargetId(curve.Evaluate(10f / take.frameRate)) == 4, "Legacy target encoding preserved");
                UnityEngine.Object.DestroyImmediate(clip); UnityEngine.Object.DestroyImmediate(take);
            }
            UnityEngine.Object.DestroyImmediate(source);
            var human = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Skeleton.fbx");
            using (var rig = new AuthoringRig(human, null))
            {
                Check(rig.Animator && rig.Animator.avatar.isHuman, "SDK humanoid avatar");
                foreach (var name in StudioRigChannels.WeightNames.Concat(StudioRigChannels.TargetNames))
                    Check(rig.Bones.Any(b => b.name.Split(':').Last() == name), "Legacy channel exists: " + name);
                var take = ScriptableObject.CreateInstance<AnimationTake>(); take.bonePaths = rig.Paths; take.lastFrame = 1;
                take.SetKey(rig.Rest, 0);
                var clip = AnimationClipBaker.Bake(take, rig, true);
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var name in StudioRigChannels.WeightNames.Concat(StudioRigChannels.TargetNames))
                    Check(bindings.Any(b => b.path.EndsWith("/" + name) && b.propertyName == "m_LocalPosition.y"), "Humanoid export retains " + name);
                UnityEngine.Object.DestroyImmediate(clip); UnityEngine.Object.DestroyImmediate(take);
            }
            File.WriteAllText("result.txt", "PASS: SDK pairs, six bolts, no mags, frame rest, joint attachments, steering isolation, stable bone paths, repeat build, balance channel, full/sparse target holds, weight interpolation, exported IDs, all eight legacy human channels and humanoid export.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("result.txt", e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
