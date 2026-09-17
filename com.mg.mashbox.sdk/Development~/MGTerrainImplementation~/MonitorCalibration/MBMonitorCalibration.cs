using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.MapTools
{
    /// <summary>Editor UI reference patches, deliberately outside scene lighting/post processing.</summary>
    [InitializeOnLoad]
    internal static class MBMonitorCalibration
    {
        const string PreferenceKey = "MashBox.Mappy.MonitorCalibration";
        static readonly Dictionary<SceneView, VisualElement> Panels = new Dictionary<SceneView, VisualElement>();
        static readonly List<SceneView> ClosedViews = new List<SceneView>();
        static bool enabled;
        static event System.Action Changed;

        static MBMonitorCalibration()
        {
            enabled = EditorPrefs.GetBool(PreferenceKey, false);
            if (enabled) EditorApplication.update += UpdatePanels;
            AssemblyReloadEvents.beforeAssemblyReload += ClearPanels;
        }

        internal static Toggle CreateToggle()
        {
            var toggle = new Toggle { text = "On", value = enabled,
                tooltip = "Show monitor comparison patches in every Scene view. Local editor preference; does not change lighting." };
            toggle.RegisterValueChangedCallback(e => SetEnabled(e.newValue));
            void Sync() => toggle.SetValueWithoutNotify(enabled);
            toggle.RegisterCallback<AttachToPanelEvent>(_ => { Changed += Sync; Sync(); });
            toggle.RegisterCallback<DetachFromPanelEvent>(_ => Changed -= Sync);
            return toggle;
        }

        static void SetEnabled(bool value)
        {
            if (enabled == value) return;
            enabled = value;
            EditorPrefs.SetBool(PreferenceKey, value);
            EditorApplication.update -= UpdatePanels;
            if (value)
            {
                EditorApplication.update += UpdatePanels;
                UpdatePanels();
            }
            else ClearPanels();
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        static void ClearPanels()
        {
            foreach (var panel in Panels.Values) panel.RemoveFromHierarchy();
            Panels.Clear();
        }

        static void UpdatePanels()
        {
            ClosedViews.Clear();
            foreach (var entry in Panels)
                if (entry.Key == null || !SceneView.sceneViews.Contains(entry.Key)) ClosedViews.Add(entry.Key);
            foreach (var view in ClosedViews)
            {
                Panels[view].RemoveFromHierarchy();
                Panels.Remove(view);
            }
            foreach (SceneView view in SceneView.sceneViews)
            {
                if (Panels.ContainsKey(view)) continue;
                var panel = BuildPanel();
                view.rootVisualElement.Add(panel);
                Panels.Add(view, panel);
            }
        }

        static VisualElement BuildPanel()
        {
            var panel = new ScrollView(ScrollViewMode.Vertical) { name = "mappy-monitor-calibration" };
            panel.style.position = Position.Absolute;
            panel.style.left = 12;
            panel.style.top = 42;
            panel.style.bottom = StyleKeyword.Auto;
            panel.style.width = 420;
            panel.style.maxWidth = Length.Percent(90);
            panel.style.maxHeight = Length.Percent(85);
            panel.style.backgroundColor = Gray(28);
            panel.style.paddingLeft = panel.style.paddingRight = 10;
            panel.style.paddingTop = panel.style.paddingBottom = 8;
            panel.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            // Consume input over the chart so clicks and wheel gestures do not edit the scene.
            panel.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            panel.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            panel.RegisterCallback<WheelEvent>(e => e.StopPropagation());

            var header = Row();
            var title = Text("DISPLAY REFERENCE  /  v1", 12);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1;
            header.Add(title);
            header.Add(new Button(() => SetEnabled(false)) { text = "Close" });
            panel.Add(header);
            panel.Add(Text("UI RGB levels 0–255 • independent of scene exposure", 10));

            panel.Add(Heading("BLACK LEVEL  •  numbers on RGB 0"));
            panel.Add(NumberPatches(new[] { 0, 2, 4, 8, 12, 16, 24, 32 }, 0));
            panel.Add(Heading("WHITE LEVEL  •  numbers on RGB 255"));
            panel.Add(NumberPatches(new[] { 223, 231, 239, 243, 247, 251, 253, 255 }, 255));

            panel.Add(Heading("GRAYSCALE  •  neutral, distinct steps"));
            var grays = Row();
            foreach (int level in new[] { 0, 32, 64, 96, 128, 160, 192, 224, 255 })
                grays.Add(Swatch(Gray(level), level.ToString()));
            panel.Add(grays);
            panel.Add(Heading("COLOR  •  full RGB primaries and secondaries"));
            var colors = Row();
            colors.Add(Swatch(Color.red, "R"));
            colors.Add(Swatch(Color.green, "G"));
            colors.Add(Swatch(Color.blue, "B"));
            colors.Add(Swatch(Color.cyan, "C"));
            colors.Add(Swatch(Color.magenta, "M"));
            colors.Add(Swatch(Color.yellow, "Y"));
            panel.Add(colors);
            panel.Add(Text("Compare the first/last visible numbers on both monitors. The 0 and 255 endpoint numbers are intentionally invisible. This is a visual reference, not a measured calibration or gamma reading.", 11));
            panel.Add(Text("Compare both screenshots on ONE display too: if scene brightness still differs there, investigate exposure, post processing, quality and HDR settings.", 11));
            return panel;
        }

        static VisualElement NumberPatches(int[] levels, int background)
        {
            var row = Row();
            foreach (int level in levels)
            {
                var cell = new VisualElement();
                cell.style.flexGrow = 1;
                cell.style.flexBasis = 0;
                var number = Text(level.ToString(), 19);
                number.style.height = 42;
                number.style.marginLeft = number.style.marginRight = 0;
                number.style.marginTop = number.style.marginBottom = 0;
                number.style.backgroundColor = Gray(background);
                number.style.color = Gray(level);
                number.style.unityFontStyleAndWeight = FontStyle.Bold;
                number.style.unityTextAlign = TextAnchor.MiddleCenter;
                cell.Add(number);
                var label = Text(level.ToString(), 10);
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                cell.Add(label);
                row.Add(cell);
            }
            return row;
        }

        static VisualElement Swatch(Color color, string label)
        {
            var cell = new VisualElement();
            cell.style.flexGrow = 1;
            cell.style.flexBasis = 0;
            var patch = new VisualElement();
            patch.style.height = 26;
            patch.style.backgroundColor = color;
            cell.Add(patch);
            var text = Text(label, 10);
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            cell.Add(text);
            return cell;
        }

        static Color Gray(int level) => new Color32((byte)level, (byte)level, (byte)level, 255);
        static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            return row;
        }
        static Label Heading(string text)
        {
            var label = Text(text, 10);
            label.style.marginTop = 7;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }
        static Label Text(string text, int size)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = Gray(220);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = label.style.marginBottom = 2;
            return label;
        }
    }
}
