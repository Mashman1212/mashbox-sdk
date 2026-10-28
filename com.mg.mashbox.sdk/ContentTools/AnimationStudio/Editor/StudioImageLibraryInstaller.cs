using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace MashBoxSDK.AnimationStudio
{
    [InitializeOnLoad]
    internal static class StudioImageLibraryInstaller
    {
        private const string SourceUrl = "https://raw.githubusercontent.com/Mashman1212/mashbox-sdk-samples/main/com.mg.mashbox.samples/Samples~/Vanilla%20Graphics/Vehicles_BMX_Brands/";
        private const string Destination = "Assets/MashBox/Image Library/Vanilla Graphics/Vehicles_BMX_Brands";
        private const string Cache = "Library/MashBox/VanillaGraphics";
        private const string Key = "MashBox.ImageLibrary.Install.";
        [Serializable] private sealed class Catalog { public int imageCount; public CatalogImage[] images; }
        [Serializable] private sealed class CatalogImage { public string file, sha256; }
        private sealed class Download { internal string file, hash; internal UnityWebRequest request; }
        private static UnityWebRequest catalogRequest;
        private static Catalog catalog;
        private static Queue<Download> queued;
        private static readonly List<Download> active = new List<Download>();
        private static int total, completed;
        internal static event Action Changed;
        internal static bool IsBusy => SessionState.GetBool(Key + "Pending", false);
        internal static string Status => SessionState.GetString(Key + "Status", "");
        internal static bool HasError => SessionState.GetBool(Key + "Error", false);
        static StudioImageLibraryInstaller()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseRequests;
            if (IsBusy) EditorApplication.update += Poll;
        }
        internal static void Install()
        {
            if (IsBusy) return;
            catalog = null; queued = null;
            SessionState.SetBool(Key + "Pending", true);
            SetStatus("Downloading Vanilla Graphics catalog…");
            EditorApplication.update += Poll;
        }
        private static UnityWebRequest BeginDownload(string file)
        {
            var request = UnityWebRequest.Get(SourceUrl + string.Join("/", file.Split('/').Select(Uri.EscapeDataString)));
            request.timeout = 60;
            request.SendWebRequest();
            return request;
        }
        private static void Poll()
        {
            if (!IsBusy) { EditorApplication.update -= Poll; return; }
            try
            {
                if (catalog == null)
                {
                    if (catalogRequest == null) catalogRequest = BeginDownload("ImageCatalog.json");
                    if (!catalogRequest.isDone) return;
                    CheckResponse(catalogRequest, "ImageCatalog.json");
                    catalog = JsonUtility.FromJson<Catalog>(catalogRequest.downloadHandler.text);
                    catalogRequest.Dispose(); catalogRequest = null;
                    PrepareDownloads();
                }
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    var download = active[i];
                    if (!download.request.isDone) continue;
                    CheckResponse(download.request, download.file);
                    byte[] bytes = download.request.downloadHandler.data;
                    if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Image download is too large: " + download.file);
                    if (download.hash != null && Hash(bytes) != download.hash)
                        throw new InvalidDataException("Image checksum did not match the catalog: " + download.file + ". Please retry.");
                    string path = SafePath(Cache, download.file);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, bytes);
                    download.request.Dispose(); active.RemoveAt(i); completed++;
                    SetStatus("Downloading Vanilla Graphics… " + completed + " / " + total + " files");
                }
                while (active.Count < 4 && queued.Count > 0)
                {
                    var download = queued.Dequeue();
                    download.request = BeginDownload(download.file); active.Add(download);
                }
                if (queued.Count > 0 || active.Count > 0 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                ImportGraphics();
            }
            catch (Exception error) { Finish("Could not install Vanilla Graphics: " + error.Message, true); }
        }
        private static void CheckResponse(UnityWebRequest request, string file)
        {
            if (request.result == UnityWebRequest.Result.Success) return;
            if (request.responseCode == 404)
                throw new FileNotFoundException("The graphics are not available on GitHub yet. Publish the Vanilla Graphics sample, then retry. Missing: " + file);
            throw new IOException("Could not download " + file + ": " + request.error);
        }
        private static void PrepareDownloads()
        {
            if (catalog?.images == null || catalog.images.Length == 0 || catalog.images.Length > 5000 || catalog.imageCount != catalog.images.Length)
                throw new InvalidDataException("The Vanilla Graphics catalog is invalid.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            queued = new Queue<Download>();
            foreach (var image in catalog.images)
            {
                if (image == null || string.IsNullOrWhiteSpace(image.file) || !image.file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(image.sha256) || !Regex.IsMatch(image.sha256, "^[a-f0-9]{64}$") || !names.Add(image.file))
                    throw new InvalidDataException("The catalog contains an invalid image entry.");
                string path = SafePath(Cache, image.file); SafePath(Destination, image.file);
                if (!File.Exists(path) || Hash(File.ReadAllBytes(path)) != image.sha256)
                    queued.Enqueue(new Download { file = image.file, hash = image.sha256 });
                queued.Enqueue(new Download { file = image.file + ".meta" });
            }
            completed = 0; total = queued.Count;
            SetStatus("Downloading Vanilla Graphics… 0 / " + total + " files");
        }
        private static string SafePath(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Contains("\\") || relative.Contains(":")
                || relative.Split('/').Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidDataException("Invalid image path in catalog.");
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Image path is outside the library.");
            return path;
        }
        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private static void ImportGraphics()
        {
            SetStatus("Importing Vanilla Graphics…");
            int copied = 0;
            foreach (var image in catalog.images)
            {
                string source = SafePath(Cache, image.file), target = SafePath(Destination, image.file);
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                // Metadata first: a failed write can be retried without skipping this image.
                if (!File.Exists(target + ".meta"))
                {
                    string metadata = File.ReadAllText(source + ".meta");
                    var guid = Regex.Match(metadata, @"(?m)^guid: ([a-fA-F0-9]{32})\r?$");
                    if (guid.Success && !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid.Groups[1].Value)))
                        metadata = metadata.Remove(guid.Groups[1].Index, 32).Insert(guid.Groups[1].Index, Guid.NewGuid().ToString("N"));
                    File.WriteAllText(target + ".meta", metadata);
                }
                File.Copy(source, target); copied++;
            }
            SessionState.SetBool(Key + "Pending", false);
            AssetDatabase.Refresh();
            Finish(copied > 0 ? "Vanilla Graphics installed: " + copied + " images added." : "Vanilla Graphics is already installed. Your existing images were preserved.");
        }
        private static void ReleaseRequests()
        {
            catalogRequest?.Dispose(); catalogRequest = null;
            foreach (var download in active) download.request?.Dispose();
            active.Clear();
        }
        private static void SetStatus(string message, bool error = false)
        {
            SessionState.SetString(Key + "Status", message); SessionState.SetBool(Key + "Error", error); Changed?.Invoke();
        }
        private static void Finish(string message, bool error = false)
        {
            SessionState.SetBool(Key + "Pending", false); EditorApplication.update -= Poll;
            ReleaseRequests(); catalog = null; queued = null; SetStatus(message, error);
        }
    }
}
