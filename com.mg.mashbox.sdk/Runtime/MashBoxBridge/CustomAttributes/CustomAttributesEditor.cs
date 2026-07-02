using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace MashBoxBridge.CustomAttributes
{
    internal static class InspectorButtonGUI
    {
        public static void DrawMethodButtons(UnityEngine.Object[] targets)
        {
            if (targets == null || targets.Length == 0 || targets[0] == null)
                return;

            bool drewButton = false;
            foreach (MethodInfo method in GetAllMethods(targets[0].GetType()))
            {
                if (!HasAttribute(method, "InspectorButtonAttribute") && !HasAttribute(method, "ButtonAttribute"))
                    continue;

                if (method.GetParameters().Length > 0)
                    continue;

                if (!drewButton)
                {
                    EditorGUILayout.Space(2f);
                    drewButton = true;
                }

                object buttonAttribute = GetAttribute(method, "ButtonAttribute") ?? GetAttribute(method, "InspectorButtonAttribute");
                string label = GetStringProperty(buttonAttribute, "Label");
                if (string.IsNullOrEmpty(label))
                    label = ObjectNames.NicifyVariableName(method.Name);

                if (GUILayout.Button(label))
                {
                    foreach (UnityEngine.Object target in targets)
                    {
                        if (target == null || !method.DeclaringType.IsAssignableFrom(target.GetType()))
                            continue;

                        method.Invoke(target, null);
                    }
                }
            }
        }

        private static IEnumerable<MethodInfo> GetAllMethods(Type type)
        {
            while (type != null)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    yield return method;

                type = type.BaseType;
            }
        }

        private static bool HasAttribute(MemberInfo member, string attributeName)
        {
            return GetAttribute(member, attributeName) != null;
        }

        private static object GetAttribute(MemberInfo member, string attributeName)
        {
            if (member == null)
                return null;

            return member.GetCustomAttributes(true).FirstOrDefault(attribute => attribute.GetType().Name == attributeName);
        }

        private static string GetStringProperty(object instance, string propertyName)
        {
            return instance?.GetType().GetProperty(propertyName)?.GetValue(instance) as string;
        }
    }

    [CustomEditor(typeof(UnityEngine.Object), true)]
    [CanEditMultipleObjects]
    public class EnhancedInspector : UnityEditor.Editor
    {
        private readonly Dictionary<string, bool> foldoutStatus = new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var changedMembers = new List<MemberInfo>();
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                SerializedProperty property = iterator.Copy();
                MemberInfo member = FindMember(target.GetType(), property.propertyPath);

                if (member != null && !ShouldDrawMember(target, member))
                    continue;

                DrawInfoBox(target, member);

                EditorGUI.BeginChangeCheck();
                DrawProperty(property, member);
                if (EditorGUI.EndChangeCheck() && member != null)
                    changedMembers.Add(member);
            }

            serializedObject.ApplyModifiedProperties();

            foreach (MemberInfo member in changedMembers)
                InvokeOnValueChanged(target, member);

            DrawShowInInspectorMembers(target, target.GetType());
            InspectorButtonGUI.DrawMethodButtons(targets);
            DrawOnInspectorGuiMethods(target, target.GetType());
        }

        private void DrawProperty(SerializedProperty property, MemberInfo member)
        {
            bool wasEnabled = GUI.enabled;
            if (HasAttribute(member, "DisplayAsStringAttribute"))
                GUI.enabled = false;

            GUIContent label = MakeLabel(member, property.displayName);

            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                property.objectReferenceValue is ScriptableObject scriptableObject)
            {
                string key = property.propertyPath;
                foldoutStatus.TryGetValue(key, out bool foldout);

                EditorGUILayout.BeginHorizontal();
                foldout = EditorGUILayout.Foldout(foldout, GUIContent.none, true);
                EditorGUILayout.PropertyField(property, label, false);
                EditorGUILayout.EndHorizontal();

                foldoutStatus[key] = foldout;

                if (foldout)
                {
                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    UnityEditor.Editor nestedEditor = CreateEditor(scriptableObject);
                    nestedEditor.OnInspectorGUI();
                    DestroyImmediate(nestedEditor);
                    EditorGUILayout.EndVertical();
                }
            }
            else
            {
                EditorGUILayout.PropertyField(property, label, true);
            }

            GUI.enabled = wasEnabled;
        }

        private static GUIContent MakeLabel(MemberInfo member, string fallback)
        {
            object attribute = GetAttribute(member, "LabelTextAttribute");
            string label = GetStringProperty(attribute, "Text");
            return string.IsNullOrEmpty(label) ? new GUIContent(fallback) : new GUIContent(label);
        }

        private static void DrawInfoBox(object target, MemberInfo member)
        {
            object attribute = GetAttribute(member, "InfoBoxAttribute");
            if (attribute == null)
                return;

            string visibleIfMemberName = GetStringProperty(attribute, "VisibleIfMemberName");
            if (!string.IsNullOrEmpty(visibleIfMemberName) && !EvaluateCondition(target, visibleIfMemberName))
                return;

            string message = GetStringProperty(attribute, "Message");
            if (!string.IsNullOrEmpty(message))
                EditorGUILayout.HelpBox(message, MessageType.Info);
        }

        private static bool ShouldDrawMember(object target, MemberInfo member)
        {
            object attribute = GetAttribute(member, "ShowIfAttribute");
            if (attribute == null)
                return true;

            string condition = GetStringProperty(attribute, "Condition");
            return EvaluateCondition(target, condition);
        }

        private static void InvokeOnValueChanged(object target, MemberInfo member)
        {
            object attribute = GetAttribute(member, "OnValueChangedAttribute");
            string callbackName = GetStringProperty(attribute, "CallbackName");
            if (string.IsNullOrEmpty(callbackName))
                return;

            MethodInfo method = FindMethod(target.GetType(), callbackName);
            if (method != null && method.GetParameters().Length == 0)
                method.Invoke(target, null);
        }

        private static void DrawMethodButtons(object target, Type type)
        {
            foreach (MethodInfo method in GetAllMethods(type))
            {
                if (!HasAttribute(method, "InspectorButtonAttribute") && !HasAttribute(method, "ButtonAttribute"))
                    continue;

                if (method.GetParameters().Length > 0)
                    continue;

                object buttonAttribute = GetAttribute(method, "ButtonAttribute") ?? GetAttribute(method, "InspectorButtonAttribute");
                string label = GetStringProperty(buttonAttribute, "Label");
                if (string.IsNullOrEmpty(label))
                    label = ObjectNames.NicifyVariableName(method.Name);

                if (GUILayout.Button(label))
                    method.Invoke(target, null);
            }
        }

        private static void DrawOnInspectorGuiMethods(object target, Type type)
        {
            foreach (MethodInfo method in GetAllMethods(type))
            {
                if (!HasAttribute(method, "OnInspectorGUIAttribute") || method.GetParameters().Length > 0)
                    continue;

                method.Invoke(target, null);
            }
        }

        private static void DrawShowInInspectorMembers(object target, Type type)
        {
            foreach (FieldInfo field in GetAllFields(type))
            {
                if (!HasAttribute(field, "ShowInInspectorAttribute"))
                    continue;

                object value = field.GetValue(target);
                EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(field.Name), value?.ToString() ?? "null");
            }

            foreach (PropertyInfo property in GetAllProperties(type))
            {
                if (!HasAttribute(property, "ShowInInspectorAttribute") || property.GetIndexParameters().Length > 0)
                    continue;

                object value = property.GetValue(target);
                EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(property.Name), value?.ToString() ?? "null");
            }
        }

        private static bool EvaluateCondition(object target, string condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return true;

            condition = condition.Trim();
            bool invert = condition.StartsWith("!");
            condition = condition.TrimStart('!', '$');

            object value = null;
            FieldInfo field = FindField(target.GetType(), condition);
            if (field != null)
                value = field.GetValue(target);

            PropertyInfo property = value == null ? FindProperty(target.GetType(), condition) : null;
            if (property != null && property.GetIndexParameters().Length == 0)
                value = property.GetValue(target);

            MethodInfo method = value == null ? FindMethod(target.GetType(), condition) : null;
            if (method != null && method.GetParameters().Length == 0)
                value = method.Invoke(target, null);

            bool result = value switch
            {
                bool boolValue => boolValue,
                UnityEngine.Object unityObject => unityObject != null,
                null => false,
                _ => true
            };

            return invert ? !result : result;
        }

        private static MemberInfo FindMember(Type type, string propertyPath)
        {
            string fieldName = propertyPath.Split('.')[0];
            return FindField(type, fieldName);
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field;

                type = type.BaseType;
            }

            return null;
        }

        private static PropertyInfo FindProperty(Type type, string propertyName)
        {
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                    return property;

                type = type.BaseType;
            }

            return null;
        }

        private static MethodInfo FindMethod(Type type, string methodName)
        {
            while (type != null)
            {
                MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                    return method;

                type = type.BaseType;
            }

            return null;
        }

        private static IEnumerable<FieldInfo> GetAllFields(Type type)
        {
            while (type != null)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    yield return field;

                type = type.BaseType;
            }
        }

        private static IEnumerable<PropertyInfo> GetAllProperties(Type type)
        {
            while (type != null)
            {
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    yield return property;

                type = type.BaseType;
            }
        }

        private static IEnumerable<MethodInfo> GetAllMethods(Type type)
        {
            while (type != null)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    yield return method;

                type = type.BaseType;
            }
        }

        private static bool HasAttribute(MemberInfo member, string attributeName)
        {
            return GetAttribute(member, attributeName) != null;
        }

        private static object GetAttribute(MemberInfo member, string attributeName)
        {
            if (member == null)
                return null;

            return member.GetCustomAttributes(true).FirstOrDefault(attribute => attribute.GetType().Name == attributeName);
        }

        private static string GetStringProperty(object instance, string propertyName)
        {
            return instance?.GetType().GetProperty(propertyName)?.GetValue(instance) as string;
        }
    }
}
#endif
