#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.Maps.TerrainSystem;
[InitializeOnLoad]
static class MGDetailBudgetRepair
{
 static string Folder=>Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MGTerrainWorld).Assembly).resolvedPath,"Development~/MGTerrainImplementation~/GrassSubIdSize/");
 static MGDetailBudgetRepair(){EditorApplication.delayCall+=Apply;}
 static void Apply(){if(!File.Exists(Folder+"apply-budget.request"))return;File.Delete(Folder+"apply-budget.request");int changed=0;foreach(var world in Object.FindObjectsByType<MGTerrainWorld>(FindObjectsSortMode.None)){if(!world.gameObject.scene.IsValid()||!world.gameObject.scene.isLoaded)continue;var so=new SerializedObject(world);var budget=so.FindProperty("m_VisibleDetailBudget");if(budget==null||budget.intValue>=200000)continue;budget.intValue=200000;so.ApplyModifiedProperties();changed++;}File.WriteAllText(Folder+"budget-repair.txt",$"Applied tested 200000 detail budget to {changed} loaded terrain world(s) using SerializedObject with Undo. Scene not saved.\n");SceneView.RepaintAll();}
}
#endif
