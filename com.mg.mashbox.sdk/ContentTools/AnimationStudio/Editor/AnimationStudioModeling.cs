using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private bool modelingMode;
        [SerializeField] private StudioModelDocument modelDocument;
        private StudioModelEditor modelEditor;
        private Quaternion animationCameraRotation;
        private Vector3 animationCameraPivot;
        private float animationCameraSize;
        private bool savedAnimationCamera;

        private void EnsureModelEditor()
        {
            if (modelEditor != null) return;
            if (!modelDocument) modelDocument = StudioModelDocument.OpenFile(EditorPrefs.GetString("MashBox.LastModel." + Application.dataPath, ""));
            if (modelDocument && AssetDatabase.Contains(modelDocument)) modelDocument = modelDocument.SaveAs(StudioModelDocument.NewWorkspacePath(modelDocument.name));
            if (modelDocument) EditorPrefs.SetString("MashBox.LastModel." + Application.dataPath, modelDocument.WorkspacePath);
            modelEditor = new StudioModelEditor(() => { Repaint(); viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene); });
            modelEditor.WorkspaceSavedAs = LoadStudioModel;
            if (modelDocument) modelEditor.Load(modelDocument);
        }
        private void ReleaseModelEditor() { modelEditor?.Dispose(); modelEditor = null; }
        private void LoadStudioModel(StudioModelDocument document)
        {
            if (document && AssetDatabase.Contains(document)) document = document.SaveAs(StudioModelDocument.NewWorkspacePath(document.name));
            modelDocument = document; EnsureModelEditor(); modelEditor.Load(document);
            EditorPrefs.SetString("MashBox.LastModel." + Application.dataPath, document ? document.WorkspacePath : "");
            viewport.Bind(modelEditor.Scene); modelEditor.Frame(viewport);
        }
        private void BindStudioMode()
        {
            if (!viewport) return;
            if (!modelingMode) modelEditor?.SuspendLighting();
            if (modelingMode)
            {
                playing = false; EnsureModelEditor();
                viewport.Bind(modelEditor.Scene);
                viewport.BindRigControls(() => modelEditor?.DrawHierarchy(LoadStudioModel));
                viewport.BindInspector(() => modelEditor?.DrawInspector());
                viewport.BindTimeline(null);
            }
            viewport.BindModes(DrawStudioModes, modelingMode);
            viewport.BindViewportControls(
                DrawStudioMenuBar,
                modelingMode ? (System.Action)DrawModelViewportOptions : null, DrawStudioSettings);
            viewport.BindModelTools(modelingMode ? (System.Action)(() => modelEditor?.DrawToolStrip()) : null);
            viewport.BindWorkspaceStatistics();
            viewport.BindModelToolOptions(modelingMode ? (System.Action)(() => modelEditor?.DrawEmbeddedToolOptions()) : null);
            if (modelEditor != null) modelEditor.FocusToolOptions = viewport.FocusModelToolOptions;
            viewport.BindStatusBar(modelingMode ? (System.Func<string>)(() => modelEditor?.StatusText ?? "Ready") : null);
            viewport.BindStatistics(modelingMode ? (System.Action)(() => modelEditor?.DrawStatistics()) : null);
            viewport.BindMarkingMenu(modelingMode ? (System.Action)(() => modelEditor?.DrawMarkingMenu()) : null);
            viewport.BindModelInput(modelingMode ? (System.Func<bool>)(() => modelingMode) : null,
                () => viewport.HandleWindowShortcut() || modelEditor.HandleModelKeyboard(viewport),
                () => modelEditor?.ReleaseShortcutHolds());
            viewport.minSize = new Vector2(1050, 600);
            if (!modelingMode) viewport.SetInspectorExpanded(inspectorExpanded);
        }
        private void DrawStudioMenuBar()
        {
            if (modelingMode) modelEditor?.DrawMenuBar(viewport, LoadStudioModel);
            else { DrawAnimationFileMenu(); DrawAnimationEditMenu(); }
        }
        private void DrawStudioModes()
        {
            var content = modelingMode
                ? new GUIContent(" Modeling", EditorGUIUtility.IconContent("Mesh Icon").image, "Switch studio mode")
                : new GUIContent(" Animation", EditorGUIUtility.IconContent("AnimationClip Icon").image, "Switch studio mode");
            var rect = new Rect(0, 0, MashBoxStudioViewport.ModeSelectorWidth, 21);
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Passive, StudioTheme.Styles.menuDropdown)) return;
            var menu = new StudioMenu();
            menu.AddItem(new GUIContent("Animation"), !modelingMode, () => SwitchStudioMode(false));
            menu.AddItem(new GUIContent("Modeling"), modelingMode, () => SwitchStudioMode(true));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(StudioShortcuts.MenuLabel(viewport.IsStudioMaximized ? "Restore View" : "Maximize View",StudioShortcut.Maximize)), false, viewport.ToggleMaximized);
            menu.DropDown(rect);
        }
        private void DrawModelViewportOptions()
        {
            using (new EditorGUILayout.HorizontalScope()) modelEditor?.DrawViewportOptions(viewport);
        }
        private void DrawStudioSettings()
        {
            var content = new GUIContent(EditorGUIUtility.IconContent("SettingsIcon").image, "MashBox Studio settings — Appearance and preferences");
            if (GUI.Button(new Rect(0, 0, 24, 24), content, StudioTheme.Styles.overlayButton)) StudioSettingsWindow.Open();
        }
        private void SwitchStudioMode(bool modeling)
        {
            if (modelingMode == modeling) return;
            modelEditor?.CancelMarkingMenu();
            modelEditor?.CloseToolOptions();
            GUIUtility.hotControl = 0; GUI.FocusControl(null);
            if (modeling)
            {
                FlushEdits(); playing = false;
                animationCameraPivot = viewport.pivot; animationCameraRotation = viewport.rotation; animationCameraSize = viewport.size; savedAnimationCamera = true;
                modelingMode = true; BindStudioMode(); modelEditor.Frame(viewport);
            }
            else
            {
                modelingMode = false; OpenViewport();
                if (savedAnimationCamera) { viewport.pivot = animationCameraPivot; viewport.rotation = animationCameraRotation; viewport.size = animationCameraSize; }
            }
            viewport.Repaint();
        }
        private void DrawModelScene(SceneView view)
        {
            if(Event.current.type == EventType.Repaint) viewport.RecordWorkspaceFrame(EditorApplication.timeSinceStartup);
            EnsureModelEditor(); if (viewport.modelGridVisible) viewport.DrawGroundGrid(); modelEditor.SceneGUI(view, LoadStudioModel);
        }
    }
}
