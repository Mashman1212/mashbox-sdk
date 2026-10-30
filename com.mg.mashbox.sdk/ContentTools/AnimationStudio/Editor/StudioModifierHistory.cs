using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // Portable construction history. No Unity object/session identities are saved.
    [Serializable] public sealed class StudioModifierSource
    {
        public string mesh;
        public int[] polygons, geometry, normals;
        internal static StudioModifierSource Capture(StudioModelDocument.Node node) => new StudioModifierSource {
            mesh=StudioWorkspaceText.CaptureModifierMesh(node.mesh),polygons=node.cutPolygons?.ToArray(),
            geometry=StudioSubdivisionData.Geometry(node).ToArray(),normals=node.normalGroups?.ToArray() };
        internal StudioModelDocument.Node Restore()
        {
            var result=StudioWorkspaceText.RestoreModifierMesh(mesh);result.hideFlags=HideFlags.HideAndDontSave;
            return new StudioModelDocument.Node{mesh=result,cutPolygons=polygons?.ToArray(),cutTriangles=result.triangles,
                geometryVertices=geometry?.ToArray(),normalGroups=normals?.ToArray()};
        }
    }
    [Serializable] public sealed class StudioModifierOperation
    {
        public string id=Guid.NewGuid().ToString("N"),kind,name;
        public bool enabled=true,weld=true;
        public Vector4 parameters;
        public Vector3 flow;
        public Matrix4x4 world=Matrix4x4.identity;
        public int[] faces,edges,vertices,inputPolygons,inputGeometry;
        public int inputCount;
        public StudioSubdivisionSettings subdivision;
        public StudioModifierSource replacement;
        public string inputMesh;
        public Vector3[] offsets;
        public string failure;
        internal void Bind(StudioModelDocument.Node node)
        {inputCount=node.mesh.vertexCount;inputPolygons=node.cutPolygons?.ToArray();inputGeometry=StudioSubdivisionData.Geometry(node).ToArray();}
        internal bool Matches(StudioModelDocument.Node node) => inputCount==node.mesh.vertexCount &&
            (inputPolygons??Array.Empty<int>()).SequenceEqual(node.cutPolygons??Array.Empty<int>()) &&
            (inputGeometry??Array.Empty<int>()).SequenceEqual(StudioSubdivisionData.Geometry(node));
    }
    [Serializable] public sealed class StudioModifierHistory
    {
        public StudioModifierSource source;
        public List<StudioModifierOperation> operations=new List<StudioModifierOperation>();
        // The input to a terminal Subdivision is retained for GPU rendering. The
        // editable mesh is its evaluated result, so later tools address that result.
        public StudioModifierSource displaySource;
        public StudioSubdivisionSettings displaySettings;
        public int revision;
        public StudioModifierHistory Copy()=>JsonUtility.FromJson<StudioModifierHistory>(JsonUtility.ToJson(this));
        internal bool HasHistory=>source!=null&&!string.IsNullOrEmpty(source.mesh)&&operations!=null&&operations.Count>0;
        internal bool HasDisplay=>displaySource!=null&&!string.IsNullOrEmpty(displaySource.mesh)&&displaySettings!=null&&displaySettings.preview&&displaySettings.level>0;
    }
    internal static class StudioModifierEvaluator
    {
        internal static void Publish(StudioModelDocument.Node from,StudioModelDocument.Node to)
        {
            StudioMeshToolPreview.CopyMesh(from.mesh,to.mesh);to.cutPolygons=from.cutPolygons?.ToArray();to.cutTriangles=to.mesh.triangles;
            to.geometryVertices=from.geometryVertices?.ToArray();to.normalGroups=from.normalGroups?.ToArray();
        }
        internal static StudioModelDocument.Node Evaluate(StudioModifierHistory history)
        {
            if(history?.source==null)throw new InvalidOperationException("Modifier history has no source mesh.");
            var work=history.source.Restore();history.displaySource=null;history.displaySettings=null;
            foreach(var operation in history.operations)operation.failure=null;
            for(int i=0;i<history.operations.Count;i++)
            {
                var operation=history.operations[i];if(!operation.enabled)continue;
                // Evaluate each operation into a private mesh. Failed operations
                // cannot partly overwrite the last valid evaluated result.
                var trial=new StudioModelDocument.Node {mesh=UnityEngine.Object.Instantiate(work.mesh),cutPolygons=work.cutPolygons,
                    cutTriangles=work.cutTriangles,geometryVertices=work.geometryVertices,normalGroups=work.normalGroups};
                try
                {
                    if(operation.kind!="Subdivision"&&!operation.Matches(work))
                        throw new InvalidOperationException("The input topology changed. Delete this operation or restore the earlier topology.");
                    Apply(trial,operation);
                    history.displaySource=operation.kind=="Subdivision"?StudioModifierSource.Capture(work):null;
                    history.displaySettings=operation.kind=="Subdivision"?operation.subdivision.Copy():null;
                    UnityEngine.Object.DestroyImmediate(work.mesh);work=trial;
                }
                catch(Exception e)
                {
                    UnityEngine.Object.DestroyImmediate(trial.mesh);operation.failure=e.Message;
                    for(int j=i+1;j<history.operations.Count;j++)history.operations[j].failure="Blocked by "+operation.name+".";
                    break;
                }
            }
            history.revision++;return work;
        }
        internal static void Apply(StudioModelDocument.Node node,StudioModifierOperation operation)
        {
            var p=operation.parameters;
            foreach(float value in new[]{p.x,p.y,p.z,p.w,operation.flow.x,operation.flow.y,operation.flow.z})
                if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidOperationException("Modifier values must be finite.");
            var topology=new StudioMeshCuts(node);
            var edges=new List<(int,int)>();
            if(operation.edges!=null)for(int i=0;i+1<operation.edges.Length;i+=2)edges.Add((operation.edges[i],operation.edges[i+1]));
            switch(operation.kind)
            {
                case "Bevel":
                    if(p.x<=0)return;
                    var bevel=topology.BevelEdges(node,edges,new StudioMeshCuts.BevelOptions {Width=p.x,Subdivisions=Mathf.Clamp(Mathf.RoundToInt(p.y),0,32),Tension=p.z,Junction=(StudioMeshCuts.BevelJunction)Mathf.RoundToInt(p.w)});
                    if(bevel==null)throw new InvalidOperationException("The saved edges cannot be beveled.");
                    if(operation.flow.x>=.5f&&p.y>0&&bevel.RenderSelection.Count>0)
                    {
                        var flow=new StudioMeshCuts(node).CreateEdgeFlowPlan(node.mesh,bevel.RenderSelection,true,new HashSet<Vector3>(topology.Positions));
                        SetPositions(node,flow.Evaluate(operation.flow.y,Mathf.Clamp(Mathf.RoundToInt(operation.flow.z),1,64)));
                    }
                    break;
                case "Extrude":
                    if(Mathf.Abs(p.x)<1e-5f&&Mathf.Abs(p.z)<1e-5f)return;
                    topology.Extrude(node,operation.faces!=null&&operation.faces.Length>0?operation.faces:null,operation.edges==null||operation.edges.Length==0?null:edges.ToArray(),p.x,p.y>=.5f,operation.weld,inset:p.z);node.normalGroups=null;break;
                case "Bridge":topology.BridgeEdges(node,edges,Mathf.Clamp(Mathf.RoundToInt(p.x),0,64),Mathf.Clamp(p.y,-1,1));break;
                case "Merge":topology.MergeVertices(node,operation.vertices,true,p.x,operation.world);break;
                case "Soften":topology.SetEdgeNormals(node,new HashSet<(int,int)>(edges),p.x,false);break;
                case "EdgeFlow":SetPositions(node,topology.CreateEdgeFlowPlan(node.mesh,edges,operation.weld).Evaluate(p.x,Mathf.Clamp(Mathf.RoundToInt(p.y),1,64)));break;
                case "Subdivision":
                    node.subdivision=operation.subdivision.Copy();
                    if(node.subdivision.level==0)return;
                    var input=new StudioSubdivisionData.Input(node);var evaluated=input.CreateMesh(node,true,out var polygons);
                    try{StudioMeshToolPreview.CopyMesh(evaluated,node.mesh);node.cutPolygons=polygons;node.cutTriangles=node.mesh.triangles;node.geometryVertices=input.Plan.Corners.ToArray();node.normalGroups=null;}
                    finally{UnityEngine.Object.DestroyImmediate(evaluated);}break;
                case "Transform":
                    var positions=node.mesh.vertices;
                    if(operation.offsets==null||operation.offsets.Length!=positions.Length)throw new InvalidOperationException("The transformed vertices no longer exist.");
                    for(int i=0;i<positions.Length;i++)positions[i]+=operation.offsets[i];SetPositions(node,positions);break;
                case "Direct":
                    if(StudioWorkspaceText.CaptureModifierMesh(node.mesh)!=operation.inputMesh)
                        throw new InvalidOperationException("This direct edit requires its original input geometry. Restore earlier values or delete this edit.");
                    var replacement=operation.replacement.Restore();try{Publish(replacement,node);}finally{UnityEngine.Object.DestroyImmediate(replacement.mesh);}break;
                default:throw new InvalidOperationException("Unknown modifier operation: "+operation.kind);
            }
        }
        private static void SetPositions(StudioModelDocument.Node node,Vector3[] points)
        {
            node.mesh.vertices=points;node.mesh.RecalculateNormals();
            var groups=node.normalGroups;
            if(groups!=null&&groups.Length==points.Length&&groups.Length>0)
            {
                var normals=node.mesh.normals;var sums=new Vector3[groups.Max()+1];
                for(int i=0;i<groups.Length;i++)sums[groups[i]]+=normals[i];
                for(int i=0;i<sums.Length;i++)if(sums[i].sqrMagnitude>1e-20f)sums[i].Normalize();
                for(int i=0;i<groups.Length;i++)if(sums[groups[i]].sqrMagnitude>.5f)normals[i]=sums[groups[i]];
                node.mesh.normals=normals;
            }
            if(node.mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))node.mesh.RecalculateTangents();node.mesh.RecalculateBounds();
        }
    }
}
