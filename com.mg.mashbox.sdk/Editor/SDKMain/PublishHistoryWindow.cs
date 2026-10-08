#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.SDKMain
{
    public sealed class PublishHistoryWindow : EditorWindow
    {
        private PublishHistory.Entry[] entries = Array.Empty<PublishHistory.Entry>();
        private string selectedId, error, search = "";
        private bool refreshing;
        private double nextRefresh;
        private Vector2 scroll;

        [MenuItem("MashBox/My Publishes")]
        public static void Open() => GetWindow<PublishHistoryWindow>("My Publishes");
        private void OnEnable() { Reload(); EditorApplication.update += Tick; }
        private void OnDisable() { EditorApplication.update -= Tick; }
        private void Reload()
        {
            try { entries = PublishHistory.Read(); selectedId ??= entries.FirstOrDefault()?.jobId; }
            catch (Exception) { error = "Saved publish history could not be read. The existing file has been preserved."; }
        }
        private void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextRefresh || refreshing) return;
            nextRefresh = EditorApplication.timeSinceStartup + 20;
            Reload(); RefreshSelected();
        }
        private async void RefreshSelected()
        {
            var entry = entries.FirstOrDefault(e => e.jobId == selectedId);
            if (refreshing || entry == null || !entry.trackingSupported) return;
            refreshing = true; error = null;
            try
            {
                var status = await PublisherService.GetPublishStatusAsync(entry.jobId, entry.token);
                PublishHistory.Update(entry.jobId, status);
                Reload();
            }
            catch (Exception) { error = "Status could not be refreshed. Any displayed result is from the last successful check."; }
            finally { refreshing = false; if (this != null) Repaint(); }
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("My Publishes", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("History of uploads started with this SDK on this computer/user. Select a publish to check its queue and processing attempts. The selected publish refreshes every 20 seconds.", MessageType.Info);
            search = EditorGUILayout.TextField("Search", search);
            using (new EditorGUI.DisabledScope(refreshing))
                if (GUILayout.Button(refreshing ? "Checking..." : "Refresh selected publish")) { Reload(); RefreshSelected(); }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (entries.Length == 0) EditorGUILayout.LabelField("Your next publish will appear here automatically.");
            foreach (var entry in entries.Where(e => string.IsNullOrEmpty(search) ||
                         (e.fileName + " " + e.game + " " + e.jobId).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Button(entry.fileName + " | " + Platform(entry.container) + " | " +
                        (entry.lastStatus?.status ?? "Status not checked"), entry.jobId == selectedId ? EditorStyles.boldLabel : EditorStyles.label))
                    { selectedId = entry.jobId; error = null; nextRefresh = 0; }
                    EditorGUILayout.LabelField(entry.game + " / " + entry.region.ToUpperInvariant() + " / " + entry.createdUtc, EditorStyles.miniLabel);
                    if (entry.jobId != selectedId) continue;
                    EditorGUILayout.LabelField("Publish ID");
                    EditorGUILayout.SelectableLabel(entry.jobId, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    if (GUILayout.Button("Copy publish ID")) EditorGUIUtility.systemCopyBuffer = entry.jobId;
                    if (!entry.trackingSupported)
                    { EditorGUILayout.HelpBox("The upload service did not support tracking for this publish. Keep this ID for reference.", MessageType.Info); continue; }
                    var state = entry.lastStatus;
                    if (state == null) continue;
                    EditorGUILayout.LabelField("Last checked (UTC)", state.utc);
                    if (!string.IsNullOrEmpty(state.message)) EditorGUILayout.HelpBox(state.message, MessageType.Warning);
                    if (state.queuePosition > 0) EditorGUILayout.LabelField("Estimated queue position", state.queuePosition + " of " + state.queueTotal);
                    if (state.queueTruncated) EditorGUILayout.HelpBox("Queue listing is partial; position is unavailable.", MessageType.Info);
                    if (state.status == "Queued" || state.status == "Waiting for PC" || state.status == "Claimed")
                        EditorGUILayout.HelpBox("Queue positions are estimates within this platform and region. Other workers, claimed items and PC dependencies can change the order. Claimed does not prove Unity is making progress.", MessageType.Info);
                    foreach (var attempt in state.attempts ?? Array.Empty<PublishHistory.Attempt>())
                    {
                        EditorGUILayout.LabelField(attempt.outcome + " / " + attempt.stage, EditorStyles.boldLabel);
                        EditorGUILayout.SelectableLabel("Attempt: " + attempt.runId, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                        EditorGUILayout.LabelField("Updated (UTC)", attempt.updatedUtc);
                        if (!string.IsNullOrEmpty(attempt.failureStage)) EditorGUILayout.LabelField("Failure stage", attempt.failureStage);
                        if (!string.IsNullOrEmpty(attempt.modId)) EditorGUILayout.LabelField("mod.io item ID", attempt.modId);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
        private static string Platform(string container) => container == "inbox-windows" ? "PC" :
            container == "inbox-xbox" ? "Xbox Series" : container == "inbox-ps5" ? "PlayStation 5" : container;
    }
}
#endif
