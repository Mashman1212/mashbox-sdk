using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Authoring checks only. Export validation also checks prefab integrity,
    // materials, textures and mesh budgets; preview objects are not prefab assets.
    internal static class StudioVehiclePartValidation
    {
        internal sealed class Result
        {
            internal readonly List<string> issues = new List<string>();
            internal int matchedRules;
        }
        private static IEnumerable<string> Strings(SerializedProperty array)
        {
            if (array == null) yield break;
            for (int i = 0; i < array.arraySize; i++) yield return array.GetArrayElementAtIndex(i).stringValue;
        }
        private static string EnumName(SerializedProperty field) => field != null && field.enumValueIndex >= 0 && field.enumValueIndex < field.enumNames.Length ? field.enumNames[field.enumValueIndex] : "";
        private static Vector3 Direction(string name)
        {
            switch (name)
            {
                case "Back": return Vector3.back; case "Left": return Vector3.left;
                case "Right": return Vector3.right; case "Up": return Vector3.up;
                case "Down": return Vector3.down; default: return Vector3.forward;
            }
        }
        internal static Result Validate(StudioModelDocument document, int index, UnityEngine.Object rulesAsset)
        {
            var result = new Result();
            var root = document.nodes[index]; var part = root.vehiclePart;
            if (part == null) return result;
            void Issue(string message) { if (!result.issues.Contains(message)) result.issues.Add(message); }
            var tags = (root.name ?? "").Split('_');
            if (tags.Length != 4) Issue("Model names must contain four tags: Vehicle_Part_Brand_Name. Color tags belong on the prefab created later.");
            else
            {
                if (tags.Any(t => !Regex.IsMatch(t, @"^[A-Za-z0-9 ]+$"))) Issue("Name tags must be non-empty and contain only letters, digits and spaces.");
                if (!string.Equals(tags[0], part.vehicle, StringComparison.OrdinalIgnoreCase) || !string.Equals(tags[1].Trim(), part.part, StringComparison.OrdinalIgnoreCase))
                    Issue("Name identifies a different type. Expected " + part.vehicle + "_" + part.part + "_Brand_Name.");
            }
            if (!rulesAsset) { Issue("SDK content validation rules are unavailable."); return result; }
            // Build exact root-relative paths, including inactive nodes. These checks
            // intentionally use case-sensitive authored names, as the exporter does.
            var paths = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < document.nodes.Count; i++)
            {
                if (i == index) continue;
                var segments = new List<string>(); int cursor = i;
                while (cursor >= 0 && cursor != index) { segments.Add(document.nodes[cursor].name); cursor = document.nodes[cursor].parent; }
                if (cursor != index) continue;
                segments.Reverse(); string path = string.Join("/", segments);
                if (!paths.TryGetValue(path, out var entries)) paths[path] = entries = new List<int>();
                entries.Add(i);
            }
            foreach (var path in paths.Where(p => p.Value.Count > 1)) Issue("Duplicate hierarchy path: '" + path.Key + "'.");
            using (var data = new SerializedObject(rulesAsset))
            {
                var pairs = data.FindProperty("AllowedPairs"); var rules = data.FindProperty("ItemRules");
                if (pairs == null || rules == null) { Issue("SDK content validation rules could not be read."); return result; }
                bool allowed = false;
                for (int i = 0; i < pairs.arraySize; i++)
                {
                    var pair = pairs.GetArrayElementAtIndex(i);
                    if (pair.FindPropertyRelative("SuperType").stringValue == part.vehicle && Strings(pair.FindPropertyRelative("Types")).Contains(part.part)) allowed = true;
                }
                if (!allowed) Issue("The SDK does not allow " + part.vehicle + " / " + part.part + ". Choose a supported vehicle and part.");
                string brand = tags.Length == 4 ? tags[2] : "";
                for (int r = 0; r < rules.arraySize; r++)
                {
                    var rule = rules.GetArrayElementAtIndex(r);
                    if (EnumName(rule.FindPropertyRelative("AppliesToSuperType")) != part.vehicle || EnumName(rule.FindPropertyRelative("AppliesToType")).Replace('_', ' ') != part.part) continue;
                    string scope = rule.FindPropertyRelative("AppliesToBrand").stringValue;
                    if (!string.IsNullOrEmpty(scope) && scope != brand) continue;
                    result.matchedRules++;
                    if (!rule.FindPropertyRelative("IgnoreRequiredChildren").boolValue)
                    {
                        var required = Strings(rule.FindPropertyRelative("RequiredChildren")).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().Replace('\\', '/')).ToArray();
                        var anywhere = Strings(rule.FindPropertyRelative("RequiredDescendantsAnywhere")).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                        foreach (string path in required) if (!paths.ContainsKey(path)) Issue("Missing required child: '" + path + "'.");
                        foreach (string name in anywhere) if (!paths.Values.SelectMany(v => v).Any(i => document.nodes[i].name == name)) Issue("Missing required descendant: '" + name + "' (anywhere below this root).");
                        foreach (var path in paths)
                            if (!required.Any(p => p == path.Key || p.StartsWith(path.Key + "/", StringComparison.Ordinal)) && !anywhere.Contains(document.nodes[path.Value[0]].name)) Issue("Unexpected child: '" + path.Key + "'.");
                    }
                    var orientations = rule.FindPropertyRelative("PreferredOrientations");
                    if (orientations == null) continue;
                    for (int o = 0; o < orientations.arraySize; o++)
                    {
                        var orientation = orientations.GetArrayElementAtIndex(o);
                        string path = orientation.FindPropertyRelative("ChildPath").stringValue;
                        if (string.IsNullOrEmpty(path) || !paths.TryGetValue(path, out var children)) continue;
                        var rotation = document.nodes[children[0]].rotation;
                        float tolerance = orientation.FindPropertyRelative("Tolerance").floatValue;
                        if (Vector3.Angle(rotation * Vector3.forward, Direction(EnumName(orientation.FindPropertyRelative("Forward")))) > tolerance ||
                            Vector3.Angle(rotation * Vector3.up, Direction(EnumName(orientation.FindPropertyRelative("Up")))) > tolerance)
                            Issue("Orientation differs from the SDK rule at '" + path + "' (tolerance " + tolerance + "°).");
                    }
                }
            }
            return result;
        }
    }
}
