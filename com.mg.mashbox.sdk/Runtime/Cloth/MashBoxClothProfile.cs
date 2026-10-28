using System;
using UnityEngine;
using Unity.Mathematics;

namespace MashBoxSDK.Cloth
{
    [CreateAssetMenu(menuName = "MashBox/Cloth Profile", fileName = "Cloth Profile")]
    public sealed class MashBoxClothProfile : ScriptableObject
    {
        public Mesh sourceMesh;
        [Tooltip("Per render vertex movement allowance in metres. Zero pins to the animated pose.")]
        public float[] motion = Array.Empty<float>();
        public ClothBoneCapsule[] bodyCapsules = Array.Empty<ClothBoneCapsule>();
        public bool bodyCollisions = true;
        [Tooltip("Use skinning to restrict each cloth region to its associated body bones. Unskinned meshes use all body colliders.")]
        public bool localBoneCollisions = true;
        [Tooltip("Keep particles outside spheres behind the animated mesh surface. Uses the animated mesh normals; independent of body capsules.")]
        public bool backstop;
        [Min(0), Tooltip("Inward travel allowed before contacting the backstop, in metres.")]
        public float backstopDistance = .005f;
        [Min(.001f), Tooltip("Radius of the sphere behind each particle, in metres. Larger spheres block a wider area.")]
        public float backstopRadius = .05f;
        [Range(0, 1), Tooltip("Tangential velocity removed on body contact.")] public float collisionFriction;
        [Range(32, 8192)] public int particleBudget = 1500;
        [Range(15, 120)] public int frequency = 60;
        [Tooltip("Blend between physics steps at the display update rate. Adds one physics step of visual latency without additional solver steps.")]
        public bool interpolateMotion = true;
        [Range(1, 8)] public int substeps = 2;
        [Range(1, 16)] public int iterations = 4;
        [Min(0)] public float stretchCompliance = 0.000001f;
        [Min(0)] public float bendCompliance = 0.001f;
        [Range(0, 20)] public float damping = 3;
        [Range(0, 1), Tooltip("Follow the animated bones and restore the animated shape. Zero retains full world-space inertia; one follows closely while allowing cloth motion.")]
        public float animationFollow = 1;
        [Range(0, 1), Tooltip("Inertia from changes in animation velocity. Higher gives more lag on acceleration and overshoot on stopping, even with Animation Follow enabled. Zero disables this extra inertia.")]
        public float animationInertia = .5f;
        [Range(0, 1), Tooltip("Additional soft support toward the animated shape. Zero disables it; higher values reduce sag and bunching. Pins and collision still take priority.")]
        public float shapeRetention;
        [Range(0, 2), Tooltip("Damping of shape recovery. Around 0.7 settles gently; lower values allow more bounce.")]
        public float shapeDamping = .7f;
        public Vector3 gravity = new Vector3(0, -9.81f, 0);
        public Vector3 wind = new Vector3(0, 0, 4);
        [Range(0, 1)] public float gust = .25f;
        [Range(0.001f, .05f)] public float thickness = .008f;
        [Min(.1f)] public float teleportDistance = 2;
        [Tooltip("Keep imported smooth shading, updated by the character animation. Cloth folds do not rebuild normals from triangles.")]
        public bool preserveAuthoredShading = true;
        [Tooltip("When preserving authored shading is off, recalculate normals at this physics-step interval. Zero uses animated imported shading.")]
        [Range(0, 60)] public int normalsEverySteps = 2;

        public bool Matches(Mesh mesh) => mesh && sourceMesh == mesh && motion != null && motion.Length == mesh.vertexCount;

        public void Initialize(Mesh mesh)
        {
            if (!mesh || !mesh.isReadable) throw new ArgumentException("Cloth requires a readable mesh. Enable Read/Write on its importer.");
            sourceMesh = mesh;
            var vertices = mesh.vertices;
            motion = new float[vertices.Length];
            // A usable starting point; users paint garment-specific attachment regions.
            float pinHeight = mesh.bounds.max.y - mesh.bounds.size.y * .15f;
            for (int i = 0; i < motion.Length; i++) motion[i] = vertices[i].y >= pinHeight ? 0 : .15f;
        }
    }

    [Serializable]
    public struct ClothBoneCapsule
    {
        public string startBone, endBone;
        [Tooltip("Start sphere center offset in the start bone's local space.")] public Vector3 startOffset;
        [Tooltip("End sphere center offset in the end bone's local space.")] public Vector3 endOffset;
        [Min(.001f), InspectorName("Start Radius")] public float radius;
        [Min(0), Tooltip("End bone radius. Zero preserves the original uniform capsule.")] public float endRadius;
        public ClothCapsule ToWorld(Transform start, Transform end)
        {
            var s = start.lossyScale; var e = end.lossyScale;
            float ra = Mathf.Max(.001f, radius) * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            float rb = endRadius > 0 ? endRadius * Mathf.Max(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) : ra;
            return new ClothCapsule(start.TransformPoint(startOffset), end.TransformPoint(endOffset), ra, rb);
        }

    }

    [Serializable]
    public struct ClothCapsule
    {
        public Vector3 a, b;
        public float radius, endRadius;
        public ClothCapsule(Vector3 a, Vector3 b, float radius) : this(a, b, radius, radius) { }
        public ClothCapsule(Vector3 a, Vector3 b, float radius, float endRadius)
        { this.a = a; this.b = b; this.radius = radius; this.endRadius = endRadius; }

        // Conservative advancement against a linearly moving rounded cone.
        // Broadphase is performed by the solver before calling this method.
        public bool Sweep(float3 start, ref float3 end, ClothCapsule old, float thickness, out float3 contactNormal)
        {
            contactNormal=float3.zero;
            float oldB=old.endRadius>0?old.endRadius:old.radius;
            float newB=endRadius>0?endRadius:radius;
            float gate=.25f*math.max(.0001f,math.min(math.min(old.radius,oldB),math.min(radius,newB))+thickness);
            float radiusMotion=math.max(math.abs(radius-old.radius),math.abs(newB-oldB));
            // Small movements use the existing endpoint projection. Reserve CCD
            // for travel large enough to cross a substantial part of the shape.
            if(math.lengthsq(end-start)<gate*gate*.0625f &&
               math.distancesq(old.a,a)<gate*gate*.0625f && math.distancesq(old.b,b)<gate*gate*.0625f && radiusMotion<gate*.25f)return false;
            float speed=math.length(end-start)+math.max(math.distance(old.a,a),math.distance(old.b,b))+
                math.max(math.abs(radius-old.radius),math.abs(newB-oldB));
            if(speed<1e-7f)return false;
            if(math.all((float3)old.a==(float3)a)&&math.all((float3)old.b==(float3)b)&&old.radius==radius&&oldB==newB)
            {
                float initial=Distance(start,thickness,out float3 separatingNormal);
                // A static convex collider cannot be crossed when both endpoints
                // remain outside the closest-point supporting plane.
                if(initial>=0 && initial+math.dot(end-start,separatingNormal)>1e-5f)return false;
            }
            float t=0;
            for(int iteration=0;iteration<24;iteration++)
            {
                var shape=new ClothCapsule(math.lerp((float3)old.a,(float3)a,t),math.lerp((float3)old.b,(float3)b,t),math.lerp(old.radius,radius,t),math.lerp(oldB,newB,t));
                float3 point=math.lerp(start,end,t);
                float d=shape.Distance(point,thickness,out float3 normal);
                // Existing penetration belongs to the discrete contact solver.
                if(t==0 && d < -1e-5f)return false;
                if(d<=1e-5f)
                {
                    float3 ab=(float3)shape.b-(float3)shape.a;
                    float u=math.saturate(math.dot(point-(float3)shape.a,ab)/math.max(1e-12f,math.lengthsq(ab)));
                    float3 movement=math.lerp((float3)a-(float3)shape.a,(float3)b-(float3)shape.b,u);
                    movement+=normal*math.lerp(radius-shape.radius,newB-shape.endRadius,u);
                    float3 remaining=end-point-movement;
                    // Ignore departing or tangential contact at the beginning.
                    if(t==0 && math.dot(remaining,normal)>=0)return false;
                    end=point+movement+remaining-normal*math.min(0,math.dot(remaining,normal))+normal*1e-5f;
                    float finalDistance=Distance(end,thickness,out float3 finalNormal);
                    if(finalDistance<0)end-=finalNormal*finalDistance;
                    contactNormal=normal;return true;
                }
                t+=d/speed;
                if(t>1)return false;
            }
            return false;
        }

        // Exact signed distance to the convex hull of two endpoint spheres.
        // Shared by Burst simulation and render-vertex collision correction.
        public float Distance(float3 point, float thickness, out float3 normal)
        {
            float ra = math.max(0, radius) + thickness;
            float rb = math.max(0, endRadius > 0 ? endRadius : radius) + thickness;
            float3 segment = (float3)b - (float3)a;
            float length = math.length(segment), difference = rb - ra;
            if (length <= math.abs(difference) + 1e-7f)
            {
                float3 offset = point - (rb > ra ? (float3)b : (float3)a);
                normal = math.normalizesafe(offset, new float3(1, 0, 0));
                return math.length(offset) - math.max(ra, rb);
            }
            float3 axis = segment / length, relative = point - (float3)a;
            float along = math.dot(relative, axis);
            float3 radial = relative - along * axis;
            float rho = math.length(radial);
            float slope = difference / length, cosine = math.sqrt(math.max(0, 1 - slope * slope));
            float center = along + slope * rho / cosine;
            float3 fallback = math.normalizesafe(math.cross(axis, new float3(0, 1, 0)), new float3(1, 0, 0));
            if (center <= 0 || center >= length)
            {
                float3 offset = point - (center <= 0 ? (float3)a : (float3)b);
                normal = math.normalizesafe(offset, fallback);
                return math.length(offset) - (center <= 0 ? ra : rb);
            }
            normal = math.normalizesafe(radial, fallback) * cosine - axis * slope;
            return rho * cosine - along * slope - ra;
        }
    }
}
