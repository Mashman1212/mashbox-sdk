using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    public sealed class AnimationStudioViewport : SceneView
    {
        [SerializeField] internal AnimationStudioWindow Studio;
        [InitializeOnLoadMethod]
        private static void RestoreStudioViews()
        {
            EditorApplication.delayCall += () =>
            {
                foreach (var view in Resources.FindObjectsOfTypeAll<AnimationStudioViewport>())
#if MashBoxDev
                    if (view) AnimationStudioWindow.RestoreExistingViewport(view);
#else
                    if (view) view.Close();
#endif
            };
        }
        private IMGUIContainer timeline;
        private IMGUIContainer inspector;

        internal void BindInspector(System.Action draw)
        {
            if (inspector == null)
            {
                inspector = new IMGUIContainer();
                inspector.name = "mashbox-animation-inspector";
                inspector.style.position = Position.Absolute;
                inspector.style.top = 8;
                inspector.style.right = 8;
                inspector.style.width = 350;
                inspector.style.backgroundColor = new Color(0.13f, 0.14f, 0.16f, 0.98f);
                rootVisualElement.Add(inspector);
            }
            inspector.onGUIHandler = draw;
            SetInspectorExpanded(true);
            inspector.BringToFront();
        }

        internal void SetInspectorExpanded(bool expanded)
        {
            if (inspector == null) return;
            inspector.style.bottom = expanded ? new StyleLength(116) : new StyleLength(StyleKeyword.Auto);
            inspector.style.height = expanded ? new StyleLength(StyleKeyword.Auto) : new StyleLength(28);
        }

        internal void BindTimeline(System.Action draw)
        {
            minSize = new Vector2(700, 400);
            if (timeline == null)
            {
                timeline = new IMGUIContainer();
                timeline.name = "mashbox-animation-timeline";
                timeline.style.position = Position.Absolute;
                timeline.style.left = 0;
                timeline.style.right = 0;
                timeline.style.bottom = 0;
                timeline.style.height = 108;
                timeline.style.backgroundColor = new Color(0.11f, 0.12f, 0.14f);
                rootVisualElement.Add(timeline);
            }
            timeline.onGUIHandler = draw;
            timeline.BringToFront();
        }

        internal void Bind(Scene scene)
        {
            customScene = scene;
            titleContent = new GUIContent("Animation Studio");
            sceneLighting = true;
            showGrid = false; // Draw our own ground reference in the isolated preview scene.
            sceneViewState.showSkybox = false;
            sceneViewState.showFog = false;
            sceneViewState.showImageEffects = false;
            rotation = Quaternion.Euler(10, 180, 0);
            foreach (var overlay in overlayCanvas.overlays) overlay.displayed = false;
            // SceneView's stage handling otherwise culls ordinary preview scenes.
            typeof(SceneView).GetProperty("overrideSceneCullingMask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(this, EditorSceneManager.GetSceneCullingMask(scene));
            Repaint();
        }

        internal void DrawGroundGrid()
        {
            if (Event.current.type != EventType.Repaint) return;
            var previousColor = Handles.color;
            var previousDepth = Handles.zTest;
            var previousMatrix = Handles.matrix;
            try
            {
                Handles.matrix = Matrix4x4.identity;
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                const float spacing = 0.25f;
                const int steps = 40;
                const float extent = steps * spacing;
                // Fixed, bounded geometry: no scene objects, asset writes, or picking controls.
                for (int i = -steps; i <= steps; i++)
                {
                    if (i == 0) continue;
                    float coordinate = i * spacing;
                    float fade = 1f - 0.65f * Mathf.Abs(i) / steps;
                    Handles.color = new Color(0.65f, 0.7f, 0.75f, (i % 4 == 0 ? 0.32f : 0.14f) * fade);
                    Handles.DrawLine(new Vector3(-extent, 0, coordinate), new Vector3(extent, 0, coordinate));
                    Handles.DrawLine(new Vector3(coordinate, 0, -extent), new Vector3(coordinate, 0, extent));
                }
                Handles.color = new Color(0.9f, 0.3f, 0.25f, 0.65f);
                Handles.DrawLine(new Vector3(-extent, 0, 0), new Vector3(extent, 0, 0));
                Handles.color = new Color(0.25f, 0.5f, 1f, 0.65f);
                Handles.DrawLine(new Vector3(0, 0, -extent), new Vector3(0, 0, extent));
            }
            finally
            {
                Handles.color = previousColor;
                Handles.zTest = previousDepth;
                Handles.matrix = previousMatrix;
            }
        }
    }
}
