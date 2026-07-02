#if MashBoxDev

using System.IO;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.Dev
{
    public class TextureResizerTool : EditorWindow
    {
        private string folderPath = "Assets";

        [MenuItem("MashBox/Dev/Texture Resizer Tool")]
        public static void ShowWindow()
        {
            GetWindow<TextureResizerTool>("Texture Resizer");
        }

        
        
        private void OnGUI()
        {
            GUILayout.Label("Resize PNGs to Import Max Size", EditorStyles.boldLabel);

            folderPath = EditorGUILayout.TextField("Root Folder (Assets/...)", folderPath);

            if (GUILayout.Button("Process PNGs"))
            {
                ProcessTextures(folderPath);
            }
        }

        private void ProcessTextures(string rootPath)
        {
            int processed = 0;
            int found = 0;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { rootPath });

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (!assetPath.ToLower().EndsWith(".png"))
                    continue;

                found++;

                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null)
                    continue;

                int maxSize = importer.maxTextureSize;

                // ✅ Get TRUE source file size (not Unity imported size)
                string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);

                if (!File.Exists(fullPath))
                {
                    Debug.LogWarning($"File not found on disk: {fullPath}");
                    continue;
                }

                byte[] fileData = File.ReadAllBytes(fullPath);
                Texture2D original = new Texture2D(2, 2);
                original.LoadImage(fileData);

                int width = original.width;
                int height = original.height;

                int largestDimension = Mathf.Max(width, height);

                Debug.Log($"Found: {assetPath} ({width}x{height}) Max:{maxSize}");

                if (largestDimension <= maxSize)
                    continue;

                Debug.Log($"Resizing: {assetPath} ({width}x{height}) → MaxSize {maxSize}");

                ResizeTexture(fullPath, original, width, height, maxSize);

                processed++;
            }

            AssetDatabase.Refresh();

            Debug.Log($"Done. Found {found} PNGs. Processed {processed} textures.");
        }

        private void ResizeTexture(string fullPath, Texture2D original, int width, int height, int maxSize)
        {
            float scale = (float)maxSize / Mathf.Max(width, height);
            int newWidth = Mathf.RoundToInt(width * scale);
            int newHeight = Mathf.RoundToInt(height * scale);

            Texture2D resized = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);

            // Bilinear resize
            for (int y = 0; y < newHeight; y++)
            {
                for (int x = 0; x < newWidth; x++)
                {
                    float u = (float)x / newWidth;
                    float v = (float)y / newHeight;
                    resized.SetPixel(x, y, original.GetPixelBilinear(u, v));
                }
            }

            resized.Apply();

            byte[] pngData = resized.EncodeToPNG();
            File.WriteAllBytes(fullPath, pngData);
        }
    }
}

#endif