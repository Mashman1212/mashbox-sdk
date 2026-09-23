#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEngine;
namespace MashBoxSDK.MapTools
{
    internal static partial class MGTerrainTileAuthoring
    {
        internal static IEnumerable<(Vector2Int direction, Bounds bounds)> PlacementBounds(MGTerrain source, float size)
        {
            var world = source.GetComponentInParent<MGTerrainWorld>(true);
            if (world == null || size <= 0) yield break;
            var b = BoundsOf(source); var origin = world.transform.position;
            foreach (var d in new[]{Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up})
            {
                bool vertical = d.x != 0;
                float min = vertical ? b.min.z-origin.z : b.min.x-origin.x;
                float max = vertical ? b.max.z-origin.z : b.max.x-origin.x;
                float edge = vertical ? (d.x < 0 ? b.min.x-origin.x-size : b.max.x-origin.x)
                    : (d.y < 0 ? b.min.z-origin.z-size : b.max.z-origin.z);
                float fixedMin = edge;
                for (int cell = Mathf.FloorToInt((min+.001f)/size); cell <= Mathf.FloorToInt((max-.001f)/size); cell++)
                {
                    var start = new Vector3(origin.x + (vertical ? fixedMin : cell*size), b.center.y,
                        origin.z + (vertical ? cell*size : fixedMin));
                    yield return (d, new Bounds(start + new Vector3(size*.5f,0,size*.5f),new Vector3(size,b.size.y,size)));
                }
            }
        }
        internal static void BuildScaledGeometry(MGTerrain source, Bounds destination, IReadOnlyList<MGTerrain> neighbours,
            int width, int height, out Vector3[] output, out Vector2[] uv, out int[] indices)
        {
            if (width < 2 || height < 2 || (long)width*height > 4000000) throw new InvalidOperationException("Invalid background tile grid resolution.");
            float baseY = source.MeshFilter.transform.position.y;
            var sides = new SortedDictionary<float,float>[4];
            for(int side=0;side<4;side++) sides[side]=new SortedDictionary<float,float>();
            void Sample(int side,float position,float y)
            {
                if(sides[side].TryGetValue(position,out float previous) && Mathf.Abs(previous-y)>.002f)
                    throw new InvalidOperationException("Existing tile borders disagree in height. Repair their seam before extending.");
                sides[side][position]=y;
            }
            const float tolerance=.001f;
            foreach(var tile in neighbours)
            {
                var mesh=tile.MeshFilter.sharedMesh;var bounds=mesh.bounds;
                var b=BoundsOf(tile);
                if(b.max.x<destination.min.x-tolerance || b.min.x>destination.max.x+tolerance
                    ||b.max.z<destination.min.z-tolerance ||b.min.z>destination.max.z+tolerance) continue;
                foreach(var v in mesh.vertices)
                {
                    if(!Border(v,bounds))continue;
                    var p=tile.MeshFilter.transform.TransformPoint(v);
                    float x=p.x-destination.min.x, z=p.z-destination.min.z, y=p.y-baseY;
                    if(x < -tolerance || x>destination.size.x+tolerance || z < -tolerance || z>destination.size.z+tolerance)continue;
                    x=Mathf.Clamp(x,0,destination.size.x);z=Mathf.Clamp(z,0,destination.size.z);
                    if(Mathf.Abs(x)<tolerance)Sample(0,z,y);
                    if(Mathf.Abs(x-destination.size.x)<tolerance)Sample(1,z,y);
                    if(Mathf.Abs(z)<tolerance)Sample(2,x,y);
                    if(Mathf.Abs(z-destination.size.z)<tolerance)Sample(3,x,y);
                }
            }
            var samples=sides.Select(s=>s.ToArray()).ToArray();
            float Interpolate(int side,float position)
            {
                var points=samples[side];
                if(position<=points[0].Key)return points[0].Value;
                if(position>=points[points.Length-1].Key)return points[points.Length-1].Value;
                int lo=0,hi=points.Length-1;
                while(hi-lo>1){int mid=(lo+hi)/2;if(points[mid].Key<=position)lo=mid;else hi=mid;}
                return Mathf.Lerp(points[lo].Value,points[hi].Value,Mathf.InverseLerp(points[lo].Key,points[hi].Key,position));
            }
            var vertices=new List<Vector3>(width*height); var uvs=new List<Vector2>(width*height);
            int Add(Vector3 p) {int index=vertices.Count;vertices.Add(p);uvs.Add(new Vector2(p.x/destination.size.x,p.z/destination.size.z));return index;}
            for(int z=0;z<height;z++)for(int x=0;x<width;x++)
            {
                float px=destination.size.x*x/(width-1),pz=destination.size.z*z/(height-1);
                float sum=0,weight=0;
                for(int side=0;side<4;side++)
                {
                    if(samples[side].Length==0)continue;
                    float distance=side==0?px:side==1?destination.size.x-px:side==2?pz:destination.size.z-pz;
                    float along=side<2?pz:px;
                    bool contact=along>=samples[side][0].Key-tolerance && along<=samples[side][samples[side].Length-1].Key+tolerance;
                    if(distance<tolerance && contact) {sum=Interpolate(side,along);weight=1;break;}
                    float w=1/Mathf.Max(.01f,distance);sum+=Interpolate(side,along)*w;weight+=w;
                }
                Add(new Vector3(px,weight>0?sum/weight:BoundsOf(source).center.y-baseY,pz));
            }
            // Retain every existing fine border sample; only the rim needs more geometry.
            var splits=new Dictionary<(int,int),List<int>>();
            for(int side=0;side<4;side++)foreach(var sample in samples[side])
            {
                int count=side<2?height:width;float length=side<2?destination.size.z:destination.size.x;
                float f=sample.Key/length*(count-1);int nearest=Mathf.RoundToInt(f);
                int Grid(int i)=>side==0?i*width:side==1?i*width+width-1:side==2?i:(height-1)*width+i;
                if(Mathf.Abs(f-nearest)<.0001f)continue;
                int segment=Mathf.Clamp(Mathf.FloorToInt(f),0,count-2);
                var key=(Grid(segment),Grid(segment+1));
                if(!splits.TryGetValue(key,out var list))splits.Add(key,list=new List<int>());
                list.Add(Add(new Vector3(side==0?0:side==1?destination.size.x:sample.Key,sample.Value,
                    side==2?0:side==3?destination.size.z:sample.Key)));
            }
            var triangles=new List<int>((width-1)*(height-1)*6);
            var polygon=new List<int>();
            void Triangle(int a,int b,int c)
            {
                polygon.Clear();
                void Edge(int first,int last)
                {
                    polygon.Add(first);
                    if(splits.TryGetValue((first,last),out var forward))polygon.AddRange(forward);
                    else if(splits.TryGetValue((last,first),out var reverse))for(int i=reverse.Count-1;i>=0;i--)polygon.Add(reverse[i]);
                }
                Edge(a,b);Edge(b,c);Edge(c,a);
                if(polygon.Count==3){triangles.Add(a);triangles.Add(b);triangles.Add(c);return;}
                int center=Add((vertices[a]+vertices[b]+vertices[c])/3);
                for(int i=0;i<polygon.Count;i++){triangles.Add(center);triangles.Add(polygon[i]);triangles.Add(polygon[(i+1)%polygon.Count]);}
            }
            for(int z=0;z<height-1;z++)for(int x=0;x<width-1;x++)
            {int i=z*width+x;Triangle(i,i+width,i+1);Triangle(i+1,i+width,i+width+1);}
            output=vertices.ToArray();uv=uvs.ToArray();indices=triangles.ToArray();
        }
    }
}
#endif
