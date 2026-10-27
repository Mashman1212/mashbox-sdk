#if MASHBOX_STUDIO_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MashBoxSDK.AnimationStudio;

public static class StudioIsolationValidation
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    static AnimationStudioWindow host;
    static MashBoxStudioViewport view;
    static SceneView ordinary;
    static Color ordinaryBackground;
    static StudioModelDocument document;
    static double ready;
    static int phase, checks;
    static Color32[] baseline;
    static StudioModelEditor editor; static int scenario; static bool probed; static int selectedComponent; static int[] sourceTriangles; static int[] sourcePolygons; static System.Collections.Generic.HashSet<int> expectedPatchVertices; static int expectedPatchTriangles;
    static readonly Color Custom = new Color(.13f, .32f, .57f, 1);
    static string Output(string name) => Path.GetFullPath(Path.Combine(Application.dataPath, "../../" + name));
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    public static void Run() => EditorApplication.delayCall += Start;
    static void Start()
    {
        try
        {
            var global = Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings, Unity.RenderPipelines.HighDefinition.Runtime");
            global.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { true });
            var pipeline = (RenderPipelineAsset)ScriptableObject.CreateInstance(Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset, Unity.RenderPipelines.HighDefinition.Runtime"));
            AssetDatabase.CreateAsset(pipeline, AssetDatabase.GenerateUniqueAssetPath("Assets/BackgroundValidationPipeline.asset"));
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            document = ScriptableObject.CreateInstance<StudioModelDocument>();
            Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);
            document.SetWorkspacePath(Path.Combine(StudioModelDocument.WorkspaceFolder, "background-"+Guid.NewGuid().ToString("N")+".workspace"));
            for(int i=0;i<3;i++)
            {
                var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);
                document.nodes.Add(new StudioModelDocument.Node{name="Cube "+i,mesh=mesh,materials=new[]{StudioMeshMaterialPreview.DefaultMaterial},cutPolygons=polygons,cutTriangles=mesh.triangles,position=new Vector3((i-1)*2,0,0)});
            }
            AnimationStudioWindow.Open();
            host = Resources.FindObjectsOfTypeAll<AnimationStudioWindow>().First();
            typeof(AnimationStudioWindow).GetMethod("SwitchStudioMode", Members).Invoke(host, new object[] { true });
            typeof(AnimationStudioWindow).GetMethod("LoadStudioModel", Members).Invoke(host, new object[] { document });
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().First(v => v.Studio == host);
            editor=host.ToolbarModelEditor;
            editor.SetDisplayMode(true,false);
            sourceTriangles=document.nodes[0].mesh.triangles;
            view.modelGridVisible = false;
            view.position = new Rect(60,60,800,650);
            view.LookAtDirect(Vector3.zero, Quaternion.identity, 5);
            ready = EditorApplication.timeSinceStartup + 2;
            view.Repaint();
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error.ToString()); }
    }
    static Color32[] Pixels()
    {
        var texture = typeof(MashBoxStudioViewport).GetField("cachedScene", Members).GetValue(view) as RenderTexture;
        if (!texture || !texture.IsCreated()) return null;
        var previous = RenderTexture.active;
        var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);
            image.Apply();
            File.WriteAllBytes(Output("isolation-"+scenario+"-"+phase+".png"), image.EncodeToPNG());
            var result=image.GetPixels32();
            var overlay=typeof(MashBoxStudioViewport).GetField("cachedOverlay",Members).GetValue(view) as RenderTexture;
            if(overlay && overlay.width==texture.width && overlay.height==texture.height) { RenderTexture.active=overlay;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();File.WriteAllBytes(Output("isolation-overlay-"+scenario+"-"+phase+".png"),image.EncodeToPNG());if(phase==1 && scenario==1)Check(image.GetPixels32().Count(c=>c.a>100 && c.r>180 && c.g>90 && c.g<240 && c.b<100)>2,"Isolated GPU vertex marker absent from native overlay"); }
            return result;
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
    }

    static object Call(string name,params object[] args) { var method=typeof(StudioModelEditor).GetMethod(name,Members); var parameters=method.GetParameters(); var supplied=new object[parameters.Length]; for(int i=0;i<supplied.Length;i++)supplied[i]=i<args.Length?args[i]:parameters[i].DefaultValue; return method.Invoke(editor,supplied); }
    static void Select(StudioModelEditor.ComponentMode mode)
    {
        Call("SelectObject",0);
        Call("ChangeComponentMode",mode,null);
        editor.Components.Clear();
        selectedComponent=0;
        if(mode==StudioModelEditor.ComponentMode.Face)
        {
            var faces=(System.Collections.IList)Call("Faces");
            float best=float.PositiveInfinity;
            for(int i=0;i<faces.Count;i++)
            {
                var face=(StudioMeshCuts.Face)faces[i];
                float z=face.Vertices.Average(v=>document.nodes[0].mesh.vertices[v].z);
                if(z<best){best=z;selectedComponent=i;}
            }
        }
        if(mode!=StudioModelEditor.ComponentMode.Object)editor.Components.Add(selectedComponent);
    }
    static int VisibleSurfaces() => editor.Transforms.Count(t=>t.gameObject.activeInHierarchy && t.GetComponent<MeshFilter>().sharedMesh.triangles.Length>0);
    static void Probe(SceneView scene)
    {
        if(scene!=view || Event.current.type!=EventType.Repaint || probed || phase!=1 || scenario==0)return;
        try
        {
            var mesh=document.nodes[0].mesh;var points=mesh.vertices;Vector3 center;
            var mode=(StudioModelEditor.ComponentMode)scenario;
            if(mode==StudioModelEditor.ComponentMode.Vertex)center=points[selectedComponent];
            else if(mode==StudioModelEditor.ComponentMode.Edge)
            {
                var edges=(System.Collections.Generic.List<(int a,int b)>)Call("Edges");var edge=edges[selectedComponent];center=(points[edge.a]+points[edge.b])*.5f;
            }
            else
            {
                var faces=(System.Collections.Generic.List<StudioMeshCuts.Face>)Call("Faces");center=faces[selectedComponent].Vertices.Aggregate(Vector3.zero,(v,i)=>v+points[i])/faces[selectedComponent].Vertices.Count;
            }
            Vector2 pointer=HandleUtility.WorldToGUIPoint(editor.Transforms[0].TransformPoint(center));
            var find=typeof(StudioModelEditor).GetMethods(Members).Single(m=>m.Name=="FindComponentAt" && m.GetParameters().Length==2);
            Check((int)find.Invoke(editor,new object[]{view,pointer})==selectedComponent,"Visible isolated component not pickable in mode "+mode);
            Check((int)Call("MeshAtModePointer",view,pointer)==0,"Isolated component cannot acquire its mesh in mode "+mode);
            probed=true;
        }
        catch(Exception error){Finish(error.ToString());}
    }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<ready)return;
        try
        {
            var pixels=Pixels();if(pixels==null){view.Repaint();ready=EditorApplication.timeSinceStartup+.2;return;}
            if(phase==0)
            {
                if(scenario==0)
                {
                    foreach(string name in new[]{"viewportCameraButton","viewportLightingButton","viewportIsolateButton","viewportShowAllButton"})
                    {
                        var button=(UnityEngine.UIElements.IMGUIContainer)typeof(MashBoxStudioViewport).GetField(name,Members).GetValue(view);
                        Check(button!=null && button.onGUIHandler!=null,"Missing overlay action "+name);
                    }
                    baseline=pixels;
                    SceneView.duringSceneGui+=Probe;
                }
                Select((StudioModelEditor.ComponentMode)scenario);
                Check(editor.CanIsolateViewportSelection,"Valid selection cannot isolate");
                if(scenario==1)
                {
                    var selected=editor.SelectedVertices();expectedPatchVertices=new System.Collections.Generic.HashSet<int>(selected);
                    var faces=(System.Collections.Generic.List<StudioMeshCuts.Face>)Call("Faces");
                    expectedPatchTriangles=0;
                    foreach(var face in faces.Where(f=>f.Vertices.Any(selected.Contains)))
                    {expectedPatchVertices.UnionWith(face.Vertices);expectedPatchTriangles+=(face.Vertices.Count-2)*3;}
                    Check(expectedPatchTriangles>0 && expectedPatchTriangles<sourceTriangles.Length,"Fixture must distinguish a direct face patch from the whole island");
                }
                editor.RunViewportIsolation(false);
                Check(editor.Transforms[0].gameObject.activeInHierarchy && !editor.Transforms[1].gameObject.activeInHierarchy && !editor.Transforms[2].gameObject.activeInHierarchy,"Other objects remained visible");
                Check(document.nodes.All(n=>n.active),"Temporary visibility changed document flags");
                Check(document.nodes[0].mesh.triangles.SequenceEqual(sourceTriangles),"Isolation changed authored topology");
                Check(VisibleSurfaces()==(scenario==2?0:1),"Unexpected surface visibility");
                if(scenario==1)Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh.triangles.Length==expectedPatchTriangles,"Vertex isolation retained the wrong incident face patch");
                if(scenario==3)Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh.triangles.Length==6,"Face isolation did not retain exactly one quad");
                if(scenario>0)
                {
                    Check((bool)Call("IsolationAllowsComponent",selectedComponent),"Selected component hidden");
                    int other=scenario==1?Enumerable.Range(0,document.nodes[0].mesh.vertexCount).First(i=>!expectedPatchVertices.Contains(i)):selectedComponent==0?1:0;
                    Check(!(bool)Call("IsolationAllowsComponent",other),"Unrelated component visible");
                    if(scenario==1)foreach(int vertex in expectedPatchVertices)Check((bool)Call("IsolationAllowsComponent",vertex),"Incident face boundary vertex hidden");
                }
                phase=1;probed=false;view.Repaint();ready=EditorApplication.timeSinceStartup+1;return;
            }
            if(phase==1)
            {
                Check(scenario==0||probed,"Native component pick probe did not execute");
                Check(!pixels.SequenceEqual(baseline),"Isolated native scene image unchanged");
                if(scenario==1 || scenario==3)
                {
                    var mesh=document.nodes[0].mesh;var points=mesh.vertices;points[selectedComponent]+=Vector3.up*.2f;mesh.vertices=points;
                    Call("PositionsChanged",false,null,null);
                    Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh.vertices.SequenceEqual(points),"Isolated face preview did not follow source edit");
                }
                editor.RunViewportIsolation(true);
                Check(editor.Transforms.All(t=>t.gameObject.activeInHierarchy),"Show All left objects hidden");
                Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh==document.nodes[0].mesh,"Show All did not restore original mesh binding");
                Check(document.nodes[0].mesh.triangles.SequenceEqual(sourceTriangles),"Show All changed source topology");
                if(++scenario<4){phase=0;ready=EditorApplication.timeSinceStartup+.4;view.Repaint();return;}
                // Multiple component targets use independent masks.
                Select(StudioModelEditor.ComponentMode.Vertex);
                Call("ActivateComponentTarget",1,StudioModelEditor.ComponentMode.Vertex);editor.Components.Add(0);
                editor.RunViewportIsolation(false);
                Check(editor.Transforms[0].gameObject.activeInHierarchy&&editor.Transforms[1].gameObject.activeInHierarchy&&!editor.Transforms[2].gameObject.activeInHierarchy,"Multi-mesh component isolation lost a target");
                Call("PrepareIsolationTopologyEdit");
                Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh==document.nodes[0].mesh,"Topology tool retained an obsolete component proxy");
                Check(!editor.Transforms[2].gameObject.activeInHierarchy,"Topology preparation lost object isolation");
                editor.RunViewportIsolation(true);
                Call("SelectObject",0);Call("ChangeComponentMode",StudioModelEditor.ComponentMode.Vertex,null);editor.Components.Clear();
                Check(!editor.CanIsolateViewportSelection,"Empty component selection should disable isolation");
                Finish("PASS "+checks+" overlay binding, Object/Vertex/Edge/Face visibility, native picking/rendering, source edits/restoration and multi-mesh checks");
            }
        }
        catch(Exception error){Finish(error.ToString());}
    }
    static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        SceneView.duringSceneGui -= Probe;
        File.WriteAllText(Output("isolation-result.txt"), result);
        EditorApplication.delayCall += () =>
        {
            if (ordinary) ordinary.Close();
            if (view) view.Close();
            if (host) UnityEngine.Object.DestroyImmediate(host);
            double end=EditorApplication.timeSinceStartup+1;
            void Exit() { if(EditorApplication.timeSinceStartup<end)return;EditorApplication.update-=Exit;EditorApplication.Exit(result.StartsWith("PASS")?0:1); }
            EditorApplication.update+=Exit;
        };
    }
}
#endif
