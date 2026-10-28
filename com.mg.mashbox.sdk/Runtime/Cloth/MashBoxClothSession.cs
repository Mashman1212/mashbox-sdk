using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
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
        readonly List<Vector3> outputVertices = new List<Vector3>();
        readonly ClothCapsule[] capsules = new ClothCapsule[64];
        readonly Transform[] starts, ends;
        readonly bool originalForceOff;
        readonly bool originalOffscreen;
        readonly SkinnedMeshRenderer skin;
        GameObject outputObject;
        Mesh baked;
        Vector3 lastPosition;
        Vector3 lastScale;
        float accumulator, time;
        readonly Stopwatch totalWatch = new Stopwatch(), solveWatch = new Stopwatch(), bakeWatch = new Stopwatch();
        bool disposed;
        public bool Visible => outputObject && outputObject.GetComponent<Renderer>().isVisible;
        public IReadOnlyList<Vector3> Vertices => outputVertices;

        public static Mesh GetMesh(Renderer renderer) => renderer is SkinnedMeshRenderer s ? s.sharedMesh : renderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;

        public MashBoxClothSession(Renderer source, MashBoxClothProfile profile, Transform collisionRoot)
        {
            Source = source; Profile = profile; SourceMesh = GetMesh(source); skin = source as SkinnedMeshRenderer;
            if (!source || !profile || !profile.Matches(SourceMesh)) throw new ArgumentException("Assign a profile made for this exact garment mesh.");
            if (!SourceMesh.isReadable) throw new ArgumentException("Enable Read/Write on the garment mesh importer.");
            if (source.forceRenderingOff) throw new ArgumentException("The garment is already hidden by another preview or cloth session.");
            originalForceOff = source.forceRenderingOff;
            originalOffscreen = skin && skin.updateWhenOffscreen;
            var bindings = profile.bodyCapsules ?? Array.Empty<ClothBoneCapsule>();
            starts = new Transform[Math.Min(64, bindings.Length)]; ends = new Transform[starts.Length];
            for (int i = 0; i < starts.Length; i++)
            {
                starts[i] = collisionRoot ? collisionRoot.Find(bindings[i].startBone ?? "") : null;
                ends[i] = collisionRoot ? collisionRoot.Find(bindings[i].endBone ?? "") : null;
            }
            try
            {
                baked = new Mesh { name = "Cloth skin pose", hideFlags = HideFlags.HideAndDontSave };
                bakedVertices.Capacity = outputVertices.Capacity = SourceMesh.vertexCount;
                ReadPose();
                var topology = new MashBoxClothTopology(bakedVertices.ToArray(), SourceMesh.triangles, profile.particleBudget);
                Solver = new MashBoxClothSolver(topology);
                Solver.SetTargets(bakedVertices, Source.transform.localToWorldMatrix, profile.motion);
                Solver.Reset(true);
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

        void ReadPose()
        {
            // Compensate the renderer transform scale: output is rendered under that same transform.
            if (skin) { skin.BakeMesh(baked, true); baked.GetVertices(bakedVertices); }
            else SourceMesh.GetVertices(bakedVertices);
        }

        public void Reset()
        {
            ReadPose(); Solver.SetTargets(bakedVertices, Source.transform.localToWorldMatrix, Profile.motion);
            Solver.Reset(); accumulator = time = 0; lastPosition = Source.transform.position;
            WriteOutput();
        }

        public void Advance(float deltaTime)
        {
            if (disposed || !Source || !Profile) return;
            float fixedDt = 1f / Mathf.Clamp(Profile.frequency, 15, 120);
            accumulator += Mathf.Clamp(deltaTime, 0, .25f);
            int due = Mathf.FloorToInt(accumulator / fixedDt);
            if (due == 0) return;
            // Never spiral on a slow frame. Excess time is explicitly counted in the HUD.
            DroppedSteps += Mathf.Max(0, due - 3);
            accumulator -= due * fixedDt;
            for (int i = 0; i < Mathf.Min(due, 3); i++) Step(fixedDt);
        }

        public void Step(float dt)
        {
            if (disposed || !Source || !Profile) return;
            if (!Profile.Matches(GetMesh(Source))) throw new InvalidOperationException("Garment mesh or profile changed; rebuild the cloth session.");
            totalWatch.Restart(); bakeWatch.Restart();
            ReadPose(); bakeWatch.Stop(); BakeMilliseconds = bakeWatch.Elapsed.TotalMilliseconds;
            Solver.SetTargets(bakedVertices, Source.transform.localToWorldMatrix, Profile.motion);
            bool scaleChanged = (Source.transform.lossyScale - lastScale).sqrMagnitude > .000001f;
            if (scaleChanged || Vector3.Distance(lastPosition, Source.transform.position) > Mathf.Max(.1f, Profile.teleportDistance)) Solver.Reset(scaleChanged);
            lastPosition = Source.transform.position; lastScale = Source.transform.lossyScale;
            int count = 0;
            for (int i = 0; i < starts.Length; i++)
                if (starts[i] && ends[i] && i < Profile.bodyCapsules.Length)
                {
                    var s = starts[i].lossyScale;
                    capsules[count++] = new ClothCapsule(starts[i].position, ends[i].position,
                        Mathf.Max(.001f, Profile.bodyCapsules[i].radius) * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
                }
            solveWatch.Restart(); Solver.Step(dt, time, Profile, capsules, count); solveWatch.Stop();
            SolveMilliseconds = solveWatch.Elapsed.TotalMilliseconds;
            time += dt; Steps++; WriteOutput();
            totalWatch.Stop(); TotalMilliseconds = totalWatch.Elapsed.TotalMilliseconds;
        }

        void WriteOutput()
        {
            outputVertices.Clear(); var inverse = Source.transform.worldToLocalMatrix;
            for (int i = 0; i < bakedVertices.Count; i++)
            {
                Vector3 displacement = Profile.motion[i] <= 0 ? Vector3.zero : Solver.Displacement(Solver.Topology.vertexParticle[i]);
                outputVertices.Add(bakedVertices[i] + inverse.MultiplyVector(displacement));
            }
            Output.SetVertices(outputVertices); Output.RecalculateBounds();
            if (Profile.normalsEverySteps > 0 && Steps % Profile.normalsEverySteps == 0) Output.RecalculateNormals();
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Solver?.Dispose(); Solver = null;
            if (Source) Source.forceRenderingOff = originalForceOff;
            if (skin) skin.updateWhenOffscreen = originalOffscreen;
            Destroy(outputObject); Destroy(Output); Destroy(baked);
            outputObject = null; Output = null; baked = null;
        }
        static void Destroy(Object value) { if (!value) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
