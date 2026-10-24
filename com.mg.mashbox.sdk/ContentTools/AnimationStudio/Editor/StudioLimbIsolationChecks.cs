using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioLimbIsolationChecks
    {
        private const string Request = "Temp/StudioLimbIsolation.request";
        [InitializeOnLoadMethod]
        private static void Pending()
        {
            if (File.Exists(Request)) EditorApplication.update += RunPending;
        }
        private static void RunPending()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= RunPending;
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            try { Run(); File.WriteAllText("Temp/StudioLimbIsolation.result", "PASS: limb isolation, matched handoff, world pins, body IK target movement, fixed bone lengths and keyed playback."); }
            catch (Exception e) { File.WriteAllText("Temp/StudioLimbIsolation.result", e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("MashBox/Animation/Validate Independent Limb IK")]
        public static void Run()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Skeleton.fbx");
            using (var rig = new AuthoringRig(source, null))
            {
                Check(rig.HasIK, "IK initialized: " + rig.SolverStatus);
                var rest = rig.Capture();
                for (int limb = 0; limb < 4; limb++)
                {
                    rig.Apply(rest);
                    var positions = rig.Bones.Select(b => b.position).ToArray();
                    var rotations = rig.Bones.Select(b => b.rotation).ToArray();
                    var poles = Enumerable.Range(0, 4).Select(i => rig.MatchEffector(i, 0).pole).ToArray();
                    var edit = rest.Copy(0);
                    edit.effectors[limb] = rig.MatchEffector(limb, 1);
                    rig.Apply(edit);
                    for (int b = 0; b < rig.Bones.Length; b++)
                    {
                        Check(Vector3.Distance(positions[b], rig.Bones[b].position) < .001f, "matched activation position " + limb + "/" + rig.Bones[b].name);
                        Check(Quaternion.Angle(rotations[b], rig.Bones[b].rotation) < .5f, "matched activation twist " + limb + "/" + rig.Bones[b].name);
                    }
                    var start = rig.Root.transform.InverseTransformPoint(rig.Starts[limb].position);
                    edit.effectors[limb].position = Vector3.Lerp(edit.effectors[limb].position, start, .15f);
                    foreach (float weight in new[] { .01f, .5f, 1f })
                    {
                        edit.effectors[limb].weight = weight;
                        rig.Apply(edit);
                        for (int b = 0; b < rig.Bones.Length; b++)
                        {
                            if (rig.Bones[b] == rig.Starts[limb] || rig.Bones[b].IsChildOf(rig.Starts[limb])) continue;
                            Check(Vector3.Distance(positions[b], rig.Bones[b].position) < .00001f, "unrelated position changed");
                            Check(Quaternion.Angle(rotations[b], rig.Bones[b].rotation) < .05f, "unrelated rotation changed");
                        }
                        for (int i = 0; i < 4; i++) if (i != limb)
                            Check(Vector3.Distance(poles[i], rig.MatchEffector(i, 0).pole) < .00001f, "inactive pole moved");
                    }
                    Check(Vector3.Distance(rig.Ends[limb].position, rig.Root.transform.TransformPoint(edit.effectors[limb].position)) < .001f, "target reach");
                    var solved = rig.Bones.Select(b => b.rotation).ToArray();
                    for (int repeat = 0; repeat < 10; repeat++) rig.Apply(edit);
                    for (int b = 0; b < rig.Bones.Length; b++) Check(Quaternion.Angle(solved[b], rig.Bones[b].rotation) < .05f, "repeatability");
                    CheckMatchedHandoff(rig, edit, limb);
                    CheckPins(rig, edit, limb);
                }
                CheckBodyControls(rig, rest);
            }
            Debug.Log("PASS: Studio limb IK, matched handoff, world pins and body IK controls.");
        }

        private static void CheckBodyControls(AuthoringRig rig, PoseKey rest)
        {
            for (int kind = 0; kind < 3; kind++)
            {
                rig.Apply(rest);
                var edit = rest.Copy(0);
                for (int limb = 0; limb < 4; limb++)
                {
                    edit.effectors[limb] = rig.MatchEffector(limb, 1);
                    var start = rig.Root.transform.InverseTransformPoint(rig.Starts[limb].position);
                    edit.effectors[limb].position = Vector3.Lerp(edit.effectors[limb].position, start, .15f);
                }
                rig.Apply(edit);
                for (int limb = 0; limb < 4; limb++)
                {
                    edit.effectors[limb].pinPosition = edit.effectors[limb].pinRotation = true;
                    edit.effectors[limb].pinnedPosition = rig.Ends[limb].position;
                    edit.effectors[limb].pinnedRotation = rig.Ends[limb].rotation;
                }
                var body = rig.BodyControl(kind);
                Check(body, "body control exists " + kind);
                var chain = rig.BodyControlChain(kind);
                Vector3 rootPosition = rig.Root.transform.position;
                Vector3 pelvis = rig.BodyControl(0).position;
                var lengths = chain.Select(b => b.localPosition).ToArray();
                Vector3 target = body.position + Vector3.down * .015f;
                if (kind != 0)
                {
                    var anchor = chain[chain.Count - 1];
                    target = anchor.position + Quaternion.AngleAxis(4, rig.Root.transform.forward) * (body.position - anchor.position);
                }
                rig.PoseBodyControl(edit, kind, target, body.rotation, false);
                Check(Vector3.Distance(body.position, target) < .003f, "body IK target reach " + kind);
                Check(Vector3.Distance(rig.Root.transform.position, rootPosition) < .00001f, "body IK leaves actor root fixed");
                if (kind != 0)
                {
                    Check(Vector3.Distance(rig.BodyControl(0).position, pelvis) < .00001f, "torso IK leaves hips fixed");
                    for (int i = 0; i < chain.Count; i++) Check(Vector3.Distance(chain[i].localPosition, lengths[i]) < .00001f, "torso bone length preserved");
                }
                for (int limb = 0; limb < 4; limb++)
                {
                    Check(Vector3.Distance(rig.Ends[limb].position, edit.effectors[limb].pinnedPosition) < .002f, "body edit preserves limb position pin");
                    Check(Quaternion.Angle(rig.Ends[limb].rotation, edit.effectors[limb].pinnedRotation) < .05f, "body edit preserves limb rotation pin");
                }
                Quaternion rotated = Quaternion.AngleAxis(3, rig.Root.transform.up) * body.rotation;
                rig.PoseBodyControl(edit, kind, body.position, rotated, true);
                Check(Quaternion.Angle(body.rotation, rotated) < .05f, "body rotation control");
                var take = ScriptableObject.CreateInstance<AnimationTake>();
                try
                {
                    take.SetKey(edit, 0); rig.Apply(take.Evaluate(0));
                    Check(Quaternion.Angle(body.rotation, rotated) < .05f, "body pose keys roundtrip");
                }
                finally { UnityEngine.Object.DestroyImmediate(take); }
            }
        }

        private static void CheckPins(AuthoringRig rig, PoseKey source, int limb)
        {
            rig.Apply(source);
            var pin = source.Copy(0);
            var e = rig.MatchEffector(limb, 0);
            e.pinPosition = e.pinRotation = e.pinPole = true;
            e.pinnedPosition = rig.Ends[limb].position;
            e.pinnedRotation = rig.Ends[limb].rotation;
            e.pinnedPole = rig.Root.transform.TransformPoint(e.pole);
            pin.effectors[limb] = e;
            int root = Array.IndexOf(rig.Bones, rig.Root.transform);
            pin.bones[root].position += (e.pinnedPosition - rig.Root.transform.position).normalized * .02f;
            pin.bones[root].rotation = Quaternion.Euler(0, 4, 0) * pin.bones[root].rotation;
            rig.Apply(pin);
            Check(Vector3.Distance(rig.Ends[limb].position, e.pinnedPosition) < .001f, "world position pin");
            Check(Quaternion.Angle(rig.Ends[limb].rotation, e.pinnedRotation) < .05f, "world rotation pin");
            Check(Vector3.Distance(rig.Root.transform.TransformPoint(e.ResolvePins(rig.Root.transform).pole), e.pinnedPole) < .00001f, "world pole pin");

            var free = pin.Copy(0);
            free.effectors[limb].pinPosition = free.effectors[limb].pinRotation = free.effectors[limb].pinPole = false;
            rig.Apply(free);
            var fkPosition = rig.Ends[limb].position;
            var fkRotation = rig.Ends[limb].rotation;
            var onlyPosition = pin.Copy(0); onlyPosition.effectors[limb].pinRotation = false;
            rig.Apply(onlyPosition);
            Check(Vector3.Distance(rig.Ends[limb].position, e.pinnedPosition) < .001f, "position only hold");
            Check(Quaternion.Angle(rig.Ends[limb].rotation, fkRotation) < .05f, "position pin leaves rotation free");
            var onlyRotation = pin.Copy(0); onlyRotation.effectors[limb].pinPosition = false;
            rig.Apply(onlyRotation);
            Check(Vector3.Distance(rig.Ends[limb].position, fkPosition) < .00001f, "rotation pin leaves position free");
            Check(Quaternion.Angle(rig.Ends[limb].rotation, e.pinnedRotation) < .05f, "rotation only hold");

            var take = ScriptableObject.CreateInstance<AnimationTake>();
            try
            {
                take.SetKey(free, 0);
                var masks = new int[4]; masks[limb] = 8;
                take.SetChannelKey(pin, 10, new int[pin.bones.Length], masks);
                var keyed = take.Evaluate(10).effectors[limb];
                Check(keyed.pinPosition && keyed.pinRotation && keyed.pinPole, "sparse key pin flags");
                Check(keyed.pinnedPosition == e.pinnedPosition, "sparse key pin anchor");
                Check(!take.Evaluate(5).effectors[limb].pinPosition, "pin state steps at key");
                take.SetKey(pin, 0);
                rig.Apply(take.Evaluate(0));
                Check(Vector3.Distance(rig.Ends[limb].position, e.pinnedPosition) < .001f, "keyed world pin playback");
                var unpin = pin.Copy(0);
                rig.MatchLimbBlend(unpin, limb, 0, true);
                unpin.effectors[limb].pinPosition = unpin.effectors[limb].pinRotation = unpin.effectors[limb].pinPole = false;
                rig.Apply(unpin);
                Check(Vector3.Distance(rig.Ends[limb].position, e.pinnedPosition) < .001f, "unpin without snap");
            }
            finally { UnityEngine.Object.DestroyImmediate(take); }
        }

        private static void CheckMatchedHandoff(AuthoringRig rig, PoseKey edit, int limb)
        {
            var positions = rig.Bones.Select(b => b.position).ToArray();
            var rotations = rig.Bones.Select(b => b.rotation).ToArray();
            var take = ScriptableObject.CreateInstance<AnimationTake>();
            try
            {
                foreach (float weight in new[] { .8f, .35f, 0f, .25f, .7f, 1f, 0f })
                {
                    rig.MatchLimbBlend(edit, limb, weight);
                    rig.Apply(edit);
                    AssertPose();
                    take.SetKey(edit, 0);
                    rig.Apply(take.Evaluate(0));
                    AssertPose();
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(take); }
            void AssertPose()
            {
                for (int b = 0; b < rig.Bones.Length; b++)
                {
                    Check(Vector3.Distance(positions[b], rig.Bones[b].position) < .001f, "handoff position " + limb + "/" + rig.Bones[b].name);
                    Check(Quaternion.Angle(rotations[b], rig.Bones[b].rotation) < .5f, "handoff rotation " + limb + "/" + rig.Bones[b].name);
                }
            }
        }
        private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    }
}
