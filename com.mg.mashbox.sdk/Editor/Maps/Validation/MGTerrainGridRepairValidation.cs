#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainGridRepairValidation
    {
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Fixture(int w, int h, out Vector3[] v, out Vector2[] uv) {
    v=new Vector3[w*h]; uv=new Vector2[v.Length];
    for(int z=0;z<h;z++) for(int x=0;x<w;x++) {
        int i=z*w+x; uv[i]=new Vector2(x/(float)(w-1),z/(float)(h-1));
        v[i]=new Vector3(-64+uv[i].x*512, (float)Math.Sin(i)*7, 12+uv[i].y*256);
    }
}
[MenuItem("Tools/MashBox/MG Terrain/Validate Grid Repair")]
static void Run() {
    int cases=0;
    foreach(var shape in new[]{(129,129),(17,9),(2,2),(514,258)}) {
        int w=shape.Item1,h=shape.Item2;
        Fixture(w,h,out var v,out var uv); var original=(Vector3[])v.Clone(); var f=new Vector4(-64,12,512,256);
        Check(MGTerrainGridRepair.TryRestore(v,uv,w,h,true,ref f,out int changed)&&changed==0,"Clean grid changed");
        for(int i=0;i<v.Length;i++) { v[i].x+=4; v[i].z-=3; }
        Check(MGTerrainGridRepair.TryRestore(v,uv,w,h,true,ref f,out changed)&&changed==v.Length,"Stored footprint failed");
        for(int i=0;i<v.Length;i++) Check(v[i].x==original[i].x&&v[i].z==original[i].z&&v[i].y==original[i].y,"Height or original position changed");
        Check(MGTerrainGridRepair.TryRestore(v,uv,w,h,true,ref f,out changed)&&changed==0,"Not idempotent");
        cases+=3;
    }
    Fixture(129,65,out var legacy,out var legacyUv); var before=(Vector3[])legacy.Clone(); var inferred=new Vector4();
    for(int i=0;i<legacy.Length;i+=11) { legacy[i].x+=13;legacy[i].z-=9; }
    Check(MGTerrainGridRepair.TryRestore(legacy,legacyUv,129,65,false,ref inferred,out int n)&&n>0,"Legacy localized recovery failed");
    for(int i=0;i<legacy.Length;i++) Check(Math.Abs(legacy[i].x-before[i].x)<.0001&&Math.Abs(legacy[i].z-before[i].z)<.0001&&legacy[i].y==before[i].y,"Legacy recovery incorrect");
    cases++;
    Fixture(17,9,out var ambiguous,out var u);
    for(int i=0;i<ambiguous.Length;i++) ambiguous[i].x=(float)Math.Sin(i*17.23)*300;
    var copy=(Vector3[])ambiguous.Clone();
    Check(!MGTerrainGridRepair.TryRestore(ambiguous,u,17,9,false,ref inferred,out n),"Ambiguous legacy grid accepted");
    for(int i=0;i<copy.Length;i++) Check(ambiguous[i].x==copy[i].x,"Rejected grid mutated");
    cases++;
    Fixture(17,9,out var invalid,out var invalidUv); invalidUv[7].x=.9f;
    Check(!MGTerrainGridRepair.TryRestore(invalid,invalidUv,17,9,true,ref inferred,out n),"Invalid UV layout accepted");cases++;
    Check(!MGTerrainGridRepair.TryRestore(invalid,invalidUv,4,4,true,ref inferred,out n),"Invalid topology accepted");cases++;
    Fixture(17,9,out invalid,out invalidUv);invalid[7].y=float.NaN;
    Check(!MGTerrainGridRepair.TryRestore(invalid,invalidUv,17,9,true,ref inferred,out n),"Nonfinite geometry accepted");cases++;
    Debug.Log(cases+" grid repair cases passed.");
}
}
}
#endif
