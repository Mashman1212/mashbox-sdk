using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        // Serialized references survive script reloads, including an unsaved original workspace.
        [SerializeField] private StudioRetopologyDocument retopologyBeforeTutorial;
        [SerializeField] private bool retopologyTutorialSession;
        [SerializeField] private Vector3 retopologyTutorialReturnPivot;
        [SerializeField] private Quaternion retopologyTutorialReturnRotation;
        [SerializeField] private float retopologyTutorialReturnSize;
        [SerializeField] private bool retopologyTutorialReturnOrtho;

        private void StartRetopologyTutorial(string id)=>BeginRetopologyTutorial(id,false);
        private void RestartRetopologyTutorial(string id)=>BeginRetopologyTutorial(id,true);
        private void BeginRetopologyTutorial(string id,bool resetProgress)
        {
            Guard(()=>
            {
                // Prepare first: a missing sample or invalid lesson must leave the live workspace intact.
                var practice=StudioRetopologyTutorialScenarios.Create(id);
                if(resetProgress)StudioRetopologyLessons.Reset(StudioRetopologyLessons.Find(id));
                else if(retopologyDocument&&retopologyDocument.tutorialId==id)practice.tutorialActions.Add("restart");
                if(!retopologyTutorialSession)
                {
                    retopologyBeforeTutorial=retopologyDocument;
                    retopologyTutorialSession=true;
                    if(viewport)
                    {
                        retopologyTutorialReturnPivot=viewport.pivot;retopologyTutorialReturnRotation=viewport.rotation;
                        retopologyTutorialReturnSize=viewport.size;retopologyTutorialReturnOrtho=viewport.orthographic;
                    }
                }
                else ReleaseRetopologyPractice();
                retopologyEditor?.Dispose();retopologyEditor=null;
                retopologyDocument=practice;
                BindRetopologyMode();viewport.ShowRetopologyTutorialGuide();
            });
        }
        private void ExitRetopologyTutorial()
        {
            if(!retopologyTutorialSession)
            {
                // A saved practice workspace can be opened directly, without a parked workspace.
                if(retopologyDocument){retopologyDocument.tutorialId="";retopologyDocument.tutorialActions.Clear();}
                viewport?.SetRetopologyTutorialGuide(false);
                viewport?.Repaint();return;
            }
            ReleaseRetopologyPractice();
            retopologyDocument=retopologyBeforeTutorial;retopologyBeforeTutorial=null;
            retopologyTutorialSession=false;
            BindRetopologyMode();
            if(viewport&&retopologyTutorialReturnSize>0)
                viewport.LookAt(retopologyTutorialReturnPivot,retopologyTutorialReturnRotation,retopologyTutorialReturnSize,retopologyTutorialReturnOrtho,true);
            UpdateClipSaveState();
        }
        private void ReleaseRetopologyPractice()
        {
            retopologyEditor?.Dispose();retopologyEditor=null;
            if(!retopologyDocument||EditorUtility.IsPersistent(retopologyDocument))return;
            // Only private lesson copies are released. Saved workspaces and source assets remain intact.
            Undo.ClearUndo(retopologyDocument);
            foreach(var part in retopologyDocument.parts)
                if(part.reference&&!EditorUtility.IsPersistent(part.reference))DestroyImmediate(part.reference);
            DestroyImmediate(retopologyDocument);retopologyDocument=null;
        }
    }
    public sealed partial class MashBoxStudioViewport
    {
        private bool retopologyTutorialGuide;
        internal void SetRetopologyTutorialGuide(bool active)
        {retopologyTutorialGuide=active;Repaint();}
        internal void ShowRetopologyTutorialGuide()
        {skinRightTucked=false;UpdatePanelLayout();Repaint();}
    }
}
