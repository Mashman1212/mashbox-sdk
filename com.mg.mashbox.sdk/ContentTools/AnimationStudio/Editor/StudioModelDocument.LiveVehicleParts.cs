using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        public enum VehicleTargetDirection { None, Left, Right, Front, Rear, Forward, Back, FrontLeft, FrontRight, RearLeft, RearRight }
        [Serializable]
        public sealed class VehiclePartData
        {
            public string vehicle = "BMX", part = "Frame", targetAnchor = "";
            public bool live;
            public VehicleTargetDirection targetDirection;
            public int targetAnchorId;
            internal string ResolvedTargetAnchor()
            {
                string anchor = DirectionalTargetAnchor();
                return targetAnchorId > 0 && !string.IsNullOrEmpty(anchor) ? anchor + "_" + targetAnchorId : anchor;
            }
            private string DirectionalTargetAnchor()
            {
                if (targetDirection == VehicleTargetDirection.None) return targetAnchor;
                string anchor = string.IsNullOrEmpty(targetAnchor) ? part.Replace(" ", "") + "_Anchor" : targetAnchor;
                if (anchor.StartsWith("Anchor_", StringComparison.OrdinalIgnoreCase)) anchor = anchor.Substring(7) + "_Anchor";
                var directions = new[] { "Left_", "Right_", "Front_", "Rear_", "Forward_", "Back_" };
                string prefix;
                while ((prefix = directions.FirstOrDefault(d => anchor.StartsWith(d, StringComparison.OrdinalIgnoreCase))) != null)
                    anchor = anchor.Substring(prefix.Length);
                string direction = targetDirection.ToString().Replace("FrontLeft", "Front_Left").Replace("FrontRight", "Front_Right")
                    .Replace("RearLeft", "Rear_Left").Replace("RearRight", "Rear_Right");
                return direction + "_" + anchor;
            }
            public VehiclePartData Copy() => (VehiclePartData)MemberwiseClone();
            internal static VehiclePartData From(StudioVehiclePartCatalog.Template template)
                => new VehiclePartData { vehicle = template.vehicle, part = template.part, targetAnchor = DefaultAnchor(template.part) };
            internal static string DefaultAnchor(string part)
            {
                string name = part.Replace(" ", "") + "_Anchor";
                return StudioAnchorCatalog.Contains(name) || StudioAnchorCatalog.Contains(name + "_1") ? name : "";
            }
        }

        // Same ID convention as Project X's AnchorComponent: ignore separators and
        // accept both Anchor_Forks and Forks_Anchor. The modeler's root is the socket.
        internal static string VehicleAnchorId(string value)
        {
            value = value ?? "";
            if (value.StartsWith("Anchor_", StringComparison.OrdinalIgnoreCase)) value = value.Substring(7);
            else if (value.EndsWith("_Anchor", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 7);
            return new string(value.Where(c => c != '_' && c != '-' && c != '(' && c != ')' && !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray());
        }

        internal Dictionary<int, string> SnapLiveVehicleParts(Transform[] transforms, ISet<int> attachedParts = null)
        {
            attachedParts?.Clear();
            var status = new Dictionary<int, string>();
            if (transforms.Length != nodes.Count) return status;
            bool Descendant(int child, int parent)
            {
                for (int i = child; i >= 0; i = nodes[i].parent) if (i == parent) return true;
                return false;
            }
            int Top(int index) { while (nodes[index].parent >= 0) index = nodes[index].parent; return index; }
            int Owner(int index)
            {
                for (int i = index; i >= 0; i = nodes[i].parent) if (nodes[i].vehiclePart != null) return i;
                return -1;
            }
            VehicleTargetDirection InheritedDirection(int anchor)
            {
                for (int p = nodes[anchor].parent; p >= 0; p = nodes[p].parent)
                {
                    var parentPart = nodes[p].vehiclePart;
                    if (parentPart == null) continue;
                    if (parentPart.targetDirection != VehicleTargetDirection.None) return parentPart.targetDirection;
                    // A manually selected Left_CrankArm_Anchor also identifies its side.
                    string parentTarget = VehicleAnchorId(parentPart.targetAnchor);
                    foreach (var direction in Enum.GetValues(typeof(VehicleTargetDirection)).Cast<VehicleTargetDirection>()
                        .Where(d => d != VehicleTargetDirection.None).OrderByDescending(d => d.ToString().Length))
                        if (parentTarget.StartsWith(direction.ToString().ToLowerInvariant(), StringComparison.Ordinal)) return direction;
                }
                return VehicleTargetDirection.None;
            }
            // Normalize each active socket once, rather than once per live part.
            var sockets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var socketNames = new string[nodes.Count];
            for (int j = 0; j < nodes.Count; j++)
            {
                string anchor = nodes[j].anchorType;
                if (string.IsNullOrEmpty(anchor) && (nodes[j].name.StartsWith("Anchor_", StringComparison.OrdinalIgnoreCase) || nodes[j].name.EndsWith("_Anchor", StringComparison.OrdinalIgnoreCase))) anchor = nodes[j].name;
                if (string.IsNullOrEmpty(anchor) || !transforms[j].gameObject.activeInHierarchy) continue;
                socketNames[j] = anchor;
                string key = VehicleAnchorId(anchor);
                if (!sockets.TryGetValue(key, out var entries)) sockets[key] = entries = new List<int>();
                entries.Add(j);
            }
            var targets = new Dictionary<int, int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var part = nodes[i].vehiclePart;
                if (part == null || !part.live) continue;
                string id = VehicleAnchorId(part.ResolvedTargetAnchor());
                if (id.Length == 0) { status[i] = "Choose a target anchor."; continue; }
                if (!transforms[i].gameObject.activeInHierarchy) { status[i] = "Inactive part."; continue; }
                var matches = new List<int>();
                string baseAnchor = string.IsNullOrEmpty(part.targetAnchor) ? part.part.Replace(" ", "") + "_Anchor" : part.targetAnchor;
                string baseTarget = VehicleAnchorId(baseAnchor + (part.targetAnchorId > 0 ? "_" + part.targetAnchorId : ""));
                var candidates = new List<int>();
                if (sockets.TryGetValue(id, out var direct)) candidates.AddRange(direct);
                if (part.targetDirection != VehicleTargetDirection.None && baseTarget != id && sockets.TryGetValue(baseTarget, out var inherited)) candidates.AddRange(inherited);
                foreach (int j in candidates)
                {
                    string anchor = socketNames[j];
                    if (Descendant(j, i)) continue;
                    string anchorId = VehicleAnchorId(anchor);
                    bool directMatch = anchorId == id;
                    bool inheritedMatch = false;
                    if (!directMatch && part.targetDirection != VehicleTargetDirection.None)
                    {
                        var directionalSocket = new VehiclePartData { targetAnchor = anchor, targetDirection = part.targetDirection };
                        // Only add context to an unqualified socket; never reinterpret an
                        // explicitly opposite-side socket using its parent's direction.
                        inheritedMatch = anchorId == baseTarget && InheritedDirection(j) == part.targetDirection &&
                            VehicleAnchorId(directionalSocket.ResolvedTargetAnchor()) == id;
                    }
                    if (!directMatch && !inheritedMatch) continue;
                    // Group an assembly to isolate it from other bikes in the workspace.
                    if (nodes[i].parent >= 0 && Top(i) != Top(j)) continue;
                    int owner = Owner(j);
                    if (owner >= 0 && nodes[owner].vehiclePart.vehicle != part.vehicle) continue;
                    matches.Add(j);
                }
                if (matches.Count != 1) { status[i] = matches.Count == 0 ? "Waiting for a matching anchor." : "Multiple matches. Group this vehicle separately or choose a specific anchor."; continue; }
                targets[i] = matches[0];
            }
            var states = new Dictionary<int, int>();
            bool Resolve(int index)
            {
                if (states.TryGetValue(index, out int state)) return state == 2;
                states[index] = 1;
                int target = targets[index];
                // Resolve every moving ancestor first, including nested live parts.
                var dependencies = new HashSet<int>();
                for (int p = target; p >= 0; p = nodes[p].parent) if (targets.ContainsKey(p)) dependencies.Add(p);
                for (int p = nodes[index].parent; p >= 0; p = nodes[p].parent) if (targets.ContainsKey(p)) dependencies.Add(p);
                foreach (int dependency in dependencies)
                    if (!Resolve(dependency)) { states[index] = 3; status[index] = "Anchor dependency cycle; snapping paused."; return false; }
                var source = transforms[index]; var destination = transforms[target];
                if (source.parent && Mathf.Abs(source.parent.localToWorldMatrix.determinant) < 1e-12f)
                { states[index] = 3; status[index] = "Parent has zero scale; snapping paused."; return false; }
                var rotation = destination.rotation;
                int targetOwner = Owner(target);
                if (string.Equals(nodes[index].vehiclePart.part, "Pedal", StringComparison.OrdinalIgnoreCase) && targetOwner >= 0 &&
                    string.Equals(nodes[targetOwner].vehiclePart.part.Replace(" ", ""), "CrankArm", StringComparison.OrdinalIgnoreCase))
                    rotation *= Quaternion.Euler(0, 180, 0);
                source.SetPositionAndRotation(destination.position, rotation);
                // Keep authored scale and pivot offsets; only position/rotation follow.
                var node = nodes[index];
                node.position = source.localPosition - node.positionOffset;
                node.rotation = source.localRotation;
                status[index] = "Attached to " + nodes[target].name;
                states[index] = 2;
                attachedParts?.Add(index);
                return true;
            }
            foreach (int index in targets.Keys) Resolve(index);
            return status;
        }
    }
}
