using UnityEngine;
namespace MashBoxSDK.Cloth {
public static class MashBoxClothCollisionFilter {
 public static ulong BoneMask(Transform bone,Transform[] starts,Transform[] ends,int count){
 for(var current=bone;current;current=current.parent){ulong mask=0;
 for(int c=0;c<count;c++)if(starts[c]&&ends[c] && (current==starts[c]||current==ends[c]||(current.IsChildOf(starts[c])&&ends[c].IsChildOf(current))))mask|=1UL<<c;
 if(mask!=0)return mask;
 }return 0;
 }
 public static ulong VertexMask(BoneWeight w,ulong[] bones){
 float peak=Mathf.Max(w.weight0,Mathf.Max(w.weight1,Mathf.Max(w.weight2,w.weight3)));
 if(peak<=0)return ulong.MaxValue;
 float threshold=Mathf.Max(.05f,peak*.2f);ulong mask=0;
 void Add(int index,float weight){if(weight>=threshold&&index>=0&&index<bones.Length)mask|=bones[index];}
 Add(w.boneIndex0,w.weight0);Add(w.boneIndex1,w.weight1);Add(w.boneIndex2,w.weight2);Add(w.boneIndex3,w.weight3);return mask;
 }
}
}
