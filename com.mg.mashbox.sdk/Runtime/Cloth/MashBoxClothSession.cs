using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Object = UnityEngine.Object;

namespace MashBoxSDK.Cloth
{
    /// <summary>Disposable mesh output shared by editor and player. Source meshes are never modified.</summary>
    public sealed class MashBoxClothSession : IDisposable
    {
        public readonly Renderer Source;
        public readonly MashBoxClothProfile Profile;
        public MashBoxClothSolver Solver { get; private set; }
        public Mesh Output { get; private set; }
        public double SolveMilliseconds { get; private set; }
        public double TotalMilliseconds { get; private set; }
        public double BakeMilliseconds { get; private set; }
        public int DroppedSteps { get; private set; }
        public int Steps { get; private set; }
        public Mesh SourceMesh { get; }
        readonly List<Vector3> bakedVertices = new List<Vector3>();
        readonly List<Vector3> bakedNormals = new List<Vector3>();
        readonly List<Vector4> bakedTangents = new List<Vector4>();
        readonly List<Vector3> outputVertices = new List<Vector3>();
        readonly ClothCapsule[] capsules = new ClothCapsule[64];
        readonly Transform[] starts = new Transform[64], ends = new Transform[64];
        readonly string[] startPaths = new string[64], endPaths = new string[64];
        readonly Transform collisionRoot;
        readonly bool originalForceOff;
        readonly bool originalOffscreen;
        readonly SkinnedMeshRenderer skin;
        GameObject outputObject;
        Mesh baked;
        Vector3 lastPosition;
        Vector3 lastScale;
        float accumulator, time;
        Vector3[] previousDisplacement, currentDisplacement;
        readonly Stopwatch totalWatch = new Stopwatch(), solveWatch = new Stopwatch(), bakeWatch = new Stopwatch();
        NativeArray<Vector3> renderPose, renderOutput, renderPrevious, renderCurrent;
        NativeArray<float> renderMotion;
        NativeArray<int> renderParticles;
        NativeArray<RenderCollider> renderColliders;
        Vector3[] renderCopy;
        readonly int[] seamVertices;
        readonly float[] seamMotion;
        NativeArray<ulong> renderCollisionMasks;
        ulong[] particleCollisionMasks;
        readonly Transform[] maskStarts=new Transform[64],maskEnds=new Transform[64];
        int maskCount=-1;bool maskLocal;
        BoneWeight[] sourceWeights;
        Transform[] sourceBones;
        struct RenderCollider
        {
            public ClothCapsule capsule;
            public float3 min, max;
        }
        bool disposed;
        public bool Visible => outputObject && outputObject.GetComponent<Renderer>().isVisible;
        public IReadOnlyList<Vector3> Vertices => outputVertices;

        public static Mesh GetMesh(Renderer renderer) => renderer is SkinnedMeshRenderer s ? s.sharedMesh : renderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;

        public MashBoxClothSession(Renderer source, MashBoxClothProfile profile, Transform collisionRoot)
        {
            if (profile) profile.ApplyFixedSettings();
            Source = source; Profile = profile; SourceMesh = GetMesh(source); skin = source as SkinnedMeshRenderer;
            if (!source || !profile || !profile.Matches(SourceMesh)) throw new ArgumentException("Assign a profile made for this exact garment mesh.");
            if (!SourceMesh.isReadable) throw new ArgumentException("Enable Read/Write on the garment mesh importer.");
            if (source.forceRenderingOff) throw new ArgumentException("The garment is already hidden by another preview or cloth session.");
            seamVertices=MashBoxClothSeams.Build(SourceMesh);seamMotion=new float[SourceMesh.vertexCount];
            originalForceOff = source.forceRenderingOff;
            originalOffscreen = skin && skin.updateWhenOffscreen;
            this.collisionRoot = collisionRoot;
            try
            {
                baked = new Mesh { name = "Cloth skin pose", hideFlags = HideFlags.HideAndDontSave };
                bakedVertices.Capacity = outputVertices.Capacity = SourceMesh.vertexCount;
                ReadPose();
                var topology = new MashBoxClothTopology(bakedVertices.ToArray(), SourceMesh.triangles, profile.particleBudget);
                Solver = new MashBoxClothSolver(topology);
                Solver.SetTargets(bakedVertices, Source.transform.localToWorldMatrix, EffectiveMotion(), bakedNormals);
                Solver.Reset(true);
                previousDisplacement = new Vector3[Solver.ParticleCount];
                currentDisplacement = new Vector3[Solver.ParticleCount];
                int vertexCount = SourceMesh.vertexCount;
                renderPose = new NativeArray<Vector3>(vertexCount, Allocator.Persistent);
                renderOutput = new NativeArray<Vector3>(vertexCount, Allocator.Persistent);
                renderMotion = new NativeArray<float>(vertexCount, Allocator.Persistent);
                renderParticles = new NativeArray<int>(Solver.Topology.vertexParticle, Allocator.Persistent);
                renderPrevious = new NativeArray<Vector3>(Solver.ParticleCount, Allocator.Persistent);
                renderCurrent = new NativeArray<Vector3>(Solver.ParticleCount, Allocator.Persistent);
                renderColliders = new NativeArray<RenderCollider>(64, Allocator.Persistent);
                renderCopy = new Vector3[vertexCount];
                renderCollisionMasks=new NativeArray<ulong>(vertexCount,Allocator.Persistent);
                particleCollisionMasks=new ulong[Solver.ParticleCount];
                sourceWeights=SourceMesh.boneWeights;sourceBones=skin?skin.bones:Array.Empty<Transform>();
                Output = Object.Instantiate(SourceMesh); Output.name = source.name + " • Cloth output";
                Output.hideFlags = HideFlags.HideAndDontSave; Output.MarkDynamic();
                outputObject = new GameObject("MashBox Cloth Output") { hideFlags = HideFlags.HideAndDontSave, layer = source.gameObject.layer };
                // Explicit scene ownership is required for SceneView preview culling.
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(outputObject, source.gameObject.scene);
                outputObject.transform.SetParent(source.transform, false);
                outputObject.AddComponent<MeshFilter>().sharedMesh = Output;
                var renderer = outputObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = source.sharedMaterials;
                renderer.shadowCastingMode = source.shadowCastingMode; renderer.receiveShadows = source.receiveShadows;
                renderer.renderingLayerMask = source.renderingLayerMask;
                var properties = new MaterialPropertyBlock(); source.GetPropertyBlock(properties); renderer.SetPropertyBlock(properties);
                if (skin) skin.updateWhenOffscreen = true;
                source.forceRenderingOff = true;
                lastPosition = source.transform.position; lastScale = source.transform.lossyScale;
                WriteOutput();
            }
            catch { Dispose(); throw; }
        }

        float[] EffectiveMotion()
        {
            Array.Copy(Profile.motion,seamMotion,seamMotion.Length);
            MashBoxClothSeams.Synchronize(seamMotion,seamVertices);
            return seamMotion;
        }

        void ReadPose()
        {
            // Keep vertices renderer-local; the renderer matrix applies scale once.
            if (skin) { skin.BakeMesh(baked, false); baked.GetVertices(bakedVertices); }
            else SourceMesh.GetVertices(bakedVertices);
            if (skin) baked.GetNormals(bakedNormals); else SourceMesh.GetNormals(bakedNormals);
            if (Profile.preserveAuthoredShading || Profile.normalsEverySteps <= 0)
            { if (skin) baked.GetTangents(bakedTangents); else SourceMesh.GetTangents(bakedTangents); }
        }

        public void Reset()
        {
            ReadPose(); Solver.SetTargets(bakedVertices, Source.transform.localToWorldMatrix, EffectiveMotion(), bakedNormals);
            Solver.Reset(); accumulator = time = 0; lastPosition = Source.transform.position;
            lastScale = Source.transform.lossyScale;
            ClearInterpolation();
            WriteOutput();
        }

        public void Advance(float deltaTime)
        {
            if (disposed || !Source || !Profile) return;
            Profile.ApplyFixedSettings();
            float fixedDt = 1f / Mathf.Clamp(Profile.frequency, 15, 120);
            accumulator += Mathf.Clamp(deltaTime, 0, .25f);
            int due = Mathf.FloorToInt(accumulator / fixedDt);
            if (due == 0 && !Profile.interpolateMotion) return;
            totalWatch.Restart(); bakeWatch.Restart();
            // Skin once per display update, keeping pinned vertices attached to
            // the current animation even on frames without a physics step.
            ReadPose(); bakeWatch.Stop(); BakeMilliseconds = bakeWatch.Elapsed.TotalMilliseconds;
            // Never spiral on a slow frame. Excess time is explicitly counted in the HUD.
            DroppedSteps += Mathf.Max(0, due - 3);
            accumulator -= due * fixedDt;
            PreparePose();
            for (int i = 0; i < Mathf.Min(due, 3); i++) StepSolver(fixedDt);
            WriteOutput(Profile.interpolateMotion ? Mathf.Clamp01(accumulator / fixedDt) : 1);
            totalWatch.Stop(); TotalMilliseconds = totalWatch.Elapsed.TotalMilliseconds;
        }

        public void Step(float dt)
        {
            if (disposed || !Source || !Profile) return;
            Profile.ApplyFixedSettings();
            if (!Profile.Matches(GetMesh(Source))) throw new InvalidOperationException("Garment mesh or profile changed; rebuild the cloth session.");
            totalWatch.Restart(); bakeWatch.Restart();
            ReadPose(); bakeWatch.Stop(); BakeMilliseconds = bakeWatch.Elapsed.TotalMilliseconds;
            PreparePose();
            StepSolver(dt);
            // Explicit single stepping displays the newly solved state.
            Array.Copy(currentDisplacement, previousDisplacement, currentDisplacement.Length);
            accumulator = 0; WriteOutput();
            totalWatch.Stop(); TotalMilliseconds = totalWatch.Elapsed.TotalMilliseconds;
        }

        void ClearInterpolation()
        {
            Array.Clear(previousDisplacement, 0, previousDisplacement.Length);
            Array.Clear(currentDisplacement, 0, currentDisplacement.Length);
        }

        void PreparePose()
        {
            if (!Profile.Matches(GetMesh(Source))) throw new InvalidOperationException("Garment mesh or profile changed; rebuild the cloth session.");
            Solver.SetTargets(bakedVertices, Source.transform.localToWorldMatrix, EffectiveMotion(), bakedNormals);
            bool scaleChanged = (Source.transform.lossyScale - lastScale).sqrMagnitude > .000001f;
            if (scaleChanged || Vector3.Distance(lastPosition, Source.transform.position) > Mathf.Max(.1f, Profile.teleportDistance) || Solver.TargetJumpExceeds(Profile.teleportDistance))
            { Solver.Reset(scaleChanged); ClearInterpolation();accumulator=0; }
            lastPosition = Source.transform.position; lastScale = Source.transform.lossyScale;
        }

        void StepSolver(float dt)
        {
            Array.Copy(currentDisplacement, previousDisplacement, currentDisplacement.Length);
            int count = RefreshCapsules();
            solveWatch.Restart(); Solver.Step(dt, time, Profile, capsules, count, particleCollisionMasks); solveWatch.Stop();
            SolveMilliseconds = solveWatch.Elapsed.TotalMilliseconds;
            for (int i = 0; i < currentDisplacement.Length; i++) currentDisplacement[i] = Solver.Displacement(i);
            time += dt; Steps++;
        }

        int RefreshCapsules()
        {
            int count = 0;bool changed=maskLocal!=Profile.localBoneCollisions;
            var bindings = Profile.bodyCapsules ?? Array.Empty<ClothBoneCapsule>();
            for (int i = 0; Profile.bodyCollisions && i < Math.Min(64, bindings.Length); i++)
            {
                string start = bindings[i].startBone ?? "", end = bindings[i].endBone ?? "";
                if (startPaths[i] != start || !starts[i]) { startPaths[i] = start; starts[i] = collisionRoot ? collisionRoot.Find(start) : null; }
                if (endPaths[i] != end || !ends[i]) { endPaths[i] = end; ends[i] = collisionRoot ? collisionRoot.Find(end) : null; }
                if (starts[i] && ends[i])
                {
                    if(maskStarts[count]!=starts[i]||maskEnds[count]!=ends[i])changed=true;
                    maskStarts[count]=starts[i];maskEnds[count]=ends[i];
                    capsules[count++] = bindings[i].ToWorld(starts[i], ends[i]);
                }
            }
            if(changed||maskCount!=count)
            {
                maskCount=count;maskLocal=Profile.localBoneCollisions;
                var boneMasks=new ulong[sourceBones.Length];
                for(int b=0;b<boneMasks.Length;b++)boneMasks[b]=MashBoxClothCollisionFilter.BoneMask(sourceBones[b],maskStarts,maskEnds,count);
                Array.Clear(particleCollisionMasks,0,particleCollisionMasks.Length);
                bool filtered=maskLocal&&sourceBones.Length>0&&sourceWeights.Length==SourceMesh.vertexCount;
                for(int v=0;v<SourceMesh.vertexCount;v++){
                    ulong mask=filtered?MashBoxClothCollisionFilter.VertexMask(sourceWeights[v],boneMasks):ulong.MaxValue;
                    renderCollisionMasks[v]=mask;particleCollisionMasks[Solver.Topology.vertexParticle[v]]|=mask;
                }
            }
            return count;
        }

        void WriteOutput(float alpha = 1)
        {
            outputVertices.Clear();
            int capsuleCount = RefreshCapsules();
            float thickness = Mathf.Max(0, Profile.thickness);
            for (int c = 0; c < capsuleCount; c++)
            {
                var capsule = capsules[c];
                float ra = capsule.radius + thickness, rb = capsule.endRadius + thickness;
                renderColliders[c] = new RenderCollider { capsule = capsule,
                    min = math.min((float3)capsule.a - ra, (float3)capsule.b - rb),
                    max = math.max((float3)capsule.a + ra, (float3)capsule.b + rb) };
            }
            for (int i = 0; i < bakedVertices.Count; i++) renderPose[i] = bakedVertices[i];
            renderMotion.CopyFrom(EffectiveMotion());
            renderPrevious.CopyFrom(previousDisplacement); renderCurrent.CopyFrom(currentDisplacement);
            new FilteredRenderOutputJob { pose = renderPose, result = renderOutput, motion = renderMotion,
                collisionMasks=renderCollisionMasks, particles = renderParticles, previous = renderPrevious, current = renderCurrent,
                colliders = renderColliders, colliderCount = capsuleCount, thickness = thickness, alpha = alpha,
                localToWorld = Source.transform.localToWorldMatrix, worldToLocal = Source.transform.worldToLocalMatrix
            }.Schedule(renderPose.Length, 128).Complete();
            renderOutput.CopyTo(renderCopy);
            // UV/normal copies with identical skin bindings must never tear apart,
            // including after per-render-vertex collision correction.
            for(int i=0;i<renderCopy.Length;i++)renderCopy[i]=renderCopy[seamVertices[i]];
            outputVertices.AddRange(renderCopy);
            Output.SetVertices(outputVertices); Output.RecalculateBounds();
            if ((Profile.preserveAuthoredShading || Profile.normalsEverySteps <= 0) && bakedNormals.Count == bakedVertices.Count)
            {
                // Preserve authored split/smooth normals while following skinning.
                // Leaving the output's old normals untouched freezes them in an old pose.
                Output.SetNormals(bakedNormals);
                if (bakedTangents.Count == bakedVertices.Count) Output.SetTangents(bakedTangents);
            }
            else if (Profile.normalsEverySteps > 0 && Steps % Profile.normalsEverySteps == 0)
                Output.RecalculateNormals();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct FilteredRenderOutputJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Vector3> pose, previous, current;
            [ReadOnly] public NativeArray<float> motion;
            [ReadOnly] public NativeArray<int> particles;
            [ReadOnly] public NativeArray<ulong> collisionMasks;
            [ReadOnly] public NativeArray<RenderCollider> colliders;
            [WriteOnly] public NativeArray<Vector3> result;
            public Matrix4x4 localToWorld, worldToLocal;
            public int colliderCount;
            public float thickness, alpha;
            public void Execute(int i)
            {
                float allowance = math.max(0, motion[i]);
                int particle = particles[i];
                float3 displacement = allowance > 0 ? math.lerp((float3)previous[particle], (float3)current[particle], alpha) : float3.zero;
                float lengthSq = math.lengthsq(displacement);
                if (lengthSq > allowance * allowance) displacement *= allowance * math.rsqrt(lengthSq);
                float3 world = (float3)localToWorld.MultiplyPoint3x4(pose[i]) + displacement;
                // Keep all three contact passes, including on interpolated frames.
                // Reject distant shapes before evaluating the exact tapered surface.
                if (allowance > 0)
                    for (int pass = 0; pass < 3; pass++)
                    {
                        bool moved = false;
                        for (int c = 0; c < colliderCount; c++)
                        {
                            if((collisionMasks[i]&(1UL<<c))==0)continue;
                            var shape = colliders[c];
                            if (math.any(world < shape.min) || math.any(world > shape.max)) continue;
                            float distance = shape.capsule.Distance(world, thickness, out var normal);
                            if (distance < 0) { world -= normal * distance; moved = true; }
                        }
                        if (!moved) break;
                    }
                result[i] = worldToLocal.MultiplyPoint3x4(world);
            }
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Solver?.Dispose(); Solver = null;
            if (renderPose.IsCreated) renderPose.Dispose();
            if (renderOutput.IsCreated) renderOutput.Dispose();
            if (renderMotion.IsCreated) renderMotion.Dispose();
            if(renderCollisionMasks.IsCreated)renderCollisionMasks.Dispose();
            if (renderParticles.IsCreated) renderParticles.Dispose();
            if (renderPrevious.IsCreated) renderPrevious.Dispose();
            if (renderCurrent.IsCreated) renderCurrent.Dispose();
            if (renderColliders.IsCreated) renderColliders.Dispose();
            renderCopy = null;
            if (Source) Source.forceRenderingOff = originalForceOff;
            if (skin) skin.updateWhenOffscreen = originalOffscreen;
            Destroy(outputObject); Destroy(Output); Destroy(baked);
            outputObject = null; Output = null; baked = null;
        }
        static void Destroy(Object value) { if (!value) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
