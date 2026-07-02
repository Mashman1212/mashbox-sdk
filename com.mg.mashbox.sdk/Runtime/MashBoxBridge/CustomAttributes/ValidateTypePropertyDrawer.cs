#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxBridge.CustomAttributes
{
    [CustomPropertyDrawer(typeof(ValidateTypeAttribute))]
    public class ValidateTypePropertyDrawer : PropertyDrawer, IDisposable
    {
        private SerializedProperty cachedProperty;
        private bool isValid = false;
        public ValidateTypePropertyDrawer()
        {
            EditorApplication.update += Update;
        }
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (cachedProperty != property)
            {
                cachedProperty = property;
                isValid = Validate(property);

                // Check if the property is invalid after validation
                if (!isValid)
                {
                    // Log an error message
                    if(property.objectReferenceValue != null)
                    Debug.LogError("Invalid property value. Value has been set to null.");
                    // Make the property value null
                    property.objectReferenceValue = null;
                    // Do not forget to apply the changes to the serialized object
                    property.serializedObject.ApplyModifiedProperties();
                }
            }

            EditorGUI.BeginProperty(position, label, property);

            if (!isValid)
            {
                GUI.color = Color.red;
            }

            var validateAttribute = (ValidateTypeAttribute)attribute;
            property.objectReferenceValue = EditorGUILayout.ObjectField(label, property.objectReferenceValue, typeof(UnityEngine.Object), validateAttribute.AllowSceneObjects);
            GUI.color = Color.white;
            EditorGUI.EndProperty();
        }
        void Update()
        {
            if (cachedProperty != null)
            {
                isValid = Validate(cachedProperty);
            }
        }
        
        bool Validate(SerializedProperty property)
        {  
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
            {
                return false;
            }

            var validateAttribute = (ValidateTypeAttribute)attribute;
            return validateAttribute.PropertyType.IsInstanceOfType(property.objectReferenceValue);
        }

        public void Dispose()
        {
            EditorApplication.update -= Update;
        }
    }
}

#endif