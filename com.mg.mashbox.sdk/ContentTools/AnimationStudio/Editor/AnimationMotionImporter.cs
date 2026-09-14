using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.AnimationStudio
{
    [Serializable] internal sealed class GeneratedMotionFrame
    {
        public Vector3 translation;
        public Quaternion[] rotations;
    }
    [Serializable] internal sealed class GeneratedMotion
    {
        public int version;
        public int fps;
        public string provider;
        public string note;
        public string[] names;
        public int[] parents;
        public Vector3[] restPositions;
        public GeneratedMotionFrame[] frames;
    }

    internal static class AnimationMotionImporter
    {
        internal static PoseKey SampleEndpoint(AnimationClip clip, float time, GameObject character, string[] paths)
        {
            if (!clip || !character) throw new InvalidOperationException("Choose a clip and character before sampling a pose.");
            // Clip curves may animate more than bones. Sample a separate preview rig so even
            // object activation, root scale and renderer curves cannot alter the working scene.
            using (var sampler = new AuthoringRig(character, null))
            {
                if (!sampler.Paths.SequenceEqual(paths)) throw new InvalidOperationException("Clip sampler does not match the current skeleton.");
                if (clip.isHumanMotion)
                {
                    if (!sampler.Animator || !sampler.Animator.avatar || !sampler.Animator.avatar.isHuman)
                        throw new InvalidOperationException("A Humanoid clip needs a valid Humanoid character avatar.");
                }
                else
                {
                    bool compatible = AnimationUtility.GetCurveBindings(clip).Any(binding => binding.type == typeof(Transform)
                        && Array.IndexOf(paths, binding.path) >= 0);
                    if (!compatible) throw new InvalidOperationException("This clip has no matching skeleton transform curves. Use a Humanoid clip or a clip authored for this skeleton.");
                    if (sampler.Animator) { sampler.Animator.avatar = null; sampler.Animator.Rebind(); }
                }
                sampler.Apply(sampler.Rest);
                clip.SampleAnimation(sampler.Root, Mathf.Clamp(time, 0, clip.length));
                return sampler.Capture();
            }
        }

        internal static void MatchEndpoints(AnimationTake take, PoseKey start, PoseKey end, float seconds)
        {
            if (start == null && end == null) return;
            if (take.lastFrame < 1 || take.lastFrame > 3600 || take.frameRate < 1 || take.keys.Count == 0)
                throw new InvalidOperationException("Pose matching needs a take of 2–3601 frames.");
            foreach (var endpoint in new[] { start, end })
                if (endpoint != null && (endpoint.bones.Length != take.bonePaths.Length || endpoint.effectors.Length != 4))
                    throw new InvalidOperationException("Captured endpoint does not match this skeleton. Capture it again.");
            int duration = Mathf.Clamp(Mathf.RoundToInt(seconds * take.frameRate), 1,
                start != null && end != null ? Mathf.Max(1, take.lastFrame / 2) : take.lastFrame);
            var keys = new System.Collections.Generic.List<PoseKey>();
            for (int f = 0; f <= take.lastFrame; f++)
            {
                var key = take.Evaluate(f);
                if (start != null && f < duration)
                    key = PoseKey.Blend(start, key, Mathf.SmoothStep(0, 1, (float)f / duration));
                if (end != null && f > take.lastFrame-duration)
                    key = PoseKey.Blend(key, end, Mathf.SmoothStep(0, 1, (float)(f-take.lastFrame+duration) / duration));
                key.frame = f; keys.Add(key);
            }
            // Explicit copies guarantee exact endpoints, including a two-frame take.
            if (start != null) keys[0] = start.Copy(0);
            if (end != null) keys[take.lastFrame] = end.Copy(take.lastFrame);
            take.keys = keys; take.interpolation = PoseInterpolation.Linear;
        }

        internal static void ConnectWithSteps(AnimationTake take, AuthoringRig rig, PoseKey start, PoseKey end, float seconds, float lift, float bodyWeight = .8f)
        {
            if (start == null && end == null) return;
            if (!take.bonePaths.SequenceEqual(rig.Paths) || take.keys.Count == 0 || take.frameRate < 1)
                throw new InvalidOperationException("Step transitions require a take matching the current skeleton.");
            for (int i = 2; i < 4; i++)
                if (!rig.Starts[i] || !rig.Middles[i] || !rig.Ends[i])
                    throw new InvalidOperationException("Step transitions require both humanoid legs.");
            int lead = Mathf.Clamp(take.endpointLeadFrames,0,take.lastFrame);
            int tail = Mathf.Clamp(take.endpointTailFrames,0,take.lastFrame-lead);
            int sourceLast = take.lastFrame-lead-tail;
            int count = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(.2f,seconds)*take.frameRate));
            int total = sourceLast + count*((start != null ? 1 : 0)+(end != null ? 1 : 0));
            if (total > 3600) throw new InvalidOperationException("The take plus step transitions exceeds 3601 frames.");
            var saved = rig.Capture();
            var keys = new System.Collections.Generic.List<PoseKey>();
            try
            {
                if (start != null) AddStepBridge(keys,rig,start,take.Evaluate(lead),count,lift,false,bodyWeight);
                for (int f = 0; f <= sourceLast; f++) keys.Add(take.Evaluate(f+lead).Copy(keys.Count));
                if (end != null) AddStepBridge(keys,rig,take.Evaluate(take.lastFrame-tail),end,count,lift,true,bodyWeight);
                take.keys = keys; take.lastFrame = keys.Count-1; take.interpolation = PoseInterpolation.Linear;
                take.endpointLeadFrames = start != null ? count : 0;
                take.endpointTailFrames = end != null ? count : 0;
            }
            finally { rig.Apply(saved); }
        }

        private static void AddStepBridge(System.Collections.Generic.List<PoseKey> keys, AuthoringRig rig,
            PoseKey from, PoseKey to, int count, float lift, bool ending, float bodyWeight)
        {
            var a = new Vector3[2]; var b = new Vector3[2];
            var ar = new Quaternion[2]; var br = new Quaternion[2];
            var length = new float[2];
            rig.Apply(from);
            for (int s = 0; s < 2; s++)
            {
                a[s] = rig.Ends[s+2].position; ar[s] = rig.Ends[s+2].rotation;
                length[s] = Vector3.Distance(rig.Starts[s+2].position,rig.Middles[s+2].position)+Vector3.Distance(rig.Middles[s+2].position,rig.Ends[s+2].position);
            }
            rig.Apply(to);
            for (int s = 0; s < 2; s++)
            {
                b[s] = rig.Ends[s+2].position; br[s] = rig.Ends[s+2].rotation;
                if (Mathf.Abs(a[s].y-b[s].y) > length[s]*.18f)
                    throw new InvalidOperationException("The transition connects to a lifted foot. Trim the dance to a grounded frame or turn off Step between stances.");
            }
            int first = Vector3.Distance(a[0],b[0]) >= Vector3.Distance(a[1],b[1]) ? 0 : 1;
            var hips = rig.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var chest = rig.Animator.GetBoneTransform(HumanBodyBones.Chest) ?? rig.Animator.GetBoneTransform(HumanBodyBones.Spine);
            var head = rig.Animator.GetBoneTransform(HumanBodyBones.Head);
            var targets = new Vector3[2]; var rotations = new Quaternion[2];
            var moving = new bool[2];
            for (int s = 0; s < 2; s++) moving[s] = Vector3.ProjectOnPlane(b[s]-a[s],Vector3.up).magnitude > length[s]*.025f || Quaternion.Angle(ar[s],br[s]) > 12;
            for (int f = ending ? 1 : 0; f <= (ending ? count : count-1); f++)
            {
                float t = (float)f/count;
                var blended = PoseKey.Blend(from,to,Mathf.SmoothStep(0,1,t));
                rig.Apply(blended);
                // Anticipate each step on two planted feet, then swing, transfer and settle.
                // These are kinematic weight-transfer cues, not a physical COM simulation.
                for (int s = 0; s < 2; s++)
                {
                    float phase = s == first ? Mathf.Clamp01((t-.12f)/.31f) : Mathf.Clamp01((t-.60f)/.30f);
                    float smooth = Mathf.SmoothStep(0,1,phase);
                    Vector3 target = Vector3.Lerp(a[s],b[s],smooth);
                    float horizontal = Vector3.ProjectOnPlane(b[s]-a[s],Vector3.up).magnitude;
                    if (moving[s])
                        target.y += Mathf.Clamp(lift,.01f,.25f)*length[s]*Mathf.Pow(Mathf.Sin(Mathf.PI*phase),2);
                    targets[s] = target; rotations[s] = Quaternion.Slerp(ar[s],br[s],smooth);
                }
                Vector3 shift = Vector3.zero;
                float load = 0;
                if (hips)
                {
                    for (int s = 0; s < 2; s++)
                    {
                        if (!moving[s]) continue;
                        float support = s == first ? StepLoad(t,0,.12f,.43f,.55f) : StepLoad(t,.48f,.60f,.90f,1);
                        Vector3 towardSupport = Vector3.ProjectOnPlane(targets[1-s]-hips.position,Vector3.up);
                        shift += Vector3.ClampMagnitude(towardSupport,length[s]*.16f)*support*Mathf.Clamp01(bodyWeight);
                        load += support;
                    }
                    Quaternion gaze = head ? head.rotation : Quaternion.identity;
                    hips.position += shift;
                    // Loading the support knee softens lift-off and landing. Both leg solves
                    // run after the body move, preserving the planted foot's target.
                    hips.position -= Vector3.up*Mathf.Min(length[0],length[1])*.025f*load;
                    if (shift.sqrMagnitude > .0000001f)
                    {
                        Vector3 rollAxis = Vector3.Cross(Vector3.up,shift.normalized);
                        float lean = Mathf.Clamp01(shift.magnitude/(Mathf.Min(length[0],length[1])*.16f));
                        hips.rotation = Quaternion.AngleAxis(2.5f*lean,rollAxis)*hips.rotation;
                        if (chest) chest.rotation = Quaternion.AngleAxis(-4f*lean,rollAxis)*chest.rotation;
                        if (head) head.rotation = Quaternion.Slerp(head.rotation,gaze,.7f*Mathf.Clamp01(load));
                    }
                }
                for (int s = 0; s < 2; s++)
                {
                    SolveLeg(rig.Starts[s+2],rig.Middles[s+2],rig.Ends[s+2],targets[s]);
                    rig.Ends[s+2].rotation = rotations[s];
                }
                var key = rig.Capture();
                if (f == 0) key = from.Copy(0);
                if (f == count) key = to.Copy(0);
                key.frame = keys.Count; keys.Add(key);
            }
        }

        private static float StepLoad(float t, float begin, float loaded, float landed, float settled)
        {
            return Mathf.SmoothStep(0,1,Mathf.InverseLerp(begin,loaded,t)) *
                (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(landed,settled,t)));
        }

        // Work in the retargeted character's world space, then bake back to editable FK keys.
        // No runtime solver or per-frame asset writes are needed for the resulting take.
        internal static int PlantFeet(AnimationTake take, AuthoringRig rig, float speed = .25f)
        {
            if (take.lastFrame < 1 || take.lastFrame > 3600 || take.frameRate < 1)
                throw new InvalidOperationException("Foot cleanup supports takes of 2–3601 frames.");
            if (!take.bonePaths.SequenceEqual(rig.Paths))
                throw new InvalidOperationException("The take does not match the current character.");
            for (int side = 2; side < 4; side++)
                if (!rig.Starts[side] || !rig.Middles[side] || !rig.Ends[side])
                    throw new InvalidOperationException("Foot cleanup needs both humanoid leg chains.");
            var saved = rig.Capture();
            var samples = new PoseKey[take.lastFrame + 1];
            var feet = new Vector3[2][] { new Vector3[samples.Length], new Vector3[samples.Length] };
            var lengths = new float[2];
            int contacts = 0;
            try
            {
                rig.Apply(rig.Rest);
                for (int s = 0; s < 2; s++) lengths[s] = Vector3.Distance(rig.Starts[s+2].position, rig.Middles[s+2].position)
                    + Vector3.Distance(rig.Middles[s+2].position, rig.Ends[s+2].position);
                for (int f = 0; f < samples.Length; f++)
                {
                    rig.Apply(take.Evaluate(f)); samples[f] = rig.Capture(); samples[f].frame = f;
                    for (int s = 0; s < 2; s++) feet[s][f] = rig.Ends[s+2].position;
                }
                for (int s = 0; s < 2; s++)
                {
                    var points = feet[s];
                    // A low percentile avoids a single bad frame setting the contact plane.
                    float floor = points.Select(p => p.y).OrderBy(y => y).ElementAt((points.Length - 1) / 20);
                    var contact = new bool[points.Length];
                    for (int f = 0; f < points.Length; f++)
                    {
                        int a = Mathf.Max(0, f-1), b = Mathf.Min(points.Length-1, f+1);
                        float velocity = Vector3.Distance(points[a], points[b]) * take.frameRate / (b-a);
                        contact[f] = points[f].y <= floor + lengths[s] * .055f && velocity <= lengths[s] * speed;
                    }
                    int minimum = Mathf.Max(3, Mathf.CeilToInt(take.frameRate * .10f));
                    int blend = Mathf.Max(1, Mathf.RoundToInt(take.frameRate * .067f));
                    for (int start = 0; start < points.Length; start++)
                    {
                        if (!contact[start]) continue;
                        int end = start;
                        while (end+1 < points.Length && contact[end+1]) end++;
                        if (end-start+1 >= minimum)
                        {
                            contacts++;
                            var anchor = points[start];
                            for (int f = start; f <= end; f++)
                            {
                                float weight = Mathf.Min(start == 0 ? 1 : (float)(f-start)/blend,
                                    end == points.Length-1 ? 1 : (float)(end-f)/blend);
                                weight = Mathf.SmoothStep(0, 1, weight);
                                rig.Apply(samples[f]);
                                SolveLeg(rig.Starts[s+2], rig.Middles[s+2], rig.Ends[s+2], Vector3.Lerp(points[f], anchor, weight));
                                samples[f] = rig.Capture(); samples[f].frame = f;
                            }
                        }
                        start = end;
                    }
                }
                if (contacts > 0)
                {
                    take.keys = samples.ToList();
                    take.interpolation = PoseInterpolation.Linear;
                }
                return contacts;
            }
            finally { rig.Apply(saved); }
        }

        private static void SolveLeg(Transform upper, Transform lower, Transform foot, Vector3 target)
        {
            Quaternion rotation = foot.rotation;
            Vector3 origin = upper.position, delta = target-origin;
            float a = Vector3.Distance(origin, lower.position), b = Vector3.Distance(lower.position, foot.position);
            if (a < .00001f || b < .00001f || delta.sqrMagnitude < .00000001f) return;
            Vector3 axis = delta.normalized;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a-b)+.00001f, a+b-.00001f);
            Vector3 bend = Vector3.ProjectOnPlane(lower.position-origin, axis);
            if (bend.sqrMagnitude < .00000001f) bend = Vector3.ProjectOnPlane(foot.forward, axis);
            if (bend.sqrMagnitude < .00000001f) bend = Vector3.Cross(axis, Vector3.right);
            if (bend.sqrMagnitude < .00000001f) bend = Vector3.Cross(axis, Vector3.up);
            float along = (a*a-b*b+distance*distance)/(2*distance);
            Vector3 knee = origin + axis*along + bend.normalized * Mathf.Sqrt(Mathf.Max(0, a*a-along*along));
            upper.rotation = Quaternion.FromToRotation(lower.position-origin, knee-origin) * upper.rotation;
            Vector3 reachable = origin + axis*distance;
            lower.rotation = Quaternion.FromToRotation(foot.position-lower.position, reachable-lower.position) * lower.rotation;
            foot.rotation = rotation; // Keep authored foot pivots and toe articulation.
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        internal static void Validate(GeneratedMotion data)
        {
            if (data == null || data.version != 1 || data.fps < 1 || data.fps > 120 ||
                data.names == null || data.names.Length < 15 || data.names.Length > 55 ||
                data.parents == null || data.parents.Length != data.names.Length ||
                data.restPositions == null || data.restPositions.Length != data.names.Length ||
                data.frames == null || data.frames.Length < 2 || data.frames.Length > 3600)
                throw new InvalidOperationException("Invalid generated motion format or duration.");
            if (data.names.Distinct().Count() != data.names.Length || data.names[0] != "Hips" || data.parents[0] != -1)
                throw new InvalidOperationException("Motion must have one Hips root and unique humanoid bone names.");
            for (int i = 0; i < data.names.Length; i++)
            {
                if (!Enum.TryParse(data.names[i], out HumanBodyBones bone) || bone == HumanBodyBones.LastBone ||
                    (i > 0 && (data.parents[i] < 0 || data.parents[i] >= i)) || !Finite(data.restPositions[i]))
                    throw new InvalidOperationException("Invalid generated skeleton mapping.");
            }
            foreach (var frame in data.frames)
            {
                if (frame == null || !Finite(frame.translation) || frame.rotations == null || frame.rotations.Length != data.names.Length)
                    throw new InvalidOperationException("Incomplete motion frame.");
                foreach (var q in frame.rotations)
                    if (!Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w) || Quaternion.Dot(q, q) < .5f || Quaternion.Dot(q, q) > 1.5f)
                        throw new InvalidOperationException("Invalid motion rotation.");
            }
        }

        internal static AnimationTake Create(GeneratedMotion data, AuthoringRig rig, GameObject character,
            GameObject body, GameObject[] clothing, OutfitOptions[] outfitOptions, bool showProgress = true)
        {
            Validate(data);
            if (!rig.Animator || !rig.Animator.avatar || !rig.Animator.avatar.isHuman)
                throw new InvalidOperationException("Generated motion needs a valid Humanoid character avatar.");
            var savedPose = rig.Capture();
            var source = new GameObject("Motion Source") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(source, rig.Scene);
            Avatar avatar = null;
            AnimationTake result = null;
            try
            {
                var transforms = new Transform[data.names.Length];
                var skeleton = new SkeletonBone[data.names.Length + 1];
                skeleton[0] = new SkeletonBone { name = source.name, rotation = Quaternion.identity, scale = Vector3.one };
                var human = new HumanBone[data.names.Length];
                for (int i = 0; i < transforms.Length; i++)
                {
                    var bone = new GameObject(data.names[i]).transform;
                    bone.SetParent(i == 0 ? source.transform : transforms[data.parents[i]], false);
                    bone.localPosition = data.restPositions[i];
                    transforms[i] = bone;
                    skeleton[i + 1] = new SkeletonBone { name = bone.name, position = bone.localPosition, rotation = Quaternion.identity, scale = Vector3.one };
                    human[i] = new HumanBone { boneName = bone.name,
                        humanName = HumanTrait.BoneName[(int)Enum.Parse(typeof(HumanBodyBones), bone.name)],
                        limit = new HumanLimit { useDefaultValues = true } };
                }
                avatar = AvatarBuilder.BuildHumanAvatar(source, new HumanDescription { human = human, skeleton = skeleton,
                    upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
                    armStretch = .05f, legStretch = .05f, feetSpacing = 0, hasTranslationDoF = false });
                avatar.hideFlags = HideFlags.HideAndDontSave;
                if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Could not characterize the generated skeleton as Humanoid.");
                using (var reader = new HumanPoseHandler(avatar, source.transform))
                using (var writer = new HumanPoseHandler(rig.Animator.avatar, rig.Root.transform))
                {
                    result = ScriptableObject.CreateInstance<AnimationTake>();
                    result.character = character; result.body = body;
                    result.clothing = (GameObject[])(clothing ?? Array.Empty<GameObject>()).Clone();
                    result.outfitOptions = (OutfitOptions[])(outfitOptions ?? Array.Empty<OutfitOptions>()).Clone();
                    result.frameRate = data.fps; result.lastFrame = data.frames.Length - 1;
                    result.interpolation = PoseInterpolation.Linear; result.loop = false;
                    result.bonePaths = (string[])rig.Paths.Clone();
                    var humanPose = new HumanPose();
                    for (int f = 0; f < data.frames.Length; f++)
                    {
                        if (showProgress && f % 30 == 0 && EditorUtility.DisplayCancelableProgressBar("Import generated motion", "Retargeting frame " + f, (float)f / data.frames.Length))
                            throw new OperationCanceledException("Motion import cancelled.");
                        for (int j = 0; j < transforms.Length; j++)
                        {
                            transforms[j].localPosition = data.restPositions[j];
                            transforms[j].localRotation = data.frames[f].rotations[j].normalized;
                        }
                        transforms[0].localPosition += data.frames[f].translation;
                        reader.GetHumanPose(ref humanPose);
                        rig.Apply(rig.Rest);
                        writer.SetHumanPose(ref humanPose);
                        var key = rig.Capture(); key.frame = f; result.keys.Add(key);
                    }
                }
                return result;
            }
            catch { if (result) UnityEngine.Object.DestroyImmediate(result); throw; }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
                rig.Apply(savedPose);
                if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }
}
