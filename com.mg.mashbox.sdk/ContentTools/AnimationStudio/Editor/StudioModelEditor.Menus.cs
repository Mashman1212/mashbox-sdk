using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal void DrawMenuBar(SceneView view, Action<StudioModelDocument> loaded)
        {
            using (new EditorGUILayout.HorizontalScope(GUIStyle.none))
            {
                MenuButton("File", menu => {
                    menu.AddItem(new GUIContent("Open Workspace…"), false, () => Run(() => OpenWorkspace(loaded)));
                    menu.AddItem(new GUIContent("Import FBX…"), false, () => Run(() => ImportPath(EditorUtility.OpenFilePanel("Import FBX into workspace", "", "fbx"), loaded)));
                    menu.AddSeparator("");
                    AddCommand(menu, "Save Workspace\tCtrl+S", Document, Save);
                    AddCommand(menu, "Save Workspace As…\tCtrl+Shift+S", Document, SaveAs);
                    menu.AddSeparator("");
                    AddCommand(menu, "Export All… (FBX)", Document && Document.nodes.Count > 0, Export);
                    AddCommand(menu, "Export Selection… (FBX)", CanExportSelection, () => Export(true));
                });
                MenuButton("Edit", menu => {
                    AddCommand(menu, "Undo\tCtrl+Z", Document, Undo.PerformUndo); AddCommand(menu, "Redo\tCtrl+Y", Document, Undo.PerformRedo);
                    menu.AddSeparator(""); AddCommand(menu, "Duplicate\tCtrl+D", CanDuplicateObjects, DuplicateSelectedObjects);
                    AddCommand(menu, "Delete\tDel", CanDeleteObjects, DeleteSelectedObjects);
                });
                MenuButton("Create", menu => {
                    AddVehiclePartMenu(menu, view, loaded);
                    foreach (var type in new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Plane, PrimitiveType.Quad, PrimitiveType.Cylinder })
                    { var primitive = type; menu.AddItem(new GUIContent("Polygon Primitives/" + type), false, () => Run(() => CreatePrimitive(primitive, view, loaded))); }
                    menu.AddSeparator("");
                    AddCommand(menu, "Empty Transform", true, () => CreateEmptyTransform(false, view, loaded));
                    AddCommand(menu, "Empty Child", Document && Active >= 0, () => CreateEmptyTransform(true, view, loaded));
                    AddCommand(menu, "Anchor Point", StudioAnchorCatalog.Names.Length > 0, () => CreateAnchor(loaded));
                });
                MenuButton("Select", menu => {
                    AddAction(menu, "All", MarkingAction.SelectAll, view); AddAction(menu, "Deselect", MarkingAction.Clear, view);
                    AddCommand(menu, "Invert Selection", Document && (mode == ComponentMode.Object || ActiveMesh), InvertSelection);
                    menu.AddSeparator("");
                    AddAction(menu, "Object Mode", MarkingAction.Object, view); AddAction(menu, "Vertex", MarkingAction.Vertex, view);
                    AddAction(menu, "Edge", MarkingAction.Edge, view); AddAction(menu, "Face", MarkingAction.Face, view);
                    menu.AddSeparator(""); AddAction(menu, "Frame Selection", MarkingAction.Frame, view);
                    menu.AddItem(new GUIContent("Through Selection"), xray, () => { xray = !xray; repaint(); });
                });
                MenuButton("Modify", menu => {
                    AddAction(menu, pivot ? "Lock Pivot" : "Edit Pivot", MarkingAction.EditPivot, view);
                    AddAction(menu, "Center Pivot", MarkingAction.CenterPivot, view); AddAction(menu, "Freeze Transforms", MarkingAction.Freeze, view);
                    menu.AddSeparator(""); menu.AddItem(new GUIContent("Local Axes"), local, () => { local = !local; repaint(); });
                });
                MenuButton("Mesh", menu => {
                    menu.AddItem(new GUIContent("Create Foliage Morph Set…"), false, () => { var recipe = ScriptableObject.CreateInstance<StudioFoliageMorphSet>(); ProjectWindowUtil.CreateAsset(recipe, "Foliage Morph Set.asset"); });
                    AddAction(menu, "UV Editor", MarkingAction.OpenUV, view);
                });
                MenuButton("Edit Mesh", menu => {
                    AddCommand(menu, "Extrude\tCtrl+E", CanExtrude, ExtrudeSelected);
                    menu.AddSeparator("");
                    AddAction(menu, "Vertex Mode", MarkingAction.Vertex, view);
                    AddAction(menu, "Edge Mode", MarkingAction.Edge, view); AddAction(menu, "Face Mode", MarkingAction.Face, view);
                    menu.AddSeparator(""); menu.AddItem(new GUIContent("Keep Seams Together"), weld, () => { weld = !weld; repaint(); });
                });
                MenuButton("Mesh Tools", menu => {
                    if (CanCutMesh)
                    {
                        menu.AddItem(new GUIContent("Insert Edge Loop"), cutTool == CutTool.EdgeLoop, () => Run(() => ActivateCutTool(CutTool.EdgeLoop)));
                        menu.AddItem(new GUIContent("Multi-Cut"), cutTool == CutTool.MultiCut, () => Run(() => ActivateCutTool(CutTool.MultiCut)));
                    }
                    else
                    {
                        menu.AddDisabledItem(new GUIContent("Insert Edge Loop"));
                        menu.AddDisabledItem(new GUIContent("Multi-Cut"));
                    }
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Select\tQ"), cutTool == CutTool.None && tool == Tool.None, () => ActivateTool(Tool.None));
                    AddAction(menu, "Move", MarkingAction.Move, view); AddAction(menu, "Rotate", MarkingAction.Rotate, view); AddAction(menu, "Scale", MarkingAction.Scale, view);
                    menu.AddSeparator(""); menu.AddItem(new GUIContent("Tool Options…"), false, OpenToolOptions);
                });
                MenuButton("Deform", menu => {
                    AddCommand(menu, "Blend Shape", Document && SelectedBlendMeshes.Length >= 2, CreateSelectedBlendShape);
                    AddCommand(menu, "Bake Current Pose", HasBlendShape, BakeSelectedBlendShape);
                });
                MenuButton("Mesh Display", menu => {
                    menu.AddItem(new GUIContent("Mesh Statistics"), StudioSettings.Current.showMeshStatistics, () => {
                        StudioSettings.Current.showMeshStatistics = !StudioSettings.Current.showMeshStatistics; StudioSettings.Save();
                    });
                    menu.AddSeparator("");
                    AddCommand(menu, "Recalculate Normals", ActiveMesh && CanEditComponents(), () => {
                        Undo.RegisterCompleteObjectUndo(ActiveMesh, "Recalculate normals"); ActiveMesh.RecalculateNormals(); ActiveMesh.RecalculateTangents(); Changed();
                    });
                    menu.AddSeparator("");
                    AddDisplay(menu, view, "Shaded", DrawCameraMode.Textured);
                    AddDisplay(menu, view, "Wireframe", DrawCameraMode.Wireframe);
                    AddDisplay(menu, view, "Shaded Wireframe", DrawCameraMode.TexturedWire);
                    if (view is MashBoxStudioViewport studio)
                        menu.AddItem(new GUIContent("Grid"), studio.modelGridVisible, () => { studio.modelGridVisible = !studio.modelGridVisible; repaint(); });
                });
                MenuButton("UV", menu => {
                    AddAction(menu, "UV Editor", MarkingAction.OpenUV, view);
                    for (int i = 0; i < 8; i++) AddAction(menu, "UV Sets/UV " + i, MarkingAction.UV0 + i, view);
                });
                MenuButton("Lighting", menu => {
                    AddCommand(menu, "Bake Vertex AO (Red)…", Document, () => StudioVertexAOWindow.Open(this));
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Camera Lighting"), !LightingSettings.studioLights, () => EditLighting(s => s.studioLights = false));
                    menu.AddItem(new GUIContent("Studio Lights"), LightingSettings.studioLights, () => EditLighting(s => s.studioLights = true));
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Lighting Settings…"), false, OpenLighting);
                    menu.AddItem(new GUIContent("Shadows"), LightingSettings.studioLights && LightingSettings.shadows, () => EditLighting(s => { s.shadows = !(s.studioLights && s.shadows); s.studioLights = true; }));
                    menu.AddItem(new GUIContent("Ground Plane"), LightingSettings.studioLights && LightingSettings.ground, () => EditLighting(s => { s.ground = !(s.studioLights && s.ground); s.studioLights = true; }));
                });
                GUILayout.FlexibleSpace();
            }
        }
        private void MenuButton(string title, Action<StudioMenu> populate)
        {
            var content = new GUIContent(title);
            var rect = GUILayoutUtility.GetRect(content, StudioTheme.Styles.menuDropdown, GUILayout.Width(StudioTheme.Styles.menuDropdown.CalcSize(content).x + 4));
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Passive, StudioTheme.Styles.menuDropdown)) return;
            CancelMarkingMenu(); var menu = new StudioMenu(); populate(menu); menu.DropDown(rect);
        }
        private void AddCommand(StudioMenu menu, string label, bool enabled, Action action)
        {
            if (enabled) menu.AddItem(new GUIContent(label), false, () => Run(action)); else menu.AddDisabledItem(new GUIContent(label));
        }
        private void AddAction(StudioMenu menu, string label, MarkingAction action, SceneView view)
        {
            string shortcut = action == MarkingAction.Object ? "1" : action == MarkingAction.Vertex ? "2" :
                action == MarkingAction.Edge ? "3" : action == MarkingAction.Face ? "4" :
                action == MarkingAction.Move ? "W" : action == MarkingAction.Rotate ? "E" :
                action == MarkingAction.Scale ? "R" : action == MarkingAction.Frame ? "F" :
                action == MarkingAction.EditPivot ? "D" : null;
            if (shortcut != null) label += "\t" + shortcut;
            if (MarkingActionEnabled(action)) menu.AddItem(new GUIContent(label), MarkingIsCurrent(action), () => Run(() => ExecuteMarkingAction(action, view)));
            else menu.AddDisabledItem(new GUIContent(label));
        }
        private static void AddDisplay(StudioMenu menu, SceneView view, string label, DrawCameraMode mode)
        {
            menu.AddItem(new GUIContent(label), view.cameraMode.drawMode == mode, () => { view.cameraMode = SceneView.GetBuiltinCameraMode(mode); view.Repaint(); });
        }
        internal void CreateEmptyTransform(bool asChild, SceneView view, Action<StudioModelDocument> loaded)
        {
            CancelMarkingMenu(); EnsureWorkspace(loaded);
            int index = Document.AddEmptyTransform(asChild ? Active : -1);
            mode = ComponentMode.Object; pivot = false; search = "";
            Rebuild(); SelectObject(index); Changed(false);
            if (view) Frame(view);
        }
        internal void CreatePrimitive(PrimitiveType type, SceneView view, Action<StudioModelDocument> loaded)
        {
            CancelMarkingMenu();
            EnsureWorkspace(loaded);
            int index = Document.AddPrimitive(type);
            mode = ComponentMode.Object; pivot = false; search = "";
            Rebuild(); SelectObject(index); Changed(); Frame(view);
        }
        private void EnsureWorkspace(Action<StudioModelDocument> loaded)
        {
            if (!Document)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Studio Models")) AssetDatabase.CreateFolder("Assets", "Studio Models");
                var path = AssetDatabase.GenerateUniqueAssetPath("Assets/Studio Models/Untitled Workspace.asset");
                loaded(StudioModelDocument.CreateEmpty(path));
            }
        }
        private void InvertSelection()
        {
            if (mode == ComponentMode.Object)
            {
                for (int i = 0; i < Transforms.Length; i++) if (!objects.Remove(i)) objects.Add(i);
                Active = -1; foreach (int i in objects) { Active = i; break; }
            }
            else for (int i = 0; i < ComponentCount(); i++) if (!Components.Remove(i)) Components.Add(i);
            repaint();
        }
    }
}
