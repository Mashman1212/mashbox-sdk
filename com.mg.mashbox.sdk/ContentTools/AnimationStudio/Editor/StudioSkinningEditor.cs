using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor : IDisposable
    {
        internal enum Brush { Select, Paint, SmoothWeights, Inflate, Bulge, Grab, SmoothShape, Pose, Object, FitBody }
        internal readonly StudioSkinningDocument Document;
        internal readonly Scene Scene;
        internal Brush Tool;
        private readonly Action repaint;
        private GameObject skeleton;
        private Transform[] bones;
        private Matrix4x4[] rest;
        private Quaternion[] restRotations;
        private bool[] locked;
        private bool[] deformBones;
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<Mesh> displayMeshes = new List<Mesh>();
        private Material shaded, heat, sculptContext, referenceShaded;
        private static readonly Color ReferencePreviewTint = new Color(.42f,.44f,.46f,1);
        private MeshCollider picker;
        private Mesh pickMesh;
        private Vector2 listScroll, boneScroll, inspectorScroll;
        private int boneIndex;
        private int hoveredInfluence = -1, influenceHoverCandidate = -1;
        private Rect influenceHoverScreenRect;
        private void SetInfluencePreview(int index)
        {
            if (hoveredInfluence == index) return;
            hoveredInfluence = index;
            if(index>=0 && HasEditableMesh && Part.bound)RecordSkinTutorialAction("hover");
            UpdateDisplay();
        }
        private string search = "", error;
        private float radius = .12f, strength = .2f, targetWeight = 1;
        private bool heatmap = true, frontOnly = true, smoothWhileSculpting;
        private StudioSkinWeights weights;
        private Vector3[] vertices, deformed, normals, initialPoints, initialVertices;
        private Matrix4x4[] inverses;
        private int[][] neighbours;
        private int[][] sculptSeams;
        private int[] sculptRepresentatives;
        private readonly HashSet<int> sculptVisited = new HashSet<int>();
        private readonly Dictionary<Mesh, int[][]> topology = new Dictionary<Mesh, int[][]>();
        private StudioSkinSpatialIndex spatial;
        private readonly List<int> candidates = new List<int>();
        private Vector3 hitPoint, hitNormal, grabStart;
        private Plane grabPlane;
        private bool stroke, geometryChanged, weightsChanged;
        private int undoGroup, brushControl;
        private Vector2 lastMouse;
        private int cachedPart = -1;
        private SceneView lastView;
        private Matrix4x4[] skinMatrices;
        internal bool HasStroke => stroke;
        private StudioSkinningDocument.Part Part => Document.selected >= 0 && Document.selected < Document.parts.Count ? Document.parts[Document.selected] : null;

        internal StudioSkinningEditor(StudioSkinningDocument document, Action repaint)
        {
            Document = document; this.repaint = repaint;
            Scene = EditorSceneManager.NewPreviewScene();
            try
            {
                skeleton = Object.Instantiate(document.skeleton); skeleton.name = "SDK Skeleton";
                skeleton.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(skeleton, Scene);
                skeleton.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); skeleton.transform.localScale = Vector3.one;
                foreach (var animator in skeleton.GetComponentsInChildren<Animator>()) animator.enabled = false;
                foreach (var renderer in skeleton.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                bones = skeleton.GetComponentsInChildren<Transform>(true);
                var paths = bones.Select(t => AnimationUtility.CalculateTransformPath(t, skeleton.transform)).ToArray();
                if (document.bonePaths != null && document.bonePaths.Length > 0 && !document.bonePaths.SequenceEqual(paths))
                    throw new InvalidOperationException("The SDK skeleton hierarchy changed. This workspace has been preserved; restore its original skeleton before editing.");
                document.bonePaths = paths;
                // Humanoid import can centre the FBX hips on Z=0 even though the
                // avatar retains the authored bind-pose offset. Establish that anchor
                // before caching rest matrices or importing any skin references.
                var avatar = document.skeleton.GetComponent<Animator>()?.avatar;
                if (avatar && avatar.isValid && avatar.isHuman)
                {
                    var description = avatar.humanDescription;
                    var mapping = description.human.FirstOrDefault(b => b.humanName == "Hips");
                    var anchors = bones.Where(b => b.name == mapping.boneName).ToArray();
                    var authored = description.skeleton.Where(b => b.name == mapping.boneName).ToArray();
                    if (anchors.Length == 1 && authored.Length == 1) anchors[0].localPosition = authored[0].position;
                }
                rest = bones.Select(b => b.localToWorldMatrix).ToArray(); restRotations = bones.Select(b => b.localRotation).ToArray();
                locked = new bool[bones.Length];
                // SDK animation targets are siblings of Hips, not skin influences.
                var hips = bones.FirstOrDefault(b => StudioSkinningDocument.BoneName(b.name) == "Hips");
                deformBones = bones.Select(b => hips ? b == hips || b.IsChildOf(hips) : b != skeleton.transform).ToArray();
                boneIndex = Array.FindIndex(deformBones, b => b); if (boneIndex < 0) boneIndex = 0;
                shaded = new Material(Shader.Find("MashBox/Studio/Preview")) { hideFlags = HideFlags.HideAndDontSave };
                shaded.SetColor("_Color", new Color(.7f,.74f,.78f)); shaded.SetFloat("_Cull", 0);
                referenceShaded = new Material(shaded) { name = "Default reference preview", hideFlags = HideFlags.HideAndDontSave };
                referenceShaded.SetColor("_Color", ReferencePreviewTint);
                sculptContext = new Material(shaded) { name = "Sculpt context preview", hideFlags = HideFlags.HideAndDontSave };
                sculptContext.SetColor("_Color", new Color(.48f,.31f,.53f));
                heat = new Material(Shader.Find("Hidden/MashBox/SkinWeightPreview")) { hideFlags = HideFlags.HideAndDontSave };
                var pickObject = new GameObject("Brush surface") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(pickObject, Scene); picker = pickObject.AddComponent<MeshCollider>();
                pickMesh = new Mesh { name = "Skin brush surface", hideFlags = HideFlags.HideAndDontSave };
                Rebuild(); Undo.undoRedoPerformed += OnUndo;
                InitializePreviewAnimation();
            }
            catch { Dispose(); throw; }
        }
        internal void Run(Action action)
        { try { action(); error = null; } catch (Exception e) { error = e.Message; Debug.LogException(e); } repaint(); }
        private void Changed()
        { Document.dirty = true; EditorUtility.SetDirty(Document); if (Part?.mesh) EditorUtility.SetDirty(Part.mesh); repaint(); }
        private void OnUndo()
        { objectUndoGroup = -1; poseUndoGroup = -1; CancelBrushInteraction(); stroke = false; GUIUtility.hotControl = 0; Run(Rebuild); }
        private static void ApplyPreviewWeights(Mesh source,Mesh preview)
        {
            var previewWeights=new StudioSkinWeights(source);
            previewWeights.LimitToFour();previewWeights.Apply(preview);
        }
        private void Rebuild()
        {
            foreach (var go in objects) if (go) Object.DestroyImmediate(go); objects.Clear();
            foreach (var mesh in displayMeshes) if (mesh) Object.DestroyImmediate(mesh); displayMeshes.Clear();
            foreach (var part in Document.parts)
            {
                part.referenceOnly |= IsDefaultReference(part);
                if (part.referenceOnly) AssignDefaultMeshRole(part);
                if (part.referenceOnly) AlignReferenceBindPose(part);
                var go = new GameObject(part.name) { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(go, Scene);
                objects.Add(go); var mesh = Object.Instantiate(part.mesh); mesh.hideFlags = HideFlags.HideAndDontSave; displayMeshes.Add(mesh);
                Renderer renderer;
                if (part.bound)
                {
                    ApplyPreviewWeights(part.mesh,mesh);
                    var skin = go.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.bones = bones;
                    skin.quality = SkinQuality.Bone4;
                    skin.rootBone = skeleton.transform; skin.updateWhenOffscreen = true; skin.localBounds = new Bounds(Vector3.up, Vector3.one * 20); renderer = skin;
                }
                else { go.AddComponent<MeshFilter>().sharedMesh = mesh; renderer = go.AddComponent<MeshRenderer>(); }
                renderer.sharedMaterials = Enumerable.Repeat(part.referenceOnly ? referenceShaded : shaded, mesh.subMeshCount).ToArray(); go.SetActive(part.visible);
            }
            if (Document.selected < 0 || Document.selected >= Document.parts.Count || !CanPaintPart(Document.parts[Document.selected]))
                Document.selected = Document.parts.FindIndex(part => CanPaintPart(part));
            cachedPart = -1; RefreshSurface();
        }
        internal void Import(GameObject source, bool referenceOnly = false, bool visible = true)
        {
            EndStroke(); int first = Document.parts.Count;
            Document.Import(source, bones, rest);
            for (int i=first;i<Document.parts.Count;i++)
            {
                Document.parts[i].referenceOnly = referenceOnly || IsDefaultReference(Document.parts[i]);
                Document.parts[i].visible = visible;
                AssignImportedMeshRole(Document.parts[i]);
            }
            Rebuild();
            if (Document.selected >= first) ChooseImportedMeshTool();
            Changed();
        }
        private void ChooseImportedMeshTool()
        {
            if (Part == null || Part.referenceOnly) return;
            CancelBrushInteraction();
            if (Part.bound)
            {
                sculptMode = false;
                heatmap = true;
                ChooseBrush(Brush.Paint);
                UpdateDisplay();
            }
            else ChooseBrush(Brush.Object);
        }
        private bool IsReferencePart => Part != null && Part.referenceOnly;
        private bool CanPaintPart(StudioSkinningDocument.Part part)
        {
            if (part == null) return false;
            if (!part.referenceOnly) return true;
#if MashBoxDev
            return paintableReferences.Contains(part);
#else
            return false;
#endif
        }
        private bool IsBrushLocked => IsReferencePart &&
            (!CanPaintPart(Part) || (Tool != Brush.Paint && Tool != Brush.SmoothWeights && Tool != Brush.Select));
#if MashBoxDev
        private readonly HashSet<StudioSkinningDocument.Part> paintableReferences = new HashSet<StudioSkinningDocument.Part>();
        private void ToggleReferencePainting(StudioSkinningDocument.Part part)
        {
            EndStroke(); CancelBrushInteraction();
            if (!paintableReferences.Remove(part))
            {
                paintableReferences.Add(part);
                Document.selected = Document.parts.IndexOf(part);
                sameShading = false;
                heatmap = true;
                sculptMode = false;
                ChooseBrush(Brush.Paint);
            }
            RefreshSurface(); repaint();
        }
#endif

        private void AlignReferenceBindPose(StudioSkinningDocument.Part part)
        {
            // Stitching rebases the bust's vertices and weights into the body's
            // bind space. Restoring its source binds afterwards tears that seam.
            if (part.HasStitchWeightLock) return;
            if (!part.bound || !part.sourceMesh) return;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(part.sourceMesh));
            if (!source) return;
            var candidates = source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh == part.sourceMesh).ToArray();
            var renderer = candidates.FirstOrDefault(r => AnimationUtility.CalculateTransformPath(r.transform,source.transform) == part.sourceRendererPath);
            if (!renderer && candidates.Length == 1) renderer = candidates[0];
            if (!renderer) throw new InvalidOperationException("Cannot locate the original renderer for locked reference " + part.name);
            var originalBinds = part.sourceMesh.bindposes;
            var sourceBones = renderer.bones;
            var binds = rest.Select(r => r.inverse).ToArray();
            for (int i=0;i<sourceBones.Length && i<originalBinds.Length;i++)
            {
                if (!sourceBones[i]) continue;
                var matches = Enumerable.Range(0,bones.Length).Where(b => StudioSkinningDocument.BoneName(bones[b].name) == StudioSkinningDocument.BoneName(sourceBones[i].name)).ToArray();
                if (matches.Length != 1) continue; // Unused extra bones were already checked during import.
                // Animation Studio keeps the authored bind pose and remaps only
                // the bones. Skinning has baked the renderer transform into the
                // vertices, so cancel that transform here, without a source-pose correction.
                binds[matches[0]] = originalBinds[i] * part.sourceToWorkspace.inverse;
            }
            part.mesh.bindposes = binds;
        }
        private static bool IsDefaultReference(StudioSkinningDocument.Part part)
        {
            if (!part.sourceMesh) return false;
            var defaults = StudioDefaultReferences.LoadSkinning("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if (!defaults) return false;
            foreach (var source in new[] { defaults.body, defaults.bust })
            {
                if (!source) continue;
                if (source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.sharedMesh == part.sourceMesh) ||
                    source.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh == part.sourceMesh)) return true;
            }
            return false;
        }
        internal void Frame(SceneView view)
        {
            var bounds = new Bounds(Vector3.up, Vector3.one * 2);
            foreach (var part in Document.parts) if (part.mesh) bounds.Encapsulate(part.mesh.bounds);
            view.Frame(bounds, false);
        }
        internal void DrawHierarchy()
        {
            if (Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform)
                HandleModelDrop(Event.current);
            GUILayout.Label("Drop clothing or objects into the viewport.", StudioTheme.Styles.wordWrappedMiniLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            for (int i = 0; i < Document.parts.Count; i++)
            {
                var part = Document.parts[i];
                if (i > 0 && Document.parts[i-1].referenceOnly != part.referenceOnly)
                {
                    GUILayout.Space(6);
                    var separator = GUILayoutUtility.GetRect(0,1,GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(separator,new Color(.28f,.30f,.31f,1));
                    GUILayout.Space(6);
                }
                DrawMeshListRow(part,i);
                DrawMaterialSlots(part);
            }
            EditorGUILayout.EndScrollView();
        }
        private void DrawMeshListRow(StudioSkinningDocument.Part part,int i)
        {
                using (new GUILayout.HorizontalScope())
                {
                    bool next = GUILayout.Toggle(part.visible, GUIContent.none, GUILayout.Width(18));
                    if (next != part.visible) { EndStroke(); Undo.RecordObject(Document,"Show skinning mesh"); part.visible = next; objects[i].SetActive(next); Changed(); RecordSkinTutorialAction("visibility"); }
                    if (part.referenceOnly)
                    {
                        var lockedRow = GUILayoutUtility.GetRect(new GUIContent(part.name), GUI.skin.button, GUILayout.ExpandWidth(true));
                        DrawMeshRoleRow(lockedRow,part);
#if MashBoxDev
                        var paintRect = new Rect(lockedRow.xMax-20,lockedRow.center.y-8,16,16);
                        bool enabled = CanPaintPart(part);
                        var paintIcon = EditorGUIUtility.IconContent(enabled ? "LockIcon-Off" : "LockIcon-On").image;
                        if (GUI.Button(paintRect,new GUIContent(paintIcon,enabled ? "Disable weight painting on this default part" : "Enable weight painting on this default part (MashBoxDev)"),GUIStyle.none))
                            ToggleReferencePainting(part);
                        if (((Event.current.type == EventType.MouseDown && Event.current.button == 1) || Event.current.type == EventType.ContextClick) && lockedRow.Contains(Event.current.mousePosition))
                        {
                            var menu = new GenericMenu();
                            menu.AddItem(new GUIContent("Enable Weight Painting"),enabled,()=>Run(()=>ToggleReferencePainting(part)));
                            if(part.role == StudioSkinningDocument.MeshRole.Bust)
                                menu.AddItem(new GUIContent("Stitch Bust"),false,()=>Run(()=>StitchBustParts(new[]{part})));
                            menu.AddItem(new GUIContent("Save FBX"),false,()=>Run(()=>SaveReferenceFbx(part,false)));
                            menu.AddItem(new GUIContent("Save FBX As…"),false,()=>Run(()=>SaveReferenceFbx(part,true)));
                            menu.ShowAsContext(); Event.current.Use();
                        }
                        if(enabled && Event.current.type==EventType.MouseDown && Event.current.button==0 && lockedRow.Contains(Event.current.mousePosition))
                        { EndStroke(); Document.selected=i; sculptMode=false; ChooseBrush(Brush.Paint); RefreshSurface(); Event.current.Use(); }
                        if(enabled && Document.selected==i)
                            EditorGUI.DrawRect(new Rect(lockedRow.x,lockedRow.yMax-2,lockedRow.width,2),new Color(.5f,1,.87f));
#endif
                        return;
                    }
                    // Reserve the row and process RMB before the toggle can
                    // consume it. Ordinary selection remains a left-click action.
                    var row = GUILayoutUtility.GetRect(new GUIContent(part.name), GUI.skin.button, GUILayout.ExpandWidth(true));
                    if(row.Contains(Event.current.mousePosition)&&StudioMeshMaterialPreview.IsMaterialDrag)StudioMeshMaterialPreview.Drop(m=>SetMaterial(part,0,m));
                    if (DrawMeshRename(row,part,i)) return;
                    if (((Event.current.type == EventType.MouseDown && Event.current.button == 1) || Event.current.type == EventType.ContextClick) && row.Contains(Event.current.mousePosition))
                    {
                        EndStroke(); Document.selected = i; RefreshSurface();
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("Rename"), false, () => Run(() => BeginMeshRename(part)));
                        menu.AddSeparator("");
                        if (!part.bound) menu.AddItem(new GUIContent("Skin"), false, () => Run(SkinSelected));
                        else menu.AddDisabledItem(new GUIContent("Skin"));
                        if (part.bound) menu.AddItem(new GUIContent("Unskin"), false, () => Run(UnskinSelected));
                        else menu.AddDisabledItem(new GUIContent("Unskin"));
                        menu.AddSeparator("");
                        menu.AddItem(new GUIContent("Mark as Bust"), part.role == StudioSkinningDocument.MeshRole.Bust,
                            () => Run(() => SetMeshRole(part, StudioSkinningDocument.MeshRole.Bust)));
                        menu.AddItem(new GUIContent("Mark as Clothing"), part.role == StudioSkinningDocument.MeshRole.Clothing,
                            () => Run(() => SetMeshRole(part, StudioSkinningDocument.MeshRole.Clothing)));
                        menu.AddItem(new GUIContent("Mark as Body"), part.role == StudioSkinningDocument.MeshRole.Body,
                            () => Run(() => SetMeshRole(part, StudioSkinningDocument.MeshRole.Body)));
                        if (CanStitchSelectedBust) menu.AddItem(new GUIContent("Stitch Bust"), false, () => Run(StitchBust));
                        else menu.AddDisabledItem(new GUIContent("Stitch Bust"));
                        if (CanMatchDefaultBody) menu.AddItem(new GUIContent("Match Default Body"), false, () => Run(MatchDefaultBody));
                        else menu.AddDisabledItem(new GUIContent("Match Default Body"));
                        menu.AddItem(new GUIContent("Send to Retopology"), false, () => Run(SendSelectedToRetopology));
                        menu.AddItem(new GUIContent("Save FBX"), false, () => Run(SaveSelectedFbx));
                        menu.AddItem(new GUIContent("Save FBX As…"), false, () => Run(SaveSelectedFbxAs));
                        menu.AddSeparator("");
                        menu.AddItem(new GUIContent("Remove Mesh", "Remove from this workspace only. The source asset is unchanged. Supports Undo."), false, () => Run(() => RemoveMesh(part)));
                        menu.ShowAsContext(); Event.current.Use();
                    }
                    if (GUI.Toggle(row,Document.selected == i,GUIContent.none,"Button") && Document.selected != i)
                    { EndStroke(); Document.selected = i; RefreshSurface(); }
                    DrawMeshRoleRow(row,part);
                }
        }
        internal void RemoveSelectedMesh() => RemoveMesh(Part);
        private void RemoveMesh(StudioSkinningDocument.Part part)
        {
            if(part==null||part.referenceOnly||IsDefaultReference(part))return;
            int index=Document.parts.IndexOf(part);if(index<0)return;
            EndStroke();FinishMeshRename(false);CancelBrushInteraction();
            Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Remove skinning mesh");
            // Keep the editable mesh alive so Undo can restore all unsaved edits.
            Document.parts.RemoveAt(index);
            if(Document.selected==index)Document.selected=-1;
            else if(Document.selected>index)Document.selected--;
            Rebuild();Changed();
        }
        internal void DrawInfluences()
        {
            search = EditorGUILayout.TextField(search, StudioTheme.Styles.toolbarSearchField);
            boneScroll = EditorGUILayout.BeginScrollView(boneScroll);
            if (Event.current.type == EventType.Repaint) influenceHoverCandidate = -1;
            DrawInfluenceTree();
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
            {
                // Ignore rows clipped above or below the scroll viewport.
                if (!GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) influenceHoverCandidate = -1;
                SetInfluencePreview(influenceHoverCandidate);
            }
        }
        internal void DrawInspector() => DrawInspector(true);
        private void DrawInspector(bool scroll)
        {
            float width = Mathf.Max(160, (lastView is MashBoxStudioViewport panelView ? panelView.SkinInspectorWidth : 300) - 22);
            float oldLabelWidth = EditorGUIUtility.labelWidth, oldFieldWidth = EditorGUIUtility.fieldWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(126, width * .46f); EditorGUIUtility.fieldWidth = 42;
            if(scroll)inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll, GUILayout.MinWidth(0));
            EditorGUILayout.BeginVertical(GUILayout.Width(width), GUILayout.MaxWidth(width));
            GUILayout.Label(sculptMode ? "SCULPT" : "SKIN WEIGHT PAINT", StudioTheme.Styles.boldLabel);
            var choices = ModeBrushes;
            int chosen = GUILayout.SelectionGrid(Array.IndexOf(choices, Tool), choices.Select(BrushLabel).ToArray(), 2, GUILayout.Width(width));
            var nextTool = chosen >= 0 ? choices[chosen] : Tool;
            if (nextTool != Tool) ChooseBrush(nextTool);
            radius = EditorGUILayout.Slider("Radius", radius, .005f, .75f);
            strength = EditorGUILayout.Slider("Strength", strength, .001f, 1);
            if (Tool == Brush.Grab)
            {
                int falloff = EditorGUILayout.Popup(new GUIContent("Grab falloff", "Volume pulls through the mesh. Surface follows connected edges."), Document.grabVolume ? 0 : 1, new[] { "Volume", "Surface" });
                if ((falloff == 0) != Document.grabVolume)
                { EndStroke(); Undo.RecordObject(Document,"Grab falloff"); Document.grabVolume=falloff==0; Changed(); }
            }
            if (Tool == Brush.FitBody)
            {
                fitBodyGap = EditorGUILayout.Slider(new GUIContent("Body gap", "Space between the body surface and clothing, in metres."), fitBodyGap, 0, .05f);
                fitBodyReach = EditorGUILayout.Slider(new GUIContent("Fit distance", "Only fit vertices within this distance of the body surface, in metres."), fitBodyReach, .01f, .5f);
                EditorGUILayout.HelpBox("Brush clothing toward the posed body. Shift smooths. Skin weights are preserved.", MessageType.Info);
            }
            if (Tool == Brush.Paint) targetWeight = EditorGUILayout.Slider("Target weight", targetWeight, 0, 1);
            using (new EditorGUI.DisabledScope(Tool == Brush.FitBody || (Tool == Brush.Grab && Document.grabVolume)))
                frontOnly = EditorGUILayout.Toggle(new GUIContent("Front-facing only", "Fit to Body and Volume Grab reach both sides of the mesh regardless of this filter."), frontOnly);
            bool showHeat = EditorGUILayout.Toggle("Show weights", heatmap);
            if (showHeat != heatmap) { heatmap = showHeat; UpdateDisplay(); }
            smoothWhileSculpting = EditorGUILayout.Toggle(new GUIContent("Sculpt + smooth weights", "Optionally smooth skin weights under sculpt strokes. Off preserves all skin weights."), smoothWhileSculpting);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.EndVertical();
            if(scroll)EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = oldLabelWidth; EditorGUIUtility.fieldWidth = oldFieldWidth;
        }
        private void AutoBind() => BindWithJointCleanup();
        private void RefreshSurface()
        {
            wirePoseDirty = true;
            if (!stroke) fitBodySurface = null;
            if (!CanPaintPart(Part))
            {
                picker.sharedMesh = null; weights = null; cachedPart = -1;
                UpdateDisplay(); return;
            }
            cachedPart = Document.selected; vertices = Part.mesh.vertices; weights = new StudioSkinWeights(Part.mesh);
            // Restore the saved seam values instead of feeding Unity's quantized
            // upload values back into each subsequent stroke.
            Part.RestoreStitchWeights(weights);
            deformed = new Vector3[vertices.Length]; inverses = new Matrix4x4[vertices.Length];
            var binds = Part.mesh.bindposes;
            var matrices = skinMatrices = Part.bound ? bones.Select((b,i) => b.localToWorldMatrix * binds[i]).ToArray() : null;
            for (int i = 0; i < vertices.Length; i++)
            {
                var matrix = Part.bound ? weights.PreviewSkinMatrix(i, matrices) : Matrix4x4.identity;
                if (Mathf.Abs(matrix.determinant) < 1e-8f) throw new InvalidOperationException("A posed skin transform is singular. Reset the preview pose before sculpting.");
                deformed[i] = matrix.MultiplyPoint3x4(vertices[i]); inverses[i] = matrix.inverse;
            }
            var triangles = Part.mesh.triangles;
            picker.sharedMesh = null; pickMesh.Clear(); pickMesh.indexFormat = Part.mesh.indexFormat; pickMesh.vertices = deformed; pickMesh.triangles = triangles; pickMesh.RecalculateNormals();
            normals = pickMesh.normals; picker.sharedMesh = pickMesh;
            if (!topology.TryGetValue(Part.mesh, out neighbours))
            {
                var sets = new HashSet<int>[vertices.Length]; for (int i = 0; i < sets.Length; i++) sets[i] = new HashSet<int>();
                for (int i = 0; i < triangles.Length; i += 3)
                { int a = triangles[i], b = triangles[i+1], c = triangles[i+2]; sets[a].Add(b); sets[a].Add(c); sets[b].Add(a); sets[b].Add(c); sets[c].Add(a); sets[c].Add(b); }
                neighbours = sets.Select(s => s.ToArray()).ToArray(); topology[Part.mesh] = neighbours;
            }
            BuildSculptSeams();
            UpdateDisplay();
        }
        // Render vertices are split at UV/material/normal seams. Keep their streams
        // intact, but treat coincident rest positions as one sculpt point.
        private void BuildSculptSeams()
        {
            var groups = new Dictionary<Vector3, List<int>>();
            for (int v = 0; v < vertices.Length; v++)
            {
                if (!groups.TryGetValue(vertices[v], out var group)) groups.Add(vertices[v], group = new List<int>());
                group.Add(v);
            }
            sculptSeams = new int[vertices.Length][]; sculptRepresentatives = new int[vertices.Length];
            // Never change the cached render topology used by other meshes/strokes.
            neighbours = (int[][])neighbours.Clone();
            foreach (var group in groups.Values)
            {
                var members = group.ToArray(); var adjacent = new HashSet<int>(); Vector3 normal = Vector3.zero;
                foreach (int v in members) { normal += normals[v]; foreach (int n in neighbours[v]) adjacent.Add(n); }
                adjacent.ExceptWith(members);
                var joined = adjacent.ToArray(); normal.Normalize();
                foreach (int v in members)
                {
                    sculptSeams[v] = members; sculptRepresentatives[v] = members[0];
                    neighbours[v] = joined;
                    if (normal.sqrMagnitude > 0) normals[v] = normal;
                }
            }
        }
        private void UpdateDisplay()
        {
            wirePoseDirty = true;
            if (referenceShaded) referenceShaded.SetColor("_Color",ReferencePreviewTint);
            for (int i = 0; i < objects.Count; i++)
            {
                if (sameShading)
                {
                    objects[i].GetComponent<Renderer>().sharedMaterials = Enumerable.Repeat(referenceShaded, displayMeshes[i].subMeshCount).ToArray();
                    continue;
                }
                bool checker = hoveredInfluence < 0 && showUVChecker && i == Document.selected && !Document.parts[i].referenceOnly;
                if(checker) EnsureUVChecker();
                bool textured = hoveredInfluence < 0 && showTextured && i == Document.selected && !Document.parts[i].referenceOnly;
                bool show = !checker && !textured && i == Document.selected && CanPaintPart(Document.parts[i]) && (heatmap || hoveredInfluence >= 0) && Document.parts[i].bound;
                // Body and bust are one neutral reference surface in every mode.
                // Only editable clothing participates in selection/context colouring.
                var previewMaterial = Document.parts[i].referenceOnly && !show ? referenceShaded
                    : checker ? uvCheckerMaterial : show ? heat : sculptMode && i != Document.selected ? sculptContext : shaded;
                objects[i].GetComponent<Renderer>().sharedMaterials = textured ? materialPreview.Materials(Document.parts[i].materials,displayMeshes[i].subMeshCount,shaded) : Enumerable.Repeat(previewMaterial, displayMeshes[i].subMeshCount).ToArray();
                if (!show || weights == null) continue;
                var colors = new Color[weights.Values.Length];
                for (int v = 0; v < colors.Length; v++) colors[v] = Color.Lerp(new Color(.03f,.08f,.35f), new Color(1,.25f,.03f), weights.Weight(v,hoveredInfluence >= 0 ? hoveredInfluence : boneIndex));
                displayMeshes[i].colors = colors;
            }
            repaint();
        }
        private float brushCursorDepth;
        internal void SceneGUI(SceneView view)
        {
            ObserveSkinTutorial(view);
            lastView = view;
            view.wantsMouseMove = true;
            if (Event.current.type == EventType.MouseMove) repaint();
            if (hoveredInfluence >= 0 && (Event.current.type == EventType.MouseLeaveWindow ||
                !influenceHoverScreenRect.Contains(GUIUtility.GUIToScreenPoint(Event.current.mousePosition))))
                SetInfluencePreview(-1);
            DrawBrushOverlay(view);
            DrawSelectedMeshWireframe();
            if (adjustingBrush)
            {
                bool valid=Part!=null&&Part.visible&&!IsBrushLocked&&
                    ((Tool!=Brush.Paint&&Tool!=Brush.SmoothWeights)||Part.bound);
                RaycastHit adjustmentHit=default;
                valid=valid&&picker.Raycast(HandleUtility.GUIPointToWorldRay(adjustmentAnchor),out adjustmentHit,10000);
                StudioBrushCursor.Draw(view,adjustmentAnchor,radius,valid,adjustmentHit.point,adjustmentHit.normal,new Color(1,.7f,.2f,.85f),ref brushCursorDepth);
            }
            if (brushMenuOpen || adjustingBrush) return;
            var evt = Event.current;
            if (previewPlaying)
            {
                if (evt.type == EventType.MouseDown && evt.button == 0) PausePreviewAnimation();
                else return;
            }
            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DropMaterialOnViewport(evt,view);
                if(evt.type==EventType.Used)return;
                HandleModelDrop(evt);
                if (evt.type == EventType.Used) return;
            }
            if (evt.type == EventType.KeyDown && (evt.control || evt.command) && evt.keyCode == KeyCode.S)
            { Run(() => { EndStroke(); Document.SaveDocument(); }); evt.Use(); }
            if (Tool == Brush.Object) { DrawObjectTransform(view); return; }
            Handles.color = new Color(.3f,.9f,.75f,.7f);
            for (int i = 1; i < bones.Length; i++) if (deformBones[i] && bones[i].parent && Array.IndexOf(bones,bones[i].parent) is int parent && parent >= 0 && deformBones[parent])
                Handles.DrawLine(bones[i].parent.position, bones[i].position);
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(bones[boneIndex].position, view.camera.transform.forward, HandleUtility.GetHandleSize(bones[boneIndex].position)*.045f);
            if (Tool == Brush.Pose)
            {
                DrawManualPose(view);
                return;
            }
            if(Tool!=Brush.Select && (Part==null||!Part.visible||IsBrushLocked||((Tool==Brush.Paint||Tool==Brush.SmoothWeights)&&!Part.bound)))
            {
                StudioBrushCursor.Draw(view,evt.mousePosition,radius,false,default,default,Color.white,ref brushCursorDepth);
                return;
            }
            if (Part == null || !Part.visible || IsBrushLocked) return;
            if (cachedPart != Document.selected) Run(RefreshSurface);
            if (Tool == Brush.Select) return;
            if ((Tool == Brush.Paint || Tool == Brush.SmoothWeights) && !Part.bound) return;
            int id = GUIUtility.GetControlID("MashBoxSkinBrush".GetHashCode(), FocusType.Passive);
            var viewport = view as MashBoxStudioViewport;
            bool overUI = DisplayToolbarRect(view).Contains(evt.mousePosition) || ModeRect(view).Contains(evt.mousePosition) || OverSymmetry(view,evt.mousePosition) || (viewport && viewport.IsOverSkinningPanels(evt.mousePosition));
            if (evt.type == EventType.Layout && !evt.alt && !overUI) HandleUtility.AddDefaultControl(id);
            var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            bool hit = picker.Raycast(ray, out var info, 10000);
            if (Tool == Brush.FitBody)
            {
                if (fitBodySurface == null) PrepareFitBody();
                // Exposed body is a valid brush anchor when the sleeve is inside it.
                if (fitBodySurface != null && fitBodySurface.Raycast(ray, out var bodyHit) && (!hit || bodyHit.distance < info.distance))
                { info = bodyHit; hit = true; }
            }
            if(!overUI)StudioBrushCursor.Draw(view,evt.mousePosition,radius,hit,info.point,info.normal,new Color(1,.7f,.2f,.85f),ref brushCursorDepth);
            if (hit && !overUI)
            {
                DrawMirroredBrush(info.point,info.normal);
                if (evt.type == EventType.MouseMove) view.Repaint();
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && !overUI && hit && GUIUtility.hotControl == 0)
            {
                if (Tool == Brush.FitBody && !PrepareFitBody()) { evt.Use(); return; }
                brushControl = id; GUIUtility.hotControl = id; stroke = true; geometryChanged = weightsChanged = false; tutorialStrokeActions.Clear();
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.RegisterCompleteObjectUndo(new Object[] { Document, Part.mesh }, "Skinning " + Tool);
                hitPoint = info.point; hitNormal = info.normal; initialPoints = (Vector3[])deformed.Clone(); initialVertices = (Vector3[])vertices.Clone();
                spatial = new StudioSkinSpatialIndex(initialPoints, radius);
                PrepareGrabFalloff(hitPoint);
                grabPlane = new Plane(view.camera.transform.forward,hitPoint); grabPlane.Raycast(ray,out float distance); grabStart = ray.GetPoint(distance);
                lastMouse = evt.mousePosition; ApplyBrush(hitPoint,Vector3.zero,evt); evt.Use();
            }
            else if (stroke && GUIUtility.hotControl == brushControl && evt.type == EventType.MouseDrag)
            {
                if ((evt.mousePosition-lastMouse).sqrMagnitude >= 4)
                {
                    if (Tool == Brush.Grab) { if (grabPlane.Raycast(ray,out float distance)) ApplyBrush(hitPoint,ray.GetPoint(distance)-grabStart,evt); }
                    else if (hit) ApplyBrush(info.point,Vector3.zero,evt);
                    lastMouse = evt.mousePosition;
                }
                evt.Use();
            }
            if (stroke && (evt.rawType == EventType.MouseUp || evt.type == EventType.Ignore || (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)))
            { Run(EndStroke); if (evt.type != EventType.Ignore) evt.Use(); }
        }
        private void ApplyBrush(Vector3 center, Vector3 grab, Event evt)
        {
            sculptVisited.Clear();
            bool mirror=SymmetryActive && Tool != Brush.Paint && Tool != Brush.SmoothWeights;
            var mirrored=mirror ? ReflectSymmetry(center,true) : center;
            var smoothSnapshot=mirror && (Tool == Brush.SmoothShape || evt.shift) ? (Vector3[])deformed.Clone() : null;
            ApplyBrushDab(center,grab,evt,mirror,mirrored,false,smoothSnapshot);
            if(!mirror) return;
            var originalNormal=hitNormal;
            try
            {
                hitNormal=ReflectSymmetry(originalNormal,false).normalized;
                ApplyBrushDab(mirrored,ReflectSymmetry(grab,false),evt,true,center,true,smoothSnapshot);
            }
            finally { hitNormal=originalNormal; }
        }
        private void ApplyBrushDab(Vector3 center, Vector3 grab, Event evt, bool symmetry, Vector3 otherCenter, bool mirrored, Vector3[] smoothSnapshot)
        {
            if (Part == null || IsBrushLocked) return;
            spatial.Query(center,radius,candidates);
            bool weightTool = Tool == Brush.Paint || Tool == Brush.SmoothWeights;
            Dictionary<int,BoneWeight1[]> before = null;
            if (weightTool || (smoothWhileSculpting && Tool != Brush.FitBody))
            {
                before = new Dictionary<int,BoneWeight1[]>();
                foreach (int v in candidates)
                {
                    if (!before.ContainsKey(v)) before.Add(v,weights.Values[v].ToArray());
                    foreach (int n in neighbours[v]) if (!before.ContainsKey(n)) before.Add(n,weights.Values[n].ToArray());
                }
            }
            foreach (int v in candidates)
            {
                if (Part.IsStitchVertex(v)) continue;
                var surfaceDistances = mirrored ? grabMirrorDistances : grabSurfaceDistances;
                float distance = Tool == Brush.Grab && !Document.grabVolume && surfaceDistances != null
                    ? surfaceDistances[v] : Vector3.Distance(initialPoints[v],center);
                if (distance >= radius) continue;
                // Assign overlap to the nearer dab; center-line vertices are
                // processed only once, so symmetry never doubles strength.
                if(symmetry)
                {
                    var otherDistances = mirrored ? grabSurfaceDistances : grabMirrorDistances;
                    float other=Tool == Brush.Grab && !Document.grabVolume && otherDistances != null
                        ? otherDistances[v] : Vector3.Distance(initialPoints[v],otherCenter);
                    if(distance>other+1e-6f || (mirrored && Mathf.Abs(distance-other)<=1e-6f)) continue;
                }
                if (frontOnly && Tool != Brush.FitBody && !(Tool == Brush.Grab && Document.grabVolume) && Vector3.Dot(normals[v],hitNormal) <= 0) continue;
                if (!weightTool && !sculptVisited.Add(sculptRepresentatives[v])) continue;
                float falloff = Mathf.SmoothStep(1,0,distance/radius), amount = strength*falloff;
                if (weightTool)
                {
                    var oldValues=HasTutorial ? weights.Values[v].ToArray() : null;
                    if (Tool == Brush.SmoothWeights || evt.shift) SmoothWeight(v,amount,before);
                    else weights.Set(v,boneIndex,Mathf.Lerp(weights.Weight(v,hoveredInfluence >= 0 ? hoveredInfluence : boneIndex),evt.control ? 0 : targetWeight,amount),locked);
                    weightsChanged = true;
                    if(HasTutorial && !oldValues.SequenceEqual(weights.Values[v]))
                        tutorialStrokeActions.Add(Tool==Brush.SmoothWeights||evt.shift?"smoothWeights":"paint");
                }
                else
                {
                    Vector3 wanted;
                    if (Tool == Brush.Grab && !evt.shift) wanted = initialPoints[v] + grab*falloff;
                    else if (Tool == Brush.SmoothShape || evt.shift)
                    { var points = smoothSnapshot ?? deformed; Vector3 average = Vector3.zero; foreach (int n in neighbours[v]) average += points[n]; wanted = neighbours[v].Length > 0 ? Vector3.Lerp(points[v],average/neighbours[v].Length,amount) : points[v]; }
                    else if (Tool == Brush.FitBody) wanted = FitBodyPoint(deformed[v], amount);
                    else
                    {
                        Vector3 direction = Tool == Brush.Bulge ? (normals[v] + (initialPoints[v]-center)/radius*.5f).normalized : normals[v];
                        wanted = deformed[v] + direction*(radius*.08f*amount*(evt.control ? -1 : 1));
                    }
                    if(symmetry && (initialPoints[v]-ReflectSymmetry(initialPoints[v],true)).sqrMagnitude<1e-10f)
                        wanted=(wanted+ReflectSymmetry(wanted,true))*.5f;
                    if(HasTutorial && (wanted-deformed[v]).sqrMagnitude>1e-14f)
                    {
                        tutorialStrokeActions.Add("sculpt:"+(evt.shift?Brush.SmoothShape:Tool));
                        if(SymmetryActive && Document.sculptSymmetryAxis==1)tutorialStrokeActions.Add("symmetricStroke");
                    }
                    vertices[v] = inverses[v].MultiplyPoint3x4(wanted); deformed[v] = wanted; geometryChanged = true;
                    if (smoothWhileSculpting && Part.bound && Tool != Brush.FitBody)
                    {
                        SmoothWeight(v,amount*.25f,before); weightsChanged = true;
                        var updated = weights.PreviewSkinMatrix(v,skinMatrices);
                        if (Mathf.Abs(updated.determinant) > 1e-8f)
                        { inverses[v] = updated.inverse; vertices[v] = inverses[v].MultiplyPoint3x4(wanted); }
                    }
                    // Copy the rest-space position even when a seam duplicate was
                    // outside the picked triangle/front-face mask. UVs, weights,
                    // submeshes and blend shapes remain separate and untouched.
                    foreach (int peer in sculptSeams[v])
                    {
                        if (peer == v || Part.IsStitchVertex(peer)) continue;
                        vertices[peer] = vertices[v];
                        deformed[peer] = Part.bound ? weights.PreviewSkinMatrix(peer, skinMatrices).MultiplyPoint3x4(vertices[peer]) : vertices[peer];
                    }
                }
            }
            if (geometryChanged) { wirePoseDirty = true; Part.mesh.vertices = vertices; displayMeshes[Document.selected].vertices = vertices; }
            // Weight edits preview in the current pose immediately; recalculating
            // normals and collision cooking are deferred until stroke release.
            if (weightsChanged) { weights.Apply(Part.mesh); ApplyPreviewWeights(Part.mesh,displayMeshes[Document.selected]); }
            if (weightsChanged) UpdateDisplay();
            Changed();
        }
        private void SmoothWeight(int vertex, float amount, Dictionary<int,BoneWeight1[]> before)
        {
            if (Part.IsStitchVertex(vertex) || neighbours[vertex].Length == 0) return;
            var average = new Dictionary<int,float>();
            foreach (int n in neighbours[vertex]) foreach (var w in before[n])
                average[w.boneIndex] = (average.TryGetValue(w.boneIndex,out float value) ? value : 0) + w.weight/neighbours[vertex].Length;
            var list = weights.Values[vertex]; var result = new Dictionary<int,float>(); float fixedSum = 0;
            foreach (var w in before[vertex])
            { if (locked[w.boneIndex]) { result[w.boneIndex] = w.weight; fixedSum += w.weight; } else result[w.boneIndex] = w.weight*(1-amount); }
            foreach (var pair in average) if (!locked[pair.Key]) result[pair.Key] = (result.TryGetValue(pair.Key,out float value) ? value : 0) + pair.Value*amount;
            float total = result.Where(p => !locked[p.Key]).Sum(p => p.Value);
            list.Clear(); foreach (var pair in result) if (pair.Value > 1e-8f)
                list.Add(new BoneWeight1 { boneIndex = pair.Key, weight = locked[pair.Key] ? pair.Value : total > 0 ? pair.Value*(1-fixedSum)/total : 0 });
        }
        internal void EndStroke()
        {
            if (!stroke) return;
            fitBodySurface = null;
            stroke = false; if (GUIUtility.hotControl == brushControl) GUIUtility.hotControl = 0;
            if (Part != null && geometryChanged)
            {
                Part.mesh.RecalculateNormals(); if (Part.mesh.uv.Length == Part.mesh.vertexCount) Part.mesh.RecalculateTangents(); Part.mesh.RecalculateBounds();
            }
            Undo.CollapseUndoOperations(undoGroup); Undo.IncrementCurrentGroup(); Rebuild(); Changed();
            foreach(var action in tutorialStrokeActions)RecordSkinTutorialAction(action);
            tutorialStrokeActions.Clear();
        }
        private void Export()
        {
            EndStroke();
            if (!Document.parts.Any(p => p.bound)) throw new InvalidOperationException("Bind at least one mesh before exporting.");
            string path = EditorUtility.SaveFilePanelInProject("Export skinned character", "Skinned Character", "prefab", "Export independent meshes and a skeleton prefab.");
            if (string.IsNullOrEmpty(path)) return;
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            string meshPath = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(path,null) + " Meshes.asset");
            EditorGUIUtility.PingObject(ExportToPath(path, meshPath));
        }
        internal GameObject ExportToPath(string path, string meshPath)
        {
            EndStroke();
            if (!Document.parts.Any(p => p.bound)) throw new InvalidOperationException("Bind at least one mesh before exporting.");
            if (AssetDatabase.LoadMainAssetAtPath(path) || AssetDatabase.LoadMainAssetAtPath(meshPath))
                throw new InvalidOperationException("Export paths must be new assets.");
            var exportPose = CaptureExportTPose();
            var root = Object.Instantiate(Document.skeleton); root.hideFlags = HideFlags.None;
            var owned = new List<Mesh>();
            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); root.transform.localScale = Vector3.one;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>()) Object.DestroyImmediate(renderer);
                PoseExportSkeleton(root,exportPose);
                var rigBones = root.GetComponentsInChildren<Transform>(true);
                foreach (var part in Document.parts.Where(p => p.bound))
                {
                    var mesh = Object.Instantiate(part.mesh); mesh.hideFlags = HideFlags.None; owned.Add(mesh);
                    BakePartForTPose(mesh,part.preserveStitchedBindPose ? CapturePartExportPose(part) : exportPose);
                    if (!part.preserveStitchedBindPose)
                        mesh.bindposes=rigBones.Select(b=>b.worldToLocalMatrix*root.transform.localToWorldMatrix).ToArray();
                    if (owned.Count == 1) AssetDatabase.CreateAsset(mesh,meshPath); else AssetDatabase.AddObjectToAsset(mesh,meshPath);
                    var go = new GameObject(part.name); go.transform.SetParent(root.transform,false);
                    var skin = go.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.bones = rigBones; skin.rootBone = root.transform;
                    skin.sharedMaterials = part.materials; skin.localBounds = new Bounds(Vector3.up,Vector3.one*4);
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root,path); if (!prefab) throw new InvalidOperationException("Unity could not save the prefab.");
                foreach (var mesh in owned) AssetDatabase.SaveAssetIfDirty(mesh);
                return prefab;
            }
            finally { Object.DestroyImmediate(root); foreach (var mesh in owned) if (!EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh); }
        }
        public void Dispose()
        {
            DisposeUVChecker();materialPreview.Dispose();
            DisposeWireframe();
            FinishObjectGesture();
            FinishPoseGesture();
            DisposePreviewAnimation();
            CancelBrushInteraction();
            if (stroke) EndStroke(); Undo.undoRedoPerformed -= OnUndo;
            if (sculptContext) Object.DestroyImmediate(sculptContext);
            foreach (var mesh in displayMeshes) if (mesh) Object.DestroyImmediate(mesh);
            if (shaded) Object.DestroyImmediate(shaded); if (heat) Object.DestroyImmediate(heat); if (pickMesh) Object.DestroyImmediate(pickMesh);
            if (referenceShaded) Object.DestroyImmediate(referenceShaded);
            if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
        }
    }
}
