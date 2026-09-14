using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public enum PoseInterpolation { Smooth, Linear, Stepped }
    public enum OutfitRole { SkinnedClothing, Body, Hat }
    [Serializable]
    public sealed class StudioVehiclePart
    {
        public GameObject source;
        public string joint = "Frame_Joint";
        public int anchorPart = -1;
        public string anchor = "";
        public Vector3 position, rotation;
        public Vector3 scale = Vector3.one;
        public bool bindFromRest = true;
    }
    [Serializable]
    public sealed class StudioHandleOffset
    {
        public string control;
        public Vector3 position, rotation;
    }
    [Serializable]
    public sealed class StudioConstraint
    {
        public bool enabled = true;
        public AnimationTake drivenActor, targetActor;
        public string drivenBone = "", targetBone = "";
        public int drivenIK = -1, targetIK = -1;
        public Vector3 positionOffset, rotationOffset;
        public bool position = true, rotation = true;
        public float weight = 1;
        public int firstFrame, lastFrame = 60, blendFrames;
    }
    [Serializable]
    public struct OutfitOptions
    {
        public OutfitRole role;
        public bool doubleSided;
        public bool hideBody;
        public float cutoutDistance;
        public Vector3 position;
        public Vector3 rotation;
        public float scale;
    }

    [Serializable]
    public struct BonePose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public static BonePose Read(Transform t) => new BonePose
            { position = t.localPosition, rotation = t.localRotation, scale = t.localScale };
        public void Apply(Transform t)
        {
            t.localPosition = position; t.localRotation = rotation; t.localScale = scale;
        }
        public static BonePose Blend(BonePose a, BonePose b, float t) => new BonePose
        {
            position = Vector3.Lerp(a.position, b.position, t),
            rotation = Quaternion.Slerp(a.rotation, b.rotation, t),
            scale = Vector3.Lerp(a.scale, b.scale, t)
        };
    }

    [Serializable]
    public struct EffectorPose
    {
        // Coordinates relative to the authoring character root, never the live character.
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 pole;
        public float weight;
        public static EffectorPose Blend(EffectorPose a, EffectorPose b, float t) => new EffectorPose
        {
            position = Vector3.Lerp(a.position, b.position, t),
            rotation = Quaternion.Slerp(a.rotation, b.rotation, t),
            pole = Vector3.Lerp(a.pole, b.pole, t),
            weight = Mathf.Lerp(a.weight, b.weight, t)
        };
    }

    [Serializable]
    public class PoseKey
    {
        public int frame;
        public BonePose[] bones;
        public EffectorPose[] effectors;
        // Old keys are full poses. Sparse keys affect only the marked channels.
        public bool selective;
        public int[] boneChannels, effectorChannels;
        public PoseKey Copy(int newFrame) => new PoseKey
        {
            frame = newFrame, bones = (BonePose[])bones.Clone(),
            effectors = (EffectorPose[])effectors.Clone(), selective=selective,
            boneChannels=boneChannels == null ? null : (int[])boneChannels.Clone(),
            effectorChannels=effectorChannels == null ? null : (int[])effectorChannels.Clone()
        };
        public static PoseKey Blend(PoseKey a, PoseKey b, float t)
        {
            var result = a.Copy(a.frame);
            for (int i = 0; i < result.bones.Length; i++)
                result.bones[i] = BonePose.Blend(a.bones[i], b.bones[i], t);
            for (int i = 0; i < result.effectors.Length; i++)
                result.effectors[i] = EffectorPose.Blend(a.effectors[i], b.effectors[i], t);
            return result;
        }
    }

    [CreateAssetMenu(menuName = "MashBox/Animation Take", fileName = "Animation Take")]
    public sealed partial class AnimationTake : ScriptableObject
    {
        public GameObject character;
        public GameObject body;
        public GameObject[] clothing = Array.Empty<GameObject>();
        public OutfitOptions[] outfitOptions = Array.Empty<OutfitOptions>();
        public int frameRate = 30;
        public int lastFrame = 60;
        [HideInInspector] public int endpointLeadFrames;
        [HideInInspector] public int endpointTailFrames;
        public bool loop = true;
        public PoseInterpolation interpolation = PoseInterpolation.Smooth;
        public string[] bonePaths = Array.Empty<string>();
        public List<PoseKey> keys = new List<PoseKey>();
        public List<AnimationTake> actors = new List<AnimationTake>();
        public List<StudioConstraint> constraints = new List<StudioConstraint>();
        public List<StudioVehiclePart> vehicleParts = new List<StudioVehiclePart>();
        public List<StudioHandleOffset> handleOffsets = new List<StudioHandleOffset>();

        public PoseKey Evaluate(float frame)
        {
            return EvaluateThrough(frame,layers == null ? 0 : layers.Count);
        }
        public PoseKey EvaluateBase(float frame)
        {
            if (keys.Count == 0) return null;
            if(keys.Exists(k=>k.selective)) return EvaluateChannels(frame);
            if (frame <= keys[0].frame) return keys[0].Copy(keys[0].frame);
            for (int i = 1; i < keys.Count; i++)
            {
                if (frame > keys[i].frame) continue;
                PoseKey a = keys[i - 1], b = keys[i];
                float t = Mathf.InverseLerp(a.frame, b.frame, frame);
                if (interpolation == PoseInterpolation.Stepped) t = frame < b.frame ? 0 : 1;
                else if (interpolation == PoseInterpolation.Smooth) t = t * t * (3 - 2 * t);
                return PoseKey.Blend(a, b, t);
            }
            return keys[keys.Count - 1].Copy(keys[keys.Count - 1].frame);
        }

        public void SetKey(PoseKey pose, int frame)
        {
            keys.RemoveAll(k => k.frame == frame);
            var full=pose.Copy(frame); full.selective=false; full.boneChannels=null; full.effectorChannels=null;
            keys.Add(full);
            keys.Sort((a, b) => a.frame.CompareTo(b.frame));
        }
        private void ChannelPair(float frame,int index,int bit,bool effector,out PoseKey a,out PoseKey b,out float t)
        {
            a=null; b=null;
            foreach(var key in keys)
            {
                var channels=effector ? key.effectorChannels : key.boneChannels;
                if(key.selective && (channels==null || index>=channels.Length || (channels[index]&bit)==0)) continue;
                if(key.frame<=frame) a=key;
                if(key.frame>=frame) { b=key; break; }
            }
            if(a==null) a=b ?? keys[0];
            if(b==null) b=a;
            t=Mathf.InverseLerp(a.frame,b.frame,frame);
            if(interpolation==PoseInterpolation.Stepped) t=frame<b.frame ? 0 : 1;
            else if(interpolation==PoseInterpolation.Smooth) t=t*t*(3-2*t);
        }
        private PoseKey EvaluateChannels(float frame)
        {
            var result=keys[0].Copy(Mathf.RoundToInt(frame));
            result.selective=false; result.boneChannels=null; result.effectorChannels=null;
            for(int i=0;i<result.bones.Length;i++)
                for(int bit=1;bit<=4;bit*=2)
                {
                    ChannelPair(frame,i,bit,false,out var a,out var b,out float t);
                    if(bit==1) result.bones[i].position=Vector3.Lerp(a.bones[i].position,b.bones[i].position,t);
                    else if(bit==2) result.bones[i].rotation=Quaternion.Slerp(a.bones[i].rotation,b.bones[i].rotation,t);
                    else result.bones[i].scale=Vector3.Lerp(a.bones[i].scale,b.bones[i].scale,t);
                }
            for(int i=0;i<result.effectors.Length;i++)
                for(int bit=1;bit<=8;bit*=2)
                {
                    ChannelPair(frame,i,bit,true,out var a,out var b,out float t);
                    var e=EffectorPose.Blend(a.effectors[i],b.effectors[i],t);
                    if(bit==1) result.effectors[i].position=e.position;
                    else if(bit==2) result.effectors[i].rotation=e.rotation;
                    else if(bit==4) result.effectors[i].pole=e.pole;
                    else result.effectors[i].weight=e.weight;
                }
            return result;
        }
        public void SetChannelKey(PoseKey source,int frame,int[] bones,int[] effectors)
        {
            var key=keys.Find(k=>k.frame==frame);
            if(key==null)
            {
                key=(Evaluate(frame) ?? source).Copy(frame); key.selective=keys.Count>0;
                key.boneChannels=new int[source.bones.Length]; key.effectorChannels=new int[source.effectors.Length];
                keys.Add(key); keys.Sort((a,b)=>a.frame.CompareTo(b.frame));
            }
            for(int i=0;i<bones.Length;i++)
            {
                int mask=bones[i];
                if((mask&1)!=0) key.bones[i].position=source.bones[i].position;
                if((mask&2)!=0) key.bones[i].rotation=source.bones[i].rotation;
                if((mask&4)!=0) key.bones[i].scale=source.bones[i].scale;
                if(key.selective) key.boneChannels[i]|=mask;
            }
            for(int i=0;i<effectors.Length;i++)
            {
                int mask=effectors[i];
                if((mask&1)!=0) key.effectors[i].position=source.effectors[i].position;
                if((mask&2)!=0) key.effectors[i].rotation=source.effectors[i].rotation;
                if((mask&4)!=0) key.effectors[i].pole=source.effectors[i].pole;
                if((mask&8)!=0) key.effectors[i].weight=source.effectors[i].weight;
                if(key.selective) key.effectorChannels[i]|=mask;
            }
        }
    }
}
