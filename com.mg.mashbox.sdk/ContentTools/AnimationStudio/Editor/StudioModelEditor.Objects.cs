using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal bool CanDeleteObjects => Document && mode == ComponentMode.Object && objects.Count > 0;
        internal void DeleteSelectedObjects()
        {
            if (!CanDeleteObjects) return;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Document.DeleteObjects(objects);
            Undo.CollapseUndoOperations(group);
            CancelMarkingMenu(); EndPivotSnap(); StopCutTool(); pivot = false; marquee = false; gestureUndo = -1;
            objects.Clear(); Components.Clear(); Active = -1; GUIUtility.hotControl = 0;
            Rebuild(); Changed();
        }
        internal bool HandleObjectDelete()
        {
            var evt = Event.current;
            if (evt == null || EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0 || cutTool != CutTool.None) return false;
            bool key = StudioShortcuts.Pressed(StudioShortcut.Delete,evt);
            bool command = (evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand) && (evt.commandName == "Delete" || evt.commandName == "SoftDelete");
            if (!key && !command) return false;
            if (evt.type != EventType.ValidateCommand)
            {
                if (CanDeleteObjects) Run(DeleteSelectedObjects);
                else if (mode != ComponentMode.Object) { error = "Switch to Object mode to delete objects."; repaint(); }
            }
            // Never let Unity delete an unrelated selection from the main scene.
            evt.Use(); return true;
        }
        internal bool HandleModelDrop(SceneView view, Action<StudioModelDocument> loaded)
        {
            var evt = Event.current;
            if (evt == null || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)) return false;
            if (DragAndDrop.GetGenericData(HierarchyDragKey) is HierarchyDragPayload) return false;
            if (DragAndDrop.objectReferences.Any(o => o is Material)) return HandleMaterialDrop(view);
            // The workspace ObjectField retains its normal document-asset drop behavior.
            if (!view && DragAndDrop.objectReferences.Any(o => o is StudioModelDocument)) return false;
            var sources = ModelDropSources(DragAndDrop.objectReferences);
            var covered = new HashSet<string>(sources.Select(AssetDatabase.GetAssetPath), StringComparer.OrdinalIgnoreCase);
            var paths = DragAndDrop.paths.Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) && !covered.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            bool supported = sources.Length > 0 || paths.Length > 0;
            DragAndDrop.visualMode = supported ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (evt.type == EventType.DragPerform && supported)
            {
                DragAndDrop.AcceptDrag();
                // Snapshot payload before importing/rebuilding changes Unity's drag state.
                Run(() => {
                    foreach (var source in sources) ImportDroppedSource(source, loaded);
                    foreach (var path in paths) ImportPath(path, loaded);
                    if (view) Frame(view);
                });
            }
            evt.Use(); return true;
        }
        internal static Object[] ModelDropSources(Object[] references)
        {
            var sources = references.Select(o => o is Component c ? c.gameObject : o).Where(o => o is GameObject || o is Mesh).Distinct().ToArray();
            return sources.Where(o => !(o is GameObject child) || !sources.OfType<GameObject>().Any(parent => parent != child && child.transform.IsChildOf(parent.transform))).ToArray();
        }
        internal void ImportDroppedSource(Object source, Action<StudioModelDocument> loaded)
        {
            if (source is GameObject go) { ImportModel(go, loaded); return; }
            if (!(source is Mesh mesh)) throw new ArgumentException("Drop a model, prefab, or mesh asset.");
            var holder = new GameObject(mesh.name) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                holder.AddComponent<MeshFilter>().sharedMesh = mesh;
                var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                holder.AddComponent<MeshRenderer>().sharedMaterial = pipeline ? pipeline.defaultMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                ImportModel(holder, loaded);
            }
            finally { Object.DestroyImmediate(holder); }
        }
    }
}
