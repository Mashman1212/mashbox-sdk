using System;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.Editing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    public sealed class MapEditReceiverWindow : EditorWindow
    {
        SceneAsset destination { get => MapEditReceiverState.Destination; set => MapEditReceiverState.Destination = value; }
        string file { get => MapEditReceiverState.FilePath; set => MapEditReceiverState.FilePath = value; }
        bool listening { get => MapEditReceiverState.Listening; set => MapEditReceiverState.Listening = value; }
        bool pendingReview { get => MapEditReceiverState.Pending; set => MapEditReceiverState.Pending = value; }
        string lastReceivedFile => MapEditReceiverState.LastReceived;
        string message { get => MapEditReceiverState.Message; set => MapEditReceiverState.Message = value; }
        string report = "", reportFile;
        Vector2 scroll;
        [MenuItem("Tools/MashBox/Map Editing/Receive Runtime Changes")]
        public static void Open() => GetWindow<MapEditReceiverWindow>("Runtime Map Changes");
        void OnEnable()
        {
            MapEditReceiverState.Changed += ReceiverChanged;
            if (!destination && !string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                destination = AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneManager.GetActiveScene().path);
        }
        void OnDisable() => MapEditReceiverState.Changed -= ReceiverChanged;
        void ReceiverChanged()
        {
            if (reportFile != file) { report = ""; reportFile = file; }
            Repaint();
        }
        Scene Destination()
        {
            if (!destination) throw new InvalidOperationException("Choose a destination scene.");
            string path = AssetDatabase.GetAssetPath(destination);
            Scene scene = SceneManager.GetSceneByPath(path);
            return scene.IsValid() && scene.isLoaded ? scene : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Runtime map changes", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Choose the source map scene (or a copy). Receiving ON automatically applies incoming changes. Imports are undoable and leave the scene dirty for saving. Source conflicts stop the entire import.", MessageType.Info);
            destination = (SceneAsset)EditorGUILayout.ObjectField("Destination scene", destination, typeof(SceneAsset), false);
            listening = EditorGUILayout.Toggle("Receive from running game", listening);
            EditorGUILayout.HelpBox("Receiving stays enabled when this window is closed and across script reloads. Turn it off here to stop.", MessageType.None);
            EditorGUILayout.LabelField("JSON file");
            EditorGUILayout.BeginHorizontal(); file = EditorGUILayout.TextField(file);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                string chosen = EditorUtility.OpenFilePanel("Runtime map edits", "", "json");
                if (!string.IsNullOrEmpty(chosen)) { file = chosen; report = ""; }
            }
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || !destination || !File.Exists(file)))
            {
                if (GUILayout.Button("Review changes and check conflicts")) Run(false);
                if (GUILayout.Button("Apply changes to destination scene")) Run(true);
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
        void Run(bool apply)
        {
            try
            {
                var data = MapEditDocument.Read(file); var scene = Destination();
                var conflicts = MapEditImporter.Conflicts(data, scene);
                report = "From " + data.sceneName + "\n" + data.changes.Count + " changed objects\n\n" +
                    string.Join("\n", data.changes.Select(c => (c.after.deleted ? "Delete" : c.kind == "existing" ? "Edit" : "Create") + "  " + c.after.name)) +
                    (conflicts.Count == 0 ? "\n\nNo source conflicts." : "\n\nCONFLICTS\n" + string.Join("\n", conflicts));
                if (!apply || conflicts.Count != 0) { message = conflicts.Count == 0 ? "Ready to apply." : "Resolve the listed source conflicts before importing."; return; }
                MapEditImporter.Apply(data, scene);
                MapEditReceiverState.RecordApplied(scene, data, file);
                if (file == lastReceivedFile) pendingReview = false;
                message = "Applied to " + scene.name + ". Review the scene and save. Ctrl+Z undoes the import.";
            }
            catch (Exception error) { message = error.Message; Debug.LogException(error); }
        }

        // Read existing receiver state; never create a receiver or touch disk during overlay repaint.
        internal static UnityEngine.UIElements.VisualElement CreateStatusIndicator()
        {
            var panel = new UnityEngine.UIElements.VisualElement();
            panel.style.flexShrink = 0;
            panel.style.paddingBottom = 3;
            var row = new UnityEngine.UIElements.VisualElement();
            row.style.flexDirection = UnityEngine.UIElements.FlexDirection.Row;
            var button = new UnityEngine.UIElements.Button();
            button.name = "receiving-toggle";
            button.style.flexGrow = 1;
            button.style.minWidth = 0;
            button.style.height = 24;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            var options = new UnityEngine.UIElements.Button(Open) { text = "⋯", name = "receiver-options" };
            options.style.width = 26;
            options.style.height = 24;
            options.style.flexShrink = 0;
            var destinationLabel = new UnityEngine.UIElements.Label();
            destinationLabel.style.fontSize = 11;
            destinationLabel.style.marginLeft = 6;
            destinationLabel.style.whiteSpace = UnityEngine.UIElements.WhiteSpace.Normal;
            row.Add(button); row.Add(options);
            panel.Add(row); panel.Add(destinationLabel);
            void Refresh()
            {
                bool available = true;
                bool error = available && !string.IsNullOrEmpty(MapEditReceiverState.Error);
                bool pending = available && MapEditReceiverState.Pending;
                bool enabled = available && MapEditReceiverState.Listening;
                bool ready = enabled && MapEditReceiverState.Destination;
                button.text = !enabled ? "○ Receiving off" : error ? "● Receiver retrying" : ready ? "● Receiving · Waiting" : "● Receiving · Choose scene";
                button.style.color = !enabled ? new Color(.75f, .75f, .75f) : error ? new Color(1f, .45f, .35f) : !ready ? new Color(1f, .72f, .3f) : new Color(.45f, .85f, .55f);
                destinationLabel.text = MapEditReceiverState.Destination ? "To: " + MapEditReceiverState.Destination.name : "";
                string message = MapEditReceiverState.Message;
                if (!string.IsNullOrEmpty(message))
                {
                    string summary = message.Split('\n')[0];
                    if (summary.Length > 160) summary = summary.Substring(0, 157) + "…";
                    if (summary.Length < message.Length) summary += "\nDetails in ⋯";
                    destinationLabel.text += (destinationLabel.text.Length > 0 ? "\n" : "") + summary;
                }
                destinationLabel.tooltip = message;
                destinationLabel.style.display = string.IsNullOrEmpty(destinationLabel.text) ? UnityEngine.UIElements.DisplayStyle.None : UnityEngine.UIElements.DisplayStyle.Flex;
                button.tooltip = enabled ? "Click to turn receiving off." : "Click to turn receiving on.";
                if (error) button.tooltip += "\n" + MapEditReceiverState.Error;
                if (!MapEditReceiverState.Destination) button.tooltip += "\nUse ⋯ to choose a destination scene.";
                options.tooltip = pending ? "Review changes that could not be applied and receiver settings." : "Receiver settings and destination scene";
                options.style.color = pending ? new Color(1f, .72f, .3f) : new Color(.75f, .75f, .75f);
                if (available && MapEditReceiverState.Destination) button.tooltip += "\n" + AssetDatabase.GetAssetPath(MapEditReceiverState.Destination);
            }
            button.clicked += () =>
            {
                if (!MapEditReceiverState.Listening && !MapEditReceiverState.Destination)
                {
                    string path = SceneManager.GetActiveScene().path;
                    if (!string.IsNullOrEmpty(path)) MapEditReceiverState.Destination = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                }
                MapEditReceiverState.Listening = !MapEditReceiverState.Listening;
                Refresh();
            };
            Refresh();
            var update = panel.schedule.Execute(Refresh).Every(500);
            panel.RegisterCallback<UnityEngine.UIElements.AttachToPanelEvent>(_ => { update.Resume(); Refresh(); });
            panel.RegisterCallback<UnityEngine.UIElements.DetachFromPanelEvent>(_ => update.Pause());
            return panel;
        }

        [MenuItem("MashBox/Map Tools/Prepare Active Scene for Runtime Editing")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Prepare the source scene in Edit Mode.");
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Save this source scene before preparing it.");
            var identity = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapEditSceneIdentity>(true)).FirstOrDefault();
            if (!identity)
            {
                var go = new GameObject("Map edit identities"); SceneManager.MoveGameObjectToScene(go, scene);
                identity = go.AddComponent<MapEditSceneIdentity>(); Undo.RegisterCreatedObjectUndo(go, "Prepare map identities");
            }
            Undo.RecordObject(identity, "Prepare map identities");
            var saved = identity.objects.Where(i => i.target).ToDictionary(i => i.target, i => i.id);
            identity.objects.Clear();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var target in root.GetComponentsInChildren<Transform>(true))
                {
                    if (MapEditSession.IsGenerated(target)) continue;
                    identity.objects.Add(new MapEditSceneIdentity.Item { target = target,
                        id = saved.TryGetValue(target, out string id) ? id : "map:" + Guid.NewGuid().ToString("N") });
                    Undo.RecordObject(target.gameObject, "Prepare runtime map editing");
                    GameObjectUtility.SetStaticEditorFlags(target.gameObject, GameObjectUtility.GetStaticEditorFlags(target.gameObject) & ~StaticEditorFlags.BatchingStatic);
                    var filter = target.GetComponent<MeshFilter>();
                    if (filter && filter.sharedMesh) RuntimeMeshCombiner.EnableReadWriteOnSerializedMesh(filter.sharedMesh);
                    var loft = target.GetComponent<MashBoxSDK.Maps.Spline.MultiSplineLoft>();
                    if (loft && loft.GeneratedMesh) RuntimeMeshCombiner.EnableReadWriteOnSerializedMesh(loft.GeneratedMesh);
                    var collider = target.GetComponent<MeshCollider>();
                    if (collider && collider.sharedMesh) RuntimeMeshCombiner.EnableReadWriteOnSerializedMesh(collider.sharedMesh);
                }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorUtility.SetDirty(identity);
            Debug.Log("Prepared scene objects for runtime editing. Save and export the map; keep this authoring scene as the import destination.");
        }
    }
}
