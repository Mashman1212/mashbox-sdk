using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private const string HierarchyDragKey = "MashBoxStudio.HierarchyDrag";
        private StudioModelDocument.Node hierarchyRenameNode;
        private string hierarchyRenameText;
        private bool focusHierarchyRename;
        private Rect hierarchyRenameScreenRect;

        private void CommitHierarchyRenameOnClick()
        {
            if (hierarchyRenameNode == null || Event.current.type != EventType.MouseDown ||
                hierarchyRenameScreenRect.Contains(GUIUtility.GUIToScreenPoint(Event.current.mousePosition))) return;
            int index = Document ? Document.nodes.IndexOf(hierarchyRenameNode) : -1;
            string value = hierarchyRenameText;
            hierarchyRenameNode = null;
            GUI.FocusControl(null); EditorGUIUtility.editingTextField = false;
            if (index >= 0) Run(() => RenameObject(index, value));
        }

        internal void RenameObject(int index, string value)
        {
            var node = Document.nodes[index];
            if (!string.IsNullOrEmpty(node.anchorType)) return;
            value = (value ?? "").Trim();
            if (value == node.name) return;
            if (value.Length == 0 || value.IndexOfAny(new[] { '/', '\\', '\r', '\n', '\t' }) >= 0)
                throw new InvalidOperationException("Enter a nonempty object name without slashes or control characters.");
            if (Document.nodes.Any(other => other != node && other.parent == node.parent && other.name == value))
                throw new InvalidOperationException("An object with that name already exists under this parent.");
            Undo.RegisterCompleteObjectUndo(Document, "Rename object");
            node.name = value;
            Transforms[index].name = value;
            Changed(false);
        }

        private bool DrawHierarchyRename(Rect row, int index)
        {
            if (hierarchyRenameNode != Document.nodes[index]) return false;
            return DrawActiveHierarchyRename(row,index);
        }
        private bool DrawActiveHierarchyRename(Rect row,int index)
        {
            hierarchyRenameScreenRect = new Rect(GUIUtility.GUIToScreenPoint(row.position), row.size);
            var evt = Event.current;
            bool cancel = evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape;
            bool enter = evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter);
            bool outside = evt.type == EventType.MouseDown && !row.Contains(evt.mousePosition);
            if (cancel || enter || outside)
            {
                string value = hierarchyRenameText;
                hierarchyRenameNode = null;
                GUI.FocusControl(null); EditorGUIUtility.editingTextField = false;
                if (!cancel) Run(() => RenameObject(index, value));
                if (!outside) evt.Use();
                repaint();
                return false;
            }
            GUI.SetNextControlName("StudioHierarchyRename");
            hierarchyRenameText = EditorGUI.TextField(row, hierarchyRenameText);
            if (focusHierarchyRename)
            {
                EditorGUI.FocusTextInControl("StudioHierarchyRename");
                focusHierarchyRename = false;
            }
            return true;
        }
        private sealed class HierarchyDragPayload
        {
            internal StudioModelEditor owner;
            internal StudioModelDocument document;
            internal int[] selection;
            internal StudioModelDocument.Node[] nodes;
        }
        private int hierarchyDragControl, hierarchyDragButton;
        private Vector2 hierarchyDragStart;
        private EventModifiers hierarchyClickModifiers;
        private StudioModelDocument hierarchySelectionDocument;
        private StudioModelDocument.Node hierarchyLastClickedNode;

        private void SelectHierarchyRow(int index, bool keepMultiSelection)
        {
            var visible = new System.Collections.Generic.List<int>();
            foreach (int row in HierarchyRows())
                if (string.IsNullOrEmpty(search) || Document.nodes[row].name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    visible.Add(row);
            int lastClicked = hierarchySelectionDocument == Document && hierarchyLastClickedNode != null
                ? Document.nodes.IndexOf(hierarchyLastClickedNode) : Active;
            var evt = Event.current;
            var modifiers = evt.modifiers;
            System.Collections.Generic.List<int> selection;
            try
            {
                evt.modifiers = hierarchyClickModifiers;
                // Use the same range/toggle rules as Unity's hierarchy and TreeView.
#pragma warning disable CS0618
                selection = UnityEditorInternal.InternalEditorUtility.GetNewSelection(index, visible,
                    objects.ToList(), lastClicked, keepMultiSelection, false, true);
#pragma warning restore CS0618
            }
            finally { evt.modifiers = modifiers; }
            FinishChannelEdit();
            if (componentPivot) componentPivot.custom = false;
            StopCutTool(); Components.Clear();
            objects.Clear(); objects.UnionWith(selection);
            Active = objects.Contains(index) ? index : objects.Count > 0 ? objects.Last() : -1;
            hierarchySelectionDocument = Document;
            hierarchyLastClickedNode = Document.nodes[index];
            repaint();
        }
        // Keep the OS drag loop separate from IMGUI event handling for editor tests.
        private Action<string> beginHierarchyDrag = DragAndDrop.StartDrag;
        private void CancelHierarchyDrag()
        {
            if (hierarchyDragControl != 0 && GUIUtility.hotControl == hierarchyDragControl) GUIUtility.hotControl = 0;
            hierarchyDragControl = 0;
            if (DragAndDrop.GetGenericData(HierarchyDragKey) is HierarchyDragPayload payload && payload.owner == this)
                DragAndDrop.SetGenericData(HierarchyDragKey, null);
        }
        private void HandleHierarchyDragCancellation()
        {
            var evt = Event.current;
            // DragExited can be sent when the pointer leaves this window during a
            // live drag. Keep the payload so it can be brought back and dropped.
            if (evt.type == EventType.MouseDown && DragAndDrop.GetGenericData(HierarchyDragKey) is HierarchyDragPayload)
                CancelHierarchyDrag();
            if (StudioShortcuts.Pressed(StudioShortcut.Cancel,evt) &&
                (hierarchyDragControl != 0 || DragAndDrop.GetGenericData(HierarchyDragKey) is HierarchyDragPayload))
            { CancelHierarchyDrag(); evt.Use(); repaint(); }
        }
        private static readonly GUIContent HierarchyIconGap = new GUIContent("  ");
        private readonly GUIContent hierarchyRowText = new GUIContent();
        private void DrawHierarchyRowContent(Rect row, GUIContent content, GUIStyle style)
        {
            if (!content.image) { style.Draw(row, content, false, false, false, false); return; }
            const float iconSize = 16f;
            float gap = Mathf.Max(0, style.CalcSize(HierarchyIconGap).x - style.padding.horizontal);
            var iconRect = new Rect(row.x + style.padding.left, row.center.y - iconSize * .5f, iconSize, iconSize);
            GUI.DrawTexture(iconRect, content.image, ScaleMode.ScaleToFit, true);
            row.xMin = Mathf.Min(row.xMax, iconRect.xMax + gap - style.padding.left);
            hierarchyRowText.text = content.text;
            style.Draw(row, hierarchyRowText, false, false, false, false);
        }
        private void DrawHierarchyDragRow(Rect row, GUIContent content, GUIStyle style, int index)
        {
            if (DrawHierarchyRename(row, index)) return;
            int id = GUIUtility.GetControlID("StudioHierarchyRow".GetHashCode(), FocusType.Passive, row);
            var evt = Event.current;
            if (evt.type == EventType.Repaint)
            {
                bool hovered = !StudioTheme.HoverSuppressed && row.Contains(evt.mousePosition);
                bool pressed = !StudioTheme.HoverSuppressed && GUIUtility.hotControl == id;
                if (objects.Contains(index) || pressed) EditorGUI.DrawRect(row, StudioTheme.Current.Selection);
                else if (hovered) EditorGUI.DrawRect(row, StudioTheme.Current.Hover);
                DrawHierarchyRowContent(row, content, style);
            }
            HandleHierarchyDrop(row, index);
            if (evt.type == EventType.MouseDown && row.Contains(evt.mousePosition) && (evt.button == 0 || evt.button == 2))
            {
                if (evt.button == 0 && evt.clickCount == 2 && string.IsNullOrEmpty(Document.nodes[index].anchorType))
                {
                    CancelHierarchyDrag(); SelectObject(index);
                    hierarchyRenameNode = Document.nodes[index]; hierarchyRenameText = hierarchyRenameNode.name;
                    focusHierarchyRename = true;
                    evt.Use(); repaint(); return;
                }
                hierarchyClickModifiers = evt.modifiers;
                hierarchyDragControl = id; hierarchyDragButton = evt.button;
                hierarchyDragStart = GUIUtility.GUIToScreenPoint(evt.mousePosition);
                GUIUtility.hotControl = id; evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == id && hierarchyDragControl == id)
            {
                if ((GUIUtility.GUIToScreenPoint(evt.mousePosition) - hierarchyDragStart).sqrMagnitude >= 25)
                {
                    SelectHierarchyRow(index, true);
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>(); DragAndDrop.paths = Array.Empty<string>();
                    var selection = Document.OrderedHierarchy().Where(objects.Contains).ToArray();
                    DragAndDrop.SetGenericData(HierarchyDragKey, new HierarchyDragPayload { owner = this, document = Document,
                        selection = selection, nodes = selection.Select(i => Document.nodes[i]).ToArray() });
                    GUIUtility.hotControl = 0; hierarchyDragControl = 0;
                    beginHierarchyDrag(objects.Count == 1 ? Document.nodes[index].name : "MashBox Studio objects");
                }
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == hierarchyDragButton && GUIUtility.hotControl == id)
            {
                if (row.Contains(evt.mousePosition) && hierarchyDragButton == 0)
                    SelectHierarchyRow(index, false);
                GUIUtility.hotControl = 0; hierarchyDragControl = 0; evt.Use();
            }
        }
        private void HandleHierarchyDrop(Rect rect, int parent)
        {
            if (!(DragAndDrop.GetGenericData(HierarchyDragKey) is HierarchyDragPayload payload)) return;
            HandleActiveHierarchyDrop(rect,parent,payload);
        }
        internal struct HierarchyDropPlacement
        {
            internal int Parent, Before;
            internal bool Insert;
            internal Vector2 Line;
        }
        internal HierarchyDropPlacement ResolveHierarchyDrop(Rect row,int index,Vector2 pointer)
        {
            if(index<0)return new HierarchyDropPlacement { Parent=-1,Before=-1 };
            HierarchyRows();
            int depth=hierarchyDepths[index];float rootX=row.x-MinHierarchyIndent(depth);
            bool before=pointer.y<row.y+Mathf.Min(5,row.height*.25f);
            bool after=pointer.y>row.yMax-Mathf.Min(5,row.height*.25f);
            bool left=pointer.x<row.x-3;
            if(!before&&!after&&!left)return new HierarchyDropPlacement { Parent=index,Before=-1 };
            if(left&&!before&&!after)after=pointer.y>=row.center.y;
            if(!before&&!after)before=true;
            bool expandedChildren=hierarchyParents.Contains(index)&&(!collapsedHierarchy.Contains(Document.nodes[index])||!string.IsNullOrEmpty(search));
            int wanted=left ? Mathf.Max(0,depth-Mathf.CeilToInt((row.x-pointer.x)/12))
                : depth+(after&&expandedChildren&&pointer.x>=row.x+12?1:0);
            int parent, sibling;
            if(after&&wanted>depth)
            {
                parent=index;var children=Document.OrderedChildren(index);sibling=children.Length==0?-1:children[0];
            }
            else
            {
                int reference=index;
                while(hierarchyDepths[reference]>wanted&&Document.nodes[reference].parent>=0)reference=Document.nodes[reference].parent;
                parent=Document.nodes[reference].parent;
                if(before && reference==index)sibling=reference;
                else
                {
                    var siblings=Document.OrderedChildren(parent);int at=Array.IndexOf(siblings,reference);
                    sibling=at+1<siblings.Length?siblings[at+1]:-1;
                }
            }
            int targetDepth=parent<0?0:hierarchyDepths[parent]+1;
            return new HierarchyDropPlacement { Parent=parent,Before=sibling,Insert=true,
                Line=new Vector2(rootX+MinHierarchyIndent(targetDepth),before?row.y:row.yMax) };
        }
        private static float MinHierarchyIndent(int depth)=>Mathf.Min(depth*12,96);
        private void HandleActiveHierarchyDrop(Rect rect,int index,HierarchyDragPayload payload)
        {
            var evt = Event.current;
            var hitRect=rect;
            if(index>=0) { HierarchyRows();hitRect.xMin-=14+MinHierarchyIndent(hierarchyDepths[index]); }
            if(index>=0) { hitRect.yMin-=2;hitRect.yMax+=2; }
            if (!hitRect.Contains(evt.mousePosition)) return;
            var placement=ResolveHierarchyDrop(rect,index,evt.mousePosition);
            string reason = "Drop into the source workspace.";
            bool valid = payload.owner == this && payload.document == Document && Document &&
                payload.selection.Length==payload.nodes.Length && payload.selection.Select((i,n)=>i>=0&&i<Document.nodes.Count&&ReferenceEquals(Document.nodes[i],payload.nodes[n])).All(same=>same) &&
                Document.CanPlaceHierarchy(payload.selection,placement.Parent,placement.Before,Transforms,out reason);
            if (evt.type == EventType.Repaint)
            {
                var color=valid?StudioTheme.Current.Accent:new Color(1,.25f,.2f);
                if(placement.Insert)
                {
                    EditorGUI.DrawRect(new Rect(placement.Line.x,placement.Line.y-1,Mathf.Max(1,rect.xMax-placement.Line.x),2),color);
                    EditorGUI.DrawRect(new Rect(placement.Line.x,placement.Line.y-3,2,6),color);
                }
                else
                {
                    EditorGUI.DrawRect(rect,StudioTheme.Alpha(color,.22f));
                    if(index<0)GUI.Label(rect,valid?"Drop to move to root":reason,StudioTheme.Styles.miniLabel);
                }
            }
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
            if (evt.type == EventType.DragPerform)
            {
                if (valid)
                {
                    DragAndDrop.AcceptDrag();
                    Run(() => PlaceHierarchy(payload.selection,placement.Parent,placement.Before));
                }
                else if(reason!="Already in this position.") { error = reason; repaint(); }
                CancelHierarchyDrag();
            }
            evt.Use();
        }
        internal void PlaceHierarchy(int[] selection,int parent,int beforeSibling)
        {
            Document.PlaceHierarchy(selection,parent,beforeSibling,Transforms);
            CancelMarkingMenu();EndPivotSnap();StopCutTool();pivot=false;Components.Clear();
            if(parent>=0)for(int p=parent;p>=0;p=Document.nodes[p].parent)collapsedHierarchy.Remove(Document.nodes[p]);
            hierarchyRows=null;Changed(false);
        }
        internal void ReparentHierarchy(int[] selection, int parent) => PlaceHierarchy(selection,parent,-1);
    }
}
