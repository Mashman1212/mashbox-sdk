using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [Serializable] public sealed class StudioSweepSettings
    {
        public string sourceId;
        public int profileShape;
        public bool align;
        public int horizontalAlignment, verticalAlignment;
        public float horizontalOffset, verticalOffset;
        public float profileWidth=1, profileHeight=1;
        public string profileId, profileAncestorId;
        public Matrix4x4 profileFrame=Matrix4x4.identity, profileAncestorBind=Matrix4x4.identity, profileAncestorLast=Matrix4x4.identity;
        public int[] profileVertices, profileTopology;
        public bool profileClosed;
        public Vector3 profileX, profileY, profileWorldX, profileWorldY;
        internal void ClampFields()
        {
            horizontalAlignment=Mathf.Clamp(horizontalAlignment,0,2);verticalAlignment=Mathf.Clamp(verticalAlignment,0,2);
            horizontalOffset=float.IsFinite(horizontalOffset)?Mathf.Clamp(horizontalOffset,-1000000,1000000):0;
            verticalOffset=float.IsFinite(verticalOffset)?Mathf.Clamp(verticalOffset,-1000000,1000000):0;
            sides=Mathf.Clamp(sides,4,256);subdivisions=Mathf.Clamp(subdivisions,1,4096);
            profileWidth=float.IsFinite(profileWidth)?Mathf.Clamp(profileWidth,.000001f,1000000f):1;
            profileHeight=float.IsFinite(profileHeight)?Mathf.Clamp(profileHeight,.000001f,1000000f):1;
            radius=float.IsFinite(radius)?Mathf.Clamp(radius,.000001f,1000000f):.1f;
            rotation=float.IsFinite(rotation)?Mathf.Repeat(rotation+180,360)-180:0;
            twist=float.IsFinite(twist)?Mathf.Clamp(twist,-360000,360000):0;
            if(taper==null||taper.length==0)taper=AnimationCurve.Linear(0,1,1,1);
            var keys=taper.keys;
            for(int i=0;i<keys.Length;i++)
            {keys[i].time=float.IsFinite(keys[i].time)?Mathf.Clamp01(keys[i].time):0;keys[i].value=float.IsFinite(keys[i].value)?Mathf.Clamp(keys[i].value,.000001f,10):1;if(!float.IsFinite(keys[i].inTangent))keys[i].inTangent=0;if(!float.IsFinite(keys[i].outTangent))keys[i].outTangent=0;}
            taper.keys=keys.GroupBy(k=>k.time).Select(g=>g.Last()).OrderBy(k=>k.time).ToArray();
        }
        public int sides = 8, subdivisions = 32;
        public float radius = .1f, rotation, twist;
        public AnimationCurve taper = AnimationCurve.Linear(0, 1, 1, 1);
        public bool capStart = true, capEnd = true;
        // Maps world-space generation into the output's creation frame. Ordinary
        // output transforms compose after generation; source transforms apply once.
        public Matrix4x4 worldToCreation = Matrix4x4.identity;
        public string ancestorId;
        public Matrix4x4 sourceFrame = Matrix4x4.identity, ancestorBind = Matrix4x4.identity, ancestorLast = Matrix4x4.identity;
        public Vector3 referenceTangent, referenceNormal;
        public StudioSweepSettings Copy() => JsonUtility.FromJson<StudioSweepSettings>(JsonUtility.ToJson(this));
        internal void Validate(bool closed)
        {
            if(horizontalAlignment<0||horizontalAlignment>2||verticalAlignment<0||verticalAlignment>2||!float.IsFinite(horizontalOffset)||!float.IsFinite(verticalOffset)||Mathf.Abs(horizontalOffset)>1000000||Mathf.Abs(verticalOffset)>1000000)
                throw new InvalidOperationException("Sweep alignment requires valid anchors and finite offsets within �1000000 metres.");
            if (sides < 4 || sides > 256 || subdivisions < 1 || subdivisions > 4096 ||
                !float.IsFinite(radius) || radius < .000001f || radius > 1000000f || !float.IsFinite(rotation) || !float.IsFinite(twist) || Mathf.Abs(twist)>360000 || taper == null || taper.length == 0)
                throw new InvalidOperationException("Sweep requires 4–256 profile sides, 1–4096 length subdivisions, a positive radius and finite angles/taper.");
            foreach (var k in taper.keys)
                if (!float.IsFinite(k.time) || !float.IsFinite(k.value) || k.value < .000001f || k.value > 10 || k.time < 0 || k.time > 1 || !float.IsFinite(k.inTangent) || !float.IsFinite(k.outTangent))
                    throw new InvalidOperationException("Sweep taper keys and tangents must be finite, with positive values.");
            if (closed && (Mathf.Abs(taper.Evaluate(0)-taper.Evaluate(1)) > 1e-5f || Mathf.Abs(twist/360-Mathf.Round(twist/360)) > 1e-5f))
                throw new InvalidOperationException("Closed sweeps require equal taper endpoints and Total Twist in whole 360-degree turns.");
        }
    }

    internal static class StudioSweep
    {
        internal sealed class TopologyCache
        {
            internal string key;
            internal int[] polygons, triangles, identities;
        }
        internal sealed class Path
        {
            internal readonly List<Vector3> points = new List<Vector3>(), tangent = new List<Vector3>(), normal = new List<Vector3>();
            internal readonly List<float> distances = new List<float>();
            internal float length;
        }
        private struct Sample { internal Vector3 point; internal int segment; internal float t, distance; }
        // Arc-length lookup is independent of the viewport's display tessellation.
        internal static Path SamplePath(StudioSpline spline, Matrix4x4 world, int subdivisions)
        {
            spline.Validate();
            var lookup = new List<Sample>(); var boundaries = new List<float>(); float length = 0;
            Vector3 previous = world.MultiplyPoint3x4(spline.Evaluate(0,0));
            lookup.Add(new Sample { point=previous, segment=0 });
            for (int segment=0; segment<spline.SegmentCount; segment++)
            {
                int steps=spline.kind==StudioSplineKind.Linear?1:256;
                for(int j=1;j<=steps;j++)
                {
                    float t=(float)j/steps;var point=world.MultiplyPoint3x4(spline.Evaluate(segment,t));
                    if(!StudioSpline.Finite(point))throw new InvalidOperationException("Sweep source transform must be finite.");
                    length+=Vector3.Distance(previous,point);previous=point;
                    lookup.Add(new Sample{point=point,segment=segment,t=t,distance=length});
                }
                if(spline.kind==StudioSplineKind.Linear)boundaries.Add(length);
            }
            if(!float.IsFinite(length)||length<=1e-7f)throw new InvalidOperationException("Sweep source has no usable length.");
            var distances=new List<float>();int count=Math.Max(subdivisions,spline.closed?3:1);
            for(int i=0;i<=count;i++)distances.Add(length*i/count);
            distances.AddRange(boundaries);distances.Sort();
            var path=new Path{length=length};int cursor=1;
            foreach(float d in distances)
            {
                if(spline.closed&&d>=length-1e-7f)continue;
                if(path.distances.Count>0&&d-path.distances[path.distances.Count-1]<length*1e-7f)continue;
                while(cursor<lookup.Count-1&&lookup[cursor].distance<d)cursor++;
                var a=lookup[cursor-1];var b=lookup[cursor];
                float f=b.distance>a.distance?(d-a.distance)/(b.distance-a.distance):0;
                float start=a.segment==b.segment?a.t:0;
                var point=world.MultiplyPoint3x4(spline.Evaluate(b.segment,Mathf.Lerp(start,b.t,f)));
                path.points.Add(point);path.distances.Add(d);
            }
            int n=path.points.Count;
            if(n<(spline.closed?3:2))throw new InvalidOperationException("Sweep source has too few distinct samples.");
            for(int i=0;i<n;i++)
            {
                var before=path.points[i]-path.points[(i+n-1)%n];var after=path.points[(i+1)%n]-path.points[i];
                if(!spline.closed&&i==0)before=after;if(!spline.closed&&i==n-1)after=before;
                if(before.sqrMagnitude<1e-18f||after.sqrMagnitude<1e-18f||Vector3.Dot(before.normalized,after.normalized)<-.999f)
                    throw new InvalidOperationException("Sweep cannot frame a repeated sample or reversing cusp. Repair the source curve.");
                path.tangent.Add((before.normalized+after.normalized).normalized);
            }
            var tangent=path.tangent[0];var axis=Mathf.Abs(tangent.y)<.9f?Vector3.up:Vector3.right;
            path.normal.Add(Vector3.Cross(tangent,axis).normalized);
            for(int i=1;i<n;i++)path.normal.Add(Transport(path.normal[i-1],path.tangent[i-1],path.tangent[i]));
            if(spline.closed)
            {
                var seam=Transport(path.normal[n-1],path.tangent[n-1],path.tangent[0]);
                float correction=Vector3.SignedAngle(seam,path.normal[0],path.tangent[0]);
                for(int i=1;i<n;i++)path.normal[i]=Quaternion.AngleAxis(correction*path.distances[i]/length,path.tangent[i])*path.normal[i];
            }
            return path;
        }
        private static Vector3 Transport(Vector3 normal,Vector3 from,Vector3 to)
        {
            if(Vector3.Dot(from,to)<-.9999f)throw new InvalidOperationException("Sweep cannot transport a frame through a reversing cusp.");
            var transported=Quaternion.FromToRotation(from,to)*normal;
            return (transported-to*Vector3.Dot(transported,to)).normalized;
        }
        internal static StudioModelDocument.Node Generate(StudioSpline spline,Matrix4x4 sourceWorld,StudioSweepSettings settings,TopologyCache cache=null, Vector3[] profile=null)
        {
            settings.Validate(spline.closed);
            if(settings.profileShape==3&&profile==null)throw new InvalidOperationException("Assign live profile edges or faces before generating the sweep.");
            if(profile==null&&settings.profileShape==1)profile=new[]{new Vector3(-settings.profileWidth*.5f,0,0),new Vector3(settings.profileWidth*.5f,0,0)};
            if(profile==null&&settings.profileShape==2)profile=new[]{new Vector2(-settings.profileWidth*.5f,-settings.profileHeight*.5f),new Vector2(settings.profileWidth*.5f,-settings.profileHeight*.5f),new Vector2(settings.profileWidth*.5f,settings.profileHeight*.5f),new Vector2(-settings.profileWidth*.5f,settings.profileHeight*.5f)}.Select(v=>(Vector3)v).ToArray();
            if(settings.profileShape<0||settings.profileShape>3||!float.IsFinite(settings.profileWidth)||settings.profileWidth<.000001f||settings.profileWidth>1000000||!float.IsFinite(settings.profileHeight)||settings.profileHeight<.000001f||settings.profileHeight>1000000)
                throw new InvalidOperationException("Sweep profile dimensions must be finite and between 0.000001 and 1000000 metres.");
            var path=SamplePath(spline,sourceWorld,settings.subdivisions);
            if(!float.IsFinite(sourceWorld.determinant)||Mathf.Abs(sourceWorld.determinant)<1e-12f||!float.IsFinite(settings.worldToCreation.determinant)||Mathf.Abs(settings.worldToCreation.determinant)<1e-12f)
                throw new InvalidOperationException("Sweep source and creation frames must be finite and invertible.");
            if(settings.referenceTangent.sqrMagnitude<1e-12f)
            {settings.referenceTangent=sourceWorld.inverse.MultiplyVector(path.tangent[0]).normalized;settings.referenceNormal=sourceWorld.inverse.MultiplyVector(path.normal[0]).normalized;}
            var reference=sourceWorld.MultiplyVector(settings.referenceTangent).normalized;
            var referenceNormal=sourceWorld.MultiplyVector(settings.referenceNormal).normalized;
            referenceNormal=Vector3.Dot(reference,path.tangent[0])<-.9999f?referenceNormal:Quaternion.FromToRotation(reference,path.tangent[0])*referenceNormal;
            referenceNormal=(referenceNormal-path.tangent[0]*Vector3.Dot(referenceNormal,path.tangent[0])).normalized;
            float roll=Vector3.SignedAngle(path.normal[0],referenceNormal,path.tangent[0]);
            // SamplePath's binormal points down. Live profiles use a captured upright basis.
            if(settings.profileShape==3)roll+=180;
            for(int i=0;i<path.normal.Count;i++)path.normal[i]=Quaternion.AngleAxis(roll,path.tangent[i])*path.normal[i];
            bool profileClosed=settings.profileShape==0||settings.profileShape==2||settings.profileShape==3&&settings.profileClosed;
            int rings=path.points.Count,sides=profile==null?settings.sides:profile.Length-(profileClosed?0:1),stride=sides+1,renderRings=rings+(spline.closed?1:0);
            int profileCount=profileClosed?sides:stride;
            if(sides<1)throw new InvalidOperationException("Sweep profile needs at least two distinct points.");
            var alignment=Vector3.zero;
            if(settings.align)
            {
                // UI axes are right/up; built-in profiles use the opposite transported axes.
                float direction=settings.profileShape==3?1:-1;
                var bounds=profile??Enumerable.Range(0,sides).Select(i=>settings.radius*new Vector3(Mathf.Cos(2*Mathf.PI*i/sides),Mathf.Sin(2*Mathf.PI*i/sides),0)).ToArray();
                float left=bounds.Min(p=>direction*p.x),right=bounds.Max(p=>direction*p.x),bottom=bounds.Min(p=>direction*p.y),top=bounds.Max(p=>direction*p.y);
                float x=settings.horizontalAlignment==1?left:settings.horizontalAlignment==2?right:(left+right)*.5f;
                float y=settings.verticalAlignment==1?top:settings.verticalAlignment==2?bottom:(bottom+top)*.5f;
                alignment=new Vector3(direction*(settings.horizontalOffset-x),direction*(settings.verticalOffset-y),0);
            }
            string key=settings.profileShape+":"+profileClosed+":"+rings+":"+sides+":"+spline.closed+":"+settings.capStart+":"+settings.capEnd;
            bool reuse=profile==null&&cache!=null&&cache.key==key;
            if((long)renderRings*stride>2000000)throw new InvalidOperationException("Sweep exceeds two million render vertices. Reduce subdivisions or profile sides.");
            var points=new List<Vector3>();var uv=new List<Vector2>();var identities=new List<int>();
            var triangles=new List<int>();var polygons=new List<int>();
            void Face(params int[] face){polygons.Add(0);polygons.Add(face.Length);polygons.AddRange(face);}
            for(int r=0;r<renderRings;r++)
            {
                int ring=r%rings;float v=r==rings?1:path.distances[ring]/path.length;
                float taper=settings.taper.Evaluate(v);
                if(!float.IsFinite(taper)||taper<.000001f||taper>10)throw new InvalidOperationException("Sweep taper must remain positive along the entire sampled curve.");
                var normal=path.normal[ring];var binormal=Vector3.Cross(path.tangent[ring],normal);
                for(int s=0;s<=sides;s++)
                {
                    int corner=profileClosed?s%sides:s;
                    var xy=profile==null?settings.radius*new Vector3(Mathf.Cos(2*Mathf.PI*corner/sides),Mathf.Sin(2*Mathf.PI*corner/sides),0):profile[corner];
                    xy+=alignment;
                    float angle=(settings.rotation+settings.twist*v)*Mathf.Deg2Rad;
                    var rotated=new Vector2(xy.x*Mathf.Cos(angle)-xy.y*Mathf.Sin(angle),xy.x*Mathf.Sin(angle)+xy.y*Mathf.Cos(angle));
                    var p=path.points[ring]+taper*(rotated.x*normal+rotated.y*binormal+xy.z*path.tangent[ring]);
                    points.Add(settings.worldToCreation.MultiplyPoint3x4(p));uv.Add(new Vector2((float)s/sides,v));identities.Add(ring*profileCount+(profileClosed?s%sides:s));
                }
            }
            if(!reuse)for(int r=0;r<renderRings-1;r++)for(int s=0;s<sides;s++)
            {
                int a=r*stride+s,b=a+1,c=b+stride,d=a+stride;
                Face(a,b,c,d);triangles.AddRange(new[]{a,b,c,a,c,d});
            }
            void Cap(bool end)
            {
                int ring=end?rings-1:0;var face=new List<int>();
                for(int j=0;j<sides;j++)
                {
                    int s=end?j:sides-1-j;face.Add(points.Count);points.Add(points[ring*stride+s]);
                    float angle=2*Mathf.PI*s/sides;uv.Add(profile==null?new Vector2(.5f+.5f*Mathf.Cos(angle),.5f+.5f*Mathf.Sin(angle)):(Vector2)profile[s]);identities.Add(ring*sides+s);
                }
                if(reuse)return;
                Face(face.ToArray());var normal=Vector3.zero;
                for(int j=0;j<sides;j++)normal+=Vector3.Cross(points[face[j]]-points[face[0]],points[face[(j+1)%sides]]-points[face[0]]);
                StudioMeshCuts.TriangulateSliceCap(face,points,normal.normalized,triangles);
            }
            if(!spline.closed&&profileClosed){if(settings.capStart)Cap(false);if(settings.capEnd)Cap(true);}
            if(points.Any(p=>!StudioSpline.Finite(p)))throw new InvalidOperationException("Sweep coordinates overflowed. Reduce the radius, taper or source scale.");
            int[] indices=reuse?cache.triangles:triangles.ToArray(), authored=reuse?cache.polygons:polygons.ToArray(), geometry=reuse?cache.identities:identities.ToArray();
            // Boundary traversal and reflected profiles must not turn a closed solid inside out.
            if(profile!=null&&profileClosed)
            {
                float area=0;for(int i=0;i<profile.Length;i++){var a=profile[i];var b=profile[(i+1)%profile.Length];area+=a.x*b.y-b.x*a.y;}
                if(area*settings.worldToCreation.determinant<0)
                {
                    for(int i=0;i<indices.Length;i+=3){int swap=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=swap;}
                    for(int i=0;i<authored.Length;){int count=authored[i+1];Array.Reverse(authored,i+2,count);i+=count+2;}
                }
            }
            for(int i=0;i<indices.Length;i+=3)
                if(Vector3.Cross(points[indices[i+1]]-points[indices[i]],points[indices[i+2]]-points[indices[i]]).sqrMagnitude<1e-24f)
                    throw new InvalidOperationException("Sweep contains degenerate faces. Adjust the curve, radius or taper.");
            var mesh=new Mesh{name="Sweep Mesh",indexFormat=points.Count>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16};
            try
            {
                mesh.SetVertices(points);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();
                var normals=mesh.normals;var sums=new Vector3[rings*profileCount];
                for(int i=0;i<renderRings*stride;i++)sums[identities[i]]+=normals[i];
                for(int i=0;i<renderRings*stride;i++)normals[i]=sums[identities[i]].normalized;
                mesh.normals=normals;mesh.RecalculateTangents();mesh.RecalculateBounds();
                if(cache!=null&&!reuse){cache.key=key;cache.triangles=indices;cache.polygons=authored;cache.identities=geometry;}
                return new StudioModelDocument.Node{mesh=mesh,cutPolygons=authored,cutTriangles=indices,geometryVertices=geometry};
            }
            catch{UnityEngine.Object.DestroyImmediate(mesh);throw;}
        }
    }
}
