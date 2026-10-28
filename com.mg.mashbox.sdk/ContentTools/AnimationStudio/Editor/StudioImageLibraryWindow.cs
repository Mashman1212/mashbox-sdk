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
        private const string PackageName = "com.mg.mashbox.samples";
        private const string SampleImportRoot = "Assets/Samples/MashBox Samples/";
        private const string LegacySampleImportRoot = "Assets/Samples/MashBox SDK/";
        private const string ProjectFolder = "Assets/MashBox/Image Library";
        private const string SampleName = "Vanilla Graphics";
        [SerializeField] private StudioImagingWindow owner;
        [SerializeField] private string search = "", folder = "";
        [SerializeField] private float thumbnailSize = 100;
        private readonly List<string> images = new List<string>();
        private readonly List<string> folders = new List<string>();
        private Vector2 scroll, folderScroll, dragStart;
        private Texture2D dragCandidate;
        private string selectedPath, status;
        private MessageType statusType;
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
            needsRefresh = true;
        }

        private void OnDisable() => EditorApplication.projectChanged -= ProjectChanged;
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
                    if (GUILayout.Button("Install Vanilla Graphics", EditorStyles.toolbarButton, GUILayout.Width(145))) InstallSample();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(165)))
                    {
                        folderScroll = EditorGUILayout.BeginScrollView(folderScroll);
                        DrawFolder("All Images", "");
                        foreach (string path in folders)
                        {
                            string label = path.StartsWith(ProjectFolder, StringComparison.OrdinalIgnoreCase)
                                ? "Project" + path.Substring(ProjectFolder.Length)
                                : path.Substring((path.StartsWith(SampleImportRoot, StringComparison.OrdinalIgnoreCase) ? SampleImportRoot : LegacySampleImportRoot).Length);
                            DrawFolder(label, path);
                        }
                        EditorGUILayout.EndScrollView();
                    }
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true))) DrawGrid();
                }
                if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label(images.Count + " images · Double-click or drag onto canvas", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    thumbnailSize = GUILayout.HorizontalSlider(thumbnailSize, 64, 160, GUILayout.Width(90));
                }
            }
        }

        private void DrawFolder(string label, string path)
        {
            if (GUILayout.Toggle(folder == path, new GUIContent(label, path), "Button")) folder = path;
        }

        private void DrawGrid()
        {
            var visible = images.Where(path => (string.IsNullOrEmpty(folder) || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(search) || path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (visible.Length == 0)
            {
                EditorGUILayout.HelpBox(images.Count == 0
                    ? "Your Image Library is empty. Install Vanilla Graphics, or add images to the Project Folder."
                    : "No images match this folder and search.", MessageType.Info);
            }
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 195) / (thumbnailSize + 12)));
            for (int i = 0; i < visible.Length;)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int column = 0; column < columns && i < visible.Length; column++, i++)
                    {
                        string path = visible[i];
                        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        var cell = GUILayoutUtility.GetRect(thumbnailSize + 8, thumbnailSize + 32, GUILayout.Width(thumbnailSize + 8));
                        if (Event.current.type == EventType.Repaint)
                        {
                            EditorGUI.DrawRect(cell, selectedPath == path ? StudioTheme.Current.Accent * .45f : StudioTheme.Current.Header);
                            if (texture) GUI.DrawTexture(new Rect(cell.x + 4, cell.y + 4, thumbnailSize, thumbnailSize), texture, ScaleMode.ScaleToFit, true);
                            GUI.Label(new Rect(cell.x + 2, cell.y + thumbnailSize + 6, cell.width - 4, 22), new GUIContent(Path.GetFileNameWithoutExtension(path), path), EditorStyles.centeredGreyMiniLabel);
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

        private void InstallSample()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + PackageName + "/package.json");
                if (package == null)
                {
                    status = "Install MashBox Samples from MashBox > SDK, then click Install Vanilla Graphics again.";
                    statusType = MessageType.Info;
                    EditorApplication.ExecuteMenuItem("MashBox/SDK");
                    return;
                }
                string source = Path.Combine(package.resolvedPath, "Samples~", SampleName);
                if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Vanilla Graphics is missing from MashBox Samples. Update the samples package from MashBox > SDK.");
                string destination = SampleImportRoot + package.version + "/" + SampleName;
                Directory.CreateDirectory(destination);
                int copied = 0;
                // Add files without replacing a user's edited image or its importer metadata.
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string target = Path.Combine(destination, relative);
                    if (File.Exists(target)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target);
                    if (File.Exists(file + ".meta") && !File.Exists(target + ".meta"))
                    {
                        string metadata = File.ReadAllText(file + ".meta");
                        var guid = System.Text.RegularExpressions.Regex.Match(metadata, @"(?m)^guid: ([a-fA-F0-9]{32})\r?$");
                        // Earlier SDK sample imports may already own this GUID. Keep importer settings,
                        // but give this additional copy its own identity rather than creating a conflict.
                        if (guid.Success && !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid.Groups[1].Value)))
                            metadata = metadata.Remove(guid.Groups[1].Index, 32).Insert(guid.Groups[1].Index, Guid.NewGuid().ToString("N"));
                        File.WriteAllText(target + ".meta", metadata);
                    }
                    copied++;
                }
                AssetDatabase.Refresh();
                needsRefresh = true;
                status = copied > 0 ? "Vanilla Graphics installed. The library will show any images included in the sample."
                    : "Vanilla Graphics is up to date. No existing project files were replaced.";
                statusType = MessageType.Info;
            }
            catch (Exception error) { status = "Could not install Vanilla Graphics: " + error.Message; statusType = MessageType.Error; }
        }
    }
}
