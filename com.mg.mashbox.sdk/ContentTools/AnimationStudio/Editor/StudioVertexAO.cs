using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.MapTools;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    // Uses the same deterministic BVH sampler as the SDK Vertex AO tool, in isolation.
    public static class StudioVertexAO
    {
        [Serializable] public sealed class Settings
        {
            [Range(16,1024)] public int samples = 256;
            [Range(.001f,1)] public float relativeDistance = .12f;
            public float bias = .001f;
            public float falloff = 1;
            public bool twoSided = true;
        }
        public static float[] Sample(Mesh mesh, Settings settings, Func<float,bool> cancel = null)
        {
            if (!mesh || !mesh.isReadable) throw new InvalidOperationException("Vertex AO needs a readable mesh.");
            float distance = Mathf.Max(mesh.bounds.size.x,Mathf.Max(mesh.bounds.size.y,mesh.bounds.size.z))*settings.relativeDistance;
            return VDPrecisionBaker.SampleIsolated(mesh, distance, settings.bias, settings.samples, settings.falloff, settings.twoSided, cancel);
        }
        public static Color[] RedColors(Mesh mesh, float[] ao)
        {
            if (ao.Length != mesh.vertexCount) throw new InvalidOperationException("AO count differs from mesh vertex count.");
            var colors=mesh.colors;
            if(colors.Length!=ao.Length)colors=Enumerable.Repeat(Color.white,ao.Length).ToArray();
            for(int i=0;i<colors.Length;i++)colors[i].r=Mathf.Clamp01(ao[i]);
            return colors;
        }
        public static void Bake(StudioModelDocument document, int[] indices, Settings settings, bool showProgress = true)
        {
            if (!document || indices==null || indices.Length==0) throw new InvalidOperationException("Choose one or more mesh shapes.");
            var pending=new List<(StudioModelDocument.Node node, Color[] colors, StudioBlendShape blend)>();
            try
            {
                for(int i=0;i<indices.Length;i++)
                {
                    var node=document.nodes[indices[i]];
                    if(!node.mesh || node.skinned)throw new InvalidOperationException("Vertex AO baking supports static mesh shapes.");
                    bool Cancel(float progress) => showProgress && EditorUtility.DisplayCancelableProgressBar("Bake vertex AO (red)",node.name,(i+progress)/indices.Length);
                    var blend=node.blendShape?.Copy();
                    Color[] colors;
                    if(blend!=null)
                    {
                        var mesh=Object.Instantiate(node.mesh);
                        try
                        {
                            for(int s=0;s<blend.shapes.Length;s++)
                            {
                                var shape=blend.shapes[s];mesh.vertices=shape.positions;
                                if(shape.normals.Length==mesh.vertexCount)mesh.normals=shape.normals;else mesh.RecalculateNormals();
                                mesh.RecalculateBounds();int shapeIndex=s;
                                shape.vertexAO=Sample(mesh,settings,p=>Cancel((shapeIndex+p)/blend.shapes.Length));
                            }
                            blend.Apply(mesh);colors=mesh.colors;
                        }
                        finally {Object.DestroyImmediate(mesh);}
                    }
                    else colors=RedColors(node.mesh,Sample(node.mesh,settings,Cancel));
                    pending.Add((node,colors,blend));
                }
                // Cancel or sampling errors above leave every authored mesh unchanged.
                Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Bake shape vertex AO");
                Undo.RegisterCompleteObjectUndo(new Object[]{document}.Concat(pending.Select(p=>(Object)p.node.mesh)).Distinct().ToArray(),"Bake shape vertex AO");
                foreach(var p in pending)
                {
                    p.node.mesh.colors=p.colors;p.node.vertexAOBaked=true;
                    if(p.blend!=null)p.node.blendShape=p.blend;
                    EditorUtility.SetDirty(p.node.mesh);
                }
                EditorUtility.SetDirty(document);Undo.CollapseUndoOperations(group);
            }
            finally {if(showProgress)EditorUtility.ClearProgressBar();}
        }
        public static Texture2DArray ExportArray(StudioModelDocument document, Material material, string path)
        {
            var nodes=document.nodes.Where(n=>n.mesh&&n.blendShape==null).ToArray();
            if(nodes.Length!=8)throw new InvalidOperationException("Export requires exactly eight source shapes, in hierarchy order.");
            if(nodes.Any(n=>!n.vertexAOBaked))throw new InvalidOperationException("Bake red-channel AO on all eight shapes before exporting.");
            if(!material || !material.HasProperty("_GrassShapeAO"))throw new InvalidOperationException("Choose a GodGrass material with vertex AO support.");
            var shapes=nodes.Select(n=>MGGrassShapeBaker.Read(n.mesh,Matrix4x4.TRS(Vector3.zero,n.rotation,n.scale))).ToArray();
            MGGrassShapeBaker.Validate(shapes);
            var positions=material.GetTexture("_GrassShapePositions") as Texture2DArray;
            if(!positions)throw new InvalidOperationException("Bake the position/UV shape arrays first.");
            // Equal dimensions cannot prove that the hierarchy order matches the cooked slices.
            for(int slice=0;slice<8;slice++)
            {
                var pixels=positions.GetPixels(slice,0);
                for(int v=0;v<shapes[slice].positions.Length;v++)
                {
                    var p=shapes[slice].positions[v];var stored=pixels[v];
                    if(Vector3.Distance(p,new Vector3(stored.r,stored.g,stored.b))>1e-4f)
                        throw new InvalidOperationException("Shape "+slice+" does not match the position array. Rebuild position/UV arrays before exporting AO.");
                }
            }
            var created=MGGrassShapeBaker.BuildAO(shapes);
            try
            {
                if(created.width!=positions.width||created.height!=positions.height||positions.depth!=8)
                    throw new InvalidOperationException("AO and position arrays have different layouts. Rebuild the shape arrays first.");
                if(!path.StartsWith("Assets/",StringComparison.Ordinal)||!path.EndsWith(".asset",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Save the AO array as an asset under Assets.");
                var saved=AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
                if(saved){Undo.RegisterCompleteObjectUndo(saved,"Rebuild shape AO array");EditorUtility.CopySerialized(created,saved);}
                else {if(System.IO.File.Exists(path))throw new InvalidOperationException("Another asset occupies "+path);saved=created;AssetDatabase.CreateAsset(saved,path);created=null;}
                Undo.RecordObject(material,"Assign shape AO array");material.SetTexture("_GrassShapeAO",saved);material.SetFloat("_GrassUseShapeAO",1);
                EditorUtility.SetDirty(saved);EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(saved);AssetDatabase.SaveAssetIfDirty(material);
                return saved;
            }
            finally {if(created)Object.DestroyImmediate(created);}
        }
    }
    internal sealed class StudioVertexAOWindow : EditorWindow
    {
        StudioModelEditor editor;
        StudioVertexAO.Settings settings=new StudioVertexAO.Settings();
        Material material;string message;
        internal static void Open(StudioModelEditor editor)
        {
            var w=GetWindow<StudioVertexAOWindow>(true,"Shape Vertex AO");w.editor=editor;w.minSize=new Vector2(420,560);w.Show();
        }
        void OnGUI()
        {
            using(StudioTheme.Scope())
            {
                if(editor==null||!editor.Document){EditorGUILayout.HelpBox("Open a modeling workspace first.",MessageType.Info);return;}
                EditorGUILayout.LabelField("Bake Vertex AO — Red Channel",EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Each shape is baked alone, including hidden source shapes. Green, blue and alpha stay unchanged. Live blend shapes bake each stored pose separately.",MessageType.None);
                settings.samples=EditorGUILayout.IntSlider("Samples",settings.samples,16,1024);
                settings.relativeDistance=EditorGUILayout.Slider("Reach / shape size",settings.relativeDistance,.001f,1);
                settings.bias=Mathf.Max(0,EditorGUILayout.FloatField("Surface bias",settings.bias));
                settings.falloff=EditorGUILayout.Slider("Distance falloff",settings.falloff,0,4);
                settings.twoSided=EditorGUILayout.Toggle("Two-sided occluders",settings.twoSided);
                EditorGUILayout.HelpBox("Samples mesh geometry. Leaf-card transparency is not ray-traced; tune reach and material strength for foliage.",MessageType.None);
                if(GUILayout.Button("Bake Selected Shapes"))Run(()=>{StudioVertexAO.Bake(editor.Document,editor.AOSelectedNodes(),settings);editor.RefreshVertexAO();message="Red-channel AO baked. Save Workspace to keep the edits.";});
                if(GUILayout.Button("Bake All Shapes"))Run(()=>{StudioVertexAO.Bake(editor.Document,editor.Document.nodes.Select((n,i)=>(n,i)).Where(p=>p.n.mesh).Select(p=>p.i).ToArray(),settings);editor.RefreshVertexAO();message="Red-channel AO baked on all shapes. Save Workspace to keep the edits.";});
                GUILayout.Space(8);material=(Material)EditorGUILayout.ObjectField("GodGrass material",material,typeof(Material),false);
                using(new EditorGUI.DisabledScope(!material))
                    if(GUILayout.Button("Export 8-Shape AO Array to Material…"))Run(()=>{
                        string path=EditorUtility.SaveFilePanelInProject("Save Shape AO Array",editor.Document.name+"_AO","asset","Single-channel AO for the eight source shapes.");
                        if(string.IsNullOrEmpty(path))return;
                        StudioVertexAO.ExportArray(editor.Document,material,path);message="R8 AO array assigned. Increase Vertex AO Strength on the material to enable its contribution.";
                    });
                if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
            }
        }
        void Run(Action action){try{action();}catch(OperationCanceledException){message="Bake cancelled; mesh colors unchanged.";}catch(Exception e){message=e.Message;}}
    }
    internal sealed partial class StudioModelEditor
    {
        internal int[] AOSelectedNodes()=>objects.Where(i=>Document.nodes[i].mesh).OrderBy(i=>i).ToArray();
        internal void RefreshVertexAO(){ApplyDisplayMaterials();Changed();}
    }
}
