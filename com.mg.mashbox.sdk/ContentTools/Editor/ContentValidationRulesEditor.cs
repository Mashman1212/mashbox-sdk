#if UNITY_EDITOR
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

            SerializedProperty rulesProp = serializedObject.FindProperty("ItemRules");

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();

            if (changed)
            {
                var rules = (ContentValidationRules)target;

                foreach (var rule in rules.ItemRules)
                {
                    if (rule.ShaderType == ContentValidationRules.ShaderType.Null)
                        continue;

                    Shader shader = ContentValidationRules.GetShader(rule.ShaderType);
                    if (shader == null)
                        continue;

                    // Only rebuild slots if empty
                    if (rule.Slots == null || rule.Slots.Count == 0)
                    {
                        PopulateShaderSlots(rule, shader);
                    }
                }

                EditorUtility.SetDirty(target);
            }

            serializedObject.ApplyModifiedProperties();
        }

        void PopulateShaderSlots(ContentValidationRules.ItemRule rule, Shader shader)
        {
            rule.Slots.Clear();

            int propertyCount = ShaderUtil.GetPropertyCount(shader);

            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                    continue;

                string propName = ShaderUtil.GetPropertyName(shader, i);

                if (propName.StartsWith("unity_"))
                    continue;

                if (propName.StartsWith("_SampleTexture"))
                {
                    continue;
                }
                
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