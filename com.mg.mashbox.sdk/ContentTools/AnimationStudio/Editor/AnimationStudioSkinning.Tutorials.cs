using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private StudioSkinningDocument skinningBeforeTutorial;
        [SerializeField] private bool skinningTutorialSession;
        [SerializeField] private Vector3 skinningTutorialPivot;
        [SerializeField] private Quaternion skinningTutorialRotation;
        [SerializeField] private float skinningTutorialSize;
        [SerializeField] private bool skinningTutorialOrtho;
        [SerializeField] private StudioSkinTutorialPose skinningTutorialPose;

        private void ConfigureSkinTutorials()
        {
            skinningEditor.StartTutorial=id=>BeginSkinningTutorial(id,false);
            skinningEditor.RestartTutorial=id=>BeginSkinningTutorial(id,true);
            skinningEditor.ExitTutorial=ExitSkinningTutorial;
            skinningEditor.ImportPractice=()=>Guard(()=>
            {
                var model=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Practice Hoodie.FBX");
                if(!model)throw new InvalidOperationException("The SDK Practice Hoodie is missing. Reinstall the SDK samples.");
                skinningEditor.Import(model);skinningEditor.Frame(viewport);UpdateClipSaveState();
            });
        }
        private void BeginSkinningTutorial(string id,bool restart)
        {
            Guard(()=>
            {
                // Build a complete private scenario before touching the open workspace.
                var practice=StudioSkinningEditor.CreateTutorial(id,ResolveSdkSkeleton());
                skinningEditor?.EndStroke();
                if(restart)StudioSkinningLessons.Reset(StudioSkinningLessons.Find(id));
                if(!skinningTutorialSession)
                {
                    skinningBeforeTutorial=skinningDocument;skinningTutorialSession=true;
                    skinningTutorialPose=skinningEditor?.CaptureTutorialPose();
                    if(viewport)
                    {
                        skinningTutorialPivot=viewport.pivot;skinningTutorialRotation=viewport.rotation;
                        skinningTutorialSize=viewport.size;skinningTutorialOrtho=viewport.orthographic;
                    }
                    skinningEditor?.Dispose();skinningEditor=null;
                }
                else ReleaseSkinningPractice();
                skinningDocument=practice;
                BindSkinningMode();skinningEditor.RestoreTutorialStartPose();skinningEditor.Frame(viewport);
                viewport.ShowSkinTutorialGuide();UpdateClipSaveState();
            });
        }
        private void ExitSkinningTutorial()
        {
            if(!skinningTutorialSession)
            {
                if(skinningDocument){skinningDocument.tutorialId="";skinningDocument.tutorialActions.Clear();}
                viewport?.SetSkinTutorialGuide(false);return;
            }
            ReleaseSkinningPractice();skinningDocument=skinningBeforeTutorial;skinningBeforeTutorial=null;
            skinningTutorialSession=false;
            BindSkinningMode();skinningEditor.RestoreTutorialPose(skinningTutorialPose);skinningTutorialPose=null;
            if(viewport&&skinningTutorialSize>0)
                viewport.LookAt(skinningTutorialPivot,skinningTutorialRotation,skinningTutorialSize,skinningTutorialOrtho,true);
            UpdateClipSaveState();
        }
        private void ReleaseSkinningPractice()
        {
            skinningEditor?.Dispose();skinningEditor=null;
            if(!skinningDocument||EditorUtility.IsPersistent(skinningDocument))return;
            Undo.ClearUndo(skinningDocument);
            foreach(var p in skinningDocument.parts)
                if(p.mesh&&!EditorUtility.IsPersistent(p.mesh)){Undo.ClearUndo(p.mesh);DestroyImmediate(p.mesh);}
            DestroyImmediate(skinningDocument);skinningDocument=null;
        }
    }
    [Serializable] internal sealed class StudioSkinTutorialPose
    {
        public Vector3[] positions,scales;
        public Quaternion[] rotations;
        public float time,radius,strength;
        public int tool;
        public bool sculpt;
    }
    internal sealed partial class StudioSkinningEditor
    {
        internal StudioSkinTutorialPose CaptureTutorialPose()=>new StudioSkinTutorialPose {
            positions=bones.Select(b=>b.localPosition).ToArray(),scales=bones.Select(b=>b.localScale).ToArray(),
            rotations=bones.Select(b=>b.localRotation).ToArray(),time=previewTime,tool=(int)Tool,sculpt=sculptMode,radius=radius,strength=strength};
        internal void RestoreTutorialPose(StudioSkinTutorialPose pose)
        {
            if(pose==null||pose.positions==null||pose.positions.Length!=bones.Length)return;
            PausePreviewAnimation();if(previewGraph.IsValid())previewGraph.Destroy();
            for(int i=0;i<bones.Length;i++)
            {bones[i].localPosition=pose.positions[i];bones[i].localRotation=pose.rotations[i];bones[i].localScale=pose.scales[i];}
            previewTime=pose.time;Tool=(Brush)pose.tool;sculptMode=pose.sculpt;radius=pose.radius;strength=pose.strength;
            RefreshSurface();
        }
        internal void RestoreTutorialStartPose()
        {if(HasTutorial){JumpToPreviewFrame(2);Tool=SkinLesson.unbound?Brush.Object:Brush.Paint;heatmap=!SkinLesson.unbound;UpdateDisplay();}}

        internal static StudioSkinningDocument CreateTutorial(string id,GameObject rig)
        {
            var lesson=StudioSkinningLessons.Find(id);
            if(lesson==null||!rig)throw new InvalidOperationException("This lesson requires the SDK Skeleton.");
            var defaults=StudioDefaultReferences.LoadSkinning("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if(!defaults||!defaults.body)throw new InvalidOperationException("Assign the default skinning body before starting a lesson.");
            const string human="Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/";
            var garment=AssetDatabase.LoadAssetAtPath<GameObject>(human+(lesson.pants?"Human_Pants_Punch_WC.fbx":"Human_Shirt_Default_Hoodie.fbx"));
            if(!garment)throw new InvalidOperationException("The SDK practice clothing is missing. Reinstall the SDK human samples.");
            var doc=ScriptableObject.CreateInstance<StudioSkinningDocument>();
            doc.name="Practice - "+lesson.title;doc.hideFlags=HideFlags.HideAndDontSave;doc.skeleton=rig;
            StudioSkinningEditor editor=null;
            try
            {
                editor=new StudioSkinningEditor(doc,()=>{});
                if(!editor.previewClip)throw new InvalidOperationException("The SDK Skeleton is missing its Skinning Test clip. Reimport Skeleton before starting a lesson.");
                editor.Import(defaults.body,true);
                if(defaults.bust)editor.Import(defaults.bust,true);
                editor.Import(garment);
                if(!editor.HasEditableMesh)throw new InvalidOperationException("The practice clothing has no editable mesh.");
                editor.Part.name=lesson.pants?"Practice Pants":"Practice Hoodie";
                editor.JumpToPreviewFrame(2);
                if(lesson.unbound)
                {
                    // Bake the visible A-pose into our private mesh before removing weights.
                    var posed=(Vector3[])editor.deformed.Clone();
                    editor.UnskinSelected();editor.Part.mesh.vertices=posed;
                    editor.Part.mesh.RecalculateNormals();editor.Part.mesh.RecalculateBounds();editor.Rebuild();
                }
                else if(!editor.Part.bound)editor.TransferBodySkin();
                editor.Dispose();editor=null;
                doc.tutorialId=id;doc.tutorialActions.Clear();doc.dirty=false;
                Undo.ClearUndo(doc);foreach(var part in doc.parts)Undo.ClearUndo(part.mesh);
                return doc;
            }
            catch
            {
                editor?.Dispose();foreach(var part in doc.parts)if(part.mesh&&!EditorUtility.IsPersistent(part.mesh))UnityEngine.Object.DestroyImmediate(part.mesh);
                UnityEngine.Object.DestroyImmediate(doc);throw;
            }
        }
    }
    public sealed partial class MashBoxStudioViewport
    {
        private bool skinTutorialGuide;
        internal void SetSkinTutorialGuide(bool active){skinTutorialGuide=active;Repaint();}
        internal void ShowSkinTutorialGuide(){skinTutorialGuide=true;skinRightTucked=false;UpdatePanelLayout();Repaint();}
    }
}
