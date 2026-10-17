using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioVehiclePartCatalog
    {
        private const string RulesGuid = "c7baf1b43fbb9564ea4bcc515370d9ad";
        internal sealed class Template
        {
            internal string vehicle, part, brand, color;
            internal int ruleCount, maxRenderers = -1;
            internal readonly List<string> paths = new List<string>();
            internal readonly Dictionary<string, Quaternion> orientations = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            internal string MenuPath => "Vehicle Part/" + vehicle + "/" + part + (string.IsNullOrEmpty(brand) ? "" : " (" + brand + ")") + (ruleCount == 0 ? " (mesh only)" : "");
        }
        internal static UnityEngine.Object RulesAsset => AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(RulesGuid));
        private static string EnumName(SerializedProperty field) => field.enumValueIndex >= 0 && field.enumValueIndex < field.enumNames.Length ? field.enumNames[field.enumValueIndex] : "";
        private static IEnumerable<string> Strings(SerializedProperty array)
        {
            if (array == null) yield break;
            for (int i = 0; i < array.arraySize; i++) yield return array.GetArrayElementAtIndex(i).stringValue;
        }
        internal static List<Template> Load()
        {
            var result = new List<Template>(); var asset = RulesAsset;
            if (!asset) return result;
            using (var data = new SerializedObject(asset))
            {
                var pairs = data.FindProperty("AllowedPairs"); var rules = data.FindProperty("ItemRules");
                if (pairs == null || rules == null) return result;
                string color = Strings(data.FindProperty("Colors")).FirstOrDefault(c => c == "Black") ?? Strings(data.FindProperty("Colors")).FirstOrDefault() ?? "Black";
                for (int i = 0; i < pairs.arraySize; i++)
                {
                    var pair = pairs.GetArrayElementAtIndex(i); string vehicle = pair.FindPropertyRelative("SuperType").stringValue;
                    if (!new[] { "BMX", "MTB", "Scooter", "Drone", "Skateboard" }.Contains(vehicle)) continue;
                    foreach (string part in Strings(pair.FindPropertyRelative("Types")).Distinct())
                    {
                        var matching = new List<int>(); var brands = new HashSet<string>(StringComparer.Ordinal) { "" };
                        for (int r = 0; r < rules.arraySize; r++)
                        {
                            var rule = rules.GetArrayElementAtIndex(r);
                            if (EnumName(rule.FindPropertyRelative("AppliesToSuperType")) != vehicle || EnumName(rule.FindPropertyRelative("AppliesToType")).Replace('_', ' ') != part) continue;
                            matching.Add(r); brands.Add(rule.FindPropertyRelative("AppliesToBrand").stringValue ?? "");
                        }
                        foreach (string brand in brands)
                        {
                            var template = new Template { vehicle = vehicle, part = part, brand = brand, color = color };
                            foreach (int r in matching)
                            {
                                var rule = rules.GetArrayElementAtIndex(r);
                                string scope = rule.FindPropertyRelative("AppliesToBrand").stringValue;
                                if (!string.IsNullOrEmpty(scope) && scope != brand) continue;
                                template.ruleCount++;
                                int limit = rule.FindPropertyRelative("MaxRenderers")?.intValue ?? -1;
                                if (limit >= 0) template.maxRenderers = template.maxRenderers < 0 ? limit : Math.Min(limit, template.maxRenderers);
                                if (!rule.FindPropertyRelative("IgnoreRequiredChildren").boolValue)
                                {
                                    foreach (string path in Strings(rule.FindPropertyRelative("RequiredChildren"))) AddPath(template, path);
                                    foreach (string name in Strings(rule.FindPropertyRelative("RequiredDescendantsAnywhere")))
                                    {
                                        if (!template.paths.Any(p => p.Split('/').Last() == name)) AddPath(template, name);
                                    }
                                }
                                var orientations = rule.FindPropertyRelative("PreferredOrientations");
                                if (orientations == null) continue;
                                for (int o = 0; o < orientations.arraySize; o++)
                                {
                                    var item = orientations.GetArrayElementAtIndex(o);
                                    string path = NormalizePath(item.FindPropertyRelative("ChildPath").stringValue);
                                    if (string.IsNullOrEmpty(path)) continue;
                                    var forward = Direction(EnumName(item.FindPropertyRelative("Forward")));
                                    var up = Direction(EnumName(item.FindPropertyRelative("Up")));
                                    if (Vector3.Cross(forward, up).sqrMagnitude < .5f) throw new InvalidOperationException("Invalid orientation in vehicle rules: " + path);
                                    // Preferred orientations apply only to required nodes, as in the validator.
                                    template.orientations[path] = Quaternion.LookRotation(forward, up);
                                }
                            }
                            result.Add(template);
                        }
                    }
                }
            }
            return result.OrderBy(t => t.vehicle, StringComparer.Ordinal).ThenBy(t => t.part, StringComparer.Ordinal).ThenBy(t => t.brand, StringComparer.Ordinal).ToList();
        }
        private static string NormalizePath(string value) => (value ?? "").Trim().Replace('\\', '/').Trim('/');
        private static void AddPath(Template template, string value)
        {
            string path = NormalizePath(value); if (string.IsNullOrEmpty(path)) return;
            if (path.Split('/').Any(s => string.IsNullOrEmpty(s) || s == "." || s == "..")) throw new InvalidOperationException("Invalid path in vehicle rules: " + value);
            if (!template.paths.Contains(path)) template.paths.Add(path);
        }
        private static Vector3 Direction(string name)
        {
            switch (name)
            {
                case "Forward": return Vector3.forward; case "Back": return Vector3.back;
                case "Left": return Vector3.left; case "Right": return Vector3.right;
                case "Up": return Vector3.up; case "Down": return Vector3.down;
                default: throw new InvalidOperationException("Unknown anchor direction: " + name);
            }
        }
    }
}
