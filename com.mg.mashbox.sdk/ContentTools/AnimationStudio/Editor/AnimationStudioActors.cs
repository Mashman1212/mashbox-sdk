using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private const string BikePath = "Assets/MashBox/BMX Physics Development/Bike Skeleton.fbx";
        [SerializeField] private int activeActor = -1;
        [SerializeField] private bool showAllActorControls = true;
        private bool drawingActorControls, actorPickersOnly, skipActorPickers;
        private bool pickerActorSelected = true;
        private void DrawActorControlVisibility()
        {
            EditorGUI.BeginChangeCheck();
            showAllActorControls = EditorGUILayout.Toggle(new GUIContent("All actor controls", "Show and pick controls on every actor directly in the viewport."), showAllActorControls);
            lockActorMeshes=EditorGUILayout.Toggle(new GUIContent("Lock actor meshes", "Drag empty viewport space to marquee-select rig controls. Unlock to allow Unity mesh selection."),lockActorMeshes);
            if(rigSelection.Count>1) EditorGUILayout.LabelField(rigSelection.Count+" controls selected · Shift-click toggles",EditorStyles.miniLabel);
            if(EditorGUI.EndChangeCheck()) viewport?.Repaint();
        }
        private void DrawActorPickers(SceneView view, int actorIndex)
        {
            pickerActorSelected = activeActor == actorIndex;
            var before = Event.current.type;
            Action draw = () => { if(!DrawBikeScene(view)) DrawRigPickers(view); };
            if(actorIndex < 0) draw(); else InActor(actorPreviews[actorIndex],draw);
            if((before == EventType.MouseDown || before == EventType.MouseUp) && Event.current.type == EventType.Used)
            {
                FinishSceneEdit(); FinishActorEdits();
                activeActor = actorIndex;
                Repaint(); viewport?.Repaint();
            }
        }
        [SerializeField] private GameObject actorSource;
        [SerializeField] private AnimationClip actorClip;
        private bool inActorScope;
        private readonly List<ActorPreview> actorPreviews = new List<ActorPreview>();
        private sealed class ActorPreview
        {
            public AnimationTake Take;
            public AuthoringRig Rig;
            public PoseKey Pose;
            public int Control;
            public bool IK, Pole, Draft, Unkeyed;
        }
        private void ReleaseActors()
        {
            constrainedPoses.Clear();
            foreach (var actor in actorPreviews)
            {
                if (actor.Rig.Root) DestroyImmediate(actor.Rig.Root);
                actor.Rig.Dispose();
            }
            actorPreviews.Clear();
        }
        private void EnsureActors()
        {
            if (inActorScope || rig == null || !take) return;
            take.actors = take.actors ?? new List<AnimationTake>();
            if (actorPreviews.Count == take.actors.Count && actorPreviews.Select(a=>a.Take).SequenceEqual(take.actors))
            { activeActor=Mathf.Clamp(activeActor,-1,actorPreviews.Count-1); return; }
            ReleaseActors();
            try
            {
                foreach (var asset in take.actors)
                {
                    if (!asset || !asset.character) throw new InvalidOperationException("An actor is missing its source character.");
                    var preview = new ActorPreview { Take=asset, Rig=new AuthoringRig(asset.character,asset.body,asset.clothing,asset.outfitOptions) };
                    actorPreviews.Add(preview);
                    preview.Rig.BuildVehicleParts(asset.vehicleParts);
                    SceneManager.MoveGameObjectToScene(preview.Rig.Root,rig.Scene);
                    preview.Rig.SetNeutralShading(neutralShading);
                    preview.Pose = asset.Evaluate((float)frame/take.frameRate*asset.frameRate) ?? preview.Rig.Rest.Copy(0);
                    preview.Rig.Apply(preview.Pose);
                }
            }
            catch { ReleaseActors(); throw; }
            activeActor = Mathf.Clamp(activeActor,-1,actorPreviews.Count-1);
        }
        private void SampleActors(float atFrame)
        {
            if (inActorScope) return;
            EnsureActors();
            foreach (var actor in actorPreviews)
            {
                actor.Pose = actor.Take.Evaluate(atFrame/take.frameRate*actor.Take.frameRate) ?? actor.Rig.Rest.Copy(0);
                actor.Rig.Apply(actor.Pose); actor.Draft = actor.Unkeyed = false;
            }
            EvaluateConstraints();
        }
        private void InActor(ActorPreview actor, Action action)
        {
            var oldTake=take; var oldRig=rig; var oldPose=pose; int oldControl=control, oldFrame=frame;
            bool oldIK=ikMode, oldPole=poleMode, oldDraft=sceneDraft, oldUnkeyed=unkeyedPose;
            take=actor.Take; rig=actor.Rig; pose=actor.Pose; control=actor.Control; ikMode=actor.IK; poleMode=actor.Pole;
            frame=Mathf.RoundToInt((float)oldFrame/oldTake.frameRate*take.frameRate); sceneDraft=actor.Draft; unkeyedPose=actor.Unkeyed;
            inActorScope=true;
            cachedBoneSearch=null; boneIndices=null;
            try { action(); }
            finally
            {
                actor.Pose=pose; actor.Control=control; actor.IK=ikMode; actor.Pole=poleMode; actor.Draft=sceneDraft; actor.Unkeyed=unkeyedPose;
                take=oldTake; rig=oldRig; pose=oldPose; control=oldControl; frame=oldFrame; ikMode=oldIK; poleMode=oldPole;
                sceneDraft=oldDraft; unkeyedPose=oldUnkeyed; inActorScope=false;
                cachedBoneSearch=null; boneIndices=null;
                if(constraintsDirty) EvaluateConstraints();
            }
        }
        private void FinishActorEdits()
        {
            if (inActorScope) return;
            foreach (var actor in actorPreviews) if (actor.Draft) InActor(actor,FinishSceneEdit);
        }
        private void KeyCurrentActor()
        {
            if(!inActorScope && activeActor>=0 && activeActor<actorPreviews.Count) InActor(actorPreviews[activeActor],()=>Commit("Key actor pose",true));
            else Commit("Key pose",true);
        }
        private void DrawActors()
        {
            if (!take || rig == null) { EditorGUILayout.HelpBox("Open a primary actor take first.",MessageType.Info); return; }
            EnsureActors();
            DrawActorPresets();
            var names = new[] { "Primary · "+take.name }.Concat(actorPreviews.Select(a=>a.Take.name)).ToArray();
            EditorGUI.BeginChangeCheck();
            int chosen=EditorGUILayout.Popup("Edit actor",activeActor+1,names)-1;
            if(EditorGUI.EndChangeCheck()) { FinishSceneEdit(); FinishActorEdits(); activeActor=chosen; viewport?.Repaint(); }
            actorSource=(GameObject)EditorGUILayout.ObjectField("Actor / vehicle source",actorSource,typeof(GameObject),false);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Add vehicle · BMX")) Guard(()=>AddActor(AssetDatabase.LoadAssetAtPath<GameObject>(BikePath)));
                using(new EditorGUI.DisabledScope(!actorSource)) if(GUILayout.Button("Add actor")) Guard(()=>AddActor(actorSource));
            }
            actorClip=(AnimationClip)EditorGUILayout.ObjectField("Clip for selected actor",actorClip,typeof(AnimationClip),false);
            using(new EditorGUI.DisabledScope(!actorClip)) if(GUILayout.Button("Import clip on shared timeline")) Guard(ImportActorClip);
            if(GUILayout.Button("Export all actors as separate clips…")) Guard(ExportActors);
            if(activeActor>=0 && activeActor<actorPreviews.Count && GUILayout.Button("Remove selected actor"))
            {
                FinishActorEdits(); Undo.RecordObject(take,"Remove Studio actor");
                var removed=take.actors[activeActor]; take.actors.RemoveAt(activeActor); activeActor=-1;
                ReleaseActors(); Undo.DestroyObjectImmediate(removed); Save(); return;
            }
            EditorGUILayout.HelpBox("Actors share time in seconds. Import the Player clip on Primary and the BMX clip on the bike. Each keeps separate pose keys and exports separately. Shorter clips hold their last pose.",MessageType.None);
            DrawConstraints();
        }
        private void AddActor(GameObject source)
        {
            if(!source) throw new InvalidOperationException("Actor source not found.");
            if(!EditorUtility.IsPersistent(take)) throw new InvalidOperationException("Save the primary take before adding actors.");
            FinishActorEdits();
            var child=CreateInstance<AnimationTake>(); child.name=source.name; child.character=source;
            child.frameRate=take.frameRate; child.lastFrame=take.lastFrame; child.loop=take.loop;
            try
            {
                using(var preview=new AuthoringRig(source,null)) { child.bonePaths=preview.Paths; child.keys.Add(preview.Rest.Copy(0)); }
                AssetDatabase.AddObjectToAsset(child,take);
                Undo.RegisterCreatedObjectUndo(child,"Add Studio actor"); Undo.RecordObject(take,"Add Studio actor");
                take.actors.Add(child); EditorUtility.SetDirty(child); Save(); FlushSave();
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(take));
                EnsureActors(); activeActor=actorPreviews.Count-1;
            }
            catch { if(!EditorUtility.IsPersistent(child)) DestroyImmediate(child); throw; }
        }
        private void ImportActorClip()
        {
            EnsureActors(); FinishActorEdits();
            var target=activeActor < 0 ? take : actorPreviews[activeActor].Take;
            var targetRig=activeActor < 0 ? rig : actorPreviews[activeActor].Rig;
            var replacement=CreateInstance<AnimationTake>();
            replacement.bonePaths=targetRig.Paths; replacement.frameRate=take.frameRate;
            replacement.lastFrame=Mathf.Max(1,Mathf.CeilToInt(actorClip.length*take.frameRate));
            replacement.loop=take.loop; replacement.interpolation=PoseInterpolation.Linear;
            if(replacement.lastFrame>18000) { DestroyImmediate(replacement); throw new InvalidOperationException("Clip exceeds 18,000 frames."); }
            var saved=targetRig.Capture();
            var avatar=targetRig.Animator ? targetRig.Animator.avatar : null;
            try
            {
                if(actorClip.isHumanMotion && !targetRig.Animator) throw new InvalidOperationException("A Humanoid clip needs a Humanoid actor.");
                if(!actorClip.isHumanMotion && !AnimationUtility.GetCurveBindings(actorClip).Any(b=>b.type==typeof(Transform)&&targetRig.Paths.Contains(b.path)))
                    throw new InvalidOperationException("Clip paths do not match the selected actor.");
                if(!actorClip.isHumanMotion && targetRig.Animator) { targetRig.Animator.avatar=null; targetRig.Animator.Rebind(); }
                for(int f=0;f<=replacement.lastFrame;f++)
                {
                    if(f%60==0 && EditorUtility.DisplayCancelableProgressBar("Import actor clip",target.name,(float)f/replacement.lastFrame)) throw new OperationCanceledException();
                    targetRig.Apply(targetRig.Rest); actorClip.SampleAnimation(targetRig.Root,Mathf.Min(actorClip.length,(float)f/take.frameRate));
                    var key=targetRig.Capture(); key.frame=f; replacement.keys.Add(key);
                }
                Undo.RegisterCompleteObjectUndo(target,"Import actor clip");
                int sharedEnd=Mathf.Max(take.lastFrame,replacement.lastFrame);
                target.keys=replacement.keys; target.frameRate=replacement.frameRate; target.lastFrame=replacement.lastFrame;
                target.interpolation=PoseInterpolation.Linear; target.endpointLeadFrames=target.endpointTailFrames=0;
                EditorUtility.SetDirty(target); AssetDatabase.SaveAssetIfDirty(target);
                Undo.RecordObject(take,"Extend shared timeline"); take.lastFrame=sharedEnd; Save();
            }
            finally
            {
                if(targetRig.Animator && targetRig.Animator.avatar!=avatar) { targetRig.Animator.avatar=avatar; targetRig.Animator.Rebind(); }
                targetRig.Apply(saved); DestroyImmediate(replacement); EditorUtility.ClearProgressBar();
            }
            Seek(0);
        }
        private void ExportActors()
        {
            string folder=EditorUtility.SaveFolderPanel("Export synchronized actor clips",Application.dataPath,"");
            if(string.IsNullOrEmpty(folder)) return;
            string relative=FileUtil.GetProjectRelativePath(folder);
            if(relative!="Assets" && !relative.StartsWith("Assets/",StringComparison.Ordinal)) throw new InvalidOperationException("Choose a folder inside Assets.");
            FinishSceneEdit(); FinishActorEdits(); EnsureActors();
            var all=new[] { new ActorPreview { Take=take,Rig=rig } }.Concat(actorPreviews);
            foreach(var actor in all)
            {
                var timed=SampleConstrainedTake(actor.Take);
                try
                {
                    var clip=AnimationClipBaker.Bake(timed,actor.Rig,actor.Rig.Animator && humanoidExport);
                    var joints=actor.Rig.Bones.FirstOrDefault(b=>b.name=="Joints");
                    if(joints && actor.Rig.Bones.Any(b=>b.name=="DriveTrain_Joint"))
                    {
                        string jointPath=AnimationUtility.CalculateTransformPath(joints,actor.Rig.Root.transform);
                        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
                            if(binding.path!=jointPath && !binding.path.StartsWith(jointPath+"/",StringComparison.Ordinal)) AnimationUtility.SetEditorCurve(clip,binding,null);
                    }
                    var name=string.Join("_",actor.Take.name.Split(System.IO.Path.GetInvalidFileNameChars()));
                    AssetDatabase.CreateAsset(clip,AssetDatabase.GenerateUniqueAssetPath(relative+"/"+name+".anim"));
                }
                finally { DestroyImmediate(timed); }
            }
            AssetDatabase.SaveAssets();
            EvaluateConstraints();
        }
        private bool DrawActorScene(SceneView view)
        {
            if(drawingActorControls || inActorScope || view!=viewport || rig==null || !take) return false;
            EnsureActors();
            if(showAllActorControls)
            {
                drawingActorControls=true;
                try
                {
                    // Fixed actor order keeps IMGUI control IDs stable across selection changes.
                    actorPickersOnly=true;
                    DrawActorPickers(view,-1);
                    for(int i=0;i<actorPreviews.Count;i++) DrawActorPickers(view,i);
                    actorPickersOnly=false; pickerActorSelected=true; skipActorPickers=true;
                    if(activeActor>=0) InActor(actorPreviews[activeActor],()=>DuringSceneGUI(view));
                    else DuringSceneGUI(view);
                }
                finally { drawingActorControls=false; actorPickersOnly=false; skipActorPickers=false; pickerActorSelected=true; }
                return true;
            }
            var color=Handles.color;
            if(activeActor>=0)
            {
                Handles.color=new Color(.35f,.65f,.8f,.55f);
                foreach(var bone in rig.DisplayBones) if(bone.parent && rig.DisplayBones.Contains(bone.parent)) Handles.DrawLine(bone.parent.position,bone.position);
            }
            foreach(var actor in actorPreviews)
            {
                if(activeActor>=0 && actor==actorPreviews[activeActor]) continue;
                Handles.color=new Color(.8f,.65f,.3f,.7f);
                foreach(var bone in actor.Rig.DisplayBones) if(bone.parent && actor.Rig.DisplayBones.Contains(bone.parent)) Handles.DrawLine(bone.parent.position,bone.position);
            }
            Handles.color=color;
            if(activeActor<0 || activeActor>=actorPreviews.Count) return false;
            InActor(actorPreviews[activeActor],()=>DuringSceneGUI(view)); return true;
        }
    }
}
