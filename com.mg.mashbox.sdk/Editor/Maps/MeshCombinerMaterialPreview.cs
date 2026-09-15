using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only material inventory shared by the runtime and offline inspectors.</summary>
internal sealed class MeshCombinerMaterialPreview
{
    private sealed class Entry
    {
        public Material Material;
        public int Submeshes;
        public int ReadableSubmeshes;
        public readonly HashSet<GameObject> Sources = new HashSet<GameObject>();
    }

    private readonly List<Entry> entries = new List<Entry>();
    private int rendererCount;
    private int submeshCount;
    private int unreadableCount;
    private double nextRefresh;
    private bool expanded = true;
    private Vector2 scroll;
    private string search = "";

    public void Draw(Func<List<MeshFilter>> getSources, bool runtime, bool forceRefresh)
    {
        if (forceRefresh || EditorApplication.timeSinceStartup >= nextRefresh)
        {
            Refresh(getSources());
            nextRefresh = EditorApplication.timeSinceStartup + 1.0;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Material Preview", EditorStyles.boldLabel);
        int uniqueMaterials = 0;
        int readableGroups = 0;
        int missingSlots = 0;
        foreach (Entry entry in entries)
        {
            if (entry.Material != null)
                uniqueMaterials++;
            else
                missingSlots += entry.Submeshes;
            if (entry.ReadableSubmeshes > 0)
                readableGroups++;
        }

        EditorGUILayout.LabelField("Unique Materials", uniqueMaterials.ToString("N0"));
        EditorGUILayout.LabelField("Source Renderers / Submeshes", $"{rendererCount:N0} / {submeshCount:N0}");
        EditorGUILayout.LabelField("Combined Material Groups", entries.Count.ToString("N0"));
        if (runtime && unreadableCount > 0)
            EditorGUILayout.LabelField("Groups Currently Readable", readableGroups.ToString("N0"));

        EditorGUILayout.HelpBox(
            "Each material group becomes a separate combined submesh. Different material assets remain separate even when they look identical. " +
            "This is a material inventory, not a measured draw-call or GPU-instancing count.", MessageType.Info);

        if (unreadableCount > 0)
            EditorGUILayout.HelpBox(
                $"{unreadableCount:N0} source mesh(es) have Read/Write disabled. Their materials are included in this inventory. " +
                (runtime
                    ? "Runtime combining skips these meshes unless Read/Write is enabled before combining."
                    : "Offline baking temporarily enables Read/Write on imported models; meshes that remain unreadable are skipped."),
                MessageType.Warning);
        if (missingSlots > 0)
            EditorGUILayout.HelpBox($"{missingSlots:N0} source submesh(es) have no material. These form one missing-material group.", MessageType.Warning);

        if (GUILayout.Button("Refresh Material Preview"))
        {
            Refresh(getSources());
            nextRefresh = EditorApplication.timeSinceStartup + 1.0;
        }

        expanded = EditorGUILayout.Foldout(expanded, $"Materials ({uniqueMaterials:N0})", true);
        if (!expanded)
            return;
        if (entries.Count == 0)
        {
            EditorGUILayout.HelpBox("No source submeshes match the current inclusion settings.", MessageType.Info);
            return;
        }

        search = EditorGUILayout.TextField("Filter Materials", search);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(360f));
        foreach (Entry entry in entries)
        {
            string materialName = entry.Material != null ? entry.Material.name : "Missing material";
            if (!string.IsNullOrEmpty(search) &&
                materialName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            // Ignore the return value: this field is for inspecting/pinging the asset only.
            EditorGUILayout.ObjectField(entry.Material, typeof(Material), false);
            EditorGUILayout.LabelField(
                $"{entry.Sources.Count:N0} renderers / {entry.Submeshes:N0} submeshes",
                EditorStyles.miniLabel);
            if (entry.Material != null)
            {
                EditorGUILayout.LabelField("Material GPU Instancing",
                    entry.Material.enableInstancing ? "Enabled" : "Disabled", EditorStyles.miniLabel);
            }
            if (GUILayout.Button("Select Source Objects", EditorStyles.miniButton))
            {
                List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
                foreach (GameObject source in entry.Sources)
                    if (source != null)
                        objects.Add(source);
                Selection.objects = objects.ToArray();
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();
    }

    private void Refresh(List<MeshFilter> filters)
    {
        entries.Clear();
        rendererCount = 0;
        submeshCount = 0;
        unreadableCount = 0;
        Dictionary<Material, Entry> byMaterial = new Dictionary<Material, Entry>();
        Entry missing = null;
        foreach (MeshFilter filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            Mesh mesh = filter.sharedMesh;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || mesh.subMeshCount == 0)
                continue;

            rendererCount++;
            if (!mesh.isReadable)
                unreadableCount++;
            Material[] materials = renderer.sharedMaterials;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                Material material = submesh < materials.Length ? materials[submesh] : null;
                Entry entry;
                if (material == null)
                {
                    if (missing == null)
                    {
                        missing = new Entry();
                        entries.Add(missing);
                    }
                    entry = missing;
                }
                else if (!byMaterial.TryGetValue(material, out entry))
                {
                    entry = new Entry { Material = material };
                    byMaterial.Add(material, entry);
                    entries.Add(entry);
                }
                entry.Submeshes++;
                if (mesh.isReadable)
                    entry.ReadableSubmeshes++;
                entry.Sources.Add(renderer.gameObject);
                submeshCount++;
            }
        }
        entries.Sort((left, right) =>
        {
            string leftName = left.Material != null ? left.Material.name : "";
            string rightName = right.Material != null ? right.Material.name : "";
            int comparison = string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            return comparison != 0 ? comparison :
                string.Compare(AssetDatabase.GetAssetPath(left.Material),
                    AssetDatabase.GetAssetPath(right.Material), StringComparison.Ordinal);
        });
    }
}
