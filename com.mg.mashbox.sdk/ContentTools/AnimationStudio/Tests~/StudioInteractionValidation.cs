using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioInteractionValidation
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);
        private static void Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Flags).Invoke(o, args);
        private static void Check(bool value, string description)
        { if (!value) throw new Exception(description); Debug.Log("PASS: " + description); }
        public static void RunEntryPoints()
        {
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var take = ScriptableObject.CreateInstance<AnimationTake>(); take.character = source;
                using (var rig = new AuthoringRig(source,null)) { take.bonePaths = rig.Paths; take.keys.Add(rig.Rest.Copy(0)); }
                string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Entry Take.asset"); AssetDatabase.CreateAsset(take,path);
                AnimationStudioWindow.OpenInStudio(take);
                var views = Resources.FindObjectsOfTypeAll<AnimationStudioViewport>();
                Check(views.Length == 1 && views[0].Studio,"Opening take creates one bound Studio viewport");
                var owner = views[0].Studio;
                Check(typeof(AnimationStudioWindow).GetField("take",Flags).GetValue(owner) == take,"Entry point loads the requested take");
                AnimationStudioWindow.OpenInStudio(take);
                Check(Resources.FindObjectsOfTypeAll<AnimationStudioViewport>().Length == 1,"Reopening reuses viewport");
                var inspector = UnityEditor.Editor.CreateEditor(take);
                Check(inspector is AnimationTakeEditor,"Take asset gets dedicated inspector"); UnityEngine.Object.DestroyImmediate(inspector);
                var script = MonoScript.FromScriptableObject(take);
                Check(((MonoImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(script))).GetIcon(),"Script importer has persistent custom icon");
                views[0].Close(); UnityEngine.Object.DestroyImmediate(owner);
                AnimationStudioWindow.OpenInStudio(null);
                views = Resources.FindObjectsOfTypeAll<AnimationStudioViewport>();
                Check(views.Length == 1 && typeof(AnimationStudioWindow).GetField("take",Flags).GetValue(views[0].Studio) == take,"Fresh controller restores last saved take");
                owner = views[0].Studio;
                var activeRig = (AuthoringRig)typeof(AnimationStudioWindow).GetField("rig",Flags).GetValue(owner);
                var clip = new AnimationClip { frameRate = 24 };
                int hips = Array.IndexOf(activeRig.Bones,activeRig.Animator.GetBoneTransform(HumanBodyBones.Hips));
                clip.SetCurve(activeRig.Paths[hips],typeof(Transform),"m_LocalPosition.x",AnimationCurve.Linear(0,0,1,.12f));
                AssetDatabase.CreateAsset(clip,AssetDatabase.GenerateUniqueAssetPath("Assets/Context Clip.anim"));
                Selection.activeObject = clip;
                Check((bool)typeof(AnimationStudioWindow).GetMethod("CanOpenSelectedClip",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null),"Clip context action is enabled for an animation asset");
                Set(owner,"importClip",clip);
                Call(owner,"ImportClipToPath",AssetDatabase.GenerateUniqueAssetPath("Assets/Context Clip Take.asset"));
                var imported = (AnimationTake)typeof(AnimationStudioWindow).GetField("take",Flags).GetValue(owner);
                Check(imported != take && imported.frameRate == 24 && imported.lastFrame == 24 && imported.keys.Count == 25,"Clip opens as new take using source timing");
                Check(Mathf.Abs(imported.keys[24].bones[hips].position.x-.12f)<.0001f,"Imported take contains sampled clip motion");
                views[0].Close(); UnityEngine.Object.DestroyImmediate(owner);
                File.WriteAllText("entry-result.txt","PASS: viewport entry, take loading, viewport reuse, custom inspector/icon and saved take restoration."); EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); File.WriteAllText("entry-result.txt",e.ToString()); EditorApplication.Exit(1); }
        }
        public static void RunUpdateTake()
        {
            AnimationStudioWindow window = null;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var rig = new AuthoringRig(source,null);
                window = ScriptableObject.CreateInstance<AnimationStudioWindow>();
                var take = ScriptableObject.CreateInstance<AnimationTake>();
                take.character = source; take.bonePaths = rig.Paths; take.lastFrame = 60;
                take.keys.Add(rig.Rest.Copy(0));
                string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Update Validation.asset");
                AssetDatabase.CreateAsset(take,path); AssetDatabase.SaveAssetIfDirty(take);
                string guid = AssetDatabase.AssetPathToGUID(path);
                Set(window,"rig",rig); Set(window,"take",take); Set(window,"pose",rig.Rest.Copy(0));
                var result = ScriptableObject.CreateInstance<AnimationTake>(); result.bonePaths = rig.Paths;
                result.frameRate = 60; result.lastFrame = 29;
                for (int i=0;i<30;i++) result.keys.Add(rig.Rest.Copy(i));
                Call(window,"StoreMotionResult",result,null,"Apply generated motion");
                Check(take.lastFrame == 29 && take.frameRate == 60 && take.keys.Count == 30,"Current timing and keys replaced");
                Check(take.character == source && AssetDatabase.AssetPathToGUID(path) == guid,"Character and asset identity preserved");
                Undo.PerformUndo();
                Check(take.lastFrame == 60 && take.frameRate == 30 && take.keys.Count == 1,"One Undo restores previous motion and timing");
                Undo.PerformRedo();
                Check(take.lastFrame == 29 && take.keys.Count == 30,"Redo restores generated result");
                UnityEngine.Object.DestroyImmediate(result);
                Check(take.keys[0].bones.Length == rig.Bones.Length,"Replacement does not depend on temporary take lifetime");
                Call(window,"FlushEdits");
                Check(!EditorUtility.IsDirty(take),"Existing asset persists through deferred save");
                UnityEngine.Object.DestroyImmediate(window); window = null;
                File.WriteAllText("update-take-result.txt","PASS: same asset, timing update, Undo/Redo, independent pose data and deferred saving."); EditorApplication.Exit(0);
            }
            catch(Exception e) { if(window) UnityEngine.Object.DestroyImmediate(window); Debug.LogException(e); File.WriteAllText("update-take-result.txt",e.ToString()); EditorApplication.Exit(1); }
        }
        public static void RunDiagram()
        {
            AnimationStudioWindow window = null;
            AnimationTake take = null;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var rig = new AuthoringRig(source, null);
                window = ScriptableObject.CreateInstance<AnimationStudioWindow>();
                take = ScriptableObject.CreateInstance<AnimationTake>(); take.bonePaths = rig.Paths;
                var pose = rig.Capture(); take.SetKey(pose,0);
                Set(window,"rig",rig); Set(window,"take",take); Set(window,"pose",pose); Set(window,"autoKey",false);
                var type = typeof(AnimationStudioWindow);
                Func<bool,int,float> alpha = (ik,limb) => (float)type.GetMethod("RigAlpha",Flags).Invoke(window,new object[]{ik,limb});
                Check(alpha(true,0) < .2f && alpha(false,0) == 1, "Zero IK fades targets and shows FK");
                Set(window,"hideInactiveControls",true);
                Check(alpha(true,0) == 0, "Zero IK can be hidden completely");
                Vector3 before = rig.Ends[0].position;
                Call(window,"SetLimbBlend",0,1f);
                pose = (PoseKey)type.GetField("pose",Flags).GetValue(window);
                Check(Vector3.Distance(before,rig.Ends[0].position) < .025f, "Enabling diagram IK matches current hand pose");
                Check(alpha(false,0) == 0 && alpha(true,0) == 1, "Full IK hides FK chain controls");
                Check(pose.effectors[1].weight == 0 && pose.effectors[2].weight == 0, "Limb blend does not affect other limbs");
                Check(take.keys.Count == 1 && take.keys[0].effectors[0].weight == 0, "Diagram blend respects Auto Key off");
                Set(window,"showIKControls",false); Check(alpha(true,0) == 0, "IK visibility overrides active blend");
                Set(window,"showFKControls",false); Check(alpha(false,-1) == 0, "FK visibility hides torso controls too");
                Call(window,"SelectBone",rig.Middles[0]);
                Check(!(bool)type.GetField("ikMode",Flags).GetValue(window), "Diagram bone selection remains available with gizmos hidden");
                Check((int)type.GetMethod("BoneLimb",Flags).Invoke(window,new object[]{rig.Middles[0]}) == 0, "Elbow maps to arm blend");
                Check((int)type.GetMethod("BoneLimb",Flags).Invoke(window,new object[]{rig.Animator.GetBoneTransform(HumanBodyBones.Hips)}) == -1, "Hips remain FK with no fake limb blend");
                UnityEngine.Object.DestroyImmediate(window); window = null;
                UnityEngine.Object.DestroyImmediate(take); take = null;
                File.WriteAllText("diagram-result.txt","PASS: visibility, blend fade/hide, limb isolation, matching, hidden selection and manual key preservation.");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                if (window) UnityEngine.Object.DestroyImmediate(window);
                if (take) UnityEngine.Object.DestroyImmediate(take);
                Debug.LogException(e); File.WriteAllText("diagram-result.txt",e.ToString()); EditorApplication.Exit(1);
            }
        }
        public static void Run()
        {
            AnimationStudioWindow window = null;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/SM_Body_01.fbx");
                var rig = new AuthoringRig(source, body);
                window = ScriptableObject.CreateInstance<AnimationStudioWindow>();
                Check(!(bool)typeof(AnimationStudioWindow).GetField("autoKey", Flags).GetValue(window), "Auto Key defaults off");
                Set(window, "autoKey", true);
                var take = ScriptableObject.CreateInstance<AnimationTake>();
                take.character = source; take.body = body; take.bonePaths = rig.Paths;
                var pose = rig.Capture(); take.SetKey(pose, 0);
                string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Interaction Take.asset");
                AssetDatabase.CreateAsset(take, path); AssetDatabase.SaveAssetIfDirty(take);
                string saved = File.ReadAllText(path);
                Set(window, "rig", rig); Set(window, "take", take); Set(window, "pose", pose); Set(window, "frame", 10);
                Set(window, "character", source); Set(window, "body", body);
                int beforeRenderers = rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>().Length;
                Set(window, "clothing", new[] { body });
                string beforeOutfit = EditorJsonUtility.ToJson(take);
                Call(window, "RebuildAppearance");
                rig = (AuthoringRig)typeof(AnimationStudioWindow).GetField("rig", Flags).GetValue(window);
                Check(rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>().Length > beforeRenderers, "Additional skinned mesh layers attach to preview");
                Check(EditorJsonUtility.ToJson(take) == beforeOutfit && rig.Bones.Length == pose.bones.Length,
                    "Outfit rebuild preserves take keys and skeleton mapping");
                Set(window, "clothing", Array.Empty<GameObject>());
                Call(window, "RebuildAppearance");
                rig = (AuthoringRig)typeof(AnimationStudioWindow).GetField("rig", Flags).GetValue(window);
                Check(rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>().Length == beforeRenderers, "Removing clothing restores base preview");
                pose.effectors[0] = rig.MatchEffector(0, 1);
                pose.effectors[2] = rig.MatchEffector(2, 1); pose.effectors[3] = rig.MatchEffector(3, 1);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 120; i++)
                {
                    pose.effectors[0].position.y += 0.0005f;
                    Call(window, "PreviewSceneEdit", "Drag test");
                }
                timer.Stop();
                Check(take.keys.Count == 1 && !EditorUtility.IsDirty(take), "120 drag updates do not mutate or dirty the take asset");
                Check(File.ReadAllText(path) == saved, "No disk write during drag");
                Call(window, "FinishSceneEdit");
                Check(take.keys.Count == 2 && take.keys[1].frame == 10, "Release commits exactly one pose key");
                Check(File.ReadAllText(path) == saved, "Release queues saving outside the GUI event");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(take.keys.Count == 1, "One Undo removes the complete drag");
                Undo.PerformRedo(); Check(take.keys.Count == 2, "Redo restores the complete drag");
                // Undo/redo seeks and replaces the window's working pose.
                pose = (PoseKey)typeof(AnimationStudioWindow).GetField("pose", Flags).GetValue(window);
                int arm = Array.IndexOf(rig.Bones, rig.Middles[0]);
                Vector3 handBefore = rig.Ends[0].position;
                Call(window, "PrepareFKEdit", arm); rig.Apply(pose);
                Check(pose.effectors[0].weight == 0 && pose.effectors[2].weight == 1 && pose.effectors[3].weight == 1,
                    "FK releases only the selected arm and retains foot pins");
                Check(Vector3.Distance(handBefore, rig.Ends[0].position) < 0.025f, "FK match preserves hand position");
                int hips = Array.IndexOf(rig.Bones, rig.Animator.GetBoneTransform(HumanBodyBones.Hips));
                Call(window, "PrepareFKEdit", hips);
                Check(pose.effectors[2].weight == 1 && pose.effectors[3].weight == 1, "Hip editing keeps feet pinned");
                Call(window, "SelectBone", rig.Middles[0]);
                Check(!(bool)typeof(AnimationStudioWindow).GetField("ikMode", Flags).GetValue(window) &&
                    (bool)typeof(AnimationStudioWindow).GetField("rotate", Flags).GetValue(window), "Bone selection enables FK rotation");
                Call(window, "FlushEdits");
                Check(File.ReadAllText(path) != saved && !EditorUtility.IsDirty(take), "Deferred flush persists take");
                Set(window, "autoKey", false);
                string manualBaseline = EditorJsonUtility.ToJson(take);
                pose.bones[hips].position.y -= 0.01f;
                Call(window, "PreviewSceneEdit", "Manual drag");
                Call(window, "FinishSceneEdit");
                Check(EditorJsonUtility.ToJson(take) == manualBaseline, "Manual drag does not replace an existing key");
                Set(window, "frame", 11);
                Call(window, "Commit", "Numeric edit", false);
                Call(window, "FlushEdits");
                Check(EditorJsonUtility.ToJson(take) == manualBaseline, "Manual edit and Save now do not insert a key");
                Call(window, "Commit", "Key pose", true);
                Check(take.keys.Count == 3 && take.keys[2].frame == 11, "Explicit key works with Auto Key off");
                float keyedHeight = take.keys[2].bones[hips].position.y;
                pose.bones[hips].position.y -= 0.01f;
                Call(window, "Commit", "Manual edit", false);
                Call(window, "Seek", 11);
                pose = (PoseKey)typeof(AnimationStudioWindow).GetField("pose", Flags).GetValue(window);
                Check(Mathf.Approximately(pose.bones[hips].position.y, keyedHeight), "Scrubbing restores keyed pose without saving manual changes");
                Set(window, "autoKey", true);
                Set(window, "frame", 12);
                pose.bones[hips].position.y -= 0.02f;
                Call(window, "PreviewSceneEdit", "Close during edit");
                Call(window, "ReleaseRig");
                Check(take.keys.Count == 4 && !EditorUtility.IsDirty(take), "Closing commits and saves an unfinished auto-key gesture");
                File.WriteAllText("interaction-validation-result.txt", "PASS: drag transaction, deferred persistence, single Undo/Redo, FK matching, bone selection, hip pins. 120 solve/preview updates: " + timer.ElapsedMilliseconds + "ms (batch mode, not viewport frame timing).");
                UnityEngine.Object.DestroyImmediate(window); window = null;
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex); File.WriteAllText("interaction-validation-result.txt", ex.ToString());
                if (window) UnityEngine.Object.DestroyImmediate(window);
                EditorApplication.Exit(1);
            }
        }
    }
}
