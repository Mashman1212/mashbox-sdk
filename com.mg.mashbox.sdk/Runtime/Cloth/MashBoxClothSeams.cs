using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.Cloth {
// Render vertices may split at UV/normal seams while sharing one physical point.
public static class MashBoxClothSeams {
 public static int[] Build(Mesh mesh){
 var vertices=mesh.vertices;var bones=mesh.boneWeights;var map=new int[vertices.Length];
 var groups=new Dictionary<(Vector3,BoneWeight),int>();
 for(int i=0;i<vertices.Length;i++){
 var key=(vertices[i],bones.Length==vertices.Length?bones[i]:default(BoneWeight));
 if(!groups.TryGetValue(key,out int first)){first=i;groups.Add(key,first);}map[i]=first;
 }return map;
 }
 // Preserve existing pins when repairing conflicting seam weights.
 public static bool Synchronize(float[] values,int[] seams){
 if(values==null||values.Length!=seams.Length)return false;
 bool changed=false;
 for(int i=0;i<values.Length;i++){int root=seams[i];float value=Mathf.Min(values[root],values[i]);changed|=values[root]!=value;values[root]=value;}
 for(int i=0;i<values.Length;i++){float value=values[seams[i]];changed|=values[i]!=value;values[i]=value;}
 return changed;
 }
}
}
