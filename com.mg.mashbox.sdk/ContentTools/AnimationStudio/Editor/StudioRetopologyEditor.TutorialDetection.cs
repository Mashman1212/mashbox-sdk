using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private float tutorialQuadSize;
        private int tutorialPatchSegments,tutorialTubeSides,tutorialTubeRows;
        private bool tutorialNavigationPending,tutorialXray,tutorialWire,tutorialSymmetry;
        private Vector3 tutorialCameraPivot;
        private Quaternion tutorialCameraRotation;
        private float tutorialCameraSize;

        private void CaptureTutorialSettings()
        {
            tutorialXray=workingXray;tutorialWire=referenceWire;tutorialSymmetry=Document.symmetry;
            tutorialQuadSize=patchQuadSize;
            tutorialPatchSegments=patchSegments;tutorialTubeSides=tubeSides;tutorialTubeRows=tubeDivisions;
        }
        // Compare against the editor's initial state: loading saved preferences is not an action.
        private void ObserveTutorialSettings()
        {
            if(CurrentLesson==null)return;
            if(tutorialXray!=workingXray&&workingXray)RecordTutorialAction("enableXray");
            if(tutorialWire!=referenceWire&&referenceWire)RecordTutorialAction("enableReferenceWire");
            if(tutorialSymmetry!=Document.symmetry&&Document.symmetry)RecordTutorialAction("enableSymmetry");
            if(activeTool==RetopoTool.Tube&&tubePreview!=null&&(tutorialTubeSides!=tubeSides||tutorialTubeRows!=tubeDivisions))
                RecordTutorialAction("tubeSettings");
            CaptureTutorialSettings();
            if(tutorialNavigationPending&&view)
            {
                if(Quaternion.Angle(tutorialCameraRotation,view.rotation)>.5f ||
                    (tutorialCameraPivot-view.pivot).sqrMagnitude>1e-6f || Mathf.Abs(tutorialCameraSize-view.size)>.001f)
                {tutorialNavigationPending=false;RecordTutorialAction("navigate");}
            }
        }
        private void ObserveTutorialNavigation(Event evt,MashBoxStudioViewport canvas)
        {
            if(CurrentLesson==null||!tutorialFramed)return;
            if(evt.type==EventType.KeyDown&&evt.keyCode==KeyCode.F){tutorialNavigationPending=false;return;}
            if(!canvas.ModelCanvasRect.Contains(evt.mousePosition)||canvas.IsOverViewportControls(evt.mousePosition))return;
            if((evt.type==EventType.MouseDrag&&evt.alt)||evt.type==EventType.ScrollWheel)
            {
                if(!tutorialNavigationPending)
                {
                    tutorialCameraPivot=canvas.pivot;tutorialCameraRotation=canvas.rotation;tutorialCameraSize=canvas.size;
                    tutorialNavigationPending=true;
                }
            }
            else if(evt.type==EventType.MouseDown) tutorialNavigationPending=false;
        }
        private void RecordTutorialGeometry(string label,List<Face> previous)
        {
            if(CurrentLesson==null||Active==null)return;
            // A triangle/quad must reuse an entire old boundary edge; proximity alone is not welding.
            if(label!="Draw projected quad"||Active.faces.Count<=previous.Count)return;
            var counts=new Dictionary<ulong,int>();
            foreach(var face in previous)
                for(int i=0;i<face.vertices.Length;i++)
                {
                    var key=TutorialEdge(face.vertices[i],face.vertices[(i+1)%face.vertices.Length]);
                    counts.TryGetValue(key,out int count);counts[key]=count+1;
                }
            for(int f=previous.Count;f<Active.faces.Count;f++)
            {
                var ids=Active.faces[f].vertices;
                for(int i=0;i<ids.Length;i++)
                    if(counts.TryGetValue(TutorialEdge(ids[i],ids[(i+1)%ids.Length]),out int count)&&count==1)
                    {RecordTutorialAction("joinedFace");return;}
            }
        }
        private static ulong TutorialEdge(int a,int b)=>((ulong)(uint)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
    }
}
