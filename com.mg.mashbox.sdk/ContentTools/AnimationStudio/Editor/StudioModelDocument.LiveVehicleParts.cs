using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioModelDocument
    {
        [Serializable]
        public sealed class VehiclePartData
        {
            public string vehicle = "BMX", part = "Frame", targetAnchor = "";
            public bool live;
            public VehiclePartData Copy() => (VehiclePartData)MemberwiseClone();
            internal static VehiclePartData From(StudioVehiclePartCatalog.Template template)
                => new VehiclePartData { vehicle = template.vehicle, part = template.part, targetAnchor = DefaultAnchor(template.part) };
            internal static string DefaultAnchor(string part)
            {
                string name = part.Replace(" ", "") + "_Anchor";
                return StudioAnchorCatalog.Contains(name) ? name : "";
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
            var targets = new Dictionary<int, int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var part = nodes[i].vehiclePart;
                if (part == null || !part.live) continue;
                string id = VehicleAnchorId(part.targetAnchor);
                if (id.Length == 0) { status[i] = "Choose a target anchor."; continue; }
                if (!transforms[i].gameObject.activeInHierarchy) { status[i] = "Inactive part."; continue; }
                var matches = new List<int>();
                for (int j = 0; j < nodes.Count; j++)
                {
                    string anchor = nodes[j].anchorType;
                    if (string.IsNullOrEmpty(anchor) && (nodes[j].name.StartsWith("Anchor_", StringComparison.OrdinalIgnoreCase) || nodes[j].name.EndsWith("_Anchor", StringComparison.OrdinalIgnoreCase))) anchor = nodes[j].name;
                    if (string.IsNullOrEmpty(anchor) || VehicleAnchorId(anchor) != id || Descendant(j, i) || !transforms[j].gameObject.activeInHierarchy) continue;
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
                source.SetPositionAndRotation(destination.position, destination.rotation);
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
