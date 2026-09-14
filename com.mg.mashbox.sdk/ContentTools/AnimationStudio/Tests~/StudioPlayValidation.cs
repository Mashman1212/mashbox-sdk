using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [InitializeOnLoad]
    public static class StudioPlayValidation
    {
        private const string Key = "MashBox.Studio.PlayValidation";
        static StudioPlayValidation() { EditorApplication.playModeStateChanged += Changed; }
        public static void Run() { SessionState.SetInt(Key, 1); EditorApplication.isPlaying = true; }
        private static void Changed(PlayModeStateChange state)
        {
            if (SessionState.GetInt(Key, 0) == 0) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                    var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/SM_Body_01.fbx");
                    using (var rig = new AuthoringRig(source, body))
                    {
                        if (!rig.HasIK) throw new Exception(rig.SolverStatus);
                        var take = ScriptableObject.CreateInstance<AnimationTake>();
                        take.character = source; take.body = body; take.bonePaths = rig.Paths; take.lastFrame = 2;
                        var pose = rig.Capture(); pose.effectors[0] = rig.MatchEffector(0, 1);
                        pose.effectors[0].position += Vector3.forward * 0.1f;
                        take.SetKey(pose, 0); rig.Apply(pose);
                        var clip = AnimationClipBaker.Bake(take, rig, true);
                        AssetDatabase.CreateAsset(take, "Assets/Play Mode Take.asset");
                        AssetDatabase.CreateAsset(clip, "Assets/Play Mode Clip.anim");
                        AssetDatabase.SaveAssets();
                    }
                    SessionState.SetInt(Key, 2);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex); File.WriteAllText("play-validation-result.txt", ex.ToString());
                    SessionState.SetInt(Key, 3);
                }
                EditorApplication.isPlaying = false;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                bool success = SessionState.GetInt(Key, 0) == 2 && AssetDatabase.LoadAssetAtPath<AnimationTake>("Assets/Play Mode Take.asset") && AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Play Mode Clip.anim");
                SessionState.SetInt(Key, 0);
                if (success) File.WriteAllText("play-validation-result.txt", "PASS: Preview scene, Final IK solving and Humanoid baking in Play Mode; take and clip survive exiting Play Mode.");
                EditorApplication.Exit(success ? 0 : 1);
            }
        }
    }
}
