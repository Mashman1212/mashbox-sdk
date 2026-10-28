using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor : IDisposable
    {
        internal readonly StudioRetopologyDocument Document;
        internal Scene Scene { get; private set; }
        private readonly Action repaint;
        private readonly List<GameObject> references=new List<GameObject>(),outputs=new List<GameObject>();
        private readonly List<Mesh> display=new List<Mesh>();
        private readonly List<Mesh> xrayLines=new List<Mesh>();
        private readonly float xrayDepthPercent=Mathf.Clamp(EditorPrefs.GetFloat("MashBox.Retopology.XrayDepthPercent",2f),0f,20f);
        private readonly Dictionary<Mesh,StudioRetopologySurface> surfaces=new Dictionary<Mesh,StudioRetopologySurface>();
        private readonly Dictionary<Mesh,(bool reference,bool output)> visibility=new Dictionary<Mesh,(bool reference,bool output)>();
        private readonly HashSet<int> selected=new HashSet<int>();
        private Material referenceMaterial,outputMaterial,workingXrayMaterial;
        private StudioPreviewLighting lighting;
        private Vector2 meshScroll,toolsScroll;
        private string error,status;
        private readonly Dictionary<Part,string> audits=new Dictionary<Part,string>();
        private bool wire=true,local=true;
        private bool workingXray=EditorPrefs.GetBool("MashBox.Retopology.WorkingXray",true);
        private bool referenceWire=EditorPrefs.GetBool("MashBox.Retopology.ReferenceWire",false);
        private readonly Dictionary<Mesh,Vector3[]> referenceWireLines=new Dictionary<Mesh,Vector3[]>();
        private int patchSegments=6;
        private int bridgeBands=1;
        private bool patchAutoDensity=true;
        private float patchQuadSize;
        private float PatchQuadSize => patchQuadSize>0?patchQuadSize:Mathf.Max(.0001f,Active.reference?Mathf.Max(Active.reference.bounds.size.x,Mathf.Max(Active.reference.bounds.size.y,Active.reference.bounds.size.z))/48f:.05f);
        private SceneView view;
        internal Part Active => Document.selected>=0&&Document.selected<Document.parts.Count?Document.parts[Document.selected]:null;
        internal Action<Mesh,string> EditInModeling;
        internal Action<Part> SendToSkinner;
        internal Action<Part,bool> SendToCloth;
        internal Action<GameObject> SkinOutput;

        internal StudioRetopologyEditor(StudioRetopologyDocument document,Action repaint)
        {
            Document=document;this.repaint=repaint;Scene=EditorSceneManager.NewPreviewScene();
            referenceMaterial=new Material(Shader.Find("MashBox/Studio/Preview")) { hideFlags=HideFlags.HideAndDontSave };
            referenceMaterial.SetColor("_Color",new Color(.36f,.39f,.43f));referenceMaterial.SetFloat("_Cull",0);
            outputMaterial=new Material(referenceMaterial);outputMaterial.SetColor("_Color",new Color(.56f,.74f,.70f));
            lighting=new StudioPreviewLighting(Scene);lighting.ApplyMaterialPreview();
            Undo.undoRedoPerformed+=OnUndo;EditorApplication.update+=TickAutoTumble; Rebuild();RestorePolygonDraft();CaptureTutorialSettings();
        }
        internal void SuspendLighting(){StopAutoTumble();lighting?.RestoreCameras();}
        internal void ConfigureLightingCamera(SceneView view)=>lighting?.ConfigureCamera(view);
        private void OnUndo()
        {
            StopAutoTumble();
            ReleaseQuadDrag();
            // Stop capture without collapsing or reverting the history being restored.
            if(stripStroke&&GUIUtility.hotControl==stripControl)GUIUtility.hotControl=0;
            if(relaxStroke&&GUIUtility.hotControl==relaxControl)GUIUtility.hotControl=0;
            stripStroke=relaxStroke=false;
            // Visibility is a viewport preference, independent of geometry Undo.
            foreach(var part in Document.parts)
                if(part.reference&&visibility.TryGetValue(part.reference,out var shown))
                {part.showReference=shown.reference;part.showOutput=shown.output;}
            CancelResolution();CancelVertexSelection();ClearComponents();ClearPatchCorners(true,false);RestorePolygonDraft();dragUndo=-1;status=null;error=null;Rebuild();repaint();
        }
        internal void Run(Action action)
        {
            try { action();error=null; }
            catch(OperationCanceledException e){status=e.Message;}
            catch(Exception e){error=e.GetBaseException().Message;Debug.LogException(e);}
            finally { EditorUtility.ClearProgressBar(); repaint(); }
        }
        private StudioRetopologySurface Surface
        {
            get { if(Active==null||!Active.reference)throw new InvalidOperationException("The reference mesh is missing. Reimport the reference before projecting topology.");if(!surfaces.TryGetValue(Active.reference,out var surface)){surface=new StudioRetopologySurface(Active.reference);surfaces.Add(Active.reference,surface);}return surface; }
        }
        private void Rebuild()
        {
            ClearTopologyDrawing();
            // Recover references lost by the old JSON rollback while the original
            // transient mesh is still alive. Never guess between duplicate names.
            for(int i=0;i<Document.parts.Count;i++)
            {
                var part=Document.parts[i];if(part.reference)continue;
                Mesh recovered=null;
                if(i<references.Count&&references[i])recovered=references[i].GetComponent<MeshFilter>()?.sharedMesh;
                if(!recovered)
                {
                    var matches=Resources.FindObjectsOfTypeAll<Mesh>().Where(m=>m&&m.name==part.name+" Reference"&&
                        (m.hideFlags&HideFlags.DontSaveInEditor)!=0&&!EditorUtility.IsPersistent(m)).Take(2).ToArray();
                    if(matches.Length==1)recovered=matches[0];
                }
                if(recovered){part.reference=recovered;status="Recovered the in-memory reference mesh.";}
            }
            audits.Clear();
            foreach(var go in references.Concat(outputs))if(go)Object.DestroyImmediate(go);
            foreach(var mesh in display)if(mesh)Object.DestroyImmediate(mesh);
            foreach(var lines in xrayLines)if(lines)Object.DestroyImmediate(lines);xrayLines.Clear();
            references.Clear();outputs.Clear();display.Clear();
            for(int i=0;i<Document.parts.Count;i++)
            {
                var p=Document.parts[i];
                if(p.reference&&!visibility.ContainsKey(p.reference))visibility[p.reference]=(p.showReference,p.showOutput);
                var reference=Create(p.name+" Reference",p.reference,referenceMaterial);references.Add(reference);reference.SetActive(p.showReference&&p.reference);
                if(p.reference){var collider=reference.AddComponent<MeshCollider>();collider.sharedMesh=p.reference;}
                var mesh=StudioRetopologyDocument.BuildMesh(p);display.Add(mesh);
                var lines=new Mesh{name="Retopology X-ray edges",hideFlags=HideFlags.HideAndDontSave,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                lines.SetVertices(p.vertices);lines.SetIndices(StudioRetopologyOperations.Edges(p).Keys.SelectMany(e=>new[]{e.Item1,e.Item2}).ToArray(),MeshTopology.Lines,0);xrayLines.Add(lines);
                var output=Create(p.name+" Low",mesh,outputMaterial);outputs.Add(output);output.SetActive(p.showOutput);
                if(mesh.vertexCount>0&&mesh.triangles.Length>0)output.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
            ApplyCheckerDisplay();repaint();
        }
        private GameObject Create(string name,Mesh mesh,Material material)
        {
            var go=new GameObject(name){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(go,Scene);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=Enumerable.Repeat(material,mesh?Mathf.Max(1,mesh.subMeshCount):1).ToArray();return go;
        }
        private void Change(string label,Action action)
        {
            if(Active==null)return;
            // Keep Unity object references and Part identity alive. JSON rollback
            // cannot safely restore a transient reference Mesh.
            var part=Active;
            var vertices=new List<Vector3>(part.vertices);var uv=new List<Vector2>(part.uv);
            var faces=part.faces.Select(f=>new StudioRetopologyDocument.Face((int[])f.vertices.Clone(),f.material){cornerUV=f.cornerUV==null?null:(Vector2[])f.cornerUV.Clone()}).ToList();
            var reference=part.reference;var materials=part.materials;bool needsUV=part.needsUV,dirty=Document.dirty;
            int revision=part.Revision;
            StoreToolState();
            var toolState=Document.toolState;var draft=Document.polygonPoints.ToArray();int draftPart=Document.polygonPart;var draftNormal=Document.polygonNormal;
            Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,label);
            try { action();
                bool topologyChanged=vertices.Count!=part.vertices.Count||faces.Count!=part.faces.Count||faces.Where((f,i)=>i<part.faces.Count&&!f.vertices.SequenceEqual(part.faces[i].vertices)).Any();
                if(topologyChanged&&faces.Any(f=>f.cornerUV!=null)){foreach(var face in part.faces)face.cornerUV=null;part.needsUV=true;}
                Active.Revision++;Document.dirty=true;EditorUtility.SetDirty(Document);ClearComponents();ClearPatchCorners();StoreToolState();Rebuild();
                if(CurrentLesson!=null && (vertices.Count!=part.vertices.Count || faces.Count!=part.faces.Count || !vertices.SequenceEqual(part.vertices)
                    || faces.Where((f,i)=>i<part.faces.Count&&!f.vertices.SequenceEqual(part.faces[i].vertices)).Any()))
                    RecordTutorialAction(label);
                if(CurrentLesson!=null)RecordTutorialGeometry(label,faces);
            }
            catch
            {
                part.vertices=vertices;part.uv=uv;part.faces=faces;part.reference=reference;part.materials=materials;
                part.needsUV=needsUV;part.Revision=revision;Document.dirty=dirty;
                Document.toolState=toolState;Document.polygonPoints=draft.ToList();Document.polygonPart=draftPart;Document.polygonNormal=draftNormal;
                ClearComponents();ClearPatchCorners(true,false);RestoreToolState();
                Rebuild();throw;
            }
        }
        internal void Import(GameObject source)
        {
            if(!source)return;var added=new List<Part>();
            try
            {
                foreach(var renderer in source.GetComponentsInChildren<Renderer>(true))
                {
                    var skin=renderer as SkinnedMeshRenderer;var filter=renderer.GetComponent<MeshFilter>();var original=skin?skin.sharedMesh:filter?filter.sharedMesh:null;
                    if(!original)continue;
                    var mesh=StudioSkinningDocument.CreateEditableCopy(original);
                    var part=new Part { name=renderer.name,reference=mesh,materials=renderer.sharedMaterials };added.Add(part);
                    // Imported local bind geometry remains the reference; no posing or source edits.
                    var matrix=source.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                    if(skin) CaptureReferenceSkin(part,skin,source.transform,matrix);
                    mesh.vertices=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
                    if(matrix.determinant<0)
                    {for(int s=0;s<mesh.subMeshCount;s++){var indices=mesh.GetTriangles(s);for(int t=0;t<indices.Length;t+=3){int swap=indices[t];indices[t]=indices[t+2];indices[t+2]=swap;}mesh.SetTriangles(indices,s);}mesh.RecalculateNormals();}
                    mesh.name=part.name+" Reference";
                }
                if(added.Count==0)throw new InvalidOperationException("The dropped object contains no mesh geometry.");
                Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Add retopology reference");
                Document.parts.AddRange(added);Document.selected=Document.parts.Count-added.Count;
                // Keep detached reference copies alive through Undo/Redo and script reloads.
                Document.dirty=true;EditorUtility.SetDirty(Document);ClearComponents();ClearPatchCorners();Rebuild();Frame();
                status="Reference locked. Copy it to edit, generate a reduced mesh, or draw a new patch.";
            }
            catch {foreach(var p in added)if(!Document.parts.Contains(p)&&p.reference)Object.DestroyImmediate(p.reference);throw;}
        }
        internal void DrawMeshes()
        {
            GUILayout.Label("Drop a model into the viewport",StudioTheme.Styles.miniLabel);
            meshScroll=EditorGUILayout.BeginScrollView(meshScroll);
            for(int i=0;i<Document.parts.Count;i++)
            {
                int index=i;var p=Document.parts[i];var row=GUILayoutUtility.GetRect(50,24,GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(row,Document.selected==i?new Color(.18f,.31f,.29f):new Color(.12f,.13f,.14f));
                if(row.Contains(Event.current.mousePosition)&&StudioMeshMaterialPreview.IsMaterialDrag)StudioMeshMaterialPreview.Drop(m=>SetMaterial(p,0,m));
                HandleMeshRowContext(row,p);
                if(GUI.Button(row,new GUIContent("  "+p.name,"Select this mesh"),new GUIStyle(StudioTheme.Styles.label){clipping=TextClipping.Clip}))
                {Document.selected=i;ClearComponents();ClearPatchCorners();ApplyCheckerDisplay();repaint();}
                var referenceRow=GUILayoutUtility.GetRect(50,22,GUILayout.ExpandWidth(true));
                if(referenceRow.Contains(Event.current.mousePosition)&&(Event.current.type==EventType.ContextClick||(Event.current.button==1&&(Event.current.type==EventType.MouseDown||Event.current.type==EventType.MouseUp))))
                {
                    if(Event.current.type==EventType.MouseDown)
                    {
                        var menu=new StudioMenu();
                        if(p.reference&&SendToCloth!=null)menu.AddItem(new GUIContent("Send to Cloth Studio"),false,()=>Run(()=>SendToCloth(p,true)));
                        else menu.AddDisabledItem(new GUIContent("Send to Cloth Studio"));
                        menu.DropDown(new Rect(Event.current.mousePosition,Vector2.zero));
                    }
                    Event.current.Use();
                }
                bool visible=GUI.Toggle(new Rect(referenceRow.x+12,referenceRow.y,100,referenceRow.height),p.showReference,"Reference");
                GUI.Label(new Rect(referenceRow.x+112,referenceRow.center.y-8,16,16),EditorGUIUtility.IconContent("LockIcon-On"));
                if(visible!=p.showReference){p.showReference=visible;if(p.reference)visibility[p.reference]=(p.showReference,p.showOutput);references[index].SetActive(visible);repaint();}
                bool referenceSkinned=StudioSkinStatusIcon.HasWeights(p.reference);
                StudioSkinStatusIcon.Draw(new Rect(referenceRow.xMax-20,referenceRow.center.y-8,16,16),referenceSkinned,
                    referenceSkinned ? "Skinned reference — contains skin weights" : "Unskinned reference — no skin weights");
                if(referenceRow.Contains(Event.current.mousePosition)&&StudioMeshMaterialPreview.IsMaterialDrag)StudioMeshMaterialPreview.Drop(m=>SetMaterial(p,0,m));
                var workingRow=GUILayoutUtility.GetRect(50,22,GUILayout.ExpandWidth(true));
                if(workingRow.Contains(Event.current.mousePosition)&&StudioMeshMaterialPreview.IsMaterialDrag)StudioMeshMaterialPreview.Drop(m=>SetMaterial(p,0,m));
                // IMGUI toggles can consume RMB MouseDown before ContextClick.
                // Claim the complete right-button gesture before drawing the toggle.
                var rowEvent=Event.current;
                if(workingRow.Contains(rowEvent.mousePosition)&&
                    (rowEvent.type==EventType.ContextClick||
                     (rowEvent.button==1&&(rowEvent.type==EventType.MouseDown||rowEvent.type==EventType.MouseUp))))
                {
                    if(rowEvent.type==EventType.MouseDown)
                        ShowWorkingMeshMenu(p,new Rect(rowEvent.mousePosition,Vector2.zero));
                    rowEvent.Use();
                }
                var toggleRect=new Rect(workingRow.x+12,workingRow.y,125,workingRow.height);
                bool outputVisible=GUI.Toggle(toggleRect,p.showOutput,new GUIContent("Working mesh","Right-click for mesh actions"));
                if(outputVisible!=p.showOutput&&Event.current.button==0){p.showOutput=outputVisible;if(p.reference)visibility[p.reference]=(p.showReference,p.showOutput);outputs[index].SetActive(outputVisible);repaint();}
                GUI.Label(new Rect(toggleRect.xMax,workingRow.y,Mathf.Max(0,workingRow.xMax-toggleRect.xMax-24),workingRow.height),p.faces.Count.ToString("N0"),StudioTheme.Styles.miniLabel);
                StudioSkinStatusIcon.Draw(new Rect(workingRow.xMax-20,workingRow.center.y-8,16,16),false,
                    referenceSkinned ? "Unskinned working mesh — reference weights transfer when sent to Skinner" : "Unskinned working mesh — no reference skin weights to transfer");
                DrawMaterialSlots(p);
                GUILayout.Space(6);
            }
            EditorGUILayout.EndScrollView();
        }
        private void HandleMeshRowContext(Rect row,Part part)
        {
            var evt=Event.current;
            if(!row.Contains(evt.mousePosition))return;
            if(evt.type==EventType.ContextClick||
                (evt.button==1&&(evt.type==EventType.MouseDown||evt.type==EventType.MouseUp)))
            {
                if(evt.type==EventType.MouseDown)ShowWorkingMeshMenu(part,new Rect(evt.mousePosition,Vector2.zero));
                evt.Use();
            }
        }
        private void RemoveMesh()
        {
            if(Active==null)return;
            EndStripStroke();EndBrushAdjustment();EndRelaxBrush();CancelResolution();CancelVertexSelection();
            CancelContextWheel();ClearComponents();ClearPatchCorners();dragUndo=-1;
            Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Remove retopology mesh");
            Document.parts.RemoveAt(Document.selected);
            Document.selected=Mathf.Min(Document.selected,Document.parts.Count-1);
            Document.dirty=true;EditorUtility.SetDirty(Document);
            // Keep reference Mesh objects alive for Undo; Rebuild disposes the viewport copies.
            Rebuild();status="Mesh removed from workspace. Undo restores it. Source asset unchanged.";
        }
        private void ShowWorkingMeshMenu(Part part,Rect anchor)
        {
            void OnPart(Action action)
            {
                int index=Document.parts.IndexOf(part);if(index<0)return;
                if(Document.selected!=index){CancelResolution();CancelVertexSelection();ClearComponents();ClearPatchCorners();}
                Document.selected=index;ApplyCheckerDisplay();Run(action);
            }
            var menu=new StudioMenu();bool hasMesh=part.vertices.Count>0;
            void Item(string title,Action action,bool enabled=true)
            {if(enabled)menu.AddItem(new GUIContent(title),false,()=>OnPart(action));else menu.AddDisabledItem(new GUIContent(title));}
            Item(part.showOutput?"Hide Working Mesh":"Show Working Mesh",()=>
            {
                part.showOutput=!part.showOutput;if(part.reference)visibility[part.reference]=(part.showReference,part.showOutput);
                outputs[Document.selected].SetActive(part.showOutput);repaint();
            });
            Item("Frame Working Mesh",()=>{if(view)view.Frame(display[Document.selected].bounds,false);},hasMesh);
            menu.AddSeparator("");
            Item("Export FBX...",()=>Export(false),part.faces.Count>0);
            Item("Export OBJ (quads)...",()=>Export(true),part.faces.Count>0);
            Item("Send Reference to Cloth Studio",()=>SendToCloth?.Invoke(part,true),part.reference&&SendToCloth!=null);
            Item("Send to Cloth Studio",()=>SendToCloth?.Invoke(part,false),part.faces.Count>0&&SendToCloth!=null);
            Item("Send to Skinner",()=>SendToSkinner?.Invoke(part),part.faces.Count>0&&SendToSkinner!=null);
            Item("Continue in Modeling",()=>Send(EditInModeling),part.faces.Count>0&&EditInModeling!=null);
            menu.AddSeparator("");
            Item("Clear Working Mesh",ClearWorkingMesh,hasMesh);
            Item("Remove Mesh",RemoveMesh);
            menu.DropDown(anchor);
        }
        private void ClearWorkingMesh()
        {
            CancelResolution();CancelVertexSelection();
            Change("Clear working mesh",()=>
            {
                Active.vertices.Clear();Active.faces.Clear();Active.uv.Clear();Active.needsUV=false;
                status="Working mesh cleared. Reference retained. Undo restores the working mesh.";
            });
        }
        internal void DrawTools()
        {
            toolsScroll=EditorGUILayout.BeginScrollView(toolsScroll);
            if(DrawTutorialGuide()){EditorGUILayout.EndScrollView();return;}
            GUILayout.Label("RETOPOLOGY",StudioTheme.Styles.boldLabel);
            GUILayout.Label("Reference stays locked. Work on a separate mesh.",StudioTheme.Styles.wordWrappedMiniLabel);
            if(Active==null){GUILayout.Label("Drop an FBX, OBJ, prefab or mesh object to begin.",StudioTheme.Styles.wordWrappedMiniLabel);EditorGUILayout.EndScrollView();return;}
            string[] profiles={"Clothing","Pants","Shirt / Hoodie","Head","Hard Surface"};
            int profile=Array.IndexOf(profiles,Document.profile);int next=EditorGUILayout.Popup("Preset",Mathf.Max(0,profile),profiles);
            if(next!=profile){Document.profile=profiles[next];Document.targetTriangles=next==3?6000:next==1?5000:4000;Document.preserveBorders=true;}
            Document.preserveBorders=EditorGUILayout.Toggle("Protect openings",Document.preserveBorders);
            if(Document.profile=="Pants")
            {
                Document.targetTriangles=Mathf.Max(4,EditorGUILayout.IntField("Cage triangle budget",Document.targetTriangles));
                if(GUILayout.Button("Generate pants quad cage"))Run(GeneratePants);
                GUILayout.Label("Continuous waist, crotch and leg loops. Upright pants only; pockets and straps need separate topology. New UVs required.",StudioTheme.Styles.wordWrappedMiniLabel);
            }
            if(GUILayout.Button("Copy reference to edit"))Run(()=>Change("Copy reference",()=>StudioRetopologyDocument.CopyReference(Active)));
            GUILayout.Space(8);
            Document.project=EditorGUILayout.Toggle("Surface snapping",Document.project);
            Document.offset=EditorGUILayout.FloatField("Surface offset",Document.offset);
            Document.symmetry=EditorGUILayout.Toggle(new GUIContent("Mirror drawing X","New quads and patches are mirrored around the imported model's local X=0 plane."),Document.symmetry);
            DrawStripSettings();DrawTubeSettings();DrawResolutionSettings();DrawRelaxBrushSettings();
            using(new EditorGUI.DisabledScope(Active.faces.Count==0))
            {
                if(GUILayout.Button(new GUIContent("Bridge selected borders","Edges mode: Shift-click matching boundary chains, or double-click a boundary edge and Shift-double-click another loop. Bridge Bands in the top toolbar controls rows across the gap.")))Run(BridgeLoops);
                if(GUILayout.Button("Project onto reference"))Run(Project);
                if(GUILayout.Button("Relax on surface"))Run(Relax);
                if(GUILayout.Button("Pair triangles into quads"))Run(()=>Change("Pair quads",()=>{int n=StudioRetopologyOperations.PairQuads(Active);status=$"Paired {n:N0} quads; unsuitable triangles retained.";}));
                if(GUILayout.Button("Subdivide + project"))Run(()=>Change("Subdivide",()=>StudioRetopologyOperations.Subdivide(Active,Document.project?Surface:null,Document.offset)));
                if(GUILayout.Button("Clean mesh"))Run(CleanMesh);
            }
            if(!audits.TryGetValue(Active,out var audit)){audit=StudioRetopologyOperations.Audit(Active);audits[Active]=audit;}
            GUILayout.Space(8);GUILayout.Label(audit,StudioTheme.Styles.wordWrappedMiniLabel);
            if(Active.needsUV)EditorGUILayout.HelpBox("UVs need review. Use UV > Transfer UVs from Reference, or unwrap in Modeling.",MessageType.Info);
            if(!string.IsNullOrEmpty(status))GUILayout.Label(status,StudioTheme.Styles.wordWrappedMiniLabel);
            if(!string.IsNullOrEmpty(error))EditorGUILayout.HelpBox(error,MessageType.Error);
            EditorGUILayout.EndScrollView();
        }
        private void GeneratePants()
        {
            // Compute separately, then commit as one undoable edit.
            var cage=StudioRetopologyPants.Generate(Active.reference,Document.targetTriangles,Document.offset,
                progress=>EditorUtility.DisplayCancelableProgressBar("Pants quad cage","Fitting continuous garment loops",progress));
            Change("Generate pants quad cage",()=>
            {
                Active.vertices=cage.vertices;Active.uv=cage.uv;Active.faces=cage.faces;Active.needsUV=true;
                status=$"{Active.faces.Count:N0} quads with continuous leg loops. Review crotch fit and unwrap UVs before skinning.";
            });
        }
        private void CleanMesh()
        {
            Change("Clean topology",()=>StudioRetopologyOperations.Clean(Active));
            status=StudioRetopologyOperations.Audit(Active);
            RecordTutorialAction("validate");
            view?.ShowNotification(new GUIContent("Mesh cleaned and checked"));
        }
        private void Project()=>Change("Project topology",()=>{for(int i=0;i<Active.vertices.Count;i++)if(selected.Count==0||selected.Contains(i))Active.vertices[i]=Surface.Project(Active.vertices[i],Document.offset);});
        private void Relax()=>Change("Relax topology",()=>StudioRetopologyOperations.Relax(Active,Document.project?Surface:null,Document.preserveBorders,Document.offset,selected));
        private void BridgeLoops()
        {
            if(selectionMode==SelectionMode.Faces){status="Switch to Edges and select matching borders on both sides of the gap.";return;}
            string issue=selectionMode==SelectionMode.Edges?StudioRetopologyBridgeEdges.Validate(Active,selectedEdges,out _,out _):StudioRetopologyLoops.BridgeIssue(Active,selected);
            if(issue!=null){status=issue;return;}
            if(Document.project&&!Active.reference){status="The reference mesh is missing. Reimport it or turn off surface snapping before bridging.";return;}
            var vertices=new HashSet<int>(selected);var edges=new HashSet<(int,int)>(selectedEdges);
            bool byEdges=selectionMode==SelectionMode.Edges;
            var surface=Document.project?Surface:null;float offset=Document.offset;
            var bridge=new DensityBridge { owner=Active,before=new PatchGeometry(Active) };
            bridge.build=()=>{if(byEdges)StudioRetopologyBridgeEdges.Bridge(Active,edges,bridgeBands,surface,offset);else StudioRetopologyLoops.Bridge(Active,vertices,bridgeBands,surface,offset);};
            Change("Bridge boundary borders",bridge.build);
            bridge.after=new PatchGeometry(Active);lastDensityBridge=bridge;
        }
        internal void Frame()
        {
            if(!view||Active==null)return;
            RecordTutorialAction("frame");
            // Edge and face selections already maintain their participating vertex set.
            var indices=selected.Where(i=>i>=0&&i<Active.vertices.Count).ToArray();
            if(indices.Length>0)
            {
                var bounds=new Bounds(Active.vertices[indices[0]],Vector3.zero);
                foreach(int i in indices)bounds.Encapsulate(Active.vertices[i]);
                float modelSize=Active.reference?Active.reference.bounds.size.magnitude:1f;
                float minimum=Mathf.Max(.001f,modelSize*.02f);
                var size=bounds.size*1.2f;
                bounds.size=new Vector3(Mathf.Max(size.x,minimum),Mathf.Max(size.y,minimum),Mathf.Max(size.z,minimum));
                view.Frame(bounds,false);return;
            }
            if(Active.reference)view.Frame(Active.reference.bounds,false);
            else if(Document.selected<display.Count&&display[Document.selected])view.Frame(display[Document.selected].bounds,false);
        }
        private GUIStyle statisticNumber;
        internal void DrawStatistics()
        {
            if(Active==null)return;
            if(statisticNumber==null)statisticNumber=new GUIStyle(StudioTheme.Styles.miniLabel){alignment=TextAnchor.MiddleRight};
            long referenceFaces=0,referenceTriangles=0,referenceQuads=0;
            if(Active.reference)for(int sub=0;sub<Active.reference.subMeshCount;sub++)
            {
                long indices=(long)Active.reference.GetIndexCount(sub);
                var topology=Active.reference.GetTopology(sub);
                if(topology==MeshTopology.Triangles){referenceFaces+=indices/3;referenceTriangles+=indices/3;}
                else if(topology==MeshTopology.Quads){referenceFaces+=indices/4;referenceQuads+=indices/4;referenceTriangles+=indices/2;}
            }
            long triangles=0,quads=0;
            foreach(var face in Active.faces){triangles+=Mathf.Max(0,face.vertices.Length-2);if(face.vertices.Length==4)quads++;}
            void Row(string label,string vertices,string faces,string tris,string quadCount,string tooltip="")
            {
                using(new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent(label,tooltip),StudioTheme.Styles.miniLabel,GUILayout.Width(78));
                    foreach(var value in new[]{vertices,faces,tris,quadCount})GUILayout.Label(value,statisticNumber,GUILayout.Width(76));
                }
            }
            Row("Mesh","Verts","Faces","Tris","Quads");
            Row("Reference",Active.reference?Active.reference.vertexCount.ToString("N0"):"—",
                Active.reference?referenceFaces.ToString("N0"):"—",Active.reference?referenceTriangles.ToString("N0"):"—",Active.reference?referenceQuads.ToString("N0"):"—",
                "Actual imported topology. Unity usually imports polygon faces as triangles; inferred wireframe quad pairs are not counted as source quads.");
            Row("Working",Active.vertices.Count.ToString("N0"),Active.faces.Count.ToString("N0"),triangles.ToString("N0"),quads.ToString("N0"),
                "Editable polygon faces, their triangulated count, and four-sided faces.");
            GUILayout.Space(3);
            int count=selectionMode==SelectionMode.Faces?selectedFaces.Count:selectionMode==SelectionMode.Edges?selectedEdges.Count:selected.Count;
            GUILayout.Label($"Selected: {count:N0} {selectionMode.ToString().ToLowerInvariant()}",StudioTheme.Styles.miniLabel);
        }
        public void Dispose()
        {
            StopAutoTumble();EditorApplication.update-=TickAutoTumble;
            autoTumbleNormalMesh=null;autoTumbleNormals=null;autoTumbleTriangles=null;
            ReleaseQuadDrag();
            ClearTopologyDrawing(); if(topologyOverlayMaterial)Object.DestroyImmediate(topologyOverlayMaterial);
            if(topologyWireMaterial)Object.DestroyImmediate(topologyWireMaterial);
            EndStripStroke();
            EndBrushAdjustment();EndRelaxBrush();CancelResolution();CancelVertexSelection(); CancelContextWheel(); Undo.undoRedoPerformed-=OnUndo;lighting?.Dispose();
            referenceWireLines.Clear();
            foreach(var m in display)if(m)Object.DestroyImmediate(m);
            foreach(var lines in xrayLines)if(lines)Object.DestroyImmediate(lines);
            DisposeUVDisplay();materialPreview.Dispose();
            if(workingXrayMaterial)Object.DestroyImmediate(workingXrayMaterial);
            if(referenceMaterial)Object.DestroyImmediate(referenceMaterial);if(outputMaterial)Object.DestroyImmediate(outputMaterial);
            if(Scene.IsValid())EditorSceneManager.ClosePreviewScene(Scene);
        }
    }
}
