using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        internal Action<string> StartTutorial,RestartTutorial;
        internal Action ExitTutorial,ImportPractice;
        private int tutorialReview=-1;
        private bool tutorialObserved,tutorialBrushSettings;
        private float tutorialRadius,tutorialStrength;
        private bool tutorialWire,tutorialChecker,tutorialTexture;
        private Quaternion tutorialCamera;
        private Vector2 tutorialScroll;
        private readonly System.Collections.Generic.HashSet<string> tutorialStrokeActions=new System.Collections.Generic.HashSet<string>();
        private readonly Color tutorialGreen=new Color(.5f,1,.75f);
        private StudioSkinningLessons.Lesson SkinLesson=>StudioSkinningLessons.Find(Document.tutorialId);
        internal bool HasTutorial=>SkinLesson!=null;

        internal void RecordSkinTutorialAction(string action)
        {
            if(!HasTutorial || string.IsNullOrEmpty(action)) return;
            if(!Document.tutorialActions.Contains(action)) Document.tutorialActions.Add(action);
            CheckSkinTutorialProgress();
        }
        private void CheckSkinTutorialProgress()
        {
            var lesson=SkinLesson;if(lesson==null)return;
            int before=StudioSkinningLessons.Count(lesson);
            foreach(var step in lesson.steps)
                if(!string.IsNullOrEmpty(step.action) && Document.tutorialActions.Contains(step.action))
                    StudioSkinningLessons.SetDone(lesson,step,true);
            int after=StudioSkinningLessons.Count(lesson);
            if(after>before)
            {
                tutorialReview=-1;
                lastView?.ShowNotification(new GUIContent(after==lesson.steps.Length ? "Lesson complete!" : "Checkpoint complete"));
                repaint();
            }
        }
        private void ObserveSkinTutorial(SceneView view)
        {
            if(!HasTutorial)return;
            if(tutorialObserved)
            {
                if(Quaternion.Angle(tutorialCamera,view.rotation)>3) RecordSkinTutorialAction("orbit");
                if(!Mathf.Approximately(tutorialRadius,radius))RecordSkinTutorialAction("radius");
                if(!Mathf.Approximately(tutorialStrength,strength))RecordSkinTutorialAction("strength");
                if(tutorialWire!=showSelectedWireframe)RecordSkinTutorialAction("wire");
                if(tutorialChecker!=showUVChecker && showUVChecker)RecordSkinTutorialAction("checker");
                if(tutorialTexture!=showTextured && showTextured)RecordSkinTutorialAction("texture");
            }
            else tutorialCamera=view.rotation;
            tutorialObserved=true;tutorialRadius=radius;tutorialStrength=strength;
            tutorialWire=showSelectedWireframe;tutorialChecker=showUVChecker;tutorialTexture=showTextured;
        }
        private void QueueSkinLesson(string id,bool restart)
        {
            EditorApplication.delayCall+=()=>{if(Scene.IsValid())(restart?RestartTutorial:StartTutorial)?.Invoke(id);};
        }
        private void QueueSkinExit()=>EditorApplication.delayCall+=()=>{if(Scene.IsValid())ExitTutorial?.Invoke();};
        internal void DrawSkinTutorialMenu()
        {
            var rect=new Rect(0,0,104,21);
            if(GUI.Button(rect,new GUIContent("Tutorials",$"{StudioSkinningLessons.Completed}/{StudioSkinningLessons.Total} checkpoints"),StudioTheme.Styles.menuDropdown))
                PopupWindow.Show(rect,new SkinTutorialLibrary(this));
        }
        private static void SkinProgress(float ratio)
        {
            var bar=GUILayoutUtility.GetRect(0,4,GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar,new Color(.12f,.15f,.15f));
            EditorGUI.DrawRect(new Rect(bar.x,bar.y,bar.width*Mathf.Clamp01(ratio),bar.height),new Color(.5f,1,.87f));
        }
        // The drawings are the toolbar's own icons, shown as a legend, never as
        // a second set of activation controls inside a tutorial.
        private void DrawSkinTutorialIcon(string icon)
        {
            if(string.IsNullOrEmpty(icon))return;
            var row=GUILayoutUtility.GetRect(0,36,GUILayout.ExpandWidth(true));
            var rect=new Rect(row.x,row.y,32,32);
            using(new EditorGUI.DisabledScope(true))
            {
                if(icon=="object")DrawObjectModeIcon(rect);
                else if(icon=="paint"||icon=="sculpt")DrawModeIcon(rect,icon=="sculpt");
                else if(icon=="wire")DrawWireframeButton(rect);
                else if(icon=="checker")DrawUVCheckerButton(rect);
                else if(icon=="texture")DrawTexturedButton(rect);
                else DrawPosePresetButton(rect,icon=="apose");
            }
            string label=icon=="object"?"Object - top toolbar":icon=="paint"?"Skin Weight Paint - top toolbar":
                icon=="sculpt"?"Sculpt - top toolbar":icon=="wire"?"Wireframe - above playback":
                icon=="checker"?"UV checker - above playback":icon=="texture"?"Textured - above playback":
                icon=="apose"?"A-pose - playback bar":"T-pose - playback bar";
            GUI.Label(new Rect(row.x+40,row.y,Mathf.Max(0,row.width-40),32),label,StudioTheme.Styles.wordWrappedMiniLabel);
        }
        internal void DrawSkinInspector()
        {
            if(!HasTutorial){DrawInspector();return;}
            var lesson=SkinLesson;
            tutorialScroll=EditorGUILayout.BeginScrollView(tutorialScroll,GUILayout.MinWidth(0));
            GUILayout.Label("SKINNING / TRAINING",StudioTheme.Styles.miniLabel);
            GUILayout.Label(lesson.title,StudioTheme.Styles.boldLabel);
            GUILayout.Label(lesson.purpose,StudioTheme.Styles.wordWrappedMiniLabel);
            int done=StudioSkinningLessons.Count(lesson);
            SkinProgress((float)done/lesson.steps.Length);
            GUILayout.Label($"{done} / {lesson.steps.Length} checkpoints  -  {done*100/lesson.steps.Length}%",StudioTheme.Styles.miniLabel);
            int next=Array.FindIndex(lesson.steps,s=>!StudioSkinningLessons.Done(lesson,s));
            int focus=tutorialReview>=0?tutorialReview:next;
            for(int i=0;i<lesson.steps.Length;i++)
            {
                var step=lesson.steps[i];bool complete=StudioSkinningLessons.Done(lesson,step);
                var row=GUILayoutUtility.GetRect(0,30,GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(row,i==focus?new Color(.17f,.30f,.28f):new Color(.075f,.085f,.085f));
                if(GUI.Button(row,GUIContent.none,GUIStyle.none))tutorialReview=i;
                var old=GUI.color;if(complete)GUI.color=tutorialGreen;
                GUI.Label(new Rect(row.x+6,row.y+5,22,20),complete?"\u2713":(i+1).ToString());GUI.color=old;
                GUI.Label(new Rect(row.x+30,row.y+4,row.width-34,23),step.title,StudioTheme.Styles.label);
                GUILayout.Space(3);
            }
            GUILayout.Space(8);
            if(focus>=0&&focus<lesson.steps.Length)
            {
                var step=lesson.steps[focus];
                DrawSkinTutorialIcon(step.icon);
                GUILayout.Label(step.help,new GUIStyle(StudioTheme.Styles.label){wordWrap=true});
                GUILayout.Space(8);
                if(string.IsNullOrEmpty(step.action))
                {
                    bool was=StudioSkinningLessons.Done(lesson,step);
                    bool review=GUILayout.Toggle(was,"I reviewed the result");
                    if(was!=review){StudioSkinningLessons.SetDone(lesson,step,review);tutorialReview=-1;repaint();}
                }
                else GUILayout.Label("Checks off automatically after the action succeeds.",StudioTheme.Styles.wordWrappedMiniLabel);
                if(tutorialReview>=0 && next>=0 && GUILayout.Button("Back to next checkpoint"))tutorialReview=-1;
            }
            else
            {
                var old=GUI.color;GUI.color=tutorialGreen;GUILayout.Label("\u2713  Lesson complete",StudioTheme.Styles.boldLabel);GUI.color=old;
                int index=Array.IndexOf(StudioSkinningLessons.All,lesson);
                if(index+1<StudioSkinningLessons.All.Length && GUILayout.Button("Next lesson"))QueueSkinLesson(StudioSkinningLessons.All[index+1].id,false);
            }
            GUILayout.Space(10);
            using(new GUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Restart scenario"))QueueSkinLesson(lesson.id,true);
                if(GUILayout.Button("Return to workspace"))QueueSkinExit();
            }
            GUILayout.Label("Practice stays in memory. Restart / return discards unsaved practice edits. Save a workspace to keep them. Your original workspace is preserved.",StudioTheme.Styles.wordWrappedMiniLabel);
            if(!string.IsNullOrEmpty(error))EditorGUILayout.HelpBox(error,MessageType.Error);
            tutorialBrushSettings=EditorGUILayout.Foldout(tutorialBrushSettings,"Brush Settings",true);
            // One scroll view keeps the normal brush controls accessible below the guide.
            if(tutorialBrushSettings)DrawInspector(false);
            EditorGUILayout.EndScrollView();
        }
        private sealed class SkinTutorialLibrary:PopupWindowContent
        {
            private readonly StudioSkinningEditor owner;
            private Vector2 scroll;
            private string search="";
            internal SkinTutorialLibrary(StudioSkinningEditor owner){this.owner=owner;}
            public override Vector2 GetWindowSize()=>new Vector2(410,540);
            public override void OnGUI(Rect rect)=>StudioTheme.Wrap(Draw)();
            private void Draw()
            {
                GUILayout.Label("SKINNING QUESTS",StudioTheme.Styles.boldLabel);
                int badges=StudioSkinningLessons.All.Count(l=>StudioSkinningLessons.Count(l)==l.steps.Length);
                GUILayout.Label($"{badges} / {StudioSkinningLessons.All.Length} badges earned");
                SkinProgress((float)StudioSkinningLessons.Completed/StudioSkinningLessons.Total);
                GUILayout.Label($"{StudioSkinningLessons.Completed} / {StudioSkinningLessons.Total} checkpoints",StudioTheme.Styles.miniLabel);
                GUILayout.Label("Practice on private copies. Your workspace is kept aside. Progress is saved locally; no workspace asset is required.",StudioTheme.Styles.wordWrappedMiniLabel);
                search=EditorGUILayout.TextField("Find a lesson",search);
                scroll=EditorGUILayout.BeginScrollView(scroll);
                foreach(var lesson in StudioSkinningLessons.All)
                {
                    if(!string.IsNullOrEmpty(search)&&(lesson.title+lesson.purpose).IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;
                    using(new GUILayout.VerticalScope(GUI.skin.box))
                    {
                        int count=StudioSkinningLessons.Count(lesson);bool complete=count==lesson.steps.Length;
                        using(new GUILayout.HorizontalScope())
                        {
                            if(complete){var old=GUI.color;GUI.color=owner.tutorialGreen;GUILayout.Label("\u2713",GUILayout.Width(18));GUI.color=old;}
                            GUILayout.Label(lesson.title,StudioTheme.Styles.boldLabel);
                        }
                        GUILayout.Label(lesson.purpose,StudioTheme.Styles.wordWrappedMiniLabel);
                        using(new GUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"{count}/{lesson.steps.Length} checkpoints",StudioTheme.Styles.miniLabel);
                            if(GUILayout.Button(complete?"Replay":count>0?"Continue":"Start",GUILayout.Width(80)))
                            {owner.QueueSkinLesson(lesson.id,complete);editorWindow.Close();}
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
                if(owner.HasTutorial&&GUILayout.Button("Return to workspace")){owner.QueueSkinExit();editorWindow.Close();}
                if(GUILayout.Button("Import Practice Hoodie into current workspace"))
                {EditorApplication.delayCall+=()=>{if(owner.Scene.IsValid())owner.ImportPractice?.Invoke();};editorWindow.Close();}
            }
        }
    }
}
