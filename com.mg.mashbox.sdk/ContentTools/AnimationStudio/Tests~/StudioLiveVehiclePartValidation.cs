using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;

// Run only in a disposable LiveVehiclePartsValidation project.
public static class StudioLiveVehiclePartValidation
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/LiveVehiclePartsValidation/Assets", StringComparison.Ordinal))
            throw new InvalidOperationException("Use the disposable LiveVehiclePartsValidation project.");
        var report = new List<string>(); int code = 0;
        var doc = ScriptableObject.CreateInstance<StudioModelDocument>();
        Transform[] transforms = null;
        try
        {
            var frame = new StudioModelDocument.Node { name = "Frame", vehiclePart = new StudioModelDocument.VehiclePartData() };
            var forkSocket = new StudioModelDocument.Node { name = "Forks_Anchor", anchorType = "Forks_Anchor", parent = 0, position = new Vector3(1, 2, 3), rotation = Quaternion.Euler(10, 20, 30) };
            var forks = new StudioModelDocument.Node { name = "Forks", position = Vector3.left, vehiclePart = new StudioModelDocument.VehiclePartData { part = "Forks", targetAnchor = "Anchor_Forks", live = true } };
            var stemSocket = new StudioModelDocument.Node { name = "Stem_Anchor", anchorType = "Stem_Anchor", parent = 2, position = Vector3.up };
            var stem = new StudioModelDocument.Node { name = "Stem", vehiclePart = new StudioModelDocument.VehiclePartData { part = "Stem", targetAnchor = "Stem_Anchor", live = true }, scale = Vector3.one * 2 };
            doc.nodes.AddRange(new[] { frame, forkSocket, forks, stemSocket, stem });
            transforms = doc.nodes.Select(n => new GameObject(n.name).transform).ToArray();
            for (int i = 0; i < transforms.Length; i++)
            {
                if (doc.nodes[i].parent >= 0) transforms[i].SetParent(transforms[doc.nodes[i].parent], false);
                transforms[i].localPosition = doc.nodes[i].position; transforms[i].localRotation = doc.nodes[i].rotation; transforms[i].localScale = doc.nodes[i].scale;
            }
            doc.SnapLiveVehicleParts(transforms);
            Require(Vector3.Distance(transforms[2].position, transforms[1].position) < 1e-5f && Quaternion.Angle(transforms[2].rotation, transforms[1].rotation) < .01f, "Root position and rotation match the socket");
            Require(Vector3.Distance(transforms[4].position, transforms[3].position) < 1e-5f && stem.scale == Vector3.one * 2, "Chains resolve and preserve authored scale");
            report.Add("PASS: normalized IDs, position/rotation, dependency chain, scale preservation");
            transforms[0].position += Vector3.right * 4;
            doc.SnapLiveVehicleParts(transforms);
            Require(Vector3.Distance(transforms[4].position, transforms[3].position) < 1e-5f, "Live chain follows moved frame");
            forks.vehiclePart.live = false; var stopped = transforms[2].position;
            transforms[0].position += Vector3.right;
            doc.SnapLiveVehicleParts(transforms);
            Require(transforms[2].position == stopped, "Disabled Live leaves pose unchanged");
            report.Add("PASS: follows edits; Live off holds pose");
            forks.vehiclePart.live = true;
            forks.vehiclePart.targetAnchor = "Missing_Anchor";
            Require(doc.SnapLiveVehicleParts(transforms)[2].StartsWith("Waiting"), "Missing socket status");
            forks.vehiclePart.targetAnchor = "Stem_Anchor";
            Require(doc.SnapLiveVehicleParts(transforms)[2].StartsWith("Waiting"), "Own descendant is excluded");
            forks.vehiclePart.targetAnchor = "Forks_Anchor";
            stemSocket.anchorType = "Forks_Anchor";
            stem.vehiclePart.targetAnchor = "Forks_Anchor";
            Require(doc.SnapLiveVehicleParts(transforms)[4].StartsWith("Multiple"), "Ambiguous socket status");
            stemSocket.anchorType = "Stem_Anchor";
            stem.vehiclePart.targetAnchor = "Stem_Anchor";
            frame.vehiclePart.live = true; frame.vehiclePart.targetAnchor = "Stem_Anchor";
            var before = transforms.Select(t => t.position).ToArray();
            var cycle = doc.SnapLiveVehicleParts(transforms);
            Require(cycle[0].Contains("cycle") && cycle[2].Contains("cycle") && cycle[4].Contains("cycle"), "Cycle and dependents paused");
            Require(transforms.Select((t, i) => t.position == before[i]).All(v => v), "Cycle does not move any roots");
            report.Add("PASS: missing anchors, own descendants, ambiguous anchors, cycles");
            frame.vehiclePart.live = false;
            frame.vehiclePart.vehicle = "MTB";
            Require(doc.SnapLiveVehicleParts(transforms)[2].StartsWith("Waiting"), "Different vehicle categories do not attach");
            frame.vehiclePart.vehicle = "BMX";
            doc.nodes.Add(new StudioModelDocument.Node { name = "Bike assembly" });
            doc.nodes.Add(new StudioModelDocument.Node { name = "Other bike socket", anchorType = "Forks_Anchor" });
            transforms = transforms.Concat(new[] { new GameObject("Bike assembly").transform, new GameObject("Other bike socket").transform }).ToArray();
            foreach (int index in new[] { 0, 2, 4 }) { doc.nodes[index].parent = 5; transforms[index].SetParent(transforms[5], true); }
            transforms[5].position = Vector3.forward * 2; transforms[5].rotation = Quaternion.Euler(0, 35, 0); transforms[5].localScale = Vector3.one * 1.5f;
            Require(doc.SnapLiveVehicleParts(transforms)[2].StartsWith("Attached"), "Grouped vehicle ignores other assemblies");
            Require(Vector3.Distance(transforms[2].position, transforms[1].position) < 1e-5f, "World pose under transformed parent");
            transforms[5].localScale = Vector3.zero;
            Require(doc.SnapLiveVehicleParts(transforms)[2].Contains("zero scale"), "Singular parent is rejected");
            transforms[5].localScale = Vector3.one;
            transforms[1].gameObject.SetActive(false);
            Require(doc.SnapLiveVehicleParts(transforms)[2].StartsWith("Waiting"), "Inactive anchor ignored");
            report.Add("PASS: assembly isolation, transformed parents, zero scale, inactive anchors");
            var copy = forks.vehiclePart.Copy(); copy.live = false;
            Require(forks.vehiclePart.live, "Metadata copy is independent");
            var restored = ScriptableObject.CreateInstance<StudioModelDocument>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(doc), restored);
            Require(restored.nodes[2].vehiclePart.live && restored.nodes[1].vehiclePart == null, "Unity metadata roundtrip preserves marked and unmarked nodes");
            UnityEngine.Object.DestroyImmediate(restored);
            report.Add("PASS: vehicle filtering, independent copies, Unity serialization");
        }
        catch (Exception ex) { code = 1; report.Add("FAIL: " + ex); }
        finally
        {
            if (transforms != null) foreach (var transform in transforms) if (transform) UnityEngine.Object.DestroyImmediate(transform.gameObject);
            UnityEngine.Object.DestroyImmediate(doc);
        }
        File.WriteAllLines(Path.Combine(Application.dataPath, "../live-vehicle-result.txt"), report);
        EditorApplication.Exit(code);
    }
}
