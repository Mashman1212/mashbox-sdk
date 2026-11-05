using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

public static class StudioVisibilityValidation
{
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/BmxAnimatorValidation/Assets")) throw new InvalidOperationException("Use isolated fixture.");
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Bike Skeleton.fbx");
            var take = ScriptableObject.CreateInstance<AnimationTake>(); take.character = source; take.body = source;
            using (var rig = new AuthoringRig(source, null))
            {
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube); mesh.transform.SetParent(rig.Root.transform, false);
                var renderer = mesh.GetComponent<Renderer>(); renderer.enabled = false;
                var root = rig.Root; var bones = rig.Bones;
                var window = ScriptableObject.CreateInstance<AnimationStudioWindow>();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                typeof(AnimationStudioWindow).GetField("take", flags).SetValue(window, take);
                typeof(AnimationStudioWindow).GetField("rig", flags).SetValue(window, rig);
                var toggle = typeof(AnimationStudioWindow).GetMethod("ToggleCharacterRow", flags);
                for (int i = 0; i < 10; i++)
                {
                    toggle.Invoke(window, new object[] { true });
                    if (!renderer.forceRenderingOff || !root.activeSelf) throw new Exception("Hide must affect renderers only.");
                    toggle.Invoke(window, new object[] { false });
                    if (renderer.forceRenderingOff || renderer.enabled || rig.Root != root || rig.Bones != bones || take.body != source || take.character != source)
                        throw new Exception("Visibility must preserve rig, outfit and original renderer enabled state.");
                }
                typeof(AnimationStudioWindow).GetField("rig", flags).SetValue(window, null);
                UnityEngine.Object.DestroyImmediate(window);
            }
            UnityEngine.Object.DestroyImmediate(take);
            File.WriteAllText("visibility-result.txt", "PASS: repeated hide/show retains rig, bones, meshes, outfit references and renderer enabled state; no rebuild.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText("visibility-result.txt", e.ToString()); EditorApplication.Exit(1); }
    }
}
