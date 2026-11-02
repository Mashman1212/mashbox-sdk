using System;
using System.IO;
using System.Reflection;
using MashBoxSDK.MapTools;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
public static class DetailWorkspaceProbe
{
    public static void Run()
    {
        const string report="../validation-result.txt";
        try
        {
            File.WriteAllText(report,"Transparent prototype preview regression\n");
            var go=new GameObject("Tile");go.SetActive(false);var tile=go.AddComponent<MGTerrain>();
            var prefab=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var inflated=UnityEngine.Object.Instantiate(prefab.GetComponent<MeshFilter>().sharedMesh); inflated.bounds=new Bounds(Vector3.zero,Vector3.one*8); prefab.GetComponent<MeshFilter>().sharedMesh=inflated;
            var material=new Material(Shader.Find("Unlit/Color"));material.color=new Color(.3f,.8f,.35f);
            prefab.GetComponent<MeshRenderer>().sharedMaterial=material;
            using(var data=new SerializedObject(tile)){var prototypes=data.FindProperty("m_Prototypes");prototypes.arraySize=1;prototypes.GetArrayElementAtIndex(0).FindPropertyRelative("m_Prefab").objectReferenceValue=prefab;data.ApplyModifiedPropertiesWithoutUndo();}
            var editor=UnityEditor.Editor.CreateEditor(tile,typeof(MGTerrainEditor));
            var method=typeof(MGTerrainEditor).GetMethod("GetDetailPrototypeThumbnail",BindingFlags.Instance|BindingFlags.NonPublic);
            var texture=(Texture)method.Invoke(editor,new object[]{tile,0,prefab});
            if(texture==null)throw new Exception("Preview is missing");
            var target=RenderTexture.GetTemporary(128,128,0,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;Graphics.Blit(texture,target);RenderTexture.active=target;
            var copy=new Texture2D(128,128,TextureFormat.RGBA32,false);copy.ReadPixels(new Rect(0,0,128,128),0,0);copy.Apply();
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
            var pixels=copy.GetPixels32();int opaque=0,transparent=0;
            foreach(var pixel in pixels){if(pixel.a>128)opaque++;if(pixel.a<8)transparent++;}
            if(opaque<100||transparent<1000||pixels[0].a>8)throw new Exception($"Expected visible object and transparent background; opaque={opaque}, transparent={transparent}, corner={pixels[0].a}");
            File.AppendAllText(report,"PASS: visible object with transparent corners/background.\n");
            if(opaque<7000)throw new Exception("Visible detail still too small after framing: "+opaque);
            if(!ReferenceEquals(texture,method.Invoke(editor,new object[]{tile,0,prefab})))throw new Exception("Preview was not cached");
            File.AppendAllText(report,"PASS: repeated draws reuse the cached texture.\n");
            File.WriteAllBytes(report.Replace("result.txt","preview.png"),copy.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(editor);
            if(texture!=null)throw new Exception("Preview texture survived editor disposal");
            File.AppendAllText(report,"PASS: editor disposal releases generated previews.\nALL PASSED\n");
            EditorApplication.Exit(0);
        }
        catch(Exception error){File.AppendAllText(report,"FAIL: "+error+"\n");Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
