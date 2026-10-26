using System;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private sealed class DensityBridge
        {
            internal Part owner;
            internal PatchGeometry before,after;
            internal Action build;
        }
        private DensityBridge lastDensityBridge;
        private void DrawBridgeBands()
        {
            if(selectionMode==SelectionMode.Faces||Active==null)return;
            if(lastDensityBridge!=null&&(lastDensityBridge.owner!=Active||!lastDensityBridge.after.Matches(Active)))
                lastDensityBridge=null;
            if(lastDensityBridge==null)
            {
                bool bridgeReady=selectionMode==SelectionMode.Edges
                    ? selectedEdges.Count>=2&&StudioRetopologyBridgeEdges.Validate(Active,selectedEdges,out _,out _)==null
                    : selected.Count>=4&&StudioRetopologyLoops.BridgeIssue(Active,selected)==null;
                if(!bridgeReady)return;
            }
            GUILayout.Space(4);
            using(new GUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent("Bridge Bands","Rows of faces across the gap. 1 creates a single band. Also updates the last bridge until another edit."),StudioTheme.Styles.miniLabel,GUILayout.Width(84),GUILayout.Height(24));
                var rect=GUILayoutUtility.GetRect(174,24,GUILayout.Width(174),GUILayout.Height(24));rect.y+=4;rect.height=18;
                EditorGUI.BeginChangeCheck();bridgeBands=EditorGUI.IntSlider(rect,bridgeBands,1,24);
                if(EditorGUI.EndChangeCheck()){Run(UpdateLastBridgeBands);repaint();}
                GUILayout.Space(10);
                GUILayout.Label(lastDensityBridge!=null?"Last bridge":"Next bridge",StudioTheme.Styles.miniLabel,GUILayout.Height(24));
                GUILayout.FlexibleSpace();
            }
        }
        private void UpdateLastBridgeBands()
        {
            var bridge=lastDensityBridge;
            if(bridge==null)return;
            if(bridge.owner!=Active||!bridge.after.Matches(Active)){lastDensityBridge=null;return;}
            if(Event.current!=null&&GUIUtility.hotControl!=0&&densityUndoGroup<0)densityUndoGroup=Undo.GetCurrentGroup()+1;
            Change("Adjust bridge bands",()=>{bridge.before.Apply(Active);bridge.build();});
            bridge.after=new PatchGeometry(Active);lastDensityBridge=bridge;
            status="Bridge updated: "+bridgeBands+" bands. Original borders preserved.";
        }
    }
}
