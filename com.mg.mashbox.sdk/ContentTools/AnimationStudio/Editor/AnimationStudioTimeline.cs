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
        private int timelineViewDrag;
        private int timelineViewDragFirst, timelineViewDragLast;
        private float timelineViewDragX;
        private AnimationTake timelineSelectionActor;
        private List<PoseKey> timelineSelectionKeys;
        [SerializeField] private AnimationTake timelineViewTake;
        [SerializeField] private int timelineViewFirst;
        [SerializeField] private int timelineViewLast = -1;
        [SerializeField] private int timelineViewLength;

        private void DrawTimelineFrameRate(Rect rect)
        {
            var content = new GUIContent(take.frameRate + " fps", "Timeline frame rate. Keeps key frame numbers; changes playback speed and duration.");
            if (!TimelineButton(rect, content.text + "  ▾", content.tooltip)) return;
            var target = take;
            var menu = new StudioMenu();
            foreach (int fps in new[] { 12, 15, 24, 25, 30, 48, 50, 60, 90, 120 })
            {
                int rate = fps;
                menu.AddItem(new GUIContent(rate + " fps"), target.frameRate == rate, () => Guard(() =>
                {
                    if (!target || take != target || target.frameRate == rate) return;
                    FlushEdits(); playing = false; StopStudioAudio();
                    Undo.RecordObject(target, "Change timeline frame rate");
                    target.frameRate = rate;
                    Save(); Seek(Mathf.Min(frame, target.lastFrame)); viewport?.Repaint();
                }));
            }
            menu.DropDown(rect);
        }

        private void EnsureTimelineView()
        {
            if (!take) return;
            if (timelineViewTake != take || timelineViewLast < 0)
            { timelineViewTake = take; timelineViewFirst = take.firstFrame; timelineViewLast = take.lastFrame; }
            else if (timelineViewLength != take.lastFrame && timelineViewLast == timelineViewLength)
                timelineViewLast = take.lastFrame;
            timelineViewFirst = Mathf.Clamp(timelineViewFirst, take.firstFrame, Mathf.Max(take.firstFrame, take.lastFrame - 1));
            timelineViewLast = Mathf.Clamp(timelineViewLast, timelineViewFirst + 1, Mathf.Max(1, take.lastFrame));
            timelineViewLength = take.lastFrame;
        }

        private void SetTimelineView(int first, int last)
        {
            EnsureTimelineView();
            timelineViewFirst = Mathf.Clamp(first, take.firstFrame, Mathf.Max(take.firstFrame, take.lastFrame - 1));
            timelineViewLast = Mathf.Clamp(last, timelineViewFirst + 1, take.lastFrame);
            viewport?.Repaint(); Repaint();
        }

        private void SetTimelineClipStart(int first)
        {
            if (!take) return;
            first = Mathf.Clamp(first, 0, take.lastFrame - 1);
            if (first == take.firstFrame) return;
            FlushEdits(); playing = false;
            Undo.RegisterCompleteObjectUndo(take, "Change animation clip start");
            take.firstFrame = first;
            EnsureTimelineView(); Save(); FlushSave(); Seek(Mathf.Max(frame, first));
            timelineRangeStart = timelineRangeEnd = -1;
        }

        private void SetTimelineClipEnd(int last)
        {
            if (!take) return;
            last = Mathf.Clamp(last, take.firstFrame + 1, 18000);
            if (last == take.lastFrame) return;
            EnsureTimelineView(); FlushEdits(); playing = false;
            Undo.RegisterCompleteObjectUndo(take, "Change animation clip end");
            take.lastFrame = last;
            // Keep keys beyond the playback end so shortening is reversible without data loss.
            EnsureTimelineView(); Save(); FlushSave(); Seek(Mathf.Min(frame, last));
            timelineRangeStart = timelineRangeEnd = -1;
        }

        private float TimelineFrameWidth(Rect ruler) => ruler.width /
            Mathf.Max(1, timelineViewLast - timelineViewFirst + 1);

        private float TimelineFrameX(Rect ruler, int atFrame) => ruler.x + TimelineFrameWidth(ruler) *
            (atFrame - timelineViewFirst);

        private void ZoomTimelineView(float scale, int anchor)
        {
            EnsureTimelineView();
            int oldSpan = timelineViewLast - timelineViewFirst;
            int span = Mathf.Clamp(Mathf.RoundToInt(oldSpan * scale), 1, take.lastFrame - take.firstFrame);
            float fraction = Mathf.Clamp01((anchor - timelineViewFirst) / (float)oldSpan);
            int first = Mathf.Clamp(Mathf.RoundToInt(anchor - span * fraction), take.firstFrame, take.lastFrame - span);
            SetTimelineView(first, first + span);
        }

        private int TimelineSharedFrame(AnimationTake actor,int f) => Mathf.RoundToInt((float)f/actor.frameRate*take.frameRate);
        private bool TimelineInRange(AnimationTake actor,PoseKey key) => timelineRangeStart>=0 &&
            TimelineSharedFrame(actor,key.frame)>=Mathf.Min(timelineRangeStart,timelineRangeEnd) &&
            TimelineSharedFrame(actor,key.frame)<=Mathf.Max(timelineRangeStart,timelineRangeEnd);
        private void CopyTimelineKeys(AnimationTake actor,List<PoseKey> keys)
        {
            if(timelineRangeStart<0) return;
            FlushEdits();
            float origin=Mathf.Min(timelineRangeStart,timelineRangeEnd)/(float)take.frameRate;
            var selected=keys.Where(k=>TimelineInRange(actor,k)).ToArray();
            InSelectedActor(actor,()=>
            {
                SelectedControlMasks(out var bones,out var effectors);
                var clipboard=StudioKeyClipboard.Capture(actor,keys,selected,bones,effectors,origin);
                if(clipboard.keys.Length>0) clipboard.Store();
            });
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
            if(rigSelectionExplicit && rigSelection.Count==0 && selectedRigChannelActor!=actor) return;
            FlushEdits(); playing=false;
            int sharedFps=take.frameRate;
            var targets=selectedRigChannelActor==actor ? new[]{actor} :
                rigSelection.Count>0 ? rigSelection.Select(s=>s.Actor).Where(a=>a).Distinct().ToArray() : new[]{actor};
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Delete selected control keys");
            foreach(var target in targets) InSelectedActor(target,()=>
            {
                if(target.singleFramePose) { message="Single Frame Pose endpoint keys are linked. Edit the pose or disable the mode under Clip to delete keys."; return; }
                var track=target==actor ? keys : EditingKeys;
                DeleteControlChannels(track,first,last,sharedFps);
            });
            FlushSave(); Seek(frame); Undo.IncrementCurrentGroup();
        }

        private void SelectedControlMasks(out int[] bones,out int[] effectors)
        {
            var boneMask=new int[rig.Bones.Length]; var effectorMask=new int[4];
            if (selectedRigChannelActor == take && selectedRigChannelBone >= 0 && selectedRigChannelBone < boneMask.Length)
            {
                boneMask[selectedRigChannelBone] = 1;
                bones = boneMask; effectors = effectorMask;
                return;
            }
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
            var targets=selectedRigChannelActor==actor ? new[]{actor} :
                rigSelection.Count>0 ? rigSelection.Select(s=>s.Actor).Where(a=>a).Distinct().ToArray() : new[]{actor};
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
            PasteCachedKeys(actor,keys,destination,false);
        }
        private void TimelineContextMenu(AnimationTake actor,List<PoseKey> keys,int atFrame)
        {
            var menu=new GenericMenu();
            int count=SelectedTimelineKeys(actor,keys).Count(k=>TimelineInRange(actor,k));
            if(count>0) menu.AddItem(new GUIContent("Copy selected keys"),false,()=>Guard(()=>CopyTimelineKeys(actor,keys)));
            else menu.AddDisabledItem(new GUIContent("Copy selected keys"));
            if(count>0)
                menu.AddItem(new GUIContent("Delete keys for selected controls"),false,()=>DeleteTimelineKeys(actor,keys));
            else menu.AddDisabledItem(new GUIContent("Delete keys for selected controls"));
            menu.AddSeparator("");
            AddClipboardMenuItems(actor,keys,atFrame,(label,enabled,action)=>
            {
                if(enabled) menu.AddItem(new GUIContent(label),false,()=>Guard(action));
                else menu.AddDisabledItem(new GUIContent(label));
            });
            menu.AddItem(new GUIContent("Clear range selection"),false,()=>{ timelineRangeStart=timelineRangeEnd=-1; viewport?.Repaint(); });
            if(MotionGenerationAvailable)
            {
                menu.AddSeparator("");
                if(!MotionRunning && timelineRangeStart>=0 && timelineRangeEnd!=timelineRangeStart)
                    menu.AddItem(new GUIContent("Generate AI in-betweens…"),false,()=>Guard(()=>StartInbetween(actor,Mathf.Min(timelineRangeStart,timelineRangeEnd),Mathf.Max(timelineRangeStart,timelineRangeEnd))));
                else menu.AddDisabledItem(new GUIContent("Generate AI in-betweens (select a range)"));
            }
            AddAudioMenu(menu, actor, atFrame);
            menu.AddItem(new GUIContent("Audio/Mute playback"), muteAudio, () =>
            { muteAudio = !muteAudio; if (muteAudio) StopStudioAudio(); viewport?.Repaint(); });
            menu.AddItem(new GUIContent("View/Fit full clip"), false, () => SetTimelineView(take.firstFrame, take.lastFrame));
            menu.ShowAsContext();
        }
        private static readonly Color TimelineBackground = new Color32(13, 14, 15, 255);
        private static readonly Color TimelineToolbarBackground = new Color32(18, 19, 20, 255);
        private static readonly Color TimelineButtonBackground = new Color32(22, 23, 24, 255);
        private static readonly Color TimelineBorder = new Color32(66, 66, 66, 255);
        private static readonly Color TimelineGold = new Color32(242, 190, 50, 255);
        private static readonly Color TimelineHover = new Color32(64, 68, 70, 255);
        private static readonly Color TimelineTrack = new Color32(102, 102, 102, 255);
        private static readonly Color TimelineText = new Color32(240, 240, 240, 255);
        private static GUIStyle timelineButtonStyle, timelinePlayStyle, timelineWhiteStyle, timelineLabelStyle, timelineMutedStyle, timelineFrameCellStyle, timelineRulerLabelStyle, timelineRangeLabelStyle, timelineRangeRightLabelStyle, timelineRangeCenterLabelStyle;

        private static void TimelineRect(Rect rect, Color color)
        {
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, color);
        }

        private static bool TimelineHovered(Rect rect) => GUI.enabled && !StudioTheme.HoverSuppressed &&
            rect.Contains(Event.current.mousePosition);

        private static void DrawAnimationHover(Rect rect)
        {
            if (TimelineHovered(rect)) TimelineRect(rect, TimelineHover);
        }

        private static void TimelineOutline(Rect rect, Color color)
        {
            TimelineRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            TimelineRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            TimelineRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            TimelineRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }

        private static GUIStyle TimelineStyle(bool play = false, bool muted = false)
        {
            if (timelineButtonStyle == null)
            {
                timelineButtonStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold, padding = new RectOffset() };
                timelineButtonStyle.normal.textColor = TimelineText;
                timelinePlayStyle = new GUIStyle(timelineButtonStyle);
                timelinePlayStyle.normal.textColor = Color.black;
                timelineWhiteStyle = new GUIStyle(timelineButtonStyle);
                timelineWhiteStyle.normal.textColor = Color.white;
                timelineLabelStyle = new GUIStyle(timelineButtonStyle) { fontSize = 11, fontStyle = FontStyle.Normal };
                timelineMutedStyle = new GUIStyle(timelineLabelStyle);
                timelineMutedStyle.normal.textColor = new Color32(160, 160, 160, 255);
                timelineFrameCellStyle = new GUIStyle(timelineLabelStyle) { alignment = TextAnchor.LowerLeft, fontSize = 9 };
                timelineFrameCellStyle.normal.textColor = TimelineText;
                timelineRulerLabelStyle = new GUIStyle(timelineMutedStyle) { alignment = TextAnchor.MiddleLeft, fontSize = 9 };
                timelineRangeLabelStyle = new GUIStyle(timelineLabelStyle) { alignment = TextAnchor.MiddleLeft, fontSize = 9 };
                timelineRangeRightLabelStyle = new GUIStyle(timelineRangeLabelStyle) { alignment = TextAnchor.MiddleRight };
                timelineRangeCenterLabelStyle = new GUIStyle(timelineRangeLabelStyle) { alignment = TextAnchor.MiddleCenter };
            }
            return play ? timelinePlayStyle : muted ? timelineMutedStyle : timelineLabelStyle;
        }

        private static bool TimelineButton(Rect rect, string label, string tooltip, bool accent = false, bool whiteAccent = false)
        {
            bool hovered = TimelineHovered(rect);
            TimelineRect(rect, accent ? hovered ? Color.Lerp(TimelineGold, Color.white, .20f) : TimelineGold :
                hovered ? TimelineHover : TimelineButtonBackground);
            TimelineOutline(rect, TimelineBorder);
            if (!whiteAccent) return GUI.Button(rect, new GUIContent(label, tooltip), accent ? TimelineStyle(true) : TimelineStyle());
            TimelineStyle(); // Ensure the cached toolbar styles have been created.
            return GUI.Button(rect, new GUIContent(label, tooltip), timelineWhiteStyle);
        }

        private static int TimelineNumber(Rect rect, int value, string tooltip)
        {
            TimelineRect(rect, TimelineHovered(rect) ? TimelineHover : new Color32(16, 17, 18, 255));
            TimelineOutline(rect, TimelineBorder);
            var field = new Rect(rect.x + 4, rect.y + 2, rect.width - 20, rect.height - 4);
            int result = EditorGUI.DelayedIntField(field, value);
            float arrowX = rect.xMax - 14;
            if (GUI.Button(new Rect(arrowX, rect.y + 1, 12, 10), new GUIContent("▴", tooltip + ": increment"), TimelineStyle())) result = value + 1;
            if (GUI.Button(new Rect(arrowX, rect.y + 13, 12, 10), new GUIContent("▾", tooltip + ": decrement"), TimelineStyle())) result = value - 1;
            return result;
        }

        private static void TimelineDivider(float x, float y)
        {
            TimelineRect(new Rect(x, y + 3, 1, 18), TimelineBorder);
        }

        private void DrawTimelineToolbar(Rect panel, int[] keyFrames)
        {
            var toolbar = new Rect(panel.x + 10, panel.y + 5, panel.width - 20, 24);
            TimelineRect(new Rect(panel.x + 1, panel.y + 1, panel.width - 2, 33), TimelineToolbarBackground);
            TimelineRect(new Rect(panel.x + 1, panel.y + 34, panel.width - 2, 1), TimelineBorder);
            bool compact = panel.width < 1050;
            float buttonWidth = compact ? 27 : 40;
            float x = toolbar.x;
            if (TimelineButton(new Rect(x, toolbar.y, buttonWidth, 24), "|◀", "First frame")) ScrubViewport(take.firstFrame); x += buttonWidth + 4;
            if (TimelineButton(new Rect(x, toolbar.y, buttonWidth, 24), "◀", "Previous frame (Shift: previous key)"))
                ScrubViewport(Event.current.shift ? keyFrames.Where(f=>f<frame).DefaultIfEmpty(take.firstFrame).Max() : frame - 1); x += buttonWidth + 4;
            if (TimelineButton(new Rect(x, toolbar.y, buttonWidth, 24), playing ? "Ⅱ" : "▶", playing ? "Pause" : "Play", !playing)) TogglePlay(); x += buttonWidth + 4;
            if (TimelineButton(new Rect(x, toolbar.y, buttonWidth, 24), "▶", "Next frame (Shift: next key)"))
                ScrubViewport(Event.current.shift ? keyFrames.Where(f=>f>frame).DefaultIfEmpty(take.lastFrame).Min() : frame + 1); x += buttonWidth + 4;
            if (TimelineButton(new Rect(x, toolbar.y, buttonWidth, 24), "▶|", "Last frame")) ScrubViewport(take.lastFrame); x += buttonWidth + 10;
            GUI.Label(new Rect(x, toolbar.y, compact ? 56 : 74, 24), new GUIContent(frame + " / " + take.lastFrame, "Current frame / clip end"), TimelineStyle()); x += compact ? 62 : 80;
            TimelineDivider(x, toolbar.y); x += 12;
            GUI.Label(new Rect(x, toolbar.y, compact ? 30 : 36, 24), "Start", TimelineStyle(false, true)); x += compact ? 32 : 38;
            int first = TimelineNumber(new Rect(x, toolbar.y, compact ? 58 : 56, 24), take.firstFrame, "Clip start frame");
            if (first != take.firstFrame) SetTimelineClipStart(first); x += compact ? 65 : 64;
            GUI.Label(new Rect(x, toolbar.y, compact ? 25 : 28, 24), "End", TimelineStyle(false, true)); x += compact ? 27 : 30;
            int end = TimelineNumber(new Rect(x, toolbar.y, compact ? 58 : 56, 24), take.lastFrame, "Clip end frame");
            if (end != take.lastFrame) SetTimelineClipEnd(end);

            float layerWidth = compact ? 95 : 150;
            float fpsWidth = compact ? 65 : 76;
            float rightX = toolbar.xMax - (layerWidth + fpsWidth + (compact ? 74 : 126));
            TimelineDivider(rightX, toolbar.y); rightX += 13;
            if (!compact)
            {
                for (int line=0; line<3; line++) TimelineRect(new Rect(rightX+3+line,toolbar.y+6+line*4,12,2),TimelineText);
                rightX += 19;
            }
            if (!compact) { GUI.Label(new Rect(rightX, toolbar.y, 36, 24), "Layer", TimelineStyle(false, true)); rightX += 38; }
            DrawTimelineLayerPicker(new Rect(rightX, toolbar.y, layerWidth, 24)); rightX += layerWidth + 9;
            DrawTimelineFrameRate(new Rect(rightX, toolbar.y, fpsWidth, 24)); rightX += fpsWidth + 9;
            var record = new Rect(rightX, toolbar.y, 24, 24);
            var recordRed = new Color32(240, 68, 68, 255);
            Color recordBackground = autoKey ? new Color32(57, 27, 30, 255) : TimelineButtonBackground;
            if (TimelineHovered(record)) recordBackground = Color.Lerp(recordBackground, autoKey ? recordRed : TimelineHover, .22f);
            TimelineRect(record, recordBackground);
            TimelineOutline(record, autoKey ? recordRed : TimelineBorder);
            if (GUI.Button(record, new GUIContent("", autoKey ? "Auto Key ON — pose edits create keys" : "Auto Key OFF — click to enable"), TimelineStyle()))
            { FinishSceneEdit(); autoKey = !autoKey; Repaint(); }
            if (Event.current.type == EventType.Repaint)
            {
                var previousColor = Handles.color;
                Handles.BeginGUI();
                Handles.color = autoKey ? recordRed : new Color32(125, 86, 86, 255);
                Handles.DrawSolidDisc(record.center, Vector3.forward, 6);
                Handles.color = recordBackground;
                Handles.DrawSolidDisc(record.center, Vector3.forward, autoKey ? 3.5f : 4.5f);
                Handles.EndGUI();
                Handles.color = previousColor;
            }
        }

        private float TimelineTrackX(Rect track, int atFrame) => track.x + track.width *
            (atFrame - take.firstFrame) / Mathf.Max(1f, take.lastFrame - take.firstFrame);

        private void DrawTimelineViewRange(Rect track)
        {
            var evt = Event.current;
            int id = GUIUtility.GetControlID("MashBoxTimelineViewRange".GetHashCode(), FocusType.Passive, track);
            float left = TimelineTrackX(track, timelineViewFirst);
            float right = TimelineTrackX(track, timelineViewLast);
            if (evt.type == EventType.Repaint)
            {
                TimelineRect(track, new Color32(44, 44, 44, 255));
                bool rangeHovered = TimelineHovered(new Rect(left, track.y-3, Mathf.Max(1, right-left), track.height+6));
                TimelineRect(new Rect(left, track.y, Mathf.Max(1, right-left), track.height),
                    rangeHovered ? new Color32(126, 130, 132, 255) : TimelineTrack);
                TimelineOutline(track, TimelineBorder);
                var handleColor = new Color32(214, 214, 214, 255);
                TimelineRect(new Rect(left-2, track.y, 5, track.height),
                    TimelineHovered(new Rect(left-7, track.y-3, 14, track.height+6)) ? Color.white : handleColor);
                TimelineRect(new Rect(right-2, track.y, 5, track.height),
                    TimelineHovered(new Rect(right-7, track.y-3, 14, track.height+6)) ? Color.white : handleColor);
                if (right-left >= 76)
                {
                    GUI.Label(new Rect(left+8, track.y, 32, track.height), timelineViewFirst.ToString(), timelineRangeLabelStyle);
                    GUI.Label(new Rect(right-40, track.y, 32, track.height), timelineViewLast.ToString(), timelineRangeRightLabelStyle);
                }
                else
                {
                    float labelX = Mathf.Clamp((left+right)*.5f-25, track.x+4, track.xMax-54);
                    GUI.Label(new Rect(labelX, track.y, 50, track.height), timelineViewFirst+"–"+timelineViewLast, timelineRangeCenterLabelStyle);
                }
            }
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (evt.button != 0 || !new Rect(track.x-5, track.y-3, track.width+10, track.height+6).Contains(evt.mousePosition)) break;
                    float mx = evt.mousePosition.x;
                    timelineViewDrag = Mathf.Abs(mx-left) <= 7 ? 1 : Mathf.Abs(mx-right) <= 7 ? 2 :
                        mx > left && mx < right ? 3 : 0;
                    if (timelineViewDrag == 0) break;
                    timelineViewDragFirst = timelineViewFirst; timelineViewDragLast = timelineViewLast; timelineViewDragX = mx;
                    GUIUtility.hotControl = id; evt.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    int delta = Mathf.RoundToInt((evt.mousePosition.x-timelineViewDragX) * (take.lastFrame-take.firstFrame) / Mathf.Max(1,track.width));
                    if (timelineViewDrag == 1) SetTimelineView(Mathf.Clamp(timelineViewDragFirst+delta,take.firstFrame,timelineViewDragLast-1),timelineViewDragLast);
                    else if (timelineViewDrag == 2) SetTimelineView(timelineViewDragFirst,Mathf.Clamp(timelineViewDragLast+delta,timelineViewDragFirst+1,take.lastFrame));
                    else if (timelineViewDrag == 3)
                    {
                        int span = timelineViewDragLast-timelineViewDragFirst;
                        int first = Mathf.Clamp(timelineViewDragFirst+delta,take.firstFrame,take.lastFrame-span);
                        SetTimelineView(first,first+span);
                    }
                    evt.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    timelineViewDrag = 0; GUIUtility.hotControl = 0; evt.Use();
                    break;
            }
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

            EnsureTimelineView();
            var timelineActor=OutfitActor;
            var layerKeys=TimelineLayerKeys(timelineActor);
            var keyFrames=SelectedTimelineFrames(timelineActor,layerKeys);
            Rect panel = GUILayoutUtility.GetRect(1, 111, GUILayout.ExpandWidth(true));
            TimelineRect(panel, TimelineBackground);
            TimelineOutline(new Rect(panel.x+1,panel.y+1,panel.width-2,panel.height-3), TimelineBorder);
            DrawTimelineToolbar(panel, keyFrames);

            // The toolbar may have changed the authoring layer during this GUI event.
            layerKeys=TimelineLayerKeys(timelineActor);
            keyFrames=SelectedTimelineFrames(timelineActor,layerKeys);
            Rect ruler = new Rect(panel.x+20,panel.y+36,panel.width-40,71);
            Rect scrubArea = new Rect(ruler.x,ruler.y,ruler.width,51);
            Rect track = new Rect(ruler.x,ruler.y+55,ruler.width,14);
            int id = GUIUtility.GetControlID("MashBoxViewportScrubber".GetHashCode(), FocusType.Keyboard, ruler);
            var evt = Event.current;
            DrawTimelineViewRange(track);
            if (evt.type == EventType.Repaint)
            {
                if (frame >= timelineViewFirst && frame <= timelineViewLast)
                {
                    float cellX = TimelineFrameX(ruler, frame);
                    float cellWidth = TimelineFrameWidth(ruler);
                    TimelineRect(new Rect(cellX, ruler.y, cellWidth, 51), new Color32(61, 61, 61, 255));
                    TimelineRect(new Rect(cellX, ruler.y, 1, 51), new Color32(124, 124, 124, 255));
                }
                if (TimelineHovered(scrubArea))
                {
                    int hoveredFrame = FrameAtMouse(ruler, evt.mousePosition.x);
                    float hoverX = TimelineFrameX(ruler, hoveredFrame);
                    TimelineRect(new Rect(hoverX, ruler.y, TimelineFrameWidth(ruler), 51),
                        new Color32(105, 110, 112, 70));
                }
                if(timelineRangeStart>=0 && timelineRangeStart!=timelineRangeEnd &&
                    Mathf.Max(timelineRangeStart,timelineRangeEnd)>=timelineViewFirst &&
                    Mathf.Min(timelineRangeStart,timelineRangeEnd)<=timelineViewLast)
                {
                    float left=Mathf.Max(ruler.x,TimelineFrameX(ruler,Mathf.Min(timelineRangeStart,timelineRangeEnd))-3);
                    float right=Mathf.Min(ruler.xMax,TimelineFrameX(ruler,Mathf.Max(timelineRangeStart,timelineRangeEnd))+3);
                    TimelineRect(new Rect(left,ruler.y+31,Mathf.Max(1,right-left),3),new Color32(80,80,80,255));
                }
                float framePixels = TimelineFrameWidth(ruler);
                int labelStep = framePixels >= 32f ? 1 : Mathf.Max(1, Mathf.CeilToInt(32f / framePixels));
                if (labelStep >= 4) labelStep = Mathf.CeilToInt(labelStep / 5f) * 5;
                else if (labelStep > 1) labelStep = 2;
                for (int f = Mathf.CeilToInt((float)timelineViewFirst / labelStep) * labelStep; f <= timelineViewLast; f += labelStep)
                {
                    float x = TimelineFrameX(ruler,f);
                    TimelineRect(new Rect(x, ruler.y + 22, 1, 29), new Color32(190,190,190,255));
                    GUI.Label(new Rect(x + 4, ruler.y + 1, 36, 20), f.ToString(), timelineRulerLabelStyle);
                }
                foreach (var keyFrame in keyFrames)
                {
                    if(keyFrame<timelineViewFirst || keyFrame>timelineViewLast) continue;
                    float x = TimelineFrameX(ruler,keyFrame);
                    TimelineRect(new Rect(x, ruler.y, 2, 51), new Color32(222, 53, 45, 255));
                }
                DrawAudioMarkers(ruler);
                if (frame >= timelineViewFirst && frame <= timelineViewLast)
                {
                    float cellX = TimelineFrameX(ruler, frame);
                    float cellWidth = TimelineFrameWidth(ruler);
                    float numberWidth = timelineFrameCellStyle.CalcSize(new GUIContent(frame.ToString())).x;
                    float labelWidth = Mathf.Max(cellWidth - 4, numberWidth + 2);
                    float labelX = Mathf.Min(cellX + 3, ruler.xMax - labelWidth);
                    GUI.Label(new Rect(labelX, ruler.y + 30, labelWidth, 20),
                        frame.ToString(), timelineFrameCellStyle);
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
                { Guard(()=>CopyTimelineKeys(timelineActor,layerKeys)); evt.Use(); }
                else if((evt.control || evt.command) && evt.keyCode==KeyCode.V)
                { Guard(()=>PasteTimelineKeys(timelineActor,layerKeys,frame)); evt.Use(); }
            }
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (evt.button != 0 || !scrubArea.Contains(evt.mousePosition)) break;
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
                    else
                    {
                        if (next != frame) { timelineRangeStart=timelineRangeEnd=next; ScrubViewport(next); }
                        viewport?.Repaint();
                    }
                    evt.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    viewport?.Repaint();
                    evt.Use();
                    break;
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
            return Mathf.Clamp(timelineViewFirst + Mathf.FloorToInt((x - ruler.x) / TimelineFrameWidth(ruler)),
                timelineViewFirst, timelineViewLast);
        }

        private void ScrubViewport(int next)
        {
            playing = false;
            Seek(Mathf.Clamp(next, take.firstFrame, take.lastFrame));
            EnsureTimelineView();
            int span = timelineViewLast - timelineViewFirst;
            if(frame<timelineViewFirst) SetTimelineView(frame,frame+span);
            else if(frame>timelineViewLast) SetTimelineView(frame-span,frame);
        }
    }
}
