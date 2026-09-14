using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private int timelineRangeStart=-1, timelineRangeEnd=-1;
        private bool timelineRangeDrag;
        private AnimationTake timelineSelectionActor, timelineClipboardActor;
        private List<PoseKey> timelineSelectionKeys, timelineClipboardTrack;
        private List<PoseKey> timelineClipboard = new List<PoseKey>();
        private int timelineClipboardOrigin;
        private int TimelineSharedFrame(AnimationTake actor,int f) => Mathf.RoundToInt((float)f/actor.frameRate*take.frameRate);
        private bool TimelineInRange(AnimationTake actor,PoseKey key) => timelineRangeStart>=0 &&
            TimelineSharedFrame(actor,key.frame)>=Mathf.Min(timelineRangeStart,timelineRangeEnd) &&
            TimelineSharedFrame(actor,key.frame)<=Mathf.Max(timelineRangeStart,timelineRangeEnd);
        private void CopyTimelineKeys(AnimationTake actor,List<PoseKey> keys)
        {
            timelineClipboard=keys.Where(k=>TimelineInRange(actor,k)).Select(k=>k.Copy(k.frame)).ToList();
            timelineClipboardActor=actor; timelineClipboardTrack=keys;
            timelineClipboardOrigin=Mathf.RoundToInt((float)Mathf.Min(timelineRangeStart,timelineRangeEnd)/take.frameRate*actor.frameRate);
        }
        private void DeleteTimelineKeys(AnimationTake actor,List<PoseKey> keys)
        {
            FinishSceneEdit(); FinishActorEdits(); playing=false;
            // Base evaluation needs at least one pose; an animation layer may be empty.
            if(keys==actor.keys && keys.All(k=>TimelineInRange(actor,k)))
            { message="Keep at least one base pose key. Layers may have all their keys deleted."; Repaint(); return; }
            Undo.RegisterCompleteObjectUndo(actor,"Delete timeline keys");
            keys.RemoveAll(k=>TimelineInRange(actor,k));
            EditorUtility.SetDirty(actor); Save(); Seek(frame);
        }
        private void PasteTimelineKeys(AnimationTake actor,List<PoseKey> keys,int destination)
        {
            if(timelineClipboard.Count==0 || actor!=timelineClipboardActor || keys!=timelineClipboardTrack) return;
            FinishSceneEdit(); FinishActorEdits(); playing=false;
            Undo.RegisterCompleteObjectUndo(actor,"Paste timeline keys");
            if(actor!=take) Undo.RecordObject(take,"Extend timeline");
            int origin=Mathf.RoundToInt((float)destination/take.frameRate*actor.frameRate);
            foreach(var source in timelineClipboard)
            {
                int target=origin+source.frame-timelineClipboardOrigin;
                keys.RemoveAll(k=>k.frame==target); keys.Add(source.Copy(target));
            }
            keys.Sort((a,b)=>a.frame.CompareTo(b.frame));
            actor.lastFrame=Mathf.Max(actor.lastFrame,keys.Max(k=>k.frame));
            take.lastFrame=Mathf.Max(take.lastFrame,Mathf.CeilToInt((float)actor.lastFrame/actor.frameRate*take.frameRate));
            timelineRangeStart=destination;
            timelineRangeEnd=TimelineSharedFrame(actor,origin+timelineClipboard.Max(k=>k.frame)-timelineClipboardOrigin);
            EditorUtility.SetDirty(actor); Save(); Seek(frame);
        }
        private void TimelineContextMenu(AnimationTake actor,List<PoseKey> keys,int atFrame)
        {
            var menu=new GenericMenu();
            int count=keys.Count(k=>TimelineInRange(actor,k));
            if(count>0) menu.AddItem(new GUIContent("Copy selected keys"),false,()=>CopyTimelineKeys(actor,keys));
            else menu.AddDisabledItem(new GUIContent("Copy selected keys"));
            if(count>0 && (keys!=actor.keys || count<keys.Count))
                menu.AddItem(new GUIContent("Delete selected keys"),false,()=>DeleteTimelineKeys(actor,keys));
            else menu.AddDisabledItem(new GUIContent("Delete selected keys"));
            menu.AddSeparator("");
            if(timelineClipboard.Count>0 && actor==timelineClipboardActor && keys==timelineClipboardTrack)
                menu.AddItem(new GUIContent("Paste keys at frame "+atFrame+" (replace matching keys)"),false,()=>PasteTimelineKeys(actor,keys,atFrame));
            else menu.AddDisabledItem(new GUIContent("Paste keys (copy from this actor and layer first)"));
            menu.AddItem(new GUIContent("Clear range selection"),false,()=>{ timelineRangeStart=timelineRangeEnd=-1; viewport?.Repaint(); });
            if(MotionGenerationAvailable)
            {
                menu.AddSeparator("");
                if(!MotionRunning && timelineRangeStart>=0 && timelineRangeEnd!=timelineRangeStart)
                    menu.AddItem(new GUIContent("Generate AI in-betweens…"),false,()=>Guard(()=>StartInbetween(actor,Mathf.Min(timelineRangeStart,timelineRangeEnd),Mathf.Max(timelineRangeStart,timelineRangeEnd))));
                else menu.AddDisabledItem(new GUIContent("Generate AI in-betweens (select a range)"));
            }
            menu.ShowAsContext();
        }
        private void DrawViewportTimeline()
        {
            if (!this) return;
            if (!take || rig == null || pose == null)
            {
                GUILayout.Space(15);
                GUILayout.Label("Create or open an animation take to use the timeline.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            GUILayout.Space(5);
            var timelineActor=OutfitActor;
            var layerKeys=timelineActor.activeLayer>=0 && timelineActor.activeLayer<timelineActor.layers.Count ? timelineActor.layers[timelineActor.activeLayer].keys : timelineActor.keys;
            if(timelineSelectionActor!=timelineActor || timelineSelectionKeys!=layerKeys)
            { timelineRangeStart=timelineRangeEnd=-1; timelineSelectionActor=timelineActor; timelineSelectionKeys=layerKeys; }
            var keyFrames=layerKeys.Select(k=>Mathf.RoundToInt((float)k.frame/timelineActor.frameRate*take.frameRate)).ToArray();
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(6);
                if (GUILayout.Button(new GUIContent("|<", "First frame"), GUILayout.Width(28))) ScrubViewport(0);
                if (GUILayout.Button(new GUIContent("<K", "Previous key"), GUILayout.Width(30)))
                    ScrubViewport(keyFrames.Where(f=>f<frame).DefaultIfEmpty(0).Max());
                if (GUILayout.Button(new GUIContent("<", "Previous frame"), GUILayout.Width(25))) ScrubViewport(frame - 1);
                if (GUILayout.Button(playing ? "Pause" : "Play", GUILayout.Width(48))) TogglePlay();
                if (GUILayout.Button(new GUIContent(">", "Next frame"), GUILayout.Width(25))) ScrubViewport(frame + 1);
                if (GUILayout.Button(new GUIContent("K>", "Next key"), GUILayout.Width(30)))
                    ScrubViewport(keyFrames.Where(f=>f>frame).DefaultIfEmpty(take.lastFrame).Min());
                if (GUILayout.Button(new GUIContent(">|", "Last frame"), GUILayout.Width(28))) ScrubViewport(take.lastFrame);
                GUILayout.Space(6);
                EditorGUI.BeginChangeCheck();
                int next = EditorGUILayout.DelayedIntField(frame, GUILayout.Width(46));
                if (EditorGUI.EndChangeCheck()) ScrubViewport(next);
                GUILayout.Label("/ " + take.lastFrame, GUILayout.Width(58));
                GUILayout.FlexibleSpace();
                bool nextAuto = GUILayout.Toggle(autoKey, "Auto Key", "Button", GUILayout.Width(70));
                if (nextAuto != autoKey) { FinishSceneEdit(); autoKey = nextAuto; Repaint(); }
                if (GUILayout.Button("Key [S]", GUILayout.Width(62))) KeyCurrentActor();
                GUILayout.Space(6);
            }

            Rect ruler = GUILayoutUtility.GetRect(1, 51, GUILayout.ExpandWidth(true));
            ruler.xMin += 10; ruler.xMax -= 10;
            int id = GUIUtility.GetControlID("MashBoxViewportScrubber".GetHashCode(), FocusType.Keyboard, ruler);
            var evt = Event.current;
            if (evt.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(ruler, new Color(0.065f, 0.075f, 0.09f));
                int end = Mathf.Max(1, take.lastFrame);
                if(timelineRangeStart>=0)
                {
                    float left=ruler.x+ruler.width*Mathf.Min(timelineRangeStart,timelineRangeEnd)/end;
                    float right=ruler.x+ruler.width*Mathf.Max(timelineRangeStart,timelineRangeEnd)/end;
                    EditorGUI.DrawRect(new Rect(left-3,ruler.y,Mathf.Max(6,right-left+6),ruler.height),new Color(.55f,.22f,.18f,.65f));
                }
                int step = Mathf.Max(1, Mathf.CeilToInt(end / Mathf.Max(1f, ruler.width / 55f)));
                if (step > 5) step = Mathf.CeilToInt(step / 5f) * 5;
                for (int f = 0; f <= end; f += step)
                {
                    float x = ruler.x + ruler.width * f / end;
                    EditorGUI.DrawRect(new Rect(x, ruler.y + 20, 1, 10), new Color(0.45f, 0.48f, 0.53f));
                    GUI.Label(new Rect(x + 3, ruler.y + 1, 48, 18), f.ToString(), EditorStyles.miniLabel);
                }
                foreach (var keyFrame in keyFrames)
                {
                    float x = ruler.x + ruler.width * keyFrame / end;
                    EditorGUI.DrawRect(new Rect(x - 2, ruler.y + 32, 4, 14), new Color(0.2f, 0.8f, 0.7f));
                }
                float cursor = ruler.x + ruler.width * frame / end;
                EditorGUI.DrawRect(new Rect(cursor - 1, ruler.y, 2, ruler.height), new Color(1f, 0.75f, 0.25f));
            }
            // SceneView-hosted IMGUIContainers do not reliably synthesize ContextClick.
            // Consume the actual right-button press before viewport navigation can claim it.
            if(((evt.type==EventType.MouseDown && evt.button==1) || evt.type==EventType.ContextClick) && ruler.Contains(evt.mousePosition))
            {
                int at=FrameAtMouse(ruler,evt.mousePosition.x);
                var nearby=keyFrames.OrderBy(f=>Mathf.Abs(ruler.x+ruler.width*f/Mathf.Max(1,take.lastFrame)-evt.mousePosition.x)).Take(1).ToArray();
                if(nearby.Length>0 && Mathf.Abs(ruler.x+ruler.width*nearby[0]/Mathf.Max(1,take.lastFrame)-evt.mousePosition.x)<=6) at=nearby[0];
                if(timelineRangeStart<0 || at<Mathf.Min(timelineRangeStart,timelineRangeEnd) || at>Mathf.Max(timelineRangeStart,timelineRangeEnd))
                    timelineRangeStart=timelineRangeEnd=at;
                GUIUtility.keyboardControl=id;
                TimelineContextMenu(timelineActor,layerKeys,at); evt.Use();
            }
            if(evt.type==EventType.KeyDown && GUIUtility.keyboardControl==id && !EditorGUIUtility.editingTextField)
            {
                if(evt.keyCode==KeyCode.Delete || evt.keyCode==KeyCode.Backspace)
                { if(timelineRangeStart>=0) DeleteTimelineKeys(timelineActor,layerKeys); evt.Use(); }
                else if((evt.control || evt.command) && evt.keyCode==KeyCode.C)
                { CopyTimelineKeys(timelineActor,layerKeys); evt.Use(); }
                else if((evt.control || evt.command) && evt.keyCode==KeyCode.V)
                { PasteTimelineKeys(timelineActor,layerKeys,frame); evt.Use(); }
            }
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (evt.button != 0 || !ruler.Contains(evt.mousePosition)) break;
                    GUIUtility.hotControl = id;
                    GUIUtility.keyboardControl = id;
                    timelineRangeDrag=evt.shift;
                    if(timelineRangeDrag)
                    {
                        FinishSceneEdit(); FinishActorEdits(); playing=false;
                        timelineRangeStart=timelineRangeEnd=FrameAtMouse(ruler,evt.mousePosition.x); viewport?.Repaint();
                    }
                    else { int at=FrameAtMouse(ruler,evt.mousePosition.x); timelineRangeStart=timelineRangeEnd=at; ScrubViewport(at); }
                    evt.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    int next = FrameAtMouse(ruler, evt.mousePosition.x);
                    if(timelineRangeDrag) { timelineRangeEnd=next; viewport?.Repaint(); }
                    else if (next != frame) { timelineRangeStart=timelineRangeEnd=next; ScrubViewport(next); }
                    evt.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    evt.Use();
                    break;
            }
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                string layerLabel=timelineActor.activeLayer>=0 && timelineActor.activeLayer<timelineActor.layers.Count ? timelineActor.layers[timelineActor.activeLayer].name : "Base motion";
                GUILayout.Label($"{(timelineRangeStart>=0 ? "Selected "+Mathf.Min(timelineRangeStart,timelineRangeEnd)+"–"+Mathf.Max(timelineRangeStart,timelineRangeEnd)+" · " : "Shift-drag: select · Right-click: edit · ")}{(float)frame / take.frameRate:0.000}s   ·   {take.frameRate} fps   ·   {layerKeys.Count} keys · {layerLabel}", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(unkeyedPose ? "Unkeyed changes · S: pose · Shift+W: position · Shift+E: rotation" : autoKey ? "AUTO KEY ON" : "S: pose · Shift+W: position · Shift+E: rotation", EditorStyles.miniLabel);
                GUILayout.Space(10);
            }
            HandleKeys(evt);
        }

        private int FrameAtMouse(Rect ruler, float x) =>
            Mathf.RoundToInt(Mathf.Clamp01((x - ruler.x) / Mathf.Max(1, ruler.width)) * take.lastFrame);

        private void ScrubViewport(int next)
        {
            playing = false;
            Seek(Mathf.Clamp(next, 0, take.lastFrame));
        }
    }
}
