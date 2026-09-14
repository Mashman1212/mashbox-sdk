using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public enum StudioLayerMode { Additive, Override }
    [Serializable]
    public sealed class StudioJointOffset
    {
        public string path;
        public Vector3 angles;
        public Vector3 axisX=Vector3.right,axisY=Vector3.up,axisZ=Vector3.forward;
        public Quaternion Rotation => Quaternion.AngleAxis(angles.x,axisX)*Quaternion.AngleAxis(angles.y,axisY)*Quaternion.AngleAxis(angles.z,axisZ);
    }
    [Serializable]
    public sealed class StudioAnimationLayer
    {
        public string name="Correction";
        public StudioLayerMode mode;
        public float weight=1;
        public bool muted,solo;
        public bool limitRange;
        public int firstFrame,lastFrame;
        public List<string> bones=new List<string>();
        public int[] effectors=new int[4];
        public List<PoseKey> keys=new List<PoseKey>();
        public List<StudioJointOffset> offsets=new List<StudioJointOffset>();
    }
    public sealed partial class AnimationTake
    {
        public List<StudioAnimationLayer> layers=new List<StudioAnimationLayer>();
        public int activeLayer=-1;
        public bool LayerEnabled(int index) => layers!=null && index>=0 && index<layers.Count && !layers[index].muted && layers[index].weight>0 &&
            (!layers.Any(l=>l.solo && !l.muted) || layers[index].solo);
        internal static Quaternion RotationPower(Quaternion q,float weight)
        {
            q=q.normalized;
            if(q.w<0) q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
            q.ToAngleAxis(out float angle,out var axis);
            return angle<.0001f || axis.sqrMagnitude<.0001f ? Quaternion.identity : Quaternion.AngleAxis(angle*weight,axis);
        }
        private bool LayerChannel(StudioAnimationLayer layer,float frame,int index,int bit,bool effector,out PoseKey a,out PoseKey b,out float blend)
        {
            a=null; b=null; blend=0;
            foreach(var key in layer.keys)
            {
                var mask=effector ? key.effectorChannels : key.boneChannels;
                if(mask==null || index>=mask.Length || (mask[index]&bit)==0) continue;
                if(key.frame<=frame) a=key;
                if(key.frame>=frame) { b=key; break; }
            }
            if(a==null) a=b; if(b==null) b=a;
            if(a==null) return false;
            blend=Mathf.InverseLerp(a.frame,b.frame,frame);
            if(interpolation==PoseInterpolation.Stepped) blend=frame<b.frame ? 0 : 1;
            else if(interpolation==PoseInterpolation.Smooth) blend=blend*blend*(3-2*blend);
            return true;
        }
        public PoseKey EvaluateThrough(float frame,int layerCount)
        {
            var result=EvaluateBase(frame); if(result==null || layers==null) return result;
            for(int l=0;l<Mathf.Min(layerCount,layers.Count);l++)
            {
                if(!LayerEnabled(l)) continue;
                var layer=layers[l];
                if(layer.limitRange && (frame<layer.firstFrame || frame>layer.lastFrame)) continue;
                float w=Mathf.Clamp01(layer.weight); bool add=layer.mode==StudioLayerMode.Additive;
                for(int i=0;i<result.bones.Length;i++)
                {
                    if(i>=bonePaths.Length || !layer.bones.Contains(bonePaths[i])) continue;
                    for(int bit=1;bit<=4;bit*=2)
                    {
                        if(!LayerChannel(layer,frame,i,bit,false,out var a,out var b,out float t)) continue;
                        var sample=BonePose.Blend(a.bones[i],b.bones[i],t); var current=result.bones[i];
                        if(bit==1) current.position=add ? current.position+sample.position*w : Vector3.Lerp(current.position,sample.position,w);
                        else if(bit==2) current.rotation=add ? current.rotation*RotationPower(sample.rotation,w) : Quaternion.Slerp(current.rotation,sample.rotation,w);
                        else current.scale=add ? current.scale+sample.scale*w : Vector3.Lerp(current.scale,sample.scale,w);
                        result.bones[i]=current;
                    }
                    foreach(var offset in layer.offsets)
                        if(offset.path==bonePaths[i]) result.bones[i].rotation*=RotationPower(offset.Rotation,w);
                }
                for(int i=0;i<result.effectors.Length;i++)
                    for(int bit=1;bit<=8;bit*=2)
                    {
                        if(i>=layer.effectors.Length || (layer.effectors[i]&bit)==0 || !LayerChannel(layer,frame,i,bit,true,out var a,out var b,out float t)) continue;
                        var sample=EffectorPose.Blend(a.effectors[i],b.effectors[i],t); var current=result.effectors[i];
                        if(bit==1) current.position=add ? current.position+sample.position*w : Vector3.Lerp(current.position,sample.position,w);
                        else if(bit==2) current.rotation=add ? current.rotation*RotationPower(sample.rotation,w) : Quaternion.Slerp(current.rotation,sample.rotation,w);
                        else if(bit==4) current.pole=add ? current.pole+sample.pole*w : Vector3.Lerp(current.pole,sample.pole,w);
                        else current.weight=Mathf.Clamp01(add ? current.weight+sample.weight*w : Mathf.Lerp(current.weight,sample.weight,w));
                        result.effectors[i]=current;
                    }
            }
            return result;
        }
        public void SetLayerKey(int layerIndex,PoseKey desired,int frame,int[] boneMask,int[] effectorMask)
        {
            if(layers[layerIndex].limitRange && (frame<layers[layerIndex].firstFrame || frame>layers[layerIndex].lastFrame)) throw new InvalidOperationException("This layer only affects its displayed frame interval. Key inside that interval or use another layer.");
            if(!LayerEnabled(layerIndex)) throw new InvalidOperationException("Enable the selected layer and give it a nonzero weight before keying.");
            for(int i=layerIndex+1;i<layers.Count;i++)
                if(LayerEnabled(i)) throw new InvalidOperationException("Mute layers above the editing layer, or move it to the top, before keying.");
            var layer=layers[layerIndex]; var lower=EvaluateThrough(frame,layerIndex);
            float w=Mathf.Clamp(layer.weight,.0001f,1); bool add=layer.mode==StudioLayerMode.Additive;
            var key=layer.keys.Find(k=>k.frame==frame);
            bool created=key==null;
            if(created) key=new PoseKey { frame=frame,selective=true,bones=new BonePose[desired.bones.Length],effectors=new EffectorPose[4],boneChannels=new int[desired.bones.Length],effectorChannels=new int[4] };
            bool any=false;
            for(int i=0;i<desired.bones.Length;i++)
            {
                if(!layer.bones.Contains(bonePaths[i])) continue;
                int mask=boneMask==null ? 7 : boneMask[i]; if(mask==0) continue; any=true;
                var value=desired.bones[i]; var baseline=lower.bones[i];
                foreach(var offset in layer.offsets.AsEnumerable().Reverse()) if(offset.path==bonePaths[i]) value.rotation*=Quaternion.Inverse(RotationPower(offset.Rotation,w));
                if((mask&1)!=0) key.bones[i].position=(add ? Vector3.zero : baseline.position)+(value.position-baseline.position)/w;
                if((mask&2)!=0) key.bones[i].rotation=(add ? Quaternion.identity : baseline.rotation)*RotationPower(Quaternion.Inverse(baseline.rotation)*value.rotation,1/w);
                if((mask&4)!=0) key.bones[i].scale=(add ? Vector3.zero : baseline.scale)+(value.scale-baseline.scale)/w;
                key.boneChannels[i]|=mask;
            }
            for(int i=0;i<4;i++)
            {
                int mask=layer.effectors[i]&(effectorMask==null ? 15 : effectorMask[i]); if(mask==0) continue; any=true;
                var value=desired.effectors[i]; var baseline=lower.effectors[i];
                if((mask&1)!=0) key.effectors[i].position=(add ? Vector3.zero : baseline.position)+(value.position-baseline.position)/w;
                if((mask&2)!=0) key.effectors[i].rotation=(add ? Quaternion.identity : baseline.rotation)*RotationPower(Quaternion.Inverse(baseline.rotation)*value.rotation,1/w);
                if((mask&4)!=0) key.effectors[i].pole=(add ? Vector3.zero : baseline.pole)+(value.pole-baseline.pole)/w;
                if((mask&8)!=0) key.effectors[i].weight=(add ? 0 : baseline.weight)+(value.weight-baseline.weight)/w;
                key.effectorChannels[i]|=mask;
            }
            if(!any) throw new InvalidOperationException("Add this bone or IK control to the selected layer before keying it.");
            if(created) { layer.keys.Add(key); layer.keys.Sort((a,b)=>a.frame.CompareTo(b.frame)); }
        }
        public void ConvertLayerMode(int index,StudioLayerMode mode)
        {
            var layer=layers[index]; if(layer.mode==mode) return;
            var converted=new List<PoseKey>(); bool wasAdditive=layer.mode==StudioLayerMode.Additive;
            // Absolute and relative curves need different reference motion between keys.
            // Resample at the take frame rate to preserve the displayed correction when changing mode.
            if(layer.keys.Count>0)
                for(int f=layer.limitRange ? layer.firstFrame : 0;f<=(layer.limitRange ? layer.lastFrame : Mathf.Max(lastFrame,layer.keys.Max(k=>k.frame)));f++)
                {
                    var lower=EvaluateThrough(f,index); var key=lower.Copy(f); key.selective=true;
                    key.boneChannels=new int[key.bones.Length]; key.effectorChannels=new int[4];
                    for(int i=0;i<key.bones.Length;i++)
                        for(int bit=1;bit<=4;bit*=2)
                        {
                            if(!LayerChannel(layer,f,i,bit,false,out var a,out var b,out float t)) continue;
                            var sample=BonePose.Blend(a.bones[i],b.bones[i],t); var baseline=lower.bones[i];
                            if(bit==1) key.bones[i].position=wasAdditive ? baseline.position+sample.position : sample.position-baseline.position;
                            else if(bit==2) key.bones[i].rotation=wasAdditive ? baseline.rotation*sample.rotation : Quaternion.Inverse(baseline.rotation)*sample.rotation;
                            else key.bones[i].scale=wasAdditive ? baseline.scale+sample.scale : sample.scale-baseline.scale;
                            key.boneChannels[i]|=bit;
                        }
                    for(int i=0;i<4;i++)
                        for(int bit=1;bit<=8;bit*=2)
                        {
                            if(!LayerChannel(layer,f,i,bit,true,out var a,out var b,out float t)) continue;
                            var sample=EffectorPose.Blend(a.effectors[i],b.effectors[i],t); var baseline=lower.effectors[i];
                            if(bit==1) key.effectors[i].position=wasAdditive ? baseline.position+sample.position : sample.position-baseline.position;
                            else if(bit==2) key.effectors[i].rotation=wasAdditive ? baseline.rotation*sample.rotation : Quaternion.Inverse(baseline.rotation)*sample.rotation;
                            else if(bit==4) key.effectors[i].pole=wasAdditive ? baseline.pole+sample.pole : sample.pole-baseline.pole;
                            else key.effectors[i].weight=wasAdditive ? baseline.weight+sample.weight : sample.weight-baseline.weight;
                            key.effectorChannels[i]|=bit;
                        }
                    converted.Add(key);
                }
            layer.keys=converted; layer.mode=mode;
        }
    }
}
