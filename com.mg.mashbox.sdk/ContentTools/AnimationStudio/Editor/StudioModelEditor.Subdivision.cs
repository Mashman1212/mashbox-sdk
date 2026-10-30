using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private sealed class SubdivisionCache : IDisposable
        {
            internal StudioSubdivisionData.Input Input;
            internal StudioSubdivisionGpu Gpu;
            internal StudioModelDocument.Node Source;
            internal StudioModifierHistory History;
            internal int Revision;
            public void Dispose(){Gpu?.Dispose();Gpu=null;if(Source?.mesh)UnityEngine.Object.DestroyImmediate(Source.mesh);Source=null;}
        }
        private readonly Dictionary<StudioModelDocument.Node,SubdivisionCache> subdivisionCaches=new Dictionary<StudioModelDocument.Node,SubdivisionCache>();
        private readonly Dictionary<StudioModelDocument.Node,string> subdivisionErrors=new Dictionary<StudioModelDocument.Node,string>();
        private static ComputeShader subdivisionProgram;
        private bool subdivisionSuspended;
        private static ComputeShader SubdivisionProgram
        {
            get
            {
                if(!subdivisionProgram)
                    foreach(string guid in AssetDatabase.FindAssets("StudioSubdivision t:ComputeShader"))
                    {var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(AssetDatabase.GUIDToAssetPath(guid));if(shader&&shader.name=="StudioSubdivision"){subdivisionProgram=shader;break;}}
                if(!subdivisionProgram)throw new InvalidOperationException("The Studio subdivision compute shader is unavailable.");return subdivisionProgram;
            }
        }
        private IEnumerable<StudioModelDocument.Node> SubdivisionSelection()
        {
            if(!Document)yield break;var selected=objects.Count>0?objects.OrderBy(i=>i):Enumerable.Repeat(Active,1);
            foreach(int i in selected)if(i>=0&&i<Document.nodes.Count&&Document.nodes[i].mesh)yield return Document.nodes[i];
        }
        private void ClearSubdivision()
        {foreach(var cache in subdivisionCaches.Values)cache.Dispose();subdivisionCaches.Clear();subdivisionErrors.Clear();}
        private void DirtySubdivision(Mesh mesh=null)
        {subdivisionErrors.Clear();}
        internal void SetSubdivisionActive(bool active)
        {subdivisionSuspended=!active;if(!active)ClearSubdivision();else DirtySubdivision();}
        private bool SubdivisionMeshIsEditing(Mesh mesh)=>pendingModifiers.ContainsKey(mesh)||
            (livePreview!=null&&livePreview.Targets.Any(t=>t.Live==mesh))||
            (bisectPreview!=null&&bisectPreview.Targets.Any(t=>t.Live==mesh))||
            (componentLiveTargets!=null&&componentLiveTargets.Any(t=>t.state.livePreview!=null&&t.state.livePreview.Targets.Any(p=>p.Live==mesh)));
        private void UpdateSubdivision()
        {
            if(!Document||subdivisionSuspended)return;
            for(int index=0;index<Document.nodes.Count&&index<Transforms.Length;index++)
            {
                var node=Document.nodes[index];var history=node.modifiers;subdivisionCaches.TryGetValue(node,out var cache);
                if(history==null||!history.HasDisplay||!node.mesh||!Transforms[index]||SubdivisionMeshIsEditing(node.mesh))
                {if(cache!=null){cache.Dispose();subdivisionCaches.Remove(node);}continue;}
                if(subdivisionErrors.ContainsKey(node))continue;
                try
                {
                    if(!StudioSubdivisionGpu.Supported)throw new NotSupportedException("GPU subdivision rendering requires the Windows x64 Editor with compute support. The evaluated editable result remains visible.");
                    if(cache==null||cache.History!=history||cache.Revision!=history.revision)
                    {
                        cache?.Dispose();subdivisionCaches.Remove(node);cache=new SubdivisionCache{History=history,Revision=history.revision};
                        cache.Source=history.displaySource.Restore();cache.Source.subdivision=history.displaySettings.Copy();
                        cache.Input=new StudioSubdivisionData.Input(cache.Source);
                        var template=cache.Input.CreateMesh(cache.Source,false,out _);
                        try{cache.Gpu=new StudioSubdivisionGpu(cache.Input.Plan,SubdivisionProgram,template,cache.Input.Representatives.Length,cache.Source.mesh);}
                        finally{UnityEngine.Object.DestroyImmediate(template);}
                        cache.Gpu.Evaluate(cache.Input.Positions(cache.Source.mesh),cache.Source.mesh.bounds);subdivisionCaches.Add(node,cache);
                    }
                    cache.Gpu.Attach(Transforms[index].GetComponent<MeshFilter>(),node.mesh);
                }
                catch(Exception exception)
                {cache?.Dispose();subdivisionCaches.Remove(node);subdivisionErrors[node]=exception.Message;error=exception.Message;commandView?.ShowNotification(new GUIContent(error));}
            }
        }
        private void ToggleSubdivision()
        {
            FlushModifierEdits();var selected=SubdivisionSelection().GroupBy(n=>n.mesh).Select(g=>g.First()).ToArray();
            if(selected.Length==0){PrepareMeshSelection(ComponentMode.Object,()=>SubdivisionSelection().Any(),"Select a mesh to add Subdivision.",ComponentMode.Object,ComponentMode.Vertex,ComponentMode.Edge,ComponentMode.Face);return;}
            if(selected.All(n=>n.modifiers?.operations.Any(o=>o.kind=="Subdivision")!=true)){AddSubdivisionModifier();return;}
            foreach(var node in selected)
            {
                var operation=node.modifiers?.operations.LastOrDefault(o=>o.kind=="Subdivision");
                if(operation!=null)ChangeModifier(node,operation.id,o=>o.enabled=!o.enabled);
            }
        }
        // Retain the old internal command identity for custom bindings.
        private void BakeSubdivision()=>ClearModifierHistory();
    }
}
