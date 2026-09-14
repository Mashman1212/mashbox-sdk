using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
 public sealed partial class AnimationStudioWindow
 {
  [SerializeField] private bool lockActorMeshes=true;
  private bool CanTranslateBone(int index) => rig != null && index >= 0 && index < rig.Bones.Length && rig.Bones[index] == rig.Root.transform;
  private sealed class RigSelectionItem
  {
   public AnimationTake Actor; public int Index; public bool IK,Pole;
   public bool Matches(AnimationTake a,int i,bool ik,bool pole) => Actor==a && Index==i && IK==ik && Pole==pole;
  }
  private sealed class RigPickPoint { public RigSelectionItem Item; public Vector2 Point; }
  private readonly List<RigSelectionItem> rigSelection=new List<RigSelectionItem>();
  private readonly List<RigPickPoint> rigPickPoints=new List<RigPickPoint>();
  private bool rigSelectionExplicit, addingLayerSelection;
  private bool selectingRig, collectingRigPoints, addingRigSelection;
  private Vector2 rigMarqueeStart,rigMarqueeEnd;
  private void PickRigControl(int index,bool ik=false,bool pole=false)
  {
   SetRigControlSelection(index,ik,pole,Event.current!=null && Event.current.shift);
  }
  private void SetRigControlSelection(int index,bool ik,bool pole,bool add)
  {
   rigSelectionExplicit=true;
   var existing=rigSelection.FirstOrDefault(s=>s.Matches(take,index,ik,pole));
   if(!add) rigSelection.Clear();
   if(add && existing!=null) rigSelection.Remove(existing);
   else rigSelection.Add(new RigSelectionItem { Actor=take,Index=index,IK=ik,Pole=pole });
  }
  private bool RigControlSelected(int index,bool ik=false,bool pole=false) =>
   rigSelectionExplicit ? rigSelection.Any(s=>s.Matches(take,index,ik,pole)) : pickerActorSelected && control==index && ikMode==ik && (!ik || poleMode==pole);
  private void RegisterRigPoint(int index,bool ik,bool pole,Vector3 position)
  {
   if(!collectingRigPoints || !viewport.camera || viewport.camera.WorldToViewportPoint(position).z<=0) return;
   rigPickPoints.Add(new RigPickPoint { Item=new RigSelectionItem { Actor=take,Index=index,IK=ik,Pole=pole },Point=HandleUtility.WorldToGUIPoint(position) });
  }
  private void ActivateRigSelection(RigSelectionItem item)
  {
   int actor= item.Actor==take ? -1 : actorPreviews.FindIndex(a=>a.Take==item.Actor);
   if(actor<0 && item.Actor!=take) return;
   activeActor=actor;
   Action select=()=>{control=item.Index; ikMode=item.IK; poleMode=item.Pole; if(item.Pole) rotate=false;};
   if(actor<0) select(); else InActor(actorPreviews[actor],select);
  }
  private void SelectRigRectangle(Rect rectangle,bool add)
  {
   rigSelectionExplicit=true;
   if(!add) rigSelection.Clear();
   foreach(var point in rigPickPoints)
    if(rectangle.Contains(point.Point) && !rigSelection.Any(s=>s.Matches(point.Item.Actor,point.Item.Index,point.Item.IK,point.Item.Pole))) rigSelection.Add(point.Item);
   if(rigSelection.Count>0) ActivateRigSelection(rigSelection.Last());
   Repaint(); viewport?.Repaint();
  }
  private void DuringSceneGUI(SceneView view)
  {
   if(view!=viewport || rig==null || !take || inActorScope || drawingActorControls) { DrawStudioSceneGUI(view); return; }
   rigSelection.RemoveAll(s=>!s.Actor || (s.Actor!=take && !take.actors.Contains(s.Actor)));
   int id=GUIUtility.GetControlID("MashBoxRigMarquee".GetHashCode(),FocusType.Passive);
   var evt=Event.current;
   if(evt.type==EventType.Layout && lockActorMeshes && !evt.alt) HandleUtility.AddDefaultControl(id);
   HandleRigSelectionKeys(evt);
   rigPickPoints.Clear(); collectingRigPoints=true;
   try { DrawStudioSceneGUI(view); }
   finally { collectingRigPoints=false; }
   if(rigSelection.Count>1 && !selectingRig) DrawRigGroupTransform();
   if(evt.rawType==EventType.MouseUp) { FinishSceneEdit(); FinishActorEdits(); }
   if(!lockActorMeshes || playing || evt.alt) return;
   if(evt.type==EventType.MouseDown && evt.button==0 && GUIUtility.hotControl==0 && HandleUtility.nearestControl==id)
   {
    FinishSceneEdit(); FinishActorEdits(); rigMarqueeStart=rigMarqueeEnd=evt.mousePosition;
    selectingRig=true; addingRigSelection=evt.shift; GUIUtility.hotControl=id; evt.Use();
   }
   if(selectingRig && GUIUtility.hotControl==id)
   {
    if(evt.type==EventType.MouseDrag) { rigMarqueeEnd=evt.mousePosition; evt.Use(); view.Repaint(); }
    Rect rect=Rect.MinMaxRect(Mathf.Min(rigMarqueeStart.x,rigMarqueeEnd.x),Mathf.Min(rigMarqueeStart.y,rigMarqueeEnd.y),Mathf.Max(rigMarqueeStart.x,rigMarqueeEnd.x),Mathf.Max(rigMarqueeStart.y,rigMarqueeEnd.y));
    if(evt.type==EventType.Repaint)
    {
     Handles.BeginGUI(); EditorGUI.DrawRect(rect,new Color(.2f,.6f,.85f,.2f));
     Handles.color=new Color(.4f,.8f,1); Handles.DrawAAPolyLine(new Vector3(rect.xMin,rect.yMin),new Vector3(rect.xMax,rect.yMin),new Vector3(rect.xMax,rect.yMax),new Vector3(rect.xMin,rect.yMax),new Vector3(rect.xMin,rect.yMin)); Handles.EndGUI();
    }
    if(evt.rawType==EventType.MouseUp)
    { GUIUtility.hotControl=0; selectingRig=false; SelectRigRectangle(rect,addingRigSelection); evt.Use(); }
    else if(evt.type==EventType.KeyDown && evt.keyCode==KeyCode.Escape)
    { GUIUtility.hotControl=0; selectingRig=false; evt.Use(); view.Repaint(); }
   }
  }
  private void InSelectedActor(AnimationTake actor,Action action)
  {
   if(actor==take) action();
   else { var preview=actorPreviews.FirstOrDefault(a=>a.Take==actor); if(preview!=null) InActor(preview,action); }
  }
  private bool HandleRigSelectionKeys(Event evt)
  {
   if(inActorScope || rigSelection.Count<2 || evt.type!=EventType.KeyDown || EditorGUIUtility.editingTextField || evt.alt || evt.control || evt.command) return false;
   bool channel=evt.shift && (evt.keyCode==KeyCode.W || evt.keyCode==KeyCode.E);
   if(!channel) return false;
   bool rotationChannel=evt.keyCode==KeyCode.E;
   foreach(var actor in rigSelection.Select(s=>s.Actor).Distinct().ToArray()) InSelectedActor(actor,()=>KeySelectedChannel(rotationChannel));
   evt.Use(); return true;
  }
  private sealed class SelectedTransform
  { public RigSelectionItem Item; public Vector3 Position; public Quaternion Rotation; public int Depth; }
  private List<SelectedTransform> SelectedRigTransforms()
  {
   var result=new List<SelectedTransform>();
   foreach(var item in rigSelection) InSelectedActor(item.Actor,()=>
   {
    var entry=new SelectedTransform { Item=item };
    if(item.IK)
    {
     var e=DisplayEffector(item.Index); if(e.weight<=0) e=rig.MatchEffector(item.Index,0);
     entry.Position=rig.Root.transform.TransformPoint(item.Pole ? e.pole : e.position);
     entry.Rotation=rig.Root.transform.rotation*e.rotation; entry.Depth=1000;
    }
    else
    {
     var bone=IsBike ? BikeJoint(item.Index) : rig.Bones[item.Index];
     entry.Position=bone.position; entry.Rotation=bone.rotation;
     for(var t=bone;t;t=t.parent) entry.Depth++;
    }
    result.Add(entry);
   });
   return result;
  }
  private void DrawRigGroupTransform()
  {
   if(playing || Event.current.alt) return;
   var entries=SelectedRigTransforms(); if(entries.Count<2) return;
   bool groupRotate=rotate;
   if(!groupRotate)
   {
    var movable=new List<SelectedTransform>();
    foreach(var entry in entries) InSelectedActor(entry.Item.Actor,()=>
    { if(entry.Item.IK || (IsBike ? entry.Item.Index==0 || entry.Item.Index==8 : CanTranslateBone(entry.Item.Index))) movable.Add(entry); });
    if(movable.Count==0) groupRotate=true; else entries=movable;
   }
   Vector3 pivot=Vector3.zero; foreach(var entry in entries) pivot+=entry.Position; pivot/=entries.Count;
   EditorGUI.BeginChangeCheck();
   Vector3 moved=pivot; Quaternion turn=Quaternion.identity;
   if(groupRotate) turn=Handles.RotationHandle(turn,pivot); else moved=Handles.PositionHandle(pivot,Quaternion.identity);
   if(!EditorGUI.EndChangeCheck()) return;
   foreach(var entry in entries.OrderBy(e=>e.Depth)) InSelectedActor(entry.Item.Actor,()=>
   {
    var item=entry.Item;
    if(item.IK)
    {
     if(groupRotate && item.Pole) return;
     var e=DisplayEffector(item.Index); if(e.weight<=0) e=rig.MatchEffector(item.Index,0);
     if(groupRotate) e.rotation=Quaternion.Inverse(rig.Root.transform.rotation)*turn*entry.Rotation;
     else if(item.Pole) e.pole=rig.Root.transform.InverseTransformPoint(entry.Position+moved-pivot);
     else e.position=rig.Root.transform.InverseTransformPoint(entry.Position+moved-pivot);
     e.weight=1; pose.effectors[item.Index]=e;
    }
    else if(IsBike)
    {
     var bone=BikeJoint(item.Index);
     if(item.Index>0 && item.Index<8)
     {
      if(!groupRotate) return;
      turn.ToAngleAxis(out float angle,out var axis); if(angle>180) angle-=360;
      SetBikeAngle(item.Index,BikeAngle(item.Index)+angle*Vector3.Dot(axis,bone.TransformDirection(BikeAxis(item.Index))));
     }
     else { if(groupRotate) { if(item.Index==8) return; bone.rotation=turn*entry.Rotation; } else bone.position=entry.Position+moved-pivot; }
     pose=rig.Capture();
    }
    else
    {
     PrepareFKEdit(item.Index); var bone=rig.Bones[item.Index];
     if(groupRotate) bone.rotation=turn*entry.Rotation; else bone.position=entry.Position+moved-pivot;
     pose.bones[item.Index]=BonePose.Read(bone);
    }
    PreviewSceneEdit(groupRotate ? "Rotate selected rig controls" : "Move selected rig controls");
   });
  }
  private void AddRigSelectionMasks(int[] bones,int[] effectors,int mask)
  {
   foreach(var item in rigSelection.Where(s=>s.Actor==take))
   {
    if(item.IK)
    { if(item.Pole && mask==2) continue; effectors[item.Index]|=(item.Pole?4:mask)|8; }
    else
    {
     int index=IsBike ? Array.IndexOf(rig.Bones,BikeJoint(item.Index)) : item.Index;
     if(index<0 || index>=bones.Length) continue;
     bones[index]|=mask;
     if(IsBike && item.Index==3 && mask==2) foreach(int pedal in new[]{4,5})
     { int p=Array.IndexOf(rig.Bones,BikeJoint(pedal)); if(p>=0) bones[p]|=mask; }
    }
   }
  }
 }
}
