using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool HandleMaterialDrop(SceneView view)
        {
            var evt = Event.current;
            var payload = DragAndDrop.objectReferences;
            int node = -1, slot = -1;
            bool overCanvas = view && (!(view is MashBoxStudioViewport studio) ||
                (studio.ModelCanvasRect.Contains(evt.mousePosition) && !studio.IsOverViewportControls(evt.mousePosition)));
            bool supported = payload.Length == 1 && payload[0] is Material && overCanvas &&
                TryMaterialTarget(view.camera, evt.mousePosition, EditorGUIUtility.pixelsPerPoint, out node, out slot);
            DragAndDrop.visualMode = supported ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            if (supported && evt.type == EventType.DragPerform)
            {
                var material = (Material)payload[0];
                DragAndDrop.AcceptDrag();
                Run(() => AssignMaterial(node, slot, material));
            }
            // Consume misses too: Unity must never assign to unrelated main-scene objects.
            evt.Use(); return true;
        }

        internal bool TryMaterialTarget(Camera camera, Vector2 mouse, float dpi, out int nodeIndex, out int materialSlot)
        {
            nodeIndex = materialSlot = -1;
            if (!camera || !Document) return false;
            var ray = camera.ScreenPointToRay(new Vector3(mouse.x * dpi, camera.pixelHeight - mouse.y * dpi));
            float nearest = float.PositiveInfinity;
            for (int n = 0; n < Transforms.Length; n++)
            {
                var mesh = Document.nodes[n].mesh;
                var t = Transforms[n];
                if (!mesh || !t.gameObject.activeInHierarchy) continue;
                var renderer = t.GetComponent<Renderer>();
                if (!renderer || !renderer.enabled || !renderer.bounds.IntersectRay(ray)) continue;
                var vertices = PickVertices(n);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var topology = mesh.GetTopology(sub);
                    if (topology != MeshTopology.Triangles && topology != MeshTopology.Quads) continue;
                    var indices = mesh.GetIndices(sub);
                    int stride = topology == MeshTopology.Quads ? 4 : 3;
                    for (int i = 0; i + stride <= indices.Length; i += stride)
                        for (int corner = 1; corner < stride - 1; corner++)
                        {
                            if (!RayTriangle(ray, t.TransformPoint(vertices[indices[i]]),
                                t.TransformPoint(vertices[indices[i + corner]]), t.TransformPoint(vertices[indices[i + corner + 1]]),
                                out float distance) || distance >= nearest) continue;
                            nearest = distance; nodeIndex = n; materialSlot = sub;
                        }
                }
            }
            return nodeIndex >= 0;
        }

        private void AssignMaterial(int nodeIndex, int slot, Material material)
        {
            var node = Document.nodes[nodeIndex];
            var existing = node.materials ?? Array.Empty<Material>();
            displayTextures = true;
            if (slot < existing.Length && existing[slot] == material) { ApplyDisplayMaterials();repaint();return; }
            var materials = new Material[Math.Max(existing.Length, node.mesh.subMeshCount)];
            Array.Copy(existing, materials, existing.Length);
            // Fill missing slots without disturbing existing assignments.
            for (int i = existing.Length; i < materials.Length; i++)
                materials[i] = existing.Length > 0 ? existing[existing.Length - 1] : null;
            materials[slot] = material;
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(Document, "Assign material");
            node.materials = materials;
            Transforms[nodeIndex].GetComponent<Renderer>().sharedMaterials = materials;
            ApplyDisplayMaterials();
            Changed(false);
        }

        private const string MaterialSlotCommandPrefix = "Material/Assign Slot/";
        private const string MaterialNewSlotCommand = "Material/Assign New Slot";
        private int[] MaterialAssignmentTargets() => !Document ? Array.Empty<int>() : mode == ComponentMode.Object ? NormalTargets().ToArray() : mode == ComponentMode.Face ? ComponentTargetNodes(true).ToArray() : Array.Empty<int>();
        private int SelectionMaterialSlotCount()
        {
            var targets = MaterialAssignmentTargets();
            if (targets.Length == 0 && ActiveMesh) targets = new[] { Active };
            return targets.Length == 0 ? 0 : targets.Max(i => Math.Max(Document.nodes[i].mesh.subMeshCount, Document.nodes[i].materials?.Length ?? 0));
        }
        private List<ToolbarCommand> MaterialSlotRadialChoices()
        {
            var result = new List<ToolbarCommand>(); bool enabled = CanAssignMaterialSlot;
            var targets = MaterialAssignmentTargets();
            int reference = ActiveMesh && targets.Contains(Active) ? Active : targets.Length > 0 ? targets[0] : ActiveMesh ? Active : -1;
            for (int slot = 0; slot < SelectionMaterialSlotCount(); slot++)
            {
                int captured = slot;
                var materials = reference >= 0 ? Document.nodes[reference].materials : null;
                string name = materials != null && slot < materials.Length && materials[slot] ? " · " + materials[slot].name : "";
                result.Add(new ToolbarCommand { Key = MaterialSlotCommandPrefix + slot, Path = "Material/Assign Selection/Slot " + slot,
                    Label = "Slot " + slot + name, Enabled = enabled, Execute = () => Run(() => AssignSelectionMaterialSlot(captured)) });
            }
            result.Add(new ToolbarCommand { Key = MaterialNewSlotCommand, Path = "Material/Assign Selection/New Slot", Label = "New Slot", Enabled = enabled,
                Execute = () => Run(() => AssignSelectionMaterialSlot(SelectionMaterialSlotCount())) });
            return result;
        }
        private void AddMaterialSlotRadialContext(List<StudioCustomToolbarDefinition> definitions, List<ToolbarCommand> commands)
        {
            var choices = MaterialSlotRadialChoices();
            definitions.RemoveAll(p => p.id == StudioRadialMenus.MaterialSlotsContextId);
            definitions.Add(new StudioCustomToolbarDefinition { id = StudioRadialMenus.MaterialSlotsContextId, name = "Assign Material", commands = choices.Select(c => c.Key).ToList() });
            commands.AddRange(choices);
        }
        private ToolbarCommand ResolveMaterialSlotRadialCommand(string key) =>
            key == MaterialNewSlotCommand || key.StartsWith(MaterialSlotCommandPrefix, StringComparison.Ordinal) ? MaterialSlotRadialChoices().Find(c => c.Key == key) : null;

        private void RemoveMaterialSlot(int slot) => ChangeSelectionMaterialSlot(slot, true, true);

        private bool CanAssignMaterialSlot => Document && (mode == ComponentMode.Object ? NormalTargets().Any() : mode == ComponentMode.Face && ComponentTargetNodes(true).Count > 0);
        private void AssignSelectionMaterialSlot(int slot) => ChangeSelectionMaterialSlot(slot, false);
        private void AddMaterialSlot()
        {
            if (!Document || !ActiveMesh) return;
            ChangeSelectionMaterialSlot(Math.Max(ActiveMesh.subMeshCount, Document.nodes[Active].materials?.Length ?? 0), true);
        }
        private void ChangeSelectionMaterialSlot(int slot, bool addOnly, bool remove = false)
        {
            FinishLiveTool(); FlushModifierEdits();
            if (!Document || (!addOnly && !CanAssignMaterialSlot)) { error = "Select objects or faces to assign a material slot."; return; }
            var targets = addOnly ? new[] { Active } : MaterialAssignmentTargets();
            var selections = new Dictionary<Mesh, HashSet<int>>(); var facesByMesh = new Dictionary<Mesh, List<StudioMeshCuts.Face>>(); var trianglesByMesh = new Dictionary<Mesh, List<int>[]>();
            foreach (int index in targets)
            {
                var node = Document.nodes[index];
                if (node.modifiers?.operations.Any(o => !string.IsNullOrEmpty(o.failure)) == true) throw new InvalidOperationException("Repair or delete the broken modifier, or Clear History, before assigning material slots.");
                if (!selections.TryGetValue(node.mesh, out var selected)) selections[node.mesh] = selected = new HashSet<int>();
                if (!addOnly && mode == ComponentMode.Face) using (new ComponentTargetScope(this, index))
                {
                    if (!CanEditComponents()) throw new InvalidOperationException("Select editable mesh faces to assign a material slot.");
                    selected.UnionWith(Components.Where(IsolationAllowsComponent));
                    facesByMesh[node.mesh] = Faces(); trianglesByMesh[node.mesh] = cachedFaceTriangles;
                }
            }
            string label = remove ? "Remove material slot " + slot : addOnly ? "Add material slot" : "Assign material slot " + slot;
            var plans = new List<StudioMeshCuts.MaterialSlotIndexEdit>(); var histories = new Dictionary<Mesh, StudioModifierHistory>();
            foreach (int index in targets.GroupBy(i => Document.nodes[i].mesh).Select(g => g.First()))
            {
                var node = Document.nodes[index];
                int[] selected = addOnly ? Array.Empty<int>() : mode == ComponentMode.Object ? null : selections[node.mesh].ToArray();
                var edit = StudioMeshCuts.PrepareMaterialSlotIndices(node, slot, selected, remove, remove ? Math.Max(node.mesh.subMeshCount, node.materials?.Length ?? 0) : 0,
                    facesByMesh.TryGetValue(node.mesh, out var faces) ? faces : null, trianglesByMesh.TryGetValue(node.mesh, out var triangles) ? triangles : null);
                if (!edit.Changed) continue;
                plans.Add(edit);
                var operation = new StudioModifierOperation { kind = "MaterialSlot", name = label, parameters = new Vector4(slot, remove ? 1 : 0, 0, edit.SourceCount), faces = selected };
                operation.Bind(node);
                var prior = node.modifiers;
                var history = new StudioModifierHistory { source = prior?.HasHistory == true ? prior.source : StudioModifierSource.Capture(node),
                    operations = prior?.HasHistory == true ? new List<StudioModifierOperation>(prior.operations) : new List<StudioModifierOperation>(), revision = (prior?.revision ?? 0) + 1 };
                history.operations.Add(operation); histories[node.mesh] = history;
            }
            if (plans.Count == 0) return;
            PrepareIsolationTopologyEdit();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(label);
            bool priorWritingModifiers = writingModifiers; writingModifiers = true;
            try
            {
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { Document }.Concat(plans.Select(p => (UnityEngine.Object)p.Mesh)).ToArray(), label);
                foreach (var edit in plans) edit.Apply();
                foreach (var node in Document.nodes)
                {
                    if (!node.mesh || !histories.TryGetValue(node.mesh, out var history)) continue;
                    var edit = plans.First(p => p.Mesh == node.mesh); node.cutPolygons = edit.Polygons; node.cutTriangles = edit.Triangles;
                    int count = edit.After.Length; var previous = node.materials ?? Array.Empty<Material>();
                    if (remove) node.materials = Enumerable.Range(0, Math.Max(count, previous.Length - 1)).Select(i => { int old = i >= slot ? i + 1 : i; return old < previous.Length ? previous[old] : previous.LastOrDefault(); }).ToArray();
                    else if (previous.Length < count) { var materials = new Material[count]; Array.Copy(previous, materials, previous.Length); for (int i = previous.Length; i < count; i++) materials[i] = previous.LastOrDefault(); node.materials = materials; }
                    node.modifiers = history; node.subdivision = new StudioSubdivisionSettings();
                }
                foreach (var edit in plans) EditorUtility.SetDirty(edit.Mesh);
                EditorUtility.SetDirty(Document); Undo.CollapseUndoOperations(group);
            }
            catch { Undo.RevertAllDownToGroup(group); foreach (var edit in plans) edit.Apply(true); throw; }
            finally { writingModifiers = priorWritingModifiers; Undo.IncrementCurrentGroup(); }
            var affected = new HashSet<Mesh>(plans.Select(p => p.Mesh));
            for (int index = 0; index < Document.nodes.Count; index++)
            {
                var node = Document.nodes[index]; if (!node.mesh || !affected.Contains(node.mesh)) continue;
                var edit = plans.First(p => p.Mesh == node.mesh);
                using (new ComponentTargetScope(this, index))
                {
                    if (faceNode == node && cachedFaces != null && cachedFaces.Count == edit.FaceSlots.Length)
                        for (int f = 0; f < cachedFaces.Count; f++) cachedFaces[f].Submesh = edit.FaceSlots[f];
                    else { faceNode = null; cachedFaces = null; cachedFaceTriangles = null; }
                    cutTopology = null; cutMesh = null;
                }
                statisticsCache.Remove(node); undoMeshVersions[node.mesh] = EditorUtility.GetDirtyCount(node.mesh);
            }
            foreach (var mesh in affected) DirtySubdivision(mesh);
            UpdateSubdivision(); ApplyDisplayMaterials(affected); Changed(false); repaint();
            if (!addOnly) RememberAction(RepeatKind.MaterialSlot, new Vector4(slot, 0, 0, 0));
            error = remove ? "Material slot " + slot + " removed; its faces now use Slot 0." : addOnly ? "Material slot " + slot + " added." : "Selection assigned to material slot " + slot + ".";
        }

        private bool materialChannelsExpanded = true;
        private static readonly GUIContent MaterialChannelHeader = new GUIContent("Materials", EditorGUIUtility.IconContent("Material Icon").image);
        private void DrawMaterialChannels()
        {
            if (!Document || Active<0 || !ActiveMesh) return;
            var node=Document.nodes[Active];
            GUILayout.Space(4);
            var rect=EditorGUILayout.GetControlRect(false,20);
            EditorGUI.DrawRect(new Rect(rect.x-4,rect.y,rect.width+8,rect.height),Color.Lerp(StudioTheme.Current.Panel,StudioTheme.Current.Button,.5f));
            bool expanded=DrawChannelCategoryFoldout(rect, materialChannelsExpanded, MaterialChannelHeader);
            if(expanded!=materialChannelsExpanded)
            {
                materialChannelsExpanded=expanded;
                StudioPreferenceStore.SetFloat("Model.MaterialChannelsExpanded",expanded?1:0);
                repaint();
            }
            if(!expanded)return;
            using(new EditorGUILayout.VerticalScope(StudioTheme.Styles.channelTransformInset))
            {
            int depth=0;
            for(int slot=0;slot<Math.Max(ActiveMesh.subMeshCount,node.materials?.Length??0);slot++)
            {
                var material=node.materials!=null && slot<node.materials.Length ? node.materials[slot] : null;
                using(new EditorGUILayout.HorizontalScope())
                {
                EditorGUI.BeginChangeCheck();
                var next=(Material)EditorGUILayout.ObjectField("Slot "+slot,material,typeof(Material),false);
                int target=slot;
                if(EditorGUI.EndChangeCheck())Run(()=>AssignMaterial(Active,target,next));
                using(new EditorGUI.DisabledScope(!CanAssignMaterialSlot))
                    if(GUILayout.Button(new GUIContent("Assign","Assign selected objects or faces to slot "+target),GUILayout.Width(52)))Run(()=>AssignSelectionMaterialSlot(target));
                using(new EditorGUI.DisabledScope(Math.Max(ActiveMesh.subMeshCount,node.materials?.Length??0)<=1))
                    if(GUILayout.Button(new GUIContent("−","Remove slot "+target+". Faces using it move to Slot 0; later slot IDs shift down."),GUILayout.Width(22)))Run(()=>RemoveMaterialSlot(target));
                }
                if(IsGodGrass(material) && material.GetTexture("_GrassBaseArray") is Texture2DArray array)depth=Math.Max(depth,material.HasProperty("_GrassTextureSlices03") ? 8 : array.depth);
            }
            if(GUILayout.Button("Add Slot"))Run(AddMaterialSlot);
            if(depth>0)
            {
                EditorGUI.BeginChangeCheck();int slice=EditorGUILayout.IntSlider("Preview Sub-ID",node.previewTextureSlice,0,depth-1);
                if(EditorGUI.EndChangeCheck()) { Undo.RecordObject(Document,"Preview material sub-ID");node.previewTextureSlice=slice;ApplyDisplayMaterials();Changed(false); }
                EditorGUILayout.HelpBox("Previews the selected Sub-ID on the authored mesh, including shared artwork and per-ID shading. Terrain wind and shape deformation are disabled in this modeling preview.",MessageType.None);
            }
            }
        }

        private static void ConfigureTexturePreview(Material preview,Material source,int slice)
        {
            if(!source)return;
            bool grass=IsGodGrass(source);
            float Get(string property,float fallback) => source.HasFloat(property)?source.GetFloat(property):fallback;
            bool arrays=grass && Get("_GrassUseArrays",0)>.5f && source.GetTexture("_GrassBaseArray") is Texture2DArray;
            if(arrays)
            {
                var array=(Texture2DArray)source.GetTexture("_GrassBaseArray");
                // Keep the authored ID for per-ID shading; remap only the artwork lookup.
                slice=Mathf.Clamp(slice,0,7);
                int artworkSlice=slice;
                if(source.HasProperty("_GrassTextureSlices03") && source.HasProperty("_GrassTextureSlices47"))
                {
                    var mapping=source.GetVector(slice<4 ? "_GrassTextureSlices03" : "_GrassTextureSlices47");
                    artworkSlice=Mathf.RoundToInt(mapping[slice%4]);
                }
                artworkSlice=Mathf.Clamp(artworkSlice,0,array.depth-1);
                preview.SetTexture("_BaseArray",array);preview.SetFloat("_UseArray",1);preview.SetFloat("_ArraySlice",artworkSlice);
                // Array sampling in GodGrass uses the authored UV, without legacy MainTex tiling.
                preview.SetTextureScale("_MainTex",Vector2.one);preview.SetTextureOffset("_MainTex",Vector2.zero);
            }
            bool perLayer=grass && Get("_GrassUseLayerParameters",0)>.5f;
            string Property(string global) { string layer="_GrassLayer"+slice.ToString("00")+"_"+global.TrimStart('_');return perLayer&&source.HasProperty(layer)?layer:global; }
            if(grass)
            {
                if(source.HasProperty(Property("_GrassAlbedoTint")))preview.SetColor("_Color",source.GetColor(Property("_GrassAlbedoTint")));
                preview.SetFloat("_Brightness",Get(Property("_GrassBrightness"),1));
                preview.SetFloat("_Contrast",Get(Property("_GrassContrast"),1));
                preview.SetFloat("_Saturation",Get(Property("_GrassSaturation"),1));
                preview.SetFloat("_NormalStrength",Get(Property("Vector1_58397015"),1));
                preview.SetFloat("_VertexAOStrength",Get(Property("_GrassVertexAOStrength"),0));
            }
            bool cutout=grass || source.IsKeywordEnabled("_ALPHATEST_ON") || source.GetTag("RenderType",false)=="TransparentCutout" || Get("_AlphaCutoffEnable",0)>.5f || Get("_AlphaClip",0)>.5f;
            if(cutout)preview.SetFloat("_Cutoff",Get(Property("_AlphaCutoff"),Get("_Cutoff",.5f)));
            if(Get("_DoubleSidedEnable",0)>.5f)preview.SetFloat("_Cull",0);
            if(arrays && source.GetTexture("_GrassNormalArray") is Texture2DArray normals)
            { preview.SetTexture("_NormalArray",normals);preview.SetFloat("_UseNormal",2); }
            else foreach(string name in new[]{"_NormalMap","_BumpMap","Texture2D_364700D4"})
                if(source.HasTexture(name)&&source.GetTexture(name) is Texture2D normal)
                { preview.SetTexture("_NormalTex",normal);preview.SetFloat("_UseNormal",1);break; }
        }
    }
}
