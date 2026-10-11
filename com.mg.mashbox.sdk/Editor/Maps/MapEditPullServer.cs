using System;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.Editing;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public static class MapEditPullServer
    {
        public static MapEditDocument Capture(MapEditDocument request, Scene scene)
        {
            request.Validate();
            if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open the chosen destination scene in Unity Edit Mode before pulling.");
            var lookup = MapEditCodec.Lookup(scene);
            var ids = MapEditCodec.StableIds(scene);
            var lastApplied = MapEditReceiverState.LastApplied(scene, request.sessionId)?.changes.ToDictionary(c => c.id);
            if (!request.changes.Any(c => c.id.StartsWith("map:", StringComparison.Ordinal) && lookup.ContainsKey(c.id)))
                throw new InvalidOperationException("The destination scene does not share this map's prepared identities.");
            var response = new MapEditDocument { sessionId = request.sessionId, sceneName = scene.name, scenePath = scene.path, exportedUtc = DateTime.UtcNow.ToString("O") };
            foreach (var item in request.changes)
            {
                bool created = item.id.StartsWith("new:", StringComparison.Ordinal);
                if (item.kind != "existing" || !(created || item.id.StartsWith("map:", StringComparison.Ordinal))) throw new InvalidOperationException("Unsupported pull identity.");
                // Runtime duplicates acquire shared identities when imported into Unity.
                // A never-sent runtime object must not be mistaken for an editor deletion.
                if (created && !lookup.ContainsKey(item.id) && (lastApplied == null || !lastApplied.ContainsKey(item.id))) continue;
                MapEditState editor;
                var baseline = item.before;
                // A send already accepted by Unity is the common state of both sides.
                // Keep a newer pull anchor when the game has subsequently pulled again.
                if (lastApplied != null && lastApplied.TryGetValue(item.id, out var sent) &&
                    (MapEditCodec.Same(item.before, sent.before) || MapEditCodec.Same(item.before, sent.after)))
                    baseline = sent.after;
                if (lookup.TryGetValue(item.id, out var target) && target)
                    editor = MapEditCodec.Capture(target, t => ids.TryGetValue(t, out var id) ? id : MapEditCodec.PathOf(t), item.before.hasMesh);
                else { editor = JsonUtility.FromJson<MapEditState>(JsonUtility.ToJson(item.before)); editor.deleted = true; }
                if (MapEditCodec.Same(editor, baseline) &&
                    (MapEditCodec.Same(item.before, editor) || !MapEditCodec.Same(item.after, editor))) continue; // Preserve game-only edits; acknowledge already-sent edits.
                if (!MapEditCodec.Same(item.after, baseline) && !MapEditCodec.Same(item.after, editor))
                    throw new InvalidOperationException("Both Unity and the game changed " + item.before.name + ". Resolve that object before pulling; nothing was applied.");
                response.changes.Add(new MapEditChange { id = item.id, sourceId = item.id, sourcePath = item.sourcePath, before = item.after, after = editor });
            }
            response.Validate();
            return response;
        }
        internal static void Poll(string receiverId, SceneAsset destination)
        {
            string directory = MapEditPull.DirectoryFor(receiverId);
            if (!Directory.Exists(directory)) return;
            // One request per editor tick; do not monopolize the UI with queued snapshots.
            string path = Directory.GetFiles(directory, "*.request.json").OrderBy(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (path == null) return;
            var reply = new MapEditPullReply();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var request = MapEditDocument.Read(path);
                if (!DateTime.TryParse(request.exportedUtc, out var sent) || DateTime.UtcNow - sent.ToUniversalTime() > TimeSpan.FromMinutes(1))
                    throw new InvalidOperationException("Pull request expired. Try again.");
                reply.changes = Capture(request, SceneManager.GetSceneByPath(AssetDatabase.GetAssetPath(destination)));
            }
            catch (Exception error) { reply.error = error.Message; }
            MapEditDocument.WriteAtomic(MapEditPull.ReplyPath(path), JsonUtility.ToJson(reply));
            File.Delete(path);
            MapEditReceiverState.ReportPull(!string.IsNullOrEmpty(reply.error) ? "Pull blocked: " + reply.error :
                "Sent scene snapshot to game: " + reply.changes.changes.Count + " objects in " + timer.ElapsedMilliseconds + " ms.");
        }
    }
}
