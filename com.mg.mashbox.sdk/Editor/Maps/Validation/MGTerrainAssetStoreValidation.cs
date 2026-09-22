using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainAssetStoreValidation
    {
        [MenuItem("Tools/MashBox/MG Terrain/Validate Bake Asset Persistence")]
        internal static void Run()
        {
            string folder="Assets/MGAssetValidation_"+Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
            var a=new Texture2D(4,4,TextureFormat.RGBA32,false,true);
            var b=new Texture2D(8,8,TextureFormat.RGBA32,false,true);
            var mat=new Material(Shader.Find("HDRP/Lit"));
            var report="";
            try
            {
                Fill(a,Color.red); Fill(b,Color.blue);
                string native=folder+"/Map.asset", png=folder+"/Map.png", material=folder+"/Material.mat";
                using(var tx=new MGTerrainAssetTransaction()) { MGTerrainAssetStore.Save(a,native,tx); MGTerrainAssetStore.SaveMap(a,png,true,tx); MGTerrainAssetStore.Save(mat,material,tx); tx.Commit(); }
                var id=AssetDatabase.AssetPathToGUID(native); var pngId=AssetDatabase.AssetPathToGUID(png); var matId=AssetDatabase.AssetPathToGUID(material);
                using(var tx=new MGTerrainAssetTransaction()) { var saved=MGTerrainAssetStore.Save(b,native,tx); Check(saved==a && saved.width==8,"Native reference and resized content"); MGTerrainAssetStore.SaveMap(b,png,true,tx); tx.Commit(); }
                Check(AssetDatabase.AssetPathToGUID(native)==id && AssetDatabase.AssetPathToGUID(png)==pngId,"GUIDs preserved");
                byte[] before=File.ReadAllBytes(png); var beforeNative=File.ReadAllBytes(native);
                Fill(b,Color.green);
                using(var tx=new MGTerrainAssetTransaction()) { MGTerrainAssetStore.Save(b,native,tx); MGTerrainAssetStore.SaveMap(b,png,true,tx); MGTerrainAssetStore.SaveMap(b,folder+"/Cancelled.png",true,tx); }
                Check(Convert.ToBase64String(before)==Convert.ToBase64String(File.ReadAllBytes(png)),"PNG rollback");
                Check(Convert.ToBase64String(beforeNative)==Convert.ToBase64String(File.ReadAllBytes(native)),"Native rollback");
                Check(!File.Exists(folder+"/Cancelled.png"),"Cancelled new output removed");
                Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();
                using(var tx=new MGTerrainAssetTransaction()) { MGTerrainAssetStore.SaveMap(b,png,true,tx,true); tx.Commit(); }
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.PerformUndo();
                Check(Convert.ToBase64String(before)==Convert.ToBase64String(File.ReadAllBytes(png)),"PNG stamp Undo restores disk bytes");
                Undo.PerformRedo(); Check(Convert.ToBase64String(before)!=Convert.ToBase64String(File.ReadAllBytes(png)),"PNG stamp Redo");
                Check(AssetDatabase.AssetPathToGUID(material)==matId,"Material reference preserved");
                var array = new Texture2DArray(2,2,2,TextureFormat.RGBA32,false,true);
                string arrayPath=folder+"/Array.asset";
                using(var tx=new MGTerrainAssetTransaction()) { MGTerrainAssetStore.Save(array,arrayPath,tx);tx.Commit(); }
                string arrayId=AssetDatabase.AssetPathToGUID(arrayPath);
                var resized = new Texture2DArray(4,4,2,TextureFormat.RGBA32,false,true);
                try {
                    using(var tx=new MGTerrainAssetTransaction()) { var saved=MGTerrainAssetStore.Save(resized,arrayPath,tx);Check(saved==array && saved.width==4,"Array resized in place");tx.Commit(); }
                    Check(AssetDatabase.AssetPathToGUID(arrayPath)==arrayId,"Array GUID preserved");
                } finally { UnityEngine.Object.DestroyImmediate(resized); }
                report="PASS: repeat save, resized native texture and array, PNG/native GUID stability, rollback restores old files, cancelled new output removed, PNG stamp Undo/Redo.";
                Debug.Log(report);
            }
            catch(Exception ex) { report=ex.ToString(); throw; }
            finally
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-asset-validation.txt"),report);
                if(!EditorUtility.IsPersistent(b)) UnityEngine.Object.DestroyImmediate(b);
                AssetDatabase.DeleteAsset(folder);
            }
        }
        static void Fill(Texture2D t,Color c) { var p=new Color[t.width*t.height]; for(int i=0;i<p.Length;i++)p[i]=c; t.SetPixels(p);t.Apply(); }
        static void Check(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    }
}
