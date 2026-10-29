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
            skinningEditor.SendToCloth=SendModelToCloth;
            ConfigureSkinTutorials();
            skinningEditor.RestoreTutorialStartPose();
            skinningEditor.SendToRetopology=(part,names,rest)=>
            {
                EnsureRetopologyEditor();retopologyEditor.ImportSkinning(part,names,rest);OpenRetopologyMode();
            };
            if (newWorkspace)
            {
                try
                {
                    EnsureSkinningDefaultReferences();
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
        private void EnsureSkinningDefaultReferences()
        {
            var defaults = StudioDefaultReferences.LoadSkinning(SkinningDefaultsPath);
            if (!defaults) return;
            var sources = new System.Collections.Generic.HashSet<GameObject>();
            void Add(GameObject source, bool visible)
            {
                if (!source || !sources.Add(source)) return;
                var meshes = new System.Collections.Generic.HashSet<Mesh>();
                foreach (var renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.sharedMesh) meshes.Add(renderer.sharedMesh);
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh) meshes.Add(filter.sharedMesh);
                // Keep existing references, visibility choices and authored meshes intact.
                if (skinningDocument.parts.Exists(part => part.referenceOnly && part.sourceMesh && meshes.Contains(part.sourceMesh))) return;
                skinningEditor.Import(source, true, visible);
            }
            Add(defaults.body, true);
            Add(defaults.bust, true);
            Add(defaults.pants, false);
            Add(defaults.shirt, false);
            Add(defaults.hair, true);
            Add(defaults.hat, true);
        }
        private void OpenSkinningMode()
        {
            LeaveClothMode();
            LeavePaintingMode();
            retopologyMode=false; retopologyEditor?.SuspendLighting(); viewport?.SetRetopologyPanels(false);
            FlushEdits(); playing = false;
            if (!modelingMode && !skinningMode && viewport)
            { animationCameraPivot = viewport.pivot; animationCameraRotation = viewport.rotation; animationCameraSize = viewport.size; savedAnimationCamera = true; }
            EnsureSkinningEditor(); modelEditor?.SuspendLighting();
            skinningMode = true; modelingMode = false;
            EnsureSkinningDefaultReferences();
            BindSkinningMode(); skinningEditor.Frame(viewport);
        }
        private void BindSkinningMode()
        {
            EnsureSkinningEditor(); modelEditor?.SuspendLighting();
            viewport.Bind(skinningEditor.Scene);
            viewport.BindRigControls(() => skinningEditor.DrawHierarchy());
            viewport.BindSkinInfluences(() => skinningEditor.DrawInfluences());
            viewport.BindInspector(() => skinningEditor.DrawSkinInspector());
            viewport.BindTimeline(null); viewport.BindLimbBlend(null);
            // Modeling's resizable panels also suit influences and brush settings.
            viewport.BindModes(DrawStudioModes,true);
            viewport.BindAttachments(null);
            viewport.BindViewportControls(DrawStudioMenuBar,null,DrawStudioSettings);
            viewport.BindModelToolOptions(null); viewport.BindStatistics(null); viewport.BindMarkingMenu(null);
            viewport.BindSkinPreviewTimeline(skinningEditor.DrawPreviewTimeline);
            viewport.BindSkinPractice(skinningEditor.DrawSkinTutorialMenu);
            viewport.SetSkinTutorialGuide(skinningEditor.HasTutorial);
        }
        private void DrawSkinningPracticeMenu()
        {
            var rect=new Rect(0,0,104,21);
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
            if(skinningEditor.HasEditableMesh)menu.AddItem(new GUIContent("Send to Retopology"),false,()=>Guard(skinningEditor.SendSelectedToRetopology));
            else menu.AddDisabledItem(new GUIContent("Send to Retopology"));
            menu.AddItem(new GUIContent("New skinning workspace"),false,()=>Guard(()=>
            {
                if (skinningTutorialSession) ExitSkinningTutorial();
                if (!ConfirmSkinningReplacement()) return;
                skinningEditor?.Dispose(); skinningEditor = null; skinningDocument = null; BindSkinningMode();
            }));
            menu.AddItem(new GUIContent("Open workspace…"),false,()=>Guard(()=>
            {
                string path = StudioMeshWorkspaceStorage.ChooseOpen("Open skinning workspace");
                if (string.IsNullOrEmpty(path)) return;
                var document = StudioSkinningDocument.OpenWorkspaceFile(path);
                if (!document) throw new System.InvalidOperationException("Choose a saved Skinning workspace asset.");
                if (skinningTutorialSession) ExitSkinningTutorial();
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
            if (skinningEditor.CanSkinSelected) menu.AddItem(new GUIContent("Skin"),false,()=>skinningEditor.Run(skinningEditor.SkinSelected));
            else menu.AddDisabledItem(new GUIContent("Skin"));
            if (skinningEditor.CanUnskinSelected) menu.AddItem(new GUIContent("Unskin"),false,()=>skinningEditor.Run(skinningEditor.UnskinSelected));
            else menu.AddDisabledItem(new GUIContent("Unskin"));
            menu.AddSeparator("");
            if (skinningEditor.CanStitchSelectedBust)
                menu.AddItem(new GUIContent("Stitch to Body"), false, () => Guard(() => skinningEditor.Run(skinningEditor.StitchBust)));
            else menu.AddDisabledItem(new GUIContent("Stitch to Body"));
            if (skinningEditor.CanStitchSelectedBody)
                menu.AddItem(new GUIContent("Stitch to Bust"), false, () => Guard(() => skinningEditor.Run(skinningEditor.StitchBody)));
            else menu.AddDisabledItem(new GUIContent("Stitch to Bust"));
            if (skinningEditor.CanMatchDefaultBody)
                menu.AddItem(new GUIContent("Match Default Body"), false, () => Guard(() => skinningEditor.Run(skinningEditor.MatchDefaultBody)));
            else menu.AddDisabledItem(new GUIContent("Match Default Body"));
#if MashBoxDev
            menu.AddItem(new GUIContent("Stitch Default Bust to Body"), false, () => Guard(() => skinningEditor.Run(skinningEditor.StitchDefaultBust)));
#endif
            menu.AddSeparator("");
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
            menu.AddSeparator("");
            if (skinningEditor.CanRepairArmpits)
            {
                menu.AddItem(new GUIContent("Fix Crotch Weights - Small", "Reduce leg pull at the crotch and smooth the transition. Supports Undo."),false,()=>skinningEditor.Run(skinningEditor.FixCrotchWeights));
                menu.AddItem(new GUIContent("Fix Crotch Weights - Strong", "Stronger pelvis retention with a smooth transition into each leg. Supports Undo."),false,()=>skinningEditor.Run(skinningEditor.FixCrotchWeightsStrong));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Fix Crotch Weights - Small"));
                menu.AddDisabledItem(new GUIContent("Fix Crotch Weights - Strong"));
            }
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
                menu.AddItem(new GUIContent("Mark as Clothing"),!skinningEditor.SelectedIsBust && !skinningEditor.SelectedIsBody && !skinningEditor.SelectedIsHeadgear,()=>skinningEditor.Run(()=>skinningEditor.SetSelectedMeshRole(false)));
                menu.AddItem(new GUIContent("Mark as Body"),skinningEditor.SelectedIsBody,()=>skinningEditor.Run(skinningEditor.MarkSelectedBody));
                menu.AddItem(new GUIContent("Mark as Headgear"),skinningEditor.SelectedIsHeadgear,()=>skinningEditor.Run(skinningEditor.MarkSelectedHeadgear));
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
            menu.AddSeparator("");
            if(skinningEditor.HasEditableMesh)menu.AddItem(new GUIContent("Remove Mesh"),false,()=>skinningEditor.Run(skinningEditor.RemoveSelectedMesh));
            else menu.AddDisabledItem(new GUIContent("Remove Mesh"));
            menu.DropDown(rect);
        }
        internal bool SaveAllStudioDocuments()
        {
            if(skinningBeforeTutorial&&skinningBeforeTutorial.dirty&&!skinningBeforeTutorial.SaveDocument())return false;
            if(retopologyBeforeTutorial&&retopologyBeforeTutorial.dirty)
            {retopologyBeforeTutorial.SaveWorkspace();if(retopologyBeforeTutorial.dirty)return false;}
            if(retopologyDocument&&retopologyDocument.dirty)
            {retopologyDocument.SaveWorkspace();if(retopologyDocument.dirty)return false;}
            skinningEditor?.EndStroke();
            if (skinningDocument && skinningDocument.dirty && !skinningDocument.SaveDocument()) return false;
            if (clipSession && clipSessionDirty && !SaveClipSession()) return false;
            UpdateClipSaveState(); return true;
        }
        internal void DiscardSkinningChanges()
        {
            if(skinningTutorialSession)ExitSkinningTutorial();
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
