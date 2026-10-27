#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MashBoxSDK.AnimationStudio;
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;

public static class StudioBevelRoundedValidation
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static AnimationStudioWindow host;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;
    static int checks,phase;static double ready;static string stage="setup";
    static Vector3[] original,preview,committed,rawBevel,rawBevelNormals,bevelSource;static object capturedPlan;static Color[] sourceColors;static Vector3[] sourceNormals;static Vector4[][] sourceUVs;static int[] triangles,polygons;static Color32[] before;
    static List<(string,Action)> plan;static int step;static object kindFlow,kindBevel;
    static string Output(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../"+name));
    static void Check(bool ok,string text){if(!ok)throw new Exception(stage+": "+text);checks++;}
    static object Call(string name,params object[] args)
    {var m=typeof(StudioModelEditor).GetMethods(F).Single(method=>{var pars=method.GetParameters();return method.Name==name&&pars.Length>=args.Length&&pars.Take(args.Length).Select((parameter,i)=>args[i]==null||parameter.ParameterType.IsInstanceOfType(args[i])).All(x=>x)&&pars.Skip(args.Length).All(parameter=>parameter.HasDefaultValue);});var p=m.GetParameters();var values=new object[p.Length];for(int i=0;i<p.Length;i++)values[i]=i<args.Length?args[i]:p[i].DefaultValue;return m.Invoke(editor,values);}
    static void Select(int node,params (int,int)[] pairs)
    {
        editor.SelectObject(node);Call("ChangeComponentMode",StudioModelEditor.ComponentMode.Edge,null);
        var edges=(List<(int a,int b)>)Call("Edges");editor.Components.Clear();
        foreach(var p in pairs){int id=edges.FindIndex(e=>(e.a==p.Item1&&e.b==p.Item2)||(e.a==p.Item2&&e.b==p.Item1));Check(id>=0,"Fixture authored edge missing");editor.Components.Add(id);}
        Call("EstablishSelectionBaseline");
    }
    static void Parameters(string tool,float influence,int iterations)
    {ToolChannelPane.SetFloatDefault(tool,"Influence",influence);ToolChannelPane.SetFloatDefault(tool,"Iterations",iterations);}
    static StudioModelDocument.Node Strip(bool seams=false)
    {
        var source=new List<Vector3>();float[] heights={0,.1f,1,.1f,0};
        for(int row=0;row<2;row++)for(int i=0;i<5;i++)source.Add(new Vector3(i-2,heights[i],row*.8f));
        var vertices=new List<Vector3>();var index=new List<int>();var metadata=new List<int>();
        if(!seams)vertices.AddRange(source);
        for(int i=0;i<4;i++)
        {
            int[] f={i,i+5,i+6,i+1};
            if(seams){int start=vertices.Count;vertices.AddRange(f.Select(j=>source[j]));f=new[]{start,start+1,start+2,start+3};}
            metadata.AddRange(new[]{i%2,4});metadata.AddRange(f);
        }
        var mesh=new Mesh{name="Edge flow strip"};mesh.SetVertices(vertices);mesh.subMeshCount=2;
        for(int sub=0;sub<2;sub++)
        {
            index.Clear();for(int i=sub;i<4;i+=2){int[] f=seams?new[]{i*4,i*4+1,i*4+2,i*4+3}:new[]{i,i+5,i+6,i+1};index.AddRange(new[]{f[0],f[1],f[2],f[0],f[2],f[3]});}mesh.SetTriangles(index,sub);
        }
        for(int channel=0;channel<8;channel++)mesh.SetUVs(channel,vertices.Select((v,i)=>new Vector4(i*.1f,channel*.2f,i*.01f,1)).ToList());
        mesh.colors=vertices.Select((v,i)=>new Color(i*.02f,.3f,.6f,1)).ToArray();mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
        return new StudioModelDocument.Node{name="Strip",mesh=mesh,cutPolygons=metadata.ToArray(),cutTriangles=mesh.triangles,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial,StudioMeshMaterialPreview.DefaultMaterial}};
    }
    static float Bend(Vector3[] points){float sum=0;for(int i=1;i+1<points.Length;i++)sum+=(points[i-1]-points[i]*2+points[i+1]).sqrMagnitude;return sum;}
    static void KernelChecks()
    {
        stage="path kernel";var path=new[]{new Vector3(-2,0,0),new Vector3(-1,0,0),new Vector3(0,1,0),new Vector3(1,0,0),new Vector3(2,0,0)};var saved=(Vector3[])path.Clone();
        foreach(float strength in new[]{0,.2f,.5f,1})foreach(int count in new[]{0,1,4,16})
        {
            var output=StudioEdgeFlow.Smooth(path,strength,count);
            Check(output.Length==path.Length&&output[0].Equals(path[0])&&output.Last().Equals(path.Last()),"Open endpoints/count changed");
            Check(path.SequenceEqual(saved)&&!ReferenceEquals(output,path),"Input mutated or returned by reference");
            if(strength>0&&count>0)Check(Bend(output)<Bend(path),"Bend energy did not decrease");else Check(output.SequenceEqual(path),"Zero smoothing changed the path");
        }
        var uneven=new[]{Vector3.zero,Vector3.right*.2f,Vector3.right*2,Vector3.right*5};
        Check(StudioEdgeFlow.Smooth(uneven,1,20).Zip(uneven,Vector3.Distance).Max()<1e-5f,"Uneven straight path drifted");
        var cycle=new[]{Vector3.right,Vector3.up,Vector3.left,Vector3.down,Vector3.right};var ring=StudioEdgeFlow.Smooth(cycle,.4f,2,true);
        Check(ring.Length==cycle.Length&&ring[0].Equals(ring.Last()),"Closed duplicate endpoint split");
        Check(StudioEdgeFlow.Smooth(Array.Empty<Vector3>()).Length==0,"Empty path failed");
        Check(StudioEdgeFlow.Smooth(new[]{Vector3.zero,Vector3.one},1,8).SequenceEqual(new[]{Vector3.zero,Vector3.one}),"Two fixed endpoints moved");
        bool invalid=false;try{StudioEdgeFlow.Smooth(new[]{new Vector3(float.NaN,0,0)});}catch(ArgumentException){invalid=true;}Check(invalid,"Invalid coordinates accepted");
        var huge=new[]{new Vector3(-1e25f,0,0),new Vector3(0,1e25f,0),new Vector3(1e25f,0,0)};
        Check(StudioEdgeFlow.Smooth(huge,1,8).All(StudioEdgeFlow.Finite),"Finite large-coordinate path overflowed");
    }
    static void SurfaceChecks()
    {
        stage="transverse surface flow";
        foreach(bool seams in new[]{false,true})
        {
            var node=Strip(seams);var source=node.mesh.vertices;var topology=new StudioMeshCuts(node);
            var edges=seams?new[]{(6,7)}:new[]{(2,7)};
            var plan=topology.CreateEdgeFlowPlan(node.mesh,edges);var output=plan.Evaluate(.833f,6);
            var reversed=topology.CreateEdgeFlowPlan(node.mesh,edges.Reverse().Select(e=>(e.Item2,e.Item1))).Evaluate(.833f,6);
            Check(output.SequenceEqual(reversed),"Selection reversal changed flow");
            Check(node.mesh.vertices.SequenceEqual(source),"Source mutated");
            for(int i=0;i<source.Length;i++)
            {
                bool selected=source[i].x==0;
                if(selected){Check(output[i].y<source[i].y,"Peak did not follow transverse curvature");Check(output[i].x==source[i].x&&output[i].z==source[i].z,"Selected edge shortened or slid along itself");}
                else Check(output[i].Equals(source[i]),"An unselected side vertex moved");
            }
            foreach(var group in Enumerable.Range(0,source.Length).GroupBy(i=>source[i]))Check(group.Select(i=>output[i]).Distinct().Count()==1,"Seam copies separated");
            Check(plan.Evaluate(0,6).SequenceEqual(source),"Zero influence did not restore source");
            Check(plan.Evaluate(1,0).SequenceEqual(source),"Zero iterations did not restore source");
            Check(topology.CreateEdgeFlowPlan(node.mesh,seams?new[]{(0,3)}:new[]{(0,1)}).Evaluate(1,6).SequenceEqual(source),"Boundary edge fell back to pinching along its length");
            UnityEngine.Object.DestroyImmediate(node.mesh);
        }
        var corner=new[]{new Vector3(-1,-1),new Vector3(-1,0),new Vector3(-.5f,.5f),new Vector3(0,1),new Vector3(1,1)};
        var valley=new[]{new Vector3(-2,0),new Vector3(-1,0),new Vector3(0,-.7f),new Vector3(1,0),new Vector3(2,0)};
        foreach(var profile in new[]{corner,valley})foreach(int transform in new[]{0,1,2})
        {
            var node=Strip();var source=node.mesh.vertices;
            var rotation=transform==0?Quaternion.identity:Quaternion.Euler(27*transform,51,-18);float scale=transform==2?7:.6f;
            for(int row=0;row<2;row++)for(int i=0;i<5;i++)source[i+row*5]=rotation*(profile[i]+Vector3.forward*row)*scale;
            node.mesh.vertices=source;node.mesh.RecalculateNormals();
            var topology=new StudioMeshCuts(node);var plan=topology.CreateEdgeFlowPlan(node.mesh,new[]{(2,7)});var output=plan.Evaluate(1,1);
            for(int i=0;i<10;i++)if(i!=2&&i!=7)Check(output[i].Equals(source[i]),"Fixed transverse support moved");
            Vector3 local=Quaternion.Inverse(rotation)*(output[2]/scale);
            if(ReferenceEquals(profile,corner))Check(local.x<-.55f&&local.y>.55f,"Convex profile did not fit outward");
            else Check(Mathf.Abs(local.y)<1e-5f,"Planar surrounding tangents did not flatten the valley");
            Check(Vector3.Distance(output[7]-output[2],source[7]-source[2])<1e-5f,"Flow narrowed or skewed selected edge");
            foreach(float strength in new[]{.25f,.7f,1f})foreach(int passes in new[]{1,4,64})
            {
                var positive=plan.Evaluate(strength,passes);var negative=plan.Evaluate(-strength,passes);
                for(int i=0;i<source.Length;i++)Check(Vector3.Distance(negative[i]-source[i],source[i]-positive[i])<1e-5f,"Negative influence was not symmetric or moved an unselected support");
            }
            var partial=plan.Evaluate(.3f,1);Check(Vector3.Distance(partial[2],source[2])<Vector3.Distance(output[2],source[2]),"Influence did not blend toward fit");
            var locked=topology.CreateEdgeFlowPlan(node.mesh,new[]{(2,7)},true,new HashSet<Vector3>{source[2]}).Evaluate(1,1);
            Check(locked[2].Equals(source[2]),"Protected bevel source coordinate moved");
            UnityEngine.Object.DestroyImmediate(node.mesh);
        }
    }
    static void RoundChecks()
    {
        stage="three-edge circular fillet";
        foreach(int corner in Enumerable.Range(0,8))foreach(float width in new[]{.03f,.087f,.16f})foreach(int subdiv in new[]{1,2,4,8})foreach(bool rotate in new[]{false,true})
        {
            var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polys);
            try
            {
                var scale=rotate?new Vector3(1.3f,.8f,1.1f):Vector3.one;
                var rotation=rotate?Quaternion.Euler(21,37,-18):Quaternion.identity;var inverse=Quaternion.Inverse(rotation);
                var signs=new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1);
                var sourceCorner=Vector3.Scale(signs,scale)*.5f;var worldCorner=rotation*sourceCorner;
                mesh.vertices=mesh.vertices.Select(p=>rotation*Vector3.Scale(p,scale)).ToArray();mesh.RecalculateNormals();
                var node=new StudioModelDocument.Node{mesh=mesh,cutPolygons=polys,cutTriangles=mesh.triangles};var topology=new StudioMeshCuts(node);
                var selected=new List<(int,int)>();var seen=new HashSet<(Vector3,Vector3)>();
                foreach(var f in topology.Faces)for(int i=0;i<f.Vertices.Count;i++)
                {
                    int a=f.Vertices[i],b=f.Vertices[(i+1)%f.Vertices.Count];
                    if(Vector3.Distance(topology.Positions[a],worldCorner)>1e-5f&&Vector3.Distance(topology.Positions[b],worldCorner)>1e-5f)continue;
                    if(seen.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(topology.Positions[a],topology.Positions[b])))selected.Add((a,b));
                }
                Check(selected.Count==3,"Fixture did not select the three meeting cube edges");
                topology.BevelEdges(node,selected,new StudioMeshCuts.BevelOptions{Width=width,Subdivisions=subdiv,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});
                var center=sourceCorner-signs*width;int patchPoints=0;
                var vertices=mesh.vertices;var normals=mesh.normals;
                for(int i=0;i<vertices.Length;i++)
                {
                    Vector3 p=inverse*vertices[i];Vector3 depth=Vector3.Scale(sourceCorner-p,signs);
                    if(depth.x< -1e-5f||depth.y< -1e-5f||depth.z< -1e-5f||depth.x>width+1e-5f||depth.y>width+1e-5f||depth.z>width+1e-5f)continue;
                    float error=Mathf.Abs((p-center).magnitude-width)/width;
                    Check((p-center).magnitude<=width*(1.0002f),"Corner patch protruded outside its boundary fillet envelope");
                    Check(Vector3.Dot(normals[i].normalized,(rotation*(p-center)).normalized)>.95f,"Patch normal reversed or broke surface orientation");patchPoints++;
                }
                Check(patchPoints>6,"No generated corner points were measured");
                if(subdiv==1)
                {
                    var centerCandidates=vertices.Select(p=>inverse*p).Where(p=>{var d=Vector3.Scale(sourceCorner-p,signs);return d.x>width*.1f&&d.y>width*.1f&&d.z>width*.1f&&d.x<width&&d.y<width&&d.z<width&&Mathf.Abs(d.x-d.y)<width*1e-4f&&Mathf.Abs(d.y-d.z)<width*1e-4f;}).ToArray();
                    double w=Math.Sqrt(.5),depth=(2+2*w)/(3+6*w);
                    Check(centerCandidates.Length>0&&centerCandidates.All(p=>Mathf.Abs(Vector3.Scale(sourceCorner-p,signs).x-width*(float)depth)<width*2e-4f),"One-subdivision center did not follow the boundary control net");
                }
                var result=new StudioMeshCuts(node);var incidence=new Dictionary<(Vector3,Vector3),int>();
                foreach(var face in result.Faces)for(int i=0;i<face.Vertices.Count;i++)
                {var key=StudioMeshCuts.NormalEdgePositionKeyForBevel(result.Positions[face.Vertices[i]],result.Positions[face.Vertices[(i+1)%face.Vertices.Count]]);incidence[key]=incidence.TryGetValue(key,out int count)?count+1:1;}
                Check(incidence.Values.All(n=>n==2),"Rounded junction introduced a boundary/crack");
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
    }
    static void LegacyCornerChecks()
    {
        stage="corner topology and conic symmetry";
        var make=typeof(StudioThreeEdgeCornerValidation).GetMethod("Make",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(int corner in Enumerable.Range(0,8))foreach(int transform in new[]{0,1,2})foreach(float width in new[]{.04f,.12f,.2f})foreach(int sub in new[]{0,1,3,8})foreach(bool reverse in new[]{false,true})
        {
            make.Invoke(null,new object[]{new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1),transform,width,sub,true,reverse});
            Check(true,"Legacy flat/rounded corner connectivity matrix");
        }
        var sample=typeof(StudioMeshCuts).GetMethod("ClipProfileSample",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(float angle in new[]{30,60,90,120,150})foreach(int steps in new[]{2,3,5,9})for(int k=0;k<=steps;k++)foreach(float tension in new[]{-1,0,.5f,1})
        {
            Vector3 a=Vector3.right,b=new Vector3(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad),0),control=Vector3.zero;
            var point=(Vector3)sample.Invoke(null,new object[]{a,b,control,k,steps,tension});
            var reverse=(Vector3)sample.Invoke(null,new object[]{b,a,control,steps-k,steps,tension});
            Check(point.Equals(reverse),"Reversing a shared profile changed its published point");
            if(tension==1){var center=(a+b)/(1+Vector3.Dot(a,b));float radius=(a-center).magnitude;Check(Mathf.Abs((point-center).magnitude-radius)<1e-5f,"Equal-tangent conic was not circular");}
        }
    }
    public static void Run()=>EditorApplication.delayCall+=Start;
    static void Start()
    {
        try
        {
            KernelChecks();SurfaceChecks();RoundChecks();LegacyCornerChecks();
            var global=Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings, Unity.RenderPipelines.HighDefinition.Runtime");global.GetMethod("Ensure",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{true});
            var pipeline=(RenderPipelineAsset)ScriptableObject.CreateInstance(Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset, Unity.RenderPipelines.HighDefinition.Runtime"));AssetDatabase.CreateAsset(pipeline,AssetDatabase.GenerateUniqueAssetPath("Assets/EdgeFlowPipeline.asset"));GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);doc.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder,"edge-flow-"+Guid.NewGuid().ToString("N")+".workspace"));
            doc.nodes.Add(Strip());var second=Strip();second.position=Vector3.right*6;doc.nodes.Add(second);
            var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);doc.nodes.Add(new StudioModelDocument.Node{name="Cube",position=Vector3.right*9,mesh=mesh,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial},cutPolygons=polygons,cutTriangles=mesh.triangles});
            AnimationStudioWindow.Open();host=Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode",F).Invoke(host,new object[]{true});typeof(AnimationStudioWindow).GetMethod("LoadStudioModel",F).Invoke(host,new object[]{doc});editor=host.ToolbarModelEditor;
            view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v=>v.Studio==host);view.position=new Rect(60,60,1000,720);view.modelGridVisible=false;view.LookAtDirect(new Vector3(0,.4f,.4f),Quaternion.Euler(35,0,0),3.5f);
            editor.SetPivotLocked(false);editor.SetDisplayMode(true,false);editor.EditLighting(s=>s.studioLights=false);StudioSettings.Current.sceneBackground=new Color(.16f,.16f,.16f,1);StudioSettings.Save();
            var type=typeof(StudioModelEditor).GetNestedType("LiveTool",BindingFlags.NonPublic);kindFlow=Enum.Parse(type,"EdgeFlow");kindBevel=Enum.Parse(type,"Bevel");
            Select(0,(2,7));original=editor.ActiveMesh.vertices;sourceNormals=editor.ActiveMesh.normals;sourceColors=editor.ActiveMesh.colors;sourceUVs=new Vector4[8][];for(int c=0;c<8;c++){var uv=new List<Vector4>();editor.ActiveMesh.GetUVs(c,uv);sourceUVs[c]=uv.ToArray();}triangles=editor.ActiveMesh.triangles;polygons=doc.nodes[0].cutPolygons.ToArray();
            plan=new List<(string,Action)>();void Step(string name,Action run)=>plan.Add((name,run));
            Step("menu registration",()=>
            {
                Check(StudioModelEditor.ModelMenuTitles.Contains("Edit Mesh"),"Edit Mesh category missing");
                var command=editor.ToolbarCommands(view).Single(c=>c.Path=="Edit Mesh/Edit Edge Flow…");Check(command.Enabled,"Selected-edge command disabled");
                var id=StudioModelCommands.Register("Edit Mesh","Edit Edge Flow…").Value;Check(StudioShortcuts.Definitions.Any(d=>d.id==id&&d.group=="Edit Mesh"),"Shortcut discovery omitted the tool");
                before=Pixels("edge-flow-before.png");Parameters("Edit Edge Flow",.5f,4);command.Execute();
            });
            Step("native live preview",()=>
            {
                preview=editor.ActiveMesh.vertices;Check(!preview.SequenceEqual(original),"Preview did not deform edges");
                Check(editor.ActiveMesh.triangles.SequenceEqual(triangles)&&doc.nodes[0].cutPolygons.SequenceEqual(polygons),"Preview changed topology");
                Check(editor.ActiveMesh.GetVertexAttributeDimension(VertexAttribute.TexCoord7)==4,"UV dimensions were changed");
                Check(editor.Components.Count==1,"Preview lost edge selection");
                Check(editor.ActiveMesh.colors.SequenceEqual(sourceColors),"Vertex colors changed");
                for(int c=0;c<8;c++){var uv=new List<Vector4>();editor.ActiveMesh.GetUVs(c,uv);Check(uv.SequenceEqual(sourceUVs[c]),"UV data changed in channel "+c);}
                capturedPlan=((StudioMeshToolPreview)typeof(StudioModelEditor).GetField("livePreview",F).GetValue(editor)).Targets[0].EdgeFlowPlan;
                var pixels=Pixels("edge-flow-preview.png");Check(before!=null&&pixels!=null&&before.Zip(pixels,(a,b)=>a.Equals(b)?0:1).Sum()>50,"Native viewport did not display the smoothed mesh");
                Parameters("Edit Edge Flow",-.5f,4);Call("EvaluateLiveTool",kindFlow);
                var inverted=editor.ActiveMesh.vertices;
                for(int i=0;i<inverted.Length;i++)Check(Vector3.Distance(inverted[i]-original[i],original[i]-preview[i])<1e-5f,"Native negative preview did not reverse positive flow");
                Parameters("Edit Edge Flow",.8f,8);Call("EvaluateLiveTool",kindFlow);
            });
            Step("source based preview",()=>
            {
                Parameters("Edit Edge Flow",.5f,4);Call("EvaluateLiveTool",kindFlow);Check(editor.ActiveMesh.vertices.SequenceEqual(preview),"Slider reevaluation accumulated smoothing");Check(ReferenceEquals(capturedPlan,((StudioMeshToolPreview)typeof(StudioModelEditor).GetField("livePreview",F).GetValue(editor)).Targets[0].EdgeFlowPlan),"Slider rebuilt the immutable edge graph");
                Parameters("Edit Edge Flow",0,4);Call("EvaluateLiveTool",kindFlow);Check(editor.ActiveMesh.vertices.SequenceEqual(original)&&editor.ActiveMesh.normals.SequenceEqual(sourceNormals),"Zero influence did not restore the exact source");
                Parameters("Edit Edge Flow",.5f,4);Call("EvaluateLiveTool",kindFlow);Call("CommitLiveTool",true);committed=editor.ActiveMesh.vertices;
            });
            Step("Undo flow",()=>Undo.PerformUndo());
            Step("check flow Undo",()=>{Check(editor.ActiveMesh.vertices.SequenceEqual(original),"Undo did not restore original positions");Undo.PerformRedo();});
            Step("check flow Redo",()=>
            {
                Check(editor.ActiveMesh.vertices.SequenceEqual(committed),"Redo did not restore smoothing");
                Parameters("Edit Edge Flow",.7f,6);Call("EvaluateLiveTool",kindFlow);Call("CancelLiveTool",false);
                Check(editor.ActiveMesh.vertices.SequenceEqual(committed),"Cancel did not restore the committed source");
                Select(2,(0,1));bevelSource=editor.ActiveMesh.vertices;ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.15f);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",2);ToolChannelPane.SetFloatDefault("Bevel Edges","EdgeFlow",0);Call("EvaluateLiveTool",kindBevel);rawBevel=editor.ActiveMesh.vertices;rawBevelNormals=editor.ActiveMesh.normals;
            });
            Step("bevel flow toggle",()=>
            {
                var rawTriangles=editor.ActiveMesh.triangles;ToolChannelPane.SetFloatDefault("Bevel Edges","EdgeFlow",1);ToolChannelPane.SetFloatDefault("Bevel Edges","FlowInfluence",.3f);ToolChannelPane.SetFloatDefault("Bevel Edges","FlowIterations",2);Call("EvaluateLiveTool",kindBevel);
                Check(!editor.ActiveMesh.vertices.SequenceEqual(rawBevel),"Enabled bevel flow did not adjust the rail");
                var flowed=editor.ActiveMesh.vertices;
                ToolChannelPane.SetFloatDefault("Bevel Edges","FlowInfluence",-.3f);Call("EvaluateLiveTool",kindBevel);
                var negativeBevel=editor.ActiveMesh.vertices;
                for(int i=0;i<rawBevel.Length;i++)Check(Vector3.Distance(negativeBevel[i]-rawBevel[i],rawBevel[i]-flowed[i])<1e-5f,"Bevel negative flow was skipped or not symmetric");
                for(int i=0;i<rawBevel.Length;i++)if(bevelSource.Contains(rawBevel[i]))Check(flowed[i].Equals(rawBevel[i]),"Bevel flow moved an unchanged source boundary");
                Check(editor.ActiveMesh.vertexCount==rawBevel.Length&&editor.ActiveMesh.triangles.SequenceEqual(rawTriangles),"Bevel smoothing changed connectivity");
                ToolChannelPane.SetFloatDefault("Bevel Edges","EdgeFlow",0);Call("EvaluateLiveTool",kindBevel);Check(editor.ActiveMesh.vertices.SequenceEqual(rawBevel)&&editor.ActiveMesh.normals.SequenceEqual(rawBevelNormals),"Bevel off/on/off failed exact restoration");
                Call("CancelLiveTool",false);
            });
            Step("multi-mesh flow",()=>
            {
                Select(0,(2,7));Call("ActivateComponentTarget",1,StudioModelEditor.ComponentMode.Edge);
                var edges=(List<(int a,int b)>)Call("Edges");editor.Components.Add(edges.FindIndex(e=>e.a==2&&e.b==7||e.a==7&&e.b==2));
                var a=doc.nodes[0].mesh.vertices;var b=doc.nodes[1].mesh.vertices;Parameters("Edit Edge Flow",.4f,3);Call("EvaluateLiveTool",kindFlow);
                Check(!doc.nodes[0].mesh.vertices.SequenceEqual(a)&&!doc.nodes[1].mesh.vertices.SequenceEqual(b),"Multi-object preview skipped a mesh");Call("CancelLiveTool",false);
                Check(doc.nodes[0].mesh.vertices.SequenceEqual(a)&&doc.nodes[1].mesh.vertices.SequenceEqual(b),"Multi-object cancel did not restore both sources");
            });
            Step("native three-edge corner one subdivision",()=>
            {
                Select(2,(0,1));var positions=editor.ActiveMesh.vertices;var edges=(List<(int a,int b)>)Call("Edges");editor.Components.Clear();
                var selectedCornerEdges=new HashSet<(Vector3,Vector3)>();
                for(int i=0;i<edges.Count;i++)if((positions[edges[i].a]==new Vector3(.5f,.5f,-.5f)||positions[edges[i].b]==new Vector3(.5f,.5f,-.5f))&&selectedCornerEdges.Add(StudioMeshCuts.NormalEdgePositionKeyForBevel(positions[edges[i].a],positions[edges[i].b])))editor.Components.Add(i);
                Check(editor.Components.Count==3,"Native three-edge selection missing");Call("EstablishSelectionBaseline");
                ToolChannelPane.SetFloatDefault("Bevel Edges","Width",.087f);ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",1);ToolChannelPane.SetFloatDefault("Bevel Edges","Tension",1);ToolChannelPane.SetFloatDefault("Bevel Edges","EdgeFlow",0);Call("EvaluateLiveTool",kindBevel);
                view.LookAtDirect(new Vector3(9.3f,.3f,-.3f),Quaternion.Euler(25,-35,0),1.15f);
            });
            Step("capture one subdivision",()=>{Check(Pixels("bevel-rounded-one.png")!=null,"Missing native one-subdivision render");ToolChannelPane.SetFloatDefault("Bevel Edges","Subdivisions",4);Call("EvaluateLiveTool",kindBevel);});
            Step("capture four subdivisions",()=>{Check(Pixels("bevel-rounded-four.png")!=null,"Missing native four-subdivision render");Call("CancelLiveTool",false);});
            ready=EditorApplication.timeSinceStartup+2;EditorApplication.update+=Tick;
        }
        catch(Exception error){Finish(error.ToString());}
    }
    static Color32[] Pixels(string name)
    {
        var rt=typeof(MashBoxStudioViewport).GetField("cachedScene",F).GetValue(view) as RenderTexture;if(!rt||!rt.IsCreated())return null;
        var previous=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
        try{RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Output(name),image.EncodeToPNG());return image.GetPixels32();}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
    }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<ready)return;
        try
        {
            if(step==plan.Count){Finish("PASS "+checks+" rounded fillet radius/normals, 576 corner topology controls, conic symmetry, native preview/history and signed Edge Flow checks");return;}
            UnityEditor.Undo.FlushUndoRecordObjects();stage=plan[step].Item1;plan[step++].Item2();view.Repaint();ready=EditorApplication.timeSinceStartup+.6;
        }
        catch(Exception error){Finish(error.ToString());}
    }
    static void Finish(string text)
    {
        EditorApplication.update-=Tick;File.WriteAllText(Output("bevel-rounded-result.txt"),text);
        EditorApplication.delayCall+=()=>{if(view)view.Close();if(host)UnityEngine.Object.DestroyImmediate(host);double stop=EditorApplication.timeSinceStartup+1;void Exit(){if(EditorApplication.timeSinceStartup<stop)return;EditorApplication.update-=Exit;EditorApplication.Exit(text.StartsWith("PASS")?0:1);}EditorApplication.update+=Exit;};
    }
}
#endif
