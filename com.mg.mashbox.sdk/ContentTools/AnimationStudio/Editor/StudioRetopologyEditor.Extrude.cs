using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
internal sealed partial class StudioRetopologyEditor {
 private float extrudeDistance=.02f;
 private bool extrudeAutoDistance=true;
 private int extrudeAxis;
 private static readonly Vector3[] extrudeDirections={Vector3.up,Vector3.down,Vector3.right,Vector3.left,Vector3.forward,Vector3.back};
 private void DrawExtrudeSettings(){
  if(selectionMode!=SelectionMode.Edges)return;
  GUILayout.Space(8);GUILayout.Label("EXTRUDE EDGES",StudioTheme.Styles.boldLabel);
  extrudeAxis=EditorGUILayout.Popup("Direction",extrudeAxis,new[]{"Up (+Y)","Down (-Y)","Right (+X)","Left (-X)","Forward (+Z)","Back (-Z)"});
  extrudeAutoDistance=EditorGUILayout.Toggle("Auto row height",extrudeAutoDistance);
  using(new EditorGUI.DisabledScope(extrudeAutoDistance))extrudeDistance=EditorGUILayout.FloatField("Row height",extrudeDistance);
  string issue=StudioRetopologyExtrude.Issue(Active,selectedEdges);
  using(new EditorGUI.DisabledScope(issue!=null))if(GUILayout.Button("Extrude selected edges (Shift+E)"))Run(ExtrudeSelectedEdges);
  GUILayout.Label(issue??"Each extrusion adds a welded row of quads and selects its new border. Repeat Shift+E for more rows; W moves the new border. Uses Surface snapping above.",StudioTheme.Styles.wordWrappedMiniLabel);
 }
 private void ExtrudeSelectedEdges(){
  if(Active==null||selectionMode!=SelectionMode.Edges)return;
  string issue=StudioRetopologyExtrude.Issue(Active,selectedEdges);if(issue!=null){status=issue;return;}
  var edges=new HashSet<(int,int)>(selectedEdges);
  float distance=extrudeAutoDistance?edges.Average(e=>(Active.vertices[e.Item1]-Active.vertices[e.Item2]).magnitude):extrudeDistance;
  HashSet<(int,int)> next=null;
  ChangeCore("Extrude border edges",()=>next=StudioRetopologyExtrude.Apply(Active,edges,extrudeDirections[extrudeAxis]*distance,Document.project?Surface:null,Document.offset),true);
  selectedEdges.UnionWith(next);SyncComponentVertices();activeTool=RetopoTool.Select;transformTool=Tool.Move;rotation=Quaternion.identity;scale=Vector3.one;StoreToolState();
  status=$"Extruded {next.Count} quads. Shift+E adds another row; W moves the selected border.";repaint();
 }
}
}
