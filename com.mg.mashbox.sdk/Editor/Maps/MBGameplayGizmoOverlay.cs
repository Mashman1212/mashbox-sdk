using MashBoxSDK.Maps;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;

namespace MashBoxSDK.MapTools
{
    [Overlay(typeof(SceneView), "MashBox Gameplay Gizmos", true)]
    public sealed class MBGameplayGizmoOverlay : ToolbarOverlay
    {
        public MBGameplayGizmoOverlay() : base(MBGameplayGizmoToggle.Id)
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public sealed class MBGameplayGizmoToggle : EditorToolbarToggle
    {
        public const string Id = "MashBox/Gameplay Gizmos";

        public MBGameplayGizmoToggle()
        {
            text = "Gameplay Gizmos";
            tooltip = "Show or hide MashBox race and gameplay gizmos in the Scene view.";
            value = MBGameplayGizmoVisibility.Visible;

            this.RegisterValueChangedCallback(changeEvent =>
            {
                MBGameplayGizmoVisibility.Visible = changeEvent.newValue;
                SceneView.RepaintAll();
            });
            this.RegisterCallback<AttachToPanelEvent>(_ => MBGameplayGizmoVisibility.Changed += SyncFromSharedSetting);
            this.RegisterCallback<DetachFromPanelEvent>(_ => MBGameplayGizmoVisibility.Changed -= SyncFromSharedSetting);
        }

        private void SyncFromSharedSetting()
        {
            SetValueWithoutNotify(MBGameplayGizmoVisibility.Visible);
        }
    }
}
