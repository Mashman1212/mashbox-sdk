using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MashBoxSDK.Cloth
{
    /// <summary>Owned XPBD solver. No Magica Cloth or Unity Cloth dependency. All positions are world space.</summary>
    public sealed class MashBoxClothSolver : IDisposable
    {
        public readonly MashBoxClothTopology Topology;
        NativeArray<float3> position, previous, target;
        NativeArray<float3> targetNormal, lastTarget, targetVelocity;
        bool hasTargets;
        NativeArray<float3> grabStart;
        NativeArray<float> grabWeight;
        Vector3 grabOrigin;
        public int GrabbedCount { get; private set; }
        NativeArray<float> allowance, restLength, lambda;
        NativeArray<int2> edges;
        NativeArray<byte> bending;
        NativeArray<ClothCapsule> capsules;
        NativeArray<float3> capsuleMin, capsuleMax, sweptMin, sweptMax, frameStart;
        NativeArray<ClothCapsule> oldCapsules;
        NativeArray<float3> contactStart;
        int lastCapsuleCount;
        NativeArray<ulong> collisionMasks;
        NativeArray<float3> poseStart, stepTarget;
        NativeArray<ClothCapsule> stepCapsules, stepOldCapsules;
        public int ParticleCount => Topology.ParticleCount;
        public int ConstraintCount => edges.Length;
        public Vector3 Position(int i) => position[i];
        public Vector3 Displacement(int i) => position[i] - target[i];
        public int GrabbedParticle { get; set; } = -1;
        public Vector3 GrabPosition { get; set; }

        public MashBoxClothSolver(MashBoxClothTopology topology)
        {
            Topology = topology;
            try
            {
                position = new NativeArray<float3>(ParticleCount, Allocator.Persistent);
                collisionMasks=new NativeArray<ulong>(ParticleCount,Allocator.Persistent);
                previous = new NativeArray<float3>(ParticleCount, Allocator.Persistent);
                target = new NativeArray<float3>(ParticleCount, Allocator.Persistent);
                targetNormal = new NativeArray<float3>(ParticleCount, Allocator.Persistent);
                lastTarget = new NativeArray<float3>(ParticleCount, Allocator.Persistent);
                targetVelocity = new NativeArray<float3>(ParticleCount, Allocator.Persistent);
                grabStart = new NativeArray<float3>(ParticleCount, Allocator.Persistent);
                grabWeight = new NativeArray<float>(ParticleCount, Allocator.Persistent);
                allowance = new NativeArray<float>(ParticleCount, Allocator.Persistent);
                edges = new NativeArray<int2>(topology.edges, Allocator.Persistent);
                restLength = new NativeArray<float>(edges.Length, Allocator.Persistent);
                lambda = new NativeArray<float>(edges.Length, Allocator.Persistent);
                bending = new NativeArray<byte>(edges.Length, Allocator.Persistent);
                for (int i = 0; i < edges.Length; i++) bending[i] = (byte)(topology.bending[i] ? 1 : 0);
                capsules = new NativeArray<ClothCapsule>(64, Allocator.Persistent);
                capsuleMin = new NativeArray<float3>(64, Allocator.Persistent);
                capsuleMax = new NativeArray<float3>(64, Allocator.Persistent);
                sweptMin=new NativeArray<float3>(64,Allocator.Persistent);sweptMax=new NativeArray<float3>(64,Allocator.Persistent);
                oldCapsules=new NativeArray<ClothCapsule>(64,Allocator.Persistent);frameStart=new NativeArray<float3>(ParticleCount,Allocator.Persistent);contactStart=new NativeArray<float3>(ParticleCount,Allocator.Persistent);
                poseStart=new NativeArray<float3>(ParticleCount,Allocator.Persistent);stepTarget=new NativeArray<float3>(ParticleCount,Allocator.Persistent);
                stepCapsules=new NativeArray<ClothCapsule>(64,Allocator.Persistent);stepOldCapsules=new NativeArray<ClothCapsule>(64,Allocator.Persistent);
            }
            catch { Dispose(); throw; }
        }

        public void SetTargets(System.Collections.Generic.List<Vector3> vertices, Matrix4x4 matrix, float[] motion, System.Collections.Generic.IReadOnlyList<Vector3> normals = null)
        {
            if (vertices.Count != Topology.vertexParticle.Length || motion.Length != vertices.Count)
                throw new ArgumentException("The profile/mesh topology changed. Rebuild the simulation.");
            var normalMatrix = matrix.inverse.transpose;
            bool hasNormals = normals != null && normals.Count == vertices.Count;
            for (int i = 0; i < ParticleCount; i++) { target[i] = targetNormal[i] = float3.zero; allowance[i] = float.MaxValue; }
            for (int i = 0; i < vertices.Count; i++)
            {
                int p = Topology.vertexParticle[i];
                target[p] += (float3)matrix.MultiplyPoint3x4(vertices[i]);
                if (hasNormals) targetNormal[p] += (float3)normalMatrix.MultiplyVector(normals[i]).normalized;
                // A pinned source vertex pins its cluster, preventing attachment leakage.
                allowance[p] = math.min(allowance[p], math.max(0, motion[i]));
            }
            for (int i = 0; i < ParticleCount; i++)
            { target[i] /= Topology.counts[i]; targetNormal[i] = math.normalizesafe(targetNormal[i]); }
        }

        public bool TargetJumpExceeds(float distance)
        {
            if(!hasTargets)return false;
            float limit=math.max(.1f,distance);limit*=limit;
            for(int i=0;i<ParticleCount;i++)if(math.distancesq(target[i],lastTarget[i])>limit)return true;
            return false;
        }

        public void Reset(bool rebuildRestLengths = false)
        {
            ReleaseGrab();lastCapsuleCount=0;
            for (int i = 0; i < ParticleCount; i++) position[i] = previous[i] = target[i];
            if (rebuildRestLengths)
                for (int i = 0; i < edges.Length; i++) restLength[i] = math.distance(target[edges[i].x], target[edges[i].y]);
            for (int i = 0; i < ParticleCount; i++) { lastTarget[i] = target[i]; targetVelocity[i] = float3.zero; }
            hasTargets = true;
            GrabbedParticle = -1;
        }

        public void Step(float dt, float time, MashBoxClothProfile settings, ClothCapsule[] colliders, int count, ulong[] masks = null)
        {
            for(int i=0;i<ParticleCount;i++)collisionMasks[i]=masks!=null&&masks.Length==ParticleCount?masks[i]:ulong.MaxValue;
            float stepTime = math.clamp(dt, .0001f, .066667f);
            float h = stepTime / math.clamp(settings.substeps, 1, 8);
            // Pose transport and inertia are separate: transport keeps the
            // garment with the bones; a change in target velocity creates lag
            // on acceleration and continued motion when the animation stops.
            for (int i = 0; i < ParticleCount; i++)
            {
                frameStart[i]=position[i];poseStart[i]=hasTargets?lastTarget[i]:target[i];
                float3 velocity = hasTargets ? (target[i] - lastTarget[i]) / stepTime : float3.zero;
                if (hasTargets && i != GrabbedParticle && grabWeight[i] <= 0)
                {
                    float follow = math.saturate(settings.animationFollow);
                    // Pose transport is distributed across the collision substeps below.
                    if (allowance[i] > 0)
                        previous[i] += (velocity - targetVelocity[i]) * (h * follow * math.saturate(settings.animationInertia));
                }
                targetVelocity[i] = velocity; lastTarget[i] = target[i];
            }
            hasTargets = true;
            if (!settings.bodyCollisions) count = 0;
            count = math.clamp(count, 0, math.min(capsules.Length, colliders?.Length ?? 0));
            for (int i = 0; i < count; i++)
            {
                var capsule = colliders[i]; oldCapsules[i]=lastCapsuleCount==count?capsules[i]:capsule; capsules[i] = capsule;
                float thickness = math.max(0, settings.thickness);
                float ra = math.max(0, capsule.radius) + thickness;
                float rb = math.max(0, capsule.endRadius > 0 ? capsule.endRadius : capsule.radius) + thickness;
                // The convex hull of the endpoint spheres fits exactly within
                // these conservative bounds, including tapered/contained spheres.
                capsuleMin[i] = math.min((float3)capsule.a - ra, (float3)capsule.b - rb);
                capsuleMax[i] = math.max((float3)capsule.a + ra, (float3)capsule.b + rb);
                var old=oldCapsules[i];float oa=math.max(0,old.radius)+thickness,ob=math.max(0,old.endRadius>0?old.endRadius:old.radius)+thickness;
                sweptMin[i]=math.min(capsuleMin[i],math.min((float3)old.a-oa,(float3)old.b-ob));
                sweptMax[i]=math.max(capsuleMax[i],math.max((float3)old.a+oa,(float3)old.b+ob));
            }
            lastCapsuleCount=count;
            // One Burst job avoids parallel write races between shared constraint particles.
            new FilteredMotionSolveJob
            {
                collisionMasks=collisionMasks, position = position, previous = previous, target = stepTarget, finalTarget=target, poseStart=poseStart, allowance = allowance,
                finalCapsules=capsules, capsules=stepCapsules, stepOldCapsules=stepOldCapsules,
                frameStart=frameStart, contactStart=contactStart, oldCapsules=oldCapsules, sweptMin=sweptMin, sweptMax=sweptMax,
                targetNormal = targetNormal, backstop = settings.backstop,
                backstopRadius = math.max(.001f, settings.backstopRadius), backstopDistance = math.max(0, settings.backstopDistance),
                edges = edges, rest = restLength, lambda = lambda, bending = bending, capsuleMin = capsuleMin, capsuleMax = capsuleMax,
                capsuleCount = count, dt = math.clamp(dt, .0001f, .066667f), time = time,
                substeps = math.clamp(settings.substeps, 1, 8), iterations = math.clamp(settings.iterations, 1, 16),
                stretch = math.max(0, settings.stretchCompliance), bend = math.max(0, settings.bendCompliance),
                follow = math.saturate(settings.animationFollow),
                shapeRetention = math.saturate(settings.shapeRetention), shapeDamping = math.clamp(settings.shapeDamping, 0, 2),
                damping = math.max(0, settings.damping), gravity = settings.gravity, wind = settings.wind,
                gust = math.saturate(settings.gust), thickness = math.max(0, settings.thickness),
                grab = GrabbedParticle, grabPosition = GrabPosition,
                grabStart = grabStart, grabWeight = grabWeight, grabDelta = GrabPosition - grabOrigin,
                friction = math.saturate(settings.collisionFriction)
            }.Run();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct FilteredMotionSolveJob : IJob
        {
            [ReadOnly] public NativeArray<ulong> collisionMasks;
            public NativeArray<float3> position, previous, contactStart;
            public NativeArray<float3> target;
            [ReadOnly] public NativeArray<float3> finalTarget, poseStart;
            [ReadOnly] public NativeArray<float3> targetNormal;
            public bool backstop;
            public float backstopRadius, backstopDistance;
            [ReadOnly] public NativeArray<float3> grabStart;
            [ReadOnly] public NativeArray<float> grabWeight;
            public float3 grabDelta;
            public float friction;
            [ReadOnly] public NativeArray<float> allowance, rest;
            public NativeArray<float> lambda;
            [ReadOnly] public NativeArray<int2> edges;
            [ReadOnly] public NativeArray<byte> bending;
            public NativeArray<ClothCapsule> capsules,stepOldCapsules;
            [ReadOnly] public NativeArray<ClothCapsule> finalCapsules;
            public NativeArray<float3> capsuleMin, capsuleMax, sweptMin, sweptMax;
            [ReadOnly] public NativeArray<float3> frameStart;
            [ReadOnly] public NativeArray<ClothCapsule> oldCapsules;
            public int capsuleCount, substeps, iterations, grab;
            public float dt, time, stretch, bend, damping, gust, thickness, follow, shapeRetention, shapeDamping;
            public float3 gravity, wind, grabPosition;
            static ClothCapsule Interpolate(ClothCapsule a,ClothCapsule b,float t)=>new ClothCapsule(
                math.lerp((float3)a.a,(float3)b.a,t),math.lerp((float3)a.b,(float3)b.b,t),math.lerp(a.radius,b.radius,t),
                math.lerp(a.endRadius>0?a.endRadius:a.radius,b.endRadius>0?b.endRadius:b.radius,t));
            void Bounds(ClothCapsule c,out float3 lo,out float3 hi)
            {
                float ra=math.max(0,c.radius)+thickness, rb=math.max(0,c.endRadius)+thickness;
                lo=math.min((float3)c.a-ra,(float3)c.b-rb);hi=math.max((float3)c.a+ra,(float3)c.b+rb);
            }
            float Mass(int i) => allowance[i] > 0 && i != grab && grabWeight[i] <= 0 ? 1 : 0;
            public void Execute()
            {
                float h = dt / substeps, decay = math.exp(-damping * h);
                float shapeSpring = 1600 * shapeRetention;
                float shapeDrag = 2 * math.sqrt(shapeSpring) * shapeDamping;
                float shapeDenominator = 1 + shapeDrag * h + shapeSpring * h * h;
                for (int step = 0; step < substeps; step++)
                {
                    float t0=step/(float)substeps,t1=(step+1)/(float)substeps;
                    for(int c=0;c<capsuleCount;c++)
                    {
                        var a=oldCapsules[c];var b=finalCapsules[c];
                        stepOldCapsules[c]=Interpolate(a,b,t0);capsules[c]=Interpolate(a,b,t1);
                        Bounds(capsules[c],out float3 lo,out float3 hi);capsuleMin[c]=lo;capsuleMax[c]=hi;
                        Bounds(stepOldCapsules[c],out float3 oldLo,out float3 oldHi);
                        sweptMin[c]=math.min(lo,oldLo);sweptMax[c]=math.max(hi,oldHi);
                    }
                    for (int i = 0; i < position.Length; i++)
                    {
                        float3 sweepStart=position[i];
                        target[i]=math.lerp(poseStart[i],finalTarget[i],t1);
                        if(i!=grab && grabWeight[i]<=0)
                        {
                            float3 transport=(finalTarget[i]-poseStart[i])*(follow/substeps);
                            position[i]+=transport;previous[i]+=transport;
                        }
                        float3 p = position[i];
                        float3 acceleration = gravity + wind * (1 + gust * math.sin(time * 3.1f + i * .37f))
                            + (target[i] - p) * (144 * follow);
                        float3 predicted = p + (p - previous[i]) * decay + acceleration * h * h;
                        if (shapeRetention > 0)
                        {
                            // Implicit damped spring: stable even at low frequencies.
                            // Solve before constraints, so pins, grabs, motion limits,
                            // backstop and body contact retain their existing priority.
                            float3 travel = (p - previous[i]) * decay +
                                (acceleration + (target[i] - p) * shapeSpring) * h * h;
                            predicted = p + travel / shapeDenominator;
                        }
                        position[i] = grabWeight[i] > 0 ? grabStart[i] + grabDelta * grabWeight[i] :
                            Mass(i) == 0 ? (i == grab ? grabPosition : target[i]) : predicted;
                        previous[i] = p;
                        if(Mass(i)>0)
                        {
                            float3 start=sweepStart, end=position[i];
                            for(int c=0;c<capsuleCount;c++)
                            {
                                if((collisionMasks[i]&(1UL<<c))==0)continue;
                                float3 lo=sweptMin[c], hi=sweptMax[c];
                                if(math.any(math.max(start,end)<lo)||math.any(math.min(start,end)>hi))continue;
                                if(capsules[c].Sweep(start,ref end,stepOldCapsules[c],thickness,out float3 normal))
                                {
                                    float3 velocity=position[i]-previous[i];
                                    velocity-=normal*math.min(0,math.dot(velocity,normal));
                                    previous[i]=end-velocity;
                                }
                            }
                            position[i]=end;
                        }

                    }
                    for (int e = 0; e < lambda.Length; e++) lambda[e] = 0;
                    for (int iteration = 0; iteration < iterations; iteration++)
                    {
                        if(iteration==0)for(int i=0;i<position.Length;i++)contactStart[i]=position[i];
                        for (int e = 0; e < edges.Length; e++)
                        {
                            int a = edges[e].x, b = edges[e].y;
                            float wa = Mass(a), wb = Mass(b);
                            float3 d = position[b] - position[a]; float length = math.length(d);
                            if (length < 1e-7f || wa + wb == 0) continue;
                            float alpha = (bending[e] == 0 ? stretch : bend) / (h * h);
                            float dl = (-(length - rest[e]) - alpha * lambda[e]) / (wa + wb + alpha);
                            lambda[e] += dl;
                            float3 correction = d * (dl / length);
                            position[a] -= wa * correction; position[b] += wb * correction;
                        }
                        for (int i = 0; i < position.Length; i++)
                        {
                            if (Mass(i) == 0) continue;
                            float3 p = position[i], delta = p - target[i];
                            p = target[i] + delta * math.min(1, allowance[i] / math.max(1e-7f, math.length(delta)));
                            if (backstop && math.lengthsq(targetNormal[i]) > .5f)
                            {
                                float3 center = target[i] - targetNormal[i] * (backstopRadius + backstopDistance);
                                float3 offset = p - center;
                                if (math.lengthsq(offset) < backstopRadius * backstopRadius)
                                    p = center + math.normalizesafe(offset, targetNormal[i]) * backstopRadius;
                            }
                            for (int c = 0; c < capsuleCount; c++)
                            {
                                if((collisionMasks[i]&(1UL<<c))==0)continue;
                                float3 start=contactStart[i];
                                if(iteration==iterations-1 && !math.any(math.max(start,p)<capsuleMin[c])&&!math.any(math.min(start,p)>capsuleMax[c]) &&
                                   capsules[c].Sweep(start,ref p,capsules[c],thickness,out float3 hitNormal))
                                {
                                    float3 velocity=position[i]-previous[i];
                                    previous[i]=p-(velocity-hitNormal*math.min(0,math.dot(velocity,hitNormal)));
                                }
                                // Recheck the current position after each contact: an
                                // earlier collider can move a particle into another.
                                if (math.any(p < capsuleMin[c]) || math.any(p > capsuleMax[c])) continue;
                                var capsule = capsules[c];
                                float distance = capsule.Distance(p, thickness, out float3 normal);
                                if (distance < 0)
                                {
                                    // Resolve contact without counting the push-out
                                    // distance as velocity on the next substep.
                                    var velocity = p - previous[i];
                                    float outward = math.max(0, math.dot(velocity, normal));
                                    var tangent = velocity - normal * math.dot(velocity, normal);
                                    p -= normal * distance;
                                    previous[i] = p - (normal * outward + tangent * (1 - friction));
                                }
                            }
                            position[i] = p;
                        }
                    }
                }
            }
        }

        public int BeginGrab(Vector3 center, float radius)
        {
            ReleaseGrab(); grabOrigin = GrabPosition = center;
            radius = Mathf.Max(.0001f, radius);
            for (int i = 0; i < ParticleCount; i++)
            {
                if (allowance[i] <= 0) continue;
                float distance = Vector3.Distance(position[i], center);
                if (distance >= radius) continue;
                grabStart[i] = position[i];
                grabWeight[i] = Mathf.SmoothStep(1, 0, distance / radius);
                GrabbedCount++;
            }
            return GrabbedCount;
        }

        public void ReleaseGrab()
        {
            if (grabWeight.IsCreated)
                for (int i = 0; i < grabWeight.Length; i++)
                {
                    if (grabWeight[i] > 0) previous[i] = position[i];
                    grabWeight[i] = 0;
                }
            GrabbedParticle = -1; GrabbedCount = 0;
        }

        public void Dispose()
        {
            if(collisionMasks.IsCreated)collisionMasks.Dispose();
            if(poseStart.IsCreated)poseStart.Dispose();if(stepTarget.IsCreated)stepTarget.Dispose();
            if(stepCapsules.IsCreated)stepCapsules.Dispose();if(stepOldCapsules.IsCreated)stepOldCapsules.Dispose();
            if (targetVelocity.IsCreated) targetVelocity.Dispose();
            if (lastTarget.IsCreated) lastTarget.Dispose();
            if (targetNormal.IsCreated) targetNormal.Dispose();
            if (grabStart.IsCreated) grabStart.Dispose(); if (grabWeight.IsCreated) grabWeight.Dispose();
            if (position.IsCreated) position.Dispose(); if (previous.IsCreated) previous.Dispose();
            if (target.IsCreated) target.Dispose(); if (allowance.IsCreated) allowance.Dispose();
            if (edges.IsCreated) edges.Dispose(); if (restLength.IsCreated) restLength.Dispose();
            if (lambda.IsCreated) lambda.Dispose(); if (bending.IsCreated) bending.Dispose();
            if (capsules.IsCreated) capsules.Dispose();
            if(contactStart.IsCreated)contactStart.Dispose();if(oldCapsules.IsCreated)oldCapsules.Dispose();if(frameStart.IsCreated)frameStart.Dispose();
            if(sweptMin.IsCreated)sweptMin.Dispose();if(sweptMax.IsCreated)sweptMax.Dispose();
            if (capsuleMin.IsCreated) capsuleMin.Dispose();
            if (capsuleMax.IsCreated) capsuleMax.Dispose();
        }
    }
}
