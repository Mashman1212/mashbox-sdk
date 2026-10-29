using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        // Reuse the authoring data model without creating a take asset on disk.
        [SerializeField] private AnimationTake clipSession;
        [SerializeField] private bool clipSessionDirty;
        [SerializeField] private bool memoryTakeSession;
        private bool EditingClip => clipSession && take == clipSession;

        private static void OpenClipForEditing(AnimationClip clip) => OpenClip(clip, null);

        private void MarkClipChanged()
        {
            clipSessionDirty = true;
            UpdateClipSaveState();
        }

        private void UpdateClipSaveState()
        {
            if (!viewport) return;
            viewport.SetClipUnsavedChanges(clipSession && clipSessionDirty);
            UpdateWorkspaceSaveState();
            viewport.Repaint();
        }

        internal bool SaveClipSession(bool saveAs = false)
        {
            if (!EditingClip) return false;
            if (memoryTakeSession && !saveAs) return SaveMemoryTake();
            FlushEdits();
            if (!saveAs && CanOverwriteClip(take.outputClip)) SaveToReferencedClip();
            else
            {
                string path = StudioFileDialogs.SaveAssetInProject("Save animation", take.name, "anim", "Save the edited animation as a standalone clip.");
                if (string.IsNullOrEmpty(path)) return false;
                // Save As never silently replaces another animation.
                BakeToClip(null, AssetDatabase.GenerateUniqueAssetPath(path));
            }
            clipSessionDirty = false;
            UpdateClipSaveState();
            return true;
        }

        internal void DiscardClipChanges()
        {
            ReleaseRig();
            take = null;
            ReleaseClipSession();
            importClip = null;
        }

        private bool SaveMemoryTake()
        {
            if (!EditingClip) return false;
            FlushEdits();
            string path = StudioFileDialogs.SaveAssetInProject("Save editable take", take.name, "asset", "Save the take, actor tracks, constraints and editable keys.");
            if (string.IsNullOrEmpty(path)) return false;
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            // Persist the same object graph so actor/constraint references and Undo
            // remain intact; do not release or destroy the in-memory session.
            take.hideFlags = HideFlags.None;
            try
            {
                AssetDatabase.CreateAsset(take, path);
                foreach (var actor in take.actors)
                {
                    if (!actor || EditorUtility.IsPersistent(actor)) continue;
                    actor.hideFlags = HideFlags.None;
                    AssetDatabase.AddObjectToAsset(actor, take);
                }
                EditorUtility.SetDirty(take);
                AssetDatabase.SaveAssetIfDirty(take);
            }
            catch
            {
                // Keep the session alive if writing fails so it can be recovered.
                if (!EditorUtility.IsPersistent(take)) take.hideFlags = HideFlags.HideAndDontSave;
                throw;
            }
            clipSession = null; clipSessionDirty = false; memoryTakeSession = false;
            EditorPrefs.SetString("MashBox.LastTake." + Application.dataPath, path);
            UpdateClipSaveState(); EditorGUIUtility.PingObject(take);
            return true;
        }

        private bool ConfirmReplaceClip()
        {
            FlushEdits();
            if (!EditingClip || !clipSessionDirty) return true;
            int choice = EditorUtility.DisplayDialogComplex("Unsaved animation", "Save changes to " + take.name + " before opening another animation?", "Save", "Cancel", "Discard");
            if (choice == 1) return false;
            return choice == 2 || SaveClipSession();
        }

        private void ReleaseClipSession()
        {
            if (clipSession)
            {
                foreach (var actor in clipSession.actors)
                    if (actor && !EditorUtility.IsPersistent(actor)) { Undo.ClearUndo(actor); DestroyImmediate(actor); }
                if (!EditorUtility.IsPersistent(clipSession))
                {
                    Undo.ClearUndo(clipSession);
                    DestroyImmediate(clipSession);
                }
            }
            clipSession = null;
            memoryTakeSession = false;
            clipSessionDirty = false;
            UpdateClipSaveState();
        }

        private void OnDestroy() => ReleaseClipSession();

        private void DrawAnimationFileMenu()
        {
            var content = new GUIContent("File");
            var rect = new Rect(0, 0, StudioTheme.Styles.menuBarButton.CalcSize(content).x+4, 21);
            if (!StudioMenu.MenuBarButton(viewport,rect,content)) return;
            var menu = new StudioMenu();
            menu.AddItem(new GUIContent("New Take"), false, () => Guard(NewTake));
            menu.AddSeparator("");
            if (EditingClip)
            {
                menu.AddItem(new GUIContent(memoryTakeSession ? "Save take…\tCtrl+S" : "Save animation\tCtrl+S"), false, () => Guard(() => SaveClipSession()));
                if (!memoryTakeSession) menu.AddItem(new GUIContent("Save as editable take…"), false, () => Guard(() => SaveMemoryTake()));
                menu.AddItem(new GUIContent("Save as animation clip…"), false, () => Guard(() => SaveClipSession(true)));
            }
            else if (take && rig != null)
            {
                menu.AddItem(new GUIContent("Save take"), false, () => Guard(FlushEdits));
                menu.AddItem(new GUIContent("Save as animation clip…"), false, () => Guard(Bake));
            }
            else menu.AddDisabledItem(new GUIContent("Save animation  Ctrl+S"));
            menu.DropDown(rect,viewport,"File");
        }

        private void DrawClipDocument()
        {
            EditorGUILayout.LabelField(take.name, StudioTheme.Styles.boldLabel);
            if (memoryTakeSession)
            {
                EditorGUILayout.HelpBox("Working in memory. Save an editable take or export an animation clip when ready.", MessageType.Info);
                if (GUILayout.Button("Save take…")) Guard(() => SaveMemoryTake());
                if (GUILayout.Button("Save as animation clip…")) Guard(() => SaveClipSession(true));
                return;
            }
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Animation clip", take.outputClip, typeof(AnimationClip), false);
            EditorGUILayout.HelpBox(clipSessionDirty ? "Unsaved changes in memory. Save animation writes your keyed edits to the clip." : "Editing in memory. Press S to key a pose, then Ctrl+S to save the animation.", MessageType.Info);
            if (GUILayout.Button("Save animation")) Guard(() => SaveClipSession());
            if (GUILayout.Button("Save animation as…")) Guard(() => SaveClipSession(true));
        }

        private static void PreserveClipExtras(AnimationClip source, AnimationClip baked)
        {
            if (!source) return;
            // The pose baker owns skeletal motion; retain channels it does not author.
            var bakedBindings = AnimationUtility.GetCurveBindings(baked);
            var muscles = HumanTrait.MuscleName.Select(AnimationClipBaker.MuscleProperty).ToArray();
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
                if (binding.type != typeof(Transform) && !bakedBindings.Contains(binding) &&
                    !(binding.type == typeof(Animator) && (muscles.Contains(binding.propertyName) || IsHumanoidMotionChannel(binding.propertyName))))
                    AnimationUtility.SetEditorCurve(baked, binding, AnimationUtility.GetEditorCurve(source, binding));
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
                AnimationUtility.SetObjectReferenceCurve(baked, binding, AnimationUtility.GetObjectReferenceCurve(source, binding));
            AnimationUtility.SetAnimationEvents(baked, AnimationUtility.GetAnimationEvents(source));
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            var bakedSettings = AnimationUtility.GetAnimationClipSettings(baked);
            settings.startTime = bakedSettings.startTime; settings.stopTime = bakedSettings.stopTime;
            settings.loopTime = bakedSettings.loopTime;
            AnimationUtility.SetAnimationClipSettings(baked, settings);
            baked.legacy = source.legacy;
            baked.wrapMode = source.wrapMode;
        }

        private static bool IsHumanoidMotionChannel(string property)
        {
            return property.Contains("TDOF.") || new[] { "RootT.", "RootQ.", "MotionT.", "MotionQ.",
                "LeftFootT.", "LeftFootQ.", "RightFootT.", "RightFootQ.",
                "LeftHandT.", "LeftHandQ.", "RightHandT.", "RightHandQ." }
                .Any(prefix => property.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    public sealed partial class MashBoxStudioViewport
    {
        private bool clipUnsavedChanges, workspaceUnsavedChanges;
        private string workspaceSaveName, workspaceSaveDestination;
        internal void SetClipUnsavedChanges(bool dirty)
        {
            clipUnsavedChanges = dirty;
            UpdateStudioUnsavedChanges();
        }
        internal void SetWorkspaceUnsavedChanges(bool dirty, string name, string destination = null)
        {
            workspaceUnsavedChanges = dirty;
            workspaceSaveName = name;
            workspaceSaveDestination = destination;
            UpdateStudioUnsavedChanges();
        }
        private void UpdateStudioUnsavedChanges()
        {
            hasUnsavedChanges = workspaceUnsavedChanges || clipUnsavedChanges;
            saveChangesMessage = workspaceUnsavedChanges
                ? clipUnsavedChanges ? "Save workspace and animation changes before closing MashBox Studio?"
                    : "Save changes to \"" + workspaceSaveName + "\" before closing MashBox Studio?"
                : "Save changes to the animation clip before closing MashBox Studio?";
            if (workspaceUnsavedChanges)
                saveChangesMessage += string.IsNullOrEmpty(workspaceSaveDestination)
                    ? "\n\nChoose a filename when you click Save."
                    : "\n\nFile: " + workspaceSaveDestination;
        }
        public new void OnDestroy()
        {
            try { StudioWindowOwnership.CloseOwnedWindows(this); if (Studio) Studio.StudioViewportClosed(this); }
            finally { base.OnDestroy(); }
        }
        public override void SaveChanges()
        {
            if (!Studio) return;
            // A canceled filename dialog leaves the native dirty flag set, which
            // tells Unity to cancel closing the tab/window.
            if (workspaceUnsavedChanges && !Studio.SaveWorkspaceForClose()) return;
            if (clipUnsavedChanges && !Studio.SaveClipSession()) return;
            base.SaveChanges();
        }
        public override void DiscardChanges()
        {
            if (Studio)
            {
                if (workspaceUnsavedChanges) Studio.DiscardWorkspaceForClose();
                if (clipUnsavedChanges) Studio.DiscardClipChanges();
            }
            base.DiscardChanges();
        }
    }
}
