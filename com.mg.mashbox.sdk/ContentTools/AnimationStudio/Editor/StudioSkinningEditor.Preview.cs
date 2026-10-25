using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private AnimationClip previewClip;
        private Animator previewAnimator;
        private PlayableGraph previewGraph;
        private AnimationClipPlayable previewPlayable;
        private Vector3[] previewRestPositions, previewRestScales;
        private bool previewPlaying;
        private float previewTime;
        private double previewLastTick;
        private void InitializePreviewAnimation()
        {
            previewRestPositions = bones.Select(b => b.localPosition).ToArray();
            previewRestScales = bones.Select(b => b.localScale).ToArray();
            previewClip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(Document.skeleton))
                .OfType<AnimationClip>().FirstOrDefault(c => string.Equals(c.name,"Skinning Test",StringComparison.OrdinalIgnoreCase));
            EditorApplication.update += TickPreviewAnimation;
        }
        private void EnsurePreviewGraph()
        {
            if (previewGraph.IsValid()) return;
            previewAnimator = skeleton.GetComponent<Animator>();
            if (!previewAnimator) previewAnimator = skeleton.AddComponent<Animator>();
            previewAnimator.enabled = true; previewAnimator.applyRootMotion = false;
            previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            previewGraph = PlayableGraph.Create("Studio skinning test");
            previewGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            previewPlayable = AnimationClipPlayable.Create(previewGraph, previewClip);
            previewPlayable.SetApplyFootIK(false); previewPlayable.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(previewGraph,"Skinning test",previewAnimator).SetSourcePlayable(previewPlayable);
            previewGraph.Play();
        }
        private void SamplePreviewAnimation(float time, bool refresh)
        {
            if (!previewClip) return;
            EndStroke(); EnsurePreviewGraph();
            previewTime = Mathf.Clamp(time,0,previewClip.length);
            previewPlayable.SetTime(previewTime);
            previewGraph.Evaluate(0);
            cachedPart = -1;
            if (refresh) RefreshSurface();
            repaint();
        }
        private void PausePreviewAnimation()
        {
            previewPlaying = false;
            if (cachedPart < 0) RefreshSurface();
            repaint();
        }
        private void ResetPreviewAnimation()
        {
            EndStroke(); previewPlaying = false; previewTime = 0;
            if (previewGraph.IsValid()) previewGraph.Destroy();
            if (previewAnimator) previewAnimator.enabled = false;
            for (int i=0;i<bones.Length;i++)
            {
                bones[i].localPosition = previewRestPositions[i];
                bones[i].localRotation = restRotations[i]; bones[i].localScale = previewRestScales[i];
            }
            RefreshSurface(); repaint();
        }
        private void TickPreviewAnimation()
        {
            if (!previewPlaying || !previewClip) return;
            if (!(lastView is MashBoxStudioViewport view) || !view || !view.IsSkinPreviewScene(Scene)) { previewPlaying = false; return; }
            double now = EditorApplication.timeSinceStartup;
            if (now-previewLastTick < 1.0/30) return;
            float next = previewTime + (float)Math.Min(now-previewLastTick,.1);
            previewLastTick = now;
            Run(() =>
            {
                try { SamplePreviewAnimation(previewClip.length > 0 ? next % previewClip.length : 0,false); }
                catch { previewPlaying = false; throw; }
            });
        }
        internal void DrawPreviewTimeline()
        {
            var row = GUILayoutUtility.GetRect(0,36,GUILayout.ExpandWidth(true));
            float y = row.y + 5, x = row.x + 6;
            using (new EditorGUI.DisabledScope(!previewClip))
            {
                if (GUI.Button(new Rect(x,y,26,26),new GUIContent(previewPlaying ? "Ⅱ" : "▶", "Play / pause Skinning Test")))
                {
                    if (previewPlaying) PausePreviewAnimation();
                    else { EndStroke(); previewLastTick = EditorApplication.timeSinceStartup; previewPlaying = true; }
                }
            }
            x += 32;
            using (new EditorGUI.DisabledScope(!previewClip))
            {
                if (GUI.Button(new Rect(x,y,26,26),new GUIContent("‹", "Previous frame — pause and step back one frame")))
                    Run(() => StepPreviewFrame(-1));
                x += 32;
                if (GUI.Button(new Rect(x,y,26,26),new GUIContent("›", "Next frame — pause and step forward one frame")))
                    Run(() => StepPreviewFrame(1));
                x += 32;
            }
            if (GUI.Button(new Rect(x,y,26,26),new GUIContent("↺", "Return to bind pose"))) Run(ResetPreviewAnimation);
            x += 36;
            const float poseWidth = 28;
            using (new EditorGUI.DisabledScope(!previewClip))
            {
                if (DrawPosePresetButton(new Rect(x,y,poseWidth,26), false))
                    Run(() => JumpToPreviewFrame(0));
                x += poseWidth + 6;
                if (DrawPosePresetButton(new Rect(x,y,poseWidth,26), true))
                    Run(() => JumpToPreviewFrame(2));
                x += poseWidth + 6;
                if (DrawPosePresetButton(new Rect(x,y,poseWidth,26), true, true))
                    Run(() => JumpToPreviewFrame(3));
                x += poseWidth + 10;
            }
            var label = new GUIStyle(StudioTheme.Styles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            if (row.width >= 658)
            {
                GUI.Label(new Rect(x,y,84,26),"Skinning Test",label); x += 90;
            }
            if (!previewClip)
            {
                GUI.Label(new Rect(x,y,Mathf.Max(0,row.xMax-x-6),26),new GUIContent("Test clip unavailable", "No Skinning Test clip found on the SDK Skeleton."),label);
                return;
            }
            const float timeWidth = 94;
            float timeX = row.xMax - timeWidth - 8;
            var track = new Rect(x,row.y + 10,Mathf.Max(30,timeX-x-12),16);
            EditorGUI.BeginChangeCheck();
            float next = GUI.HorizontalSlider(track,previewTime,0,Mathf.Max(.001f,previewClip.length));
            if (EditorGUI.EndChangeCheck()) { previewPlaying = false; Run(() => SamplePreviewAnimation(next,true)); }
            label.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(timeX,y,timeWidth,26),$"{previewTime:0.00} / {previewClip.length:0.00}s",label);
        }
        private static bool DrawPosePresetButton(Rect rect, bool aPose, bool plus = false)
        {
            bool clicked = GUI.Button(rect,new GUIContent("",plus ? "A-pose+ — Skinning Test frame 3" : aPose ? "A-pose — Skinning Test frame 2" : "T-pose — Skinning Test frame 0"));
            if (Event.current.type != EventType.Repaint) return clicked;
            var color = Handles.color; var matrix = Handles.matrix;
            Handles.BeginGUI();
            Handles.matrix = Matrix4x4.identity;
            Vector3 P(float x,float y) => new Vector3(rect.center.x+x,rect.center.y+y,0);
            try
            {
                Handles.color = GUI.enabled ? new Color(.5f,1,.87f) : new Color(.4f,.45f,.45f);
                Handles.DrawWireDisc(P(0,-7),Vector3.forward,2.5f);
                Handles.DrawAAPolyLine(1.6f,P(0,-3),P(0,4),P(-4,10));
                Handles.DrawAAPolyLine(1.6f,P(0,4),P(4,10));
                Handles.DrawAAPolyLine(1.6f,P(-9,aPose ? 5 : -2),P(0,-2),P(9,aPose ? 5 : -2));
                if (plus)
                {
                    Handles.DrawAAPolyLine(1.6f,P(6,-7),P(12,-7));
                    Handles.DrawAAPolyLine(1.6f,P(9,-10),P(9,-4));
                }
            }
            finally { Handles.EndGUI(); Handles.color=color; Handles.matrix=matrix; }
            return clicked;
        }
        private void StepPreviewFrame(int direction)
        {
            if (!previewClip) return;
            float fps = Mathf.Max(1,previewClip.frameRate);
            float current = previewTime * fps;
            // Scrubbing can stop between frames; step to the adjacent frame in
            // the requested direction without rounding past it.
            int frame = direction < 0 ? Mathf.CeilToInt(current - .0001f) - 1
                : Mathf.FloorToInt(current + .0001f) + 1;
            int last = Mathf.FloorToInt(previewClip.length * fps + .0001f);
            JumpToPreviewFrame(Mathf.Clamp(frame,0,last));
        }
        private void JumpToPreviewFrame(int frame)
        {
            if (!previewClip) return;
            previewPlaying = false;
            SamplePreviewAnimation(frame / Mathf.Max(1,previewClip.frameRate),true);
        }
        private void DisposePreviewAnimation()
        {
            EditorApplication.update -= TickPreviewAnimation; previewPlaying = false;
            if (previewGraph.IsValid()) previewGraph.Destroy();
        }
    }
}
