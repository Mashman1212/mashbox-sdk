using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal void DrawMenuBar(SceneView view, Action<StudioModelDocument> loaded)
        {
            void MenuButton(string title, Action<StudioMenu> populate) => DrawMenuButton(view,title,populate);
            using (new EditorGUILayout.HorizontalScope(GUIStyle.none))
            {
                MenuButton("File", menu => {
                    menu.AddItem(new GUIContent("Open Workspace…"), false, () => Run(() => OpenWorkspace(loaded)));
                    menu.AddItem(new GUIContent("Import FBX…"), false, () => Run(() => ImportPath(EditorUtility.OpenFilePanel("Import FBX into workspace", "", "fbx"), loaded)));
                    menu.AddSeparator("");
                    AddCommand(menu, StudioShortcuts.MenuLabel("Save Workspace",StudioShortcut.Save), Document, Save);
                    AddCommand(menu, StudioShortcuts.MenuLabel("Save Workspace As…",StudioShortcut.SaveAs), Document, SaveAs);
                    menu.AddSeparator("");
                    AddCommand(menu, "Export All… (FBX)", Document && Document.nodes.Count > 0, Export);
                    AddCommand(menu, "Export Selection… (FBX)", CanExportSelection, () => Export(true));
                });
                MenuButton("Edit", menu => {
                    AddCommand(menu, StudioShortcuts.MenuLabel("Undo",StudioShortcut.Undo), Document, Undo.PerformUndo); AddCommand(menu, StudioShortcuts.MenuLabel("Redo",StudioShortcut.Redo), Document, Undo.PerformRedo);
                    menu.AddSeparator(""); AddCommand(menu, StudioShortcuts.MenuLabel("Duplicate",StudioShortcut.Duplicate), CanDuplicateObjects, DuplicateSelectedObjects);
                    AddCommand(menu, StudioShortcuts.MenuLabel("Delete",StudioShortcut.Delete), CanDeleteObjects, DeleteSelectedObjects);
                    menu.AddSeparator(""); AddCommand(menu, "Preferences…", true, StudioControlsPreferences.Open);
                });
                MenuButton("Create", menu => {
                    AddVehiclePartMenu(menu, view, loaded);
                    foreach (var type in new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Plane, PrimitiveType.Quad, PrimitiveType.Cylinder })
                    { var primitive = type; menu.AddItem(new GUIContent("Polygon Primitives/" + type), false, () => Run(() => CreatePrimitive(primitive, view, loaded))); }
                    menu.AddSeparator("");
                    AddCommand(menu, "Empty Transform", true, () => CreateEmptyTransform(false, view, loaded));
                    AddCommand(menu, "Empty Child", Document && Active >= 0, () => CreateEmptyTransform(true, view, loaded));
                    AddCommand(menu, "Anchor Point", StudioAnchorCatalog.Names.Length > 0, () => CreateAnchorAtOrigin(view, loaded));
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
                    AddCommand(menu, StudioShortcuts.MenuLabel("Extrude",StudioShortcut.Extrude), CanExtrude, ExtrudeSelected);
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
                    menu.AddItem(new GUIContent(StudioShortcuts.MenuLabel("Select",StudioShortcut.Select)), cutTool == CutTool.None && tool == Tool.None, () => ActivateTool(Tool.None));
                    AddAction(menu, "Move", MarkingAction.Move, view); AddAction(menu, "Rotate", MarkingAction.Rotate, view); AddAction(menu, "Scale", MarkingAction.Scale, view);
                    menu.AddSeparator(""); menu.AddItem(new GUIContent("Tool Options…"), false, OpenToolOptions);
                });
                MenuButton("Deform", menu => {
                    AddCommand(menu, "Blend Shape", Document && SelectedBlendMeshes.Length >= 2, CreateSelectedBlendShape);
                    AddCommand(menu, "Bake Current Pose", HasBlendShape, BakeSelectedBlendShape);
                });
                MenuButton("Mesh Display", menu => {
                    menu.AddItem(new GUIContent(StudioShortcuts.MenuLabel("X-ray Mode", StudioShortcut.XRay)), displayXray, () => SetXRayMode(!displayXray));
                    menu.AddSeparator("");
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
        private void DrawMenuButton(SceneView view, string title, Action<StudioMenu> populate)
        {
            var content = new GUIContent(title);
            var rect = GUILayoutUtility.GetRect(content, StudioTheme.Styles.menuBarButton, GUILayout.Width(StudioTheme.Styles.menuBarButton.CalcSize(content).x + 4));
            if (!StudioMenu.MenuBarButton(view,rect,content)) return;
            CancelMarkingMenu(); var menu = new StudioMenu(); populate(menu); menu.DropDown(rect,view,title);
        }
        private void AddCommand(StudioMenu menu, string label, bool enabled, Action action)
        {
            if (enabled) menu.AddItem(new GUIContent(label), false, () => Run(action)); else menu.AddDisabledItem(new GUIContent(label));
        }
        private void AddAction(StudioMenu menu, string label, MarkingAction action, SceneView view)
        {
            StudioShortcut? shortcut = action == MarkingAction.Object ? StudioShortcut.Object : action == MarkingAction.Vertex ? StudioShortcut.Vertex :
                action == MarkingAction.Edge ? StudioShortcut.Edge : action == MarkingAction.Face ? StudioShortcut.Face :
                action == MarkingAction.Move ? StudioShortcut.Move : action == MarkingAction.Rotate ? StudioShortcut.Rotate :
                action == MarkingAction.Scale ? StudioShortcut.Scale : action == MarkingAction.Frame ? StudioShortcut.Frame :
                action == MarkingAction.EditPivot ? StudioShortcut.Pivot : (StudioShortcut?)null;
            if (shortcut.HasValue) label = StudioShortcuts.MenuLabel(label,shortcut.Value);
            if (MarkingActionEnabled(action)) menu.AddItem(new GUIContent(label), MarkingIsCurrent(action), () => Run(() => ExecuteMarkingAction(action, view)));
            else menu.AddDisabledItem(new GUIContent(label));
        }
        private void AddDisplay(StudioMenu menu, SceneView view, string label, DrawCameraMode mode)
        {
            menu.AddItem(new GUIContent(label), ModelDrawMode == mode, () => SetModelDrawMode(view, mode));
        }
        internal void CreateEmptyTransform(bool asChild, SceneView view, Action<StudioModelDocument> loaded)
        {
            using var camera = new CreationCameraScope(view);
            CancelMarkingMenu(); EnsureWorkspace(loaded);
            int parent = asChild ? Active : -1;
            int index = Document.AddEmptyTransform(parent, CreationPosition(parent));
            mode = ComponentMode.Object; pivot = false; search = "";
            Rebuild(); SelectObject(index); Changed(false);
        }
        internal void CreatePrimitive(PrimitiveType type, SceneView view, Action<StudioModelDocument> loaded)
        {
            using var camera = new CreationCameraScope(view);
            CancelMarkingMenu();
            EnsureWorkspace(loaded);
            int index = Document.AddPrimitive(type);
            mode = ComponentMode.Object; pivot = false; search = "";
            Rebuild(); SelectObject(index); Changed();
        }
        private Vector3 CreationPosition(int parent) => parent >= 0
            ? Transforms[parent].InverseTransformPoint(Vector3.zero) : Vector3.zero;
        // Loading a newly created workspace also binds and frames its preview scene.
        private readonly struct CreationCameraScope : IDisposable
        {
            private readonly SceneView view;
            private readonly Vector3 pivot;
            private readonly Quaternion rotation;
            private readonly float size;
            private readonly bool orthographic;
            internal CreationCameraScope(SceneView view)
            {
                this.view = view;
                pivot = view ? view.pivot : Vector3.zero;
                rotation = view ? view.rotation : Quaternion.identity;
                size = view ? view.size : 1;
                orthographic = view && view.orthographic;
            }
            public void Dispose()
            {
                if (view) { view.orthographic = orthographic; view.LookAtDirect(pivot, rotation, size); }
            }
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
