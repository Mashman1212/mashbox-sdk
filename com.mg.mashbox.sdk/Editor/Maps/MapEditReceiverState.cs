using System;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.Editing;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    // Listening belongs to the editor session, not the lifetime of its settings window.
    [InitializeOnLoad]
    internal static class MapEditReceiverState
    {
        const string Prefix = "MashBox.MapReceiver.";
        static double nextPoll;
        static double nextAnnouncement;
        internal static event Action Changed;
        internal static string Id
        {
            get
            {
                string id = Get("id");
                if (string.IsNullOrEmpty(id)) Set("id", id = Guid.NewGuid().ToString("N"));
                return id;
            }
        }
        internal static bool Listening
        {
            get => SessionState.GetBool(Prefix + "listening", false);
            set
            {
                if (value == Listening) return;
                SessionState.SetBool(Prefix + "listening", value);
                Error = ""; nextPoll = nextAnnouncement = 0;
                if (value) Poll(); else RemoveAnnouncement();
                Changed?.Invoke();
            }
        }
        internal static SceneAsset Destination
        {
            get => AssetDatabase.LoadAssetAtPath<SceneAsset>(Get("destination"));
            set
            {
                string path = value ? AssetDatabase.GetAssetPath(value) : "";
                if (path == Get("destination")) return;
                Set("destination", path); nextPoll = nextAnnouncement = 0;
                if (!value) RemoveAnnouncement();
                Changed?.Invoke();
            }
        }
        internal static string FilePath { get => Get("file"); set => Set("file", value); }
        internal static string LastReceived { get => Get("received"); set => Set("received", value); }
        internal static string Message { get => Get("message"); set => Set("message", value); }
        internal static string Error { get => Get("error"); set => Set("error", value); }
        internal static bool Pending { get => SessionState.GetBool(Prefix + "pending", false); set => SessionState.SetBool(Prefix + "pending", value); }
        static string Get(string key) => SessionState.GetString(Prefix + key, "");
        static void Set(string key, string value) => SessionState.SetString(Prefix + key, value ?? "");
        static string AppliedKey(Scene scene, string session) => "applied." + AssetDatabase.AssetPathToGUID(scene.path) + "." + session;
        internal static MapEditDocument LastApplied(Scene scene, string session)
        {
            string path = Get(AppliedKey(scene, session));
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? MapEditDocument.Read(path) : null;
        }
        internal static void RecordApplied(Scene scene, MapEditDocument document, string path) => Set(AppliedKey(scene, document.sessionId), path);
        internal static void ReportPull(string message)
        {
            Message = message;
            Changed?.Invoke();
        }
        static MapEditReceiverState()
        {
            EditorApplication.update += Poll;
            EditorApplication.quitting += RemoveAnnouncement;
        }
        internal static void Poll()
        {
            if (!Listening || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .1;
            try
            {
                var scene = Destination;
                if (!scene) { RemoveAnnouncement(); return; }
                if (EditorApplication.timeSinceStartup >= nextAnnouncement)
                {
                    var receiver = new MapEditReceiver { id = Id, project = Path.GetFileName(Path.GetDirectoryName(Application.dataPath)), scene = scene.name, updatedUtc = DateTime.UtcNow.ToString("O") };
                    MapEditDocument.WriteAtomic(Path.Combine(MapEditInbox.DirectoryPath, "receiver-" + Id + ".json"), JsonUtility.ToJson(receiver));
                    nextAnnouncement = EditorApplication.timeSinceStartup + 5;
                }
                string inbox = MapEditInbox.Inbox(Id);
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                MapEditPullServer.Poll(Id, scene);
                if (Directory.Exists(inbox))
                {
                    string newest = Directory.GetFiles(inbox, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    if (newest != null && newest != LastReceived)
                    {
                        LastReceived = FilePath = newest; Pending = true;
                        ApplyReceived(newest, scene);
                    }
                }
                if (!string.IsNullOrEmpty(Error) && Message.StartsWith("Receiver retrying:")) Message = "Receiving resumed.";
                Error = "";
            }
            catch (Exception error)
            {
                Error = error.Message;
                Message = "Receiver retrying: " + error.Message;
                RemoveAnnouncement();
                nextAnnouncement = 0;
            }
            // Overlay reads state on its own timer; do not repaint settings ten times a second.
        }
        internal static void ApplyReceived(string path, SceneAsset destination)
        {
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var scene = SceneManager.GetSceneByPath(AssetDatabase.GetAssetPath(destination));
                if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open the receiving scene in Unity first.");
                var document = MapEditDocument.Read(path);
                var previous = LastApplied(scene, document.sessionId);
                var delta = document;
                if (previous != null) Rebase(delta, previous);
                var lookup = MapEditCodec.Lookup(scene);
                // A deletion pulled back to the game is already satisfied in Unity.
                delta.changes.RemoveAll(c => c.kind == "existing" && c.after.deleted &&
                    (c.id.StartsWith("map:", StringComparison.Ordinal) || c.id.StartsWith("new:", StringComparison.Ordinal)) && !lookup.ContainsKey(c.id));
                var missing = delta.changes.Where(c => c.kind == "existing" &&
                    (c.id.StartsWith("map:", StringComparison.Ordinal) || c.id.StartsWith("new:", StringComparison.Ordinal)) && !lookup.ContainsKey(c.id)).ToList();
                if (missing.Count > 0) throw new InvalidOperationException(missing.Count + " objects were deleted or are missing in Unity. Pull from Unity to sync deletions before sending again. First: " + missing[0].after.name);
                if (delta.changes.Count > 0) MapEditImporter.Apply(delta, scene);
                RecordApplied(scene, document, path);
                Pending = false;
                Message = "Applied " + delta.changes.Count + " changes to " + scene.name + " in " + timer.ElapsedMilliseconds + " ms. Ctrl+Z to undo.";
                SceneView.RepaintAll();
            }
            catch (Exception error)
            {
                Pending = true;
                Message = "Not applied: " + error.Message + " Use ⋯ to review.";
            }
            Changed?.Invoke();
        }
        // Game sends cumulative session patches. Compare the next patch against the last
        // successfully applied one, while leaving editor-side conflict checks intact.
        static void Rebase(MapEditDocument delta, MapEditDocument previous)
        {
            var incoming = delta.changes.ToDictionary(c => c.id);
            foreach (var old in previous.changes)
            {
                if (incoming.TryGetValue(old.id, out var anchored) && anchored.baselineFromUnity &&
                    (!old.baselineFromUnity || !MapEditCodec.Same(anchored.before, old.before))) continue;
                if (!incoming.TryGetValue(old.id, out var change))
                {
                    if (old.after.deleted) continue;
                    change = JsonUtility.FromJson<MapEditChange>(JsonUtility.ToJson(old));
                    change.after = old.kind == "existing" ? old.before : JsonUtility.FromJson<MapEditState>(JsonUtility.ToJson(old.after));
                    if (old.kind != "existing") change.after.deleted = true;
                    delta.changes.Add(change);
                }
                if (MapEditCodec.Same(change.after, old.after)) { delta.changes.Remove(change); continue; }
                if (old.after.deleted) throw new InvalidOperationException("A previously deleted object was restored in the game. Undo its deletion in Unity before sending again.");
                change.kind = "existing";
                change.sourceId = change.id;
                change.sourcePath = old.sourcePath ?? old.after.name;
                change.before = old.after;
            }
        }
        internal static void RemoveAnnouncement()
        {
            string path = Path.Combine(MapEditInbox.DirectoryPath, "receiver-" + Id + ".json");
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception error) { Error = error.Message; }
        }
    }
}
