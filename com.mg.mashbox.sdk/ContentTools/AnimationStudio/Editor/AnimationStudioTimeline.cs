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
        [SerializeField] private AnimationTake timelineViewTake;
        [SerializeField] private int timelineViewFirst;
        [SerializeField] private int timelineViewLast = -1;
        [SerializeField] private int timelineViewLength;

        private void EnsureTimelineView()
        {
            if (!take) return;
            if (timelineViewTake != take || timelineViewLast < 0)
            { timelineViewTake = take; timelineViewFirst = 0; timelineViewLast = take.lastFrame; }
            else if (timelineViewLength != take.lastFrame && timelineViewLast == timelineViewLength)
                timelineViewLast = take.lastFrame;
            timelineViewFirst = Mathf.Clamp(timelineViewFirst, 0, Mathf.Max(0, take.lastFrame - 1));
            timelineViewLast = Mathf.Clamp(timelineViewLast, timelineViewFirst + 1, Mathf.Max(1, take.lastFrame));
            timelineViewLength = take.lastFrame;
        }

        private void SetTimelineView(int first, int last)
        {
            EnsureTimelineView();
            timelineViewFirst = Mathf.Clamp(first, 0, Mathf.Max(0, take.lastFrame - 1));
            timelineViewLast = Mathf.Clamp(last, timelineViewFirst + 1, take.lastFrame);
            viewport?.Repaint(); Repaint();
        }

        private void SetTimelineViewEnd(int last)
        {
            EnsureTimelineView();
            last = Mathf.Clamp(last, 1, 18000);
            int first = timelineViewFirst;
            if (last > take.lastFrame) SetTimelineClipEnd(last);
            SetTimelineView(first, last);
        }

        private void SetTimelineClipEnd(int last)
        {
            if (!take) return;
            last = Mathf.Clamp(last, 1, 18000);
            if (last == take.lastFrame) return;
            EnsureTimelineView(); FlushEdits(); playing = false;
            Undo.RegisterCompleteObjectUndo(take, "Change animation clip end");
            take.lastFrame = last;
            // Keep keys beyond the playback end so shortening is reversible without data loss.
            EnsureTimelineView(); Save(); FlushSave(); Seek(Mathf.Min(frame, last));
            timelineRangeStart = timelineRangeEnd = -1;
        }

        private float TimelineFrameX(Rect ruler, int atFrame) => ruler.x + ruler.width *
            (atFrame - timelineViewFirst) / Mathf.Max(1f, timelineViewLast - timelineViewFirst);

        private void ZoomTimelineView(float scale, int anchor)
        {
            EnsureTimelineView();
            int oldSpan = timelineViewLast - timelineViewFirst;
            int span = Mathf.Clamp(Mathf.RoundToInt(oldSpan * scale), 1, take.lastFrame);
            float fraction = Mathf.Clamp01((anchor - timelineViewFirst) / (float)oldSpan);
            int first = Mathf.Clamp(Mathf.RoundToInt(anchor - span * fraction), 0, take.lastFrame - span);
            SetTimelineView(first, first + span);
        }

        private void DrawTimelineViewingRange()
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                GUILayout.Label(new GUIContent("Clip end", "Last frame of the animation (frame numbering starts at 0). Later keys are preserved when shortening."), StudioTheme.Styles.miniLabel, GUILayout.Width(48));
                EditorGUI.BeginChangeCheck();
                int end = EditorGUILayout.DelayedIntField(take.lastFrame, GUILayout.Width(58));
                if (EditorGUI.EndChangeCheck()) SetTimelineClipEnd(end);
                GUILayout.Space(12);
                GUILayout.Label(new GUIContent("View", "Visible frame range. Enter a higher end frame to extend the clip. Drag the range handles to zoom or its center to pan."), StudioTheme.Styles.miniLabel, GUILayout.Width(30));
                EditorGUI.BeginChangeCheck();
                int first = EditorGUILayout.DelayedIntField(timelineViewFirst, GUILayout.Width(52));
                if (EditorGUI.EndChangeCheck()) SetTimelineView(first, timelineViewLast);
                float min = timelineViewFirst, max = timelineViewLast;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.MinMaxSlider(ref min, ref max, 0, take.lastFrame, GUILayout.MinWidth(70));
                if (EditorGUI.EndChangeCheck()) SetTimelineView(Mathf.RoundToInt(min), Mathf.RoundToInt(max));
                EditorGUI.BeginChangeCheck();
                int last = EditorGUILayout.DelayedIntField(timelineViewLast, GUILayout.Width(52));
                if (EditorGUI.EndChangeCheck()) SetTimelineViewEnd(last);
                if (GUILayout.Button(new GUIContent("Fit", "Show the whole clip"), GUILayout.Width(35))) SetTimelineView(0, take.lastFrame);
                GUILayout.Space(10);
            }
        }
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
            if(timelineRangeStart<0) return;
            DeleteSelectedControlKeys(actor,keys,Mathf.Min(timelineRangeStart,timelineRangeEnd),Mathf.Max(timelineRangeStart,timelineRangeEnd));
        }

        private void DeleteSelectedAtCurrentFrame()
        {
            var actor=OutfitActor;
            var keys=actor.activeLayer>=0 && actor.activeLayer<actor.layers.Count ? actor.layers[actor.activeLayer].keys : actor.keys;
            DeleteSelectedControlKeys(actor,keys,frame,frame);
        }

        private void DeleteSelectedControlKeys(AnimationTake actor,List<PoseKey> keys,int first,int last)
        {
            if(rigSelectionExplicit && rigSelection.Count==0) return;
            FlushEdits(); playing=false;
            int sharedFps=take.frameRate;
            var targets=rigSelection.Count>0 ? rigSelection.Select(s=>s.Actor).Where(a=>a).Distinct().ToArray() : new[]{actor};
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Delete selected control keys");
            foreach(var target in targets) InSelectedActor(target,()=>
            {
                var track=target==actor ? keys : EditingKeys;
                DeleteControlChannels(track,first,last,sharedFps);
            });
            FlushSave(); Seek(frame); Undo.IncrementCurrentGroup();
        }

        private void SelectedControlMasks(out int[] bones,out int[] effectors)
        {
            var boneMask=new int[rig.Bones.Length]; var effectorMask=new int[4];
            System.Action<int,bool,bool> select=(index,ik,pole)=>
            {
                if(ik && !IsBike) { if(index>=0 && index<4) effectorMask[index]|=pole ? 4 : 11; }
                else
                {
                    int bone=IsBike ? System.Array.IndexOf(rig.Bones,BikeJoint(index)) : index;
                    if(bone>=0 && bone<boneMask.Length) boneMask[bone]=7;
                }
            };
            if(rigSelection.Count>0)
                foreach(var item in rigSelection.Where(s=>s.Actor==take)) select(item.Index,item.IK,item.Pole);
            else if(!rigSelectionExplicit) select(control,ikMode,poleMode);
            bones=boneMask; effectors=effectorMask;
        }

        private List<PoseKey> SelectedTimelineKeys(AnimationTake actor,List<PoseKey> keys)
        {
            var selected=new List<PoseKey>();
            InSelectedActor(actor,()=>
            {
                SelectedControlMasks(out var bones,out var effectors);
                if(!bones.Any(v=>v!=0) && !effectors.Any(v=>v!=0)) return;
                var boneIndices=Enumerable.Range(0,bones.Length).Where(i=>bones[i]!=0).ToArray();
                var effectorIndices=Enumerable.Range(0,effectors.Length).Where(i=>effectors[i]!=0).ToArray();
                foreach(var key in keys)
                {
                    bool full=keys==actor.keys && !key.selective;
                    bool matches=full || boneIndices.Any(i=>key.boneChannels!=null && i<key.boneChannels.Length && (key.boneChannels[i]&bones[i])!=0) ||
                        effectorIndices.Any(i=>key.effectorChannels!=null && i<key.effectorChannels.Length && (key.effectorChannels[i]&effectors[i])!=0);
                    if(matches) selected.Add(key);
                }
            });
            return selected;
        }

        private int[] SelectedTimelineFrames(AnimationTake actor,List<PoseKey> keys)
        {
            int sharedFps=take.frameRate;
            var targets=rigSelection.Count>0 ? rigSelection.Select(s=>s.Actor).Where(a=>a).Distinct().ToArray() : new[]{actor};
            var frames=new List<int>();
            foreach(var target in targets)
            {
                var track=target==actor ? keys : target.activeLayer>=0 && target.activeLayer<target.layers.Count ? target.layers[target.activeLayer].keys : target.keys;
                frames.AddRange(SelectedTimelineKeys(target,track).Select(k=>Mathf.RoundToInt((float)k.frame/target.frameRate*sharedFps)));
            }
            return frames.Distinct().OrderBy(f=>f).ToArray();
        }

        private void DeleteControlChannels(List<PoseKey> keys,int first,int last,int sharedFps)
        {
            SelectedControlMasks(out var bones,out var effectors);
            if(!bones.Any(v=>v!=0) && !effectors.Any(v=>v!=0)) return;
            bool baseTrack=keys==take.keys;
            var affected=keys.Where(k=>Mathf.RoundToInt((float)k.frame/take.frameRate*sharedFps)>=first &&
                Mathf.RoundToInt((float)k.frame/take.frameRate*sharedFps)<=last).ToArray();
            if(affected.Length==0) return;
            Undo.RegisterCompleteObjectUndo(take,"Delete selected control keys");
            foreach(var key in affected)
            {
                if(!key.selective && baseTrack)
                {
                    key.boneChannels=Enumerable.Repeat(7,key.bones.Length).ToArray();
                    key.effectorChannels=Enumerable.Repeat(15,key.effectors.Length).ToArray();
                }
                key.selective=true;
                if(key.boneChannels==null) key.boneChannels=new int[key.bones.Length];
                if(key.effectorChannels==null) key.effectorChannels=new int[key.effectors.Length];
                for(int i=0;i<bones.Length && i<key.boneChannels.Length;i++) key.boneChannels[i]&=~bones[i];
                for(int i=0;i<effectors.Length && i<key.effectorChannels.Length;i++) key.effectorChannels[i]&=~effectors[i];
            }
            keys.RemoveAll(k=>k.selective && !(k.boneChannels?.Any(v=>v!=0) ?? false) && !(k.effectorChannels?.Any(v=>v!=0) ?? false));
            if(baseTrack)
            {
                if(keys.Count==0) keys.Add(rig.Rest.Copy(0));
                // Base evaluation falls back to its first pose for channels with no remaining keys.
                // Give only those completely unkeyed selected channels their neutral values.
                for(int i=0;i<bones.Length;i++)
                    for(int bit=1;bit<=4;bit*=2)
                        if((bones[i]&bit)!=0 && !keys.Any(k=>!k.selective || (k.boneChannels!=null && i<k.boneChannels.Length && (k.boneChannels[i]&bit)!=0)))
                        {
                            var value=keys[0].bones[i]; var rest=rig.Rest.bones[i];
                            if(bit==1) value.position=rest.position; else if(bit==2) value.rotation=rest.rotation; else value.scale=rest.scale;
                            keys[0].bones[i]=value;
                        }
                for(int i=0;i<effectors.Length;i++)
                    for(int bit=1;bit<=8;bit*=2)
                        if((effectors[i]&bit)!=0 && !keys.Any(k=>!k.selective || (k.effectorChannels!=null && i<k.effectorChannels.Length && (k.effectorChannels[i]&bit)!=0)))
                        {
                            var value=keys[0].effectors[i]; var rest=rig.Rest.effectors[i];
                            if(bit==1) value.position=rest.position; else if(bit==2) value.rotation=rest.rotation;
                            else if(bit==4) value.pole=rest.pole; else value.weight=rest.weight;
                            keys[0].effectors[i]=value;
                        }
            }
            Save();
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
            int count=SelectedTimelineKeys(actor,keys).Count(k=>TimelineInRange(actor,k));
            if(count>0) menu.AddItem(new GUIContent("Copy selected keys"),false,()=>CopyTimelineKeys(actor,keys));
            else menu.AddDisabledItem(new GUIContent("Copy selected keys"));
            if(count>0)
                menu.AddItem(new GUIContent("Delete keys for selected controls"),false,()=>DeleteTimelineKeys(actor,keys));
            else menu.AddDisabledItem(new GUIContent("Delete keys for selected controls"));
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
            AddAudioMenu(menu, actor, atFrame);
            menu.ShowAsContext();
        }
        private void DrawViewportTimeline()
        {
            if (!this) return;
            if (!take || rig == null || pose == null)
            {
                GUILayout.Space(15);
                GUILayout.Label("Create or open an animation take to use the timeline.", StudioTheme.Styles.centeredGreyMiniLabel);
                return;
            }

            GUILayout.Space(5);
            EnsureTimelineView();
            var timelineActor=OutfitActor;
            var layerKeys=TimelineLayerKeys(timelineActor);
            var keyFrames=SelectedTimelineFrames(timelineActor,layerKeys);
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
                if (GUILayout.Button(new GUIContent("Audio", "Edit audio keys for this actor"), GUILayout.Width(48))) OpenAudioKeys(timelineActor);
                bool nextMute = GUILayout.Toggle(muteAudio, new GUIContent("Mute", "Mute studio audio playback"), "Button", GUILayout.Width(44));
                if (nextMute != muteAudio) { muteAudio = nextMute; if (muteAudio) StopStudioAudio(); }
                DrawTimelineLayerPicker();
                bool nextAuto = GUILayout.Toggle(autoKey, "Auto Key", "Button", GUILayout.Width(70));
                if (nextAuto != autoKey) { FinishSceneEdit(); autoKey = nextAuto; Repaint(); }
                if (GUILayout.Button("Key [S]", GUILayout.Width(62))) KeyCurrentActor();
                GUILayout.Space(6);
            }

            // The toolbar may have changed the authoring layer during this GUI event.
            layerKeys=TimelineLayerKeys(timelineActor);
            keyFrames=SelectedTimelineFrames(timelineActor,layerKeys);
            Rect ruler = GUILayoutUtility.GetRect(1, 51, GUILayout.ExpandWidth(true));
            ruler.xMin += 10; ruler.xMax -= 10;
            int id = GUIUtility.GetControlID("MashBoxViewportScrubber".GetHashCode(), FocusType.Keyboard, ruler);
            var evt = Event.current;
            if (evt.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(ruler, StudioTheme.Current.Header);
                int span = Mathf.Max(1, timelineViewLast - timelineViewFirst);
                if(timelineRangeStart>=0 && Mathf.Max(timelineRangeStart,timelineRangeEnd)>=timelineViewFirst && Mathf.Min(timelineRangeStart,timelineRangeEnd)<=timelineViewLast)
                {
                    float left=Mathf.Max(ruler.x,TimelineFrameX(ruler,Mathf.Min(timelineRangeStart,timelineRangeEnd))-3);
                    float right=Mathf.Min(ruler.xMax,TimelineFrameX(ruler,Mathf.Max(timelineRangeStart,timelineRangeEnd))+3);
                    EditorGUI.DrawRect(new Rect(left,ruler.y,Mathf.Max(1,right-left),ruler.height),new Color(.55f,.22f,.18f,.65f));
                }
                int step = Mathf.Max(1, Mathf.CeilToInt(span / Mathf.Max(1f, ruler.width / 55f)));
                if (step > 5) step = Mathf.CeilToInt(step / 5f) * 5;
                for (int f = Mathf.CeilToInt((float)timelineViewFirst / step) * step; f <= timelineViewLast; f += step)
                {
                    float x = TimelineFrameX(ruler,f);
                    EditorGUI.DrawRect(new Rect(x, ruler.y + 20, 1, 10), StudioTheme.Current.Muted);
                    GUI.Label(new Rect(x + 3, ruler.y + 1, 48, 18), f.ToString(), StudioTheme.Styles.miniLabel);
                }
                foreach (var keyFrame in keyFrames)
                {
                    if(keyFrame<timelineViewFirst || keyFrame>timelineViewLast) continue;
                    float x = TimelineFrameX(ruler,keyFrame);
                    EditorGUI.DrawRect(new Rect(x - 2, ruler.y + 32, 4, 14), StudioTheme.Current.Accent);
                }
                DrawAudioMarkers(ruler);
                if(frame>=timelineViewFirst && frame<=timelineViewLast)
                {
                    float cursor = TimelineFrameX(ruler,frame);
                    EditorGUI.DrawRect(new Rect(cursor - 1, ruler.y, 2, ruler.height), StudioTheme.Current.UVSelected);
                }
            }
            // SceneView-hosted IMGUIContainers do not reliably synthesize ContextClick.
            // Consume the actual right-button press before viewport navigation can claim it.
            HandleAudioMarker(evt, ruler);
            if(((evt.type==EventType.MouseDown && evt.button==1) || evt.type==EventType.ContextClick) && ruler.Contains(evt.mousePosition))
            {
                int at=FrameAtMouse(ruler,evt.mousePosition.x);
                var nearby=keyFrames.Where(f=>f>=timelineViewFirst && f<=timelineViewLast).OrderBy(f=>Mathf.Abs(TimelineFrameX(ruler,f)-evt.mousePosition.x)).Take(1).ToArray();
                if(nearby.Length>0 && Mathf.Abs(TimelineFrameX(ruler,nearby[0])-evt.mousePosition.x)<=6) at=nearby[0];
                if(timelineRangeStart<0 || at<Mathf.Min(timelineRangeStart,timelineRangeEnd) || at>Mathf.Max(timelineRangeStart,timelineRangeEnd))
                    timelineRangeStart=timelineRangeEnd=at;
                GUIUtility.keyboardControl=id;
                TimelineContextMenu(timelineActor,layerKeys,at); evt.Use();
            }
            if(evt.type==EventType.ScrollWheel && ruler.Contains(evt.mousePosition))
            { ZoomTimelineView(Mathf.Pow(1.15f,evt.delta.y),FrameAtMouse(ruler,evt.mousePosition.x)); evt.Use(); }
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
            DrawTimelineViewingRange();
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                string layerLabel=timelineActor.activeLayer>=0 && timelineActor.activeLayer<timelineActor.layers.Count ? timelineActor.layers[timelineActor.activeLayer].name : "Base motion";
                GUILayout.Label($"{(timelineRangeStart>=0 ? "Selected "+Mathf.Min(timelineRangeStart,timelineRangeEnd)+"–"+Mathf.Max(timelineRangeStart,timelineRangeEnd)+" · " : "Shift-drag: select · Right-click: edit · ")}{(float)frame / take.frameRate:0.000}s   ·   {take.frameRate} fps   ·   {keyFrames.Length} selected keys · {layerLabel}", StudioTheme.Styles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(unkeyedPose ? "Unkeyed changes · S: pose · Shift+W: position · Shift+E: rotation" : autoKey ? "AUTO KEY ON" : "S: pose · Shift+W: position · Shift+E: rotation", StudioTheme.Styles.miniLabel);
                GUILayout.Space(10);
            }
            HandleKeys(evt);
        }

        private List<PoseKey> TimelineLayerKeys(AnimationTake actor)
        {
            var keys=actor.activeLayer>=0 && actor.activeLayer<actor.layers.Count ? actor.layers[actor.activeLayer].keys : actor.keys;
            if(timelineSelectionActor!=actor || timelineSelectionKeys!=keys)
            { timelineRangeStart=timelineRangeEnd=-1; timelineSelectionActor=actor; timelineSelectionKeys=keys; }
            return keys;
        }

        private int FrameAtMouse(Rect ruler, float x)
        {
            EnsureTimelineView();
            return timelineViewFirst + Mathf.RoundToInt(Mathf.Clamp01((x - ruler.x) / Mathf.Max(1, ruler.width)) * (timelineViewLast - timelineViewFirst));
        }

        private void ScrubViewport(int next)
        {
            playing = false;
            Seek(Mathf.Clamp(next, 0, take.lastFrame));
            EnsureTimelineView();
            int span = timelineViewLast - timelineViewFirst;
            if(frame<timelineViewFirst) SetTimelineView(frame,frame+span);
            else if(frame>timelineViewLast) SetTimelineView(frame-span,frame);
        }
    }
}
