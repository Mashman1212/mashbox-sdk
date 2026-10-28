using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal bool CameraOptionsVisible { get; private set; }
        private SceneView cameraOptionsView;
        private Quaternion cameraOptionsLastRotation;
        private Vector3 cameraOptionsEuler;
        private static readonly string[] CameraProjectionNames = { "Perspective", "Orthographic" };
        internal void ShowCameraOptions(SceneView view, bool visible)
        {
            if (visible && !view) return;
            cameraOptionsView = view; CameraOptionsVisible = visible;
            if (visible) LightingOptionsVisible = false;
            if (visible) { cameraOptionsLastRotation = view.rotation; cameraOptionsEuler = view.rotation.eulerAngles; OpenToolOptions(); }
            repaint(); toolOptions?.Repaint();
        }
        internal static void ResetViewportCamera(SceneView view)
        {
            if (!view) return;
            view.ResetCameraSettings();
            MashBoxStudioViewport.NavigationSensitivity = 1f;
            SetDefaultViewportCameraPose(view);
        }
        internal static void SetDefaultViewportCameraPose(SceneView view)
        {
            if (!view) return;
            const float distance = 10f;
            float size = distance * Mathf.Sin(view.cameraSettings.fieldOfView * .5f * Mathf.Deg2Rad);
            view.in2DMode = false; view.orthographic = false; view.camera.orthographic = false;
            view.LookAtDirect(Vector3.zero, Quaternion.Euler(37, 38, 0), size);
            CameraChanged(view);
        }
        internal static Vector3 ViewportCameraPosition(SceneView view) => view.pivot - view.rotation * Vector3.forward * view.cameraDistance;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static void CameraChanged(SceneView view)
        {
            if (view is MashBoxStudioViewport studio) studio.MarkDirty(MashBoxStudioViewport.DirtyState.Camera);
            else view.Repaint();
        }
        internal static bool SetViewportCameraPose(SceneView view, Vector3 position, Vector3 euler)
        {
            if (!view || !Finite(position) || !Finite(euler)) return false;
            var rotation = Quaternion.Euler(euler);
            if (view.in2DMode)
            {
                float size = view.size;
                view.in2DMode = false; view.orthographic = true; view.size = size;
                view.camera.orthographic = true;
            }
            var target = position + rotation * Vector3.forward * view.cameraDistance;
            if (!Finite(target)) return false;
            view.LookAtDirect(target, rotation, view.size); CameraChanged(view); return true;
        }
        internal static bool SetViewportCameraLens(SceneView view, float fov, float near, float far, bool automatic)
        {
            if (!view || !Finite(fov) || !Finite(near) || !Finite(far)) return false;
            near = Mathf.Max(.0001f, near); far = Mathf.Max(near + .001f, far);
            if (far <= near) return false;
            var position = ViewportCameraPosition(view);
            view.cameraSettings.fieldOfView = Mathf.Clamp(fov, 1, 179);
            view.cameraSettings.nearClip = near; view.cameraSettings.farClip = far; view.cameraSettings.dynamicClip = automatic;
            view.LookAtDirect(position + view.rotation * Vector3.forward * view.cameraDistance, view.rotation, view.size);
            CameraChanged(view); return true;
        }
        internal static void SetViewportCameraProjection(SceneView view, bool orthographic)
        {
            var position = ViewportCameraPosition(view);
            var rotation = view.rotation; float size = view.size;
            view.in2DMode = false; view.orthographic = orthographic; view.size = size;
            // SceneView.cameraDistance reads the rendered camera's projection flag.
            // Synchronize it before querying the distance for the new projection.
            view.camera.orthographic = orthographic;
            view.LookAtDirect(position + rotation * Vector3.forward * view.cameraDistance, rotation, size);
            CameraChanged(view);
        }
        internal static bool SetViewportCameraSize(SceneView view, float value)
        {
            if (!view || !Finite(value) || value <= 0) return false;
            float size = view.orthographic ? value : view.size * (value / Mathf.Max(.0001f, view.cameraDistance));
            if (!Finite(size) || size <= 0) return false;
            view.LookAtDirect(view.pivot, view.rotation, size); CameraChanged(view); return true;
        }
        private static float CameraDisplayValue(float value) => (float)Math.Round((double)value, 4, MidpointRounding.AwayFromZero);
        private static float CameraFloatField(string label, float value)
        {
            EditorGUI.BeginChangeCheck();
            float edited = EditorGUILayout.FloatField(label, CameraDisplayValue(value));
            return EditorGUI.EndChangeCheck() ? edited : value;
        }
        private static Vector3 CameraVectorField(string label, Vector3 value)
        {
            var displayed = new Vector3(CameraDisplayValue(value.x), CameraDisplayValue(value.y), CameraDisplayValue(value.z));
            var edited = EditorGUILayout.Vector3Field(label, displayed);
            // Editing one axis must not round the other camera components.
            return new Vector3(edited.x != displayed.x ? edited.x : value.x,
                edited.y != displayed.y ? edited.y : value.y,
                edited.z != displayed.z ? edited.z : value.z);
        }
        private void DrawViewportCameraOptions()
        {
            var view = commandView ? commandView : cameraOptionsView;
            if (!view) { EditorGUILayout.HelpBox("Open a Studio viewport to edit its camera.", MessageType.Info); return; }
            EditorGUI.BeginChangeCheck();
            float sensitivity = EditorGUILayout.Slider(new GUIContent("Navigation sensitivity", "Shared multiplier for orbit, pan, drag zoom and mouse-wheel zoom. Lower values give finer control up close; 1 is the standard speed."), MashBoxStudioViewport.NavigationSensitivity, .05f, 4f);
            if (EditorGUI.EndChangeCheck()) MashBoxStudioViewport.NavigationSensitivity = sensitivity;
            EditorGUI.BeginChangeCheck();
            int projection = EditorGUILayout.Popup("Projection", view.orthographic ? 1 : 0, CameraProjectionNames);
            if (EditorGUI.EndChangeCheck()) SetViewportCameraProjection(view, projection == 1);
            EditorGUI.BeginChangeCheck();
            float fov;
            using (new EditorGUI.DisabledScope(view.orthographic))
                fov = EditorGUILayout.Slider(new GUIContent("Field of view", "Vertical field of view in degrees."), view.cameraSettings.fieldOfView, 1, 179);
            bool automatic = EditorGUILayout.Toggle("Automatic clipping", view.cameraSettings.dynamicClip);
            float near = view.cameraSettings.nearClip, far = view.cameraSettings.farClip;
            using (new EditorGUI.DisabledScope(automatic))
            {
                near = CameraFloatField("Near clip", near);
                far = CameraFloatField("Far clip", far);
            }
            if (EditorGUI.EndChangeCheck()) SetViewportCameraLens(view, fov, near, far, automatic);
            EditorGUILayout.Space(6);
            if (Quaternion.Angle(cameraOptionsLastRotation, view.rotation) > .0001f)
            { cameraOptionsLastRotation = view.rotation; cameraOptionsEuler = view.rotation.eulerAngles; }
            EditorGUI.BeginChangeCheck();
            var position = CameraVectorField("Position", ViewportCameraPosition(view));
            var euler = CameraVectorField("Rotation", cameraOptionsEuler);
            if (EditorGUI.EndChangeCheck() && SetViewportCameraPose(view, position, euler))
            { cameraOptionsLastRotation = view.rotation; cameraOptionsEuler = euler; }
            EditorGUI.BeginChangeCheck();
            float size = CameraFloatField(view.orthographic ? "View size" : "Orbit distance", view.orthographic ? view.size : view.cameraDistance);
            if (EditorGUI.EndChangeCheck()) SetViewportCameraSize(view, size);
            EditorGUILayout.Space(6);
            if (GUILayout.Button("Reset to Defaults"))
            {
                GUI.FocusControl(null);
                ResetViewportCamera(view);
                cameraOptionsLastRotation = view.rotation; cameraOptionsEuler = view.rotation.eulerAngles;
            }
        }
    }

    public sealed partial class MashBoxStudioViewport
    {
        internal const string NavigationSensitivityKey = "Studio.Camera.NavigationSensitivity";
        internal static float NavigationSensitivity
        {
            get => Mathf.Clamp(StudioPreferenceStore.GetFloat(NavigationSensitivityKey, 1f), .05f, 4f);
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                StudioPreferenceStore.SetFloat(NavigationSensitivityKey, Mathf.Clamp(value, .05f, 4f));
            }
        }
        // Viewport Overlay: fixed viewport actions, independent of toolbar overlays.
        private IMGUIContainer viewportCameraButton, viewportLightingButton, viewportIsolateButton, viewportShowAllButton;
        private static readonly GUIContent IsolateHint = new GUIContent("", "Isolate selected");
        private static readonly GUIContent ShowAllHint = new GUIContent("", "Show all");
        private static readonly GUIContent CameraSettingsHint = new GUIContent("", "Camera settings");
        private static readonly GUIContent LightingSettingsHint = new GUIContent("", "Viewport lighting settings");
        private static Color ViewportOverlayBackground(bool hover)
        {
            var color = hover ? StudioTheme.Current.Button : StudioTheme.Current.Header;
            color.a *= .5f;
            return color;
        }
        private void BindViewportOverlayButton(ref IMGUIContainer button, string name, Action draw)
        {
            bool created = button == null;
            BindViewportControl(ref button, name, draw);
            if (created)
            {
                var element = button;
                element.RegisterCallback<MouseEnterEvent>(_ => element.MarkDirtyRepaint());
                element.RegisterCallback<MouseLeaveEvent>(_ => element.MarkDirtyRepaint());
            }
            button.AddToClassList("mashbox-viewport-overlay-button");
            button.style.width = button.style.height = 24;
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius = 4;
            button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 4;
            button.style.overflow = Overflow.Hidden;
            button.style.backgroundColor = ViewportOverlayBackground(false);
            button.style.opacity = .7f;
            button.focusable = false;
        }
        private void BindCameraSettingsButton()
        {
            BindViewportOverlayButton(ref viewportCameraButton, "mashbox-camera-settings-button", DrawCameraSettingsButton);
            BindViewportOverlayButton(ref viewportLightingButton, "mashbox-lighting-settings-button", DrawLightingSettingsButton);
            BindViewportOverlayButton(ref viewportIsolateButton, "mashbox-isolate-selected-button", DrawIsolateButton);
            BindViewportOverlayButton(ref viewportShowAllButton, "mashbox-show-all-button", DrawShowAllButton);
        }
        private void LayoutCameraSettingsButton(float left, float top)
        {
            LayoutViewportOverlayButton(viewportCameraButton, left, top);
            LayoutViewportOverlayButton(viewportLightingButton, left + 28, top);
            LayoutViewportOverlayButton(viewportIsolateButton, left + 56, top);
            LayoutViewportOverlayButton(viewportShowAllButton, left + 84, top);
        }
        private void LayoutViewportOverlayButton(IMGUIContainer button, float left, float top)
        {
            if (button == null) return;
            button.style.left = left; button.style.top = top;
            button.style.display = Studio && Studio.IsModelingMode ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private bool DrawViewportOverlayButton(IMGUIContainer element, GUIContent hint, string iconName)
        {
            var rect = new Rect(0, 0, 24, 24);
            // Ordinary action buttons: persistent translucent backing and hover highlight only.
            element.style.backgroundColor = ViewportOverlayBackground(rect.Contains(Event.current.mousePosition));
            bool clicked = GUI.Button(rect, hint, GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                var previous = GUI.color;
                GUI.color = StudioTheme.Current.Text;
                var icon = StudioToolbarIcons.Get(iconName);
                if (icon) GUI.DrawTexture(new Rect(4, 4, 16, 16), icon, ScaleMode.ScaleToFit, true);
                GUI.color = previous;
            }
            return clicked;
        }
        private void DrawCameraSettingsButton()
        {
            var editor = Studio?.ToolbarModelEditor;
            if (editor == null) return;
            if (DrawViewportOverlayButton(viewportCameraButton, CameraSettingsHint, "Camera Icon"))
                editor.ShowCameraOptions(this, true);
        }
        private void DrawIsolateButton()
        {
            var editor=Studio?.ToolbarModelEditor;if(editor==null)return;
            using(new EditorGUI.DisabledScope(!editor.ViewportIsolationActive && !editor.CanIsolateViewportSelection))
                if(DrawViewportOverlayButton(viewportIsolateButton,IsolateHint,"SceneViewOrtho")) editor.RunViewportIsolation(false);
        }
        private void DrawShowAllButton()
        {
            var editor=Studio?.ToolbarModelEditor;if(editor==null)return;
            if(DrawViewportOverlayButton(viewportShowAllButton,ShowAllHint,"scenevis_visible")) editor.RunViewportIsolation(true);
        }
        private void DrawLightingSettingsButton()
        {
            var editor = Studio?.ToolbarModelEditor;
            if (editor == null) return;
            if (DrawViewportOverlayButton(viewportLightingButton, LightingSettingsHint, "Light Icon"))
                editor.ShowLightingOptions(true);
        }
    }
}
