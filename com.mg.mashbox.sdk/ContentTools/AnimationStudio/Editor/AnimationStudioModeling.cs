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
            if (!modelDocument) modelDocument = AssetDatabase.LoadAssetAtPath<StudioModelDocument>(EditorPrefs.GetString("MashBox.LastModel." + Application.dataPath, ""));
            modelEditor = new StudioModelEditor(() => { Repaint(); viewport?.Repaint(); });
            modelEditor.WorkspaceSavedAs = LoadStudioModel;
            if (modelDocument) modelEditor.Load(modelDocument);
        }
        private void ReleaseModelEditor() { modelEditor?.Dispose(); modelEditor = null; skinningEditor?.Dispose(); skinningEditor = null; }
        private void LoadStudioModel(StudioModelDocument document)
        {
            modelDocument = document; EnsureModelEditor(); modelEditor.Load(document);
            EditorPrefs.SetString("MashBox.LastModel." + Application.dataPath, document ? AssetDatabase.GetAssetPath(document) : "");
            viewport.Bind(modelEditor.Scene); modelEditor.Frame(viewport);
        }
        private void BindStudioMode()
        {
            if (!viewport) return;
            if (skinningMode) { BindSkinningMode(); return; }
            if (!compactSettingsInitialized) { inspectorExpanded = false; compactSettingsInitialized = true; }
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
            viewport.BindAttachments(modelingMode ? null : (System.Action)DrawAttachmentRows);
            viewport.BindViewportControls(
                DrawStudioMenuBar,
                modelingMode ? (System.Action)DrawModelViewportOptions : null, modelingMode ? (System.Action)DrawStudioSettings : null);
            viewport.BindModelTools(modelingMode ? (System.Action)(() => modelEditor?.DrawToolStrip()) : null);
            viewport.BindStatistics(modelingMode ? (System.Action)(() => modelEditor?.DrawStatistics()) : null);
            viewport.BindMarkingMenu(modelingMode ? (System.Action)(() => modelEditor?.DrawMarkingMenu()) : null);
            viewport.minSize = new Vector2(1050, 600);
            if (!modelingMode) viewport.SetInspectorExpanded(inspectorExpanded);
        }
        private void DrawStudioMenuBar()
        {
            if (skinningMode) { DrawSkinningFileMenu(); DrawSkinningEditMenu(); DrawSkinToolsMenu(); return; }
            if (modelingMode) modelEditor?.DrawMenuBar(viewport, LoadStudioModel);
            else { DrawAnimationFileMenu(); DrawAnimationEditMenu(); }
        }
        private void DrawStudioModes()
        {
            var content = skinningMode ? new GUIContent(" Skinning", EditorGUIUtility.IconContent("SkinnedMeshRenderer Icon").image, "Switch studio mode") : modelingMode
                ? new GUIContent(" Modeling", EditorGUIUtility.IconContent("Mesh Icon").image, "Switch studio mode")
                : new GUIContent(" Animation", EditorGUIUtility.IconContent("AnimationClip Icon").image, "Switch studio mode");
            var rect = new Rect(0, 0, MashBoxStudioViewport.ModeSelectorWidth, 21);
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Passive, StudioTheme.Styles.menuDropdown)) return;
            var menu = new StudioMenu();
            menu.AddItem(new GUIContent("Animation"), !modelingMode && !skinningMode, () => SwitchStudioMode(false));
            menu.AddItem(new GUIContent("Modeling"), modelingMode && !skinningMode, () => SwitchStudioMode(true));
            menu.AddItem(new GUIContent("Skinning"), skinningMode, () => Guard(OpenSkinningMode));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(viewport.IsStudioMaximized ? "Restore View\tF11" : "Maximize View\tF11"), false, viewport.ToggleMaximized);
            menu.DropDown(rect);
        }
        private void DrawModelViewportOptions()
        {
            using (new EditorGUILayout.HorizontalScope()) modelEditor?.DrawViewportOptions(viewport);
        }
        private void DrawStudioSettings()
        {
            var content = new GUIContent(EditorGUIUtility.IconContent("SettingsIcon").image, "MashBox Studio settings — Appearance and preferences");
            if ((modelingMode || skinningMode) && GUI.Button(new Rect(0, 0, 24, 24), content, StudioTheme.Styles.overlayButton)) StudioSettingsWindow.Open();
        }
        private void SwitchStudioMode(bool modeling)
        {
            if (modelingMode == modeling && !skinningMode) return;
            skinningEditor?.EndStroke(); skinningMode = false;
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
            EnsureModelEditor(); if (viewport.modelGridVisible) viewport.DrawGroundGrid(); modelEditor.SceneGUI(view, LoadStudioModel);
        }
    }
}
