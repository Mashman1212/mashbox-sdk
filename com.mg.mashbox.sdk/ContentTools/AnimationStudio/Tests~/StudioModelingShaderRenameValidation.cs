using System;using System.IO;using UnityEditor;using UnityEngine;
public static class StudioModelingShaderRenameValidation
{
 public static void Run(){EditorApplication.delayCall+=()=>{
  int count=0;string result;
  try{
   string path=AssetDatabase.GUIDToAssetPath("409bba92b9fbb904cbfd7cf58b9531a6");
   if(!path.EndsWith("MashBoxStudio_Default_Modeling_Shader.shadergraph"))throw new Exception("GUID did not resolve to renamed graph: "+path);count++;
   var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Renamed graph did not import/compile");count++;
   if(shader.name!="MGShaders/HDRP/Lit/MashBoxStudio_Default_Modeling_Shader")throw new Exception("Generated shader name incorrect: "+shader.name);count++;
   foreach(string guid in new[]{"410de6c3c565a5b459045246f998f390","d2e73f08095e90e44bababe99ee53c76"}){
    var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));var expected=guid=="410de6c3c565a5b459045246f998f390"?shader:AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath("f495d7739232e7b4b96ac1ee1dbbceb2"));if(!material||material.shader!=expected)throw new Exception("Material lost its shader reference: "+guid);count++;
   }
   result="PASS "+count+" renamed shader GUID/path/name/import and Default/Checkerboard material binding checks";
  }catch(Exception error){result=error.ToString();}
  File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../modeling-shader-rename-result.txt")),result);
  EditorApplication.delayCall+=()=>EditorApplication.Exit(result.StartsWith("PASS")?0:1);
 };}
}
