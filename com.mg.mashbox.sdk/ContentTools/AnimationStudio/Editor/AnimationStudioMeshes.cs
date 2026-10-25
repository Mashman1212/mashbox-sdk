using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [Serializable] private sealed class HiddenPreviewMesh
        {
            public AnimationTake actor;
            public string path;
        }
        [SerializeField] private bool animationMeshesExpanded;
        [SerializeField] private List<HiddenPreviewMesh> hiddenPreviewMeshes = new List<HiddenPreviewMesh>();
        private Vector2 animationMeshesScroll;
        [SerializeField] private bool humanMeshesCollapsed;
        [SerializeField] private List<AnimationTake> collapsedMeshActors = new List<AnimationTake>();

        private void DrawAnimationMeshes()
        {
            GUILayout.Space(6);
            var tab = GUILayoutUtility.GetRect(106, 24, GUILayout.Width(106), GUILayout.Height(24));
            EditorGUI.DrawRect(tab, tab.Contains(Event.current.mousePosition) ? new Color(.10f,.12f,.12f) : new Color(.035f,.04f,.04f));
            if (animationMeshesExpanded) EditorGUI.DrawRect(new Rect(tab.x+5,tab.yMax-2,tab.width-10,2),new Color(.5f,1,.87f));
            GUI.Label(tab, animationMeshesExpanded ? "\u2039  Meshes" : "\u203a  Meshes",
                new GUIStyle(StudioTheme.Styles.label) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(tab, new GUIContent("", "Show / tuck Meshes"), GUIStyle.none))
            { animationMeshesExpanded = !animationMeshesExpanded; viewport?.Repaint(); }

            // Apply stored preview visibility even while the panel is folded or a rig is rebuilt.
            ApplyMeshVisibility(rig, take);
            foreach (var actor in actorPreviews) ApplyMeshVisibility(actor.Rig, actor.Take);
            if (!animationMeshesExpanded) return;

            using (new GUILayout.VerticalScope(GUILayout.Width(280)))
            {
                var background = GUILayoutUtility.GetRect(280, 0, GUILayout.Width(280));
                EditorGUI.DrawRect(new Rect(background.x,background.y,280,Mathf.Min(360, viewport ? viewport.position.height-270 : 360)), StudioTheme.Current.Panel);
                animationMeshesScroll = GUILayout.BeginScrollView(animationMeshesScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar,
                    GUILayout.Width(280),
                    GUILayout.MaxHeight(Mathf.Max(80, Mathf.Min(360, viewport ? viewport.position.height - 270 : 360))));
                int count = DrawActorMeshes(rig, take, true);
                foreach (var actor in actorPreviews)
                    if (!IsAttachmentHidden(actor.Take) && actor.Rig != null && actor.Rig.Root && actor.Rig.Root.activeInHierarchy)
                        count += DrawActorMeshes(actor.Rig, actor.Take, false);
                if (count == 0) GUILayout.Label("No preview meshes", StudioTheme.Styles.miniLabel);
                GUILayout.EndScrollView();
            }
        }

        private void ApplyMeshVisibility(AuthoringRig preview, AnimationTake actor)
        {
            if (preview == null || !preview.Root) return;
            foreach (var renderer in preview.Root.GetComponentsInChildren<Renderer>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, preview.Root.transform);
                renderer.forceRenderingOff = hiddenPreviewMeshes.Exists(item => item.actor == actor && item.path == path);
            }
        }

        private int DrawActorMeshes(AuthoringRig preview, AnimationTake actor, bool primary)
        {
            if (preview == null || !preview.Root) return 0;
            var renderers = preview.Root.GetComponentsInChildren<Renderer>(true);
            bool collapsed = primary ? humanMeshesCollapsed : collapsedMeshActors.Contains(actor);
            string title = preview.Animator && preview.Animator.isHuman ? "Human" : (actor ? actor.name : "Vehicle");
            var group = GUILayoutUtility.GetRect(256,24,GUILayout.Width(256),GUILayout.Height(24));
            EditorGUI.DrawRect(group, new Color(.09f,.10f,.10f,1));
            bool expanded = EditorGUI.Foldout(new Rect(group.x+12,group.y,group.width-12,24), !collapsed, title, true);
            if (expanded == collapsed)
            {
                if (primary) humanMeshesCollapsed = !expanded;
                else { collapsedMeshActors.Remove(actor); if (!expanded) collapsedMeshActors.Add(actor); }
                viewport?.Repaint();
            }
            if (!expanded) return 1;
            int count = 0;
            foreach (var renderer in renderers)
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                count++;
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, preview.Root.transform);
                bool shown = !hiddenPreviewMeshes.Exists(item => item.actor == actor && item.path == path);
                var row = GUILayoutUtility.GetRect(256, 22, GUILayout.Width(256), GUILayout.Height(22));
                EditorGUI.DrawRect(row, new Color(.12f,.13f,.14f,1));
                bool next = GUI.Toggle(new Rect(row.x+3,row.y+3,18,16), shown, GUIContent.none);
                var iconRect = new Rect(row.x+26,row.y+3,16,16);
                DrawAnimationMeshIcon(iconRect, renderer.name);
                string label = renderer.name.Replace("Preview \u2022 ", "").Replace("Hat \u2022 ", "");
                GUI.Label(new Rect(row.x+48,row.y,row.width-52,22), new GUIContent(label, path),
                    new GUIStyle(StudioTheme.Styles.label) { clipping = TextClipping.Clip, wordWrap = false });
                if (next != shown)
                {
                    hiddenPreviewMeshes.RemoveAll(item => item.actor == actor && item.path == path);
                    if (!next) hiddenPreviewMeshes.Add(new HiddenPreviewMesh { actor = actor, path = path });
                    ApplyMeshVisibility(preview, actor);
                    viewport?.Repaint();
                }
            }
            return count;
        }
        private static void DrawAnimationMeshIcon(Rect rect, string meshName)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = Handles.color;
            Handles.BeginGUI();
            Handles.color = new Color(.5f,1,.87f);
            Vector3 P(float x,float y) => new Vector3(rect.x+x,rect.y+y,0);
            void Line(params Vector3[] points) => Handles.DrawAAPolyLine(1.3f,points);
            try
            {
                string name = meshName.ToLowerInvariant();
                if (name.Contains("body"))
                {
                    Handles.DrawWireDisc(P(8,2),Vector3.forward,1.8f);
                    Line(P(2,6),P(8,5),P(14,6));
                    Line(P(8,5),P(8,10),P(4,15)); Line(P(8,10),P(12,15));
                }
                else if (name.Contains("bust") || name.Contains("head") || name.Contains("hat") || name.Contains("hair"))
                {
                    Handles.DrawWireDisc(P(8,4),Vector3.forward,2.6f);
                    Line(P(6,7),P(6,9),P(2,11),P(2,14),P(14,14),P(14,11),P(10,9),P(10,7));
                }
                else
                {
                    Line(P(5,2),P(1,5),P(3,8),P(5,7),P(5,14),P(11,14),P(11,7),P(13,8),P(15,5),P(11,2));
                    Line(P(5,2),P(6,4),P(10,4),P(11,2));
                }
            }
            finally { Handles.color = old; Handles.EndGUI(); }
        }
    }
}
