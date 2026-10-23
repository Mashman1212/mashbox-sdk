using EditorPrefs = MashBoxSDK.AnimationStudio.StudioPreferenceStore;
using System;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class StudioModelWorkflowValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool pass, string message)
    { if (!pass) throw new Exception(message); checks++; Debug.Log("PASS " + message); }
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    static void Mode(StudioModelEditor editor, StudioModelEditor.ComponentMode mode)
    { editor.GetType().GetField("mode", Flags).SetValue(editor, mode); editor.Components.Clear(); }
    public static void Run()
    {
        const string path = "Assets/WorkflowValidation.asset";
        var editor = new StudioModelEditor(() => {});
        var view = ScriptableObject.CreateInstance<MashBoxStudioViewport>();
        var host = ScriptableObject.CreateInstance<StudioControlsValidationHost>();
        bool existed = EditorPrefs.HasKey(MashBoxStudioViewport.ToolOptionsHeightKey);
        float saved = EditorPrefs.GetFloat(MashBoxStudioViewport.ToolOptionsHeightKey);
        var catalogNames = typeof(StudioAnchorCatalog).GetField("names", BindingFlags.Static | BindingFlags.NonPublic);
        var savedNames = catalogNames.GetValue(null);
        catalogNames.SetValue(null, new[] { "Stem_Anchor" });
        StudioModelDocument doc = null;
        string createdPath = null;
        try
        {
            host.ShowUtility(); host.Focus(); host.SendEvent(new Event { type = EventType.Layout });
            view.position = new Rect(0, 0, 1400, 800);
            Vector3 pivot = new Vector3(17, 23, -31);
            Quaternion rotation = Quaternion.Euler(27, 53, 8);
            void SetCamera() { view.orthographic = true; view.LookAtDirect(pivot, rotation, 13); }
            void Camera(string action) => Check(Vector3.Distance(view.pivot, pivot) < .001f && Quaternion.Angle(view.rotation, rotation) < .01f && Mathf.Abs(view.size - 13) < .001f && view.orthographic, action + " preserves camera");
            void Origin(string action) => Check(editor.Transforms[editor.Active].position.sqrMagnitude < .000001f, action + " creates at world origin");
            SetCamera();
            editor.CreatePrimitive(PrimitiveType.Cube, view, d => {
                doc = d; createdPath = AssetDatabase.GetAssetPath(d); editor.Load(d);
                view.Bind(editor.Scene); editor.Frame(view);
            });
            Camera("First primitive and workspace creation"); Origin("First primitive");
            foreach (var type in new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Plane, PrimitiveType.Quad, PrimitiveType.Cylinder })
            { SetCamera(); editor.CreatePrimitive(type, view, editor.Load); Camera("Repeated " + type); Origin(type.ToString()); }
            SetCamera(); editor.CreateEmptyTransform(false, view, editor.Load); Camera("Empty transform"); Origin("Empty transform");
            int parent = editor.Active;
            editor.SetObjectChannels(new Vector3(11, -7, 23), new Vector3(14, 36, 51), new Vector3(2, 3, 4), true);
            SetCamera(); editor.CreateEmptyTransform(true, view, editor.Load); Camera("Empty child"); Origin("Empty child under transformed parent");
            Check(doc.nodes[editor.Active].parent == parent, "Empty child retains parent");
            int child = editor.Active;
            Undo.PerformUndo();
            Check(doc.nodes.Count == child, "Undo removes the newly created child");
            Undo.PerformRedo(); editor.SelectObject(child);
            Origin("Redo of empty child"); Camera("Undo/redo of child creation");
            editor.SelectObject(parent);
            catalogNames.SetValue(null, new[] { "Stem_Anchor" });
            {
                SetCamera(); Call(editor, "CreateAnchorAtOrigin", view, (Action<StudioModelDocument>)editor.Load);
                Camera("Anchor"); Origin("Anchor under transformed parent");
                Check(doc.nodes[editor.Active].parent == parent, "Anchor retains parent");
            }
            var template = new StudioVehiclePartCatalog.Template { vehicle = "Test", part = "Frame", ruleCount = 0 };
            SetCamera(); editor.CreateVehiclePart(template, view, editor.Load); Camera("Vehicle part"); Origin("Vehicle part");
            editor.Load(null); AssetDatabase.DeleteAsset(createdPath); createdPath = null;

            doc = StudioModelDocument.CreateEmpty(path);
            doc.AddPrimitive(PrimitiveType.Cube); doc.AddPrimitive(PrimitiveType.Cube);
            editor.Load(doc); editor.SelectObject(0);
            var stats = editor.MeshStatistics();
            Check(stats.scene.SequenceEqual(new long[] {16,24,12,24,48}), "Scene reports totals across both cubes");
            Check(stats.selected.SequenceEqual(new long[] {8,12,6,12,24}), "Object selection reports only selected cube");
            Mode(editor, StudioModelEditor.ComponentMode.Vertex); editor.Components.Add(0);
            stats = editor.MeshStatistics();
            Check(stats.selected[0] == 1 && stats.selected[1] == 0 && stats.selected[2] == 0 && stats.selected[3] == 0, "One selected vertex does not report full cube");
            Check(stats.selected[4] == 3, "Welded vertex counts its three authored UV corners");
            Check(stats.scene.SequenceEqual(new long[] {16,24,12,24,48}), "Component selection leaves scene totals unchanged");
            Mode(editor, StudioModelEditor.ComponentMode.Edge); editor.Components.Add(0);
            stats = editor.MeshStatistics();
            Check(stats.selected[0] == 2 && stats.selected[1] == 1 && stats.selected[2] == 0 && stats.selected[3] == 0, "One selected edge reports its endpoints and one edge");
            Mode(editor, StudioModelEditor.ComponentMode.Face); editor.Components.Add(0);
            stats = editor.MeshStatistics();
            Check(stats.selected.SequenceEqual(new long[] {4,4,1,2,4}), "One selected quad reports four vertices, four edges, one face and two tris");
            editor.Components.Clear(); Check(editor.MeshStatistics().selected.All(n => n == 0), "Cleared component selection reports zero");
            Check(editor.VisibleWireframeNodes().Count() == 2, "Both selected and unselected meshes retain wireframes in component mode");
            editor.Load(doc);
            Check(editor.Active < 0 && editor.VisibleWireframeNodes().Count() == 2, "Wireframes survive clearing object selection");
            editor.Transforms[1].gameObject.SetActive(false);
            Check(editor.VisibleWireframeNodes().SequenceEqual(new[] {0}), "Hidden meshes do not draw wireframes");
            Check(editor.MeshStatistics().scene[0] == 8, "Hidden mesh excluded from scene totals");

            view.BindInspector(() => {}); view.BindModes(() => {}, true);
            view.BindModelToolOptions(editor.DrawEmbeddedToolOptions);
            var tools = view.rootVisualElement.Q<IMGUIContainer>("mashbox-model-tool-options");
            var channels = view.rootVisualElement.Q<IMGUIContainer>("mashbox-animation-inspector");
            // Use the field because the existing Channels container keeps its legacy element name.
            channels = (IMGUIContainer)view.GetType().GetField("inspector", Flags).GetValue(view);
            var divider = view.rootVisualElement.Q<IMGUIContainer>("mashbox-tool-options-divider");
            view.ResizeToolOptions(260);
            Check(Mathf.Abs(tools.style.height.value.value - 260) < .01f && Mathf.Abs(channels.style.top.value.value - 300) < .01f, "Tool Options and Channels share one horizontal split");
            Check(Mathf.Abs(tools.style.width.value.value - channels.style.width.value.value) < .01f, "Tool Options matches Channels width");
            Check(Mathf.Abs(divider.style.top.value.value - 292) < .01f, "Divider tracks Tool Options bottom");
            view.ResizeToolOptions(10000);
            Check(view.ToolOptionsHeight <= 632 && channels.style.top.value.value <= 672, "Resizing preserves minimum Channels space");
            view.ResizeToolOptions(-100);
            Check(view.ToolOptionsHeight == 80, "Resizing preserves minimum Tool Options space");
            view.ResizeToolOptions(220);
            void DividerEvent(EventType type, float y, KeyCode key = KeyCode.None)
            {
                host.Drive(() => {
                    Event.current = new Event { button = 0, mousePosition = new Vector2(view.PanelWidths.y / 2, y), keyCode = key };
                    Event.current.type = type;
                    Call(view, "DrawToolOptionsDivider");
                });
            }
            DividerEvent(EventType.MouseDown, 4);
            DividerEvent(EventType.MouseDrag, 54);
            Check(Mathf.Abs(view.ToolOptionsHeight - 270) < .01f && Mathf.Abs(channels.style.top.value.value - 310) < .01f, "Dragging center handle resizes both panes");
            DividerEvent(EventType.MouseUp, 54);
            Check(Mathf.Abs(EditorPrefs.GetFloat(MashBoxStudioViewport.ToolOptionsHeightKey) - 270) < .01f && GUIUtility.hotControl == 0, "Releasing divider saves height and releases mouse capture");
            DividerEvent(EventType.MouseDown, 4); DividerEvent(EventType.MouseDrag, 34);
            DividerEvent(EventType.KeyDown, 34, KeyCode.Escape);
            Check(Mathf.Abs(view.ToolOptionsHeight - 270) < .01f && GUIUtility.hotControl == 0, "Cancel restores divider height");
            editor.FocusToolOptions = view.FocusModelToolOptions; editor.OpenToolOptions();
            Check(Resources.FindObjectsOfTypeAll<StudioModelToolOptions>().Length == 0, "Tool Options opens embedded pane without floating popup");
            host.Draw(editor.DrawEmbeddedToolOptions);
            Check(true, "Embedded Tool Options lays out and draws");
            view.BindModes(() => {}, false);
            Check(tools.style.display.value == DisplayStyle.None && divider.style.display.value == DisplayStyle.None && channels.style.top.value.value == 32, "Animation mode restores unsplit inspector");
            view.BindModes(() => {}, true);
            Check(tools.style.display.value == DisplayStyle.Flex && divider.style.display.value == DisplayStyle.Flex, "Modeling mode restores Tool Options split");
            Debug.Log("STUDIO_MODEL_WORKFLOW_VALIDATION_SUCCESS " + checks + " checks");
        }
        finally
        {
            GUIUtility.hotControl = 0; host.Close(); Object.DestroyImmediate(view); editor.Dispose();
            if (createdPath != null) AssetDatabase.DeleteAsset(createdPath);
            AssetDatabase.DeleteAsset(path);
            if (existed) EditorPrefs.SetFloat(MashBoxStudioViewport.ToolOptionsHeightKey, saved);
            else EditorPrefs.DeleteKey(MashBoxStudioViewport.ToolOptionsHeightKey);
            catalogNames.SetValue(null, savedNames);
        }
    }
}
