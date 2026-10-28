using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class StudioImagingWindow
    {
        private enum ImageUvMode { Paint, UV, Edge, Face, Shell }
        private enum ImageUvTool { Move, Rotate, Scale }
        [Serializable] private sealed class ImageUvMesh
        {
            public Renderer renderer;
            public Mesh source, edited;
            public bool dirty;
        }
        [SerializeField] private List<ImageUvMesh> imageUvMeshes = new List<ImageUvMesh>();
        [SerializeField] private ImageUvMode imageUvMode;
        [SerializeField] private ImageUvTool imageUvTool;
        private readonly HashSet<int> imageUvSelection = new HashSet<int>();
        private readonly HashSet<int[]> imageUvSelectedFaces = new HashSet<int[]>();
        private void ClearImageUvSelection() { imageUvSelection.Clear(); imageUvSelectedFaces.Clear(); }
        private void RebuildImageUvFaceVertices()
        {
            imageUvSelection.Clear();
            foreach(var face in imageUvSelectedFaces)imageUvSelection.UnionWith(face);
        }
        private readonly HashSet<int> imageUvHover = new HashSet<int>();
        private readonly List<Vector2[]> imageUvUndo = new List<Vector2[]>(), imageUvRedo = new List<Vector2[]>();
        private Mesh imageUvTarget;
        private int imageUvChannel = -1, imageUvSubmesh = -1;
        private Vector2[] imageUvPoints, imageUvStart;
        private int[] imageUvTriangles, imageUvRoots;
        private int[] imageUvVertices;
        private Vector3[] imageUvGuiPoints, imageUvWireLines;
        private readonly List<Vector3> imageUvSelectedLines = new List<Vector3>();
        private readonly List<Vector3> imageUvHoverLines = new List<Vector3>();
        private readonly HashSet<Vector2Int> imageUvDotPixels = new HashSet<Vector2Int>();
        private int[][] imageUvFaces;
        private readonly List<int[]> imageUvEdges = new List<int[]>();
        private bool imageUvWheel, imageUvDragging, imageUvMarquee, imageUvAdd;
        private Vector2 imageUvWheelCenter, imageUvAnchor, imageUvPointer, imageUvPivot;
        private Vector2 imageUvWheelStart;
        private Vector2 imageUvWheelPointer;
        private int imageUvWheelControl, imageUvWheelHover = -1;
        private GUIStyle imageUvWheelLabel;
        // Match the Modeler's north/NE/east/south/west marking-menu layout.
        private static readonly int[] ImageUvWheelSectors = { 1, 6, 0, 4, 2 };
        private static readonly Vector2[] ImageUvWheelOffsets = {
            new Vector2(92,-28), new Vector2(-106,0), new Vector2(0,-54), new Vector2(0,54), new Vector2(106,0) };
        private Rect ImageUvWheelRect(int index) => new Rect(imageUvWheelCenter + ImageUvWheelOffsets[index] - new Vector2(47,10), new Vector2(94,20));
        private void CancelImageUvWheel()
        {
            if (imageUvWheel && GUIUtility.hotControl == imageUvWheelControl) GUIUtility.hotControl = 0;
            imageUvWheel = false; imageUvWheelHover = -1;
        }
        private void UpdateImageUvWheelHover()
        {
            imageUvWheelHover = -1;
            if ((imageUvWheelPointer-imageUvWheelStart).sqrMagnitude < 24*24 ||
                (imageUvWheelPointer-imageUvWheelCenter).sqrMagnitude < 24*24) return;
            for (int i=0;i<5;i++) if (ImageUvWheelRect(i).Contains(imageUvWheelPointer)) { imageUvWheelHover=i; return; }
            imageUvWheelHover = Array.IndexOf(ImageUvWheelSectors, StudioModelEditor.MarkingSector(imageUvWheelPointer-imageUvWheelStart));
        }
        private int imageUvControl, imageUvAxis;

        private static bool HasImageUvChannel(Mesh mesh, int channel) => mesh && mesh.vertexCount > 0 &&
            mesh.HasVertexAttribute((UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel));

        private void SelectImageUvChannel(int channel)
        {
            FinishImageUvGesture(false);
            uvChannel = (MeshUvChannel)channel;
            imageUvTarget = null;
            ClearImageUvSelection();
            imageUvUndo.Clear(); imageUvRedo.Clear();
            uvShellWires.Clear();
            MarkCompositeDirty(); Repaint();
        }

        private void DrawImageUvChannelMenu(Rect rect)
        {
            var mesh = targetRenderer ? GetRendererMesh(targetRenderer) : null;
            int first = -1, next = -1;
            for (int i = 0; i < 4; i++)
                if (HasImageUvChannel(mesh, i)) { if (first < 0) first = i; }
                else if (next < 0) next = i;
            if (first >= 0 && !HasImageUvChannel(mesh, (int)uvChannel)) SelectImageUvChannel(first);
            if (!GUI.Button(rect, first < 0 ? "UV —" : uvChannel.ToString(), StudioTheme.Styles.overlayDropdown)) return;
            var menu = new StudioMenu();
            for (int i = 0; i < 4; i++)
            {
                if (!HasImageUvChannel(mesh, i)) continue;
                int channel = i;
                menu.AddItem(new GUIContent("UV" + channel), (int)uvChannel == channel, () => SelectImageUvChannel(channel));
            }
            if (first < 0) menu.AddDisabledItem(new GUIContent("No UV channels"));
            menu.AddSeparator("");
            bool editable = mesh && targetRenderer && !EditorUtility.IsPersistent(targetRenderer);
            if (editable && next >= 0)
            {
                int destination = next;
                menu.AddItem(new GUIContent("Add UV" + destination + "/Empty channel"), false, () => AddImageUvChannel(destination, -1));
                for (int i = 0; i < 4; i++)
                {
                    if (!HasImageUvChannel(mesh, i)) continue;
                    int source = i;
                    menu.AddItem(new GUIContent("Add UV" + destination + "/Copy from UV" + source), false,
                        () => AddImageUvChannel(destination, source));
                }
            }
            else menu.AddDisabledItem(new GUIContent(next < 0 ? "UV0–UV3 already exist" : "Add UV channel (link a scene mesh)"));
            menu.DropDown(rect);
        }

        private ImageUvMesh GetOrCreateImageUvMesh(Mesh mesh)
        {
            var edit = imageUvMeshes.Find(item => item.renderer == targetRenderer && (item.source == mesh || item.edited == mesh));
            if (edit != null) return edit;
            edit = new ImageUvMesh { renderer = targetRenderer, source = mesh, edited = Instantiate(mesh) };
            // Imported meshes need not enable runtime Read/Write for editor UV work.
            // Populate the private clone using the same native-data path as the Modeler.
            if (!edit.edited.isReadable) StudioMeshToolPreview.CopyMesh(mesh, edit.edited);
            edit.edited.name = mesh.name + " • UV edits";
            edit.edited.hideFlags = HideFlags.HideAndDontSave;
            imageUvMeshes.Add(edit);
            return edit;
        }

        private void AddImageUvChannel(int destination, int source)
        {
            var mesh = targetRenderer ? GetRendererMesh(targetRenderer) : null;
            if (!mesh || EditorUtility.IsPersistent(targetRenderer) || HasImageUvChannel(mesh, destination)) return;
            if (source >= 0 && !HasImageUvChannel(mesh, source)) return;
            FinishImageUvGesture(false);
            var edit = GetOrCreateImageUvMesh(mesh);
            if (source >= 0)
            {
                var points = new List<Vector4>(); edit.edited.GetUVs(source, points);
                edit.edited.SetUVs(destination, points);
            }
            else edit.edited.SetUVs(destination, new Vector2[mesh.vertexCount]);
            AssignImageUvMesh(targetRenderer, edit.edited);
            edit.dirty = true; hasUnsavedChanges = true;
            saveChangesMessage = "Save image layers and edited UV mesh copies before closing?";
            SelectImageUvChannel(destination);
            SurfaceChanged?.Invoke(); SceneView.RepaintAll();
            status = source < 0 ? $"Added empty UV{destination}." : $"Copied UV{source} to UV{destination}.";
            statusType = MessageType.Info;
        }

        private static void AssignImageUvMesh(Renderer renderer, Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer skin) skin.sharedMesh = mesh;
            else if (renderer && renderer.TryGetComponent<MeshFilter>(out var filter)) filter.sharedMesh = mesh;
        }
        private void RestoreImageUvMeshes()
        {
            FinishImageUvGesture(true);
            foreach (var edit in imageUvMeshes)
                if (edit.renderer && edit.source && GetRendererMesh(edit.renderer) == edit.edited)
                    AssignImageUvMesh(edit.renderer, edit.source);
        }
        private void ResumeImageUvMeshes()
        {
            foreach (var edit in imageUvMeshes)
                if (edit.renderer && edit.edited && GetRendererMesh(edit.renderer) == edit.source)
                    AssignImageUvMesh(edit.renderer, edit.edited);
            imageUvTarget = null;
        }
        private void OnDestroy()
        {
            RestoreImageUvMeshes();
            foreach (var edit in imageUvMeshes) if (edit.edited) DestroyImmediate(edit.edited);
            imageUvMeshes.Clear();
        }
        private bool EnsureImageUvTarget()
        {
            if (!targetRenderer || EditorUtility.IsPersistent(targetRenderer))
            { status = "Link a scene mesh to edit UVs."; statusType = MessageType.Info; return false; }
            var mesh = GetRendererMesh(targetRenderer);
            if (!mesh || mesh.vertexCount == 0 || mesh.subMeshCount == 0)
            { status = "Link a mesh with UV geometry to edit."; statusType = MessageType.Warning; return false; }
            if (imageUvTarget == mesh && imageUvChannel == (int)uvChannel && imageUvSubmesh == materialIndex) return true;
            FinishImageUvGesture(false);
            var edit = GetOrCreateImageUvMesh(mesh);
            AssignImageUvMesh(targetRenderer, edit.edited);
            imageUvTarget = edit.edited; imageUvChannel = (int)uvChannel; imageUvSubmesh = materialIndex;
            var points = new List<Vector2>(); imageUvTarget.GetUVs(imageUvChannel, points);
            if (points.Count != imageUvTarget.vertexCount)
            { imageUvTarget = null; status = "This mesh has no complete UV set in the selected channel."; statusType = MessageType.Warning; return false; }
            imageUvPoints = points.ToArray();
            imageUvTriangles = imageUvTarget.GetTriangles(Mathf.Clamp(materialIndex, 0, imageUvTarget.subMeshCount - 1));
            imageUvVertices = imageUvTriangles.Distinct().ToArray();
            imageUvGuiPoints = new Vector3[imageUvPoints.Length];
            ClearImageUvSelection(); imageUvHover.Clear(); imageUvUndo.Clear(); imageUvRedo.Clear();
            imageUvEdges.Clear(); var seen = new HashSet<ulong>();
            imageUvRoots = Enumerable.Range(0, points.Count).ToArray();
            int Root(int n) { while (imageUvRoots[n] != n) n = imageUvRoots[n]; return n; }
            void Edge(int a, int b)
            {
                imageUvRoots[Root(b)] = Root(a);
                ulong key = ((ulong)(uint)Math.Min(a,b) << 32) | (uint)Math.Max(a,b);
                if (seen.Add(key)) imageUvEdges.Add(new[] { a,b });
            }
            for (int i=0;i<imageUvTriangles.Length;i+=3)
            { Edge(imageUvTriangles[i],imageUvTriangles[i+1]); Edge(imageUvTriangles[i+1],imageUvTriangles[i+2]); Edge(imageUvTriangles[i+2],imageUvTriangles[i]); }
            // Join normal-split vertices only when both position and UV coincide.
            var vertices = imageUvTarget.vertices;
            var welds = new Dictionary<(Vector3,Vector2),int>();
            foreach (int i in imageUvVertices)
            {
                var key = (vertices[i], imageUvPoints[i]);
                if (welds.TryGetValue(key,out int other)) imageUvRoots[Root(i)] = Root(other); else welds[key] = i;
            }
            for(int i=0;i<imageUvRoots.Length;i++)imageUvRoots[i]=Root(i);
            imageUvFaces = new int[imageUvTriangles.Length / 3][];
            var projected = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = mesh.indexFormat };
            HashSet<ulong> diagonals;
            try
            {
                projected.vertices = imageUvPoints.Select(uv => new Vector3(uv.x,uv.y,0)).ToArray();
                projected.triangles = imageUvTriangles;
                diagonals = StudioQuadWire.Diagonals(projected);
            }
            finally { DestroyImmediate(projected); }
            var pairs = new Dictionary<ulong,int>();
            for(int n=0;n<imageUvFaces.Length;n++)
            {
                var face = new[]{imageUvTriangles[n*3],imageUvTriangles[n*3+1],imageUvTriangles[n*3+2]};
                imageUvFaces[n] = face;
                for(int j=0;j<3;j++)
                {
                    int a=face[j],b=face[(j+1)%3]; ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
                    if(!diagonals.Contains(key))continue;
                    if(pairs.TryGetValue(key,out int other))imageUvFaces[n]=imageUvFaces[other]=face.Union(imageUvFaces[other]).ToArray();
                    else pairs[key]=n;
                }
            }
            imageUvEdges.RemoveAll(edge => diagonals.Contains(((ulong)(uint)Math.Min(edge[0],edge[1])<<32)|(uint)Math.Max(edge[0],edge[1])));
            imageUvWireLines = new Vector3[imageUvEdges.Count * 2];
            return true;
        }
        private void SetImageUvMode(ImageUvMode mode)
        {
            FinishImageUvGesture(false); CancelImageUvWheel();
            FinishInlineImageText(true); EndImageTransform(); EndImageMarquee(); EndImageBrushAdjustment(); EndSurfaceStroke();
            GUIUtility.keyboardControl=0; EditorGUIUtility.editingTextField=false;
            imageUvMode = mode; ClearImageUvSelection(); imageUvHover.Clear();
            if (mode != ImageUvMode.Paint) { showUvOverlay = true; EnsureImageUvTarget(); }
            Repaint();
        }
        private void ImageUvHistory(bool redo)
        {
            if (!EnsureImageUvTarget()) return;
            FinishImageUvGesture(false);
            var from = redo ? imageUvRedo : imageUvUndo; var to = redo ? imageUvUndo : imageUvRedo;
            if (from.Count == 0) return;
            to.Add((Vector2[])imageUvPoints.Clone()); imageUvPoints = from[from.Count-1]; from.RemoveAt(from.Count-1);
            ApplyImageUvPoints();
            var edit = imageUvMeshes.Find(item => item.edited == imageUvTarget);
            if (edit != null) edit.dirty = true;
            hasUnsavedChanges = true;
            saveChangesMessage = "Save image layers and edited UV mesh copies before closing?";
        }
        private void ApplyImageUvPoints()
        {
            if (!imageUvTarget || imageUvPoints == null) return;
            imageUvTarget.SetUVs(imageUvChannel, imageUvPoints);
            uvShellWires.Clear(); SceneView.RepaintAll(); SurfaceChanged?.Invoke(); Repaint();
        }
        private void TransformImageUvSelection(Vector2 delta, float angle, float scale)
        {
            foreach(int i in imageUvSelection)
            {
                var offset=imageUvStart[i]-imageUvPivot;
                if(imageUvTool==ImageUvTool.Move)imageUvPoints[i]=imageUvStart[i]+new Vector2(imageUvAxis==2?0:delta.x,imageUvAxis==1?0:delta.y);
                else if(imageUvTool==ImageUvTool.Rotate)imageUvPoints[i]=imageUvPivot+new Vector2(offset.x*Mathf.Cos(angle)-offset.y*Mathf.Sin(angle),offset.x*Mathf.Sin(angle)+offset.y*Mathf.Cos(angle));
                else imageUvPoints[i]=imageUvPivot+Vector2.Scale(offset,new Vector2(imageUvAxis==2?1:scale,imageUvAxis==1?1:scale));
            }
            ApplyImageUvPoints();
        }
        private void FinishImageUvGesture(bool cancel)
        {
            if (imageUvDragging && imageUvStart != null)
            {
                if (cancel) { imageUvPoints = imageUvStart; ApplyImageUvPoints(); }
                else if (!imageUvPoints.SequenceEqual(imageUvStart))
                {
                    imageUvUndo.Add(imageUvStart); if(imageUvUndo.Count>64)imageUvUndo.RemoveAt(0); imageUvRedo.Clear();
                    var edit = imageUvMeshes.Find(item => item.edited == imageUvTarget);
                    if(edit != null)edit.dirty = true;
                    hasUnsavedChanges = true;
                    saveChangesMessage = "Save image layers and edited UV mesh copies before closing?";
                }
            }
            imageUvStart = null; imageUvDragging = imageUvMarquee = false;
            if (GUIUtility.hotControl == imageUvControl) GUIUtility.hotControl = 0;
        }
        private bool HandleImageUvKeys()
        {
            if (imageUvWheel && Event.current.type == EventType.KeyDown)
            {
                if(Event.current.keyCode == KeyCode.Escape) CancelImageUvWheel();
                Event.current.Use(); Repaint(); return true;
            }
            if (imageUvMode == ImageUvMode.Paint || EditorGUIUtility.editingTextField) return false;
            var e = Event.current;
            if (e.type != EventType.KeyDown) return true;
            if ((e.control || e.command) && (e.keyCode == KeyCode.Z || e.keyCode == KeyCode.Y))
                ImageUvHistory(e.keyCode == KeyCode.Y || e.shift);
            else if ((e.control || e.command) && e.keyCode == KeyCode.A)
            {
                if(EnsureImageUvTarget())
                {
                    foreach(int i in imageUvTriangles)imageUvSelection.Add(i);
                    if(imageUvMode==ImageUvMode.Face)foreach(var face in imageUvFaces)imageUvSelectedFaces.Add(face);
                }
            }
            else if (e.keyCode == KeyCode.Escape) { FinishImageUvGesture(true); imageUvWheel = false; }
            else if (e.keyCode == KeyCode.W) imageUvTool = ImageUvTool.Move;
            else if (e.keyCode == KeyCode.E) imageUvTool = ImageUvTool.Rotate;
            else if (e.keyCode == KeyCode.R) imageUvTool = ImageUvTool.Scale;
            else return true;
            e.Use(); Repaint(); return true;
        }
        private void ShieldImageUvTools(UnityEngine.UIElements.KeyDownEvent evt)
        {
            if(imageUvMode==ImageUvMode.Paint || imageUvWheel || EditorGUIUtility.editingTextField || evt.ctrlKey || evt.commandKey || evt.altKey)return;
            if(evt.keyCode==KeyCode.W)imageUvTool=ImageUvTool.Move;
            else if(evt.keyCode==KeyCode.E)imageUvTool=ImageUvTool.Rotate;
            else if(evt.keyCode==KeyCode.R)imageUvTool=ImageUvTool.Scale;
            else return;
            evt.imguiEvent?.Use(); evt.StopImmediatePropagation(); Repaint();
        }
        private Vector2 ImageUvCenter()
        {
            Vector2 center = Vector2.zero; foreach (int i in imageUvSelection) center += imageUvPoints[i];
            return center / Mathf.Max(1,imageUvSelection.Count);
        }
        private static float ImageUvEdgeDistance(Vector2 p, Vector2 a, Vector2 b)
        { var v=b-a;return Vector2.Distance(p,a+v*Mathf.Clamp01(Vector2.Dot(p-a,v)/Mathf.Max(1e-12f,v.sqrMagnitude))); }
        private HashSet<int> PickImageUvs(Vector2 mouse, Rect rect)
        {
            var hit = new HashSet<int>();
            if (imageUvMode == ImageUvMode.UV)
            {
                float nearest=9; int index=-1;
                foreach(int i in imageUvVertices) {float d=Vector2.Distance(mouse,UvToGui(imageUvPoints[i],rect));if(d<nearest){nearest=d;index=i;}}
                if(index>=0)hit.Add(index);
            }
            else if (imageUvMode == ImageUvMode.Edge)
            {
                float nearest=8; int[] edge=null;
                foreach(var pair in imageUvEdges){float d=ImageUvEdgeDistance(mouse,UvToGui(imageUvPoints[pair[0]],rect),UvToGui(imageUvPoints[pair[1]],rect));if(d<nearest){nearest=d;edge=pair;}}
                if(edge!=null)hit.UnionWith(edge);
            }
            else
            {
                for(int n=imageUvTriangles.Length-3;n>=0;n-=3)
                {
                    int a=imageUvTriangles[n],b=imageUvTriangles[n+1],c=imageUvTriangles[n+2];
                    Vector2 p=UvToGui(imageUvPoints[a],rect),q=UvToGui(imageUvPoints[b],rect),r=UvToGui(imageUvPoints[c],rect);
                    float Cross(Vector2 x,Vector2 y)=>x.x*y.y-x.y*y.x;
                    float area=Cross(q-p,r-p); if(Mathf.Abs(area)<1e-5f)continue;
                    float u=Cross(q-mouse,r-mouse)/area,v=Cross(r-mouse,p-mouse)/area;
                    if(u<0 || v<0 || u+v>1)continue;
                    if(imageUvMode==ImageUvMode.Face)hit.UnionWith(imageUvFaces[n/3]);
                    else foreach(int i in imageUvTriangles)if(imageUvRoots[i]==imageUvRoots[a])hit.Add(i);
                    break;
                }
            }
            return hit;
        }
        private bool HandleImageUvCanvas(Rect rect, bool inside)
        {
            var e=Event.current;
            int id=GUIUtility.GetControlID("Imaging UV controls".GetHashCode(),FocusType.Passive);
            if(inside && e.type==EventType.MouseDown && e.button==1 && !e.alt && GUIUtility.hotControl==0)
            {
                FinishImageUvGesture(false); imageUvWheel=true;
                imageUvWheelControl=id; GUIUtility.hotControl=id; GUIUtility.keyboardControl=0;
                EditorGUIUtility.editingTextField=false;
                imageUvWheelHover=-1;
                imageUvWheelStart=imageUvWheelPointer=e.mousePosition;
                imageUvWheelCenter=new Vector2(Mathf.Clamp(e.mousePosition.x,160,Mathf.Max(160,canvasArea.width-160)),Mathf.Clamp(e.mousePosition.y,74,Mathf.Max(74,canvasArea.height-74)));
                e.Use();Repaint();return true;
            }
            if(imageUvWheel)
            {
                if(e.type==EventType.MouseLeaveWindow || GUIUtility.hotControl!=imageUvWheelControl)
                { CancelImageUvWheel(); Repaint(); return true; }
                if(e.type==EventType.MouseDrag || e.type==EventType.MouseMove || (e.type==EventType.MouseUp && e.button==1))
                {
                    imageUvWheelPointer=e.mousePosition; UpdateImageUvWheelHover();
                    if(e.type==EventType.MouseUp)
                    {
                        int choice=imageUvWheelHover; CancelImageUvWheel();
                        if(choice>=0)SetImageUvMode((ImageUvMode)choice);
                    }
                    e.Use(); Repaint();
                }
                else if(e.isMouse || e.isKey || e.type==EventType.ScrollWheel || e.type==EventType.ContextClick)e.Use();
                return true;
            }
            if(imageUvMode==ImageUvMode.Paint)return false;
            if(e.type==EventType.ContextClick){e.Use();return true;}
            if(!EnsureImageUvTarget())return true;
            if(e.type==EventType.MouseMove || e.type==EventType.MouseDrag || e.type==EventType.MouseDown || e.type==EventType.MouseUp || e.type==EventType.MouseLeaveWindow)
            {
                imageUvHover.Clear();
                if(inside && !imageUvDragging && !imageUvMarquee && !e.alt && e.type!=EventType.MouseLeaveWindow)
                    imageUvHover.UnionWith(PickImageUvs(e.mousePosition,rect));
                Repaint();
            }
            if(inside && (e.type==EventType.ScrollWheel || e.button==2 || e.alt))
            {
                if(e.type==EventType.ScrollWheel){zoom=Mathf.Clamp(zoom*Mathf.Exp(-e.delta.y*.06f),.1f,12);e.Use();Repaint();}
                else if(e.type==EventType.MouseDrag){pan+=e.delta;e.Use();Repaint();}
                return true;
            }
            if(e.type==EventType.MouseDown && e.button==0 && inside)
            {
                imageUvControl=id;GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;
                EditorGUIUtility.editingTextField=false;
                imageUvAnchor=imageUvPointer=e.mousePosition;imageUvAdd=e.shift;
                var center=(Vector2)UvToGui(ImageUvCenter(),rect);imageUvAxis=0;
                bool handle=!e.shift && imageUvSelection.Count>0 && (Vector2.Distance(center,e.mousePosition)<12 ||
                    ImageUvEdgeDistance(e.mousePosition,center,center+new Vector2(55,0))<8 ||
                    ImageUvEdgeDistance(e.mousePosition,center,center+new Vector2(0,-55))<8 ||
                    (imageUvTool==ImageUvTool.Rotate && Mathf.Abs(Vector2.Distance(center,e.mousePosition)-45)<8));
                if(handle && imageUvTool!=ImageUvTool.Rotate)
                { if(e.mousePosition.x>center.x+12)imageUvAxis=1;else if(e.mousePosition.y<center.y-12)imageUvAxis=2; }
                if(!handle)
                {
                    var hit=PickImageUvs(e.mousePosition,rect);
                    if(hit.Count==0){imageUvMarquee=true;if(!e.shift)ClearImageUvSelection();}
                    else if(imageUvMode==ImageUvMode.Face)
                    {
                        var face=imageUvFaces.FirstOrDefault(candidate=>hit.SetEquals(candidate));
                        if(face!=null)
                        {
                            if(e.shift){if(!imageUvSelectedFaces.Add(face))imageUvSelectedFaces.Remove(face);}
                            else if(!imageUvSelectedFaces.Contains(face)){imageUvSelectedFaces.Clear();imageUvSelectedFaces.Add(face);}
                            RebuildImageUvFaceVertices();
                        }
                    }
                    else if(e.shift){foreach(int i in hit)if(!imageUvSelection.Add(i))imageUvSelection.Remove(i);}
                    else if(!hit.All(imageUvSelection.Contains)){imageUvSelection.Clear();imageUvSelection.UnionWith(hit);}
                }
                if(!imageUvMarquee && imageUvSelection.Count>0 && !e.shift)
                {imageUvDragging=true;imageUvStart=(Vector2[])imageUvPoints.Clone();imageUvPivot=ImageUvCenter();}
                e.Use();Repaint();
            }
            else if(e.type==EventType.MouseDrag && GUIUtility.hotControl==imageUvControl)
            {
                imageUvPointer=e.mousePosition;
                if(imageUvDragging)
                {
                    Vector2 delta=new Vector2(e.mousePosition.x-imageUvAnchor.x,imageUvAnchor.y-e.mousePosition.y)/rect.width;
                    var center=(Vector2)UvToGui(imageUvPivot,rect);
                    float angle=Vector2.SignedAngle(imageUvAnchor-center,e.mousePosition-center)*-Mathf.Deg2Rad;
                    float scale=Mathf.Exp(Mathf.Clamp((e.mousePosition.x-imageUvAnchor.x+imageUvAnchor.y-e.mousePosition.y)*.01f,-7,7));
                    TransformImageUvSelection(delta,angle,scale);
                }
                e.Use();Repaint();
            }
            else if(e.rawType==EventType.MouseUp && e.button==0 && GUIUtility.hotControl==imageUvControl)
            {
                if(imageUvMarquee)
                {
                    var box=Rect.MinMaxRect(Mathf.Min(imageUvAnchor.x,imageUvPointer.x),Mathf.Min(imageUvAnchor.y,imageUvPointer.y),Mathf.Max(imageUvAnchor.x,imageUvPointer.x),Mathf.Max(imageUvAnchor.y,imageUvPointer.y));
                    foreach(int i in imageUvVertices)if(box.Contains(UvToGui(imageUvPoints[i],rect)))
                    {
                        if(imageUvMode==ImageUvMode.Shell){foreach(int j in imageUvTriangles)if(imageUvRoots[j]==imageUvRoots[i])imageUvSelection.Add(j);}
                        else if(imageUvMode==ImageUvMode.Face){foreach(var face in imageUvFaces)if(face.Contains(i))imageUvSelectedFaces.Add(face);}
                        else if(imageUvMode==ImageUvMode.Edge){foreach(var edge in imageUvEdges)if(edge.Contains(i))imageUvSelection.UnionWith(edge);}
                        else imageUvSelection.Add(i);
                    }
                }
                if(imageUvMarquee && imageUvMode==ImageUvMode.Face)RebuildImageUvFaceVertices();
                FinishImageUvGesture(false);e.Use();Repaint();
            }
            return true;
        }
        private void DrawImageUvEditing(Rect rect)
        {
            if(imageUvMode!=ImageUvMode.Paint && imageUvTarget && imageUvPoints!=null)
            {
                if(Event.current.type==EventType.Repaint)
                {
                    var selectedColor=new Color(1,.65f,.15f);
                    var hoverColor=new Color(1,.95f,.45f);
                    foreach(int i in imageUvVertices)imageUvGuiPoints[i]=UvToGui(imageUvPoints[i],rect);
                    imageUvSelectedLines.Clear(); imageUvHoverLines.Clear();
                    for(int n=0;n<imageUvEdges.Count;n++)
                    {
                        var edge=imageUvEdges[n]; var a=imageUvGuiPoints[edge[0]]; var b=imageUvGuiPoints[edge[1]];
                        imageUvWireLines[n*2]=a; imageUvWireLines[n*2+1]=b;
                        if(imageUvHover.Contains(edge[0])&&imageUvHover.Contains(edge[1])){imageUvHoverLines.Add(a);imageUvHoverLines.Add(b);}
                        else if(imageUvSelection.Contains(edge[0])&&imageUvSelection.Contains(edge[1])){imageUvSelectedLines.Add(a);imageUvSelectedLines.Add(b);}
                    }
                    var lineColor=Handles.color;
                    try
                    {
                        if(showUvOverlay){Handles.color=uvOverlayColor;Handles.DrawLines(imageUvWireLines);}
                        if(imageUvSelectedLines.Count>0){Handles.color=selectedColor;Handles.DrawLines(imageUvSelectedLines.ToArray());}
                        if(imageUvHoverLines.Count>0){Handles.color=hoverColor;Handles.DrawLines(imageUvHoverLines.ToArray());}
                    }
                    finally{Handles.color=lineColor;}
                    if(imageUvMode==ImageUvMode.Face || imageUvMode==ImageUvMode.Shell)
                    {
                        var previous=Handles.color;
                        try
                        {
                            for(int n=0;n<imageUvTriangles.Length;n+=3)
                            {
                                int a=imageUvTriangles[n],b=imageUvTriangles[n+1],c=imageUvTriangles[n+2];
                                bool hovered=imageUvHover.Contains(a)&&imageUvHover.Contains(b)&&imageUvHover.Contains(c);
                                bool selected=imageUvMode==ImageUvMode.Face ? imageUvSelectedFaces.Contains(imageUvFaces[n/3]) :
                                    imageUvSelection.Contains(a)&&imageUvSelection.Contains(b)&&imageUvSelection.Contains(c);
                                if(!hovered&&!selected)continue;
                                var fill=hovered?hoverColor:selectedColor;fill.a=hovered?.3f:.22f;Handles.color=fill;
                                Handles.DrawAAConvexPolygon(imageUvGuiPoints[a],imageUvGuiPoints[b],imageUvGuiPoints[c]);
                            }
                        }
                        finally{Handles.color=previous;}
                    }
                    imageUvDotPixels.Clear();
                    var visibleCanvas=new Rect(0,0,canvasArea.width,canvasArea.height);
                    foreach(int i in imageUvVertices)
                    {
                        var point=imageUvGuiPoints[i];
                        if(!visibleCanvas.Contains(point))continue;
                        if(imageUvMode==ImageUvMode.UV || imageUvSelection.Contains(i))
                        {
                            bool emphasized=imageUvHover.Contains(i)||imageUvSelection.Contains(i);
                            if(!imageUvDotPixels.Add(new Vector2Int(Mathf.RoundToInt(point.x/4),Mathf.RoundToInt(point.y/4)))&&!emphasized)continue;
                            EditorGUI.DrawRect(new Rect(point.x-3.5f,point.y-3.5f,7,7),new Color(.08f,.04f,.12f,.95f));
                            EditorGUI.DrawRect(new Rect(point.x-2.5f,point.y-2.5f,5,5),imageUvHover.Contains(i)?hoverColor:imageUvSelection.Contains(i)?selectedColor:new Color(.8f,.35f,1));
                        }
                    }
                    if(imageUvSelection.Count>0)
                    {
                        Vector2 c=UvToGui(ImageUvCenter(),rect);
                        if(imageUvTool==ImageUvTool.Rotate)DrawImageGuiCircle(c,45,new Color(1,.65f,.15f));
                        else
                        {
                            DrawImageIconLine(Color.red,2,c,c+new Vector2(55,0));DrawImageIconLine(Color.green,2,c,c+new Vector2(0,-55));
                            if(imageUvTool==ImageUvTool.Move)
                            {
                                DrawImageIconLine(Color.red,2,c+new Vector2(45,-6),c+new Vector2(55,0),c+new Vector2(45,6));
                                DrawImageIconLine(Color.green,2,c+new Vector2(-6,-45),c+new Vector2(0,-55),c+new Vector2(6,-45));
                            }
                            else
                            {
                                EditorGUI.DrawRect(new Rect(c.x+51,c.y-4,8,8),Color.red);EditorGUI.DrawRect(new Rect(c.x-4,c.y-59,8,8),Color.green);
                            }
                        }
                        EditorGUI.DrawRect(new Rect(c.x-4,c.y-4,8,8),Color.yellow);
                    }
                }
                if(imageUvMarquee && Event.current.type==EventType.Repaint)
                {
                    var box=Rect.MinMaxRect(Mathf.Min(imageUvAnchor.x,imageUvPointer.x),Mathf.Min(imageUvAnchor.y,imageUvPointer.y),Mathf.Max(imageUvAnchor.x,imageUvPointer.x),Mathf.Max(imageUvAnchor.y,imageUvPointer.y));
                    EditorGUI.DrawRect(box,new Color(.7f,.3f,1,.12f));
                    DrawImageIconLine(new Color(.85f,.55f,1),1,new Vector3(box.xMin,box.yMin),new Vector3(box.xMax,box.yMin),new Vector3(box.xMax,box.yMax),new Vector3(box.xMin,box.yMax),new Vector3(box.xMin,box.yMin));
                }
                GUI.Label(new Rect(10,6,430,22),$"{imageUvMode} • {imageUvTool} • W/E/R • Shift: add • RMB: modes",EditorStyles.helpBox);
            }
            if(!imageUvWheel || Event.current.type!=EventType.Repaint)return;
            if(imageUvWheelLabel==null)imageUvWheelLabel=new GUIStyle(StudioTheme.Styles.label){alignment=TextAnchor.MiddleCenter,fontSize=11};
            var palette=StudioTheme.Current;
            var delta=imageUvWheelPointer-imageUvWheelStart;
            if(delta.sqrMagnitude>=1)
            {
                var matrix=GUI.matrix;
                try
                {
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,imageUvWheelStart);
                    EditorGUI.DrawRect(new Rect(imageUvWheelStart.x,imageUvWheelStart.y-2,delta.magnitude,4),new Color(.08f,.08f,.08f,.9f));
                    EditorGUI.DrawRect(new Rect(imageUvWheelStart.x,imageUvWheelStart.y-1,delta.magnitude,2),palette.Text);
                }
                finally{GUI.matrix=matrix;}
                EditorGUI.DrawRect(new Rect(imageUvWheelStart.x-2,imageUvWheelStart.y-2,4,4),palette.Text);
            }
            string[] labels={"Paint","UV","Edge","Face","Shell"};
            for(int i=0;i<labels.Length;i++)
            {
                var box=ImageUvWheelRect(i);
                EditorGUI.DrawRect(new Rect(box.x-1,box.y-1,box.width+2,box.height+2),palette.Header);
                EditorGUI.DrawRect(box,imageUvWheelHover==i?palette.Hover:palette.Button);
                imageUvWheelLabel.normal.textColor=palette.Text;
                GUI.Label(box,labels[i],imageUvWheelLabel);
            }
        }
        private void DrawImageUvToolbar(Rect bar)
        {
            if(bar.width<360)return;
            if(GUI.Button(new Rect(bar.x+118,bar.y+2,90,22),imageUvMode==ImageUvMode.Paint?"Paint ▾":imageUvMode+" ▾"))
            {
                var menu=new StudioMenu();foreach(ImageUvMode mode in Enum.GetValues(typeof(ImageUvMode))){var value=mode;menu.AddItem(new GUIContent(mode.ToString()),imageUvMode==mode,()=>SetImageUvMode(value));}menu.DropDown(new Rect(bar.x+118,bar.y+2,90,22));
            }
            if(imageUvMode!=ImageUvMode.Paint && bar.width>450 && GUI.Button(new Rect(bar.x+213,bar.y+2,90,22),"Save UV Mesh"))SaveImageUvMesh();
        }
        private void SaveImageUvMesh()
        {
            if(!EnsureImageUvTarget())return;
            FinishImageUvGesture(false);
            SaveImageUvMeshCopy(imageUvMeshes.Find(item=>item.edited==imageUvTarget));
        }
        private bool SaveImageUvMeshCopy(ImageUvMesh edit)
        {
            if(edit==null || !edit.edited)return true;
            string path=EditorUtility.SaveFilePanelInProject("Save mesh with edited UVs",edit.edited.name.Replace(" • UV edits","_UV"),"asset","Save a mesh copy. The source FBX stays unchanged.");
            if(string.IsNullOrEmpty(path))return false;
            var copy=Instantiate(edit.edited);copy.hideFlags=HideFlags.None;
            AssetDatabase.CreateAsset(copy,path);AssetDatabase.SaveAssetIfDirty(copy);EditorGUIUtility.PingObject(copy);
            edit.dirty=false;
            status="Saved UV mesh: "+path;statusType=MessageType.Info;
            return true;
        }
        private bool SaveAllImageUvMeshes()
        {
            FinishImageUvGesture(false);
            foreach(var edit in imageUvMeshes)if(edit.dirty && !SaveImageUvMeshCopy(edit))return false;
            return true;
        }
    }
}
