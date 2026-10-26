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
            mesh.RecalculateNormals(); if(part.uv.Count==part.vertices.Count) mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
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
