using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace MashBoxSDK.AnimationStudio
{
    [InitializeOnLoad]
    internal static class StudioAnchorCatalog
    {
        // The SDK's canonical ContentValidationRules asset; GUID survives package/folder moves.
        private const string RulesGuid = "c7baf1b43fbb9564ea4bcc515370d9ad";
        private static string[] names;
        internal static string[] Names => names ?? (names = LoadNames());
        static StudioAnchorCatalog() { EditorApplication.projectChanged += () => names = null; }
        internal static bool Contains(string name) => Array.IndexOf(Names,name) >= 0;
        private static string[] LoadNames()
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(RulesGuid));
            if (!asset) return Array.Empty<string>();
            var found = new HashSet<string>(StringComparer.Ordinal);
            using (var serialized = new SerializedObject(asset))
            {
                var rules = serialized.FindProperty("ItemRules");
                if (rules == null) return Array.Empty<string>();
                void AddPath(string path)
                {
                    foreach (var segment in (path ?? "").Split('/'))
                        if (Regex.IsMatch(segment,@"^[A-Za-z0-9_]+_Anchor(?:_[0-9]+)?$")) found.Add(segment);
                }
                for (int i=0;i<rules.arraySize;i++)
                {
                    var rule = rules.GetArrayElementAtIndex(i);
                    foreach (var field in new[]{"RequiredChildren","RequiredDescendantsAnywhere"})
                    {
                        var paths = rule.FindPropertyRelative(field);
                        if (paths != null) for(int j=0;j<paths.arraySize;j++) AddPath(paths.GetArrayElementAtIndex(j).stringValue);
                    }
                    var orientations = rule.FindPropertyRelative("PreferredOrientations");
                    if (orientations != null) for(int j=0;j<orientations.arraySize;j++)
                        AddPath(orientations.GetArrayElementAtIndex(j).FindPropertyRelative("ChildPath").stringValue);
                }
            }
            return found.OrderBy(n=>n,StringComparer.Ordinal).ToArray();
        }
    }
}
