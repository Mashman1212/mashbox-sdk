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
            if (modelDocument && AssetDatabase.Contains(modelDocument)) modelDocument = modelDocument.SaveAs(StudioModelDocument.NewWorkspacePath(modelDocument.name));
            modelEditor = new StudioModelEditor(() => { Repaint(); viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene); });
            modelEditor.StatusMessage = message => viewport?.ShowStatusMessage(message);
            modelEditor.WorkspaceSavedAs = AdoptSavedStudioModel;
            modelEditor.WorkspaceSaveStateChanged = UpdateWorkspaceSaveState;
            if (modelDocument) modelEditor.Load(modelDocument);
            // Readiness can change while overlays are being restored. Refresh their
            // binding even when the new editor has the same tool/display values.
            viewport?.SyncStudioToolbarState(true);
        }
        private void ReleaseModelEditor()
        {
            modelEditor?.Dispose(); modelEditor = null;
            viewport?.SyncStudioToolbarState(true);
        }
        private void LoadStudioModel(StudioModelDocument document)
        {
            if (document && AssetDatabase.Contains(document)) document = document.SaveAs(StudioModelDocument.NewWorkspacePath(document.name));
            modelDocument = document; EnsureModelEditor(); modelEditor.Load(document);
            viewport.Bind(modelEditor.Scene); modelEditor.Frame(viewport);
        }
        private void AdoptSavedStudioModel(StudioModelDocument document)
        {
            // Save As replaces the document, not the workspace view. The same
            // modeling editor/preview scene stays bound; Bind/Frame would reset
            // the camera's orbit, position and zoom during a file operation.
            modelDocument = document;
            EnsureModelEditor(); modelEditor.Load(document);
            UpdateWorkspaceSaveState();
        }
        private void UpdateWorkspaceSaveState()
        {
            if (viewport) viewport.SetWorkspaceUnsavedChanges(modelEditor != null && modelEditor.HasUnsavedWorkspaceChanges,
                modelDocument ? modelDocument.name : "workspace", modelEditor?.WorkspaceSaveDestination);
        }
        internal bool SaveWorkspaceForClose()
        {
            EnsureModelEditor();
            modelEditor.Save();
            UpdateWorkspaceSaveState();
            return !modelEditor.HasUnsavedWorkspaceChanges;
        }
        internal void DiscardWorkspaceForClose()
        {
            modelEditor?.DiscardWorkspaceForClose();
            modelDocument = null;
            UpdateWorkspaceSaveState();
        }
        internal void StudioViewportClosed(MashBoxStudioViewport closed)
        {
            // A host-mode change replaces the viewport before closing the old one.
            // Only the actual active window ending clears the workspace session.
            if (!ReferenceEquals(viewport, closed)) return;
            viewport = null;
            modelEditor?.DiscardWorkspaceForClose();
            ReleaseModelEditor();
            modelDocument = null;
            StudioPreferenceStore.DeleteKey("MashBox.LastModel." + Application.dataPath);
            StudioModelEditor.ClearTemporaryWorkspaceFile();
        }
        internal void ReloadToolDefaults() => modelEditor?.ReloadStoredToolDefaults();
        internal MashBoxStudioViewport ActiveViewport => viewport;
        internal void RestoreStoredMode() => SwitchStudioMode(StudioPreferenceStore.GetString("Studio.Mode", "Animation") == "Modeling");
        internal bool IsModelingMode => modelingMode && !skinningMode && !retopologyMode;
        internal StudioModelEditor ToolbarModelEditor => IsModelingMode ? modelEditor : null;
        private void BindStudioMode()
        {
            if (!viewport) return;
            // Keep navigation available even if the selected preview mode cannot initialize.
            viewport.BindModes(DrawStudioModes, modelingMode || skinningMode || retopologyMode);
            if (retopologyMode) { BindRetopologyMode(); return; }
            viewport.SetRetopologyPanels(false);
            retopologyEditor?.SuspendLighting();
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
            StudioPreferenceStore.SetString("Studio.Mode", modelingMode ? "Modeling" : "Animation");
            if (modelingMode) modelEditor.BindModelCommands(viewport, LoadStudioModel);
            viewport.BindModes(DrawStudioModes, modelingMode);
            viewport.BindAttachments(modelingMode ? null : (System.Action)DrawAttachmentRows);
            viewport.BindViewportControls(
                DrawStudioMenuBar, null, null);
            viewport.BindWorkspaceStatistics();
            viewport.BindModelToolOptions(modelingMode ? (System.Action)(() => modelEditor?.DrawEmbeddedToolOptions()) : null);
            if (modelEditor != null) modelEditor.FocusToolOptions = viewport.FocusModelToolOptions;
            viewport.BindStatusBar(modelingMode);
            viewport.BindStatistics(modelingMode ? (System.Action)(() => modelEditor?.DrawStatistics()) : null);
            viewport.BindMarkingMenu(modelingMode ? (System.Action)(() => modelEditor?.DrawMarkingMenu()) : null);
            viewport.BindModelInput(modelingMode ? (System.Func<bool>)(() => modelingMode) : null,
                () => viewport.HandleWindowShortcut() || modelEditor.HandleModelKeyboard(viewport),
                () => modelEditor?.ReleaseShortcutHolds());
            viewport.minSize = new Vector2(1050, 600);
            if (!modelingMode) viewport.SetInspectorExpanded(inspectorExpanded);
            viewport.SyncStudioToolbarState(true);
        }
        private void DrawStudioMenuBar()
        {
            if (retopologyMode) { retopologyEditor?.DrawMenu(); return; }
            if (skinningMode) { DrawSkinningFileMenu(); DrawSkinningEditMenu(); DrawSkinToolsMenu(); return; }
            if (modelingMode) modelEditor?.DrawMenuBar(viewport, LoadStudioModel);
            else { DrawAnimationFileMenu(); DrawAnimationEditMenu(); }
        }
        private void DrawStudioModes()
        {
            var content = retopologyMode ? new GUIContent(" Retopology", EditorGUIUtility.IconContent("Mesh Icon").image, "Switch studio mode") : skinningMode ? new GUIContent(" Skinning", EditorGUIUtility.IconContent("SkinnedMeshRenderer Icon").image, "Switch studio mode") : modelingMode
                ? new GUIContent(" Modeling", EditorGUIUtility.IconContent("Mesh Icon").image, "Switch studio mode")
                : new GUIContent(" Animation", EditorGUIUtility.IconContent("AnimationClip Icon").image, "Switch studio mode");
            var rect = new Rect(0, 0, MashBoxStudioViewport.ModeSelectorWidth, 21);
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Passive, StudioTheme.Styles.menuDropdown)) return;
            var menu = new StudioMenu();
            menu.AddItem(new GUIContent("Animation"), !modelingMode && !skinningMode && !retopologyMode, () => SwitchStudioMode(false));
            menu.AddItem(new GUIContent("Modeling"), modelingMode && !skinningMode && !retopologyMode, () => SwitchStudioMode(true));
            menu.AddItem(new GUIContent("Retopology"), retopologyMode, () => Guard(OpenRetopologyMode));
            menu.AddItem(new GUIContent("Skinning"), skinningMode, () => Guard(OpenSkinningMode));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(StudioShortcuts.MenuLabel(viewport.IsStudioMaximized ? "Restore View" : "Maximize View",StudioShortcut.Maximize)), false, viewport.ToggleMaximized);
            menu.DropDown(rect);
        }
        private void DrawStudioSettings()
        {
            var content = new GUIContent(EditorGUIUtility.IconContent("SettingsIcon").image, "MashBox Studio settings — Appearance and preferences");
            if ((modelingMode || skinningMode || retopologyMode) && GUI.Button(new Rect(0, 0, 24, 24), content, StudioTheme.Styles.overlayButton)) StudioSettingsWindow.Open();
        }
        private void SwitchStudioMode(bool modeling)
        {
            if (modelingMode == modeling && !skinningMode && !retopologyMode) return;
            retopologyMode=false; retopologyEditor?.SuspendLighting(); viewport.SetRetopologyPanels(false);
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
            if(Event.current.type == EventType.Repaint) viewport.RecordWorkspaceFrame(EditorApplication.timeSinceStartup);
            EnsureModelEditor(); if (viewport.modelGridVisible) viewport.DrawGroundGrid(); modelEditor.SceneGUI(view, LoadStudioModel);
        }
    }
}
