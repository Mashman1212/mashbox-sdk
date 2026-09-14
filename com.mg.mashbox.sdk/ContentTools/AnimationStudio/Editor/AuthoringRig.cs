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
                var previewShader = Shader.Find("Hidden/MashBox/AnimationStudioPreview");
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
                    try { solver = new FinalIKBridge(Animator); SolverStatus = "Final IK • Full Body Biped"; }
                    catch (Exception ex) { SolverStatus = "FK only: " + ex.GetBaseException().Message; }
                }
                else SolverStatus = "FK only: select the root with a valid Humanoid Animator for FBIK.";
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
                var mapped = new Transform[skin.bones.Length];
                for (int i = 0; i < mapped.Length; i++)
                {
                    if (!skin.bones[i] || !names.TryGetValue(BoneName(skin.bones[i].name), out var matches) || matches.Length != 1)
                        throw new InvalidOperationException("Body bone cannot be uniquely matched: " + (skin.bones[i] ? skin.bones[i].name : "missing bone"));
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

        public void Apply(PoseKey pose)
        {
            if (pose == null || pose.bones.Length != Bones.Length || pose.effectors.Length != 4)
                throw new InvalidOperationException("Take does not match this skeleton. Create a new take for this character.");
            for (int i = 0; i < Bones.Length; i++) pose.bones[i].Apply(Bones[i]);
            solver?.Solve(pose.effectors);
        }

        public void Dispose()
        {
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
        private readonly object solver;
        private readonly object[] effectors = new object[4];
        private readonly object[] bends = new object[4];
        private readonly Transform[] poles = new Transform[4];
        private readonly Transform root;
        private readonly MethodInfo update;
        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
        private static object Get(object o, string name) => o.GetType().GetField(name)?.GetValue(o)
            ?? o.GetType().GetProperty(name)?.GetValue(o);
        private static void Set(object o, string name, object value)
        {
            var field = o.GetType().GetField(name);
            if (field == null) throw new MissingFieldException(o.GetType().Name, name);
            field.SetValue(o, value);
        }

        public FinalIKBridge(Animator animator)
        {
            var type = FindType("RootMotion.FinalIK.IKSolverFullBodyBiped");
            var refType = FindType("RootMotion.BipedReferences");
            if (type == null || refType == null) throw new InvalidOperationException("Final IK is not installed.");
            root = animator.transform;
            var refs = Activator.CreateInstance(refType);
            Set(refs, "root", root);
            string[] names = { "pelvis", "head", "leftThigh", "leftCalf", "leftFoot", "rightThigh", "rightCalf", "rightFoot", "leftUpperArm", "leftForearm", "leftHand", "rightUpperArm", "rightForearm", "rightHand" };
            HumanBodyBones[] ids = { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };
            for (int i = 0; i < names.Length; i++)
            {
                var bone = animator.GetBoneTransform(ids[i]);
                if (!bone) throw new InvalidOperationException("Humanoid is missing " + ids[i]);
                Set(refs, names[i], bone);
            }
            var spine = new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }
                .Select(animator.GetBoneTransform).Where(t => t).ToArray();
            if (spine.Length == 0) throw new InvalidOperationException("Humanoid has no spine mapping.");
            Set(refs, "spine", spine);
            solver = Activator.CreateInstance(type);
            type.GetMethod("SetToReferences").Invoke(solver, new[] { refs, (object)spine[0] });
            type.GetMethod("Initiate").Invoke(solver, new object[] { root });
            if (!(bool)Get(solver, "initiated")) throw new InvalidOperationException("Final IK could not initialize this skeleton.");
            update = type.GetMethod("Update", Type.EmptyTypes);
            string[] sides = { "leftHand", "rightHand", "leftFoot", "rightFoot" };
            string[] chains = { "leftArmChain", "rightArmChain", "leftLegChain", "rightLegChain" };
            for (int i = 0; i < 4; i++)
            {
                effectors[i] = Get(solver, sides[i] + "Effector");
                bends[i] = Get(Get(solver, chains[i]), "bendConstraint");
                poles[i] = new GameObject("Pole " + i).transform;
                poles[i].SetParent(root, false);
                Set(bends[i], "bendGoal", poles[i]);
            }
        }

        public void Solve(EffectorPose[] pose)
        {
            // FK mode must be exact; even zero-weight FBIK can apply mapping constraints.
            if (pose.All(p => p.weight <= 0)) return;
            for (int i = 0; i < 4; i++)
            {
                Set(effectors[i], "position", root.TransformPoint(pose[i].position));
                Set(effectors[i], "rotation", root.rotation * pose[i].rotation);
                Set(effectors[i], "positionWeight", pose[i].weight);
                Set(effectors[i], "rotationWeight", pose[i].weight);
                poles[i].position = root.TransformPoint(pose[i].pole);
                Set(bends[i], "weight", pose[i].weight);
            }
            update.Invoke(solver, null);
        }
    }
}
