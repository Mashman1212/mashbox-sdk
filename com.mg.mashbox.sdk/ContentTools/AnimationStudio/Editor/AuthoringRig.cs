using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    // Rebuild only transforms and renderers. Instantiating a gameplay prefab can execute Awake.
    internal sealed partial class AuthoringRig : IDisposable
    {
        public Scene Scene { get; private set; }
        public GameObject Root { get; private set; }
        public Animator Animator { get; private set; }
        public Transform[] Bones { get; private set; }
        public string[] Paths { get; private set; }
        public PoseKey Rest { get; private set; }
        public readonly HashSet<Transform> DisplayBones = new HashSet<Transform>();
        public readonly Transform[] Ends = new Transform[4];
        public readonly Transform[] Middles = new Transform[4];
        public readonly Transform[] Starts = new Transform[4];
        public string SolverStatus { get; private set; }
        public bool HasIK => solver != null;
        public bool HumanLimitsEnabled { get; set; }
        public bool SupportsHumanLimits => Animator && Animator.avatar && Animator.avatar.isValid && Animator.avatar.isHuman;
        private LocalJointLimit[] humanJointLimits;

        // Avatar muscle conversion is used only once to calibrate local axes/ranges.
        // Never reconstruct a whole humanoid while the user is manipulating a joint:
        // that distributes forearm twist into neighbouring bones and can flip the chain.
        private sealed class LocalJointLimit
        {
            public Transform bone;
            public Quaternion neutral, basis;
            public Vector3 min, max;

            public Quaternion Clamp(Quaternion rotation)
            {
                Quaternion q = Quaternion.Inverse(basis) * Quaternion.Inverse(neutral) * rotation * basis;
                if (q.w < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                // Separate axial twist from the two swing directions. Unlike Euler angles,
                // this has no 90-degree gimbal transition at the elbow bend limit.
                float length = Mathf.Sqrt(q.x * q.x + q.w * q.w);
                Quaternion twist = length > 1e-6f ? new Quaternion(q.x / length, 0, 0, q.w / length) : Quaternion.identity;
                Quaternion swing = q * Quaternion.Inverse(twist);
                float x = Mathf.Clamp(2 * Mathf.Atan2(twist.x, twist.w) * Mathf.Rad2Deg, min.x, max.x);
                float y = Mathf.Clamp(2 * Mathf.Atan2(swing.y, Mathf.Max(swing.w, 1e-6f)) * Mathf.Rad2Deg, min.y, max.y);
                float z = Mathf.Clamp(2 * Mathf.Atan2(swing.z, Mathf.Max(swing.w, 1e-6f)) * Mathf.Rad2Deg, min.z, max.z);
                var limitedSwing = new Quaternion(0, Mathf.Tan(y * Mathf.Deg2Rad * .5f), Mathf.Tan(z * Mathf.Deg2Rad * .5f), 1).normalized;
                return neutral * basis * limitedSwing * Quaternion.AngleAxis(x, Vector3.right) * Quaternion.Inverse(basis);
            }
        }

        private static float JointSampleAngle(Quaternion delta, Vector3 axis)
        {
            if (delta.w < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            return 2 * Mathf.Atan2(Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), axis), delta.w) * Mathf.Rad2Deg;
        }

        private void BuildHumanJointLimits()
        {
            var saved = Bones.Select(BonePose.Read).ToArray();
            var limits = new List<LocalJointLimit>();
            try
            {
                using (var handler = new HumanPoseHandler(Animator.avatar, Root.transform))
                {
                    var human = new HumanPose();
                    handler.GetHumanPose(ref human);
                    Array.Clear(human.muscles, 0, human.muscles.Length);
                    for (int b = 0; b < (int)HumanBodyBones.LastBone; b++)
                    {
                        if (b == (int)HumanBodyBones.Hips) continue;
                        var bone = Animator.GetBoneTransform((HumanBodyBones)b);
                        if (!bone) continue;
                        int hinge = b == (int)HumanBodyBones.LeftLowerArm || b == (int)HumanBodyBones.RightLowerArm
                            || b == (int)HumanBodyBones.LeftLowerLeg || b == (int)HumanBodyBones.RightLowerLeg
                            ? HumanTrait.MuscleFromBone(b, 2) : -1;
                        if (hinge >= 0) human.muscles[hinge] = 1;
                        handler.SetHumanPose(ref human);
                        var neutral = bone.localRotation;
                        var low = new Quaternion[3]; var high = new Quaternion[3];
                        var axes = new Vector3[3];
                        bool any = false;
                        for (int a = 0; a < 3; a++)
                        {
                            int muscle = HumanTrait.MuscleFromBone(b, a);
                            if (muscle < 0) continue;
                            human.muscles[muscle] = -1;
                            handler.SetHumanPose(ref human);
                            low[a] = Quaternion.Inverse(neutral) * bone.localRotation;
                            human.muscles[muscle] = 1;
                            handler.SetHumanPose(ref human);
                            high[a] = Quaternion.Inverse(neutral) * bone.localRotation;
                            human.muscles[muscle] = muscle == hinge ? 1 : 0;
                            var delta = Quaternion.Inverse(low[a]) * high[a];
                            if (delta.w < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
                            axes[a] = new Vector3(delta.x, delta.y, delta.z).normalized;
                            any |= axes[a].sqrMagnitude > .5f;
                        }
                        if (hinge >= 0) human.muscles[hinge] = 0;
                        if (!any) continue;
                        Vector3 x = axes[0], y = axes[1], z = axes[2];
                        if (x.sqrMagnitude < .5f)
                            x = y.sqrMagnitude > .5f && z.sqrMagnitude > .5f ? Vector3.Cross(y, z).normalized
                                : Vector3.Cross(y.sqrMagnitude > .5f ? y : z,
                                    Mathf.Abs(Vector3.Dot(y.sqrMagnitude > .5f ? y : z, Vector3.up)) < .9f ? Vector3.up : Vector3.right).normalized;
                        if (y.sqrMagnitude < .5f) y = z.sqrMagnitude > .5f ? Vector3.Cross(z, x).normalized
                            : Vector3.Cross(Mathf.Abs(Vector3.Dot(x, Vector3.up)) < .9f ? Vector3.up : Vector3.forward, x).normalized;
                        y = Vector3.ProjectOnPlane(y, x).normalized;
                        z = Vector3.Cross(x, y).normalized;
                        var limit = new LocalJointLimit { bone = bone, neutral = neutral, basis = Quaternion.LookRotation(z, y) };
                        for (int a = 0; a < 3; a++)
                        {
                            if (axes[a].sqrMagnitude < .5f) continue;
                            var axis = a == 0 ? x : a == 1 ? y : z;
                            float lo = JointSampleAngle(low[a], axis), hi = JointSampleAngle(high[a], axis);
                            limit.min[a] = Mathf.Min(lo, hi); limit.max[a] = Mathf.Max(lo, hi);
                        }
                        limits.Add(limit);
                    }
                }
                humanJointLimits = limits.ToArray();
            }
            finally
            {
                for (int i = 0; i < Bones.Length; i++) saved[i].Apply(Bones[i]);
            }
        }

        public void EnforceHumanLimits()
        {
            if (!HumanLimitsEnabled || !SupportsHumanLimits) return;
            if (humanJointLimits == null) BuildHumanJointLimits();
            foreach (var limit in humanJointLimits)
            {
                var rotation = limit.bone.localRotation;
                var constrained = limit.Clamp(rotation);
                // Avoid touching valid joints (and accumulating quaternion roundoff).
                if (Quaternion.Angle(rotation, constrained) > .01f) limit.bone.localRotation = constrained;
            }
        }
        public void ConstrainAuthoredPose(PoseKey pose)
        {
            if (!HumanLimitsEnabled || !SupportsHumanLimits || pose == null) return;
            for (int i = 0; i < Bones.Length; i++) pose.bones[i].Apply(Bones[i]);
            EnforceHumanLimits();
            // Preserve IK targets, weights and pins without baking solved IK into FK channels.
            for (int i = 0; i < Bones.Length; i++) pose.bones[i] = BonePose.Read(Bones[i]);
        }
        private FinalIKBridge solver;
        private Material neutralMaterial;
        private readonly HashSet<Renderer> doubleSidedRenderers = new HashSet<Renderer>();
        private readonly Dictionary<Material, Material> doubleSidedMaterials = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> texturedMaterials = new Dictionary<Material, Material>();
        private readonly Dictionary<Renderer, Material[]> sourceMaterials = new Dictionary<Renderer, Material[]>();

        public void SetNeutralShading(bool enabled)
        {
            if (!neutralMaterial)
            {
                var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                var template = pipeline ? pipeline.defaultMaterial : null;
                var previewShader = Shader.Find("MashBox/Studio/Preview");
                neutralMaterial = previewShader && previewShader.isSupported ? new Material(previewShader)
                    : template ? new Material(template) : new Material(Shader.Find("Standard"));
                neutralMaterial.hideFlags = HideFlags.HideAndDontSave;
                if (neutralMaterial.HasProperty("_BaseColor")) neutralMaterial.SetColor("_BaseColor", new Color(0.6f, 0.65f, 0.7f));
                if (neutralMaterial.HasProperty("_Color")) neutralMaterial.SetColor("_Color", new Color(0.6f, 0.65f, 0.7f));
            }
            foreach (var renderer in Root.GetComponentsInChildren<Renderer>(true))
            {
                if (!sourceMaterials.ContainsKey(renderer)) sourceMaterials.Add(renderer, renderer.sharedMaterials);
                renderer.sharedMaterials = enabled ? sourceMaterials[renderer].Select(_ => neutralMaterial).ToArray()
                    : sourceMaterials[renderer].Select(TexturedPreviewMaterial).ToArray();
                if (doubleSidedRenderers.Contains(renderer))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(DoubleSidedPreviewMaterial).ToArray();
            }
        }

        private Material DoubleSidedPreviewMaterial(Material source)
        {
            if (doubleSidedMaterials.TryGetValue(source, out var cached)) return cached;
            var material = new Material(source) { name = source.name + " (Double-sided)", hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            doubleSidedMaterials.Add(source, material);
            return material;
        }

        private Material TexturedPreviewMaterial(Material source)
        {
            if (!source) return neutralMaterial;
            if (texturedMaterials.TryGetValue(source, out var cached)) return cached;
            // HDRP materials depend on scene exposure and lighting. Preview base color with
            // fixed studio shading instead, without changing the source material asset.
            var preview = new Material(neutralMaterial) { name = source.name + " (Animation Preview)", hideFlags = HideFlags.HideAndDontSave };
            string textureProperty = source.HasProperty("_BaseColorMap") && source.GetTexture("_BaseColorMap") ? "_BaseColorMap"
                : source.HasProperty("_BaseMap") && source.GetTexture("_BaseMap") ? "_BaseMap"
                : source.HasProperty("_MainTex") ? "_MainTex" : null;
            if (textureProperty != null)
            {
                preview.SetTexture("_MainTex", source.GetTexture(textureProperty));
                preview.SetTextureScale("_MainTex", source.GetTextureScale(textureProperty));
                preview.SetTextureOffset("_MainTex", source.GetTextureOffset(textureProperty));
            }
            preview.SetColor("_Color", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            bool cutout = source.IsKeywordEnabled("_ALPHATEST_ON") ||
                (source.HasProperty("_AlphaCutoffEnable") && source.GetFloat("_AlphaCutoffEnable") > 0) ||
                (source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0);
            preview.SetFloat("_Cutoff", cutout ? (source.HasProperty("_AlphaCutoff") ? source.GetFloat("_AlphaCutoff")
                : source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f) : 0);
            texturedMaterials.Add(source, preview);
            return preview;
        }

        public AuthoringRig(GameObject source, GameObject body, GameObject[] clothing = null, OutfitOptions[] outfitOptions = null)
        {
            if (!source) throw new ArgumentException("Choose a character or skeleton first.");
            Scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var sourceAnimator = source.GetComponent<Animator>();
                Root = new GameObject(source.name + " • Animation Studio");
                SceneManager.MoveGameObjectToScene(Root, Scene);
                Root.hideFlags = HideFlags.HideAndDontSave;
                var map = new Dictionary<Transform, Transform> { [source.transform] = Root.transform };
                BonePose.Read(source.transform).Apply(Root.transform);
                CloneChildren(source.transform, Root.transform, map);
                Bones = Root.GetComponentsInChildren<Transform>(true);
                Paths = Bones.Select(t => AnimationUtility.CalculateTransformPath(t, Root.transform)).ToArray();
                if (Paths.Distinct().Count() != Paths.Length)
                    throw new InvalidOperationException("Skeleton has duplicate transform paths. Rename duplicate siblings before authoring.");
                CopyRenderers(source, map);
                if (sourceAnimator && sourceAnimator.avatar && sourceAnimator.avatar.isValid && sourceAnimator.avatar.isHuman)
                {
                    Animator = Root.AddComponent<Animator>();
                    Animator.avatar = sourceAnimator.avatar;
                    Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    Animator.Rebind();
                    Animator.enabled = false;
                    // Rebind establishes the avatar but must not replace the requested source pose.
                    foreach (var pair in map)
                        if (pair.Key != source.transform) BonePose.Read(pair.Key).Apply(pair.Value);
                    HumanBodyBones[] ends = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
                    HumanBodyBones[] mids = { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
                    HumanBodyBones[] starts = { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg };
                    for (int i = 0; i < 4; i++)
                    {
                        Ends[i] = Animator.GetBoneTransform(ends[i]);
                        Middles[i] = Animator.GetBoneTransform(mids[i]);
                        Starts[i] = Animator.GetBoneTransform(starts[i]);
                    }
                    for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                        for (var bone = Animator.GetBoneTransform((HumanBodyBones)i); bone && bone != Root.transform; bone = bone.parent)
                            DisplayBones.Add(bone);
                    try { solver = new FinalIKBridge(Animator); SolverStatus = "Final IK • Independent limbs"; }
                    catch (Exception ex) { SolverStatus = "FK only: " + ex.GetBaseException().Message; }
                }
                else SolverStatus = "FK only: select the root with a valid Humanoid Animator for limb IK.";
                if (!Animator) foreach (var bone in Bones) DisplayBones.Add(bone);
                BuildOutfit(body, clothing, outfitOptions);
                Rest = Capture();
                AddPreviewLight("Studio key", new Vector3(35, -30, 0), 2f);
                AddPreviewLight("Studio fill", new Vector3(15, 145, 0), 0.7f);
            }
            catch { Dispose(); throw; }
        }

        private static void CloneChildren(Transform source, Transform parent, Dictionary<Transform, Transform> map)
        {
            foreach (Transform child in source)
            {
                var copy = new GameObject(child.name).transform;
                copy.SetParent(parent, false);
                BonePose.Read(child).Apply(copy);
                map.Add(child, copy);
                CloneChildren(child, copy, map);
                copy.gameObject.SetActive(child.gameObject.activeSelf);
            }
        }

        private void AddPreviewLight(string name, Vector3 angles, float intensity)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, Scene);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.rotation = Quaternion.Euler(angles);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        private static void CopyRenderers(GameObject source, Dictionary<Transform, Transform> map)
        {
            foreach (var renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var copy = map[renderer.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = renderer.sharedMesh;
                copy.sharedMaterials = renderer.sharedMaterials;
                copy.bones = renderer.bones.Select(t => t && map.TryGetValue(t, out var bone) ? bone : null).ToArray();
                copy.rootBone = renderer.rootBone && map.TryGetValue(renderer.rootBone, out var root) ? root : null;
                copy.localBounds = renderer.localBounds;
                copy.updateWhenOffscreen = true;
                copy.enabled = renderer.enabled;
                if (copy.sharedMesh)
                    for (int i = 0; i < copy.sharedMesh.blendShapeCount; i++)
                        copy.SetBlendShapeWeight(i, renderer.GetBlendShapeWeight(i));
            }
            foreach (var renderer in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter) continue;
                map[renderer.transform].gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = map[renderer.transform].gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                copy.enabled = renderer.enabled;
            }
        }

        private static string BoneName(string name) => name.Substring(name.LastIndexOf(':') + 1);

        private List<SkinnedMeshRenderer> AttachBody(GameObject body)
        {
            var attached = new List<SkinnedMeshRenderer>();
            var names = Bones.GroupBy(t => BoneName(t.name)).ToDictionary(g => g.Key, g => g.ToArray());
            var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0) throw new InvalidOperationException("Body must contain a SkinnedMeshRenderer.");
            foreach (var skin in skins)
            {
                var sourceBones = skin.bones;
                var mapped = new Transform[sourceBones.Length];
                var used = new bool[mapped.Length];
                int bindCount = skin.sharedMesh ? skin.sharedMesh.bindposes.Length : 0;
                if (skin.sharedMesh)
                    using (var weights = skin.sharedMesh.GetAllBoneWeights())
                        foreach (var weight in weights)
                        {
                            if (weight.weight <= 0) continue;
                            if (weight.boneIndex < 0 || weight.boneIndex >= used.Length || weight.boneIndex >= bindCount)
                                throw new InvalidOperationException("Outfit '" + body.name + "' has a skin weight referencing an invalid bone or bind-pose slot.");
                            used[weight.boneIndex] = true;
                        }
                for (int i = 0; i < mapped.Length; i++)
                {
                    if (!sourceBones[i] || !names.TryGetValue(BoneName(sourceBones[i].name), out var matches) || matches.Length != 1)
                    {
                        if (used[i])
                            throw new InvalidOperationException("Outfit '" + body.name + "' has a weighted bone that cannot be uniquely matched to the character Skeleton: " + (sourceBones[i] ? sourceBones[i].name : "missing bone") + ". Use an outfit bound to the shared Skeleton, or an explicit bone mapping.");
                        // FBX bone tables can retain unused helper joints. Keep
                        // slot indices and the source mesh intact; these slots
                        // cannot affect any vertex and need no rig correspondence.
                        mapped[i] = Root.transform;
                        continue;
                    }
                    mapped[i] = matches[0];
                }
                var go = new GameObject("Preview • " + skin.name);
                go.transform.SetParent(Root.transform, false);
                // Preserve the mesh's model-space transform, independent of its scene placement.
                go.transform.localPosition = body.transform.InverseTransformPoint(skin.transform.position);
                go.transform.localRotation = Quaternion.Inverse(body.transform.rotation) * skin.transform.rotation;
                var matrix = body.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                go.transform.localScale = matrix.lossyScale;
                var copy = go.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = skin.sharedMesh; copy.sharedMaterials = skin.sharedMaterials;
                copy.bones = mapped;
                if (skin.rootBone && names.TryGetValue(BoneName(skin.rootBone.name), out var roots) && roots.Length == 1)
                    copy.rootBone = roots[0];
                copy.localBounds = skin.localBounds; copy.updateWhenOffscreen = true;
                if (copy.sharedMesh)
                    for (int i = 0; i < copy.sharedMesh.blendShapeCount; i++)
                        copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                attached.Add(copy);
            }
            return attached;
        }

        public PoseKey Capture()
        {
            EnforceHumanLimits();
            var pose = new PoseKey { bones = Bones.Select(BonePose.Read).ToArray(), effectors = new EffectorPose[4] };
            for (int i = 0; i < 4; i++) pose.effectors[i] = MatchEffector(i, 0);
            return pose;
        }

        public EffectorPose MatchEffector(int i, float weight)
        {
            if (!Ends[i]) return new EffectorPose { rotation = Quaternion.identity };
            Vector3 axis = Ends[i].position - Starts[i].position;
            Vector3 bend = Vector3.ProjectOnPlane(Middles[i].position - Starts[i].position, axis);
            if (bend.sqrMagnitude < 0.000001f)
                bend = Vector3.ProjectOnPlane(i < 2 ? -Root.transform.forward : Root.transform.forward, axis);
            if (bend.sqrMagnitude < 0.000001f) bend = Vector3.ProjectOnPlane(Root.transform.up, axis);
            return new EffectorPose
            {
                position = Root.transform.InverseTransformPoint(Ends[i].position),
                rotation = Quaternion.Inverse(Root.transform.rotation) * Ends[i].rotation,
                pole = Root.transform.InverseTransformPoint(Middles[i].position + bend.normalized * Mathf.Max(axis.magnitude * 0.5f, 0.1f)),
                weight = weight
            };
        }

        // Match from the displayed, solved transforms before changing control methods.
        // Only this chain is baked into FK; other limbs retain their authored channels.
        public void MatchLimbBlend(PoseKey pose, int limb, float weight, bool force = false)
        {
            weight = Mathf.Clamp01(weight);
            if (!force && Mathf.Approximately(pose.effectors[limb].weight, weight)) return;
            foreach (var bone in new[] { Starts[limb], Middles[limb], Ends[limb] })
            {
                int index = Array.IndexOf(Bones, bone);
                if (index < 0) throw new InvalidOperationException("IK limb is not part of this skeleton.");
                pose.bones[index] = BonePose.Read(bone);
            }
            var matched = MatchEffector(limb, weight);
            matched.CopyPinsFrom(pose.effectors[limb]);
            pose.effectors[limb] = matched;
        }

        public void Apply(PoseKey pose)
        {
            if (pose == null || pose.bones.Length != Bones.Length || pose.effectors.Length != 4)
                throw new InvalidOperationException("Take does not match this skeleton. Create a new take for this character.");
            for (int i = 0; i < Bones.Length; i++) pose.bones[i].Apply(Bones[i]);
            EnforceHumanLimits();
            solver?.Solve(pose.effectors);
            EnforceHumanLimits();
        }

        public void Dispose()
        {
            humanJointLimits = null;
            solver = null;
            if (neutralMaterial) Object.DestroyImmediate(neutralMaterial);
            foreach (var material in texturedMaterials.Values) if (material) Object.DestroyImmediate(material);
            texturedMaterials.Clear();
            foreach (var material in doubleSidedMaterials.Values) if (material) Object.DestroyImmediate(material);
            doubleSidedMaterials.Clear(); doubleSidedRenderers.Clear();
            foreach (var mesh in outfitMeshes) if (mesh) Object.DestroyImmediate(mesh);
            outfitMeshes.Clear();
            if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
            Scene = default; Root = null;
        }
    }

    // Optional dependency: Final IK is distributed in Assembly-CSharp in this project.
    // Reflection keeps the SDK assembly usable without bundling or requiring the paid plugin.
    internal sealed class FinalIKBridge
    {
        private readonly Transform root;
        private readonly Transform[] starts = new Transform[4], middles = new Transform[4], ends = new Transform[4];
        private readonly MethodInfo solve;
        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);

        public FinalIKBridge(Animator animator)
        {
            root = animator.transform;
            var type = FindType("RootMotion.FinalIK.IKSolverTrigonometric");
            if (type == null) throw new InvalidOperationException("Final IK is not installed.");
            solve = type.GetMethod("Solve", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Transform), typeof(Transform), typeof(Transform), typeof(Vector3), typeof(Vector3), typeof(float) }, null);
            if (solve == null) throw new MissingMethodException(type.FullName, "Solve");
            HumanBodyBones[] first = { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg };
            HumanBodyBones[] middle = { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
            HumanBodyBones[] last = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
            for (int i = 0; i < 4; i++)
            {
                starts[i] = animator.GetBoneTransform(first[i]);
                middles[i] = animator.GetBoneTransform(middle[i]);
                ends[i] = animator.GetBoneTransform(last[i]);
                if (!starts[i] || !middles[i] || !ends[i])
                    throw new InvalidOperationException("Humanoid is missing the " + first[i] + " chain.");
            }
        }

        public void Solve(EffectorPose[] pose)
        {
            // Authoring a limb must never remap the pelvis, spine or other limbs.
            // The static solver preserves the authored twist via minimal bone rotations.
            for (int i = 0; i < 4; i++)
            {
                var effector = pose[i].ResolvePins(root);
                float weight = effector.pinPosition ? 1 : Mathf.Clamp01(effector.weight);
                float rotationWeight = effector.pinRotation ? 1 : Mathf.Clamp01(effector.weight);
                if (weight <= 0 && rotationWeight <= 0) continue;
                Vector3 target = root.TransformPoint(effector.position);
                Vector3 axis = target - starts[i].position;
                Vector3 normal = Vector3.Cross(root.TransformPoint(effector.pole) - starts[i].position, axis);
                if (normal.sqrMagnitude < 1e-12f)
                    normal = Vector3.Cross(middles[i].position - starts[i].position, ends[i].position - starts[i].position);
                if (normal.sqrMagnitude < 1e-12f)
                    normal = Vector3.Cross(i < 2 ? -root.forward : root.forward, axis);
                if (normal.sqrMagnitude < 1e-12f) normal = Vector3.Cross(root.up, axis);
                Quaternion rotation = Quaternion.Slerp(ends[i].rotation, root.rotation * effector.rotation, rotationWeight);
                solve.Invoke(null, new object[] { starts[i], middles[i], ends[i], target, normal.normalized, weight });
                ends[i].rotation = rotation;
            }
        }
    }
}
