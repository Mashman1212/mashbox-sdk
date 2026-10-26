using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal static readonly string[] ModelMenuTitles = { "File", "Edit", "Select", "Create", "Modify", "Window", "Display", "Mesh Tools", "Mesh Display", "UV", "Render" };
        private readonly System.Collections.Generic.Dictionary<string,(GUIContent content,GUILayoutOption[] layout,GUIStyle style)> menuBarContents = new System.Collections.Generic.Dictionary<string,(GUIContent,GUILayoutOption[],GUIStyle)>();
        internal void DrawMenuBar(SceneView view, Action<StudioModelDocument> loaded)
        {
            using (new EditorGUILayout.HorizontalScope(GUIStyle.none))
            {
                foreach(string title in ModelMenuTitles)DrawMenuButton(view,title,loaded);
                GUILayout.FlexibleSpace();
            }
        }
        private void PopulateModelMenu(string title,StudioMenu menu,SceneView view,Action<StudioModelDocument> loaded)
        {
            switch(title)
            {
                case "File": {
                    menu.AddItem(new GUIContent("New Workspace"), false, () => Run(() => NewWorkspace(loaded)));
                    menu.AddItem(new GUIContent("Open Workspace…"), false, () => Run(() => OpenWorkspace(loaded)));
                    AddRecentFilesMenu(menu, loaded);
                    menu.AddItem(new GUIContent("Import FBX…"), false, () => Run(() => ImportPath(EditorUtility.OpenFilePanel("Import FBX into workspace", "", "fbx"), loaded)));
                    menu.AddSeparator("");
                    AddCommand(menu, StudioShortcuts.MenuLabel("Save Workspace",StudioShortcut.Save), Document, Save);
                    AddCommand(menu, StudioShortcuts.MenuLabel("Save Workspace As…",StudioShortcut.SaveAs), Document, SaveAs);
                    menu.AddSeparator("");
                    AddCommand(menu, "Export All… (FBX)", Document && Document.nodes.Count > 0, Export);
                    AddCommand(menu, "Export Selection… (FBX)", CanExportSelection, () => Export(true));
                
                    break; }
                case "Edit": {
                    if (Document)
                    {
                        menu.AddItem(new GUIContent(StudioShortcuts.MenuLabel("Undo",StudioShortcut.Undo)), false, () => Run(() => PerformWorkspaceHistory(false), false));
                        menu.AddItem(new GUIContent(StudioShortcuts.MenuLabel("Redo",StudioShortcut.Redo)), false, () => Run(() => PerformWorkspaceHistory(true), false));
                    }
                    else
                    {
                        menu.AddDisabledItem(new GUIContent(StudioShortcuts.MenuLabel("Undo",StudioShortcut.Undo)));
                        menu.AddDisabledItem(new GUIContent(StudioShortcuts.MenuLabel("Redo",StudioShortcut.Redo)));
                    }
                    menu.AddSeparator(""); AddCommand(menu, StudioShortcuts.MenuLabel("Duplicate",StudioShortcut.Duplicate), CanDuplicateObjects, DuplicateSelectedObjects);
                    AddCommand(menu, StudioShortcuts.MenuLabel("Delete",StudioShortcut.Delete), CanDeleteSelection, DeleteSelection);
                    menu.AddSeparator(""); AddCommand(menu, "Preferences…", true, StudioControlsPreferences.Open, shortcutEligible: false);
                
                    break; }
                case "Window": {
                    menu.AddItem(new GUIContent("Themes…"), false, StudioSettingsWindow.Open, shortcutEligible: false);
                    break; }
                case "Display": {
                    menu.AddItem(new GUIContent("Tool Options"), !StudioSettings.Current.hideToolOptions, () => {
                        StudioSettings.Current.hideToolOptions = !StudioSettings.Current.hideToolOptions;
                        StudioSettings.Save();
                        if (!StudioSettings.Current.hideToolOptions) OpenToolOptions();
                    });

                    menu.AddSeparator("");
                    if (view is MashBoxStudioViewport studioToolbarView) studioToolbarView.AddStudioToolbarMenu(menu);
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Mesh Statistics"), StudioSettings.Current.showMeshStatistics, () => {
                        StudioSettings.Current.showMeshStatistics = !StudioSettings.Current.showMeshStatistics; StudioSettings.Save();
                    });
                    menu.AddItem(new GUIContent("Notifications"), !StudioSettings.Current.hideStatusBar, () => {
                        StudioSettings.Current.hideStatusBar = !StudioSettings.Current.hideStatusBar; StudioSettings.Save();
                    });
                    menu.AddItem(new GUIContent("Orientation Gizmo"), !StudioSettings.Current.hideOrientationAxes, () => {
                        StudioSettings.Current.hideOrientationAxes = !StudioSettings.Current.hideOrientationAxes;
                        StudioSettings.Save();
                    });
                    menu.AddItem(new GUIContent("FPS Display"), !StudioSettings.Current.hideViewportFps, () => {
                        StudioSettings.Current.hideViewportFps = !StudioSettings.Current.hideViewportFps;
                        StudioSettings.Save();
                    });
                    break; }
                case "Create": {
                    foreach (var type in new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Plane, PrimitiveType.Quad, PrimitiveType.Cylinder })
                    { var primitive = type; menu.AddItem(new GUIContent("Polygon Primitives/" + type), false, () => Run(() => CreatePrimitive(primitive, view, loaded))); }
                    menu.AddSeparator("");
                    AddVehiclePartMenu(menu, view, loaded);
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Create Foliage Morph Set…"), false, () => { var recipe = ScriptableObject.CreateInstance<StudioFoliageMorphSet>(); ProjectWindowUtil.CreateAsset(recipe, "Foliage Morph Set.asset"); }, shortcutEligible: false);
                    menu.AddSeparator("");
                    AddCommand(menu, "Empty Transform", true, () => CreateEmptyTransform(false, view, loaded));
                    AddCommand(menu, "Empty Child", Document && Active >= 0, () => CreateEmptyTransform(true, view, loaded));
                    AddCommand(menu, "Anchor Point", StudioAnchorCatalog.Names.Length > 0, () => CreateAnchorAtOrigin(view, loaded));
                
                    break; }
                case "Select": {
                    AddAction(menu, "All", MarkingAction.SelectAll, view); AddAction(menu, "Deselect", MarkingAction.Clear, view);
                    AddCommand(menu, "Invert Selection", Document && (mode == ComponentMode.Object || ActiveMesh), InvertSelection);
                    menu.AddSeparator("");
                    AddAction(menu, "Object Mode", MarkingAction.Object, view); AddAction(menu, "Vertex", MarkingAction.Vertex, view);
                    AddAction(menu, "Edge", MarkingAction.Edge, view); AddAction(menu, "Face", MarkingAction.Face, view);
                    menu.AddSeparator(""); AddAction(menu, "Frame Selection", MarkingAction.Frame, view);
                    menu.AddItem(new GUIContent("Through Selection"), xray, () => { xray = !xray; repaint(); });
                
                    break; }
                case "Modify": {
                    menu.AddItem(new GUIContent(StudioShortcuts.MenuLabel("Select",StudioShortcut.Select)), cutTool == CutTool.None && tool == Tool.None, () => ActivateTool(Tool.None));
                    AddAction(menu, "Move", MarkingAction.Move, view); AddAction(menu, "Rotate", MarkingAction.Rotate, view); AddAction(menu, "Scale", MarkingAction.Scale, view);
                    menu.AddSeparator("");
                    AddAction(menu, pivot ? "Lock Pivot" : "Edit Pivot", MarkingAction.EditPivot, view);
                    AddAction(menu, "Center Pivot", MarkingAction.CenterPivot, view); AddAction(menu, "Freeze Transforms", MarkingAction.Freeze, view);
                
                    break; }
                case "Mesh Tools": {
                    AddCommand(menu, StudioShortcuts.MenuLabel("Extrude",StudioShortcut.Extrude), CanExtrude, ExtrudeSelected, finishPreview: false);
                    AddCommand(menu, "Bevel…", CanBevelEdges, OpenBevelTool, finishPreview: false);
                    AddCommand(menu, "Merge Vertices", CanMergeVertices, () => Run(MergeSelectedVertices));
                    menu.AddSeparator("");
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
                    AddCommand(menu, "Deform/Blend Shape", Document && SelectedBlendMeshes.Length >= 2, CreateSelectedBlendShape);
                    AddCommand(menu, "Deform/Bake Current Pose", HasBlendShape, BakeSelectedBlendShape);
                
                    break; }
                case "Mesh Display": {
                    AddCommand(menu, "Soften", CanAdjustEdgeNormals, () => ApplyEdgeNormals(false));
                    AddCommand(menu, "Harden", CanAdjustEdgeNormals, () => ApplyEdgeNormals(true));
                    AddCommand(menu, "Soften…", CanAdjustEdgeNormals, OpenSoftenTool, finishPreview: false);
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent(StudioShortcuts.MenuLabel("X-ray Mode", StudioShortcut.XRay)), displayXray, () => SetXRayMode(!displayXray));
                    menu.AddSeparator("");
                    AddCommand(menu, "Recalculate Normals", ActiveMesh && CanEditComponents(), () => {
                        Undo.RegisterCompleteObjectUndo(ActiveMesh, "Recalculate normals"); RecalculateEditableNormals(ActiveMesh); ActiveMesh.RecalculateTangents(); Changed();
                    });
                    menu.AddSeparator("");
                    AddDisplay(menu, view, "Shaded", DrawCameraMode.Textured);
                    AddDisplay(menu, view, "Wireframe", DrawCameraMode.Wireframe);
                    AddDisplay(menu, view, "Shaded Wireframe", DrawCameraMode.TexturedWire);
                
                    break; }
                case "UV": {
                    AddAction(menu, "UV Editor", MarkingAction.OpenUV, view);
                    for (int i = 0; i < 8; i++) AddAction(menu, "UV Sets/UV " + i, MarkingAction.UV0 + i, view);
                
                    break; }
                case "Render": {
                    AddCommand(menu, "Bake Vertex AO (Red)…", Document, () => StudioVertexAOWindow.Open(this));
                
                    break; }
            }
        }
        private void DrawMenuButton(SceneView view,string title,Action<StudioModelDocument> loaded)
        {
            var style=StudioTheme.Styles.menuBarButton;
            if(!menuBarContents.TryGetValue(title,out var entry))
                entry=(new GUIContent(title),null,null);
            // Theme styles are immutable per palette. Measure again only when the
            // style changes, rather than allocating text layout every GUI event.
            if(entry.layout==null||!ReferenceEquals(entry.style,style))
            {
                entry.layout=new[]{GUILayout.Width(style.CalcSize(entry.content).x+4)};
                entry.style=style;menuBarContents[title]=entry;
            }
            var rect=GUILayoutUtility.GetRect(entry.content,style,entry.layout);
            if(!StudioMenu.MenuBarButton(view,rect,entry.content))return;
            CancelMarkingMenu();var menu=new StudioMenu(title);PopulateModelMenu(title,menu,view,loaded);menu.DropDown(rect,view,title);
        }
        private void AddCommand(StudioMenu menu, string label, bool enabled, Action action, bool shortcutEligible = true, bool finishPreview = true)
        {
            if (enabled) menu.AddItem(new GUIContent(label), false, () => Run(action, finishPreview), shortcutEligible: shortcutEligible); else menu.AddDisabledItem(new GUIContent(label), shortcutEligible: shortcutEligible);
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
            menu.AddItem(new GUIContent(label), ModelDrawMode == mode, () => SetModelDrawModeEnabled(view, mode, ModelDrawMode != mode));
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
            private readonly bool orthographic, in2DMode;
            internal CreationCameraScope(SceneView view)
            {
                this.view = view;
                pivot = view ? view.pivot : Vector3.zero;
                rotation = view ? view.rotation : Quaternion.identity;
                size = view ? view.size : 1;
                orthographic = view && view.orthographic;
                in2DMode = view && view.in2DMode;
            }
            public void Dispose()
            {
                if (view) { view.in2DMode = in2DMode; view.orthographic = orthographic; view.LookAtDirect(pivot, rotation, size); }
            }
        }
        private void EnsureWorkspace(Action<StudioModelDocument> loaded)
        {
            if (!Document)
            {
                loaded(CreateTemporaryWorkspace());
            }
        }
        private void InvertSelection()
        {
            var before = CaptureSelection();
            if (mode == ComponentMode.Object)
            {
                for (int i = 0; i < Transforms.Length; i++) if (!objects.Remove(i)) objects.Add(i);
                Active = -1; foreach (int i in objects) { Active = i; break; }
            }
            else foreach(int node in ComponentTargetNodes()) using(new ComponentTargetScope(this,node)) for (int i = 0; i < ComponentCount(); i++) if (!Components.Remove(i)) Components.Add(i);
            RecordSelectionChange(before);
            repaint();
        }
    }
}
