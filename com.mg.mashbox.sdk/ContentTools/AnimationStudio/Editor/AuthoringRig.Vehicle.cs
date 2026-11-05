using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRigChannels
    {
        internal static readonly string[] WeightNames = { "LeftHand_IKWeight", "RightHand_IKWeight", "LeftFoot_IKWeight", "RightFoot_IKWeight" };
        internal static readonly string[] TargetNames = { "LeftHand_IKTarget_ID", "RightHand_IKTarget_ID", "LeftFootIK_Target_ID", "RightFootIK_Target_ID" };
        internal static bool MatchesName(string path, string name) => path != null && path.EndsWith(name, StringComparison.Ordinal) &&
            (path.Length == name.Length || path[path.Length - name.Length - 1] == '/' || path[path.Length - name.Length - 1] == ':');
        internal static bool IsTargetId(string path)
        {
            foreach (var name in TargetNames) if (MatchesName(path, name)) return true;
            return false;
        }
        internal static bool IsDataChannel(string path)
        {
            if (MatchesName(path, "PIDBalanceControl") || IsTargetId(path)) return true;
            foreach (var name in WeightNames) if (MatchesName(path, name)) return true;
            return false;
        }
        internal static int DecodeTargetId(float y) => Mathf.FloorToInt((y + .05f) * 10f);
    }
    internal sealed partial class AuthoringRig
    {
        private readonly List<GameObject> vehicleVisuals = new List<GameObject>();
        private sealed class VehicleRotationBinding
        {
            public Transform visual, joint;
            public Quaternion restRotation, jointRest;
            public int channel;
            public bool alignPitch;
        }
        private readonly List<VehicleRotationBinding> vehicleRotationBindings = new List<VehicleRotationBinding>();
        private Transform drivetrainPositionTarget, drivetrainPositionJoint;
        private VehicleRotationBinding barsHeadsetBinding;
        private static Quaternion YawTwist(Quaternion rotation)
        {
            float length = Mathf.Sqrt(rotation.y * rotation.y + rotation.w * rotation.w);
            return length > 1e-6f ? new Quaternion(0, rotation.y / length, 0, rotation.w / length) : Quaternion.identity;
        }
        private readonly List<KeyValuePair<Transform, Transform>> pedalPositionBindings = new List<KeyValuePair<Transform, Transform>>();
        internal void ApplyVehicleRotationConstraints()
        {
            foreach (var binding in vehicleRotationBindings)
            {
                var delta = Quaternion.Inverse(binding.jointRest) * binding.joint.localRotation;
                if (binding.alignPitch)
                {
                    // BB stays at its socket, but its pitch must align with the
                    // drivetrain, not retain the SDK frame/socket's rest tilt.
                    var parentRotation = binding.visual.parent ? binding.visual.parent.rotation : Quaternion.identity;
                    delta = Quaternion.Inverse(binding.restRotation) * Quaternion.Inverse(parentRotation) * binding.joint.rotation;
                }
                // Extract axis twist without Euler's 90-degree pitch flip.
                float angle = 2f * Mathf.Atan2(binding.channel == 2 ? delta.y : delta.x, delta.w) * Mathf.Rad2Deg;
                binding.visual.localRotation = binding.restRotation * Quaternion.AngleAxis(angle,
                    binding.channel == 2 ? Vector3.up : Vector3.right);
            }
            // Rotations finish the anchor chain first. Move the parent joint before
            // its pedal children; translation never feeds back into mesh rotation.
            if (drivetrainPositionJoint && drivetrainPositionTarget)
                drivetrainPositionJoint.position = drivetrainPositionTarget.position;
            foreach (var binding in pedalPositionBindings)
                if (binding.Key && binding.Value) binding.Key.position = binding.Value.position;
            if (barsHeadsetBinding != null)
            {
                var binding = barsHeadsetBinding;
                var jointParent = binding.joint.parent ? binding.joint.parent.rotation : Quaternion.identity;
                var headsetParent = binding.visual.parent ? binding.visual.parent.rotation : Quaternion.identity;
                // Use the unsteered headset basis so its driven yaw cannot feed
                // back into the bars. Swing follows headset tilt; twist stays keyed.
                var steering = YawTwist(Quaternion.Inverse(binding.jointRest) * binding.joint.localRotation);
                var target = Quaternion.Inverse(binding.jointRest) * Quaternion.Inverse(jointParent) * headsetParent * binding.restRotation;
                var tilt = target * Quaternion.Inverse(YawTwist(target));
                binding.joint.localRotation = binding.jointRest * tilt * steering;
                binding.joint.position = binding.visual.position;
            }
        }
        [Serializable] private sealed class VehicleOutfitSnapshot { public List<StudioVehiclePart> parts; }
        private string vehicleOutfitSnapshot;
        private static string DescribeVehicleOutfit(List<StudioVehiclePart> parts) =>
            parts == null || parts.Count == 0 ? "" : JsonUtility.ToJson(new VehicleOutfitSnapshot { parts = parts });

        internal void RefreshVehiclePartsAfterUndo(List<StudioVehiclePart> parts, bool useDefaultBmx)
        {
            // Compare only appearance data, never the potentially large pose/key lists.
            if (vehicleOutfitSnapshot != DescribeVehicleOutfit(parts)) BuildVehicleParts(parts, useDefaultBmx);
        }
        internal bool IsBmxRig => !Animator && new[] { "Frame_Joint", "Bars_Joint", "DriveTrain_Joint" }
            .All(name => Bones.Any(b => b.name.Split(':').Last() == name));
        public void BuildVehicleParts(List<StudioVehiclePart> parts, bool useDefaultBmx = false)
        {
            var snapshot = DescribeVehicleOutfit(parts);
            vehicleRotationBindings.Clear();
            barsHeadsetBinding = null;
            drivetrainPositionTarget = drivetrainPositionJoint = null;
            pedalPositionBindings.Clear();
            foreach(var visual in vehicleVisuals) if(visual) UnityEngine.Object.DestroyImmediate(visual);
            vehicleVisuals.Clear();
            if ((parts == null || parts.Count == 0) && useDefaultBmx && IsBmxRig) parts = BuildFullBmxOutfit();
            if(parts==null || parts.Count==0) { vehicleOutfitSnapshot = snapshot; return; }
            var saved=Capture();
            try
            {
                Apply(Rest);
                foreach(var part in parts)
                {
                    if(!part.source) { vehicleVisuals.Add(null); continue; }
                    var joint=Bones.FirstOrDefault(b=>b.name.Split(':').Last()==part.joint) ?? Bones.FirstOrDefault(b=>UnityEditor.AnimationUtility.CalculateTransformPath(b,Root.transform)==part.joint);
                    if(!joint) throw new InvalidOperationException("Vehicle part joint not found: "+part.joint);
                    Transform mount=null;
                    if(part.anchorPart>=0)
                    {
                        if(part.anchorPart>=vehicleVisuals.Count || !vehicleVisuals[part.anchorPart]) throw new InvalidOperationException("Mount a part to an earlier, assigned part.");
                        var parent=vehicleVisuals[part.anchorPart].transform;
                        mount=parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==part.anchor || UnityEditor.AnimationUtility.CalculateTransformPath(t,parent)==part.anchor);
                        if(!mount) throw new InvalidOperationException("Mount anchor not found: "+part.anchor);
                    }
                    var copy=new GameObject(part.source.name+" · Preview"); vehicleVisuals.Add(copy);
                    copy.transform.SetParent(mount ? mount : joint,false);
                    var map=new Dictionary<Transform,Transform> { [part.source.transform]=copy.transform };
                    CloneChildren(part.source.transform,copy.transform,map); CopyRenderers(part.source,map);
                    if(!mount && part.bindFromRest)
                    {
                        // Parts are authored in model space; retain that rest placement while the chosen joint drives motion.
                        copy.transform.SetPositionAndRotation(Root.transform.TransformPoint(part.position),Root.transform.rotation*Quaternion.Euler(part.rotation));
                    }
                    else { copy.transform.localPosition=part.position; copy.transform.localRotation=Quaternion.Euler(part.rotation); }
                    if(mount && !part.preserveAnchorParent) copy.transform.SetParent(joint,true);
                    copy.transform.localScale=Vector3.Scale(part.source.transform.localScale,part.scale);
                    if (part.preserveAnchorParent && mount)
                    {
                        string kind = part.source.name.Split('_').Skip(1).FirstOrDefault();
                        if (kind == "BB" && part.joint == "DriveTrain_Joint" && !copy.transform.IsChildOf(joint))
                        { drivetrainPositionJoint = joint; drivetrainPositionTarget = copy.transform; }
                        if (kind == "Pedal" && (part.joint == "LeftPedal_Joint" || part.joint == "RightPedal_Joint") && !mount.IsChildOf(joint))
                            pedalPositionBindings.Add(new KeyValuePair<Transform, Transform>(joint, mount));
                    }
                    if (part.preserveAnchorParent && part.localRotationChannel != 0)
                    {
                        var binding = new VehicleRotationBinding { visual = copy.transform, joint = joint,
                            restRotation = copy.transform.localRotation, jointRest = joint.localRotation, channel = part.localRotationChannel,
                            alignPitch = part.localRotationChannel == 1 && part.joint == "DriveTrain_Joint" &&
                                part.source.name.Split('_').Skip(1).FirstOrDefault() == "BB" };
                        vehicleRotationBindings.Add(binding);
                        if (mount && part.joint == "Bars_Joint" && part.source.name.Split('_').Skip(1).FirstOrDefault() == "Headset" && !copy.transform.IsChildOf(joint))
                            barsHeadsetBinding = binding;
                    }
                }
            }
            catch
            {
                vehicleRotationBindings.Clear();
                barsHeadsetBinding = null;
                drivetrainPositionTarget = drivetrainPositionJoint = null;
                pedalPositionBindings.Clear();
                foreach(var visual in vehicleVisuals) if(visual) UnityEngine.Object.DestroyImmediate(visual);
                vehicleVisuals.Clear(); throw;
            }
            finally { Apply(saved); }
            vehicleOutfitSnapshot = snapshot;
            previewMeshesVisible = null;
        }

        // Share the modeler's assembly, including paired parts, anchor IDs and live scale.
        // Persist asset references and solved rest placements, never temporary preview objects.
        internal static List<StudioVehiclePart> BuildFullBmxOutfit()
        {
            var document = ScriptableObject.CreateInstance<StudioModelDocument>();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                document.AddFullVehicle("BMX", false);
                var transforms = document.Build(scene);
                var targets = new Dictionary<int, int>();
                document.SnapLiveVehicleParts(transforms, null, targets);
                var result = new List<StudioVehiclePart>();
                var indices = new Dictionary<int, int>();
                int AddPart(int i)
                {
                    if (indices.TryGetValue(i, out int existing)) return existing;
                    var node = document.nodes[i];
                    if (node.vehiclePart == null || string.IsNullOrEmpty(node.sourcePath)) return -1;
                    int parentIndex = -1; Transform mount = null;
                    if (targets.TryGetValue(i, out int target))
                    {
                        int owner = target;
                        while (owner >= 0 && document.nodes[owner].vehiclePart == null) owner = document.nodes[owner].parent;
                        if (owner >= 0) { parentIndex = AddPart(owner); mount = transforms[target]; }
                    }
                    var source = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(node.sourcePath);
                    if (!source) throw new InvalidOperationException("BMX part model is missing: " + node.sourcePath);
                    var t = transforms[i]; var sourceScale = source.transform.localScale;
                    float Ratio(float value, float divisor) => Mathf.Abs(divisor) > 1e-8f ? value / divisor : 1;
                    string kind = node.vehiclePart.part.Replace(" ", "");
                    var relativeScale = mount ? (mount.worldToLocalMatrix * t.localToWorldMatrix).lossyScale : t.lossyScale;
                    int index = result.Count; indices[i] = index;
                    string anchorPath = "";
                    if (mount)
                    {
                        int owner = target;
                        while (document.nodes[owner].vehiclePart == null) owner = document.nodes[owner].parent;
                        anchorPath = UnityEditor.AnimationUtility.CalculateTransformPath(mount, transforms[owner]);
                    }
                    result.Add(new StudioVehiclePart { source = source, joint = BmxPartJoint(node.vehiclePart),
                        anchorPart = parentIndex, anchor = anchorPath, preserveAnchorParent = true,
                        localRotationChannel = kind == "Headset" || kind == "Forks" ? 2 : kind == "BB" || kind == "Pedal" ? 1 : 0,
                        position = mount ? mount.InverseTransformPoint(t.position) : t.position,
                        rotation = (mount ? Quaternion.Inverse(mount.rotation) * t.rotation : t.rotation).eulerAngles,
                        scale = new Vector3(Ratio(relativeScale.x, sourceScale.x), Ratio(relativeScale.y, sourceScale.y), Ratio(relativeScale.z, sourceScale.z)) });
                    return index;
                }
                for (int i = 0; i < document.nodes.Count; i++) AddPart(i);
                return result;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                foreach (var node in document.nodes) if (node.mesh) UnityEngine.Object.DestroyImmediate(node.mesh);
                UnityEngine.Object.DestroyImmediate(document);
            }
        }

        internal static string BmxPartJoint(StudioModelDocument.VehiclePartData part)
        {
            switch (part.part.Replace(" ", ""))
            {
                case "Bars": case "Forks": case "Headset": case "Stem": case "StemBolt": case "StemCap":
                case "Grip": case "BarEnd": return "Bars_Joint";
                case "BB": case "CrankArm": case "Sprocket": case "Chain": return "DriveTrain_Joint";
                case "Pedal": return part.targetDirection == StudioModelDocument.VehicleTargetDirection.Left ? "LeftPedal_Joint" : "RightPedal_Joint";
                case "FrontHub": return "FrontWheel_Joint";
                case "RearHub": return "BackWheel_Joint";
                case "Rim": case "Spokes": case "Nipples": case "Tire":
                    return part.targetDirection == StudioModelDocument.VehicleTargetDirection.Front ? "FrontWheel_Joint" : "BackWheel_Joint";
                default: return "Frame_Joint";
            }
        }
    }
}
