using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

public static class StudioPedalDirectionValidation
{
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/BmxAnimatorValidation/Assets")) throw new InvalidOperationException("Use isolated fixture.");
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Bike Skeleton.fbx");
            using (var rig = new AuthoringRig(source, null))
            {
                var window = ScriptableObject.CreateInstance<AnimationStudioWindow>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                typeof(AnimationStudioWindow).GetField("rig", flags).SetValue(window, rig);
                var set = typeof(AnimationStudioWindow).GetMethod("SetBikeAngle", flags);
                var left = rig.Bones.Single(b => b.name.Split(':').Last() == "LeftPedal_Joint");
                var right = rig.Bones.Single(b => b.name.Split(':').Last() == "RightPedal_Joint");
                var leftStart = left.localRotation; var rightStart = right.localRotation;
                foreach (float angle in new[] { 35f, 120f, 190f, -30f, 0f })
                {
                    set.Invoke(window, new object[] { 3, angle });
                    if (Quaternion.Angle(left.localRotation, Quaternion.AngleAxis(angle, Vector3.right) * leftStart) > .01f ||
                        Quaternion.Angle(right.localRotation, Quaternion.AngleAxis(-angle, Vector3.right) * rightStart) > .01f)
                        throw new Exception("Pedal compensation direction failed at " + angle);
                }
                typeof(AnimationStudioWindow).GetField("rig", flags).SetValue(window, null);
                UnityEngine.Object.DestroyImmediate(window);
            }
            File.WriteAllText("pedal-direction-result.txt", "PASS: left compensation reversed, right unchanged; incremental turns, wraparound, reverse and return to rest.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("pedal-direction-result.txt", e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
