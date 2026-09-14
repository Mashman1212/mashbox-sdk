using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private bool constraintsExpanded = true;
        [SerializeField] private int constraintDriverActor, constraintDriverControl;
        [SerializeField] private int constraintFollowerActor, constraintFollowerControl;
        [SerializeField] private bool constraintKeepOffset = true;
        private bool constraintsDirty;
        private string constraintError;
        private AnimationTake constraintSession;
        private List<ActorPreview> constraintPreviewActors;
        private readonly Dictionary<AnimationTake, PoseKey> constrainedPoses = new Dictionary<AnimationTake, PoseKey>();

        private List<ActorPreview> ConstraintActors() => new[] { new ActorPreview { Take=take, Rig=rig, Pose=pose } }.Concat(actorPreviews)
            .Select(a=>new ActorPreview { Take=a.Take,Rig=a.Rig,Pose=a.Pose }).ToList();
        private static string[] ConstraintControls(AuthoringRig r) =>
            (r.HasIK ? LimbNames.Select(n=>"IK / "+n) : Enumerable.Empty<string>())
            .Concat(r.Paths.Select(p=>"Bone / "+(p.Length==0 ? "Actor root" : p))).ToArray();
        private static void SetEndpoint(StudioConstraint c, ActorPreview actor, int index, bool driven)
        {
            int count=actor.Rig.HasIK ? 4 : 0;
            int ik=index<count ? index : -1;
            string path=ik<0 ? actor.Rig.Paths[Mathf.Clamp(index-count,0,actor.Rig.Paths.Length-1)] : "";
            if(driven) { c.drivenActor=actor.Take; c.drivenIK=ik; c.drivenBone=path; }
            else { c.targetActor=actor.Take; c.targetIK=ik; c.targetBone=path; }
        }
        private static Transform EndpointBone(AuthoringRig r, string path, int ik)
        {
            if(ik>=0) return ik<4 && r.HasIK ? r.Ends[ik] : null;
            int index=Array.IndexOf(r.Paths,path);
            return index<0 ? null : r.Bones[index];
        }
        private static void Endpoint(ActorPreview a, string path, int ik, out Vector3 p, out Quaternion q)
        {
            var bone=EndpointBone(a.Rig,path,ik);
            if(!bone) throw new InvalidOperationException("A constraint bone or IK control is missing on "+a.Take.name+".");
            if(ik>=0 && a.Pose.effectors[ik].weight>0)
            {
                p=a.Rig.Root.transform.TransformPoint(a.Pose.effectors[ik].position);
                q=a.Rig.Root.transform.rotation*a.Pose.effectors[ik].rotation;
            }
            else { p=bone.position; q=bone.rotation; }
        }
        private static float ConstraintWeight(StudioConstraint c, float f)
        {
            if(!c.enabled || f<c.firstFrame || f>c.lastFrame) return 0;
            float w=Mathf.Clamp01(c.weight);
            if(c.blendFrames>0) w*=Mathf.SmoothStep(0,1,Mathf.Min((f-c.firstFrame)/c.blendFrames,(c.lastFrame-f)/c.blendFrames));
            return w;
        }
        // Build a dependency order before touching a pose. Missing references and cycles never partially solve.
        private static List<StudioConstraint> ConstraintOrder(List<StudioConstraint> constraints, List<ActorPreview> actors)
        {
            var list=constraints.Where(c=>c.enabled).ToList();
            var rigs=actors.ToDictionary(a=>a.Take,a=>a.Rig);
            foreach(var c in list)
            {
                if(!c.drivenActor || !c.targetActor || !rigs.ContainsKey(c.drivenActor) || !rigs.ContainsKey(c.targetActor))
                    throw new InvalidOperationException("A constraint references a removed actor. Disable or remove it.");
                var d=EndpointBone(rigs[c.drivenActor],c.drivenBone,c.drivenIK);
                var t=EndpointBone(rigs[c.targetActor],c.targetBone,c.targetIK);
                if(!d || !t) throw new InvalidOperationException("A constraint references a missing bone or unsupported IK control.");
                if(c.drivenActor==c.targetActor && (t==d || t.IsChildOf(d) ||
                    (c.drivenIK>=0 && t.IsChildOf(rigs[c.drivenActor].Starts[c.drivenIK]))))
                    throw new InvalidOperationException("A control cannot follow itself or a bone that it drives.");
            }
            var ordered=new List<StudioConstraint>(); var visiting=new HashSet<StudioConstraint>();
            Action<StudioConstraint> visit=null;
            visit=c=>
            {
                if(ordered.Contains(c)) return;
                if(!visiting.Add(c)) throw new InvalidOperationException("Circular constraint dependency. Remove the reverse attachment.");
                foreach(var other in list)
                {
                    if(other==c || other.drivenActor!=c.targetActor) continue;
                    // IK can move multiple body bones; conservatively depend on the whole actor.
                    var driven=EndpointBone(rigs[other.drivenActor],other.drivenBone,other.drivenIK);
                    var target=EndpointBone(rigs[c.targetActor],c.targetBone,c.targetIK);
                    if(other.drivenIK>=0 || target==driven || target.IsChildOf(driven)) visit(other);
                }
                visiting.Remove(c); ordered.Add(c);
            };
            foreach(var c in list) visit(c);
            return ordered;
        }
        private static void SolveConstraints(List<ActorPreview> actors, List<StudioConstraint> constraints, float atFrame)
        {
            var order=ConstraintOrder(constraints,actors);
            foreach(var a in actors) { a.Pose=a.Pose.Copy(a.Pose.frame); a.Rig.Apply(a.Pose); }
            foreach(var c in order)
            {
                float w=ConstraintWeight(c,atFrame); if(w<=0) continue;
                var driven=actors.First(a=>a.Take==c.drivenActor); var target=actors.First(a=>a.Take==c.targetActor);
                Endpoint(target,c.targetBone,c.targetIK,out var tp,out var tq);
                Endpoint(driven,c.drivenBone,c.drivenIK,out var dp,out var dq);
                if(c.drivenIK>=0)
                {
                    // Blend from the solved hand/foot, not an inactive or partially weighted target.
                    var end=driven.Rig.Ends[c.drivenIK]; dp=end.position; dq=end.rotation;
                }
                Vector3 p=c.position ? Vector3.Lerp(dp,tp+tq*c.positionOffset,w) : dp;
                Quaternion q=c.rotation ? Quaternion.Slerp(dq,tq*Quaternion.Euler(c.rotationOffset),w) : dq;
                if(c.drivenIK>=0)
                {
                    var e=driven.Pose.effectors[c.drivenIK].weight>0 ? driven.Pose.effectors[c.drivenIK] : driven.Rig.MatchEffector(c.drivenIK,0);
                    e.position=driven.Rig.Root.transform.InverseTransformPoint(p);
                    e.rotation=Quaternion.Inverse(driven.Rig.Root.transform.rotation)*q;
                    e.weight=1; driven.Pose.effectors[c.drivenIK]=e;
                }
                else
                {
                    var bone=EndpointBone(driven.Rig,c.drivenBone,-1);
                    bone.SetPositionAndRotation(p,q);
                    driven.Pose.bones[Array.IndexOf(driven.Rig.Paths,c.drivenBone)]=BonePose.Read(bone);
                    for(int i=0;i<4;i++)
                        if(bone==driven.Rig.Starts[i] || bone==driven.Rig.Middles[i] || bone==driven.Rig.Ends[i]) driven.Pose.effectors[i].weight=0;
                }
                driven.Rig.Apply(driven.Pose);
            }
        }
        private void EvaluateConstraints()
        {
            constraintsDirty=true;
            if(inActorScope || !take || rig==null) return;
            constraintsDirty=false; constrainedPoses.Clear(); constraintError=null;
            constraintSession=take; constraintPreviewActors=null;
            if(take.constraints==null || take.constraints.Count==0) return;
            var actors=ConstraintActors().Select(a=>new ActorPreview { Take=a.Take,Rig=a.Rig,Pose=a.Pose }).ToList();
            try
            {
                SolveConstraints(actors,take.constraints,playing ? playFrame : frame);
                foreach(var a in actors) constrainedPoses[a.Take]=a.Pose;
                constraintPreviewActors=actors;
            }
            catch(Exception e) { constraintError=e.Message; foreach(var a in ConstraintActors()) a.Rig.Apply(a.Pose); }
        }
        private EffectorPose DisplayEffector(int index) => constrainedPoses.TryGetValue(take,out var solved) ? solved.effectors[index] : pose.effectors[index];
        private bool DrawAttachmentHandle()
        {
            if(playing || !constraintSession || constraintPreviewActors==null || (ikMode && poleMode)) return false;
            string path=ikMode ? "" : rig.Paths[Mathf.Clamp(control,0,rig.Paths.Length-1)];
            if(IsBike) path=AnimationUtility.CalculateTransformPath(BikeJoint(Mathf.Clamp(control,0,BikeJointNames.Length-1)),rig.Root.transform);
            float time=constraintSession.frameRate*(float)frame/take.frameRate;
            var c=constraintSession.constraints.LastOrDefault(x=>x.drivenActor==take && ConstraintWeight(x,time)>0 &&
                (ikMode ? x.drivenIK==control : x.drivenIK<0 && x.drivenBone==path));
            if(c==null) return false;
            var target=constraintPreviewActors.First(a=>a.Take==c.targetActor);
            Endpoint(target,c.targetBone,c.targetIK,out var tp,out var tq);
            Vector3 p=tp+tq*c.positionOffset; Quaternion q=tq*Quaternion.Euler(c.rotationOffset);
            Handles.Label(p,"Attached · edit offset");
            EditorGUI.BeginChangeCheck();
            if(rotate) q=Handles.RotationHandle(q,p); else p=Handles.PositionHandle(p,tq);
            if(EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(constraintSession,"Edit attachment offset");
                if(rotate) c.rotationOffset=(Quaternion.Inverse(tq)*q).eulerAngles;
                else c.positionOffset=Quaternion.Inverse(tq)*(p-tp);
                if(pendingSave && pendingSave!=constraintSession) FlushSave();
                EditorUtility.SetDirty(constraintSession); pendingSave=constraintSession; saveAfter=EditorApplication.timeSinceStartup+1;
                EvaluateConstraints(); viewport?.Repaint(); Repaint();
            }
            return true;
        }
        private void AddAttachment(StudioConstraint c, bool keepOffset)
        {
            EnsureActors();
            var actors=ConstraintActors();
            var proposed=new List<StudioConstraint>(take.constraints ?? new List<StudioConstraint>()) { c };
            ConstraintOrder(proposed,actors);
            if(keepOffset)
            {
                // Capture from the displayed pose, including existing attachments.
                foreach(var a in actors) if(constrainedPoses.TryGetValue(a.Take,out var solved)) a.Pose=solved;
                Endpoint(actors.First(a=>a.Take==c.drivenActor),c.drivenBone,c.drivenIK,out var p,out var q);
                Endpoint(actors.First(a=>a.Take==c.targetActor),c.targetBone,c.targetIK,out var tp,out var tq);
                c.positionOffset=Quaternion.Inverse(tq)*(p-tp); c.rotationOffset=(Quaternion.Inverse(tq)*q).eulerAngles;
            }
            Undo.RecordObject(take,"Add attachment constraint"); take.constraints=proposed; Save(); EvaluateConstraints(); viewport?.Repaint();
        }
        private void DrawConstraints()
        {
            constraintsExpanded=EditorGUILayout.Foldout(constraintsExpanded,"ATTACHMENT CONSTRAINTS",true);
            if(!constraintsExpanded) return;
            var actors=ConstraintActors(); var names=actors.Select(a=>a.Take.name).ToArray();
            int oldFollower=constraintFollowerActor,oldDriver=constraintDriverActor;
            constraintFollowerActor=EditorGUILayout.Popup("Constrain actor",Mathf.Clamp(constraintFollowerActor,0,actors.Count-1),names);
            constraintDriverActor=EditorGUILayout.Popup("Follow actor",Mathf.Clamp(constraintDriverActor,0,actors.Count-1),names);
            if(oldFollower!=constraintFollowerActor) constraintFollowerControl=0;
            if(oldDriver!=constraintDriverActor) constraintDriverControl=0;
            var follow=actors[constraintFollowerActor]; var driver=actors[constraintDriverActor];
            var fc=ConstraintControls(follow.Rig); var dc=ConstraintControls(driver.Rig);
            constraintFollowerControl=EditorGUILayout.Popup("Constrain control",Mathf.Clamp(constraintFollowerControl,0,fc.Length-1),fc);
            constraintDriverControl=EditorGUILayout.Popup("Follow bone / IK",Mathf.Clamp(constraintDriverControl,0,dc.Length-1),dc);
            constraintKeepOffset=EditorGUILayout.Toggle("Keep current offset",constraintKeepOffset);
            if(GUILayout.Button(constraintKeepOffset ? "Attach with current offset" : "Attach · snap to target")) Guard(()=>
            {
                var c=new StudioConstraint { firstFrame=frame,lastFrame=take.lastFrame };
                SetEndpoint(c,follow,constraintFollowerControl,true); SetEndpoint(c,driver,constraintDriverControl,false);
                AddAttachment(c,constraintKeepOffset);
            });
            EditorGUILayout.HelpBox("Choose a hand IK control to follow a steering or door bone. Offsets use the target's local orientation, in metres. Bike controls use their driven joints (for example Bars_Joint). Constraints save separately from pose keys; export bakes their motion.",MessageType.None);
            take.constraints=take.constraints ?? new List<StudioConstraint>();
            for(int i=0;i<take.constraints.Count;i++)
            {
                var c=take.constraints[i];
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField((c.drivenActor ? c.drivenActor.name : "Missing")+" / "+(c.drivenIK>=0 ? LimbNames[Mathf.Clamp(c.drivenIK,0,3)] : c.drivenBone),EditorStyles.boldLabel);
                EditorGUILayout.LabelField("→ "+(c.targetActor ? c.targetActor.name : "Missing")+" / "+(c.targetIK>=0 ? LimbNames[Mathf.Clamp(c.targetIK,0,3)] : c.targetBone),EditorStyles.wordWrappedMiniLabel);
                EditorGUI.BeginChangeCheck();
                bool enabled=EditorGUILayout.Toggle("Enabled",c.enabled);
                float weight=EditorGUILayout.Slider("Weight",c.weight,0,1);
                bool position=EditorGUILayout.Toggle("Follow position",c.position), rotation=EditorGUILayout.Toggle("Follow rotation",c.rotation);
                var offset=EditorGUILayout.Vector3Field("Position offset",c.positionOffset);
                var angles=EditorGUILayout.Vector3Field("Rotation offset",c.rotationOffset);
                int first=EditorGUILayout.IntField("First frame",c.firstFrame), last=EditorGUILayout.IntField("Last frame",c.lastFrame);
                int blend=EditorGUILayout.IntField("Blend in / out frames",c.blendFrames);
                if(EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(take,"Edit attachment constraint"); c.enabled=enabled; c.weight=weight; c.position=position; c.rotation=rotation;
                    c.positionOffset=offset; c.rotationOffset=angles; c.firstFrame=Mathf.Max(0,first); c.lastFrame=Mathf.Max(c.firstFrame,last); c.blendFrames=Mathf.Max(0,blend);
                    Save(); EvaluateConstraints(); viewport?.Repaint();
                }
                if(GUILayout.Button("Remove constraint"))
                {
                    Undo.RecordObject(take,"Remove attachment constraint"); take.constraints.RemoveAt(i); Save();
                    foreach(var a in ConstraintActors()) a.Rig.Apply(a.Pose);
                    EvaluateConstraints(); viewport?.Repaint(); break;
                }
            }
            if(!string.IsNullOrEmpty(constraintError)) EditorGUILayout.HelpBox(constraintError,MessageType.Error);
        }
        private AnimationTake SampleConstrainedTake(AnimationTake selected)
        {
            EnsureActors();
            var actors=ConstraintActors();
            var result=Instantiate(selected); result.keys=new List<PoseKey>(); result.frameRate=take.frameRate; result.lastFrame=take.lastFrame;
            result.layers=new List<StudioAnimationLayer>(); result.activeLayer=-1; // Captures below already include the layer stack.
            result.interpolation=selected.interpolation==PoseInterpolation.Stepped && !(take.constraints?.Any(c=>c.enabled) ?? false)
                ? PoseInterpolation.Stepped : PoseInterpolation.Linear;
            try
            {
                for(int f=0;f<=take.lastFrame;f++)
                {
                    if(f%30==0 && EditorUtility.DisplayCancelableProgressBar("Bake actor attachments","Frame "+f,(float)f/Mathf.Max(1,take.lastFrame))) throw new OperationCanceledException();
                    foreach(var a in actors) a.Pose=a.Take.Evaluate((float)f/take.frameRate*a.Take.frameRate) ?? a.Rig.Rest.Copy(f);
                    SolveConstraints(actors,take.constraints ?? new List<StudioConstraint>(),f);
                    result.keys.Add(actors.First(a=>a.Take==selected).Rig.Capture().Copy(f));
                }
                return result;
            }
            catch { DestroyImmediate(result); throw; }
            finally
            {
                foreach(var a in ConstraintActors()) a.Rig.Apply(a.Pose);
                EvaluateConstraints(); EditorUtility.ClearProgressBar();
            }
        }
    }
}
