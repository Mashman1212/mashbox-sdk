using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
 public sealed partial class AnimationStudioWindow
 {
  [Serializable] private sealed class InbetweenPose { public int frame; public Vector3[] positions; public Quaternion[] rotations; }
  [Serializable] private sealed class InbetweenRequest
  {
   public int modelFrames,seed,first,last,contextFirst,fps;
   public string[] names,paths;
   public Vector3[] restPositions;
   public Quaternion[] restRotations;
   public InbetweenPose[] poses;
   public PoseKey firstPose,lastPose;
   public string sourceHash;
  }
  [Serializable] private sealed class InbetweenFrame { public Vector3 position; public Quaternion[] rotations; }
  [Serializable] private sealed class InbetweenResult { public int version,fps; public string[] names; public InbetweenFrame[] frames; public float contactPeakSpeed; }
  [SerializeField] private AnimationTake inbetweenActor;
  [SerializeField] private int inbetweenFirst=10,inbetweenLast=50;
  [SerializeField] private string importedInbetweenJob;
  private static readonly HumanBodyBones[] InbetweenBones = {
   HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest,HumanBodyBones.Neck,HumanBodyBones.Head,
   HumanBodyBones.LeftShoulder,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,
   HumanBodyBones.RightShoulder,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,
   HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,
   HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,HumanBodyBones.RightToes };
  private static string InbetweenSourceHash(AnimationTake actor)
  {
   string snapshot=actor.frameRate+"|"+actor.lastFrame+"|"+actor.interpolation+"|"+string.Join("|",actor.bonePaths)+"|"+string.Join("|",actor.keys.Select(k=>JsonUtility.ToJson(k)))+"|"+string.Join("|",actor.layers.Select(l=>JsonUtility.ToJson(l)));
   using(var hash=System.Security.Cryptography.SHA256.Create())
    return Convert.ToBase64String(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(snapshot)));
  }
  private void DrawInbetweenTools()
  {
   EditorGUILayout.LabelField("AI IN-BETWEENS · LOCAL GPU",EditorStyles.boldLabel);
   inbetweenFirst=EditorGUILayout.IntField("First frame",inbetweenFirst);
   inbetweenLast=EditorGUILayout.IntField("Last frame",inbetweenLast);
   motionSeed=EditorGUILayout.IntField("Variation seed",motionSeed);
   using(new EditorGUI.DisabledScope(MotionRunning || !take))
   {
    if(GUILayout.Button("Use selected timeline range"))
    { if(timelineRangeStart>=0) { inbetweenFirst=Mathf.Min(timelineRangeStart,timelineRangeEnd); inbetweenLast=Mathf.Max(timelineRangeStart,timelineRangeEnd); } }
    if(GUILayout.Button("Generate AI in-betweens")) Guard(()=>StartInbetween(OutfitActor,inbetweenFirst,inbetweenLast));
   }
   using(new EditorGUI.DisabledScope(MotionRunning || !inbetweenActor || motionJob==importedInbetweenJob || !File.Exists(Path.Combine(motionJob,"inbetween.json"))))
    if(GUILayout.Button("Add result as override layer")) Guard(ImportInbetween);
   EditorGUILayout.HelpBox("Pose-only Kimodo: generate between the two range endpoints with neighboring motion as context. Adds a separate full-body override layer confined to that interval. The source take must stay unchanged until import. Seed changes produce variations.",MessageType.None);
  }
  private InbetweenRequest CaptureInbetween(AnimationTake actor,int sharedFirst,int sharedLast)
  {
   if(!actor || !actor.character) throw new InvalidOperationException("Select a Humanoid actor first.");
   int first=Mathf.RoundToInt((float)sharedFirst/take.frameRate*actor.frameRate),last=Mathf.RoundToInt((float)sharedLast/take.frameRate*actor.frameRate);
   if(first<0 || last>actor.lastFrame || last<=first || (float)(last-first)/actor.frameRate<.2f || (float)(last-first)/actor.frameRate>7.8f)
    throw new InvalidOperationException("Select a range of 0.2–7.8 seconds within this actor's take.");
   if(take.constraints.Any(c=>c.enabled && c.weight>0 && (c.drivenActor==actor || (!c.drivenActor && actor==take)))) throw new InvalidOperationException("Disable this actor's attachment constraints before generating. This first version conditions on human poses only.");
   if(actor.layers.Any(l=>l.solo && !l.muted)) throw new InvalidOperationException("Turn off layer Solo before generating a new override layer.");
   using(var sampler=new AuthoringRig(actor.character,null))
   {
    if(!sampler.Animator) throw new InvalidOperationException("AI in-betweens currently require a Humanoid actor.");
    var human=InbetweenBones.Where(b=>sampler.Animator.GetBoneTransform(b)).ToArray();
    var bones=human.Select(b=>sampler.Animator.GetBoneTransform(b)).ToArray();
    var times=new[]{Mathf.Max(0,first-Mathf.CeilToInt(actor.frameRate/30f)),first,last,Mathf.Min(actor.lastFrame,last+Mathf.CeilToInt(actor.frameRate/30f))}.Distinct().ToArray();
    var req=new InbetweenRequest { first=first,last=last,contextFirst=times[0],fps=actor.frameRate,seed=motionSeed,
     modelFrames=Mathf.RoundToInt((float)(times.Last()-times[0])/actor.frameRate*30)+1,
     names=human.Select(b=>b.ToString()).ToArray(),paths=actor.bonePaths,
     restPositions=bones.Select(b=>b.position).ToArray(),restRotations=bones.Select(b=>b.rotation).ToArray(),sourceHash=InbetweenSourceHash(actor) };
    var constraints=new List<InbetweenPose>();
    foreach(int f in times)
    {
     sampler.Apply(actor.Evaluate(f));
     if(f==first) req.firstPose=actor.Evaluate(f);
     if(f==last) req.lastPose=actor.Evaluate(f);
     constraints.Add(new InbetweenPose { frame=Mathf.RoundToInt((float)(f-times[0])/actor.frameRate*30),positions=bones.Select(b=>b.position).ToArray(),rotations=bones.Select(b=>b.rotation).ToArray() });
    }
    req.poses=constraints.GroupBy(p=>p.frame).Select(g=>g.Last()).ToArray();
    return req;
   }
  }
  private void StartInbetween(AnimationTake actor,int first,int last)
  {
   if(!MotionGenerationAvailable) throw new InvalidOperationException("Local generation is not enabled on this workstation.");
   if(MotionRunning) throw new InvalidOperationException("Wait for the current GPU job or cancel it first.");
   FlushEdits(); var request=CaptureInbetween(actor,first,last);
   string python=Path.Combine(MotionToolPath,".venv-kimodo/Scripts/python.exe"),worker=Path.Combine(MotionToolPath,"inbetween_worker.py");
   if(!File.Exists(python) || !File.Exists(worker)) throw new InvalidOperationException("Kimodo local tools are missing.");
   motionJob=Path.Combine(MotionToolPath,"jobs",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-inbetween-"+Guid.NewGuid().ToString("N").Substring(0,8));
   Directory.CreateDirectory(motionJob); string file=Path.Combine(motionJob,"request.json"); File.WriteAllText(file,JsonUtility.ToJson(request));
   var info=new ProcessStartInfo(python) { Arguments="-u \""+worker+"\" \""+file+"\"",WorkingDirectory=MotionToolPath,UseShellExecute=false,CreateNoWindow=true };
   using(var process=Process.Start(info)) { motionPid=process.Id; motionStartTicks=process.StartTime.ToUniversalTime().Ticks.ToString(); }
   inbetweenActor=actor; inbetweenFirst=first; inbetweenLast=last;
   motionStatus=new MotionStatus { state="running",message="Starting local AI in-betweens…" }; RememberMotionJob();
   inspectorTab=Array.IndexOf(PrivateInspectorTabs,"Generate"); viewport?.Repaint();
  }
  private void ImportInbetween()
  {
   if(!MotionGenerationAvailable || MotionRunning || !inbetweenActor) throw new InvalidOperationException("No completed in-between job is available.");
   if(inbetweenActor!=take && !take.actors.Contains(inbetweenActor)) throw new InvalidOperationException("Open the original actor session to import this result.");
   var request=JsonUtility.FromJson<InbetweenRequest>(File.ReadAllText(Path.Combine(motionJob,"request.json")));
   var result=JsonUtility.FromJson<InbetweenResult>(File.ReadAllText(Path.Combine(motionJob,"inbetween.json")));
   ApplyInbetween(inbetweenActor,request,result); importedInbetweenJob=motionJob;
   message="Added AI override layer. Review foot contacts; native-model peak contact speed: "+result.contactPeakSpeed.ToString("0.000")+" m/s.";
   Seek(frame);
  }
  private void ApplyInbetween(AnimationTake actor,InbetweenRequest request,InbetweenResult result)
  {
   if(InbetweenSourceHash(actor)!=request.sourceHash) throw new InvalidOperationException("The source take changed after generation. Generate again from the updated poses.");
   if(result.version!=1 || result.fps!=30 || result.frames==null || result.frames.Length!=request.modelFrames || !request.paths.SequenceEqual(actor.bonePaths))
    throw new InvalidOperationException("AI result does not match the captured interval or skeleton.");
   if(result.names==null || result.names.Distinct().Count()!=result.names.Length || result.names.Length<18 || result.frames.Any(f=>f==null || f.rotations==null || f.rotations.Length!=result.names.Length || !float.IsFinite(f.position.x) || !float.IsFinite(f.position.y) || !float.IsFinite(f.position.z) || f.rotations.Any(q=>!float.IsFinite(q.x)||!float.IsFinite(q.y)||!float.IsFinite(q.z)||!float.IsFinite(q.w) || q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w<.5f)))
    throw new InvalidOperationException("AI result contains invalid transforms.");
   var layer=new StudioAnimationLayer { name="AI in-betweens "+request.first+"–"+request.last,mode=StudioLayerMode.Override,limitRange=true,firstFrame=request.first,lastFrame=request.last,bones=actor.bonePaths.ToList(),effectors=new[]{15,15,15,15} };
   using(var sampler=new AuthoringRig(actor.character,null))
   {
    var targets=result.names.Select(n=>sampler.Animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),n))).ToArray();
    if(targets.Any(b=>!b)) throw new InvalidOperationException("AI output references a missing human bone.");
    var ordered=Enumerable.Range(0,targets.Length).OrderBy(i=>AnimationUtility.CalculateTransformPath(targets[i],sampler.Root.transform).Count(c=>c=='/')).ToArray();
    var hips=sampler.Animator.GetBoneTransform(HumanBodyBones.Hips);
    for(int f=request.first;f<=request.last;f++)
    {
     float time=(float)(f-request.contextFirst)/request.fps*30;
     int a=Mathf.Clamp(Mathf.FloorToInt(time),0,result.frames.Length-1),b=Mathf.Min(a+1,result.frames.Length-1); float t=time-a;
     var from=result.frames[a]; var to=result.frames[b];
     if(from.rotations.Length!=targets.Length || to.rotations.Length!=targets.Length) throw new InvalidOperationException("Invalid result joint count.");
     var basePose=actor.Evaluate(f); for(int i=0;i<basePose.effectors.Length;i++) basePose.effectors[i].weight=0;
     sampler.Apply(basePose);
     foreach(int i in ordered) targets[i].rotation=Quaternion.Slerp(from.rotations[i],to.rotations[i],t);
     hips.position=Vector3.Lerp(from.position,to.position,t);
     var key=sampler.Capture();
     if(f==request.first) key=request.firstPose.Copy(f);
     if(f==request.last) key=request.lastPose.Copy(f);
     key.frame=f; key.selective=true; key.boneChannels=Enumerable.Repeat(7,key.bones.Length).ToArray(); key.effectorChannels=new[]{15,15,15,15}; layer.keys.Add(key);
    }
   }
   Undo.RegisterCompleteObjectUndo(actor,"Add AI in-between layer"); actor.layers.Add(layer); actor.activeLayer=actor.layers.Count-1;
   EditorUtility.SetDirty(actor); Save();
  }
 }
}
