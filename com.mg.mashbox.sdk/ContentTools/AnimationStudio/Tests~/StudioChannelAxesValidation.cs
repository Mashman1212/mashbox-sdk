using System;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
public static class StudioChannelAxesValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static StudioModelEditor editor;
    static StudioModelDocument document;
    static AxisWindow window;
    static int scenario, step, checks;
    static double next;
    static Vector2 pointer;
    static Quaternion initialRotation;
    static Vector3 initialPosition, initialScale;
    static float total, increment;
    static void Check(bool pass, string text) { if (!pass) throw new Exception(text); checks++; }
    static void Call(string name) => typeof(StudioModelEditor).GetMethod(name, Flags).Invoke(editor, null);
    public static void Run() { EditorApplication.delayCall += Begin; }
    static void Begin()
    {
        try
        {
            document = ScriptableObject.CreateInstance<StudioModelDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.nodes.Add(new StudioModelDocument.Node { name = "Tilted channel cube", mesh = StudioModelDocument.BuildPrimitive(PrimitiveType.Cube) });
            editor = new StudioModelEditor(() => {}); editor.Load(document); editor.SelectObject(0);
            window = ScriptableObject.CreateInstance<AxisWindow>(); window.position = new Rect(100,100,500,200); window.ShowUtility();
            next = EditorApplication.timeSinceStartup + 1; EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error); }
    }
    static void Mouse(EventType type, Vector2 point, Vector2 delta = default) => window.SendEvent(new Event { type = type, button = 0, mousePosition = point, delta = delta });
    static void Paint() { window.SendEvent(new Event { type = EventType.Layout }); window.SendEvent(new Event { type = EventType.Repaint }); }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .025;
        try
        {
            if (step == 0)
            {
                Call("FinishChannelEdit");
                window.label = scenario < 6 ? "Rotation" : scenario < 12 ? "Position" : "Scale";
                window.axis = scenario % 3; window.local = scenario % 6 < 3;
                typeof(StudioModelEditor).GetField("local", Flags).SetValue(editor, window.local);
                editor.SetObjectChannels(new Vector3(2,3,4), new Vector3(45,23,17), Vector3.one * 2, true);
                initialRotation = editor.Transforms[0].rotation; initialPosition = editor.Transforms[0].position; initialScale = document.nodes[0].scale;
                Undo.ClearUndo(document); total = increment = 0; Paint(); pointer = window.labelPoint;
                Mouse(EventType.MouseDown, pointer); Mouse(EventType.MouseDrag, pointer + Vector2.right * 10, Vector2.right * 10);
                step++; return;
            }
            if (step <= 8)
            {
                pointer = new Vector2(step % 2 == 0 ? -2500 : 2500, window.labelPoint.y);
                Mouse(EventType.MouseDrag, pointer, Vector2.right * (window.label == "Rotation" ? 1200 : 60));
                float delta = window.delta;
                if (step == 1) increment = delta;
                Check(delta > 0 && Mathf.Abs(delta - increment) < Mathf.Max(.1f, increment * .01f), "Relative drag changed direction or speed across pointer wrap: " + scenario + " step " + step + " delta " + delta + " increment " + increment);
                total += delta;
                var axis = window.axis == 0 ? Vector3.right : window.axis == 1 ? Vector3.up : Vector3.forward;
                if (window.label == "Rotation")
                {
                    var turn = Quaternion.AngleAxis(total, axis);
                    var expected = window.local ? initialRotation * turn : turn * initialRotation;
                    Check(Quaternion.Angle(editor.Transforms[0].rotation, expected) < .06f, "Rotation did not follow selected coordinate mode: " + scenario);
                    Check(Quaternion.Angle(Quaternion.Euler(document.nodes[0].RotationChannels), expected) < .06f, "Displayed rotation channels are stale");
                }
                else if (window.label == "Position")
                {
                    var expected = initialPosition + (window.local ? initialRotation * axis : axis) * total;
                    Check(Vector3.Distance(editor.Transforms[0].position, expected) < .005f, "Position did not follow selected coordinate mode");
                }
                else Check(Mathf.Abs(document.nodes[0].scale[window.axis] - initialScale[window.axis] - total) < .01f, "Scale scrub lost relative movement");
                Paint(); step++; return;
            }
            Mouse(EventType.MouseUp, pointer); Paint();
            var endRotation = editor.Transforms[0].rotation; var endPosition = editor.Transforms[0].position; var endScale = document.nodes[0].scale;
            editor.PerformWorkspaceHistory(false);
            Check(Quaternion.Angle(editor.Transforms[0].rotation, initialRotation) < .06f && Vector3.Distance(editor.Transforms[0].position, initialPosition) < .005f && document.nodes[0].scale == initialScale, "Undo did not restore entire gesture");
            editor.PerformWorkspaceHistory(true);
            Check(Quaternion.Angle(editor.Transforms[0].rotation, endRotation) < .06f && Vector3.Distance(editor.Transforms[0].position, endPosition) < .005f && document.nodes[0].scale == endScale, "Redo did not restore entire gesture");
            Debug.Log("PASS native wrapped " + window.label + " axis " + window.axis + " " + (window.local ? "Local" : "World"));
            if (++scenario < 15) { step = 0; return; }
            editor.SetObjectChannels(initialPosition, new Vector3(810,-450,721), initialScale, true);
            Check(document.nodes[0].RotationChannels == new Vector3(810,-450,721), "Typed absolute rotation no longer retained");
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        if (window) window.Close(); editor?.Dispose();
        if (document) { foreach(var node in document.nodes) if(node.mesh) Object.DestroyImmediate(node.mesh); Object.DestroyImmediate(document); }
        if(error == null) Debug.Log("STUDIO_CHANNEL_AXES_SUCCESS " + checks + " checks"); else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    public class AxisWindow : EditorWindow
    {
        public int axis; public string label = "Rotation"; public bool local; public Vector2 labelPoint; public float delta;
        void OnGUI()
        {
            Call("HandleChannelHistory"); Call("FinishInactiveChannelEdit");
            typeof(StudioModelEditor).GetField("pendingChannelScrub", Flags).SetValue(editor, null);
            var node = document.nodes[0];
            var before = label == "Rotation" ? node.RotationChannels : label == "Position" ? node.position : node.scale;
            EditorGUI.BeginChangeCheck();
            var value = (Vector3)typeof(StudioModelEditor).GetMethod("DrawObjectChannels", Flags).Invoke(editor, new object[] { label, before });
            if (Event.current.type == EventType.Repaint)
            {
                var row = GUILayoutUtility.GetLastRect(); float title = Mathf.Clamp(row.width * .4f,100,150); float width = (row.width-title-8)/3;
                labelPoint = new Vector2(row.x+title+axis*(width+4)+5,row.center.y);
            }
            if (EditorGUI.EndChangeCheck())
            {
                delta = (float)typeof(StudioModelEditor).GetField("channelScrubDelta", Flags).GetValue(editor);
                editor.SetObjectChannels(label == "Position" ? value : node.position, label == "Rotation" ? value : node.RotationChannels, label == "Scale" ? value : node.scale, true);
            }
            Call("FinishInactiveChannelEdit");
        }
    }
}
