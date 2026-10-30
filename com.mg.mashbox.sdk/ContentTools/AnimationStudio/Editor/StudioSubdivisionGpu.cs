using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    // A disposable renderer mirror. The authoritative cage never binds to an
    // RW buffer. Dispatches and subsequent renderer draws use Unity's graphics
    // queue, so no asynchronous-compute fence or editing-time readback is needed.
    internal sealed class StudioSubdivisionGpu : IDisposable
    {
        internal Mesh Mesh { get; private set; }
        private readonly StudioSubdivisionPlan plan;
        private ComputeShader shader;
        private GraphicsBuffer cage,offsets,indices,weights,evaluated,corners,normals,normalOffsets,normalFaces,surface;
        private int evaluateKernel,faceKernel,writeKernel;
        private readonly int cageCount;
        private GraphicsBuffer sourceAttributes,attributeOffsets,attributeIndices,attributeWeights,attributes,values,attributeLayout;
        private int attributeKernel;
        private Vector4[] attributeData;
        private readonly List<Vector4> uvValues=new List<Vector4>();
        private readonly List<Color> colorValues=new List<Color>();
        private bool attributesReady;
        private Mesh attributeSource;
        private readonly List<(MeshFilter filter,Mesh original)> bindings=new List<(MeshFilter,Mesh)>();

        internal static bool Supported => Application.platform==RuntimePlatform.WindowsEditor && IntPtr.Size==8 && SystemInfo.supportsComputeShaders &&
            SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null &&
            SystemInfo.graphicsShaderLevel>=50;
        internal StudioSubdivisionGpu(StudioSubdivisionPlan plan,ComputeShader program,Mesh displayTemplate,int cageCount,Mesh attributeSource)
        {
            this.plan=plan;this.cageCount=cageCount;this.attributeSource=attributeSource;
            if(!Supported)throw new NotSupportedException("GPU subdivision requires compute shaders and shader model 5.0.");
            if(!program || !displayTemplate || displayTemplate.vertexCount!=plan.Corners.Length)
                throw new ArgumentException("Subdivision shader or display layout is unavailable.");
            try
            {
                shader=UnityEngine.Object.Instantiate(program);shader.hideFlags=HideFlags.HideAndDontSave;
                evaluateKernel=shader.FindKernel("EvaluatePositions");faceKernel=shader.FindKernel("FaceNormals");writeKernel=shader.FindKernel("WriteSurface");
                Mesh=UnityEngine.Object.Instantiate(displayTemplate);Mesh.name="Studio GPU subdivision";Mesh.hideFlags=HideFlags.HideAndDontSave;
                Mesh.vertexBufferTarget|=GraphicsBuffer.Target.Raw;
                int stream=Mesh.GetVertexAttributeStream(VertexAttribute.Position);
                if(stream!=Mesh.GetVertexAttributeStream(VertexAttribute.Normal) ||
                    Mesh.GetVertexAttributeFormat(VertexAttribute.Position)!=VertexAttributeFormat.Float32 ||
                    Mesh.GetVertexAttributeFormat(VertexAttribute.Normal)!=VertexAttributeFormat.Float32)
                    throw new InvalidOperationException("Subdivision positions and normals need a shared Float32 stream.");
                surface=Mesh.GetVertexBuffer(stream);
                cage=new GraphicsBuffer(GraphicsBuffer.Target.Structured,cageCount,12);
                evaluated=new GraphicsBuffer(GraphicsBuffer.Target.Structured,plan.VertexCount,12);
                normals=new GraphicsBuffer(GraphicsBuffer.Target.Structured,plan.FaceCount,12);
                offsets=Buffer(plan.PositionOffsets,4);indices=Buffer(plan.PositionIndices,4);weights=Buffer(plan.PositionWeights,4);
                corners=Buffer(plan.Corners,4);normalOffsets=Buffer(plan.NormalOffsets,4);normalFaces=Buffer(plan.NormalFaces,4);
                shader.SetInt("VertexCount",plan.VertexCount);shader.SetInt("FaceCount",plan.FaceCount);shader.SetInt("CornerCount",plan.Corners.Length);
                shader.SetInt("Stride",Mesh.GetVertexBufferStride(stream));
                shader.SetInt("PositionOffset",Mesh.GetVertexAttributeOffset(VertexAttribute.Position));
                shader.SetInt("NormalOffset",Mesh.GetVertexAttributeOffset(VertexAttribute.Normal));
                shader.SetBuffer(evaluateKernel,"Cage",cage);shader.SetBuffer(evaluateKernel,"Offsets",offsets);
                shader.SetBuffer(evaluateKernel,"Indices",indices);shader.SetBuffer(evaluateKernel,"Weights",weights);shader.SetBuffer(evaluateKernel,"Evaluated",evaluated);
                shader.SetBuffer(faceKernel,"Evaluated",evaluated);shader.SetBuffer(faceKernel,"Corners",corners);shader.SetBuffer(faceKernel,"Normals",normals);
                shader.SetBuffer(writeKernel,"Evaluated",evaluated);shader.SetBuffer(writeKernel,"Corners",corners);shader.SetBuffer(writeKernel,"Normals",normals);
                shader.SetBuffer(writeKernel,"NormalOffsets",normalOffsets);shader.SetBuffer(writeKernel,"NormalFaces",normalFaces);shader.SetBuffer(writeKernel,"Surface",surface);
                attributeKernel=shader.FindKernel("EvaluateAttributes");
                attributeData=new Vector4[attributeSource.vertexCount*9];
                sourceAttributes=new GraphicsBuffer(GraphicsBuffer.Target.Structured,attributeData.Length,16);
                attributes=new GraphicsBuffer(GraphicsBuffer.Target.Structured,(plan.AttributeOffsets.Length-1)*9,16);
                attributeOffsets=Buffer(plan.AttributeOffsets,4);attributeIndices=Buffer(plan.AttributeIndices,4);attributeWeights=Buffer(plan.AttributeWeights,4);values=Buffer(plan.Values,4);
                var layout=new Vector2Int[9];
                for(int channel=0;channel<9;channel++)
                {
                    var attr=channel==8?VertexAttribute.Color:VertexAttribute.TexCoord0+channel;
                    if(!Mesh.HasVertexAttribute(attr))continue;
                    if(Mesh.GetVertexAttributeStream(attr)!=stream||Mesh.GetVertexAttributeFormat(attr)!=VertexAttributeFormat.Float32)
                        throw new InvalidOperationException("Subdivision attributes require the Float32 surface stream.");
                    layout[channel]=new Vector2Int(Mesh.GetVertexAttributeOffset(attr),Mesh.GetVertexAttributeDimension(attr));
                }
                attributeLayout=Buffer(layout,8);
                shader.SetInt("AttributeCount",plan.AttributeOffsets.Length-1);shader.SetInt("TangentOffset",Mesh.HasVertexAttribute(VertexAttribute.Tangent)?Mesh.GetVertexAttributeOffset(VertexAttribute.Tangent):-1);
                shader.SetBuffer(attributeKernel,"SourceAttributes",sourceAttributes);shader.SetBuffer(attributeKernel,"AttributeOffsets",attributeOffsets);shader.SetBuffer(attributeKernel,"AttributeIndices",attributeIndices);
                shader.SetBuffer(attributeKernel,"AttributeWeights",attributeWeights);shader.SetBuffer(attributeKernel,"Attributes",attributes);shader.SetBuffer(attributeKernel,"AttributeLayout",attributeLayout);
                shader.SetBuffer(writeKernel,"Attributes",attributes);shader.SetBuffer(writeKernel,"Values",values);shader.SetBuffer(writeKernel,"AttributeLayout",attributeLayout);
            }
            catch { Dispose();throw; }
        }
        private static GraphicsBuffer Buffer(Array values,int stride)
        {
            var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Math.Max(1,values.Length),stride);
            try { if(values.Length>0)buffer.SetData(values); return buffer; } catch { buffer.Dispose();throw; }
        }
        internal void Evaluate(Vector3[] positions,Bounds cageBounds)
        {
            if(positions.Length!=cageCount)throw new InvalidOperationException("The subdivision cage topology changed without rebuilding its plan.");
            cage.SetData(positions);
            bool changed=!attributesReady;
            for(int channel=0;channel<9;channel++)
            {
                if(channel==8)attributeSource.GetColors(colorValues);else attributeSource.GetUVs(channel,uvValues);
                int count=channel==8?colorValues.Count:uvValues.Count;
                for(int i=0;i<count;i++){Vector4 value=channel==8?(Vector4)colorValues[i]:uvValues[i];int slot=i*9+channel;if(attributeData[slot]!=value){changed=true;attributeData[slot]=value;}}
            }
            if(changed){sourceAttributes.SetData(attributeData);shader.Dispatch(attributeKernel,(plan.AttributeOffsets.Length-1+63)/64,1,1);attributesReady=true;}
            // Positive Catmull-Clark stencil weights stay in the cage convex hull.
            Mesh.bounds=cageBounds;
            shader.Dispatch(evaluateKernel,(plan.VertexCount+63)/64,1,1);
            shader.Dispatch(faceKernel,(plan.FaceCount+63)/64,1,1);
            shader.Dispatch(writeKernel,(plan.Corners.Length+63)/64,1,1);
        }
        internal void Attach(MeshFilter filter,Mesh authoritative=null)
        {
            if(!filter)throw new ArgumentNullException(nameof(filter));
            bool known=false;foreach(var binding in bindings)if(binding.filter==filter){known=true;break;}
            if(!known)bindings.Add((filter,authoritative?authoritative:filter.sharedMesh));
            filter.sharedMesh=Mesh;
        }
        public void Dispose()
        {
            foreach(var binding in bindings)if(binding.filter && binding.filter.sharedMesh==Mesh)binding.filter.sharedMesh=binding.original;
            bindings.Clear();
            sourceAttributes?.Dispose();sourceAttributes=null;attributeOffsets?.Dispose();attributeOffsets=null;attributeIndices?.Dispose();attributeIndices=null;
            attributeWeights?.Dispose();attributeWeights=null;attributes?.Dispose();attributes=null;values?.Dispose();values=null;attributeLayout?.Dispose();attributeLayout=null;
            surface?.Dispose();surface=null;cage?.Dispose();cage=null;offsets?.Dispose();offsets=null;
            indices?.Dispose();indices=null;weights?.Dispose();weights=null;evaluated?.Dispose();evaluated=null;
            corners?.Dispose();corners=null;normals?.Dispose();normals=null;normalOffsets?.Dispose();normalOffsets=null;normalFaces?.Dispose();normalFaces=null;
            if(Mesh)UnityEngine.Object.DestroyImmediate(Mesh);Mesh=null;
            if(shader)UnityEngine.Object.DestroyImmediate(shader);shader=null;
        }
    }
}
