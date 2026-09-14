using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [CustomEditor(typeof(AnimationTake))]
    public sealed class AnimationTakeEditor : Editor
    {
        private bool advanced;
        private void OnEnable()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath("124ab1843b32445cb1f412eea26d415e"));
            if (icon) EditorGUIUtility.SetIconForObject(target,icon);
        }
        public override void OnInspectorGUI()
        {
            var take = (AnimationTake)target;
            EditorGUILayout.LabelField("ANIMATION TAKE",EditorStyles.boldLabel);
#if MashBoxDev
            if (GUILayout.Button("Open in Animation Studio",GUILayout.Height(32))) AnimationStudioWindow.OpenInStudio(take);
#endif
            EditorGUILayout.LabelField("Duration", ((float)take.lastFrame/Mathf.Max(1,take.frameRate)).ToString("0.##")+" seconds");
            EditorGUILayout.LabelField("Timing",take.frameRate+" fps · "+(take.lastFrame+1)+" frames");
            EditorGUILayout.LabelField("Pose keys",take.keys.Count.ToString());
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Character",take.character,typeof(GameObject),false);
            advanced = EditorGUILayout.Foldout(advanced,"Asset data (advanced)",true);
            if (advanced) DrawDefaultInspector();
        }
    }
}
