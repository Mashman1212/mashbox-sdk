using UnityEditor;
using UnityEngine;
using Face = MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private float stripWidth = .06f;
        private float stripCursorDepth;
        private bool stripStroke;
        private int stripControl, stripUndo, stripLeft = -1, stripRight = -1;
        private int stripMirrorLeft = -1, stripMirrorRight = -1;
        private Vector3 stripCenter, stripNormal, stripSide;
        private bool stripMirror;
        private bool stripClosed;
        private bool stripFirstRow;
        private float StripRowSpacing => stripWidth*(stripFirstRow?.2f:.75f);
        private (int,int) stripFinishEdge=(-1,-1);
        private bool TryFinishStrip(Vector3 pointer,Vector3 normal)
        {
            if(stripClosed||stripLeft<0||stripRight<0)return false;
            if(TryFillStripTriangle(pointer,normal,true))return true;
            var left=Active.vertices[stripLeft];var right=Active.vertices[stripRight];
            var across=(right-left).normalized;
            var travel=Vector3.Cross(across,normal).normalized;
            var best=(-1,-1);float score=float.PositiveInfinity;
            foreach(var entry in StudioRetopologyOperations.Edges(Active))
            {
                int l=entry.Key.Item1,r=entry.Key.Item2;
                if(entry.Value.Count!=1||l==stripLeft||r==stripLeft||l==stripRight||r==stripRight)continue;
                var a=Active.vertices[l];var b=Active.vertices[r];
                if(Vector3.Dot(b-a,across)<0){var temp=a;a=b;b=temp;int id=l;l=r;r=id;}
                float width=Vector3.Distance(a,b);
                if(width<stripWidth*.5f||width>stripWidth*1.75f||Vector3.Dot((b-a).normalized,across)<.35f)continue;
                var middle=(a+b)*.5f;var advance=middle-stripCenter;
                if(advance.magnitude>stripWidth*1.5f||Vector3.Dot(advance,travel)<stripWidth*.0001f)continue;
                if(!StripJoinValid(stripLeft,stripRight,l,r,normal))continue;
                // Capture the whole edge, including near its ends. Midpoint-only
                // capture missed slightly steered strokes and let another row pass it.
                float along=Mathf.Clamp01(Vector3.Dot(pointer-a,b-a)/(b-a).sqrMagnitude);
                float edgeDistance=(pointer-Vector3.Lerp(a,b,along)).sqrMagnitude;
                if(edgeDistance>stripWidth*stripWidth*.16f)continue;
                float d=edgeDistance+(middle-pointer).sqrMagnitude*.1f;
                if(d<score){score=d;best=(l,r);}
            }
            if(best.Item1<0)return false;
            stripFinishEdge=best;
            try { AppendStrip((Active.vertices[best.Item1]+Active.vertices[best.Item2])*.5f,normal); }
            finally {stripFinishEdge=(-1,-1);}
            return stripClosed;
        }
        private bool StripJoinValid(int sourceLeft,int sourceRight,int targetLeft,int targetRight,Vector3 normal)
        {
            if(sourceLeft<0||sourceRight<0)return true;
            var l=Active.vertices[sourceLeft];var r=Active.vertices[sourceRight];
            var a=Active.vertices[targetLeft];var b=Active.vertices[targetRight];
            var across=(r-l).normalized;var forward=Vector3.Cross(across,normal).normalized;
            if(Vector3.Dot((b-a).normalized,across)<.35f)return false;
            // Test the actual bridge instead of requiring both ends to advance
            // by a fixed distance. A valid tapered final row can be very short.
            var quad=new[]{l,r,b,a};float epsilon=stripWidth*stripWidth*.00001f;
            float orientation=Vector3.Dot(Vector3.Cross(r-l,a-l),normal);
            if(Mathf.Abs(orientation)<=epsilon)return false;
            for(int i=0;i<4;i++)
                if(Vector3.Dot(Vector3.Cross(quad[(i+1)%4]-quad[i],quad[(i+2)%4]-quad[(i+1)%4]),normal)*Mathf.Sign(orientation)<=epsilon)return false;
            if(Vector3.Dot((a+b-l-r)*.5f,forward)<=stripWidth*.0001f)return false;
            var edges=StudioRetopologyOperations.Edges(Active);
            if(!edges.TryGetValue(StudioRetopologyOperations.Edge(targetLeft,targetRight),out var faces)||faces.Count!=1)return false;
            Vector3 inside=Vector3.zero;var ids=Active.faces[faces[0]].vertices;
            foreach(int id in ids)inside+=Active.vertices[id];inside/=ids.Length;
            // Require the source on the empty side of the target edge, in its tangent plane.
            var edge=b-a;var middle=(a+b)*.5f;
            float occupied=Vector3.Dot(Vector3.Cross(edge,inside-middle),normal);
            float approach=Vector3.Dot(Vector3.Cross(edge,(l+r)*.5f-middle),normal);
            return occupied*approach<0;
        }
        private int StripSeamVertex(Vector3 point,int excludeA,int excludeB)
        {
            int best=-1;float distance=stripWidth*stripWidth*.09f;
            foreach(var edge in StudioRetopologyOperations.Edges(Active))
            {
                if(edge.Value.Count!=1)continue;
                foreach(int id in new[]{edge.Key.Item1,edge.Key.Item2})
                {
                    if(id==excludeA||id==excludeB)continue;
                    if(excludeA>=0&&excludeB>=0)
                    {
                        var l=Active.vertices[excludeA];var r=Active.vertices[excludeB];
                        var across=(r-l).normalized;var forward=Vector3.Cross(across,stripNormal).normalized;
                        var origin=(point-l).sqrMagnitude<(point-r).sqrMagnitude?l:r;
                        if(Vector3.Dot(Active.vertices[id]-origin,forward)<=stripWidth*.02f)continue;
                    }
                    float d=(Active.vertices[id]-point).sqrMagnitude;
                    if(d<distance){distance=d;best=id;}
                }
            }
            return best;
        }
        private (int,int) StripBoundary(Vector3 a,Vector3 b,int excludeLeft=-1,int excludeRight=-1,bool validateJoin=true)
        {
            var best=(-1,-1);float distance=stripWidth*stripWidth*.5f;
            foreach(var edge in StudioRetopologyOperations.Edges(Active))
            {
                int l=edge.Key.Item1,r=edge.Key.Item2;
                if(edge.Value.Count!=1||l==excludeLeft||l==excludeRight||r==excludeLeft||r==excludeRight)continue;
                float forward=(Active.vertices[l]-a).sqrMagnitude+(Active.vertices[r]-b).sqrMagnitude;
                float reverse=(Active.vertices[r]-a).sqrMagnitude+(Active.vertices[l]-b).sqrMagnitude;
                float score=Mathf.Min(forward,reverse);
                var candidate=forward<=reverse?(l,r):(r,l);
                if(validateJoin&&excludeLeft>=0&&!StripJoinValid(excludeLeft,excludeRight,candidate.Item1,candidate.Item2,stripNormal))continue;
                if(score<distance){distance=score;best=candidate;}
            }
            return best;
        }
        private (int,int) PickStripBoundary(Vector2 mouse,Vector3 surface)
        {
            var best=(-1,-1);float distance=32;
            foreach(var edge in StudioRetopologyOperations.Edges(Active))
            {
                if(edge.Value.Count!=1)continue;
                var a=Active.vertices[edge.Key.Item1];var b=Active.vertices[edge.Key.Item2];
                var center=(a+b)*.5f;
                if(Vector3.Distance(center,surface)>Mathf.Max(stripWidth,(a-b).magnitude)*1.25f||
                    view.camera.WorldToViewportPoint(center).z<=0||!IsSelectionPointVisible(center,false))continue;
                float d=HandleUtility.DistancePointToLineSegment(mouse,HandleUtility.WorldToGUIPoint(a),HandleUtility.WorldToGUIPoint(b));
                if(d<distance){distance=d;best=edge.Key;}
            }
            return best;
        }
        private void AttachStrip((int,int) edge,Vector3 normal)
        {
            stripLeft=edge.Item1;stripRight=edge.Item2;
            stripCenter=(Active.vertices[stripLeft]+Active.vertices[stripRight])*.5f;
            var faces=StudioRetopologyOperations.Edges(Active)[StudioRetopologyOperations.Edge(stripLeft,stripRight)];
            Vector3 inside=Vector3.zero;var ids=Active.faces[faces[0]].vertices;
            foreach(int id in ids)inside+=Active.vertices[id];inside/=ids.Length;
            // The actual edge determines the new row orientation. A skewed
            // neighboring face centroid must not tilt the permitted draw direction.
            var edgeAxis=Vector3.ProjectOnPlane(Active.vertices[stripRight]-Active.vertices[stripLeft],normal).normalized;
            var outward=Vector3.Cross(edgeAxis,normal).normalized;
            if(Vector3.Dot(outward,stripCenter-inside)<0)outward=-outward;
            stripSide=Vector3.Cross(normal,outward).normalized;stripNormal=normal;
            stripFirstRow=true;
            if(Vector3.Dot(Active.vertices[stripRight]-Active.vertices[stripLeft],stripSide)<0)
            {int swap=stripLeft;stripLeft=stripRight;stripRight=swap;}
            stripWidth=Vector3.Distance(Active.vertices[stripLeft],Active.vertices[stripRight]);
            var a=Active.vertices[stripLeft];var b=Active.vertices[stripRight];
            var mirror=StripBoundary(new Vector3(-a.x,a.y,a.z),new Vector3(-b.x,b.y,b.z),stripLeft,stripRight,false);
            stripMirror=Document.symmetry&&mirror.Item1>=0;
            stripMirrorLeft=mirror.Item1;stripMirrorRight=mirror.Item2;
        }
        private void ResetStrip() { stripLeft = stripRight = stripMirrorLeft = stripMirrorRight = -1; }
        private void StartStrip() { ClearPatchCorners(); activeTool = RetopoTool.Strip; repaint(); }
        private void DrawStripSettings()
        {
            if (activeTool != RetopoTool.Strip) return;
            GUILayout.Space(6);
            GUILayout.Label("QUAD STRIP", StudioTheme.Styles.boldLabel);
            stripWidth = Mathf.Max(.001f, EditorGUILayout.FloatField("Width", stripWidth));
            GUILayout.Label("Drag across the reference to draw connected quads. Start near the highlighted end to continue after orbiting. Start elsewhere for a new strip. Ctrl + MMB drag sideways or [ / ] changes width. Escape ends the strip; during a stroke it cancels that stroke.", StudioTheme.Styles.wordWrappedMiniLabel);
            if (GUILayout.Button("End strip")) CancelConstruction();
        }
        private void EndStripStroke(bool cancel = false)
        {
            if (!stripStroke) return;
            stripStroke = false;
            if (GUIUtility.hotControl == stripControl) GUIUtility.hotControl = 0;
            if (cancel) { Undo.RevertAllDownToGroup(stripUndo); }
            else Undo.CollapseUndoOperations(stripUndo);
            StoreToolState();
            repaint();
        }
        private Vector3 StripProject(Vector3 p, Vector3 normal)
        {
            return Document.project ? Surface.ProjectAlong(p, normal, Document.offset, stripWidth) : p;
        }
        private int StripNextSideVertex(int start,int other,Vector3 normal)
        {
            if(start<0||other<0)return -1;
            var origin=Active.vertices[start];var across=(Active.vertices[stripRight]-Active.vertices[stripLeft]).normalized;
            var forward=Vector3.Cross(across,normal).normalized;
            int best=-1;float score=float.PositiveInfinity;
            foreach(var entry in StudioRetopologyOperations.Edges(Active))
            {
                if(entry.Value.Count!=1)continue;
                int next=entry.Key.Item1==start?entry.Key.Item2:entry.Key.Item2==start?entry.Key.Item1:-1;
                if(next<0||next==other)continue;
                var step=Active.vertices[next]-origin;float advance=Vector3.Dot(step,forward);
                if(advance<Mathf.Max(stripWidth*.02f,step.magnitude*.5f)||step.magnitude>stripWidth*4||Mathf.Abs(Vector3.Dot(step,across))>stripWidth*.75f)continue;
                // Follow a neighboring strip only when our other endpoint lies
                // on the empty side of its border, never across its existing face.
                Vector3 inside=Vector3.zero;var ids=Active.faces[entry.Value[0]].vertices;
                foreach(int id in ids)inside+=Active.vertices[id];inside/=ids.Length;
                float occupied=Vector3.Dot(Vector3.Cross(step,inside-origin),normal);
                float incoming=Vector3.Dot(Vector3.Cross(step,Active.vertices[other]-origin),normal);
                if(occupied*incoming>=0)continue;
                if(step.sqrMagnitude<score){score=step.sqrMagnitude;best=next;}
            }
            return best;
        }
        private void AppendStrip(Vector3 center, Vector3 normal)
        {
            int guideLeft=-1,guideRight=-1;
            Vector3 guidedA=default,guidedB=default;
            if(stripFinishEdge.Item1<0&&stripLeft>=0)
            {
                guideLeft=StripNextSideVertex(stripLeft,stripRight,normal);
                guideRight=StripNextSideVertex(stripRight,stripLeft,normal);
                if(guideLeft>=0||guideRight>=0)
                {
                    var l=Active.vertices[stripLeft];var r=Active.vertices[stripRight];
                    var step=guideLeft>=0?Active.vertices[guideLeft]-l:Active.vertices[guideRight]-r;
                    guidedA=guideLeft>=0?Active.vertices[guideLeft]:StripProject(l+step,normal);
                    guidedB=guideRight>=0?Active.vertices[guideRight]:StripProject(r+step,normal);
                    var nextCenter=(guidedA+guidedB)*.5f;var advance=nextCenter-stripCenter;
                    // Do not insert a width-spaced row before the existing seam row.
                    if(Vector3.Dot(center-stripCenter,advance)<advance.sqrMagnitude*.75f)return;
                    center=nextCenter;
                }
            }
            // Keep row length stable when screen-space samples are sparse or the camera is zoomed out.
            if(stripFinishEdge.Item1<0&&guideLeft<0&&guideRight<0)center=Vector3.MoveTowards(stripCenter,center,Mathf.Max(.001f,stripWidth));
            var direction = Vector3.ProjectOnPlane(center - stripCenter, normal);
            if (direction.sqrMagnitude < .00000001f) return;
            var side = Vector3.Cross(normal, direction.normalized).normalized;
            if (stripLeft >= 0 && Vector3.Dot(side, stripSide) < .25f) return; // Avoid folded quads on abrupt reversals.
            // A small change in cursor direction must not swivel the whole row abruptly.
            if(stripLeft>=0)side=Vector3.Slerp(Vector3.ProjectOnPlane(stripSide,normal).normalized,side,.35f).normalized;
            var a = StripProject(center - side * stripWidth * .5f, normal);
            var b = StripProject(center + side * stripWidth * .5f, normal);
            if(guideLeft>=0||guideRight>=0){a=guidedA;b=guidedB;}
            if ((a-b).sqrMagnitude < stripWidth*stripWidth*.04f) return;
            var join=stripFinishEdge.Item1>=0?stripFinishEdge:StripBoundary(a,b,stripLeft,stripRight);
            if(stripFinishEdge.Item1>=0){a=Active.vertices[join.Item1];b=Active.vertices[join.Item2];}
            var mirrorJoin=stripMirror?StripBoundary(new Vector3(-a.x,a.y,a.z),new Vector3(-b.x,b.y,b.z),stripMirrorLeft,stripMirrorRight,false):(-1,-1);
            if(stripMirror&&mirrorJoin.Item1<0)join=(-1,-1);
            bool closing=stripLeft>=0&&join.Item1>=0;
            // Match side seams too; a strip is not only connected at its two ends.
            int seamA=closing?join.Item1:guideLeft>=0?guideLeft:StripSeamVertex(a,stripLeft,stripRight);
            int seamB=closing?join.Item2:guideRight>=0?guideRight:StripSeamVertex(b,stripLeft,stripRight);
            if(seamA>=0)a=Active.vertices[seamA];
            if(seamB>=0)b=Active.vertices[seamB];
            if(seamA>=0&&seamA==seamB)return;
            // Keep each stroke connected by reusing the previous end's vertex IDs.
            Change("Draw quad strip", () =>
            {
                int Add(Vector3 p) { int id=Active.vertices.Count; Active.vertices.Add(p); Active.uv.Add(Vector2.zero); return id; }
                void Quad(int l,int r,int nr,int nl,Vector3 n)
                {
                    var ids = new[]{l,r,nr,nl};
                    var edges=StudioRetopologyOperations.Edges(Active);
                    for(int i=0;i<4;i++)
                    {
                        for(int j=i+1;j<4;j++)if(ids[i]==ids[j])throw new System.InvalidOperationException("Strip seam would collapse a quad. Undo this stroke and use a smaller width.");
                        if(edges.TryGetValue(StudioRetopologyOperations.Edge(ids[i],ids[(i+1)%4]),out var faces)&&faces.Count>=2)
                            throw new System.InvalidOperationException("Strip stopped at an occupied seam. Extend an open boundary instead.");
                    }
                    if(Vector3.Dot(Vector3.Cross(Active.vertices[r]-Active.vertices[l],Active.vertices[nl]-Active.vertices[l]),n)<0)
                        System.Array.Reverse(ids);
                    // Reject bow-ties/folded corners and faces growing into an occupied side.
                    float epsilon=stripWidth*stripWidth*.00001f;
                    for(int i=0;i<4;i++)
                    {
                        var p0=Active.vertices[ids[i]];var p1=Active.vertices[ids[(i+1)%4]];var p2=Active.vertices[ids[(i+2)%4]];
                        if(Vector3.Dot(Vector3.Cross(p1-p0,p2-p1),n)<=epsilon)
                            throw new System.InvalidOperationException("Strip stopped before a folded quad. Steer toward the open border to continue.");
                        if(edges.TryGetValue(StudioRetopologyOperations.Edge(ids[i],ids[(i+1)%4]),out var adjacent))
                        {
                            var other=Active.faces[adjacent[0]].vertices;Vector3 inside=Vector3.zero;
                            foreach(int id in other)inside+=Active.vertices[id];inside/=other.Length;
                            if(Vector3.Dot(Vector3.Cross(p1-p0,inside-p0),n)>epsilon)
                                throw new System.InvalidOperationException("Strip stopped before overlapping an existing face. Continue from its open edge.");
                        }
                    }
                    Active.faces.Add(new Face(ids));
                }
                void Bridge(int l,int r,int nr,int nl,Vector3 n)
                {
                    // Reuse complete side chains, not only their end points. A
                    // short closing span can still contain several neighboring rows.
                    var corners=new[]{Active.vertices[l],Active.vertices[r],Active.vertices[nr],Active.vertices[nl]};
                    var borders=StudioRetopologyPatchBorders.Find(Active,corners,Mathf.Max(.000001f,stripWidth*.0001f));
                    int columns=1,rows=1;bool reverse=false;
                    StudioRetopologyPatchBorders.Match(Active,borders,ref columns,ref rows,ref reverse);
                    if(columns!=1)throw new System.InvalidOperationException("Strip end contains multiple edges. Bridge matching border sections instead.");
                    var left=borders[3];var right=borders[1];
                    if(left!=null)System.Array.Reverse(left);
                    // On an unattached side, follow the existing side's row spacing.
                    var guide=left??right;float total=0,travelled=0;
                    if(guide!=null)for(int i=1;i<guide.Length;i++)total+=Vector3.Distance(Active.vertices[guide[i-1]],Active.vertices[guide[i]]);
                    int previousL=l,previousR=r;
                    for(int row=1;row<=rows;row++)
                    {
                        if(guide!=null)travelled+=Vector3.Distance(Active.vertices[guide[row-1]],Active.vertices[guide[row]]);
                        float fraction=total>0?travelled/total:(float)row/rows;
                        int nextL=row==rows?nl:left!=null?left[row]:Add(StripProject(Vector3.Lerp(corners[0],corners[3],fraction),n));
                        int nextR=row==rows?nr:right!=null?right[row]:Add(StripProject(Vector3.Lerp(corners[1],corners[2],fraction),n));
                        Quad(previousL,previousR,nextR,nextL,n);
                        previousL=nextL;previousR=nextR;
                    }
                }
                Vector3 Mirror(Vector3 p) => new Vector3(-p.x,p.y,p.z);
                if(stripLeft<0)
                {
                    var startSide=Vector3.Cross(stripNormal,direction.normalized).normalized;
                    var startA=StripProject(stripCenter-startSide*stripWidth*.5f,stripNormal);
                    var startB=StripProject(stripCenter+startSide*stripWidth*.5f,stripNormal);
                    stripLeft=Add(startA);stripRight=Add(startB);
                    if(stripMirror){stripMirrorLeft=Add(Mirror(startA));stripMirrorRight=Add(Mirror(startB));}
                }
                int nextLeft=seamA>=0?seamA:Add(a),nextRight=seamB>=0?seamB:Add(b);
                Bridge(stripLeft,stripRight,nextRight,nextLeft,normal);
                stripLeft=nextLeft;stripRight=nextRight;
                if(stripMirror)
                {
                    int ml=closing?mirrorJoin.Item1:StripSeamVertex(Mirror(a),stripMirrorLeft,stripMirrorRight);
                    int mr=closing?mirrorJoin.Item2:StripSeamVertex(Mirror(b),stripMirrorLeft,stripMirrorRight);
                    if(ml<0)ml=Add(Mirror(a));if(mr<0)mr=Add(Mirror(b));
                    Bridge(stripMirrorLeft,stripMirrorRight,mr,ml,Mirror(normal));
                    stripMirrorLeft=ml;stripMirrorRight=mr;
                }
                Active.needsUV=true;
            });
            stripCenter=(a+b)*.5f;stripNormal=normal;stripSide=side;stripFirstRow=false;
            if(closing){stripClosed=true;ResetStrip();status="Strip welded to the existing boundary.";}
            StoreToolState();
        }
        private bool HandleStrip(Event evt, MashBoxStudioViewport canvas, bool overUI)
        {
            int control=GUIUtility.GetControlID("Retopology quad strip".GetHashCode(),FocusType.Passive);
            if(stripStroke)
            {
                if(evt.type==EventType.KeyDown&&evt.keyCode==KeyCode.Escape){EndStripStroke(true);evt.Use();return true;}
                if(GUIUtility.hotControl!=stripControl||evt.type==EventType.Ignore||evt.type==EventType.MouseLeaveWindow)
                {EndStripStroke();return true;}
                if(evt.type==EventType.MouseUp&&evt.button==0)
                {
                    if(!overUI&&!evt.alt&&ReferenceHit(evt.mousePosition,out var release))Run(()=>TryFinishStrip(release.point,release.normal));
                    EndStripStroke();evt.Use();return true;
                }
                if(evt.type==EventType.MouseDrag&&evt.button==0)
                {
                    if(!overUI&&!evt.alt)
                    {
                        // Resample the screen stroke so fast drags do not create enormous quads.
                        Vector2 start=HandleUtility.WorldToGUIPoint(stripCenter),end=evt.mousePosition;
                        int samples=Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(start,end)/4),1,128);
                        for(int i=1;i<=samples;i++)
                        {
                            if(stripClosed)break;
                            if(!ReferenceHit(Vector2.Lerp(start,end,(float)i/samples),out var hit))break;
                            if(TryFinishStrip(hit.point,hit.normal))break;
                            float distance=Vector3.Distance(stripCenter,hit.point);
                            if(distance>stripWidth*4)break; // Don't jump to another limb or across empty space.
                            if(distance>=StripRowSpacing||StripNextSideVertex(stripLeft,stripRight,hit.normal)>=0||StripNextSideVertex(stripRight,stripLeft,hit.normal)>=0)
                            {
                                try { AppendStrip(hit.point,hit.normal); }
                                catch { EndStripStroke(); ResetStrip(); throw; }
                            }
                        }
                    }
                    evt.Use();repaint();
                }
            }
            if(activeTool!=RetopoTool.Strip)return false;
            if(evt.type==EventType.Layout&&!evt.alt&&!overUI)HandleUtility.AddDefaultControl(control);
            if(!overUI&&!contextHeld&&!evt.alt)
            {
                if(evt.type==EventType.KeyDown&&!EditorGUIUtility.editingTextField)
                {
                    if(evt.keyCode==KeyCode.Escape){CancelConstruction();evt.Use();repaint();}
                    else if(evt.keyCode==KeyCode.LeftBracket||evt.keyCode==KeyCode.RightBracket)
                    {stripWidth=Mathf.Max(.001f,stripWidth*(evt.keyCode==KeyCode.RightBracket?1.15f:1/1.15f));evt.Use();repaint();}
                    else if(evt.keyCode==KeyCode.F){Frame();evt.Use();}
                    else if((evt.control||evt.command)&&evt.keyCode==KeyCode.S){Run(Document.SaveWorkspace);evt.Use();}
                }
                if(evt.type==EventType.MouseDown&&evt.button==0&&GUIUtility.hotControl==0&&Active.showOutput&&ReferenceHit(evt.mousePosition,out var hit))
                {
                    bool filled=false;Run(()=>filled=TryFillStripTriangle(hit.point,hit.normal,false));
                    if(filled){evt.Use();repaint();return true;}
                    var boundary=PickStripBoundary(evt.mousePosition,hit.point);
                    if(boundary.Item1<0&&PickFace(evt.mousePosition)>=0)
                    {status="Start on an open boundary to extend this mesh, or on an empty area for a new strip.";evt.Use();repaint();return true;}
                    stripUndo=RecordToolAction("Draw quad strip");
                    if(boundary.Item1>=0)AttachStrip(boundary,hit.normal);
                    else
                    {
                        ResetStrip();stripCenter=hit.point;stripNormal=hit.normal;stripMirror=Document.symmetry;stripFirstRow=false;
                    }
                    stripClosed=false;
                    StoreToolState();
                    stripStroke=true;stripControl=control;GUIUtility.hotControl=control;
                    GUIUtility.keyboardControl=0;evt.Use();repaint();
                }
                if(evt.type==EventType.MouseMove)repaint();
                if(evt.type==EventType.Repaint)
                {
                    var old=Handles.color;Handles.color=new Color(.5f,1,.87f);
                    bool valid=ReferenceHit(evt.mousePosition,out var hover);
                    StudioBrushCursor.Draw(view,evt.mousePosition,stripWidth*.5f,valid,hover.point,hover.normal,new Color(.5f,1,.87f),ref stripCursorDepth);
                    if(valid)
                    {
                        var hole=FindStripTriangle(hover.point,hover.normal,false);
                        if(hole!=null)
                        {
                            Handles.color=Color.yellow;
                            Handles.DrawAAPolyLine(4,Active.vertices[hole[0]],Active.vertices[hole[1]],Active.vertices[hole[2]],Active.vertices[hole[0]]);
                            Handles.Label(hover.point,"Fill triangle");
                        }
                        var snap=PickStripBoundary(evt.mousePosition,hover.point);
                        if(snap.Item1>=0)
                        {Handles.color=Color.yellow;Handles.DrawAAPolyLine(5,Active.vertices[snap.Item1],Active.vertices[snap.Item2]);}
                    }
                    if(stripLeft>=0&&stripRight<Active.vertices.Count)
                    {Handles.color=Color.yellow;Handles.DrawAAPolyLine(5,Active.vertices[stripLeft],Active.vertices[stripRight]);}
                    Handles.color=old;
                }
            }
            return true;
        }
    }
}
