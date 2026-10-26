using System;using System.Collections.Generic;using UnityEditor;using UnityEngine;
namespace MashBoxSDK.AnimationStudio {
 internal sealed partial class StudioModelEditor {
  private sealed class LoopSelectionState {
   internal Mesh mesh;internal int version;internal bool weld;internal List<StudioMeshCuts.Face> faces;internal List<(int a,int b)> edges;internal StudioSelectionTopology topology;internal ComponentMode mode;
   internal int lastVertex=-1,previousVertex=-1,lastEdge=-1,previousEdge=-1,lastFace=-1,previousFace=-1;
   internal void Reset(){lastVertex=previousVertex=lastEdge=previousEdge=lastFace=previousFace=-1;}
  }
  private readonly Dictionary<StudioModelDocument.Node,LoopSelectionState> loopSelections=new Dictionary<StudioModelDocument.Node,LoopSelectionState>();
  private int selectionClickCount=1;
  private LoopSelectionState LoopState(){var node=Document.nodes[Active];var faces=Faces();var edges=Edges();int version=EditorUtility.GetDirtyCount(ActiveMesh);if(!loopSelections.TryGetValue(node,out var state)||state.mesh!=ActiveMesh||state.version!=version||state.weld!=weld||!ReferenceEquals(state.faces,faces)||!ReferenceEquals(state.edges,edges)){state=new LoopSelectionState{mesh=ActiveMesh,version=version,weld=weld,faces=faces,edges=edges,mode=mode};loopSelections[node]=state;}if(state.mode!=mode){state.mode=mode;state.Reset();}return state;}
  private static int RememberLoopComponent(int current,ref int last,ref int previous){if(current!=last){previous=last;last=current;}return previous;}
  private HashSet<int> ComponentClickSelection(int current,bool expand){var state=LoopState();int previous=-1;if(mode==ComponentMode.Vertex)previous=RememberLoopComponent(current,ref state.lastVertex,ref state.previousVertex);else if(mode==ComponentMode.Edge)previous=RememberLoopComponent(current,ref state.lastEdge,ref state.previousEdge);else{if(!expand&&current==state.lastFace)state.previousFace=-1;previous=RememberLoopComponent(current,ref state.lastFace,ref state.previousFace);}if(!expand)return new HashSet<int>{current};
   if(state.topology==null)state.topology=new StudioSelectionTopology(ActiveMesh.vertices,state.faces,state.edges,weld);
   if(mode==ComponentMode.Edge)return state.topology.SelectEdges(current,previous);
   if(mode==ComponentMode.Vertex)return state.topology.SelectVertices(previous,current);
   return state.topology.SelectFaces(previous,current);
  }
  private bool BeginComponentLoopSelection(SceneView view,int id){var evt=Event.current;if(evt.type!=EventType.MouseDown||evt.button!=0||evt.clickCount<2||evt.alt||pivot||pivotSnapHeld||mode==ComponentMode.Object||GUIUtility.hotControl!=0||SelectionPaintInputHeld(view))return false;
   var canvas=view is MashBoxStudioViewport studio?studio.ModelCanvasRect:new Rect(Vector2.zero,view.position.size);if(!canvas.Contains(evt.mousePosition)||view is MashBoxStudioViewport overlay&&overlay.IsOverViewportControls(evt.mousePosition))return false;
   int node=MeshAtModePointer(view,evt.mousePosition);if(node<0)return false;bool hit;using(new ComponentTargetScope(this,node)){hit=CanEditComponents()&&FindComponentAt(view,evt.mousePosition)>=0;}if(!hit)return false;
   start=end=evt.mousePosition;additive=evt.shift;subtract=evt.control||evt.command;selectionClickCount=evt.clickCount;marquee=true;marqueeControl=id;GUIUtility.hotControl=id;evt.Use();return true;
  }
 }
}
