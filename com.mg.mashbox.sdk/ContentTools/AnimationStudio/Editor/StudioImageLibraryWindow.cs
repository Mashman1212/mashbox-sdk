using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed class StudioImageLibraryWindow : EditorWindow
    {
        private const string SampleImportRoot = "Assets/Samples/MashBox Samples/";
        private const string LegacySampleImportRoot = "Assets/Samples/MashBox SDK/";
        private const string ProjectFolder = "Assets/MashBox/Image Library";
        private const string SampleName = "Vanilla Graphics";
        [SerializeField] private StudioImagingWindow owner;
        [SerializeField] private string search = "", folder = "";
        [SerializeField] private float thumbnailSize = 100;
        [SerializeField] private float sidebarWidth = 245;
        [SerializeField] private List<string> expandedFolders = new List<string>();
        private Texture2D checker;
        private int resizeControl;
        private int visibleCount;
        private const string VanillaRoot = ProjectFolder + "/Vanilla Graphics/Vehicles_BMX_Brands";
        private readonly List<string> images = new List<string>();
        private readonly List<string> folders = new List<string>();
        private Vector2 scroll, folderScroll, dragStart;
        private Texture2D dragCandidate;
        private string selectedPath;
        private bool needsRefresh = true;

        internal static void Open(StudioImagingWindow imaging)
        {
            var window = GetWindow<StudioImageLibraryWindow>();
            window.owner = imaging;
            window.Show();
        }

        [MenuItem("MashBox/MashBox Studio/Image Library", false, 125)]
        private static void OpenFromMenu() => Open(null);

        private void OnEnable()
        {
            titleContent = new GUIContent("Image Library", EditorGUIUtility.IconContent("Texture2D Icon").image);
            minSize = new Vector2(540, 360);
            EditorApplication.projectChanged += ProjectChanged;
            StudioImageLibraryInstaller.Changed += ProjectChanged;
            needsRefresh = true;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= ProjectChanged;
            StudioImageLibraryInstaller.Changed -= ProjectChanged;
            if (checker) DestroyImmediate(checker);
        }
        private void ProjectChanged() { needsRefresh = true; Repaint(); }

        private static bool IsSampleImage(string path)
        {
            string prefix = path.StartsWith(SampleImportRoot, StringComparison.OrdinalIgnoreCase) ? SampleImportRoot : LegacySampleImportRoot;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string remainder = path.Substring(prefix.Length);
            int slash = remainder.IndexOf('/');
            return slash >= 0 && remainder.Substring(slash + 1).StartsWith(SampleName + "/", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshImages()
        {
            images.Clear();
            folders.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith(ProjectFolder + "/", StringComparison.OrdinalIgnoreCase) && !IsSampleImage(path)) continue;
                images.Add(path);
                string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
                while (!string.IsNullOrEmpty(parent) && parent != "Assets")
                {
                    if (!folders.Contains(parent)) folders.Add(parent);
                    if (parent == ProjectFolder || parent.EndsWith("/" + SampleName, StringComparison.OrdinalIgnoreCase)) break;
                    parent = Path.GetDirectoryName(parent)?.Replace('\\', '/');
                }
            }
            images.Sort(StringComparer.OrdinalIgnoreCase);
            folders.Sort(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(folder) && !folders.Contains(folder)) folder = "";
            needsRefresh = false;
        }

        private void OnGUI()
        {
            if (needsRefresh) RefreshImages();
            using (StudioTheme.Scope())
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    search = GUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
                    if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(55))) needsRefresh = true;
                    if (GUILayout.Button("Project Folder", EditorStyles.toolbarButton, GUILayout.Width(90))) SelectProjectFolder();
                    using (new EditorGUI.DisabledScope(StudioImageLibraryInstaller.IsBusy))
                        if (GUILayout.Button(StudioImageLibraryInstaller.IsBusy ? "Installing…" : "Install Vanilla Graphics", EditorStyles.toolbarButton, GUILayout.Width(145)))
                            StudioImageLibraryInstaller.Install();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    sidebarWidth = Mathf.Clamp(sidebarWidth, 180, Mathf.Max(180, position.width - 280));
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(sidebarWidth)))
                    {
                        GUILayout.Label("FOLDERS", EditorStyles.miniBoldLabel);
                        folderScroll = EditorGUILayout.BeginScrollView(folderScroll);
                        DrawFolder("All Images", "", 0, false);
                        if (folders.Contains(VanillaRoot)) DrawFolderTree(VanillaRoot, "Vanilla Graphics", 0);
                        if (images.Any(path => path.StartsWith(ProjectFolder + "/", StringComparison.OrdinalIgnoreCase)
                            && !path.StartsWith(ProjectFolder + "/Vanilla Graphics/", StringComparison.OrdinalIgnoreCase)))
                            DrawFolderTree(ProjectFolder, "Project Images", 0);
                        foreach (string root in folders.Where(path => path.EndsWith("/" + SampleName, StringComparison.OrdinalIgnoreCase)
                            && !path.StartsWith(ProjectFolder, StringComparison.OrdinalIgnoreCase)))
                            DrawFolderTree(root, "Vanilla Graphics (" + Path.GetFileName(Path.GetDirectoryName(root)) + ")", 0);
                        EditorGUILayout.EndScrollView();
                    }
                    DrawSidebarDivider();
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true))) DrawGrid();
                }
                if (!string.IsNullOrEmpty(StudioImageLibraryInstaller.Status))
                    EditorGUILayout.HelpBox(StudioImageLibraryInstaller.Status, StudioImageLibraryInstaller.HasError ? MessageType.Error : MessageType.Info);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label(visibleCount + " / " + images.Count + " images · Double-click or drag onto canvas", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    thumbnailSize = GUILayout.HorizontalSlider(thumbnailSize, 64, 160, GUILayout.Width(90));
                }
            }
        }

        private void DrawFolderTree(string path, string label, int depth)
        {
            var children = folders.Where(child => string.Equals(Path.GetDirectoryName(child)?.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase)
                && !(path == ProjectFolder && child == ProjectFolder + "/Vanilla Graphics")).ToArray();
            // Open collection roots initially; individual brands remain collapsed.
            if (depth == 0 && !expandedFolders.Contains("@" + path))
            { expandedFolders.Add("@" + path); expandedFolders.Add(path); }
            DrawFolder(label, path, depth, children.Length > 0);
            if (expandedFolders.Contains(path))
                foreach (var child in children) DrawFolderTree(child, Path.GetFileName(child), depth + 1);
        }

        private void DrawFolder(string label, string path, int depth, bool hasChildren)
        {
            var row = GUILayoutUtility.GetRect(0, 25, GUILayout.ExpandWidth(true));
            bool selected = folder == path;
            if (selected) EditorGUI.DrawRect(row, StudioTheme.Current.Selection);
            else if (row.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(row, StudioTheme.Current.Hover);
            float left = row.x + 4 + depth * 14;
            if (hasChildren)
            {
                bool expanded = expandedFolders.Contains(path);
                bool next = EditorGUI.Foldout(new Rect(left, row.y + 4, 14, 18), expanded, GUIContent.none);
                if (next != expanded) { if (next) expandedFolders.Add(path); else expandedFolders.Remove(path); }
            }
            GUI.Label(new Rect(left + 15, row.y + 4, 17, 18), EditorGUIUtility.IconContent(path == "" ? "Texture2D Icon" : "Folder Icon"));
            int count = path == "" ? images.Count : images.Count(image => image.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase));
            var textStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            GUI.Label(new Rect(left + 35, row.y, Mathf.Max(10, row.xMax - left - 69), row.height), new GUIContent(label, path), textStyle);
            GUI.Label(new Rect(row.xMax - 32, row.y + 3, 30, 20), count.ToString(), EditorStyles.miniLabel);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && row.Contains(Event.current.mousePosition)
                && Event.current.mousePosition.x > left + 14)
            {
                folder = path; scroll = Vector2.zero;
                if (Event.current.clickCount == 2 && hasChildren)
                { if (expandedFolders.Contains(path)) expandedFolders.Remove(path); else expandedFolders.Add(path); }
                Event.current.Use(); Repaint();
            }
        }

        private void DrawSidebarDivider()
        {
            var rect = GUILayoutUtility.GetRect(5, 0, GUILayout.Width(5), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, StudioTheme.Current.Border);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            int control = GUIUtility.GetControlID("ImageLibrarySidebar".GetHashCode(), FocusType.Passive);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            { resizeControl = control; GUIUtility.hotControl = control; evt.Use(); }
            if (resizeControl != control || GUIUtility.hotControl != control) return;
            if (evt.type == EventType.MouseDrag)
            { sidebarWidth = Mathf.Clamp(evt.mousePosition.x, 180, Mathf.Max(180, position.width - 280)); evt.Use(); Repaint(); }
            if (evt.rawType == EventType.MouseUp)
            { resizeControl = 0; GUIUtility.hotControl = 0; evt.Use(); }
        }

        private void DrawChecker(Rect rect)
        {
            if (!checker)
            {
                checker = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                var light = new Color(.68f, .68f, .68f, 1);
                var dark = new Color(.51f, .51f, .51f, 1);
                checker.SetPixels(new[] { light, dark, dark, light }); checker.Apply();
            }
            GUI.DrawTextureWithTexCoords(rect, checker, new Rect(0, 0, rect.width / 16, rect.height / 16));
        }

        private void DrawGrid()
        {
            var visible = images.Where(path => (string.IsNullOrEmpty(folder) || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(search) || path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            visibleCount = visible.Length;
            GUILayout.Label(string.IsNullOrEmpty(folder) ? "All Images" : folder.Substring(folder.StartsWith(ProjectFolder + "/", StringComparison.OrdinalIgnoreCase)
                ? (ProjectFolder + "/").Length : "Assets/Samples/".Length).Replace("Vehicles_BMX_Brands", "BMX Brands").Replace("/", "  /  "), EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (visible.Length == 0)
            {
                EditorGUILayout.HelpBox(images.Count == 0
                    ? "Your Image Library is empty. Install Vanilla Graphics, or add images to the Project Folder."
                    : "No images match this folder and search.", MessageType.Info);
            }
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - sidebarWidth - 36) / (thumbnailSize + 16)));
            for (int i = 0; i < visible.Length;)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int column = 0; column < columns && i < visible.Length; column++, i++)
                    {
                        string path = visible[i];
                        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        var cell = GUILayoutUtility.GetRect(thumbnailSize + 12, thumbnailSize + 52, GUILayout.Width(thumbnailSize + 12));
                        if (Event.current.type == EventType.Repaint)
                        {
                            EditorGUI.DrawRect(cell, selectedPath == path ? StudioTheme.Current.Selection : StudioTheme.Current.Panel);
                            var preview = new Rect(cell.x + 6, cell.y + 4, thumbnailSize, thumbnailSize);
                            DrawChecker(preview);
                            if (texture) GUI.DrawTexture(preview, texture, ScaleMode.ScaleToFit, true);
                            var caption = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, wordWrap = true, clipping = TextClipping.Clip };
                            GUI.Label(new Rect(cell.x + 3, cell.y + thumbnailSize + 9, cell.width - 6, 38), new GUIContent(Path.GetFileNameWithoutExtension(path), path), caption);
                        }
                        HandleImageInput(cell, path, texture);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            if (Event.current.rawType == EventType.MouseUp) dragCandidate = null;
        }

        private void HandleImageInput(Rect rect, string path, Texture2D texture)
        {
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                selectedPath = path;
                dragCandidate = texture;
                dragStart = evt.mousePosition;
                if (evt.clickCount == 2)
                {
                    if (!owner) { StudioImagingWindow.Open(); owner = GetWindow<StudioImagingWindow>(); }
                    owner.AddLibraryImage(texture);
                    dragCandidate = null;
                }
                evt.Use(); Repaint();
            }
            if (evt.type == EventType.MouseDrag && dragCandidate && Vector2.Distance(dragStart, evt.mousePosition) > 5)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new UnityEngine.Object[] { dragCandidate };
                DragAndDrop.StartDrag(dragCandidate.name);
                dragCandidate = null;
                evt.Use();
            }
        }

        private static void SelectProjectFolder()
        {
            Directory.CreateDirectory(ProjectFolder);
            AssetDatabase.Refresh();
            var asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ProjectFolder);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

    }
}
