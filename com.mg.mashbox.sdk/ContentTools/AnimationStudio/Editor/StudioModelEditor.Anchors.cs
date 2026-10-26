using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private const float AnchorGizmoBaseScale = 2.5f;
        private const float AnchorStrokeWidth = 2f, AnchorArmLength = 1.6f;
        private static readonly Vector2[] anchorUnitRing = BuildAnchorUnitRing();
        private readonly Vector3[] anchorRingPoints = new Vector3[65];
        private readonly Vector3[] anchorArmPoints = new Vector3[2];
        private static Vector2[] BuildAnchorUnitRing()
        {
            var points = new Vector2[65];
            for (int i = 0; i < 64; i++)
            {
                float angle = i * (Mathf.PI * 2 / 64);
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            points[64] = points[0];
            return points;
        }
        private void DrawAnchorArm(Vector3 center, Vector3 direction, float length, bool positiveOnly = false)
        {
            anchorArmPoints[0] = positiveOnly ? center : center - direction * length;
            anchorArmPoints[1] = center + direction * length;
            Handles.DrawAAPolyLine(AnchorStrokeWidth, anchorArmPoints);
        }
        private static Texture2D anchorIcon;
        private static Texture2D AnchorIcon
        {
            get
            {
                if (anchorIcon) return anchorIcon;
                // Rasterize at the hierarchy's display size. Pixel-aligned arms retain
                // full opacity instead of being faded twice by coverage and downsampling.
                anchorIcon = new Texture2D(16,16,TextureFormat.RGBA32,false) { name="MashBox Studio Anchor", hideFlags=HideFlags.HideAndDontSave };
                var pixels = new Color[256];
                for(int y=0;y<16;y++) for(int x=0;x<16;x++)
                {
                    float dx=x-8f, dy=y-8f, radius=Mathf.Sqrt(dx*dx+dy*dy);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(radius - 4.5f));
                    float horizontal = Mathf.Abs(dx) <= 7f ? Mathf.Clamp01(1f - Mathf.Abs(dy)) : 0f;
                    float vertical = Mathf.Abs(dy) <= 7f ? Mathf.Clamp01(1f - Mathf.Abs(dx)) : 0f;
                    var color = dx < 0 && horizontal > 0 ? new Color(.18f,.38f,.8f) : new Color(.82f,.56f,.3f);
                    color.a = Mathf.Max(ring, Mathf.Max(horizontal, vertical));
                    pixels[y*16+x] = color;
                }
                anchorIcon.SetPixels(pixels); anchorIcon.Apply(); return anchorIcon;
            }
        }
        private void CreateAnchor(Action<StudioModelDocument> loaded)
            => CreateAnchorAtOrigin(null, loaded);
        private void CreateAnchorAtOrigin(SceneView view, Action<StudioModelDocument> loaded)
        {
            using var camera = new CreationCameraScope(view);
            CancelMarkingMenu(); EnsureWorkspace(loaded);
            int parent=Active;
            var names=StudioAnchorCatalog.Names.OrderBy(n=>n=="Stem_Anchor" ? 0 : 1);
            string type=names.FirstOrDefault(n=>!Document.nodes.Any(node=>node.parent==parent && node.name==n));
            if (type==null) throw new InvalidOperationException("All available anchor types already exist under this parent.");
            int index=Document.AddAnchor(type,parent,CreationPosition(parent));
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
                    float size=HandleUtility.GetHandleSize(t.position)*.07f*AnchorGizmoBaseScale*StudioSettings.Current.handleSize;
                    var markerColor = objects.Contains(i) ? new Color(1f,.8f,.4f) : new Color(1f,.62f,.22f);
                    markerColor.r *= .85f; markerColor.g *= .85f; markerColor.b *= .85f;
                    Handles.color = markerColor;
                    var right = view.camera.transform.right * (size * .7f);
                    var up = view.camera.transform.up * (size * .7f);
                    for (int p = 0; p < anchorRingPoints.Length; p++)
                        anchorRingPoints[p] = t.position + right * anchorUnitRing[p].x + up * anchorUnitRing[p].y;
                    Handles.DrawAAPolyLine(AnchorStrokeWidth, anchorRingPoints);
                    float arm = size * AnchorArmLength;
                    DrawAnchorArm(t.position, t.right, arm);
                    DrawAnchorArm(t.position, t.up, arm);
                    DrawAnchorArm(t.position, -t.forward, arm, true);
                    var forwardColor = Handles.zAxisColor;
                    forwardColor.r *= .85f; forwardColor.g *= .85f; forwardColor.b *= .85f;
                    Handles.color = forwardColor;
                    DrawAnchorArm(t.position, t.forward, arm, true);
                }
            }
            finally { Handles.color=old; Handles.zTest=depth; }
        }
        private int PickAnchors(SceneView view,Rect rect,bool click)
        {
            int hit=-1; float best=12*AnchorGizmoBaseScale*StudioSettings.Current.handleSize;
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
