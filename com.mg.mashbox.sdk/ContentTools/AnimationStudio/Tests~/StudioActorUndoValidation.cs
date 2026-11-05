using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

// Run in a disposable host with the production actor refresh/EnsureActors/SampleActors methods.
public static class StudioActorUndoValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object o, string name) => o.GetType().GetField(name, Flags).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);
    static void Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Flags).Invoke(o, args);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/BmxAnimatorValidation/Assets"))
            throw new InvalidOperationException("Use the disposable BmxAnimatorValidation project.");
        AnimationStudioWindow window = null;
        Undo.UndoRedoCallback refresh = null;
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Bike Skeleton.fbx");
            var primary = ScriptableObject.CreateInstance<AnimationTake>(); primary.character = source;
            var bike = ScriptableObject.CreateInstance<AnimationTake>(); bike.character = source;
            primary.actors.Add(bike);
            using (var rig = new AuthoringRig(source, null))
            {
                window = ScriptableObject.CreateInstance<AnimationStudioWindow>();
                Set(window, "take", primary); Set(window, "rig", rig);
                Call(window, "EnsureActors");
                var preview = ((IList)Get(window, "actorPreviews"))[0];
                var bikeRig = (AuthoringRig)Get(preview, "Rig");
                var root = bikeRig.Root;
                var visuals = root.GetComponentsInChildren<Transform>().Where(t => t.name.EndsWith(" · Preview")).ToArray();
                Check(visuals.Length > 30, "Full BMX preview assembled");
                Set(preview, "Control", 1);
                bike.bonePaths = bikeRig.Paths; bike.SetKey(bikeRig.Rest.Copy(0), 0);
                int bars = Array.FindIndex(bikeRig.Bones, b => b.name.Split(':').Last() == "Bars_Joint");
                var before = bike.keys[0].bones[bars].rotation;
                refresh = () => { Call(window, "RefreshActorPreviewsAfterUndo"); Call(window, "SampleActors", 0f); };
                Undo.undoRedoPerformed += refresh;
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(bike, "Rotate bars test");
                var rotated = bike.keys[0].Copy(0); rotated.bones[bars].rotation *= Quaternion.Euler(0, 35, 0);
                bike.SetKey(rotated, 0); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                Call(window, "SampleActors", 0f);
                var watch = System.Diagnostics.Stopwatch.StartNew(); Undo.PerformUndo(); watch.Stop();
                Check(Quaternion.Angle(bikeRig.Bones[bars].localRotation, before) < .001f, "Undo restores rotation");
                Check(Get(((IList)Get(window, "actorPreviews"))[0], "Rig") == bikeRig && root, "Undo retains rig identity");
                Check(visuals.All(t => t) && (int)Get(preview, "Control") == 1, "Undo retains every mesh and selected control");
                Undo.PerformRedo();
                Check(Quaternion.Angle(bikeRig.Bones[bars].localRotation, rotated.bones[bars].rotation) < .001f, "Redo restores rotation");
                Check(visuals.All(t => t), "Redo retains every mesh");

                // A real loadout change still rebuilds visuals; Undo restores the default assembly.
                var changedOutfit = AuthoringRig.BuildFullBmxOutfit(); changedOutfit.RemoveAt(changedOutfit.Count - 1);
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(bike, "Change outfit test");
                bike.vehicleParts = changedOutfit;
                EditorUtility.SetDirty(bike);
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); refresh();
                Check(!visuals[0] && root, "Outfit change rebuilds visuals without replacing the rig");
                Undo.PerformUndo();
                Check(root.GetComponentsInChildren<Transform>().Count(t => t.name.EndsWith(" · Preview")) == visuals.Length, "Outfit Undo restores complete default BMX");
                Undo.PerformRedo();
                Check(root.GetComponentsInChildren<Transform>().Count(t => t.name.EndsWith(" · Preview")) == visuals.Length - 1,
                    "Outfit Redo restores changed assembly: parts=" + bike.vehicleParts.Count + ", visuals=" + root.GetComponentsInChildren<Transform>().Count(t => t.name.EndsWith(" · Preview")) + ", expected=" + (visuals.Length - 1));
                primary.actors.Clear(); refresh();
                Check(((IList)Get(window, "actorPreviews")).Count == 0 && !root, "Removed actor is disposed");
                File.WriteAllText("actor-undo-result.txt", "PASS: pose Undo/Redo preserves rig, meshes and selection; outfit Undo/Redo and actor removal refresh correctly. Pose Undo: " + watch.ElapsedMilliseconds + " ms (disposable host).");
                Set(window, "rig", null);
            }
            Undo.undoRedoPerformed -= refresh; refresh = null;
            UnityEngine.Object.DestroyImmediate(window);
            UnityEngine.Object.DestroyImmediate(primary); UnityEngine.Object.DestroyImmediate(bike);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            if (refresh != null) Undo.undoRedoPerformed -= refresh;
            Debug.LogException(e); File.WriteAllText("actor-undo-result.txt", e.ToString()); EditorApplication.Exit(1);
        }
    }
}
