using System.Collections.Generic;
using System.Linq;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private int lastLoopVertex=-1,previousLoopVertex=-1,lastLoopFace=-1,previousLoopFace=-1;

        private void ResetLoopDirection()
        {lastLoopVertex=previousLoopVertex=lastLoopFace=previousLoopFace=-1;}

        // The first click of a double-click already selects its target. Retain the
        // previous DISTINCT component so the second click doesn't erase direction.
        private static int RememberLoopDirection(int current,ref int last,ref int previous)
        {
            if(current!=last){previous=last;last=current;}
            return previous;
        }

        private HashSet<int> ClickVertexLoop(int vertex,bool expand)
        {
            int previous=RememberLoopDirection(vertex,ref lastLoopVertex,ref previousLoopVertex);
            var hits=expand?DirectionalVertexLoop(Active,previous,vertex):new HashSet<int>{vertex};
            if(expand&&!selectionSubtract&&hits.Count>1)RecordTutorialAction("selectVertexLoop");
            return hits;
        }

        private HashSet<int> ClickFaceLoop(int face,bool expand)
        {
            // A fresh click on the same face starts a hull gesture. The second
            // click preserves the anchor captured by the first click.
            if(!expand && face==lastLoopFace)previousLoopFace=-1;
            int previous=RememberLoopDirection(face,ref lastLoopFace,ref previousLoopFace);
            if(!expand)return new HashSet<int>{face};
            var hits=DirectionalFaceLoop(Active,previous,face);
            bool loop=hits.Count>1;
            if(!loop)hits=ConnectedFaceHull(Active,face);
            if(!selectionSubtract&&hits.Count>1)RecordTutorialAction(loop?"selectFaceLoop":"selectHull");
            return hits;
        }

        private static HashSet<int> ConnectedFaceHull(Part part,int seed)
        {
            var result=new HashSet<int>();
            if(part==null||seed<0||seed>=part.faces.Count)return result;
            var edges=StudioRetopologyOperations.Edges(part);
            var pending=new Queue<int>();result.Add(seed);pending.Enqueue(seed);
            while(pending.Count>0)
            {
                var ids=part.faces[pending.Dequeue()].vertices;
                for(int i=0;i<ids.Length;i++)
                {
                    if(!edges.TryGetValue(StudioRetopologyOperations.Edge(ids[i],ids[(i+1)%ids.Length]),out var adjacent))continue;
                    foreach(int next in adjacent)if(result.Add(next))pending.Enqueue(next);
                }
            }
            return result;
        }

        private static HashSet<int> DirectionalVertexLoop(Part part,int previous,int current)
        {
            var result=new HashSet<int>();
            if(part==null||current<0||current>=part.vertices.Count)return result;
            result.Add(current);
            if(previous<0||previous==current||previous>=part.vertices.Count)return result;
            // Only a real connecting edge establishes direction. Do not jump
            // between disconnected patches or pick a nearby unrelated branch.
            foreach(var edge in ConnectedEdgeLoop(part,(previous,current)))
            {result.Add(edge.Item1);result.Add(edge.Item2);}
            return result;
        }

        private static HashSet<int> DirectionalFaceLoop(Part part,int previous,int current)
        {
            var result=new HashSet<int>();
            if(part==null||current<0||current>=part.faces.Count)return result;
            result.Add(current);
            if(previous<0||previous==current||previous>=part.faces.Count)return result;
            var edges=StudioRetopologyOperations.Edges(part);
            var shared=edges.Where(e=>e.Value.Count==2&&e.Value.Contains(previous)&&e.Value.Contains(current)).Select(e=>e.Key).ToArray();
            if(shared.Length!=1)return result;
            var seam=shared[0];result.Add(previous);
            void Walk(int face,(int,int) entered)
            {
                while(true)
                {
                    var ids=part.faces[face].vertices;
                    if(ids.Length!=4)return; // A triangle or pole must not fan out into a hull.
                    int side=-1;
                    for(int i=0;i<4;i++)
                        if(StudioRetopologyOperations.Edge(ids[i],ids[(i+1)%4])==entered){side=i;break;}
                    if(side<0)return;
                    var exit=StudioRetopologyOperations.Edge(ids[(side+2)%4],ids[(side+3)%4]);
                    if(!edges.TryGetValue(exit,out var adjacent)||adjacent.Count!=2)return;
                    int next=adjacent[0]==face?adjacent[1]:adjacent[0];
                    if(!result.Add(next))return;
                    face=next;entered=exit;
                }
            }
            Walk(previous,seam);Walk(current,seam);
            return result;
        }
    }
}
