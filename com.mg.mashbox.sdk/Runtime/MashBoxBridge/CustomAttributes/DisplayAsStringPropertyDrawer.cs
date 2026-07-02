
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MashBoxBridge.CustomAttributes
{
    [CustomPropertyDrawer(typeof(DisplayAsStringAttribute))]
    public class DisplayAsStringDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            string valueStr;

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    valueStr = property.intValue.ToString();
                    break;
                case SerializedPropertyType.Boolean:
                    valueStr = property.boolValue.ToString();
                    break;
                case SerializedPropertyType.Float:
                    valueStr = property.floatValue.ToString("0.000");
                    break;
                case SerializedPropertyType.String:
                    valueStr = property.stringValue;
                    break;
                default:
                    valueStr = "(not supported)";
                    break;
            }
            EditorGUI.LabelField(position, label.text, valueStr);
        }
    }
}

#endif