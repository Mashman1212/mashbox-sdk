#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.EditorResources
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(MonoBehaviour), true)]
    internal class MashBoxMonoBehaviourInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (ShouldDrawHeader(target))
                MashBoxInspectorHeaderUtility.DrawScriptHeader();

            DrawDefaultInspector();
        }

        private static bool ShouldDrawHeader(Object inspectedObject)
        {
            return inspectedObject != null
                && (IsMashBoxSdkNamespace(inspectedObject) || IsMashBoxSdkScriptAsset(inspectedObject));
        }

        private static bool IsMashBoxSdkNamespace(Object inspectedObject)
        {
            return inspectedObject.GetType().Namespace != null
                && inspectedObject.GetType().Namespace.StartsWith("MashBoxSDK");
        }

        private static bool IsMashBoxSdkScriptAsset(Object inspectedObject)
        {
            if (inspectedObject is not MonoBehaviour behaviour)
                return false;

            var script = MonoScript.FromMonoBehaviour(behaviour);
            var scriptPath = script != null ? AssetDatabase.GetAssetPath(script) : string.Empty;
            return scriptPath.Contains("com.mg.mashbox.sdk");
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(ScriptableObject), true)]
    internal class MashBoxScriptableObjectInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (ShouldDrawHeader(target))
                MashBoxInspectorHeaderUtility.DrawScriptHeader();

            DrawDefaultInspector();
        }

        private static bool ShouldDrawHeader(Object inspectedObject)
        {
            return inspectedObject != null
                && (IsMashBoxSdkNamespace(inspectedObject) || IsMashBoxSdkScriptAsset(inspectedObject));
        }

        private static bool IsMashBoxSdkNamespace(Object inspectedObject)
        {
            return inspectedObject.GetType().Namespace != null
                && inspectedObject.GetType().Namespace.StartsWith("MashBoxSDK");
        }

        private static bool IsMashBoxSdkScriptAsset(Object inspectedObject)
        {
            if (inspectedObject is not ScriptableObject scriptableObject)
                return false;

            var script = MonoScript.FromScriptableObject(scriptableObject);
            var scriptPath = script != null ? AssetDatabase.GetAssetPath(script) : string.Empty;
            return scriptPath.Contains("com.mg.mashbox.sdk");
        }
    }
}
#endif
