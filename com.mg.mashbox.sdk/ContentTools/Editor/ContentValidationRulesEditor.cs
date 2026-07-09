#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.ContentTools;
using UnityEditor;
using UnityEngine;

namespace ContentTools.Editor
{
    [CustomEditor(typeof(ContentValidationRules))]
    public class ContentValidationRulesEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();

            if (changed)
            {
                var rules = (ContentValidationRules)target;

                foreach (var rule in rules.ItemRules)
                {
                    var shaderTypes = ContentValidationRules.GetAllowedShaderTypes(rule);
                    if (shaderTypes.Count == 0)
                        continue;

                    var shaders = shaderTypes
                        .Select(ContentValidationRules.GetShader)
                        .Where(shader => shader != null)
                        .ToList();

                    if (shaders.Count == 0)
                        continue;

                    // Only rebuild slots if empty
                    if (rule.Slots == null || rule.Slots.Count == 0)
                    {
                        PopulateShaderSlots(rule, shaders);
                    }
                }

                EditorUtility.SetDirty(target);
            }

            serializedObject.ApplyModifiedProperties();
        }

        void PopulateShaderSlots(ContentValidationRules.ItemRule rule, IEnumerable<Shader> shaders)
        {
            rule.Slots.Clear();
            var propertyNames = new HashSet<string>();

            foreach (var shader in shaders)
            {
                int propertyCount = ShaderUtil.GetPropertyCount(shader);

                for (int i = 0; i < propertyCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                        continue;

                    string propName = ShaderUtil.GetPropertyName(shader, i);

                    if (propName.StartsWith("unity_"))
                        continue;

                    if (propName.StartsWith("_SampleTexture"))
                        continue;

                    propertyNames.Add(propName);
                }
            }

            foreach (var propName in propertyNames.OrderBy(name => name))
            {
                rule.Slots.Add(new ContentValidationRules.TextureSlotLimit
                {
                    ShaderProperty = propName,
                    MaxSize = ContentValidationRules.TextureSize._512
                });
            }
        }
    }
}
#endif
