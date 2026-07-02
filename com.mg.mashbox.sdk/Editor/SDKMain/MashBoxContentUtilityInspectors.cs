#if UNITY_EDITOR
using MashBoxSDK.ContentUtility;
using UnityEditor;

namespace MashBoxSDK.EditorResources
{
    internal abstract class MashBoxContentUtilityInspectorBase : UnityEditor.Editor
    {
        protected abstract string Description { get; }
        protected virtual string SetupNotes => null;

        public override void OnInspectorGUI()
        {
            MashBoxInspectorHeaderUtility.DrawScriptHeader();

            if (!string.IsNullOrWhiteSpace(Description))
                EditorGUILayout.HelpBox(Description, MessageType.None);

            if (!string.IsNullOrWhiteSpace(SetupNotes))
                EditorGUILayout.HelpBox(SetupNotes, MessageType.Info);

            DrawDefaultInspector();
        }
    }

    [CustomEditor(typeof(MBSimpleRotator))]
    internal sealed class MBSimpleRotatorInspector : MashBoxContentUtilityInspectorBase
    {
        protected override string Description =>
            "Simple Rotator continuously spins an object around a chosen axis. It works well for display turntables, pickups, props, and hero-item previews.";
    }

    [CustomEditor(typeof(MBSimpleFloat))]
    internal sealed class MBSimpleFloatInspector : MashBoxContentUtilityInspectorBase
    {
        protected override string Description =>
            "Simple Float moves an object back and forth along a local axis using a sine wave. It is useful for collectibles, hover props, and light ambient motion.";
    }

    [CustomEditor(typeof(MBSimplePulseScale))]
    internal sealed class MBSimplePulseScaleInspector : MashBoxContentUtilityInspectorBase
    {
        protected override string Description =>
            "Simple Pulse Scale gently expands and contracts an object over time. It is useful for subtle emphasis, energy props, or stylized content presentation.";
    }

    [CustomEditor(typeof(MBSimpleLookAt))]
    internal sealed class MBSimpleLookAtInspector : MashBoxContentUtilityInspectorBase
    {
        protected override string Description =>
            "Simple Look At rotates an object so it continuously faces a target transform or the main camera. It is useful for billboards, hero props, and presentation helpers.";
    }

    [CustomEditor(typeof(MBSimpleOrbit))]
    internal sealed class MBSimpleOrbitInspector : MashBoxContentUtilityInspectorBase
    {
        protected override string Description =>
            "Simple Orbit moves an object around a local or external pivot on a chosen axis. It is useful for display turntables, hovering props, and stylized motion loops.";
    }

    [CustomEditor(typeof(MBSimpleMaterialPanner))]
    internal sealed class MBSimpleMaterialPannerInspector : MashBoxContentUtilityInspectorBase
    {
        protected override string Description =>
            "Simple Material Panner offsets a texture over time on a material instance. It is useful for emissive movement, conveyor-style surfaces, and lightweight material animation.";
    }

    [CustomEditor(typeof(MBSimpleVisibilityToggle))]
    internal sealed class MBSimpleVisibilityToggleInspector : MashBoxContentUtilityInspectorBase
    {
        protected override string Description =>
            "Simple Visibility Toggle shows and hides renderers on a timed loop. It is useful for blinking props, intermittent VFX meshes, and simple presentation cues.";
    }
}
#endif
