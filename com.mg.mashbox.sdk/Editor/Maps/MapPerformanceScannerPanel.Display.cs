using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public partial class MapPerformanceScannerPanel
{
    internal const int ReportPageSize = 25;
    [NonSerialized] private bool displayCacheValid;
    [NonSerialized] private long cachedTerrainTextureBytes;
    [NonSerialized] private long cachedMgTerrainMeshBytes;
    [NonSerialized] private int cachedShaderVariantCount;
    [NonSerialized] private List<MeshInfo> cachedLargestMeshes;
    [NonSerialized] private List<TerrainInfo> cachedSortedTerrains;
    [SerializeField] private int texturePage;
    [SerializeField] private int terrainPage;
    [SerializeField] private int rendererIssuePage;
    [SerializeField] private int mgTerrainPage;
    [SerializeField] private bool showMgTerrainDetails;

    private void EnsureDisplayCache()
    {
        if (displayCacheValid) return;
        var mgMeshes = new HashSet<Mesh>(mgTerrainMeshes);
        cachedMgTerrainMeshBytes = meshInfos.Where(info => mgMeshes.Contains(info.mesh)).Sum(info => info.memoryBytes);
        cachedTerrainTextureBytes = textureInfos.Where(info => info.usedByTerrain).Sum(info => info.memoryBytes);
        cachedShaderVariantCount = materialInfos.Where(info => !info.isDecal)
            .Select(info => (info.shader, keywords: string.Join(";", info.keywords))).Distinct().Count();
        cachedLargestMeshes = meshInfos.OrderByDescending(info => info.memoryBytes).Take(20).ToList();
        cachedSortedTerrains = terrainInfos.OrderByDescending(info => info.terrainDataMemory + info.textureMemory).ToList();
        displayCacheValid = true;
    }

    internal static int ClampReportPage(int page, int count) =>
        Mathf.Clamp(page, 0, Math.Max(0, (count - 1) / ReportPageSize));

    private static IEnumerable<T> DrawReportPage<T>(IReadOnlyList<T> items, ref int page)
    {
        page = ClampReportPage(page, items.Count);
        if (items.Count > ReportPageSize)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(page == 0))
                    if (GUILayout.Button("Previous", GUILayout.Width(80f))) { page--; GUIUtility.ExitGUI(); }
                EditorGUILayout.LabelField($"Page {page + 1:N0} of {(items.Count + ReportPageSize - 1) / ReportPageSize:N0} ({items.Count:N0} total)");
                using (new EditorGUI.DisabledScope((page + 1) * ReportPageSize >= items.Count))
                    if (GUILayout.Button("Next", GUILayout.Width(80f))) { page++; GUIUtility.ExitGUI(); }
            }
        }
        // Navigation exits the event so the next Layout builds the new page consistently.
        return items.Skip(page * ReportPageSize).Take(ReportPageSize);
    }
}
