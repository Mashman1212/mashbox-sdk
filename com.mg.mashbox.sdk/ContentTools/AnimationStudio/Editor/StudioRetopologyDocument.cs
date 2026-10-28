using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    public sealed class StudioRetopologyDocument : ScriptableObject
    {
        [Serializable] public sealed class Face
        {
            public int[] vertices;
            public int material;
            public Vector2[] cornerUV;
            public Face(int[] vertices, int material = 0) { this.vertices=vertices; this.material=material; }
        }
        [Serializable] public sealed class Part
        {
            public string name;
            public Mesh reference;
            public Material[] materials;
            public string[] skinBoneNames;
            public Matrix4x4[] skinBindCorrections;
            public bool showReference=true, showOutput=true;
            public List<Vector3> vertices=new List<Vector3>();
            public List<Vector2> uv=new List<Vector2>();
            public List<Face> faces=new List<Face>();
            public bool needsUV;
            public int Revision;
        }
        public List<Part> parts=new List<Part>();
        public int selected=-1;
        public bool dirty;
        // All tools share document Undo, including incomplete constructions.
        // The original polygon field names preserve existing saved drafts.
        [HideInInspector] public List<Vector3> polygonPoints=new List<Vector3>();
        [HideInInspector] public Vector3 polygonNormal;
        [HideInInspector] public int polygonPart=-1;
        [Serializable] public sealed class ToolState
        {
            public int tool,selectionMode,transformTool;
            public int[] selected,selectedFaces,selectedEdges,continuation,mirrorContinuation;
            public bool continuousPatch,autoDensity;
            public int divisions,tubeSides,tubeRows;
            public float quadSize,stripWidth,relaxRadius,relaxStrength,resolutionRadius;
            public bool relaxBorders;
            public int weldSource=-1,bridgeA=-1,bridgeB=-1;
            public int stripLeft=-1,stripRight=-1,stripMirrorLeft=-1,stripMirrorRight=-1;
            public Vector3 stripCenter,stripNormal,stripSide;
            public bool stripMirror,stripClosed,stripFirstRow;
        }
        [HideInInspector] public ToolState toolState;
        [HideInInspector] public string tutorialId;
        [HideInInspector] public List<string> tutorialActions=new List<string>();
        public bool project=true, symmetry, preserveBorders=true;
        public int targetTriangles=3000;
        public float offset=.002f, creaseAngle=55;
        public string profile="Clothing";

        internal static Mesh BuildMesh(Part part)
        {
            var mesh=new Mesh { name=part.name+" Low", indexFormat=IndexFormat.UInt32, hideFlags=HideFlags.HideAndDontSave };
            mesh.SetVertices(part.vertices);
            if(part.uv.Count==part.vertices.Count) mesh.SetUVs(0,part.uv);
            int submeshes=Mathf.Max(1,part.faces.Count==0?1:part.faces.Max(f=>f.material)+1);
            mesh.subMeshCount=submeshes;
            for(int sub=0;sub<submeshes;sub++)
            {
                var indices=new List<int>();
                foreach(var face in part.faces) if(face.material==sub)
                    for(int i=1;i<face.vertices.Length-1;i++) { indices.Add(face.vertices[0]); indices.Add(face.vertices[i]); indices.Add(face.vertices[i+1]); }
                mesh.SetTriangles(indices,sub,false);
            }
            mesh.RecalculateNormals(); ApplyCornerUVs(part,mesh); if(mesh.uv.Length==mesh.vertexCount) mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
        // Split only the render/export vertices at UV seams; editable topology stays welded.
        private static void ApplyCornerUVs(Part part,Mesh mesh)
        {
            if(!part.faces.Any(f=>f.cornerUV!=null&&f.cornerUV.Length==f.vertices.Length))return;
            var positions=new List<Vector3>(part.vertices);var normals=mesh.normals.ToList();
            var uv=part.vertices.Select((v,i)=>i<part.uv.Count?part.uv[i]:Vector2.zero).ToList();
            var used=new bool[part.vertices.Count];var lookup=new Dictionary<(int,Vector2),int>();
            var indices=new List<int>[mesh.subMeshCount];for(int i=0;i<indices.Length;i++)indices[i]=new List<int>();
            foreach(var face in part.faces)
            {
                var faceIndices=new int[face.vertices.Length];
                for(int c=0;c<face.vertices.Length;c++)
                {
                    int v=face.vertices[c];var value=FaceUV(part,face,c);var key=(v,value);
                    if(!lookup.TryGetValue(key,out int id))
                    {
                        if(!used[v]){id=v;used[v]=true;uv[v]=value;}
                        else {id=positions.Count;positions.Add(part.vertices[v]);normals.Add(normals[v]);uv.Add(value);}
                        lookup.Add(key,id);
                    }
                    faceIndices[c]=id;
                }
                for(int c=1;c<faceIndices.Length-1;c++){indices[face.material].Add(faceIndices[0]);indices[face.material].Add(faceIndices[c]);indices[face.material].Add(faceIndices[c+1]);}
            }
            mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uv);
            for(int i=0;i<indices.Length;i++)mesh.SetTriangles(indices[i],i,false);
        }
        internal static Vector2 FaceUV(Part part,Face face,int corner)
        {
            if(face.cornerUV!=null&&face.cornerUV.Length==face.vertices.Length)return face.cornerUV[corner];
            int v=face.vertices[corner];return v<part.uv.Count?part.uv[v]:Vector2.zero;
        }
        internal static void CopyReference(Part part)
        {
            part.vertices=part.reference.vertices.ToList();
            part.uv=part.reference.uv.ToList();
            if(part.uv.Count!=part.vertices.Count) { part.uv=Enumerable.Repeat(Vector2.zero,part.vertices.Count).ToList(); part.needsUV=true; }
            else part.needsUV=false;
            part.faces.Clear();
            for(int sub=0;sub<part.reference.subMeshCount;sub++)
            {
                var indices=part.reference.GetIndices(sub); var topology=part.reference.GetTopology(sub);
                int size=topology==MeshTopology.Quads?4:topology==MeshTopology.Triangles?3:0;
                if(size==0) continue;
                for(int i=0;i<indices.Length;i+=size) part.faces.Add(new Face(indices.Skip(i).Take(size).ToArray(),sub));
            }
            part.Revision++;
        }
        internal void SaveWorkspace()
        {
            if(!AssetDatabase.Contains(this))
            {
                string path=EditorUtility.SaveFilePanelInProject("Save Retopology Workspace", "Retopology Workspace", "asset", "Save references and editable topology.");
                if(string.IsNullOrEmpty(path)) return;
                hideFlags=HideFlags.None; AssetDatabase.CreateAsset(this,path);
            }
            foreach(var part in parts) if(part.reference && !AssetDatabase.Contains(part.reference))
            { part.reference.hideFlags=HideFlags.None; AssetDatabase.AddObjectToAsset(part.reference,this); }
            dirty=false; EditorUtility.SetDirty(this); AssetDatabase.SaveAssetIfDirty(this);
        }
    }
}
