using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        // Retain only the last construction, never rebuild across subsequent mesh edits.
        private sealed class PatchGeometry
        {
            internal readonly List<Vector3> vertices;
            internal readonly List<Vector2> uv;
            internal readonly List<Face> faces;
            internal PatchGeometry(Part part)
            {vertices=new List<Vector3>(part.vertices);uv=new List<Vector2>(part.uv);faces=part.faces.Select(f=>new Face((int[])f.vertices.Clone(),f.material)).ToList();}
            internal void Apply(Part part)
            {part.vertices=new List<Vector3>(vertices);part.uv=new List<Vector2>(uv);part.faces=faces.Select(f=>new Face((int[])f.vertices.Clone(),f.material)).ToList();}
            internal bool Matches(Part part)=>vertices.SequenceEqual(part.vertices)&&uv.SequenceEqual(part.uv)&&faces.Count==part.faces.Count&&
                faces.Select((f,i)=>f.material==part.faces[i].material&&f.vertices.SequenceEqual(part.faces[i].vertices)).All(v=>v);
        }
        private sealed class DensityPatch
        {
            internal Part owner;
            internal PatchGeometry before,after;
            internal Vector3[] points;
            internal Vector3 normal;
            internal int[] seam,mirrorSeam;
            internal bool reverse,mirror,project;
            internal float offset;
        }
        private DensityPatch lastDensityPatch;
        private int densityUndoGroup=-1;
        private void FinishDensityUndo()
        {
            if(densityUndoGroup>=0&&(Event.current.rawType==EventType.MouseUp||GUIUtility.hotControl==0))
            {Undo.CollapseUndoOperations(densityUndoGroup);densityUndoGroup=-1;}
        }
        private void UpdateLastPatchDensity()
        {
            var patch=lastDensityPatch;
            if(patch==null||patch.owner!=Active||!patch.after.Matches(Active))
            {lastDensityPatch=null;status="Density applies to the next patch. Draw a patch, then adjust its density here.";return;}
            if(corners.Count>0&&!(continuousPatch&&patchOwner==Active&&corners.Count==2))
            {status="Density applies to the patch you are drawing. Finish it to adjust its grid live.";return;}
            int columns=patch.seam!=null?patch.seam.Length-1:patchSegments,rows=patchSegments;
            if(patchAutoDensity){var grid=PatchGridSize(patch.points,PatchQuadSize,patch.seam);columns=grid.x;rows=grid.y;}
            int[] end=null,mirrorEnd=null;PatchGeometry rebuilt;
            // Generate against the original borders, before replacing the visible mesh.
            // Restore current geometry even if projection or border validation fails.
            var part=Active;var currentVertices=part.vertices;var currentUV=part.uv;var currentFaces=part.faces;
            bool project=Document.project;float offset=Document.offset;var normal=cornerNormal;
            try
            {
                patch.before.Apply(part);Document.project=patch.project;Document.offset=patch.offset;cornerNormal=patch.normal;
                end=AddPatch(patch.points,columns,rows,patch.reverse,patch.seam);
                if(patch.mirror)mirrorEnd=AddPatch(patch.points.Select(p=>new Vector3(-p.x,p.y,p.z)).ToArray(),columns,rows,!patch.reverse,patch.mirrorSeam);
                rebuilt=new PatchGeometry(part);
            }
            finally
            {part.vertices=currentVertices;part.uv=currentUV;part.faces=currentFaces;Document.project=project;Document.offset=offset;cornerNormal=normal;}
            if(rebuilt.Matches(part))
            {status="Grid unchanged at this density. Welded borders retain their counts; try a larger adjustment.";return;}
            if(Event.current!=null&&GUIUtility.hotControl!=0&&densityUndoGroup<0)densityUndoGroup=Undo.GetCurrentGroup()+1;
            Change("Adjust patch density",()=>rebuilt.Apply(part));
            patch.after=rebuilt;lastDensityPatch=patch;
            if(continuousPatch)
            {
                patchContinuation=end;mirroredPatchContinuation=mirrorEnd;patchOwner=part;
                corners.Add(part.vertices[end[0]]);corners.Add(part.vertices[end[end.Length-1]]);
            }
            RecordTutorialAction("patchDensityApplied");
            status="Updated last patch density. Welded borders preserved. Undo restores the previous grid.";
            repaint();
        }
    }
}
