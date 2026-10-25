using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private bool skinningMode;
        [SerializeField] private StudioSkinningDocument skinningDocument;
        private StudioSkinningEditor skinningEditor;
        private const string SkinningDefaultsPath = "Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset";
        private void EnsureSkinningEditor()
        {
            if (skinningEditor != null) return;
            bool newWorkspace = !skinningDocument;
            if (!skinningDocument)
            {
                skinningDocument = CreateInstance<StudioSkinningDocument>();
                skinningDocument.name = "Untitled Skinning"; skinningDocument.hideFlags = HideFlags.HideAndDontSave;
                skinningDocument.skeleton = ResolveSdkSkeleton();
            }
            if (!skinningDocument.skeleton) throw new System.InvalidOperationException("The SDK Skeleton asset is missing.");
            skinningEditor = new StudioSkinningEditor(skinningDocument, () => { UpdateClipSaveState(); viewport?.Repaint(); });
            if (newWorkspace)
            {
                try
                {
                    var defaults = StudioDefaultReferences.LoadSkinning(SkinningDefaultsPath);
                    if (defaults)
                    {
                        if (defaults.body) skinningEditor.Import(defaults.body, true);
                        if (defaults.bust && defaults.bust != defaults.body) skinningEditor.Import(defaults.bust, true);
                        if (defaults.pants && defaults.pants != defaults.body && defaults.pants != defaults.bust)
                            skinningEditor.Import(defaults.pants, true, false);
                        if (defaults.shirt && defaults.shirt != defaults.body && defaults.shirt != defaults.bust && defaults.shirt != defaults.pants)
                            skinningEditor.Import(defaults.shirt, true, false);
                    }
                    // Default setup is the starting document, not a user edit.
                    skinningDocument.dirty = false; Undo.ClearUndo(skinningDocument);
                    UpdateClipSaveState();
                }
                catch
                {
                    skinningEditor.Dispose(); skinningEditor = null;
                    foreach (var part in skinningDocument.parts) if (part.mesh) DestroyImmediate(part.mesh);
                    DestroyImmediate(skinningDocument); skinningDocument = null;
                    throw;
                }
            }
        }
        private void OpenSkinningMode()
        {
            retopologyMode=false; retopologyEditor?.SuspendLighting(); viewport?.SetRetopologyPanels(false);
            FlushEdits(); playing = false;
            if (!modelingMode && !skinningMode && viewport)
            { animationCameraPivot = viewport.pivot; animationCameraRotation = viewport.rotation; animationCameraSize = viewport.size; savedAnimationCamera = true; }
            EnsureSkinningEditor(); modelEditor?.SuspendLighting();
            skinningMode = true; modelingMode = false;
            BindSkinningMode(); skinningEditor.Frame(viewport);
        }
        private void BindSkinningMode()
        {
            EnsureSkinningEditor(); modelEditor?.SuspendLighting();
            viewport.Bind(skinningEditor.Scene);
            viewport.BindRigControls(() => skinningEditor.DrawHierarchy());
            viewport.BindSkinInfluences(() => skinningEditor.DrawInfluences());
            viewport.BindInspector(() => skinningEditor.DrawInspector());
            viewport.BindTimeline(null); viewport.BindLimbBlend(null);
            // Modeling's resizable panels also suit influences and brush settings.
            viewport.BindModes(DrawStudioModes,true);
            viewport.BindAttachments(null);
            viewport.BindViewportControls(DrawStudioMenuBar,null,DrawStudioSettings);
            viewport.BindModelTools(null); viewport.BindStatistics(null); viewport.BindMarkingMenu(null);
            viewport.BindSkinPreviewTimeline(skinningEditor.DrawPreviewTimeline);
            viewport.BindSkinPractice(DrawSkinningPracticeMenu);
        }
        private void DrawSkinningPracticeMenu()
        {
            var rect=new Rect(0,0,104,24);
            EditorGUI.DrawRect(rect,new Color(.035f,.04f,.04f));
            if(!EditorGUI.DropdownButton(rect,new GUIContent("Practice","Import sample clothing to practise skinning and sculpting"),FocusType.Passive,StudioTheme.Styles.menuDropdown))return;
            var menu=new StudioMenu();
            const string path="Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Practice Hoodie.FBX";
            var hoodie=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("1abaa86ba35c448db6091f08850b9fee"));
            if(!hoodie)hoodie=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(hoodie)
                menu.AddItem(new GUIContent("Import Practice Hoodie"),false,()=>Guard(()=>
                {
                    EnsureSkinningEditor();
                    skinningEditor.Run(()=>{skinningEditor.Import(hoodie);skinningEditor.Frame(viewport);});
                    UpdateClipSaveState();viewport?.Repaint();
                }));
            else menu.AddDisabledItem(new GUIContent("Practice Hoodie unavailable"));
            menu.DropDown(rect);
        }
        private bool ConfirmSkinningReplacement()
        {
            skinningEditor?.EndStroke();
            if (!skinningDocument || !skinningDocument.dirty) return true;
            int answer = EditorUtility.DisplayDialogComplex("Unsaved skinning workspace", "Save the skinning and sculpt changes before starting another workspace?", "Save", "Cancel", "Discard");
            return answer == 2 || (answer == 0 && skinningDocument.SaveDocument());
        }
        private void DrawSkinningFileMenu()
        {
            var rect = new Rect(0,0,48,21);
            if (!EditorGUI.DropdownButton(rect,new GUIContent("File"),FocusType.Passive,StudioTheme.Styles.menuDropdown)) return;
            var menu = new StudioMenu();
#if MashBoxDev
            menu.AddItem(new GUIContent("Save Default Bust FBX"), false, () => Guard(() => skinningEditor.Run(skinningEditor.SaveDefaultBustFbx)));
            menu.AddItem(new GUIContent("Edit default skinning body…"), false, () =>
            {
                var defaults = StudioDefaultReferences.LoadSkinning(SkinningDefaultsPath);
                Selection.activeObject = defaults; EditorGUIUtility.PingObject(defaults);
            });
#endif
            menu.AddItem(new GUIContent("Save FBX"), false, () => Guard(() => skinningEditor.SaveSelectedFbx()));
            menu.AddItem(new GUIContent("Save FBX As…"), false, () => Guard(() => skinningEditor.SaveSelectedFbxAs()));
            menu.AddItem(new GUIContent("New skinning workspace"),false,()=>Guard(()=>
            {
                if (!ConfirmSkinningReplacement()) return;
                skinningEditor?.Dispose(); skinningEditor = null; skinningDocument = null; BindSkinningMode();
            }));
            menu.AddItem(new GUIContent("Open workspace…"),false,()=>Guard(()=>
            {
                string path = EditorUtility.OpenFilePanel("Open skinning workspace",Application.dataPath,"asset");
                if (string.IsNullOrEmpty(path)) return;
                var document = AssetDatabase.LoadAssetAtPath<StudioSkinningDocument>(FileUtil.GetProjectRelativePath(path));
                if (!document) throw new System.InvalidOperationException("Choose a saved Skinning workspace asset.");
                if (!ConfirmSkinningReplacement()) return;
                skinningEditor?.Dispose(); skinningEditor = null; skinningDocument = document; BindSkinningMode(); skinningEditor.Frame(viewport);
            }));
            menu.AddItem(new GUIContent("Save workspace…\tCtrl+S"),false,()=>Guard(()=> { skinningEditor.EndStroke(); skinningDocument.SaveDocument(); UpdateClipSaveState(); }));
            menu.DropDown(rect);
        }
        private void DrawSkinToolsMenu()
        {
            var rect = new Rect(100,0,90,21);
            if (!EditorGUI.DropdownButton(rect,new GUIContent("Edit Skins"),FocusType.Passive,StudioTheme.Styles.menuDropdown)) return;
            var menu = new StudioMenu();
            if (skinningEditor.CanMirrorSkin)
            {
                menu.AddItem(new GUIContent("Mirror Weights Left to Right"),false,()=>skinningEditor.Run(()=>skinningEditor.MirrorSkinWeights(true)));
                menu.AddItem(new GUIContent("Mirror Weights Right to Left"),false,()=>skinningEditor.Run(()=>skinningEditor.MirrorSkinWeights(false)));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Mirror Weights Left to Right"));
                menu.AddDisabledItem(new GUIContent("Mirror Weights Right to Left"));
            }
            menu.AddSeparator("");
            if (skinningEditor.CanRepairArmpits)
                menu.AddItem(new GUIContent("Reduce Armpit Tenting", "Reduce arm influence on torso-side clothing vertices; supports Undo."),false,()=>skinningEditor.Run(skinningEditor.ReduceArmpitTenting));
            else menu.AddDisabledItem(new GUIContent("Reduce Armpit Tenting"));
            if (skinningEditor.CanRepairArmpits)
                menu.AddItem(new GUIContent("Reduce Armpit Tenting — Strong", "Stronger chest retention with a wider underarm transition. Supports Undo."),false,()=>skinningEditor.Run(skinningEditor.ReduceArmpitTentingStrong));
            else menu.AddDisabledItem(new GUIContent("Reduce Armpit Tenting — Strong"));
            if (skinningEditor.CanRepairArmpits)
                menu.AddItem(new GUIContent("Smooth Armpit Weights", "Gently smooth the corrected transition, with bounded arm/chest weight changes. Supports Undo."),false,()=>skinningEditor.Run(skinningEditor.SmoothArmpitWeights));
            else menu.AddDisabledItem(new GUIContent("Smooth Armpit Weights"));
            menu.DropDown(rect);
        }
        private void DrawSkinningEditMenu()
        {
            var rect = new Rect(48,0,48,21);
            if (!EditorGUI.DropdownButton(rect,new GUIContent("Edit"),FocusType.Passive,StudioTheme.Styles.menuDropdown)) return;
            var menu = new StudioMenu();
            if (skinningEditor.CanSkinSelected) menu.AddItem(new GUIContent("Skin"),false,()=>skinningEditor.Run(skinningEditor.SkinSelected));
            else menu.AddDisabledItem(new GUIContent("Skin"));
            if (skinningEditor.CanUnskinSelected) menu.AddItem(new GUIContent("Unskin"),false,()=>skinningEditor.Run(skinningEditor.UnskinSelected));
            else menu.AddDisabledItem(new GUIContent("Unskin"));
            if (skinningEditor.HasEditableMesh)
            {
                menu.AddItem(new GUIContent("Mark as Bust"),skinningEditor.SelectedIsBust,()=>skinningEditor.Run(()=>skinningEditor.SetSelectedMeshRole(true)));
                menu.AddItem(new GUIContent("Mark as Clothing"),!skinningEditor.SelectedIsBust && !skinningEditor.SelectedIsBody,()=>skinningEditor.Run(()=>skinningEditor.SetSelectedMeshRole(false)));
                menu.AddItem(new GUIContent("Mark as Body"),skinningEditor.SelectedIsBody,()=>skinningEditor.Run(skinningEditor.MarkSelectedBody));
                menu.AddItem(new GUIContent("Save FBX"),false,()=>skinningEditor.Run(skinningEditor.SaveSelectedFbx));
                menu.AddItem(new GUIContent("Save FBX As…"),false,()=>skinningEditor.Run(skinningEditor.SaveSelectedFbxAs));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Mark as Bust"));
                menu.AddDisabledItem(new GUIContent("Mark as Clothing"));
                menu.AddDisabledItem(new GUIContent("Mark as Body"));
                menu.AddDisabledItem(new GUIContent("Save FBX"));
                menu.AddDisabledItem(new GUIContent("Save FBX As…"));
            }
            if (skinningEditor.CanStitchSelectedBust)
                menu.AddItem(new GUIContent("Stitch Bust"), false, () => Guard(() => skinningEditor.Run(skinningEditor.StitchBust)));
            else menu.AddDisabledItem(new GUIContent("Stitch Bust"));
#if MashBoxDev
            menu.AddItem(new GUIContent("Stitch Default Bust"), false, () => Guard(() => skinningEditor.Run(skinningEditor.StitchDefaultBust)));
#endif
            menu.DropDown(rect);
        }
        internal bool SaveAllStudioDocuments()
        {
            if(retopologyDocument&&retopologyDocument.dirty)
            {retopologyDocument.SaveWorkspace();if(retopologyDocument.dirty)return false;}
            skinningEditor?.EndStroke();
            if (skinningDocument && skinningDocument.dirty && !skinningDocument.SaveDocument()) return false;
            if (clipSession && clipSessionDirty && !SaveClipSession()) return false;
            UpdateClipSaveState(); return true;
        }
        internal void DiscardSkinningChanges()
        {
            skinningEditor?.Dispose(); skinningEditor = null;
            if (skinningDocument && EditorUtility.IsPersistent(skinningDocument))
            {
                string path = AssetDatabase.GetAssetPath(skinningDocument);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            skinningDocument = null;
        }
    }
    public sealed partial class MashBoxStudioViewport
    {
        internal bool IsOverSkinningPanels(Vector2 point) => IsOverViewportControls(point) || IsOverControl(rigControls,point) || IsOverControl(skinInfluencesPanel,point) || IsOverControl(skinInfluencesTab,point) || IsOverControl(inspector,point) || IsOverControl(timeline,point) || IsOverControl(skinLeftTab,point) || IsOverControl(skinRightTab,point) || IsOverControl(skinPracticeTab,point);
    }
}
