using UnityEditor;
using UnityEngine;
using MashBoxSDK.Map.Rendering.Instancer;
[CustomEditor(typeof(InstancingManager))]
sealed class BackgroundInstancingInspector : Editor
{
 public override void OnInspectorGUI()
 {
  DrawDefaultInspector();
  var manager=(InstancingManager)target;
  if(!Application.isPlaying){EditorGUILayout.HelpBox("Instance Groups use this renderer independently of MG Terrain. Cells cache static trees; each rendering camera culls them separately. Bounds Padding covers extra shader movement. Maximum Distance = 0 keeps the full camera range.",MessageType.Info);return;}
  EditorGUILayout.HelpBox($"Cells: {manager.TotalCells} | Batches: {manager.TotalBatches}\nLast camera: {(manager.LastCamera!=null?manager.LastCamera.name:"none")}\nSubmitted: {manager.LastSubmittedBatches} batches / {manager.LastSubmittedInstances:N0} instances\nCulled: {manager.LastCulledBatches} batches",MessageType.Info);
  if(GUILayout.Button("Rebuild Instance Cells"))manager.MarkDirty();
 }
 public override bool RequiresConstantRepaint()=>Application.isPlaying;
}
