using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using MashBoxSDK.Map.Rendering.Instancer;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(InstanceGroup))]
[CanEditMultipleObjects]
public class InstanceGroupStatusEditor : Editor
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    double nextRefresh;
    string status;

    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Instancing starts in Play Mode. With Remove Runtime Children enabled, supported static visual children are cached and deleted from the runtime hierarchy after batching. Your painted Edit Mode hierarchy remains intact. Children with scripts, colliders or unsupported rendering features are retained.", MessageType.Info);
            return;
        }
        if (status == null || EditorApplication.timeSinceStartup >= nextRefresh)
        {
            var report = new StringBuilder();
            foreach (var item in targets) report.AppendLine(Describe((InstanceGroup)item));
            status = report.ToString();
            nextRefresh = EditorApplication.timeSinceStartup + 0.5;
        }
        EditorGUILayout.HelpBox(status, MessageType.Info);
        if (GUILayout.Button("Rebuild Instances"))
        {
            foreach (var item in targets) ((InstanceGroup)item).RefreshInstances();
            nextRefresh = 0;
        }
    }

    static IList Field(InstancingManager manager, string name) => manager == null ? null :
        (IList)typeof(InstancingManager).GetField(name, Private).GetValue(manager);

    public static string Describe(InstanceGroup group)
    {
        var manager = InstancingManager.ExistingInstance;
        var managed = new HashSet<MeshRenderer>();
        var suppressed = Field(manager, "suppressed");
        if (suppressed != null) foreach (MeshRenderer renderer in suppressed) managed.Add(renderer);
        bool registered = Field(manager, "groups")?.Contains(group) == true;
        int batched = 0, original = 0, inactive = 0, externallyHidden = 0;
        var meshes = new HashSet<Mesh>();
        var materials = new HashSet<Material>();
        foreach (var cached in group.CachedRenderers)
        {
            meshes.Add(cached.mesh);
            foreach (var material in cached.materials) materials.Add(material);
        }
        var reasons = new Dictionary<string, int>();
        foreach (var renderer in group.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) { inactive++; continue; }
            if (managed.Contains(renderer) && renderer.forceRenderingOff)
            {
                batched++;
                meshes.Add(renderer.GetComponent<MeshFilter>().sharedMesh);
                foreach (var material in renderer.sharedMaterials) materials.Add(material);
            }
            else if (renderer.forceRenderingOff) externallyHidden++;
            else
            {
                original++;
                string reason = Reason(renderer);
                reasons.TryGetValue(reason, out int count); reasons[reason] = count + 1;
            }
        }
        var report = new StringBuilder();
        report.AppendLine($"{group.name}: {(registered ? "registered" : "NOT registered")}; manager {(manager != null && manager.isActiveAndEnabled ? "running" : "NOT running")}");
        report.AppendLine($"Cached instances (children removed): {group.CachedRenderers.Count}");
        report.AppendLine($"Retained renderers instanced: {batched}; {meshes.Count} meshes, {materials.Count} materials");
        report.AppendLine($"Original renderers drawing: {original}");
        report.AppendLine($"Inactive/disabled: {inactive}; suppressed outside this manager: {externallyHidden}");
        report.AppendLine($"Manager batches across all groups: {Field(manager, "batches")?.Count ?? 0}");
        foreach (var pair in reasons) report.AppendLine($"Original path: {pair.Value} — {pair.Key}");
        return report.ToString();
    }

    static string Reason(MeshRenderer renderer)
    {
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return "missing mesh";
        var lod = renderer.GetComponentInParent<LODGroup>();
        if (lod != null && lod.lodCount > 1) return "multiple LODs";
        if (renderer.HasPropertyBlock()) return "material property override";
        if (renderer.lightmapIndex >= 0 || renderer.realtimeLightmapIndex >= 0) return "lightmapped";
        if (renderer.renderingLayerMask != 1) return "custom rendering layer";
        if (renderer.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off &&
            renderer.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.BlendProbes) return "unsupported probe mode";
        if (renderer.sharedMaterials.Length != filter.sharedMesh.subMeshCount) return "material/submesh count mismatch";
        foreach (var material in renderer.sharedMaterials)
            if (material == null || material.shader == null || !material.shader.isSupported) return "missing/unsupported material";
        return "eligible; waiting for active group/manager rebuild";
    }

    [MenuItem("MashBox/Maps/Rendering/Audit Live Instance Groups")]
    public static void Audit()
    {
        var report = new StringBuilder($"Play Mode: {Application.isPlaying}\n");
        foreach (var group in Object.FindObjectsByType<InstanceGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            report.AppendLine(group.gameObject.scene.name + "/" + (group.transform.parent != null ? group.transform.parent.name : "") + "/" + group.name);
            report.AppendLine(Describe(group));
        }
        var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "InstanceGroups.txt"), report.ToString());
        Debug.Log(report.ToString());
    }
}
