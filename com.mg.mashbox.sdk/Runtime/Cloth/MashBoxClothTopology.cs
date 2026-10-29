using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace MashBoxSDK.Cloth
{
    // Built once. Spatial clustering reduces the solve without reducing the visible mesh.
    // Connected islands are kept separate so nearby sleeves/legs cannot become stitched together.
    public sealed class MashBoxClothTopology
    {
        public readonly int[] vertexParticle, counts;
        public readonly int2[] edges;
        public readonly bool[] bending;
        public int ParticleCount => counts.Length;

        public MashBoxClothTopology(Vector3[] vertices, int[] triangles, int budget)
        {
            if (vertices == null || vertices.Length == 0) throw new ArgumentException("The cloth mesh has no vertices.");
            budget = Mathf.Clamp(budget, 32, 8192);
            var parents = new int[vertices.Length];
            for (int i = 0; i < parents.Length; i++) parents[i] = i;
            int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
            for (int i = 0; i < triangles.Length; i += 3)
            { parents[Root(triangles[i + 1])] = Root(triangles[i]); parents[Root(triangles[i + 2])] = Root(triangles[i]); }
            // Importers split vertices at UV/normal seams. Coincident positions must stay sewn
            // while spatially separated islands (even nearby sleeves) remain independent.
            var seams = new Dictionary<Vector3, int>();
            for (int i = 0; i < vertices.Length; i++)
                if (seams.TryGetValue(vertices[i], out int previous)) parents[Root(i)] = Root(previous);
                else seams.Add(vertices[i], i);
            for (int i = 0; i < parents.Length; i++) parents[i] = Root(i);
            if (new HashSet<int>(parents).Count > budget) throw new ArgumentException("More disconnected mesh islands than the particle budget. Increase the budget or weld the garment.");
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var v in vertices) bounds.Encapsulate(v);
            float cell = Mathf.Max(.000001f, bounds.size.magnitude / Mathf.Pow(budget, 1f / 3f) * .15f);
            vertexParticle = new int[vertices.Length];
            var groups = new Dictionary<int, int>();
            var cells = new Vector3Int[vertices.Length];
            var clusters = new int[vertices.Length];
            int Cluster(int i) { while(clusters[i]!=i){clusters[i]=clusters[clusters[i]];i=clusters[i];}return i; }
            void Join(int a,int b) { if(cells[a]==cells[b])clusters[Cluster(b)]=Cluster(a); }
            // Only contract actual mesh edges within a cell. Whole-island spatial
            // buckets incorrectly sew together nearby legs and folded layers.
            for (int attempt = 0; ; attempt++)
            {
                groups.Clear();
                for(int i=0;i<vertices.Length;i++){
                    clusters[i]=i;var v=(vertices[i]-bounds.min)/cell;
                    cells[i]=new Vector3Int(Mathf.FloorToInt(v.x),Mathf.FloorToInt(v.y),Mathf.FloorToInt(v.z));
                }
                for(int i=0;i<vertices.Length;i++)Join(i,seams[vertices[i]]);
                for(int i=0;i<triangles.Length;i+=3){Join(triangles[i],triangles[i+1]);Join(triangles[i+1],triangles[i+2]);Join(triangles[i+2],triangles[i]);}
                for(int i=0;i<vertices.Length;i++){
                    int root=Cluster(i);
                    if(!groups.TryGetValue(root,out int id)){id=groups.Count;groups.Add(root,id);}
                    vertexParticle[i]=id;
                }
                if(groups.Count<=budget)break;
                if(attempt==100)throw new ArgumentException("Unable to reduce this mesh to the requested cloth budget.");
                cell*=1.25f;
            }
            counts = new int[groups.Count];
            foreach (int p in vertexParticle) counts[p]++;
            var links = new List<int2>(); var bends = new List<bool>();
            var used = new HashSet<ulong>(); var opposite = new Dictionary<ulong, int>();
            ulong Key(int a, int b) => ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            void Add(int a, int b, bool bend)
            {
                if (a != b && used.Add(Key(a, b))) { links.Add(new int2(a, b)); bends.Add(bend); }
            }
            void Side(int a, int b, int c)
            {
                ulong key = Key(a, b);
                if (opposite.TryGetValue(key, out int other)) Add(vertexParticle[other], vertexParticle[c], true);
                else opposite.Add(key, c);
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = vertexParticle[triangles[i]], b = vertexParticle[triangles[i + 1]], c = vertexParticle[triangles[i + 2]];
                Add(a, b, false); Add(b, c, false); Add(c, a, false);
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = vertexParticle[triangles[i]], b = vertexParticle[triangles[i + 1]], c = vertexParticle[triangles[i + 2]];
                if (a == b || b == c || c == a) continue;
                int va=seams[vertices[triangles[i]]],vb=seams[vertices[triangles[i+1]]],vc=seams[vertices[triangles[i+2]]];
                Side(va, vb, vc); Side(vb, vc, va); Side(vc, va, vb);
            }
            edges = links.ToArray(); bending = bends.ToArray();
        }
    }
}
