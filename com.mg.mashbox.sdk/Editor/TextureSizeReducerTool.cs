#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.SDKMain
{
    [Serializable]
    internal sealed class TextureSizeReducerTool
    {
        private enum ReductionMode
        {
            MatchImporterMaxSize,
            ClampToTargetSize
        }

        [Serializable]
        private sealed class TextureCandidate
        {
            public string AssetPath;
            public string Extension;
            public string DisplayName;
            public TextureImporterType ImporterType;
            public long OriginalBytes;
            public long EstimatedBytes;
            public int Width;
            public int Height;
            public int TargetMaxSize;
            public int NewWidth;
            public int NewHeight;
            public bool NeedsReduction;
            public bool Selected;
        }

        private static readonly int[] TargetSizes = { 4096, 2048, 1024, 512, 256 };
        private static readonly string[] TargetSizeLabels = { "4096", "2048", "1024", "512", "256" };
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".tga"
        };

        private const string ModePrefKey = "MashBoxSDK.TextureReducer.Mode";
        private const string TargetSizePrefKey = "MashBoxSDK.TextureReducer.TargetSize";
        private readonly List<TextureCandidate> candidates = new List<TextureCandidate>();

        private string statusMessage = "Drop textures or folders here to inspect them for size reduction.";
        private Vector2 scrollPosition;
        private ReductionMode reductionMode = ReductionMode.MatchImporterMaxSize;
        private int targetSizeIndex = 2;
        private bool hasScanResults;
        private bool isDragHoveringDropZone;
        private bool initialized;

        public void Initialize()
        {
            if (initialized)
                return;

            initialized = true;
            reductionMode = (ReductionMode)EditorPrefs.GetInt(ModePrefKey, (int)reductionMode);
            targetSizeIndex = Mathf.Clamp(EditorPrefs.GetInt(TargetSizePrefKey, targetSizeIndex), 0, TargetSizes.Length - 1);
        }

        public void Draw()
        {
            Initialize();

            EditorGUILayout.LabelField("Texture Size Reducer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drop textures or folders here to inspect them. The tool will show everything you dropped, and only the oversized ones will be selectable for reduction.",
                MessageType.Info);

            DrawControls();
            GUILayout.Space(6f);
            DrawDropZone();
            GUILayout.Space(8f);
            DrawSummary();
            GUILayout.Space(6f);
            DrawResults();
        }

        private void DrawControls()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUI.BeginChangeCheck();
            reductionMode = (ReductionMode)EditorGUILayout.EnumPopup("Reduction Mode", reductionMode);
            if (reductionMode == ReductionMode.ClampToTargetSize)
                targetSizeIndex = EditorGUILayout.Popup("Target Max Size", targetSizeIndex, TargetSizeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                SavePrefs();
                RefreshLoadedCandidates();
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(GetSelectedReducibleCount() == 0))
            {
                if (GUILayout.Button($"Reduce Selected ({GetSelectedReducibleCount()})", GUILayout.Height(28f)))
                    ReduceSelectedTextures();
            }

            if (GUILayout.Button("Select All", GUILayout.Width(90f)))
                SetSelectionForAll(true);

            if (GUILayout.Button("None", GUILayout.Width(70f)))
                SetSelectionForAll(false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawDropZone()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0f, 56f, GUILayout.ExpandWidth(true));
            var previousColor = GUI.color;
            if (isDragHoveringDropZone)
                GUI.color = new Color(0.78f, 0.94f, 1f, 1f);

            GUI.Box(dropRect, "Drag textures or folders here to build a reduction list");
            GUI.color = previousColor;

            Event evt = Event.current;
            bool isHovering = dropRect.Contains(evt.mousePosition);
            if (isDragHoveringDropZone != isHovering)
                isDragHoveringDropZone = isHovering;

            if (!isHovering)
                return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    DragAndDrop.visualMode = HasSupportedDropTargets()
                        ? DragAndDropVisualMode.Copy
                        : DragAndDropVisualMode.Rejected;
                    evt.Use();
                    break;
                case EventType.DragPerform:
                    if (!HasSupportedDropTargets())
                    {
                        statusMessage = "Drop project textures or folders that contain PNG, JPG, or TGA files.";
                        evt.Use();
                        return;
                    }

                    DragAndDrop.AcceptDrag();
                    HandleDroppedAssets();
                    evt.Use();
                    break;
                case EventType.DragExited:
                    isDragHoveringDropZone = false;
                    break;
            }
        }

        private void DrawSummary()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Scan Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(statusMessage, EditorStyles.wordWrappedLabel);

            if (!hasScanResults)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            long originalBytes = 0;
            long estimatedBytes = 0;
            foreach (var candidate in candidates)
            {
                if (!candidate.Selected || !candidate.NeedsReduction)
                    continue;

                originalBytes += candidate.OriginalBytes;
                estimatedBytes += candidate.EstimatedBytes;
            }

            int selectedCount = GetSelectedReducibleCount();
            int reducibleCount = GetReducibleCandidateCount();
            long savingsBytes = Math.Max(0L, originalBytes - estimatedBytes);

            EditorGUILayout.LabelField($"Dropped textures: {candidates.Count}");
            EditorGUILayout.LabelField($"Needs reduction: {reducibleCount}");
            EditorGUILayout.LabelField($"Selected textures: {selectedCount}");
            EditorGUILayout.LabelField($"Current source size: {FormatBytes(originalBytes)}");
            EditorGUILayout.LabelField($"Estimated reduced size: {FormatBytes(estimatedBytes)}");
            EditorGUILayout.LabelField($"Estimated savings: {FormatBytes(savingsBytes)}");
            EditorGUILayout.EndVertical();
        }

        private void DrawResults()
        {
            if (!hasScanResults)
                return;

            if (candidates.Count == 0)
            {
                EditorGUILayout.HelpBox("No supported PNG, JPG, or TGA textures have been dropped yet.", MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var candidate in candidates)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(!candidate.NeedsReduction))
                {
                    candidate.Selected = EditorGUILayout.Toggle(candidate.Selected, GUILayout.Width(18f));
                }

                if (GUILayout.Button(candidate.DisplayName, EditorStyles.linkLabel, GUILayout.Width(260f)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(candidate.AssetPath);
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(candidate.ImporterType.ToString(), GUILayout.Width(110f));
                EditorGUILayout.LabelField(candidate.Extension.ToUpperInvariant(), GUILayout.Width(52f));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(candidate.AssetPath, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    $"Source: {candidate.Width}x{candidate.Height} ({FormatBytes(candidate.OriginalBytes)})  ->  New: {candidate.NewWidth}x{candidate.NewHeight} (~{FormatBytes(candidate.EstimatedBytes)})",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    candidate.NeedsReduction
                        ? $"Target max size: {candidate.TargetMaxSize}  |  Estimated savings: {FormatBytes(Math.Max(0L, candidate.OriginalBytes - candidate.EstimatedBytes))}"
                        : $"Already within target max size: {candidate.TargetMaxSize}",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void HandleDroppedAssets()
        {
            var assetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int unsupportedCount = 0;

            foreach (string path in DragAndDrop.paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                string normalizedPath = path.Replace('\\', '/');
                if (!IsProjectAssetPath(normalizedPath))
                    continue;

                if (AssetDatabase.IsValidFolder(normalizedPath) || IsSupportedTextureAssetPath(normalizedPath))
                {
                    assetPaths.Add(normalizedPath);
                }
                else
                {
                    unsupportedCount++;
                }
            }

            foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrWhiteSpace(assetPath))
                    continue;

                assetPath = assetPath.Replace('\\', '/');
                if (AssetDatabase.IsValidFolder(assetPath) || IsSupportedTextureAssetPath(assetPath))
                {
                    assetPaths.Add(assetPath);
                }
                else if (obj is Texture2D || obj is DefaultAsset)
                {
                    unsupportedCount++;
                }
            }

            if (assetPaths.Count == 0)
            {
                statusMessage = unsupportedCount > 0
                    ? "Those dropped assets are not supported for rewriting yet. Use PNG, JPG, or TGA textures, or drop a folder containing them."
                    : "Drop project textures or folders from the Unity project window.";
                return;
            }

            ScanAssetPaths(assetPaths, "dropped assets");
        }

        private void ScanAssetPaths(IEnumerable<string> seedPaths, string sourceLabel)
        {
            SavePrefs();
            candidates.Clear();
            hasScanResults = true;

            var folderPaths = new List<string>();
            var directTexturePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawPath in seedPaths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                    continue;

                string assetPath = rawPath.Replace('\\', '/');
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    folderPaths.Add(assetPath);
                    continue;
                }

                if (IsSupportedTextureAssetPath(assetPath))
                    directTexturePaths.Add(assetPath);
            }

            if (folderPaths.Count > 0)
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D", folderPaths.ToArray());
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (IsSupportedTextureAssetPath(assetPath))
                        directTexturePaths.Add(assetPath);
                }
            }

            foreach (string assetPath in directTexturePaths)
                AddCandidate(assetPath);

            candidates.Sort((a, b) => b.OriginalBytes.CompareTo(a.OriginalBytes));

            long totalBytes = 0;
            long estimatedTotalBytes = 0;
            foreach (var candidate in candidates)
            {
                if (!candidate.NeedsReduction)
                    continue;

                totalBytes += candidate.OriginalBytes;
                estimatedTotalBytes += candidate.EstimatedBytes;
            }

            long estimatedSavings = Math.Max(0L, totalBytes - estimatedTotalBytes);
            statusMessage = candidates.Count == 0
                ? $"No supported PNG, JPG, or TGA textures were found in {sourceLabel}."
                : $"Loaded {candidates.Count} textures from {sourceLabel}. {GetReducibleCandidateCount()} need reduction. Estimated savings if reduced: {FormatBytes(estimatedSavings)}.";
        }

        private void AddCandidate(string assetPath)
        {
            string extension = Path.GetExtension(assetPath);
            if (string.IsNullOrWhiteSpace(extension) || !SupportedExtensions.Contains(extension))
                return;

            if (!TryReadTextureInfo(assetPath, out int width, out int height, out long fileBytes))
                return;

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            int targetMaxSize = GetTargetMaxSize(importer);
            if (targetMaxSize <= 0)
                return;

            int currentLargestDimension = Mathf.Max(width, height);
            bool needsReduction = currentLargestDimension > targetMaxSize;
            float scale = targetMaxSize / (float)Math.Max(1, currentLargestDimension);
            int newWidth = needsReduction ? Mathf.Max(1, Mathf.RoundToInt(width * scale)) : width;
            int newHeight = needsReduction ? Mathf.Max(1, Mathf.RoundToInt(height * scale)) : height;
            long estimatedBytes = needsReduction
                ? EstimateReducedBytes(fileBytes, width, height, newWidth, newHeight)
                : fileBytes;

            candidates.Add(new TextureCandidate
            {
                AssetPath = assetPath,
                DisplayName = Path.GetFileName(assetPath),
                Extension = extension,
                ImporterType = importer.textureType,
                OriginalBytes = fileBytes,
                EstimatedBytes = estimatedBytes,
                Width = width,
                Height = height,
                TargetMaxSize = targetMaxSize,
                NewWidth = newWidth,
                NewHeight = newHeight,
                NeedsReduction = needsReduction,
                Selected = needsReduction
            });
        }

        private void ReduceSelectedTextures()
        {
            int processed = 0;
            int failed = 0;
            var rescannedPaths = new List<string>();

            foreach (var candidate in candidates)
            {
                rescannedPaths.Add(candidate.AssetPath);

                if (!candidate.Selected || !candidate.NeedsReduction)
                    continue;

                if (TryResizeTexture(candidate, out string error))
                {
                    processed++;
                }
                else
                {
                    failed++;
                    Debug.LogWarning($"[TextureSizeReducer] Failed to process {candidate.AssetPath}: {error}");
                }
            }

            AssetDatabase.Refresh();
            ScanAssetPaths(rescannedPaths, "dropped assets");

            statusMessage = failed == 0
                ? $"Reduced {processed} textures successfully."
                : $"Reduced {processed} textures, with {failed} failures. Check the console warnings for details.";
        }

        private void RefreshLoadedCandidates()
        {
            if (!hasScanResults || candidates.Count == 0)
                return;

            var rescannedPaths = new List<string>();
            foreach (var candidate in candidates)
                rescannedPaths.Add(candidate.AssetPath);

            ScanAssetPaths(rescannedPaths, "dropped assets");
        }

        private bool TryResizeTexture(TextureCandidate candidate, out string error)
        {
            error = string.Empty;

            string fullPath = GetFullPath(candidate.AssetPath);
            if (!File.Exists(fullPath))
            {
                error = "Source file was not found on disk.";
                return false;
            }

            Texture2D sourceTexture = null;
            Texture2D resizedTexture = null;

            try
            {
                byte[] fileData = File.ReadAllBytes(fullPath);
                sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                if (!ImageConversion.LoadImage(sourceTexture, fileData))
                {
                    error = "Unity could not decode the source image.";
                    return false;
                }

                resizedTexture = ScaleTexture(sourceTexture, candidate.NewWidth, candidate.NewHeight);
                byte[] encodedBytes = EncodeTexture(resizedTexture, candidate.Extension);
                if (encodedBytes == null || encodedBytes.Length == 0)
                {
                    error = $"Encoding failed for {candidate.Extension}.";
                    return false;
                }

                File.WriteAllBytes(fullPath, encodedBytes);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (sourceTexture != null)
                    UnityEngine.Object.DestroyImmediate(sourceTexture);

                if (resizedTexture != null)
                    UnityEngine.Object.DestroyImmediate(resizedTexture);
            }
        }

        private Texture2D ScaleTexture(Texture2D source, int width, int height)
        {
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                useMipMap = false,
                autoGenerateMips = false
            };

            RenderTexture previous = RenderTexture.active;

            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;

                Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                resized.Apply(false, false);
                return resized;
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private byte[] EncodeTexture(Texture2D texture, string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                    return texture.EncodeToJPG();
                case ".tga":
                    return texture.EncodeToTGA();
                case ".png":
                default:
                    return texture.EncodeToPNG();
            }
        }

        private int GetTargetMaxSize(TextureImporter importer)
        {
            int importerMaxSize = Mathf.Max(32, importer.maxTextureSize);
            if (reductionMode == ReductionMode.MatchImporterMaxSize)
                return importerMaxSize;

            return Mathf.Min(importerMaxSize, TargetSizes[targetSizeIndex]);
        }

        private static long EstimateReducedBytes(long originalBytes, int width, int height, int newWidth, int newHeight)
        {
            long originalPixels = (long)width * height;
            long newPixels = (long)newWidth * newHeight;

            if (originalPixels <= 0L || newPixels <= 0L)
                return originalBytes;

            double ratio = newPixels / (double)originalPixels;
            long estimate = (long)Math.Round(originalBytes * ratio);
            return Math.Max(1L, estimate);
        }

        private static bool TryReadTextureInfo(string assetPath, out int width, out int height, out long fileBytes)
        {
            width = 0;
            height = 0;
            fileBytes = 0L;

            string fullPath = GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return false;

            Texture2D probe = null;

            try
            {
                byte[] fileData = File.ReadAllBytes(fullPath);
                fileBytes = fileData.LongLength;

                probe = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                if (!ImageConversion.LoadImage(probe, fileData, markNonReadable: true))
                    return false;

                width = probe.width;
                height = probe.height;
                return width > 0 && height > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (probe != null)
                    UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        private static bool IsSupportedTextureAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                return false;

            string extension = Path.GetExtension(assetPath);
            return !string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension);
        }

        private static bool IsProjectAssetPath(string assetPath)
        {
            return assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(assetPath, "Assets", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSupportedDropTargets()
        {
            foreach (string path in DragAndDrop.paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                string normalizedPath = path.Replace('\\', '/');
                if (!IsProjectAssetPath(normalizedPath))
                    continue;

                if (AssetDatabase.IsValidFolder(normalizedPath) || IsSupportedTextureAssetPath(normalizedPath))
                    return true;
            }

            foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrWhiteSpace(assetPath))
                    continue;

                assetPath = assetPath.Replace('\\', '/');
                if (AssetDatabase.IsValidFolder(assetPath) || IsSupportedTextureAssetPath(assetPath))
                    return true;
            }

            return false;
        }

        private void SetSelectionForAll(bool isSelected)
        {
            foreach (var candidate in candidates)
                candidate.Selected = candidate.NeedsReduction && isSelected;
        }

        private void SavePrefs()
        {
            EditorPrefs.SetInt(ModePrefKey, (int)reductionMode);
            EditorPrefs.SetInt(TargetSizePrefKey, targetSizeIndex);
        }

        private int GetReducibleCandidateCount()
        {
            int count = 0;
            foreach (var candidate in candidates)
            {
                if (candidate.NeedsReduction)
                    count++;
            }

            return count;
        }

        private int GetSelectedReducibleCount()
        {
            int count = 0;
            foreach (var candidate in candidates)
            {
                if (candidate.NeedsReduction && candidate.Selected)
                    count++;
            }

            return count;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
                return "0 B";

            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            int unitIndex = 0;

            while (value >= 1024d && unitIndex < units.Length - 1)
            {
                value /= 1024d;
                unitIndex++;
            }

            return $"{value:0.##} {units[unitIndex]}";
        }

        private static string GetFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string normalizedAssetPath = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(projectRoot, normalizedAssetPath);
        }
    }
}

#endif
