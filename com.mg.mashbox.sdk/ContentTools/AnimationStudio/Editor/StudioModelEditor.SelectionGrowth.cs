using System;
using System.Collections.Generic;
using System.Linq;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {

        private void ConvertSelection(ComponentMode next,bool perimeter=false)
        {
            if(!Document)throw new InvalidOperationException("Select objects or mesh components to convert.");
            StopTargetWeld();FinishSelectionPaint();FinishLiveTool();FinishChannelEdit();StopCutTool();EndPivotSnap();CancelMarkingMenu();
            if(next==mode&&!perimeter)return;
            var before=CaptureSelection();
            var converted=new Dictionary<int,HashSet<int>>();
            bool fromObjects=mode==ComponentMode.Object;
            var nodes=fromObjects
                ? Enumerable.Range(0,Document.nodes.Count).Where(i=>objects.Contains(i)||HasSelectedParent(i)).ToList()
                : ComponentTargetNodes();
            bool hasSource=false;
            foreach(int node in nodes)
            {
                if(node<0||node>=Transforms.Length||!Document.nodes[node].mesh||!Transforms[node].gameObject.activeInHierarchy||!IsolationNodeVisible(node))continue;
                using(new ComponentTargetScope(this,node))
                {
                    if(!CanEditComponents()&&next!=ComponentMode.Object)continue;
                    var sourceMode=fromObjects?ComponentMode.Object:mode;
                    var selected=new HashSet<int>();
                    if(sourceMode!=ComponentMode.Object)
                        foreach(int id in Components)if(id>=0&&id<ComponentCount()&&IsolationAllowsComponent(id))selected.Add(id);
                    hasSource|=sourceMode==ComponentMode.Object||selected.Count>0;
                    if(next==ComponentMode.Object)
                    {if(selected.Count>0)converted.Add(node,new HashSet<int>());continue;}
                    converted.Add(node,ConvertComponents(sourceMode,next,selected,perimeter));
                }
            }
            if(!hasSource||converted.Count==0)throw new InvalidOperationException("Select objects or mesh components to convert.");
            int active=Active;
            ClearAllComponentSelections();objects.Clear();
            foreach(var pair in converted)
            {
                objects.Add(pair.Key);var target=ComponentTargetAt(pair.Key);
                target.mode=next;target.selection.UnionWith(pair.Value);
            }
            mode=next;
            Active=objects.Contains(active)?active:converted.Keys.First();
            pivot=false;ResetComponentHover();SyncComponentTarget();
            if(next==ComponentMode.Object)CloseLiveToolPanes();
            RecordSelectionChange(before,label:"Convert selection");repaint();toolOptions?.Repaint();
        }

        private HashSet<int> ConvertComponents(ComponentMode source,ComponentMode next,HashSet<int> selected,bool perimeter)
        {
            var result=new HashSet<int>();
            var positions=ComponentPositions();
            var logical=weld?SelectionWeldIdentities(positions):Enumerable.Range(0,positions.Count).ToArray();
            (int,int) Key(int a,int b)=>logical[a]<logical[b]?(logical[a],logical[b]):(logical[b],logical[a]);
            var vertices=new HashSet<int>();var boundaries=new HashSet<(int,int)>();
            var edges=Edges();var faces=Faces();
            if(perimeter&&next==ComponentMode.Face)
            {
                // First capture the ordinary face conversion, then retain only
                // faces touching that region's authored boundary. Never expand
                // from the retained ring or use render-triangulation diagonals.
                var region=ConvertComponents(source,next,selected,false);
                var incidence=new Dictionary<(int,int),int>();
                foreach(int f in region)
                {
                    var face=faces[f].Vertices;
                    for(int c=0;c<face.Count;c++)
                    {var key=Key(face[c],face[(c+1)%face.Count]);incidence.TryGetValue(key,out int count);incidence[key]=count+1;}
                }
                region.RemoveWhere(f=>{
                    var face=faces[f].Vertices;
                    for(int c=0;c<face.Count;c++)if(incidence[Key(face[c],face[(c+1)%face.Count])]==1)return false;
                    return true;
                });
                return region;
            }
            if(source==ComponentMode.Object)
            {
                for(int i=0;i<logical.Length;i++)vertices.Add(logical[i]);
                foreach(var edge in edges)boundaries.Add(Key(edge.a,edge.b));
            }
            else foreach(int id in selected)
            {
                if(source==ComponentMode.Vertex)vertices.Add(logical[id]);
                else if(source==ComponentMode.Edge)
                {var edge=edges[id];vertices.Add(logical[edge.a]);vertices.Add(logical[edge.b]);boundaries.Add(Key(edge.a,edge.b));}
                else
                {
                    var face=faces[id].Vertices;
                    for(int c=0;c<face.Count;c++){vertices.Add(logical[face[c]]);boundaries.Add(Key(face[c],face[(c+1)%face.Count]));}
                }
            }
            if(source==ComponentMode.Vertex)
                foreach(var edge in edges)if(vertices.Contains(logical[edge.a])&&vertices.Contains(logical[edge.b]))boundaries.Add(Key(edge.a,edge.b));
            if(perimeter)
            {
                // Count authored region incidences, not render triangles. A selected
                // face shared by another selected face contributes an interior edge.
                var uses=new Dictionary<(int,int),int>();var isolation=ComponentIsolation(Active);
                for(int f=0;f<faces.Count;f++)
                {
                    if(isolation!=null&&!isolation.faces.Contains(f))continue;
                    var face=faces[f].Vertices;
                    bool included=source==ComponentMode.Object||source==ComponentMode.Face&&selected.Contains(f)||
                        source==ComponentMode.Vertex&&face.All(v=>vertices.Contains(logical[v]))||
                        source==ComponentMode.Edge&&Enumerable.Range(0,face.Count).All(c=>boundaries.Contains(Key(face[c],face[(c+1)%face.Count])));
                    if(!included)continue;
                    for(int c=0;c<face.Count;c++){var key=Key(face[c],face[(c+1)%face.Count]);uses.TryGetValue(key,out int count);uses[key]=count+1;}
                }
                boundaries.RemoveWhere(key=>uses.TryGetValue(key,out int count)&&count>1);
                var rim=new HashSet<int>();
                foreach(var key in boundaries){rim.Add(key.Item1);rim.Add(key.Item2);}
                // Partial vertex regions also have a boundary at an unselected neighbor.
                if(source==ComponentMode.Vertex)
                {
                    var incident=new HashSet<int>();
                    foreach(var edge in edges)
                    {
                        int a=logical[edge.a],b=logical[edge.b];incident.Add(a);incident.Add(b);
                        if(vertices.Contains(a)&&!vertices.Contains(b))rim.Add(a);
                        if(vertices.Contains(b)&&!vertices.Contains(a))rim.Add(b);
                    }
                    foreach(int vertex in vertices)if(!incident.Contains(vertex))rim.Add(vertex);
                }
                vertices=rim;
            }
            if(next==ComponentMode.Vertex)
            {for(int i=0;i<logical.Length;i++)if(vertices.Contains(logical[i]))result.Add(i);}
            else if(next==ComponentMode.Edge)
            {for(int i=0;i<edges.Count;i++)if(boundaries.Contains(Key(edges[i].a,edges[i].b)))result.Add(i);}
            else if(source==ComponentMode.Face)result.UnionWith(selected);
            else if(source==ComponentMode.Object)result.UnionWith(Enumerable.Range(0,faces.Count));
            else
            {
                for(int i=0;i<faces.Count;i++)
                {
                    var face=faces[i].Vertices;
                    for(int c=0;c<face.Count;c++)
                        if(source==ComponentMode.Vertex?vertices.Contains(logical[face[c]]):boundaries.Contains(Key(face[c],face[(c+1)%face.Count])))
                        {result.Add(i);break;}
                }
            }
            var old=mode;mode=next;
            try{result.RemoveWhere(i=>!IsolationAllowsComponent(i));}
            finally{mode=old;}
            return result;
        }

        private bool CanGrowComponentSelection => Document && mode!=ComponentMode.Object && ComponentTargetNodes(true).Any(IsolationNodeVisible);
        private void GrowComponentSelection(bool shrink)
        {
            var before=CaptureSelection();
            foreach(int node in ComponentTargetNodes(true))
            {
                if(!IsolationNodeVisible(node))continue;
                using(new ComponentTargetScope(this,node))ResizeComponentSelection(shrink);
            }
            RecordSelectionChange(before);repaint();
        }
        private void ResizeComponentSelection(bool shrink)
        {
            var points=ComponentPositions();
            var vertices=weld?SelectionWeldIdentities(points):Enumerable.Range(0,points.Count).ToArray();
            (int,int) Key(int a,int b)=>a<b?(a,b):(b,a);
            var edges=Edges();var groups=new List<HashSet<int>>();int[] logical;
            if(mode==ComponentMode.Vertex)
            {
                logical=vertices;
                foreach(var edge in edges)groups.Add(new HashSet<int>{vertices[edge.a],vertices[edge.b]});
            }
            else if(mode==ComponentMode.Edge)
            {
                logical=new int[edges.Count];var ids=new Dictionary<(int,int),int>();var incident=new Dictionary<int,HashSet<int>>();
                for(int i=0;i<edges.Count;i++)
                {
                    int a=vertices[edges[i].a],b=vertices[edges[i].b];var key=Key(a,b);
                    if(!ids.TryGetValue(key,out int id)){id=ids.Count;ids.Add(key,id);}logical[i]=id;
                    foreach(int vertex in new[]{a,b}){if(!incident.TryGetValue(vertex,out var list))incident[vertex]=list=new HashSet<int>();list.Add(id);}
                }
                groups.AddRange(incident.Values);
            }
            else
            {
                var faces=Faces();logical=Enumerable.Range(0,faces.Count).ToArray();var incident=new Dictionary<int,HashSet<int>>();
                for(int i=0;i<faces.Count;i++)
                {
                    var face=faces[i].Vertices;
                    for(int e=0;e<face.Count;e++)
                    {
                        // Corner neighbors belong to the same square face ring.
                        int key=vertices[face[e]];
                        if(!incident.TryGetValue(key,out var list))incident[key]=list=new HashSet<int>();list.Add(i);
                    }
                }
                groups.AddRange(incident.Values);
            }
            var eligible=new HashSet<int>();var selected=new HashSet<int>();
            for(int i=0;i<logical.Length;i++)if(IsolationAllowsComponent(i))
            {eligible.Add(logical[i]);if(Components.Contains(i))selected.Add(logical[i]);}
            var next=new HashSet<int>(selected);
            // Read only the captured selection, so each invocation is one ring.
            // Incidence groups avoid quadratic edge-adjacency cliques at high valence.
            foreach(var group in groups)
            {
                bool boundary=shrink?group.Any(i=>eligible.Contains(i)&&!selected.Contains(i)):group.Any(selected.Contains);
                if(!boundary)continue;
                foreach(int id in group)if(eligible.Contains(id)){if(shrink)next.Remove(id);else next.Add(id);}
            }
            for(int i=0;i<logical.Length;i++)if(IsolationAllowsComponent(i))
            {if(next.Contains(logical[i]))Components.Add(i);else Components.Remove(i);}
        }
    }
}
