using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioVehiclePartCatalog
    {
        private const string RulesGuid = "c7baf1b43fbb9564ea4bcc515370d9ad";
        private const string BmxFolderGuid = "7406e492c18440644b2d473afa63f908";
        private const string ScooterFolderGuid = "06acbd7466271a146b771e9def5d5c00";
        private static UnityEngine.Object cachedAsset;
        private static int cachedRevision;
        private static List<Template> cachedTemplates;
        static StudioVehiclePartCatalog()
        {
            EditorApplication.projectChanged += () => cachedTemplates = null;
        }
        internal sealed class Template
        {
            internal string vehicle, part, brand, color;
            internal GameObject sourceModel;
            internal int ruleCount, maxRenderers = -1;
            internal readonly List<string> paths = new List<string>();
            internal readonly Dictionary<string, Quaternion> orientations = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            internal string MenuPath => "Vehicle Part/" + vehicle + "/" + part + (string.IsNullOrEmpty(brand) ? "" : " (" + brand + ")") + (ruleCount == 0 && !sourceModel ? " (mesh only)" : "");
        }
        internal static UnityEngine.Object RulesAsset => AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(RulesGuid));
        internal static StudioModelDocument.VehiclePartData InferPart(string name)
        {
            var tags = (name ?? "").Split('_');
            if (tags.Length < 2) return null;
            var candidates = Load().Where(t => string.Equals(t.vehicle, tags[0], StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0) return null;
            string part = tags[1].Trim();
            var template = candidates.FirstOrDefault(t => string.Equals(t.part.Replace(" ", ""), part.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
            // Recognize the vehicle prefix even when the part tag needs correction.
            // Validation reports naming problems without changing the authored name.
            return template != null ? StudioModelDocument.VehiclePartData.From(template)
                : new StudioModelDocument.VehiclePartData { vehicle = candidates[0].vehicle, part = part, targetAnchor = "" };
        }
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
            int revision = EditorUtility.GetDirtyCount(asset);
            if (cachedTemplates != null && cachedAsset == asset && cachedRevision == revision) return cachedTemplates;
            var bmxModels = LoadVehicleModels(BmxFolderGuid, "BMX");
            var scooterModels = LoadVehicleModels(ScooterFolderGuid, "Scooter");
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
                            if (vehicle == "Scooter") scooterModels.TryGetValue(part.Replace(" ", ""), out template.sourceModel);
                            if (vehicle == "BMX")
                            {
                                string modelPart = part.Replace(" ", "");
                                if (!bmxModels.TryGetValue(modelPart, out template.sourceModel))
                                {
                                    string legacyPart = part == "Front Hub" || part == "Rear Hub" ? "Hub" : part == "Mag" ? "MagWheel" : modelPart;
                                    bmxModels.TryGetValue(legacyPart, out template.sourceModel);
                                }
                            }
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
            cachedAsset = asset; cachedRevision = revision;
            return cachedTemplates = result.OrderBy(t => t.vehicle, StringComparer.Ordinal).ThenBy(t => t.part, StringComparer.Ordinal).ThenBy(t => t.brand, StringComparer.Ordinal).ToList();
        }
        private static Dictionary<string, GameObject> LoadVehicleModels(string folderGuid, string vehicle)
        {
            var models = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            string folder = AssetDatabase.GUIDToAssetPath(folderGuid);
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return models;
            foreach (string path in AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal))
            {
                string[] fields = Path.GetFileNameWithoutExtension(path).Split('_');
                if (fields.Length < 2 || !string.Equals(fields[0], vehicle, StringComparison.OrdinalIgnoreCase)) continue;
                string part = fields[1].Replace(" ", "");
                if (models.ContainsKey(part)) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model) models.Add(part, model);
            }
            return models;
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
