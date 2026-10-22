using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioUVEditor : EditorWindow
    {
        private StudioModelEditor owner;
        private Mesh mesh;
        private int channel, destination = 1, tool;
        private float zoom = 1;
        private Vector2 pan, start, end;
        private Rect canvas;
        private bool selecting, dragging, panning, add, subtract;
        private int undoGroup = -1;
        private int canvasControl;
        private Vector2[] original;
        private Vector2 center;
        private readonly HashSet<int> selection = new HashSet<int>();
        private Texture2D texture;
        private Vector2 offset, scale = Vector2.one;
        private float angle;
        private static readonly string[] Channels = Enumerable.Range(0, 8).Select(i => "UV " + i).ToArray();
        internal static void Open(StudioModelEditor editor, int selectedChannel = -1)
        {
            var window = GetWindow<StudioUVEditor>("UV Editor"); window.owner = editor; window.minSize = new Vector2(780, 500); window.Show();
            if (selectedChannel >= 0) { window.channel = Mathf.Clamp(selectedChannel, 0, 7); window.CancelInteraction(); window.selection.Clear(); window.pinned.Clear(); window.Repaint(); }
            window.Repaint();
        }
        private void OnEnable() { Undo.undoRedoPerformed += Refresh; }
        private void OnDisable() { CancelInteraction(); Undo.undoRedoPerformed -= Refresh; }
        private void Refresh() { CancelInteraction(); selection.Clear(); pinned.Clear(); Repaint(); }
        private void OnInspectorUpdate()
        {
            var selectedMesh = owner != null && owner.Document ? owner.ActiveMesh : null;
            if (mesh != selectedMesh) Repaint();
        }
        private List<Vector2> Read() { var result = new List<Vector2>(); if (mesh) mesh.GetUVs(channel, result); return result; }
        private void Write(IList<Vector2> uv, string label) { StudioModelDocument.SetUVs(mesh, channel, uv, label); owner.Changed(); Repaint(); }
        private void CancelInteraction()
        {
            if (undoGroup >= 0) Undo.CollapseUndoOperations(undoGroup);
            if (canvasControl != 0 && GUIUtility.hotControl == canvasControl) GUIUtility.hotControl = 0;
            undoGroup = -1; original = null; dragging = selecting = panning = false;
        }
        private void OnGUI() { using (StudioTheme.Scope()) { EditorGUI.DrawRect(new Rect(Vector2.zero, position.size), StudioTheme.Current.Panel); DrawThemedGUI(); } }
        private void DrawThemedGUI()
        {
            owner?.HandleSaveShortcut();
            if (Event.current.type == EventType.KeyDown && !EditorGUIUtility.editingTextField && GUIUtility.hotControl == 0)
            {
                if (StudioShortcuts.Pressed(StudioShortcut.Undo,Event.current)) { Undo.PerformUndo(); Event.current.Use(); }
                else if (StudioShortcuts.Pressed(StudioShortcut.Redo,Event.current)) { Undo.PerformRedo(); Event.current.Use(); }
                else if (StudioShortcuts.Pressed(StudioShortcut.Preferences,Event.current)) { StudioControlsPreferences.Open(); Event.current.Use(); }
            }
            if(owner != null && owner.HasBlendShape) { EditorGUILayout.HelpBox("Bake Current Pose before editing blend shape UVs.",MessageType.Info);return; }
            var selectedMesh = owner != null && owner.Document ? owner.ActiveMesh : null;
            if (mesh != selectedMesh)
            {
                CancelInteraction(); mesh = selectedMesh; selection.Clear(); pinned.Clear(); feedback = "";
            }
            var uv = Read();
            selection.RemoveWhere(i => i < 0 || i >= uv.Count);
            pinned.RemoveWhere(i => i < 0 || i >= uv.Count);
            using (new EditorGUILayout.HorizontalScope(StudioTheme.Styles.toolbar))
            {
                int next = EditorGUILayout.Popup(channel, Channels, GUILayout.Width(72));
                if (next != channel) { CancelInteraction(); channel = next; selection.Clear(); pinned.Clear(); uv = Read(); }
                GUILayout.Label(mesh ? mesh.name : "Select a mesh", StudioTheme.Styles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Frame All", StudioTheme.Styles.toolbarButton)) FrameUVs(uv, false);
                if (GUILayout.Button("0–1", StudioTheme.Styles.toolbarButton, GUILayout.Width(34))) { zoom = 1; pan = Vector2.zero; }
                if (GUILayout.Button("Settings", StudioTheme.Styles.toolbarButton, GUILayout.Width(64))) StudioControlsPreferences.Open();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    canvas = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    EditorGUI.DrawRect(canvas, StudioTheme.Current.Canvas);
                    bool exists = mesh && uv.Count == mesh.vertexCount && uv.Count > 0;
                    DrawUVs(exists ? uv : new List<Vector2>());
                    if (!exists)
                        GUI.Label(new Rect(canvas.x, canvas.center.y - 24, canvas.width, 48), mesh ? "This UV set is empty.\nCreate a set or use a projection in the toolkit." : "Select a mesh in MashBox Studio to view and edit its UVs.", StudioTheme.Styles.centeredGreyMiniLabel);
                    Input(exists ? uv : new List<Vector2>());
                }
                DrawToolkit(uv);
            }
        }
        private void TransformSelection(List<Vector2> uv, System.Func<Vector2, Vector2> transform, string label)
        { foreach (int i in selection) uv[i] = transform(uv[i]); Write(uv, label); }
        private float Unit => Mathf.Max(10, Mathf.Min(canvas.width, canvas.height) - 70) * zoom;
        private Vector2 ToScreen(Vector2 uv) => canvas.center + pan + new Vector2(uv.x - .5f, .5f - uv.y) * Unit;
        private Vector2 ToUV(Vector2 screen) { var p = (screen - canvas.center - pan) / Unit; return new Vector2(p.x + .5f, .5f - p.y); }
        private static Vector2 Rotate(Vector2 value, float radians) => new Vector2(Mathf.Cos(radians) * value.x - Mathf.Sin(radians) * value.y, Mathf.Sin(radians) * value.x + Mathf.Cos(radians) * value.y);
        private void DrawUVs(List<Vector2> uv)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.BeginClip(canvas);
            Vector2 origin = canvas.position;
            var topLeft = ToScreen(new Vector2(0, 1)) - origin;
            if (showTexture && texture) GUI.DrawTexture(new Rect(topLeft, Vector2.one * Unit), texture, ScaleMode.StretchToFill);
            Handles.BeginGUI();
            if (showGrid) DrawGrid(origin);
            Handles.color = StudioTheme.Alpha(StudioTheme.Current.UVWire, .75f); var tr = mesh && uv.Count == mesh.vertexCount ? mesh.triangles : System.Array.Empty<int>();
            for (int i = 0; i < tr.Length; i += 3) for (int j = 0; j < 3; j++) Handles.DrawLine(ToScreen(uv[tr[i + j]]) - origin, ToScreen(uv[tr[i + (j + 1) % 3]]) - origin);
            Handles.EndGUI();
            float pointSize = StudioSettings.Current.uvPointSize;
            var palette = StudioTheme.Current;
            for (int i = 0; i < uv.Count; i++) { var p = ToScreen(uv[i]) - origin; EditorGUI.DrawRect(new Rect(p.x - pointSize / 2, p.y - pointSize / 2, pointSize, pointSize), selection.Contains(i) ? palette.UVSelected : pinned.Contains(i) ? new Color(.95f, .4f, .5f) : palette.UVWire); }
            if (selecting) { var r = Rect.MinMaxRect(Mathf.Min(start.x, end.x), Mathf.Min(start.y, end.y), Mathf.Max(start.x, end.x), Mathf.Max(start.y, end.y)); r.position -= origin; EditorGUI.DrawRect(r, StudioTheme.Alpha(palette.Accent, .18f)); GUI.Box(r, GUIContent.none); }
            GUI.EndClip();
        }
        private void Input(List<Vector2> uv)
        {
            var e = Event.current; int id = GUIUtility.GetControlID("StudioUVCanvas".GetHashCode(), FocusType.Passive);
            canvasControl = id;
            if (e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField)
            {
                if (StudioShortcuts.Pressed(StudioShortcut.Select,e)) tool = 0; else if (StudioShortcuts.Pressed(StudioShortcut.Move,e)) tool = 1; else if (StudioShortcuts.Pressed(StudioShortcut.Rotate,e)) tool = 2; else if (StudioShortcuts.Pressed(StudioShortcut.Scale,e)) tool = 3;
                else if (StudioShortcuts.Pressed(StudioShortcut.SelectAll,e)) { for (int i=0;i<uv.Count;i++) selection.Add(i); }
                else if (StudioShortcuts.Pressed(StudioShortcut.Frame,e)) FrameUVs(uv, selection.Count > 0); else if (StudioShortcuts.Pressed(StudioShortcut.FrameAllUV,e)) FrameUVs(uv, false);
                else if (StudioShortcuts.Pressed(StudioShortcut.Cancel,e) && (dragging || selecting))
                { if (dragging && undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup); dragging = selecting = false; GUIUtility.hotControl = 0; undoGroup = -1; }
                else { e.Use(); return; }
                e.Use(); Repaint();
            }
            if (canvas.Contains(e.mousePosition) && e.type == EventType.ScrollWheel)
            {
                var anchor = ToUV(e.mousePosition); zoom = Mathf.Clamp(zoom * Mathf.Exp(-e.delta.y * .08f), .01f, 30); pan += e.mousePosition - ToScreen(anchor); e.Use(); Repaint();
            }
            if (canvas.Contains(e.mousePosition) && e.type == EventType.MouseDown && StudioShortcuts.Matches(StudioShortcut.Pan,e))
            { panning = true; GUIUtility.hotControl = id; e.Use(); }
            if (panning && e.type == EventType.MouseDrag) { pan += e.delta; e.Use(); Repaint(); }
            if (canvas.Contains(e.mousePosition) && e.type == EventType.MouseDown && e.button == 0)
            {
                start = end = e.mousePosition; add = e.shift; subtract = e.control || e.command;
                var hits = Enumerable.Range(0, uv.Count).Where(i => Vector2.Distance(ToScreen(uv[i]), start) < 7).ToArray();
                if (hits.Length > 0)
                {
                    var picked = ExpandPicked(hits);
                    if (!add && !subtract && !picked.Any(selection.Contains)) selection.Clear();
                    foreach (int i in picked) { if (subtract) selection.Remove(i); else selection.Add(i); }
                    if (tool > 0 && !add && !subtract && selection.Count > 0)
                    {
                        dragging = true; original = uv.ToArray(); center = selection.Aggregate(Vector2.zero, (sum, i) => sum + uv[i]) / selection.Count;
                        Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.RegisterCompleteObjectUndo(mesh, "Transform UVs");
                    }
                }
                else { selecting = true; if (!add && !subtract) selection.Clear(); }
                GUIUtility.hotControl = id; e.Use(); Repaint();
            }
            if (GUIUtility.hotControl == id && e.type == EventType.MouseDrag && !panning)
            {
                end = e.mousePosition;
                if (dragging)
                {
                    var values = (Vector2[])original.Clone(); var delta = ToUV(end) - ToUV(start);
                    float radians = (end.x - start.x) * .01f; float factor = Mathf.Exp((end.x - start.x) * .01f);
                    foreach (int i in selection) values[i] = tool == 1 ? original[i] + delta : tool == 2 ? center + Rotate(original[i] - center, radians) : center + (original[i] - center) * factor;
                    mesh.SetUVs(channel, values); EditorUtility.SetDirty(mesh); owner.Changed();
                }
                e.Use(); Repaint();
            }
            if (GUIUtility.hotControl == id && e.type == EventType.MouseUp)
            {
                if (selecting)
                {
                    end = e.mousePosition; var rect = Rect.MinMaxRect(Mathf.Min(start.x, end.x), Mathf.Min(start.y, end.y), Mathf.Max(start.x, end.x), Mathf.Max(start.y, end.y));
                    var picked = ExpandPicked(Enumerable.Range(0, uv.Count).Where(i => rect.Contains(ToScreen(uv[i]))));
                    foreach (int i in picked) { if (subtract) selection.Remove(i); else selection.Add(i); }
                }
                if (undoGroup >= 0) Undo.CollapseUndoOperations(undoGroup);
                undoGroup = -1; dragging = selecting = panning = false; GUIUtility.hotControl = 0; e.Use(); Repaint();
            }
        }
        private void SelectShell()
        {
            if (!mesh || selection.Count == 0) return;
            foreach (var shell in StudioUVTools.Shells(mesh, Read()))
                if (shell.Any(selection.Contains)) selection.UnionWith(shell);
            Repaint();
        }
        private HashSet<int> ExpandPicked(IEnumerable<int> indices)
        {
            var picked = new HashSet<int>(indices);
            if (shellSelection && mesh && picked.Count > 0)
                foreach (var shell in StudioUVTools.Shells(mesh, Read())) if (shell.Any(picked.Contains)) picked.UnionWith(shell);
            return picked;
        }
    }
}
