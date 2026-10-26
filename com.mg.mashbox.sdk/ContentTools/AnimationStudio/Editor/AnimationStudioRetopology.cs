using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private bool retopologyMode;
        [SerializeField] private StudioRetopologyDocument retopologyDocument;
        private StudioRetopologyEditor retopologyEditor;
        private void EnsureRetopologyEditor()
        {
            if(retopologyEditor!=null)return;
            if(!retopologyDocument)
            {retopologyDocument=CreateInstance<StudioRetopologyDocument>();retopologyDocument.name="Untitled Retopology";retopologyDocument.hideFlags=HideFlags.HideAndDontSave;}
            retopologyEditor=new StudioRetopologyEditor(retopologyDocument,()=>{UpdateClipSaveState();viewport?.Repaint();Repaint();});
            retopologyEditor.StartTutorial=StartRetopologyTutorial;
            retopologyEditor.RestartTutorial=RestartRetopologyTutorial;
            retopologyEditor.ExitTutorial=ExitRetopologyTutorial;
            retopologyEditor.NewWorkspace=()=>Guard(()=>ReplaceRetopology(null));
            retopologyEditor.OpenWorkspace=document=>Guard(()=>ReplaceRetopology(document));
            retopologyEditor.SkinOutput=asset=>Guard(()=>
            {
                EnsureSkinningEditor();skinningEditor.Import(asset);OpenSkinningMode();
            });
            retopologyEditor.SendToSkinner=part=>Guard(()=>
            {
                EnsureSkinningEditor();skinningEditor.ImportRetopology(part);OpenSkinningMode();
            });
            retopologyEditor.EditInModeling=(mesh,name)=>
            {
                // A separate in-memory document keeps the reference workspace intact.
                if(modelDocument && EditorUtility.IsDirty(modelDocument))
                {EditorUtility.DisplayDialog("Modeling workspace has edits","Save the Modeling workspace before sending another mesh into it.","OK");return;}
                var document=CreateInstance<StudioModelDocument>();document.name=name;document.hideFlags=HideFlags.HideAndDontSave;
                var copy=Instantiate(mesh);copy.hideFlags=HideFlags.HideAndDontSave;
                document.nodes.Add(new StudioModelDocument.Node { name=name,mesh=copy });
                retopologyMode=false;modelingMode=true;skinningMode=false;EnsureModelEditor();LoadStudioModel(document);OpenViewport();
            };
        }
        private void ReplaceRetopology(StudioRetopologyDocument document)
        {
            if(retopologyTutorialSession)ExitRetopologyTutorial();
            if(retopologyDocument && retopologyDocument.dirty)
            {
                int choice=EditorUtility.DisplayDialogComplex("Unsaved retopology","Save this workspace before replacing it?","Save","Cancel","Discard");
                if(choice==1)return;
                if(choice==0){retopologyDocument.SaveWorkspace();if(retopologyDocument.dirty)return;}
            }
            retopologyEditor?.Dispose();retopologyEditor=null;retopologyDocument=document;BindRetopologyMode();
        }
        private void OpenRetopologyMode()
        {
            FlushEdits();playing=false;skinningEditor?.EndStroke();modelEditor?.CancelMarkingMenu();
            modelEditor?.SuspendLighting();retopologyMode=true;modelingMode=false;skinningMode=false;
            EnsureRetopologyEditor();if(!viewport)OpenViewport();else BindRetopologyMode();
        }
        internal void DiscardRetopologyChanges()
        {
            if(retopologyTutorialSession)
            {
                ReleaseRetopologyPractice();retopologyDocument=retopologyBeforeTutorial;
                retopologyBeforeTutorial=null;retopologyTutorialSession=false;
            }
            retopologyEditor?.Dispose();retopologyEditor=null;
            if(retopologyDocument&&EditorUtility.IsPersistent(retopologyDocument))
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(retopologyDocument),ImportAssetOptions.ForceUpdate);
            retopologyDocument=null;
        }
        private void BindRetopologyMode()
        {
            EnsureRetopologyEditor();
            viewport.Bind(retopologyEditor.Scene);
            viewport.BindSkinPreviewTimeline(null);viewport.BindTimeline(null);viewport.BindLimbBlend(null);
            viewport.BindRigControls(retopologyEditor.DrawMeshes);viewport.BindInspector(retopologyEditor.DrawTools);
            viewport.BindAttachments(null);viewport.BindModelToolOptions(null);viewport.BindMarkingMenu(retopologyEditor.DrawContextWheel);
            viewport.BindModes(DrawStudioModes,true);
            viewport.BindViewportControls(DrawStudioMenuBar,retopologyEditor.DrawToolbar,DrawStudioSettings);
            viewport.BindStatistics(retopologyEditor.DrawStatistics);
            viewport.SetRetopologyPanels(true);
            viewport.BindSkinPractice(retopologyEditor.DrawTutorialMenu);
            viewport.SetRetopologyTutorialGuide(!string.IsNullOrEmpty(retopologyDocument.tutorialId));
        }
    }
    public sealed partial class MashBoxStudioViewport
    {
        private bool retopologyPanels;
        private bool FoldablePanels => skinPreviewTimeline || retopologyPanels;
        internal void SetRetopologyPanels(bool enabled)
        {
            if(enabled&&!retopologyPanels){skinLeftTucked=false;skinRightTucked=true;skinInfluencesTucked=true;}
            retopologyPanels=enabled;UpdatePanelLayout();
            if(meshStatistics!=null)meshStatistics.style.width=enabled?420:256;
            if(viewportOptions!=null){viewportOptions.style.width=enabled?704:202;viewportOptions.style.height=enabled?54:24;}
        }
    }
}
