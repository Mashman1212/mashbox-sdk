#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace MashBoxSDK.MapTools
{
    internal sealed class MGTerrainPlacementPreview : IDisposable
    {
        internal readonly Mesh Mesh;
        internal readonly Matrix4x4 Matrix;
        internal readonly Vector3 LabelPosition;
        internal readonly Vector3[][] Lines;
        delegate bool MeshRaycast(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit);
        static readonly MeshRaycast RaycastMesh = typeof(HandleUtility).GetMethod("IntersectRayMesh", BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,
            null, new[]{typeof(Ray),typeof(Mesh),typeof(Matrix4x4),typeof(RaycastHit).MakeByRefType()},null)?.CreateDelegate(typeof(MeshRaycast)) as MeshRaycast;
        internal MGTerrainPlacementPreview(MGTerrain source, Vector2Int direction, Bounds bounds, IReadOnlyList<MGTerrain> neighbours)
        {
            int width=source.SurfaceGridWidth,height=source.SurfaceGridHeight;
            if(width<=2 || height<=2 || (long)width*height>source.MeshFilter.sharedMesh.vertexCount)
            {
                MGTerrainTileAuthoring.BuildTileVertices(source.MeshFilter.sharedMesh,direction,out var xs,out var zs,out _,out _);
                width=xs.Length;height=zs.Length;
            }
            MGTerrainTileAuthoring.BuildScaledGeometry(source,bounds,neighbours,width,height,out var vertices,out var uv,out var triangles);
            Matrix=Matrix4x4.Translate(new Vector3(bounds.min.x,source.MeshFilter.transform.position.y,bounds.min.z));
            Mesh=new Mesh {name="Terrain placement preview",hideFlags=HideFlags.HideAndDontSave,
                indexFormat=vertices.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16,vertices=vertices,uv=uv,triangles=triangles};
            Mesh.RecalculateBounds();
            LabelPosition=Matrix.MultiplyPoint3x4(vertices[(height/2)*width+width/2]);
            var lines=new List<Vector3[]>();
            // Include stitch vertices in the perimeter so the visible edge is exact.
            for(int side=0;side<4;side++)
            {
                int s=side;
                lines.Add(vertices.Where(v=>s==0?Mathf.Abs(v.x)<.001f:s==1?Mathf.Abs(v.x-bounds.size.x)<.001f:
                    s==2?Mathf.Abs(v.z)<.001f:Mathf.Abs(v.z-bounds.size.z)<.001f)
                    .OrderBy(v=>s<2?v.z:v.x).Select(Matrix.MultiplyPoint3x4).ToArray());
            }
            for(int step=1;step<4;step++)
            {
                int z=(height-1)*step/4,x=(width-1)*step/4;
                lines.Add(Enumerable.Range(0,width).Select(i=>Matrix.MultiplyPoint3x4(vertices[z*width+i])).ToArray());
                lines.Add(Enumerable.Range(0,height).Select(i=>Matrix.MultiplyPoint3x4(vertices[i*width+x])).ToArray());
            }
            Lines=lines.ToArray();
        }
        internal bool Hit(Ray ray,out RaycastHit hit)
        {
            hit=default;
            var b=Mesh.bounds;b.center=Matrix.MultiplyPoint3x4(b.center);
            if(!b.IntersectRay(ray))return false;
            if(RaycastMesh==null)throw new InvalidOperationException("Unity mesh picking is unavailable; terrain creation preview cannot be picked safely.");
            return RaycastMesh(ray,Mesh,Matrix,out hit);
        }
        public void Dispose(){if(Mesh!=null)UnityEngine.Object.DestroyImmediate(Mesh);}
    }
    public sealed partial class MGTerrainWorldEditor
    {
        readonly Dictionary<(int,Bounds),MGTerrainPlacementPreview> m_PlacementPreviews=new Dictionary<(int,Bounds),MGTerrainPlacementPreview>();
        int m_PlacementSignature;
        Material m_PlacementMaterial;
        void ClearPlacementPreviews()
        {
            foreach(var preview in m_PlacementPreviews.Values)preview.Dispose();
            m_PlacementPreviews.Clear();
        }
        void DisposePlacementPreviews()
        {
            ClearPlacementPreviews();
            if(m_PlacementMaterial!=null)DestroyImmediate(m_PlacementMaterial);
        }
        void ValidatePlacementPreviews(MGTerrain[] tiles)
        {
            int signature=m_NewTileScale;
            unchecked {foreach(var tile in tiles)
            {
                var mesh=tile.MeshFilter.sharedMesh;
                signature=signature*397^tile.GetInstanceID();signature=signature*397^mesh.GetInstanceID();
                signature=signature*397^EditorUtility.GetDirtyCount(mesh);signature=signature*397^mesh.vertexCount;
                signature=signature*397^mesh.bounds.GetHashCode();signature=signature*397^tile.MeshFilter.transform.localToWorldMatrix.GetHashCode();
                signature=signature*397^tile.SurfaceGridWidth;signature=signature*397^tile.SurfaceGridHeight;
            }}
            if(signature==m_PlacementSignature)return;
            ClearPlacementPreviews();m_PlacementSignature=signature;
        }
        MGTerrainPlacementPreview PlacementPreview(MGTerrain tile,Vector2Int direction,Bounds bounds,MGTerrain[] neighbours)
        {
            var key=(tile.GetInstanceID(),bounds);
            if(!m_PlacementPreviews.TryGetValue(key,out var preview))
                m_PlacementPreviews.Add(key,preview=new MGTerrainPlacementPreview(tile,direction,bounds,neighbours));
            return preview;
        }
        void DrawPlacementPreview(MGTerrainPlacementPreview preview,bool hovered,string label)
        {
            if(m_PlacementMaterial==null)
            {
                m_PlacementMaterial=new Material(Shader.Find("Hidden/Internal-Colored")){hideFlags=HideFlags.HideAndDontSave};
                m_PlacementMaterial.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);
                m_PlacementMaterial.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
                m_PlacementMaterial.SetInt("_Cull",(int)CullMode.Off);
                m_PlacementMaterial.SetInt("_ZWrite",0);
                m_PlacementMaterial.SetInt("_ZTest",(int)CompareFunction.LessEqual);
            }
            var color=hovered?new Color(.2f,1,.4f,.26f):new Color(.2f,.7f,1,.13f);
            m_PlacementMaterial.SetColor("_Color",color);
            if(m_PlacementMaterial.SetPass(0))Graphics.DrawMeshNow(preview.Mesh,preview.Matrix);
            var oldColor=Handles.color;var oldDepth=Handles.zTest;
            try
            {
                Handles.zTest=CompareFunction.LessEqual;
                for(int i=0;i<preview.Lines.Length;i++)
                {
                    Handles.color=i<4?(hovered?Color.green:Color.cyan):new Color(color.r,color.g,color.b,.35f);
                    Handles.DrawAAPolyLine(i<4?2f:1f,preview.Lines[i]);
                }
                Handles.color=Color.white;Handles.Label(preview.LabelPosition,label,EditorStyles.whiteMiniLabel);
            }
            finally {Handles.color=oldColor;Handles.zTest=oldDepth;}
        }
    }
}
#endif
