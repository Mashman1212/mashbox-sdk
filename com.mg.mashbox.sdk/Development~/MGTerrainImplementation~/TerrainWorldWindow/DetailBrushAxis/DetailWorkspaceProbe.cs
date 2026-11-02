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
        bool hadRadius = EditorPrefs.HasKey("MashBoxSDK.EditorTools.BrushRadius"), hadStrength = EditorPrefs.HasKey("MashBoxSDK.EditorTools.BrushStrength");
        float savedRadius = EditorPrefs.GetFloat("MashBoxSDK.EditorTools.BrushRadius"), savedStrength = EditorPrefs.GetFloat("MashBoxSDK.EditorTools.BrushStrength");
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var editorType = assemblies.Select(a => a.GetType("MashBoxSDK.MapTools.MGTerrainEditor")).First(t => t != null);
            var stateType = assemblies.SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } }).First(t => t.Name == "MBEditorToolState");
            var radius = stateType.GetProperty("BrushRadius", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var strength = stateType.GetProperty("BrushStrength", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var editor = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(editorType);
            var axis = editorType.GetField("m_DetailAdjustmentAxis", BindingFlags.Instance | BindingFlags.NonPublic);
            var travel = editorType.GetField("m_DetailAdjustmentTravel", BindingFlags.Instance | BindingFlags.NonPublic);
            var apply = editorType.GetMethod("ApplyDetailBrushAdjustmentDelta", BindingFlags.Instance | BindingFlags.NonPublic);
            Action reset = () => { axis.SetValue(editor, Enum.ToObject(axis.FieldType, 0)); travel.SetValue(editor, Vector2.zero); radius.SetValue(null, 10f); strength.SetValue(null, .5f); };
            Func<PropertyInfo,float> value = p => (float)p.GetValue(null);
            reset(); apply.Invoke(editor, new object[] { new Vector2(1, 1) });
            if(value(radius)!=10 || value(strength)!=.5f) throw new Exception("Jitter changed brush");
            apply.Invoke(editor,new object[]{new Vector2(8,1)});
            if(value(radius)<=10 || value(strength)!=.5f) throw new Exception("Horizontal changed strength");
            float r=value(radius); apply.Invoke(editor,new object[]{new Vector2(0,30)});
            if(value(radius)!=r || value(strength)!=.5f) throw new Exception("Radius axis did not remain locked");
            reset(); apply.Invoke(editor,new object[]{new Vector2(1,-8)});
            if(value(radius)!=10 || value(strength)<=.5f) throw new Exception("Vertical changed radius");
            float s=value(strength); apply.Invoke(editor,new object[]{new Vector2(30,0)});
            if(value(radius)!=10 || value(strength)!=s) throw new Exception("Strength axis did not remain locked");

            File.WriteAllText("../validation-result.txt","PASS: initial jitter ignored, horizontal changes radius only, vertical changes strength only, both axes remain locked during cross-axis movement.");
            if(hadRadius) EditorPrefs.SetFloat("MashBoxSDK.EditorTools.BrushRadius",savedRadius); else EditorPrefs.DeleteKey("MashBoxSDK.EditorTools.BrushRadius");
            if(hadStrength) EditorPrefs.SetFloat("MashBoxSDK.EditorTools.BrushStrength",savedStrength); else EditorPrefs.DeleteKey("MashBoxSDK.EditorTools.BrushStrength");
            EditorApplication.Exit(0);
        }
        catch(Exception e) { File.WriteAllText("../validation-result.txt","FAIL: "+e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
