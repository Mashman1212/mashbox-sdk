using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private static Texture2D anchorIcon;
        private static Texture2D AnchorIcon
        {
            get
            {
                if (anchorIcon) return anchorIcon;
                anchorIcon = new Texture2D(20,20,TextureFormat.RGBA32,false) { name="MashBox Studio Anchor", hideFlags=HideFlags.HideAndDontSave };
                var pixels = new Color[400];
                for(int y=0;y<20;y++) for(int x=0;x<20;x++)
                {
                    float dx=x-9.5f, dy=y-9.5f, radius=Mathf.Sqrt(dx*dx+dy*dy);
                    bool ring=Mathf.Abs(radius-5)<1, cross=(Mathf.Abs(dx)<1 && Mathf.Abs(dy)<9)||(Mathf.Abs(dy)<1 && Mathf.Abs(dx)<9);
                    pixels[y*20+x]=ring||cross ? new Color(.35f,.85f,.9f) : Color.clear;
                }
                anchorIcon.SetPixels(pixels); anchorIcon.Apply(); return anchorIcon;
            }
        }
        private void CreateAnchor(Action<StudioModelDocument> loaded)
        {
            CancelMarkingMenu(); EnsureWorkspace(loaded);
            int parent=Active;
            var names=StudioAnchorCatalog.Names.OrderBy(n=>n=="Stem_Anchor" ? 0 : 1);
            string type=names.FirstOrDefault(n=>!Document.nodes.Any(node=>node.parent==parent && node.name==n));
            if (type==null) throw new InvalidOperationException("All available anchor types already exist under this parent.");
            int index=Document.AddAnchor(type,parent);
            mode=ComponentMode.Object; pivot=false; search="";
            Rebuild(); SelectObject(index); Changed(false);
        }
        private void DrawAnchorChannels()
        {
            var node=Document.nodes[Active];
            if (string.IsNullOrEmpty(node.anchorType)) return;
            GUILayout.Space(6);
            GUILayout.Label("Anchor Point",StudioTheme.Styles.boldLabel);
            var rect=EditorGUILayout.GetControlRect();
            rect=EditorGUI.PrefixLabel(rect,new GUIContent("Type"));
            if (EditorGUI.DropdownButton(rect,new GUIContent(node.anchorType),FocusType.Passive,StudioTheme.Styles.menuDropdown))
            {
                int index=Active;
                var used=Document.nodes.Where((n,i)=>i!=index && n.parent==node.parent).Select(n=>n.name).ToArray();
                PopupWindow.Show(rect,new AnchorTypePopup(node.anchorType,used,choice=>Run(()=>ChangeAnchorType(index,choice))));
            }
            GUILayout.Space(6);
        }
        internal void ChangeAnchorType(int index,string type)
        {
            Document.SetAnchorType(index,type);
            Transforms[index].name=type;
            Changed(false);
        }
        private void DrawAnchorMarkers(SceneView view)
        {
            if (!Document || Event.current.type!=EventType.Repaint) return;
            var old=Handles.color; var depth=Handles.zTest;
            Handles.zTest=UnityEngine.Rendering.CompareFunction.Always;
            try
            {
                for(int i=0;i<Transforms.Length;i++)
                {
                    var t=Transforms[i];
                    if(string.IsNullOrEmpty(Document.nodes[i].anchorType) || !t.gameObject.activeInHierarchy || view.camera.WorldToViewportPoint(t.position).z<=0) continue;
                    float size=HandleUtility.GetHandleSize(t.position)*.07f;
                    Handles.color=objects.Contains(i) ? StudioTheme.Current.Accent : new Color(.35f,.85f,.9f);
                    Handles.DrawWireDisc(t.position,view.camera.transform.forward,size*.7f);
                    Handles.DrawLine(t.position-t.right*size,t.position+t.right*size);
                    Handles.DrawLine(t.position-t.up*size,t.position+t.up*size);
                    Handles.DrawLine(t.position-t.forward*size,t.position+t.forward*size);
                }
            }
            finally { Handles.color=old; Handles.zTest=depth; }
        }
        private int PickAnchors(SceneView view,Rect rect,bool click)
        {
            int hit=-1; float best=12;
            for(int i=0;i<Transforms.Length;i++)
            {
                var t=Transforms[i];
                if(string.IsNullOrEmpty(Document.nodes[i].anchorType) || !t.gameObject.activeInHierarchy || view.camera.WorldToViewportPoint(t.position).z<=0) continue;
                var point=HandleUtility.WorldToGUIPoint(t.position);
                if (click)
                {
                    float distance=Vector2.Distance(end,point);
                    if(distance<best) { hit=i; best=distance; }
                }
                else if(rect.Contains(point)) { if(subtract) objects.Remove(i); else objects.Add(i); }
            }
            return hit;
        }
        private sealed class AnchorTypePopup : PopupWindowContent
        {
            private readonly string current;
            private readonly string[] used;
            private readonly Action<string> selected;
            private Vector2 scroll;
            private string search="";
            internal AnchorTypePopup(string current,string[] used,Action<string> selected)
            { this.current=current; this.used=used; this.selected=selected; }
            public override Vector2 GetWindowSize() => new Vector2(300,360);
            public override void OnGUI(Rect rect)
            {
                using(StudioTheme.Scope())
                {
                    EditorGUI.DrawRect(rect,StudioTheme.Current.Panel);
                    search=EditorGUILayout.TextField(search,StudioTheme.Styles.toolbarSearchField);
                    scroll=EditorGUILayout.BeginScrollView(scroll);
                    foreach(string type in StudioAnchorCatalog.Names)
                    {
                        if(type.IndexOf(search,StringComparison.OrdinalIgnoreCase)<0) continue;
                        using(new EditorGUI.DisabledScope(Array.IndexOf(used,type)>=0))
                            if(GUILayout.Button((type==current ? "✓  " : "    ")+type,StudioTheme.Styles.miniButton,GUILayout.Height(24)))
                            { selected(type); editorWindow.Close(); break; }
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
        }
    }
}
