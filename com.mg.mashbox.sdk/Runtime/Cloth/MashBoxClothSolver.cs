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
        NativeArray<float> allowance, restLength, lambda;
        NativeArray<int2> edges;
        NativeArray<byte> bending;
        NativeArray<ClothCapsule> capsules;
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
                previous = new NativeArray<float3>(ParticleCount, Allocator.Persistent);
                target = new NativeArray<float3>(ParticleCount, Allocator.Persistent);
                allowance = new NativeArray<float>(ParticleCount, Allocator.Persistent);
                edges = new NativeArray<int2>(topology.edges, Allocator.Persistent);
                restLength = new NativeArray<float>(edges.Length, Allocator.Persistent);
                lambda = new NativeArray<float>(edges.Length, Allocator.Persistent);
                bending = new NativeArray<byte>(edges.Length, Allocator.Persistent);
                for (int i = 0; i < edges.Length; i++) bending[i] = (byte)(topology.bending[i] ? 1 : 0);
                capsules = new NativeArray<ClothCapsule>(64, Allocator.Persistent);
            }
            catch { Dispose(); throw; }
        }

        public void SetTargets(System.Collections.Generic.List<Vector3> vertices, Matrix4x4 matrix, float[] motion)
        {
            if (vertices.Count != Topology.vertexParticle.Length || motion.Length != vertices.Count)
                throw new ArgumentException("The profile/mesh topology changed. Rebuild the simulation.");
            for (int i = 0; i < ParticleCount; i++) { target[i] = float3.zero; allowance[i] = float.MaxValue; }
            for (int i = 0; i < vertices.Count; i++)
            {
                int p = Topology.vertexParticle[i];
                target[p] += (float3)matrix.MultiplyPoint3x4(vertices[i]);
                // A pinned source vertex pins its cluster, preventing attachment leakage.
                allowance[p] = math.min(allowance[p], math.max(0, motion[i]));
            }
            for (int i = 0; i < ParticleCount; i++) target[i] /= Topology.counts[i];
        }

        public void Reset(bool rebuildRestLengths = false)
        {
            for (int i = 0; i < ParticleCount; i++) position[i] = previous[i] = target[i];
            if (rebuildRestLengths)
                for (int i = 0; i < edges.Length; i++) restLength[i] = math.distance(target[edges[i].x], target[edges[i].y]);
            GrabbedParticle = -1;
        }

        public void Step(float dt, float time, MashBoxClothProfile settings, ClothCapsule[] colliders, int count)
        {
            count = math.clamp(count, 0, math.min(capsules.Length, colliders?.Length ?? 0));
            for (int i = 0; i < count; i++) capsules[i] = colliders[i];
            // One Burst job avoids parallel write races between shared constraint particles.
            new SolveJob
            {
                position = position, previous = previous, target = target, allowance = allowance,
                edges = edges, rest = restLength, lambda = lambda, bending = bending, capsules = capsules,
                capsuleCount = count, dt = math.clamp(dt, .0001f, .066667f), time = time,
                substeps = math.clamp(settings.substeps, 1, 8), iterations = math.clamp(settings.iterations, 1, 16),
                stretch = math.max(0, settings.stretchCompliance), bend = math.max(0, settings.bendCompliance),
                damping = math.max(0, settings.damping), gravity = settings.gravity, wind = settings.wind,
                gust = math.saturate(settings.gust), thickness = math.max(0, settings.thickness),
                grab = GrabbedParticle, grabPosition = GrabPosition
            }.Run();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct SolveJob : IJob
        {
            public NativeArray<float3> position, previous;
            [ReadOnly] public NativeArray<float3> target;
            [ReadOnly] public NativeArray<float> allowance, rest;
            public NativeArray<float> lambda;
            [ReadOnly] public NativeArray<int2> edges;
            [ReadOnly] public NativeArray<byte> bending;
            [ReadOnly] public NativeArray<ClothCapsule> capsules;
            public int capsuleCount, substeps, iterations, grab;
            public float dt, time, stretch, bend, damping, gust, thickness;
            public float3 gravity, wind, grabPosition;
            float Mass(int i) => allowance[i] > 0 && i != grab ? 1 : 0;
            public void Execute()
            {
                float h = dt / substeps, decay = math.exp(-damping * h);
                for (int step = 0; step < substeps; step++)
                {
                    for (int i = 0; i < position.Length; i++)
                    {
                        float3 p = position[i];
                        float3 acceleration = gravity + wind * (1 + gust * math.sin(time * 3.1f + i * .37f));
                        position[i] = Mass(i) == 0 ? (i == grab ? grabPosition : target[i]) : p + (p - previous[i]) * decay + acceleration * h * h;
                        previous[i] = p;
                    }
                    for (int e = 0; e < lambda.Length; e++) lambda[e] = 0;
                    for (int iteration = 0; iteration < iterations; iteration++)
                    {
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
                            for (int c = 0; c < capsuleCount; c++)
                            {
                                var capsule = capsules[c];
                                float3 a = capsule.a, segment = (float3)capsule.b - a;
                                float3 closest = a + segment * math.saturate(math.dot(p - a, segment) / math.max(1e-9f, math.lengthsq(segment)));
                                float3 offset = p - closest; float length = math.length(offset), radius = capsule.radius + thickness;
                                if (length < radius) p = closest + math.normalizesafe(offset, new float3(1, 0, 0)) * radius;
                            }
                            position[i] = p;
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (position.IsCreated) position.Dispose(); if (previous.IsCreated) previous.Dispose();
            if (target.IsCreated) target.Dispose(); if (allowance.IsCreated) allowance.Dispose();
            if (edges.IsCreated) edges.Dispose(); if (restLength.IsCreated) restLength.Dispose();
            if (lambda.IsCreated) lambda.Dispose(); if (bending.IsCreated) bending.Dispose();
            if (capsules.IsCreated) capsules.Dispose();
        }
    }
}
