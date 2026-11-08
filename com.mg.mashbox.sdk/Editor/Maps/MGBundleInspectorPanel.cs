using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal sealed class MGBundleInspectionJob
    {
        private static readonly List<MGBundleInspectionJob> Jobs = new();
        internal readonly CancellationTokenSource Cancellation = new();
        internal Task<MGBundleInventory> Task;
        internal string Progress = "Preparing inspection…";
        static MGBundleInspectionJob()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
        }
        internal static MGBundleInspectionJob Start(string path)
        {
            // Capture the editor path on the main thread; the worker only uses file APIs.
            var reader = new MGBundleArchiveReader(System.IO.Path.Combine(EditorApplication.applicationContentsPath, "Tools"));
            var job = new MGBundleInspectionJob();
            Jobs.RemoveAll(item => item.Task.IsCompleted);
            job.Task = System.Threading.Tasks.Task.Run(() => reader.Inspect(path, job.Cancellation.Token, message => Volatile.Write(ref job.Progress, message)));
            Jobs.Add(job);
            return job;
        }
        private static void StopAll()
        {
            foreach (var job in Jobs) job.Cancellation.Cancel();
            try { System.Threading.Tasks.Task.WaitAll(Jobs.Select(job => job.Task).ToArray()); }
            catch (AggregateException) { /* Canceled jobs have already released their archive handles. */ }
            Jobs.Clear();
        }
    }

    [Serializable]
    internal sealed class MGBundleInspectorPanel
    {
        internal const int PageSize = 50;
        [SerializeField] private string bundlePath = "";
        [SerializeField] private string search = "";
        [SerializeField] private MGBundleCategory category;
        [SerializeField] private int sort;
        [SerializeField] private int page;
        [SerializeField] private Vector2 scroll;
        [NonSerialized] private MGBundleInventory inventory;
        [NonSerialized] private MGBundleInspectionJob job;
        [NonSerialized] private string error;
        [NonSerialized] private MGBundleEntry selected;
        [NonSerialized] private List<MGBundleEntry> visible;
        [NonSerialized] private Dictionary<MGBundleCategory, (int count, long bytes)> totals;
        [NonSerialized] private Action repaint;
        private static readonly MGBundleCategory[] Categories = (MGBundleCategory[])Enum.GetValues(typeof(MGBundleCategory));
        private static readonly string[] SortNames = { "Largest first", "Smallest first", "Name A–Z", "Type A–Z" };

        internal void DrawGUI(Action repaintHost)
        {
            repaint = repaintHost;
            EditorGUILayout.LabelField("Bundle Inspector", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("See what was packed into a built bundle, grouped by asset type.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(bundlePath) ? "No bundle selected" : bundlePath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                using (new EditorGUI.DisabledScope(job != null))
                    if (GUILayout.Button("Browse…", GUILayout.Width(85)))
                    {
                        string path = EditorUtility.OpenFilePanel("Inspect a built Unity AssetBundle", string.IsNullOrEmpty(bundlePath) ? "" : System.IO.Path.GetDirectoryName(bundlePath), "");
                        if (!string.IsNullOrEmpty(path)) BeginInspect(path);
                    }
                using (new EditorGUI.DisabledScope(job != null || !File.Exists(bundlePath)))
                    if (GUILayout.Button("Reload", GUILayout.Width(65))) BeginInspect(bundlePath);
            }

            var drop = GUILayoutUtility.GetRect(0, 64, GUILayout.ExpandWidth(true));
            GUI.Box(drop, job == null ? "Drop a built bundle here\n.bundle, .unity3d, or an extensionless Unity bundle" : Volatile.Read(ref job.Progress), EditorStyles.helpBox);
            var evt = Event.current;
            if (job == null && drop.Contains(evt.mousePosition) && (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform))
            {
                var paths = DragAndDrop.paths;
                bool valid = paths.Length == 1 && File.Exists(paths[0]);
                DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (evt.type == EventType.DragPerform && valid) { DragAndDrop.AcceptDrag(); BeginInspect(paths[0]); }
                evt.Use();
            }
            if (job != null && GUILayout.Button("Cancel inspection", GUILayout.Width(150))) job.Cancellation.Cancel();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (inventory == null) return;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label($"{inventory.Assets.Count:N0} objects   ·   {inventory.Files.Count:N0} archive files   ·   File size: {Bytes(inventory.DiskBytes)}   ·   Unpacked: {Bytes(inventory.ExpandedBytes)}", EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("Save CSV…", GUILayout.Width(90))) SaveCsv();
            }
            EditorGUILayout.HelpBox("Object sizes are serialized bytes, not GPU memory or each object's share of the compressed bundle. Large texture/audio payloads may live in resource streams listed under Archive Files. Scene bundles are inspected without opening their scenes.", MessageType.None);
            foreach (var warning in inventory.Warnings.Take(3)) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            if (inventory.Warnings.Count > 3) EditorGUILayout.LabelField($"{inventory.Warnings.Count - 3:N0} more warnings included in the CSV report.", EditorStyles.miniLabel);

            DrawCategories();
            using (new EditorGUILayout.HorizontalScope())
            {
                string nextSearch = EditorGUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.textField);
                if (nextSearch != search) { search = nextSearch; InvalidateFilter(); }
                if (GUILayout.Button("Clear", GUILayout.Width(50))) { search = ""; InvalidateFilter(); GUIUtility.keyboardControl = 0; }
                int nextSort = EditorGUILayout.Popup(sort, SortNames, GUILayout.Width(125));
                if (nextSort != sort) { sort = nextSort; InvalidateFilter(); }
            }
            visible ??= Filter(inventory, category, search, sort);
            int pages = Math.Max(1, (visible.Count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{Label(category)} · {visible.Count:N0} matches · Page {page + 1:N0} / {pages:N0}", EditorStyles.miniBoldLabel);
                using (new EditorGUI.DisabledScope(page == 0))
                    if (GUILayout.Button("Previous", GUILayout.Width(75))) { page--; GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(page + 1 >= pages))
                    if (GUILayout.Button("Next", GUILayout.Width(60))) { page++; GUIUtility.ExitGUI(); }
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Name", EditorStyles.miniBoldLabel, GUILayout.MinWidth(140));
                GUILayout.Label("Type", EditorStyles.miniBoldLabel, GUILayout.Width(140));
                GUILayout.Label(category == MGBundleCategory.ArchiveFiles ? "Unpacked" : "Serialized", EditorStyles.miniBoldLabel, GUILayout.Width(85));
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (visible.Count == 0) EditorGUILayout.HelpBox("No entries match this category and search.", MessageType.Info);
            for (int index = page * PageSize; index < Math.Min(visible.Count, (page + 1) * PageSize); index++)
            {
                var entry = visible[index];
                using (new EditorGUILayout.HorizontalScope(entry == selected ? EditorStyles.helpBox : GUIStyle.none))
                {
                    if (GUILayout.Button(new GUIContent(entry.Name, entry.File + "\n" + entry.Details), EditorStyles.linkLabel, GUILayout.MinWidth(140))) selected = entry;
                    GUILayout.Label(entry.Type, EditorStyles.miniLabel, GUILayout.Width(140));
                    GUILayout.Label(Bytes(entry.Bytes), EditorStyles.miniLabel, GUILayout.Width(85));
                }
            }
            EditorGUILayout.EndScrollView();
            if (selected != null)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(selected.Name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(selected.Category == MGBundleCategory.ArchiveFiles
                        ? $"{selected.Type}   ·   {Bytes(selected.Bytes)} unpacked"
                        : $"{selected.Type}   ·   {Bytes(selected.Bytes)} serialized   ·   Object ID {selected.Id}", EditorStyles.miniLabel);
                    EditorGUILayout.SelectableLabel(selected.File, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    if (!string.IsNullOrEmpty(selected.Details)) EditorGUILayout.LabelField(selected.Details, EditorStyles.wordWrappedLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Copy entry", GUILayout.Width(90))) EditorGUIUtility.systemCopyBuffer = $"{selected.Name}\n{selected.Type}\n{selected.File}\nObject ID: {selected.Id}\nSerialized bytes: {selected.Bytes}\n{selected.Details}";
                        if (GUILayout.Button("Show bundle in folder", GUILayout.Width(150))) EditorUtility.RevealInFinder(inventory.Path);
                    }
                }
            }
        }

        private void DrawCategories()
        {
            for (int first = 0; first < Categories.Length; first += 4)
            {
                using (new EditorGUILayout.HorizontalScope())
                    for (int index = first; index < Math.Min(first + 4, Categories.Length); index++)
                    {
                        var item = Categories[index];
                        totals.TryGetValue(item, out var total);
                        string label = $"{Label(item)} ({total.count:N0})\n{Bytes(total.bytes)}";
                        if (GUILayout.Toggle(category == item, label, GUI.skin.button, GUILayout.Height(40)) && category != item)
                        { category = item; selected = null; InvalidateFilter(); GUIUtility.ExitGUI(); }
                    }
            }
        }

        internal void BeginInspect(string path)
        {
            if (job != null) return;
            try
            {
                bundlePath = System.IO.Path.GetFullPath(path);
                error = null; inventory = null; visible = null; selected = null;
                job = MGBundleInspectionJob.Start(bundlePath);
                EditorApplication.update -= Poll;
                EditorApplication.update += Poll;
            }
            catch (Exception exception) { error = exception.Message; }
        }

        private double nextRepaint;
        private void Poll()
        {
            if (job == null) { EditorApplication.update -= Poll; return; }
            if (!job.Task.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup >= nextRepaint) { nextRepaint = EditorApplication.timeSinceStartup + .15; repaint?.Invoke(); }
                return;
            }
            try
            {
                if (job.Task.IsCanceled) error = "Inspection canceled.";
                else if (job.Task.IsFaulted) error = job.Task.Exception?.GetBaseException().Message;
                else
                {
                    inventory = job.Task.Result;
                    totals = Categories.ToDictionary(item => item, item =>
                    {
                        var entries = item == MGBundleCategory.ArchiveFiles ? inventory.Files : item == MGBundleCategory.All ? inventory.Assets : inventory.Assets.Where(entry => entry.Category == item);
                        return (entries.Count(), entries.Sum(entry => entry.Bytes));
                    });
                    InvalidateFilter();
                }
            }
            finally { job = null; EditorApplication.update -= Poll; repaint?.Invoke(); }
        }

        internal void Dispose()
        {
            EditorApplication.update -= Poll;
            job?.Cancellation.Cancel();
            job = null;
            repaint = null;
        }
        private void InvalidateFilter() { visible = null; page = 0; scroll = Vector2.zero; }
        internal static List<MGBundleEntry> Filter(MGBundleInventory source, MGBundleCategory category, string search, int sort)
        {
            IEnumerable<MGBundleEntry> entries = category == MGBundleCategory.ArchiveFiles ? source.Files : source.Assets;
            if (category != MGBundleCategory.All && category != MGBundleCategory.ArchiveFiles) entries = entries.Where(entry => entry.Category == category);
            if (!string.IsNullOrWhiteSpace(search)) entries = entries.Where(entry => new[] { entry.Name, entry.Type, entry.File, entry.Details, entry.Id.ToString() }.Any(value => value != null && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0));
            return (sort switch
            {
                1 => entries.OrderBy(entry => entry.Bytes),
                2 => entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
                3 => entries.OrderBy(entry => entry.Type, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
                _ => entries.OrderByDescending(entry => entry.Bytes)
            }).ToList();
        }
        private void SaveCsv()
        {
            string path = EditorUtility.SaveFilePanel("Save bundle inventory", "", System.IO.Path.GetFileName(inventory.Path) + "-inventory.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            try { File.WriteAllText(path, BuildCsv(inventory), new UTF8Encoding(true)); }
            catch (Exception exception) { error = exception.Message; }
        }
        internal static string BuildCsv(MGBundleInventory source)
        {
            string Cell(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
            var csv = new StringBuilder("Category,Name,Type,Bytes,ObjectID,ArchiveFile,Details\r\n");
            foreach (var entry in source.Assets.Concat(source.Files))
                csv.AppendLine(string.Join(",", Cell(Label(entry.Category)), Cell(entry.Name), Cell(entry.Type), entry.Bytes.ToString(), entry.Id.ToString(), Cell(entry.File), Cell(entry.Details)));
            foreach (var warning in source.Warnings) csv.AppendLine("Warning," + Cell(warning));
            return csv.ToString();
        }
        private static string Label(MGBundleCategory value) => value switch { MGBundleCategory.ArchiveFiles => "Archive Files", MGBundleCategory.Objects => "Scene Objects", MGBundleCategory.Scenes => "Scene Data", _ => value.ToString() };
        private static string Bytes(long value) => EditorUtility.FormatBytes(value);
    }

    internal sealed class MGBundleInspectorWindow : EditorWindow
    {
        [SerializeField] private MGBundleInspectorPanel panel = new();
        [MenuItem("Tools/MashBox/Bundle Inspector")]
        internal static void Open() => GetWindow<MGBundleInspectorWindow>("Bundle Inspector");
        private void OnEnable() { minSize = new Vector2(640, 520); }
        private void OnGUI() { panel ??= new(); panel.DrawGUI(Repaint); }
        private void OnDisable() => panel?.Dispose();
    }
}
