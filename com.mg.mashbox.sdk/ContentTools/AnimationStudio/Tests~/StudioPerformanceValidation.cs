using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;
public static class StudioPerformanceValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static AnimationStudioWindow controller;
    static MashBoxStudioViewport view;
    static StudioModelDocument doc, large;
    static StudioModelEditor editor, largeEditor;
    static Mesh mesh;
    static double next, start, cameraMs, hierarchyMs;
    static int renders;
    static bool profiling;
    static string Phase => Environment.GetCommandLineArgs().Contains("-studioPerfAfter") ? "after" : "before";
    public static void Run() { EditorApplication.delayCall += Begin; }
    static void Begin()
    {
        try
        {
            StudioSettings.Current.viewportVSync = false; StudioSettings.Current.disableViewportImageCache = true; StudioSettings.Current.showWorkspaceFps = false;
            controller = ScriptableObject.CreateInstance<AnimationStudioWindow>();
            typeof(AnimationStudioWindow).GetField("modelingMode",Flags).SetValue(controller,true);
            mesh = StudioModelDocument.BuildPrimitive(PrimitiveType.Cube);
            doc = ScriptableObject.CreateInstance<StudioModelDocument>(); doc.nodes.Add(new StudioModelDocument.Node { name="Render cube",mesh=mesh });
            typeof(AnimationStudioWindow).GetField("modelDocument",Flags).SetValue(controller,doc);
            typeof(AnimationStudioWindow).GetMethod("OpenViewport",Flags).Invoke(controller,null);
            view = Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==controller);
            view.position = new Rect(0,0,1683,800); view.pivot = Vector3.zero; view.rotation = Quaternion.Euler(15,135,0); view.size = 3;
            editor = (StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",Flags).GetValue(controller);
            large = ScriptableObject.CreateInstance<StudioModelDocument>();
            for(int i=0;i<512;i++) large.nodes.Add(new StudioModelDocument.Node { name="Hierarchy "+i, parent=i%8==0?-1:i-i%8,mesh=mesh });
            largeEditor = new StudioModelEditor(()=>{}); largeEditor.Load(large);
            next = EditorApplication.timeSinceStartup+4; SceneView.duringSceneGui += Observe; EditorApplication.update += Tick;
        }
        catch(Exception error) { Finish(error); }
    }
    static void Observe(SceneView scene) { if(scene==view && Event.current.type==EventType.Repaint && start>0) renders++; }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next) return;
        try
        {
            if(start==0)
            {
                editor.ConfigureLightingCamera(view);
                var watch=Stopwatch.StartNew();
                for(int i=0;i<2000;i++) editor.ConfigureLightingCamera(view);
                watch.Stop(); cameraMs=watch.Elapsed.TotalMilliseconds;
                var rows=typeof(StudioModelEditor).GetMethod("HierarchyRows",Flags);
                rows.Invoke(largeEditor,null); watch.Restart();
                for(int i=0;i<2000;i++) rows.Invoke(largeEditor,null);
                watch.Stop(); hierarchyMs=watch.Elapsed.TotalMilliseconds;
                if(Phase=="after") Validate();
                start=EditorApplication.timeSinceStartup;
            }
            if(EditorApplication.timeSinceStartup-start<3) { view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene); return; }
            if(profiling)
            {
                if(EditorApplication.timeSinceStartup-start<6) { view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene); return; }
                UnityEngine.Profiling.Profiler.enabled=false; ProfilerDriver.enabled=false;
                Analyze(); Finish(null); return;
            }
            double elapsed=EditorApplication.timeSinceStartup-start;
            string report=$"phase={Phase} cameraConfiguration2000Ms={cameraMs:F3} hierarchy512Nodes2000Ms={hierarchyMs:F3} nativeRepaints={renders} seconds={elapsed:F4} fps={renders/elapsed:F2} linear={QualitySettings.activeColorSpace}\n";
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../studio-performance-"+Phase+".txt")),report);
            UnityEngine.Debug.Log("STUDIO_PERFORMANCE "+report);
            if(Environment.GetCommandLineArgs().Contains("-studioProfile"))
            {
                ProfilerDriver.profileEditor=true; ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.CPU,true);
                ProfilerDriver.enabled=true; UnityEngine.Profiling.Profiler.enabled=true; profiling=true; return;
            }
            Finish(null);
        }
        catch(Exception error) { Finish(error); }
    }
    static void Require(bool pass,string text) { if(!pass)throw new Exception(text); UnityEngine.Debug.Log("PERFORMANCE_CHECK "+text); }
    static void Validate()
    {
        var light=(StudioPreviewLighting)typeof(StudioModelEditor).GetField("previewLighting",Flags).GetValue(editor);
        int count=light.CameraConfigurationCount; editor.ConfigureLightingCamera(view);
        Require(light.CameraConfigurationCount==count,"Unchanged camera settings reused");
        bool aa=StudioSettings.Current.disableViewportAntialiasing, smaa=StudioSettings.Current.useViewportSmaa;
        StudioSettings.Current.disableViewportAntialiasing=!aa; editor.ConfigureLightingCamera(view);
        Require(light.CameraConfigurationCount==count+1,"AA changes immediately reconfigure camera");
        StudioSettings.Current.useViewportSmaa=!smaa; editor.ConfigureLightingCamera(view);
        Require(light.CameraConfigurationCount==count+2,"AA quality changes immediately reconfigure camera");
        editor.EditLighting(s=>s.studioLights=true); editor.ConfigureLightingCamera(view);
        Require(light.CameraConfigurationCount==count+3,"Lighting changes immediately reconfigure camera");
        editor.EditLighting(s=>s.studioLights=false); StudioSettings.Current.disableViewportAntialiasing=aa; StudioSettings.Current.useViewportSmaa=smaa;
        editor.ConfigureLightingCamera(view); count=light.CameraConfigurationCount;
        light.RestoreCameras(); editor.ConfigureLightingCamera(view);
        Require(light.CameraConfigurationCount==count+1,"Camera resume reapplies settings after restoring originals");
        var rows=typeof(StudioModelEditor).GetMethod("HierarchyRows",Flags);
        var initial=(List<int>)rows.Invoke(largeEditor,null);
        Require(ReferenceEquals(initial,rows.Invoke(largeEditor,null)) && initial.Count==512,"Stable hierarchy traversal is reused");
        var collapsed=(HashSet<StudioModelDocument.Node>)typeof(StudioModelEditor).GetField("collapsedHierarchy",Flags).GetValue(largeEditor);
        collapsed.Add(large.nodes[0]); typeof(StudioModelEditor).GetField("hierarchyRows",Flags).SetValue(largeEditor,null);
        Require(((List<int>)rows.Invoke(largeEditor,null)).Count==505,"Foldout hides only descendants");
        typeof(StudioModelEditor).GetField("search",Flags).SetValue(largeEditor,"Hierarchy");
        Require(((List<int>)rows.Invoke(largeEditor,null)).Count==512,"Search includes collapsed descendants");
        typeof(StudioModelEditor).GetField("search",Flags).SetValue(largeEditor,"");
        Require(((List<int>)rows.Invoke(largeEditor,null)).Count==505,"Clearing search restores collapsed state");
        largeEditor.SelectObject(1);
        Require(((List<int>)rows.Invoke(largeEditor,null)).Count==512,"Selecting a child expands and refreshes hierarchy");
        var cached=rows.Invoke(largeEditor,null);
        typeof(StudioModelEditor).GetMethod("Rebuild",Flags).Invoke(largeEditor,null);
        Require(!ReferenceEquals(cached,rows.Invoke(largeEditor,null)),"Hierarchy rebuild invalidates traversal");
        CompareNeutralRendering(light);
    }
    static Color[] FramePixels(string name)
    {
        var parent=typeof(EditorWindow).GetField("m_Parent",Flags).GetValue(view);
        var texture=new RenderTexture(400,350,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        var pixels=new Texture2D(400,350,TextureFormat.RGBA32,false,true);var active=RenderTexture.active;
        try
        {
            texture.Create();
            parent.GetType().GetMethod("GrabPixels",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(parent,new object[]{texture,new Rect(650,250,400,350)});
            RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,400,350),0,0);pixels.Apply();
            File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../studio-performance-"+name+".png")),pixels.EncodeToPNG());
            return pixels.GetPixels();
        }
        finally {RenderTexture.active=active;Object.DestroyImmediate(texture);Object.DestroyImmediate(pixels);}
    }
    static void CompareNeutralRendering(StudioPreviewLighting light)
    {
        var data=view.camera.GetComponent(Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData, Unity.RenderPipelines.HighDefinition.Runtime"));
        var configure=typeof(StudioPreviewLighting).GetMethod("ConfigureFrameSettings",Flags);
        void Paint(){view.MarkDirty(MashBoxStudioViewport.DirtyState.All);view.SendEvent(new Event{type=EventType.Layout});view.SendEvent(new Event{type=EventType.Repaint});}
        configure.Invoke(light,new object[]{data,!StudioSettings.Current.disableViewportAntialiasing,false});Paint();
        var original=FramePixels("original-passes");
        configure.Invoke(light,new object[]{data,!StudioSettings.Current.disableViewportAntialiasing,true});Paint();
        var lean=FramePixels("neutral-passes");double difference=0;
        for(int i=0;i<original.Length;i++) difference+=Math.Abs(original[i].r-lean[i].r)+Math.Abs(original[i].g-lean[i].g)+Math.Abs(original[i].b-lean[i].b);
        difference/=original.Length*3;
        Require(original.Distinct().Count()>10 && difference<.002,"Neutral pipeline preserves the rendered cube/grid image; mean pixel difference="+difference);
    }
    static void Analyze()
    {
        var totals=new Dictionary<string,(double self,double time,int count)>();
        for(int frame=Math.Max(ProfilerDriver.firstFrameIndex,ProfilerDriver.lastFrameIndex-120);frame<=ProfilerDriver.lastFrameIndex;frame++)
        using(var data=ProfilerDriver.GetRawFrameDataView(frame,0))
        {
            if(!data.valid)continue;
            for(int i=0;i<data.sampleCount;i++)
            {
                string name=data.GetSampleName(i); double time=data.GetSampleTimeMs(i),self=time;
                int end=i+data.GetSampleChildrenCountRecursive(i);
                for(int child=i+1;child<=end;child+=1+data.GetSampleChildrenCountRecursive(child)) self-=data.GetSampleTimeMs(child);
                totals.TryGetValue(name,out var v); totals[name]=(v.self+self,v.time+time,v.count+1);
            }
        }
        var text=new StringBuilder();
        foreach(var pair in totals.OrderByDescending(p=>p.Value.self).Take(45))text.AppendLine($"self={pair.Value.self:F3} total={pair.Value.time:F3} count={pair.Value.count} {pair.Key}");
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../studio-performance-profile.txt")),text.ToString());
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick; SceneView.duringSceneGui-=Observe;
        largeEditor?.Dispose(); if(view)view.Close(); if(controller)Object.DestroyImmediate(controller);
        if(doc)Object.DestroyImmediate(doc); if(large)Object.DestroyImmediate(large); if(mesh)Object.DestroyImmediate(mesh);
        if(error!=null)UnityEngine.Debug.LogException(error);
        EditorApplication.Exit(error==null?0:1);
    }
}
