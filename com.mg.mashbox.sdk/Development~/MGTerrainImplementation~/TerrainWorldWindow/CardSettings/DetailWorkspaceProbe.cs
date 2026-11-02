using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class DetailWorkspaceProbe
{
    public static void Run()
    {
        try
        {
            const string path = "Assets/CardScope.shader";
            File.WriteAllText(path, "Shader \"Hidden/CardScope\" { Properties { _GrassUseLayerParameters(\"Enable\",Float)=0 _GrassWidthMultiplier(\"Shared\",Float)=1 _GrassLayer00_GrassWidthMultiplier(\"Width / Layer 0\",Float)=1 _GrassLayer00_GrassHeightMultiplier(\"Height / Layer 0\",Float)=2 _GrassLayer00_Seed(\"Seed\",Float)=1 [HideInInspector] _GrassLayer00_Hidden(\"Hidden\",Float)=1 _GrassLayer01_GrassWidthMultiplier(\"Width / Layer 1\",Float)=3 } SubShader { Pass {} } }");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(path));
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("MashBoxSDK.MapTools.MGTerrainEditor")).First(t => t != null);
            var method = type.GetMethod("DetailSubIdMaterialProperties", BindingFlags.NonPublic | BindingFlags.Static);
            var properties = (MaterialProperty[])method.Invoke(null, new object[] { material, 0 });
            if (properties.Length != 2 || properties.Any(p => !p.name.StartsWith("_GrassLayer00_"))) throw new Exception("Sub-ID scope leaked or included hidden/seed properties");
            if (material.GetFloat("_GrassUseLayerParameters") != 0) throw new Exception("Lookup enabled overrides");
            properties.Single(p => p.name.Contains("Width")).floatValue = 4;
            if (material.GetFloat("_GrassLayer00_GrassWidthMultiplier") != 4 || material.GetFloat("_GrassLayer01_GrassWidthMultiplier") != 3 || material.GetFloat("_GrassWidthMultiplier") != 1) throw new Exception("Edit changed another ID or shared setting");
            if (((MaterialProperty[])method.Invoke(null, new object[] { material, 8 })).Length != 0) throw new Exception("Invalid ID accepted");
            UnityEngine.Object.DestroyImmediate(material);
            File.WriteAllText("../validation-result.txt", "PASS: exact Sub-ID property filtering, hidden/seed exclusion, independent editing, unchanged override toggle, invalid ID guard.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("../validation-result.txt", "FAIL: " + e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
