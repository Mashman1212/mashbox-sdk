using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private Material wireBatchMaterial;
        internal int WireBatchUploads { get; private set; }
        private static readonly int WirePositions=Shader.PropertyToID("_WirePositions"), WireEdges=Shader.PropertyToID("_WireEdges"),
            WireMatrix=Shader.PropertyToID("_WireObjectToWorld"), WireViewport=Shader.PropertyToID("_WireViewport"),
            WireColor=Shader.PropertyToID("_WireColor"), WireWidth=Shader.PropertyToID("_WireWidth"),
            WireCamera=Shader.PropertyToID("_WireCamera"), WireForward=Shader.PropertyToID("_WireForward"),
            WireDepth=Shader.PropertyToID("_ZTest"), WireXRay=Shader.PropertyToID("_WireXRay"),
            WireAdjacent=Shader.PropertyToID("_WireAdjacent"),WireSubset=Shader.PropertyToID("_WireSubset"),WireUseSubset=Shader.PropertyToID("_WireUseSubset");
        private static void ReleaseWireBatch(SelectionWireframe wire)
        {
            wire.positionBuffer?.Dispose(); wire.edgeBuffer?.Dispose();
            wire.adjacentBuffer?.Dispose();wire.adjacentBuffer=null;
            wire.positionBuffer=wire.edgeBuffer=null;
        }
        private bool DrawWireBatch(SelectionWireframe wire,Matrix4x4 matrix,Color color,float width,Camera camera,GraphicsBuffer subset=null,int subsetCount=0)
        {
            if(!camera || SystemInfo.graphicsShaderLevel<45 || wire.vertices.Count==0 || wire.indices.Length==0)return false;
            if(!wireBatchMaterial)
            {
                var shader=Shader.Find("Hidden/MashBox/ModelerWireBatch");
                if(!shader || !shader.isSupported)return false;
                wireBatchMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            }
            if(wire.positionBuffer==null)
            {
                wire.positionBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,wire.vertices.Count,12);
                wire.edgeBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,wire.indices.Length,4);
                wire.edgeBuffer.SetData(wire.indices);wire.batchPositionsDirty=true;wire.vertexUpdateStart=0;
                wire.adjacentBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,wire.adjacent.Length,4);
                wire.adjacentBuffer.SetData(wire.adjacent);
            }
            if(wire.batchPositionsDirty){int start=wire.vertexUpdateStart;wire.positionBuffer.SetData(wire.vertices,start,start,wire.vertices.Count-start);wire.batchPositionsDirty=false;WireBatchUploads++;}
            wireBatchMaterial.SetBuffer(WirePositions,wire.positionBuffer);wireBatchMaterial.SetBuffer(WireEdges,wire.edgeBuffer);
            wireBatchMaterial.SetBuffer(WireAdjacent,wire.adjacentBuffer);
            wireBatchMaterial.SetInt(WireUseSubset,subset!=null?1:0);
            wireBatchMaterial.SetBuffer(WireSubset,subset??wire.edgeBuffer);
            wireBatchMaterial.SetMatrix(WireMatrix,matrix);
            wireBatchMaterial.SetVector(WireViewport,new Vector4(camera.pixelWidth,camera.pixelHeight,0,0));
            wireBatchMaterial.SetVector(WireCamera,new Vector4(outlineCameraPosition.x,outlineCameraPosition.y,outlineCameraPosition.z,outlineOrthographic?1:0));
            wireBatchMaterial.SetVector(WireForward,new Vector4(outlineCameraForward.x,outlineCameraForward.y,outlineCameraForward.z,outlineNearClip));
            // SceneView's handle overlay is composited in display space, as are
            // native Handles colors. Do not linearize these a second time.
            wireBatchMaterial.SetColor(WireColor,color);
            wireBatchMaterial.SetFloat(WireWidth,width*EditorGUIUtility.pixelsPerPoint);
            wireBatchMaterial.SetFloat(WireDepth,(float)(displayXray?CompareFunction.Always:CompareFunction.LessEqual));
            wireBatchMaterial.SetFloat(WireXRay,displayXray?1:0);
            if(!wireBatchMaterial.SetPass(0))return false;
            Graphics.DrawProceduralNow(MeshTopology.Triangles,6,subset!=null?subsetCount:wire.indices.Length/2);
            return true;
        }
    }
}
