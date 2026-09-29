using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    // Pure array work: no Unity objects or engine calls on the worker thread.
    internal static class MGTerrainSurfaceLodBuilder
    {
        internal sealed class Input
        {
            internal int width, height;
            internal Vector3[] vertices, normals;
            internal Vector4[] tangents;
            internal Color[] colors;
            internal Vector4[][] uv;
            internal int[][] triangles;
        }
        internal sealed class Level
        {
            internal int[] sourceIndices;
            internal int[][] triangles;
            internal float error;
            internal int triangleCount;
        }
        internal static Level[] Build(Input input, CancellationToken cancel)
        {
            int w = input.width, h = input.height, grid = w * h;
            if (w < 3 || h < 3 || grid > input.vertices.Length) return Array.Empty<Level>();
            var v = input.vertices;
            float dx = v[1].x - v[0].x, dz = v[w].z - v[0].z;
            if (dx <= 0 || dz <= 0) return Array.Empty<Level>();
            float tolerance = Math.Max(dx, dz) * .0001f;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    var p = v[z * w + x];
                    if (Math.Abs(p.x - (v[0].x + x * dx)) > tolerance || Math.Abs(p.z - (v[0].z + z * dz)) > tolerance)
                        return Array.Empty<Level>(); // Sculpted X/Z or arbitrary meshes retain their original surface.
                }
            var output = new List<Level>();
            int previousCount = 0;
            foreach (var indices in input.triangles) previousCount += indices.Length / 3;
            int[] strides = { 4, 8, 16 };
            float[] errors = { .125f, .5f, 2f };
            for (int i = 0; i < strides.Length; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var level = BuildLevel(input, strides[i], errors[i], cancel);
                if (level != null && level.triangleCount < previousCount * .95f)
                {
                    output.Add(level);
                    previousCount = level.triangleCount;
                }
            }
            return output.ToArray();
        }

        static Level BuildLevel(Input input, int stride, float limit, CancellationToken cancel)
        {
            int w = input.width, h = input.height, cw = w - 1, ch = h - 1;
            int nx = (cw + stride - 1) / stride, nz = (ch + stride - 1) / stride;
            var safe = new bool[nx * nz];
            var material = new int[safe.Length];
            var blockError = new float[safe.Length];
            var cellMask = new int[cw * ch];
            var cellMaterial = new int[cw * ch];
            var cellCount = new int[cw * ch];
            for (int i = 0; i < safe.Length; i++) { safe[i] = true; material[i] = -1; }
            // Only an intact, consistently wound, single-material grid quad may be replaced.
            for (int sub = 0; sub < input.triangles.Length; sub++)
            {
                var tris = input.triangles[sub];
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    if (TryCell(a, b, c, w, h, out int cell, out int mask))
                    {
                        int block = (cell / cw / stride) * nx + (cell % cw / stride);
                        if (cellCount[cell] > 0 && cellMaterial[cell] != sub) safe[block] = false;
                        if (SignedArea(input.vertices[a], input.vertices[b], input.vertices[c]) >= 0) safe[block] = false;
                        cellCount[cell]++;
                        cellMask[cell] |= 1 << mask;
                        cellMaterial[cell] = sub;
                    }
                    else
                    {
                        // Keep stitched borders and non-grid triangles, plus every block they touch.
                        var pa = input.vertices[a]; var pb = input.vertices[b]; var pc = input.vertices[c];
                        float dx = input.vertices[1].x - input.vertices[0].x;
                        float dz = input.vertices[w].z - input.vertices[0].z;
                        int x0 = Clamp((int)Math.Floor((Math.Min(pa.x, Math.Min(pb.x, pc.x)) - input.vertices[0].x) / dx / stride), 0, nx - 1);
                        int x1 = Clamp((int)Math.Floor((Math.Max(pa.x, Math.Max(pb.x, pc.x)) - input.vertices[0].x) / dx / stride), 0, nx - 1);
                        int z0 = Clamp((int)Math.Floor((Math.Min(pa.z, Math.Min(pb.z, pc.z)) - input.vertices[0].z) / dz / stride), 0, nz - 1);
                        int z1 = Clamp((int)Math.Floor((Math.Max(pa.z, Math.Max(pb.z, pc.z)) - input.vertices[0].z) / dz / stride), 0, nz - 1);
                        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) safe[z * nx + x] = false;
                    }
                }
            }
            // Corner masks for either valid diagonal: (7,14) or (11,13).
            for (int z = 0; z < ch; z++) for (int x = 0; x < cw; x++)
            {
                int cell = z * cw + x, block = (z / stride) * nx + x / stride;
                if (cellCount[cell] != 2 || (cellMask[cell] != ((1 << 7) | (1 << 14)) && cellMask[cell] != ((1 << 11) | (1 << 13)))) safe[block] = false;
                if (material[block] >= 0 && material[block] != cellMaterial[cell]) safe[block] = false;
                material[block] = cellMaterial[cell];
            }
            for (int bz = 0; bz < nz; bz++) for (int bx = 0; bx < nx; bx++)
            {
                cancel.ThrowIfCancellationRequested();
                int block = bz * nx + bx, x = bx * stride, z = bz * stride;
                if (x + stride > cw || z + stride > ch) { safe[block] = false; continue; }
                if (safe[block])
                {
                    blockError[block] = ErrorBound(input, x, z, stride, limit);
                    safe[block] = blockError[block] <= limit;
                }
            }
            var lists = new List<int>[input.triangles.Length];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<int>();
            for (int sub = 0; sub < lists.Length; sub++)
            {
                var tris = input.triangles[sub];
                for (int t = 0; t < tris.Length; t += 3)
                {
                    if (TryCell(tris[t], tris[t + 1], tris[t + 2], w, h, out int cell, out _)
                        && safe[(cell / cw / stride) * nx + cell % cw / stride]) continue;
                    lists[sub].Add(tris[t]); lists[sub].Add(tris[t + 1]); lists[sub].Add(tris[t + 2]);
                }
            }
            float error = 0;
            var perimeter = new List<int>();
            for (int bz = 0; bz < nz; bz++) for (int bx = 0; bx < nx; bx++)
            {
                int block = bz * nx + bx;
                if (!safe[block]) continue;
                error = Math.Max(error, blockError[block]);
                int x = bx * stride, z = bz * stride, center = (z + stride / 2) * w + x + stride / 2;
                perimeter.Clear();
                // Clockwise in X/Z, keeping EVERY edge vertex beside full-detail areas and tile boundaries.
                int step = bx == 0 || !safe[block - 1] ? 1 : stride;
                for (int k = 0; k < stride; k += step) perimeter.Add((z + k) * w + x);
                step = bz == nz - 1 || !safe[block + nx] ? 1 : stride;
                for (int k = 0; k < stride; k += step) perimeter.Add((z + stride) * w + x + k);
                step = bx == nx - 1 || !safe[block + 1] ? 1 : stride;
                for (int k = 0; k < stride; k += step) perimeter.Add((z + stride - k) * w + x + stride);
                step = bz == 0 || !safe[block - nx] ? 1 : stride;
                for (int k = 0; k < stride; k += step) perimeter.Add(z * w + x + stride - k);
                var list = lists[material[block]];
                for (int k = 0; k < perimeter.Count; k++) { list.Add(center); list.Add(perimeter[k]); list.Add(perimeter[(k + 1) % perimeter.Count]); }
            }
            var remap = new Dictionary<int, int>(); var sources = new List<int>();
            var result = new Level { triangles = new int[lists.Length][], error = error };
            for (int sub = 0; sub < lists.Length; sub++)
            {
                var list = lists[sub]; var indices = new int[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    int src = list[i];
                    if (!remap.TryGetValue(src, out int dst)) { dst = sources.Count; sources.Add(src); remap.Add(src, dst); }
                    indices[i] = dst;
                }
                result.triangles[sub] = indices;
                result.triangleCount += indices.Length / 3;
            }
            result.sourceIndices = sources.ToArray();
            return result;
        }

        static float ErrorBound(Input input, int x, int z, int s, float limit)
        {
            int w = input.width;
            int center = (z + s / 2) * w + x + s / 2;
            int[] corners = { z * w + x, (z + s) * w + x, (z + s) * w + x + s, z * w + x + s };
            float error = 0, planarBound = 0;
            // Attribute checks conservatively protect authored paint and UVs.
            // Fine boundary fans can add at most another copy of that deviation.
            for (int side = 0; side < 4; side++)
            {
                int a = center, b = corners[side], c = corners[(side + 1) % 4];
                var pa = input.vertices[a]; var pb = input.vertices[b]; var pc = input.vertices[c];
                float denominator = SignedArea(pa, pb, pc);
                for (int iz = z; iz <= z + s; iz++) for (int ix = x; ix <= x + s; ix++)
                {
                    int i = iz * w + ix; var p = input.vertices[i];
                    float wa = SignedArea(p, pb, pc) / denominator;
                    float wb = SignedArea(pa, p, pc) / denominator;
                    float wc = 1f - wa - wb;
                    planarBound = Math.Max(planarBound, 2f * Math.Abs(p.y - (pa.y * wa + pb.y * wb + pc.y * wc)));

                    // Preserve paint and custom UV channels; texture coordinates on regular grids are affine.
                    if (input.colors.Length != 0)
                    {
                        var d = (Vector4)input.colors[i] - ((Vector4)input.colors[a] * wa + (Vector4)input.colors[b] * wb + (Vector4)input.colors[c] * wc);
                        if (MaxAbs(d) > .01f) return float.PositiveInfinity;
                    }
                    for (int uv = 0; uv < input.uv.Length; uv++) if (input.uv[uv].Length != 0)
                    {
                        var data = input.uv[uv];
                        if (MaxAbs(data[i] - (data[a] * wa + data[b] * wb + data[c] * wc)) > .0001f) return float.PositiveInfinity;
                    }
                }
            }
            // Flat/near-planar blocks need no triangle clipping.
            if (planarBound <= limit) return planarBound;
            var polygon = new Vector3[8]; var scratch = new Vector3[8];
            for (int side = 0; side < 4; side++)
            {
                var a = input.vertices[center]; var b = input.vertices[corners[side]]; var c = input.vertices[corners[(side + 1) % 4]];
                for (int iz = z; iz < z + s; iz++) for (int ix = x; ix < x + s; ix++)
                {
                    int p = iz * w + ix;
                    // Bound either legal source diagonal. Clipping includes all plane intersections,
                    // where height error can peak even if every source vertex looks acceptable.
                    var p0 = input.vertices[p]; var p1 = input.vertices[p + 1];
                    var p2 = input.vertices[p + w]; var p3 = input.vertices[p + w + 1];
                    error = Math.Max(error, 2f * ClippedError(p0,p2,p1,a,b,c,polygon,scratch));
                    error = Math.Max(error, 2f * ClippedError(p1,p2,p3,a,b,c,polygon,scratch));
                    error = Math.Max(error, 2f * ClippedError(p0,p3,p1,a,b,c,polygon,scratch));
                    error = Math.Max(error, 2f * ClippedError(p0,p2,p3,a,b,c,polygon,scratch));
                    if (error > limit) return float.PositiveInfinity;
                }
            }
            return error;
        }
        static float ClippedError(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 a, Vector3 b, Vector3 c, Vector3[] polygon, Vector3[] scratch)
        {
            polygon[0]=p0; polygon[1]=p1; polygon[2]=p2; int count=3;
            count=Clip(polygon,count,scratch,a,b); if(count==0) return 0;
            count=Clip(scratch,count,polygon,b,c); if(count==0) return 0;
            count=Clip(polygon,count,scratch,c,a); if(count==0) return 0;
            float denominator=SignedArea(a,b,c), result=0;
            for(int i=0;i<count;i++)
            {
                var p=scratch[i]; float wa=SignedArea(p,b,c)/denominator, wb=SignedArea(a,p,c)/denominator;
                result=Math.Max(result,Math.Abs(p.y-(a.y*wa+b.y*wb+c.y*(1-wa-wb))));
            }
            return result;
        }
        static int Clip(Vector3[] input, int count, Vector3[] output, Vector3 a, Vector3 b)
        {
            int n=0; var previous=input[count-1]; float previousSide=SignedArea(a,b,previous);
            for(int i=0;i<count;i++)
            {
                var current=input[i]; float side=SignedArea(a,b,current);
                if((side<=0)!=(previousSide<=0))
                    output[n++]=previous+(current-previous)*(previousSide/(previousSide-side));
                if(side<=0) output[n++]=current;
                previous=current; previousSide=side;
            }
            return n;
        }
        static float MaxAbs(Vector4 v) => Math.Max(Math.Max(Math.Abs(v.x), Math.Abs(v.y)), Math.Max(Math.Abs(v.z), Math.Abs(v.w)));
        static float SignedArea(Vector3 a, Vector3 b, Vector3 c) => (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
        static int Clamp(int n, int min, int max) => Math.Min(max, Math.Max(min, n));
        static bool TryCell(int a, int b, int c, int w, int h, out int cell, out int mask)
        {
            cell = mask = 0;
            if (a < 0 || b < 0 || c < 0 || a >= w * h || b >= w * h || c >= w * h) return false;
            int x0 = Math.Min(a % w, Math.Min(b % w, c % w)), z0 = Math.Min(a / w, Math.Min(b / w, c / w));
            int x1 = Math.Max(a % w, Math.Max(b % w, c % w)), z1 = Math.Max(a / w, Math.Max(b / w, c / w));
            if (x1 - x0 != 1 || z1 - z0 != 1) return false;
            int ma = 1 << ((a / w - z0) * 2 + a % w - x0);
            int mb = 1 << ((b / w - z0) * 2 + b % w - x0);
            int mc = 1 << ((c / w - z0) * 2 + c % w - x0);
            mask = ma | mb | mc; cell = z0 * (w - 1) + x0;
            return ma != mb && mb != mc && ma != mc;
        }
    }
}
