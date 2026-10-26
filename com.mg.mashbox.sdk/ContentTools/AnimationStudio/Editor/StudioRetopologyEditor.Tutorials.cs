using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Lesson=MashBoxSDK.AnimationStudio.StudioRetopologyLessons.Lesson;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        internal Action<string> StartTutorial,RestartTutorial;
        internal Action ExitTutorial;
        private bool tutorialFramed,tutorialToolSettings;
        private int tutorialReview=-1;
        private static readonly Color LessonAccent=new Color(.5f,1,.87f);
        private Lesson CurrentLesson=>StudioRetopologyLessons.Find(Document.tutorialId);

        internal void DrawTutorialMenu()
        {
            var rect=new Rect(0,0,104,21);
            if(EditorGUI.DropdownButton(rect,new GUIContent("Tutorials",$"Retopology training: {StudioRetopologyLessons.Completed}/{StudioRetopologyLessons.Total} checkpoints"),FocusType.Passive,StudioTheme.Styles.menuDropdown))
                PopupWindow.Show(rect,new TutorialLibrary(this));
        }
        private void QueueTutorial(string id,bool restart=false)
        {
            // Rebinding a viewport while its IMGUI layout is running corrupts the layout stack.
            var callback=restart?RestartTutorial:StartTutorial;
            EditorApplication.delayCall+=()=>callback?.Invoke(id);
        }
        private void QueueExitTutorial()
        {var callback=ExitTutorial;EditorApplication.delayCall+=()=>callback?.Invoke();}
        internal void RecordTutorialAction(string action)
        {
            if(CurrentLesson==null)return;
            if(action=="Target weld vertex"||!Document.tutorialActions.Contains(action))Document.tutorialActions.Add(action);
            CheckTutorialProgress();
        }
        private bool TutorialCheck(string check)
        {
            if(string.IsNullOrEmpty(check))return false;
            if(check.StartsWith("event:",StringComparison.Ordinal))
                return Document.tutorialActions.Any(a=>a==check.Substring(6)||(check=="event:Subdivide"&&a=="Subdivide topology"));
            switch(check)
            {
                case "xray":return workingXray&&Document.tutorialActions.Contains("enableXray");
                case "brushAdjusted":return Document.tutorialActions.Contains("brushRadius")&&Document.tutorialActions.Contains("brushStrength");
                case "weldSeam":return Document.tutorialActions.Count(a=>a=="Target weld vertex")>=5;
                case "referenceWire":return referenceWire&&Document.tutorialActions.Contains("enableReferenceWire");
                case "symmetry":return Document.symmetry&&Document.tutorialActions.Contains("enableSymmetry");
                case "wheel":return contextHeld;
                case "vertices":return selectionMode==SelectionMode.Vertices&&selected.Count>0;
                case "edges":return selectionMode==SelectionMode.Edges&&selectedEdges.Count>1;
                case "faces":return selectionMode==SelectionMode.Faces&&selectedFaces.Count>1;
                case "tubePreview":return activeTool==RetopoTool.Tube&&tubePreview!=null;
                default:return false;
            }
        }
        private void CheckTutorialProgress()
        {
            var lesson=CurrentLesson;if(lesson==null)return;
            int before=StudioRetopologyLessons.Count(lesson);
            foreach(var step in lesson.steps)
                if(!StudioRetopologyLessons.Done(lesson,step)&&TutorialCheck(step.check))
                    StudioRetopologyLessons.SetDone(lesson,step,true);
            int after=StudioRetopologyLessons.Count(lesson);
            if(after>before)
            {
                tutorialReview=-1;
                view?.ShowNotification(new GUIContent(after==lesson.steps.Length?"Lesson complete!  +1 badge":"Checkpoint complete"));
                repaint();
            }
        }
        private void UpdateTutorialScene()
        {
            if(CurrentLesson==null)return;
            if(!tutorialFramed&&Active?.reference&&view)
            {
                // Frame once per editor instance, never while orbiting or painting.
                tutorialFramed=true;var b=Active.reference.bounds;
                view.LookAt(b.center,Quaternion.identity,Mathf.Max(.5f,b.size.magnitude*.6f),false,true);
            }
            ObserveTutorialSettings();
            CheckTutorialProgress();
        }
        private void ActivateTutorialTool(string name)
        {
            CancelResolution();EndRelaxBrush();EndBrushAdjustment();CancelVertexSelection();ClearPatchCorners();
            if(name=="Vertices"||name=="Edges"||name=="Faces")SetSelectionMode((SelectionMode)Enum.Parse(typeof(SelectionMode),name));
            else if(Enum.TryParse(name,out RetopoTool tool)){activeTool=tool;ClearComponents();}
            repaint();
        }
        // Returns true while the compact guide owns the inspector; full settings are opt-in below it.
        private bool DrawTutorialGuide()
        {
            var lesson=CurrentLesson;if(lesson==null)return false;
            int done=StudioRetopologyLessons.Count(lesson);
            GUILayout.Label("RETOPOLOGY / TRAINING",StudioTheme.Styles.miniLabel);
            GUILayout.Label(lesson.title,new GUIStyle(StudioTheme.Styles.boldLabel){wordWrap=true});
            GUILayout.Label(lesson.purpose,StudioTheme.Styles.wordWrappedMiniLabel);
            DrawLessonProgress(done,lesson.steps.Length);
            GUILayout.Space(6);
            int next=Array.FindIndex(lesson.steps,s=>!StudioRetopologyLessons.Done(lesson,s));
            int focused=tutorialReview>=0&&tutorialReview<lesson.steps.Length?tutorialReview:next;
            for(int i=0;i<lesson.steps.Length;i++)
            {
                var step=lesson.steps[i];bool complete=StudioRetopologyLessons.Done(lesson,step);
                var rowStyle=new GUIStyle(StudioTheme.Styles.label){wordWrap=true,padding=new RectOffset(30,5,5,5)};
                var row=GUILayoutUtility.GetRect(new GUIContent(step.title),rowStyle,GUILayout.MinHeight(28),GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(row,i==focused?new Color(.17f,.30f,.28f):new Color(.08f,.09f,.09f));
                var old=GUI.color;GUI.color=complete?LessonAccent:Color.gray;
                GUI.Label(new Rect(row.x+6,row.y+3,22,20),complete?"✓":(i+1).ToString(),EditorStyles.boldLabel);GUI.color=old;
                GUI.Label(row,new GUIContent(step.title,step.help),rowStyle);
                if(GUI.Button(row,GUIContent.none,GUIStyle.none))tutorialReview=i;
                GUILayout.Space(2);
            }
            if(focused>=0)
            {
                var step=lesson.steps[focused];GUILayout.Space(8);
                GUILayout.Label(step.help,new GUIStyle(StudioTheme.Styles.label){wordWrap=true});
                GUILayout.Space(5);
                DrawTutorialControls(lesson,step);
                bool complete=StudioRetopologyLessons.Done(lesson,step);
                if(step.check=="event:restart"&&GUILayout.Button("Reset practice mesh",GUILayout.Height(25)))QueueTutorial(lesson.id);
                if(string.IsNullOrEmpty(step.check))
                {
                    bool marked=GUILayout.Toggle(complete,"I reviewed the result",GUILayout.Height(24));
                    if(marked!=complete)
                    {
                        StudioRetopologyLessons.SetDone(lesson,step,marked);tutorialReview=-1;
                        if(StudioRetopologyLessons.Count(lesson)==lesson.steps.Length)view?.ShowNotification(new GUIContent("Lesson complete!  +1 badge"));
                        repaint();
                    }
                }
                else GUILayout.Label(complete?"Verified in the tool":"Checks off automatically when you complete this action.",StudioTheme.Styles.wordWrappedMiniLabel);
                if(tutorialReview>=0&&GUILayout.Button("Back to next checkpoint"))tutorialReview=-1;
            }
            if(done==lesson.steps.Length)
            {
                GUILayout.Space(6);GUILayout.Label("✓  BADGE EARNED",new GUIStyle(StudioTheme.Styles.boldLabel){normal={textColor=LessonAccent}});
                GUILayout.Label("Nice work. Your progress is saved on this computer.",StudioTheme.Styles.wordWrappedMiniLabel);
                int index=Array.IndexOf(StudioRetopologyLessons.All,lesson);
                if(index+1<StudioRetopologyLessons.All.Length&&GUILayout.Button("Next lesson  →",GUILayout.Height(26)))QueueTutorial(StudioRetopologyLessons.All[index+1].id);
            }
            GUILayout.Space(8);
            using(new GUILayout.HorizontalScope())
            {
                if(GUILayout.Button(new GUIContent("Restart scenario","Restores practice geometry and clears this lesson's checkmarks.")))QueueTutorial(lesson.id,true);
                if(ExitTutorial!=null&&GUILayout.Button("Return to workspace"))QueueExitTutorial();
            }
            GUILayout.Label("Practice geometry stays separate. Restart / exit discards unsaved practice edits; save a workspace first to keep them.",StudioTheme.Styles.wordWrappedMiniLabel);
            if(!string.IsNullOrEmpty(error))EditorGUILayout.HelpBox(error,MessageType.Error);
            else if(!string.IsNullOrEmpty(status))GUILayout.Label(status,StudioTheme.Styles.wordWrappedMiniLabel);
            GUILayout.Space(10);
            tutorialToolSettings=EditorGUILayout.Foldout(tutorialToolSettings,"Tool settings",true);
            return !tutorialToolSettings;
        }
        private static string ToolName(string name)=>name=="Vertices"?"Vertex Select":name=="Edges"?"Edge Select":name=="Faces"?"Face Select":name=="Quad"?"Draw Quad":name=="Patch"?"Quad Patch":name=="Strip"?"Quad Strip":name=="RelaxBrush"?"Relax Brush":name=="TargetWeld"?"Target Weld":name=="EdgeBridge"?"Edge Bridge":name;
        private static void DrawLessonProgress(int done,int total)
        {
            GUILayout.Space(5);
            var r=GUILayoutUtility.GetRect(0,5,GUILayout.ExpandWidth(true));EditorGUI.DrawRect(r,new Color(.12f,.13f,.13f));
            EditorGUI.DrawRect(new Rect(r.x,r.y,r.width*done/Mathf.Max(1,total),r.height),LessonAccent);
            GUILayout.Label($"{done} / {total} checkpoints  ·  {Mathf.RoundToInt(100f*done/Mathf.Max(1,total))}%",StudioTheme.Styles.miniLabel);
        }
        private sealed class TutorialLibrary : PopupWindowContent
        {
            private readonly StudioRetopologyEditor owner;
            private Vector2 scroll;private string search="";
            private GUIStyle completedCheckStyle;
            internal TutorialLibrary(StudioRetopologyEditor owner){this.owner=owner;}
            public override Vector2 GetWindowSize()=>new Vector2(390,540);
            public override void OnGUI(Rect rect)
            {
                // Same theme as the Studio panels, including wrapped text and mint progress accents.
                StudioTheme.Wrap(Draw)();
            }
            private void Draw()
            {
                GUILayout.Label("RETOPOLOGY QUESTS",StudioTheme.Styles.boldLabel);
                int badges=StudioRetopologyLessons.All.Count(l=>StudioRetopologyLessons.Count(l)==l.steps.Length);
                GUILayout.Label($"{badges} / {StudioRetopologyLessons.All.Length} badges earned",StudioTheme.Styles.label);
                DrawLessonProgress(StudioRetopologyLessons.Completed,StudioRetopologyLessons.Total);
                GUILayout.Label("Fresh practice scenes. Your current workspace is kept aside. Completion is saved locally; practice geometry stays in memory unless you save it.",StudioTheme.Styles.wordWrappedMiniLabel);
                search=EditorGUILayout.TextField("Find a lesson",search);
                scroll=EditorGUILayout.BeginScrollView(scroll);
                foreach(var lesson in StudioRetopologyLessons.All)
                {
                    if(search.Length>0&&(lesson.title+" "+lesson.purpose+" "+lesson.category).IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;
                    using(new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        int done=StudioRetopologyLessons.Count(lesson);
                        GUILayout.Label(lesson.category.ToUpperInvariant(),StudioTheme.Styles.miniLabel);
                        using(new GUILayout.HorizontalScope())
                        {
                            if(done==lesson.steps.Length)
                            {
                                if(completedCheckStyle==null)
                                {
                                    completedCheckStyle=new GUIStyle(StudioTheme.Styles.boldLabel) { fontSize=16,alignment=TextAnchor.MiddleCenter };
                                    completedCheckStyle.normal.textColor=new Color(0.35f,0.88f,0.52f,1f);
                                }
                                GUILayout.Label(new GUIContent("\u2713","Tutorial complete"),completedCheckStyle,GUILayout.Width(20));
                            }
                            GUILayout.Label(lesson.title,StudioTheme.Styles.boldLabel);
                        }
                        GUILayout.Label(lesson.purpose,StudioTheme.Styles.wordWrappedMiniLabel);
                        using(new GUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"{done}/{lesson.steps.Length} checkpoints",StudioTheme.Styles.miniLabel);
                            if(GUILayout.Button(done==0?"Start":done==lesson.steps.Length?"Replay":"Continue",GUILayout.Width(85)))
                            {owner.QueueTutorial(lesson.id,done==lesson.steps.Length);editorWindow.Close();}
                            if(done>0&&GUILayout.Button(new GUIContent("↺","Reset this lesson's completion"),GUILayout.Width(25)))
                                if(EditorUtility.DisplayDialog("Reset tutorial progress?","Clear the checklist for "+lesson.title+"? Geometry is unchanged.","Reset","Cancel"))
                                {StudioRetopologyLessons.Reset(lesson);if(owner.Document.tutorialId==lesson.id)owner.Document.tutorialActions.Clear();owner.repaint();}
                        }
                    }
                    GUILayout.Space(5);
                }
                EditorGUILayout.EndScrollView();
                if(owner.ExitTutorial!=null&&owner.CurrentLesson!=null&&GUILayout.Button("Return to my workspace",GUILayout.Height(26)))
                {owner.QueueExitTutorial();editorWindow.Close();}
            }
        }
    }
}
