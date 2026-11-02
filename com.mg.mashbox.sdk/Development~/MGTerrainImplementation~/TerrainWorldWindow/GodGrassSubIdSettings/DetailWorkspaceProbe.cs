using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
public static class DetailWorkspaceProbe
{
    public static void Run()
    {
        try
        {
            var shader = new StringBuilder("Shader \"Hidden/GodGrassPopupProbe\" { Properties { _GrassUseLayerParameters(\"Enable\",Float)=0 _GrassLayerParametersVersion(\"Version\",Float)=0 _GrassSharedShadowMode(\"Shadow\",Float)=0 _UseShadowThreshold(\"Shadow\",Float)=0 _GrassWidthMultiplier(\"Width\",Float)=1.5 _GrassHeightMultiplier(\"Height\",Float)=2.5 ");
            for(int id=0;id<8;id++)
                shader.Append($"[HideInInspector] _GrassLayer{id:00}_GrassWidthMultiplier(\"Width Multiplier\",Float)=1 [HideInInspector] _GrassLayer{id:00}_GrassHeightMultiplier(\"Height Multiplier\",Float)=1 [HideInInspector] _GrassLayer{id:00}_WindStrength(\"Wind\",Float)=1 ");
            shader.Append("} SubShader { Pass {} } }");
            File.WriteAllText("Assets/Popup.shader",shader.ToString());
            AssetDatabase.ImportAsset("Assets/Popup.shader",ImportAssetOptions.ForceSynchronousImport);
            var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Popup.shader"));
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MashBoxSDK.MapTools.MGTerrainEditor")).First(t=>t!=null);
            var query=type.GetMethod("DetailSubIdMaterialProperties",BindingFlags.NonPublic|BindingFlags.Static);
            var toggle=type.GetMethod("SetDetailSubIdOverrides",BindingFlags.NonPublic|BindingFlags.Static);
            for(int id=0;id<8;id++)
            {
                var properties=(MaterialProperty[])query.Invoke(null,new object[]{material,id});
                if(properties.Length!=3 || !properties.Any(p=>p.name.EndsWith("GrassWidthMultiplier")) || !properties.Any(p=>p.name.EndsWith("GrassHeightMultiplier"))) throw new Exception("Hidden size controls missing for ID "+id);
            }
            if(material.GetFloat("_GrassUseLayerParameters")!=0 || material.GetFloat("_GrassLayerParametersVersion")!=0)throw new Exception("Reading settings modified the material");
            toggle.Invoke(null,new object[]{material,true});
            for(int id=0;id<8;id++)
                if(material.GetFloat($"_GrassLayer{id:00}_GrassWidthMultiplier")!=1.5f || material.GetFloat($"_GrassLayer{id:00}_GrassHeightMultiplier")!=2.5f)throw new Exception("First activation lost shared size values");
            var selected=(MaterialProperty[])query.Invoke(null,new object[]{material,3});
            selected.Single(p=>p.name.EndsWith("GrassWidthMultiplier")).floatValue=3;
            selected.Single(p=>p.name.EndsWith("GrassHeightMultiplier")).floatValue=4;
            toggle.Invoke(null,new object[]{material,false});toggle.Invoke(null,new object[]{material,true});
            if(material.GetFloat("_GrassLayer03_GrassWidthMultiplier")!=3 || material.GetFloat("_GrassLayer03_GrassHeightMultiplier")!=4 || material.GetFloat("_GrassLayer04_GrassWidthMultiplier")!=1.5f)throw new Exception("Existing settings overwritten or another ID edited");
            toggle.Invoke(null,new object[]{material,false});
            if(material.GetFloat("_UseShadowThreshold")!=0)throw new Exception("Shared shadow mode not restored");
            UnityEngine.Object.DestroyImmediate(material);
            File.WriteAllText("../validation-result.txt","PASS: hidden controls exposed for all 8 Sub-IDs; width/height editing isolated; opening preserves material; first activation copies shared defaults; reactivation preserves edits; shadow mode restored.");
            EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText("../validation-result.txt","FAIL: "+e);Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
