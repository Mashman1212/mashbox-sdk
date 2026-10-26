using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private int tubeSides=12,tubeDivisions=8;
        private StudioRetopologyDocument.Part tubePreview;
        private void StartTube()
        {activeTool=RetopoTool.Tube;ClearPatchCorners();tubePreview=null;repaint();}
        private void UpdateTubePreview()
        {
            tubePreview=null;
            if(corners.Count==2)tubePreview=StudioRetopologyTube.Generate(Active.reference,corners[0],corners[1],tubeSides,tubeDivisions,Document.offset);
            repaint();
        }
        private void DrawTubeSettings()
        {
            if(activeTool!=RetopoTool.Tube)return;
            GUILayout.Space(6);GUILayout.Label("TUBE",StudioTheme.Styles.boldLabel);
            GUILayout.Label(corners.Count<2?"Click start and end along the same arm or leg. Keep points inside openings; use shorter sections for bends.":"Preview only. Enter creates the tube; Escape cancels. Open ends can be bridged to matching borders.",StudioTheme.Styles.wordWrappedMiniLabel);
            using(new EditorGUI.DisabledScope(tubePreview==null||corners.Count!=2))
                if(GUILayout.Button("Create tube  [Enter]"))Run(CreateTube);
        }
        private void CreateTube()
        {
            if(corners.Count!=2)return;
            // Generate before changing the document so invalid sections leave existing work intact.
            var tube=StudioRetopologyTube.Generate(Active.reference,corners[0],corners[1],tubeSides,tubeDivisions,Document.offset);
            Change("Create quad tube",()=>
            {
                void Append(bool mirrored)
                {
                    int start=Active.vertices.Count;
                    Active.vertices.AddRange(tube.vertices.Select(p=>mirrored?new Vector3(-p.x,p.y,p.z):p));Active.uv.AddRange(tube.uv);
                    foreach(var face in tube.faces)
                    {
                        var ids=face.vertices.Select(i=>i+start).ToArray();if(mirrored)System.Array.Reverse(ids);
                        Active.faces.Add(new StudioRetopologyDocument.Face(ids));
                    }
                }
                Append(false);if(Document.symmetry)Append(true);Active.needsUV=true;
            });
            tubePreview=null;status="Created quad tube. Use Edges and Bridge selected borders to connect its open ends.";
        }
        private void DrawTubePreview(Vector2 mouse,bool drawPointer)
        {
            var color=Handles.color;var depth=Handles.zTest;
            try
            {
                Handles.zTest=UnityEngine.Rendering.CompareFunction.LessEqual;Handles.color=new Color(1,.8f,.2f,.95f);
                if(corners.Count==2&&tubePreview!=null)
                    foreach(var edge in StudioRetopologyOperations.Edges(tubePreview).Keys)
                        Handles.DrawAAPolyLine(2,tubePreview.vertices[edge.Item1],tubePreview.vertices[edge.Item2]);
                Handles.zTest=UnityEngine.Rendering.CompareFunction.Always;
                foreach(var p in corners)Handles.DotHandleCap(0,p,Quaternion.identity,HandleUtility.GetHandleSize(p)*.035f,EventType.Repaint);
                if(drawPointer&&corners.Count==1&&ReferenceHit(mouse,out var hit))Handles.DrawDottedLine(corners[0],hit.point,4);
            }
            finally {Handles.color=color;Handles.zTest=depth;}
        }
    }
}
