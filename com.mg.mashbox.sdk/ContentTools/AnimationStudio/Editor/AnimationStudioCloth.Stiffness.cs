using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private bool clothAdvancedStiffness;

        // Logarithmic control spans useful XPBD compliance values without
        // changing stored profiles or rounding them just by drawing the UI.
        internal static float ClothComplianceToStiffness(float compliance)
            => compliance <= 0 ? 1 : Mathf.Clamp01(-Mathf.Log10(Mathf.Max(1e-8f, compliance)) / 8);
        internal static float ClothStiffnessToCompliance(float stiffness)
            => stiffness >= 1 ? 0 : Mathf.Pow(10, -8 * Mathf.Clamp01(stiffness));

        private void DrawClothStiffness(SerializedObject serialized)
        {
            GUILayout.Label("FABRIC STIFFNESS", EditorStyles.boldLabel);
            void Slider(string field, string label, string tooltip)
            {
                var property = serialized.FindProperty(field);
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.Slider(new GUIContent(label, tooltip), ClothComplianceToStiffness(property.floatValue) * 100, 0, 100);
                if (EditorGUI.EndChangeCheck()) property.floatValue = ClothStiffnessToCompliance(value / 100);
            }
            Slider("bendCompliance", "Bend stiffness", "Higher resists folding and crumpling, spreading a pull across the surrounding cloth. This is a relative control, not a measured material percentage.");
            Slider("stretchCompliance", "Stretch stiffness", "Higher resists lengthening between particles. Keep this high for cloth that should not behave like rubber.");
            GUILayout.Label("Raise Bend stiffness for broader, firmer folds. Raise Stretch stiffness to resist stretching. Changes apply while running.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.BeginHorizontal();
            void Preset(string label, float bend, float stretch, int iterations, int substeps)
            {
                if (!GUILayout.Button(label)) return;
                serialized.FindProperty("bendCompliance").floatValue = bend;
                serialized.FindProperty("stretchCompliance").floatValue = stretch;
                GUI.changed = true;
            }
            if (GUILayout.Button(new GUIContent("Flowing", "Soft folds with animation momentum. Sets Follow 0.75, Inertia 0.65, Damping 3, mild wind, and preserves solver quality. Painted weights and collision settings are preserved.")))
            {
                serialized.FindProperty("animationFollow").floatValue = .75f;
                serialized.FindProperty("animationInertia").floatValue = .65f;
                serialized.FindProperty("damping").floatValue = 3;
                serialized.FindProperty("bendCompliance").floatValue = .001f;
                serialized.FindProperty("stretchCompliance").floatValue = .000001f;
                serialized.FindProperty("wind").vector3Value = new Vector3(0,0,1);
                serialized.FindProperty("gust").floatValue = .2f;
                GUI.changed = true;
            }
            Preset("Soft", .001f, .000001f, 4, 2);
            Preset("Firm", .00001f, .0000001f, 8, 3);
            Preset("Stiff", .0000001f, 0, 12, 4);
            GUILayout.EndHorizontal();
            GUILayout.Label("Presets are starting points. A larger grab radius spreads the pull.", EditorStyles.wordWrappedMiniLabel);
            clothAdvancedStiffness = EditorGUILayout.Foldout(clothAdvancedStiffness, "Advanced compliance values", true);
            if (clothAdvancedStiffness)
            {
                EditorGUILayout.PropertyField(serialized.FindProperty("bendCompliance"));
                EditorGUILayout.PropertyField(serialized.FindProperty("stretchCompliance"));
                GUILayout.Label("Compliance is inverse stiffness: smaller values are stiffer; zero is the strongest constraint.", EditorStyles.wordWrappedMiniLabel);
            }
            GUILayout.Space(8);
            GUILayout.Label("SHAPE SUPPORT", EditorStyles.boldLabel);
            var retention = serialized.FindProperty("shapeRetention");
            EditorGUI.BeginChangeCheck();
            float support = EditorGUILayout.Slider(new GUIContent("Shape retention", "Soft support toward the animated garment. Higher reduces sag; zero keeps the previous cloth behavior."), retention.floatValue * 100, 0, 100);
            if (EditorGUI.EndChangeCheck()) retention.floatValue = support / 100;
            using (new EditorGUI.DisabledScope(retention.floatValue <= 0))
                EditorGUILayout.PropertyField(serialized.FindProperty("shapeDamping"), new GUIContent("Recovery damping"));
            if (GUILayout.Button(new GUIContent("Gentle support", "Set Shape Retention to 35% and Recovery Damping to 0.7. Keeps painting, collisions and other fabric settings unchanged.")))
            {
                retention.floatValue = .35f; serialized.FindProperty("shapeDamping").floatValue = .7f; GUI.changed = true;
            }
            GUILayout.Label("Softly supports the animated shape to reduce sag and cuff bunching. Applies live. Painted zero-distance vertices remain pinned.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);
        }
    }
}
