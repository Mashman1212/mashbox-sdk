using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private List<Mesh> clothRemovedMeshes = new List<Mesh>();
        [SerializeField] private List<Mesh> clothHiddenMeshes = new List<Mesh>();
        [SerializeField] private bool clothDefaultClothingInitialized;
        [SerializeField] private List<MashBoxClothProfile> clothProfiles = new List<MashBoxClothProfile>();
        private StudioSkinningDocument.Part[] clothMeshRows = Array.Empty<StudioSkinningDocument.Part>();
        private GameObject[] clothMeshSources = Array.Empty<GameObject>();
        private bool ClothEditable => ClothRenderer && clothRendererIndex < clothMeshRows.Length && !clothMeshRows[clothRendererIndex].referenceOnly;
        private bool ClothVisible => ClothEditable && ClothRenderer.gameObject.activeSelf;

        private GameObject[] ClothSources(out HashSet<Mesh> bodyMeshes, out HashSet<Mesh> bustMeshes)
        {
            var defaults = StudioDefaultReferences.LoadSkinning(StudioDefaultReferences.SkinningPath);
            if (!defaults || !defaults.body || !defaults.bust)
                throw new InvalidOperationException("The default Skinning body and bust could not be loaded.");
            bodyMeshes = ClothSourceMeshes(defaults.body);
            bustMeshes = ClothSourceMeshes(defaults.bust);
            var sources = new List<GameObject> { defaults.body, defaults.bust };
            if (!clothDefaultClothingInitialized)
            {
                foreach (var source in new[] { defaults.pants, defaults.shirt }.Where(s => s))
                    foreach (var mesh in ClothSourceMeshes(source))
                        if (!clothHiddenMeshes.Contains(mesh)) clothHiddenMeshes.Add(mesh);
                clothDefaultClothingInitialized = true;
            }
            // The old single-garment field overrides its stored Animation outfit.
            var garments = clothGarment ? new[] { clothGarment } : clothOutfit ?? Array.Empty<GameObject>();
            var garmentMeshes = new HashSet<Mesh>(garments.Where(g=>g).SelectMany(ClothSourceMeshes));
            // An explicit drop promotes default clothing into an editable target.
            // Do not also instantiate a locked copy of the same garment mesh.
            foreach(var reference in new[]{defaults.pants,defaults.shirt,defaults.hair,defaults.hat}.Where(g=>g))
                if(!ClothSourceMeshes(reference).Overlaps(garmentMeshes))sources.Add(reference);
            sources.AddRange(garments);
            return sources.Where(s => s).Distinct().ToArray();
        }

        private static HashSet<Mesh> ClothSourceMeshes(GameObject source) => new HashSet<Mesh>(source
            .GetComponentsInChildren<Renderer>(true).Select(MashBoxClothSession.GetMesh).Where(m => m));

        private void BuildClothMeshRows(GameObject[] sources, HashSet<Mesh> bodyMeshes, HashSet<Mesh> bustMeshes)
        {
            var defaults = StudioDefaultReferences.LoadSkinning(StudioDefaultReferences.SkinningPath);
            var pantsMeshes = defaults && defaults.pants ? ClothSourceMeshes(defaults.pants) : new HashSet<Mesh>();
            var shirtMeshes = defaults && defaults.shirt ? ClothSourceMeshes(defaults.shirt) : new HashSet<Mesh>();
            var hairMeshes = defaults && defaults.hair ? ClothSourceMeshes(defaults.hair) : new HashSet<Mesh>();
            var hatMeshes = defaults && defaults.hat ? ClothSourceMeshes(defaults.hat) : new HashSet<Mesh>();
            var garments=clothGarment?new[]{clothGarment}:clothOutfit??Array.Empty<GameObject>();
            var editableMeshes=new HashSet<Mesh>(garments.Where(g=>g).SelectMany(ClothSourceMeshes));
            var sourceMeshes = sources.Select(ClothSourceMeshes).ToArray();
            // A skeleton prefab may contain its own preview geometry. Only explicitly
            // imported models belong to this workspace; keep bone transforms active.
            var included = new HashSet<Mesh>(sourceMeshes.SelectMany(meshes => meshes));
            included.ExceptWith(clothRemovedMeshes);
            foreach (var renderer in clothRenderers)
                if (!included.Contains(MashBoxClothSession.GetMesh(renderer))) renderer.enabled = false;
            clothRenderers = clothRenderers.Where(r => included.Contains(MashBoxClothSession.GetMesh(r))).ToArray();
            clothRenderers = clothRenderers.OrderBy(r => bodyMeshes.Contains(MashBoxClothSession.GetMesh(r)) ? 0
                : bustMeshes.Contains(MashBoxClothSession.GetMesh(r)) ? 1
                : pantsMeshes.Contains(MashBoxClothSession.GetMesh(r)) ? 2
                : shirtMeshes.Contains(MashBoxClothSession.GetMesh(r)) ? 3 : 4).ToArray();
            clothMeshRows = new StudioSkinningDocument.Part[clothRenderers.Length];
            clothMeshSources = new GameObject[clothRenderers.Length];
            for (int i = 0; i < clothRenderers.Length; i++)
            {
                var renderer = clothRenderers[i];
                var mesh = MashBoxClothSession.GetMesh(renderer);
                bool isBody = bodyMeshes.Contains(mesh), isBust = bustMeshes.Contains(mesh);
                bool isPants = pantsMeshes.Contains(mesh), isShirt = shirtMeshes.Contains(mesh);
                bool isHair=hairMeshes.Contains(mesh), isHat=hatMeshes.Contains(mesh);
                bool lockedClothing=(isPants||isShirt||isHair||isHat)&&!editableMeshes.Contains(mesh);
                int source = Array.FindIndex(sourceMeshes, set => set.Contains(mesh));
                clothMeshSources[i] = source >= 0 ? sources[source] : null;
                clothMeshRows[i] = new StudioSkinningDocument.Part {
                    name = renderer.name.Replace("Preview • ", ""), mesh = mesh, sourceMesh = mesh,
                    referenceOnly = isBody || isBust || lockedClothing, bound = renderer is SkinnedMeshRenderer,
                    defaultReferenceLabel = lockedClothing ? (isHair ? "Default Hair" : isHat ? "Default Hat" : isPants ? "Default Pants" : "Default Shirt") : null,
                    role = isHair || isHat ? StudioSkinningDocument.MeshRole.Headgear : isBody ? StudioSkinningDocument.MeshRole.Body : isBust ? StudioSkinningDocument.MeshRole.Bust : StudioSkinningDocument.MeshRole.Clothing,
                    visible = !clothHiddenMeshes.Contains(mesh)
                };
                renderer.gameObject.SetActive(clothMeshRows[i].visible);
            }
        }

        private void RememberClothProfile()
        {
            if (!clothProfile) return;
            clothProfiles.RemoveAll(p => !p || p == clothProfile || p.Matches(clothProfile.sourceMesh));
            clothProfiles.Add(clothProfile);
        }

        private void SelectClothMesh(int index)
        {
            if (index < 0 || index >= clothMeshRows.Length || clothMeshRows[index].referenceOnly) return;
            EndClothStroke(); StopClothSession(); RememberClothProfile();
            clothPainting = false; clothRendererIndex = index;
            clothProfile = clothProfiles.Find(p => p && p.Matches(clothMeshRows[index].sourceMesh));
            RefreshClothPickMesh();
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }

        private bool EnsureClothModelsReadable(IEnumerable<GameObject> models)
        {
            // Resolve mesh assets rather than prefab roots: a garment prefab
            // can reference meshes from several different imported models.
            var paths = models.Where(model => model).SelectMany(ClothSourceMeshes)
                .Where(mesh => mesh && !mesh.isReadable)
                .Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path)).Distinct().ToArray();
            bool changed = false;
            foreach (var path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (!importer || importer.isReadable) continue;
                try
                {
                    EndClothStroke(); StopClothSession();
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    changed = true;
                }
                catch (Exception e)
                {
                    // Keep the garment available and retain the manual repair
                    // control when an importer cannot be changed or reimported.
                    clothStatus = "Could not automatically enable Read/Write for " + path + ": " + e.GetBaseException().Message;
                }
            }
            return changed;
        }

        private void AddClothModels(IEnumerable<GameObject> models)
        {
            ClothSources(out var bodyMeshes, out var bustMeshes);
            var garments = new List<GameObject>(clothGarment ? new[] { clothGarment } : clothOutfit ?? Array.Empty<GameObject>());
            var requested = models.Where(g => g && EditorUtility.IsPersistent(g))
                .Distinct().Where(g => { var meshes = ClothSourceMeshes(g); return meshes.Count > 0 && !meshes.Overlaps(bodyMeshes) && !meshes.Overlaps(bustMeshes); }).ToArray();
            if(requested.Length==0)return;
            var additions=requested.Where(g=>!garments.Contains(g)).ToArray();
            bool readabilityChanged = additions.Length == 0 && EnsureClothModelsReadable(requested);
            RememberClothProfile();
            var previousGarment = clothGarment;var previousOutfit = clothOutfit;
            var previousHidden=clothHiddenMeshes.ToArray();
            var requestedMeshes=new HashSet<Mesh>(requested.SelectMany(ClothSourceMeshes));
            clothHiddenMeshes.RemoveAll(mesh=>requestedMeshes.Contains(mesh));
            bool restoring=clothRemovedMeshes.RemoveAll(mesh=>requestedMeshes.Contains(mesh))>0;
            garments.AddRange(additions);
            if(additions.Length>0 || readabilityChanged || restoring)
            {
                clothGarment = null; clothOutfit = garments.Distinct().ToArray();
                try { BuildClothPreview(); }
                catch { clothGarment = previousGarment;clothOutfit = previousOutfit;clothHiddenMeshes.Clear();clothHiddenMeshes.AddRange(previousHidden);throw; }
            }
            int selected=Array.FindIndex(clothMeshSources,source=>source==requested[requested.Length-1]);
            if(selected<0)selected=Array.FindIndex(clothMeshRows,part=>!part.referenceOnly&&requestedMeshes.Contains(part.sourceMesh));
            for(int i=0;i<clothMeshRows.Length;i++)
                if(!clothMeshRows[i].referenceOnly&&requestedMeshes.Contains(clothMeshRows[i].sourceMesh))
                {clothMeshRows[i].visible=true;clothRenderers[i].gameObject.SetActive(true);}
            if(selected>=0)
            {
                if(selected!=clothRendererIndex)SelectClothMesh(selected);
            }
            if(additions.Length>0)FrameCloth();
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }

        private void HandleClothDrop()
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            var models = DragAndDrop.objectReferences.OfType<GameObject>().Where(EditorUtility.IsPersistent).ToArray();
            if (models.Length == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag(); ClothAction(() => AddClothModels(models));
            }
            evt.Use();
        }

        private void DrawClothMeshList()
        {
            HandleClothDrop();
            GUILayout.Label("Drop clothing or objects into the viewport.", EditorStyles.wordWrappedMiniLabel);
            clothTargetScroll = EditorGUILayout.BeginScrollView(clothTargetScroll);
            for (int i = 0; i < clothMeshRows.Length; i++)
            {
                var part = clothMeshRows[i];
                if (i > 0 && part.referenceOnly != clothMeshRows[i - 1].referenceOnly)
                {
                    GUILayout.Space(6);
                    EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), new Color(.2f, .22f, .24f, 1));
                    GUILayout.Space(6);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool visible = GUILayout.Toggle(part.visible, new GUIContent("", "Show or hide this mesh"), GUILayout.Width(18));
                    if (visible != part.visible)
                    {
                        // Dispose the simulation before hiding its source; output visibility must follow the row.
                        if (i == clothRendererIndex) { EndClothStroke(); StopClothSession(); clothPainting = false; }
                        part.visible = visible;
                        clothHiddenMeshes.Remove(part.sourceMesh);
                        if (!visible) clothHiddenMeshes.Add(part.sourceMesh);
                        clothRenderers[i].gameObject.SetActive(visible);
                        ClothAction(ApplyClothClipping);
                        viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
                    }
                    var row = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.ExpandWidth(true));
                    // Handle the context click before GUI.Toggle can consume right-button input.
                    if (row.Contains(Event.current.mousePosition) &&
                        (Event.current.type == EventType.ContextClick || (Event.current.type == EventType.MouseDown && Event.current.button == 1)))
                    {
                        var mesh=part.sourceMesh;
                        var menu=new GenericMenu();
                        if (!part.referenceOnly)
                            menu.AddItem(new GUIContent("Export cloth prefab…"),false,()=>ClothAction(()=>
                            {
                                int index=Array.FindIndex(clothMeshRows,p=>p.sourceMesh==mesh);
                                if(index<0)return;
                                if(index!=clothRendererIndex)SelectClothMesh(index);
                                ExportClothPrefab();
                            }));
                        menu.AddItem(new GUIContent("Remove mesh from studio"),false,()=>ClothAction(()=>RemoveClothMesh(mesh)));
                        menu.ShowAsContext();Event.current.Use();
                    }
                    if (!part.referenceOnly && GUI.Toggle(row, i == clothRendererIndex, GUIContent.none, "Button") && i != clothRendererIndex) SelectClothMesh(i);
                    StudioSkinningEditor.DrawMeshRoleRow(row, part, part.bound ? "Skinned mesh" : "Static mesh");
                }
                DrawClothMeshReferences(i);
            }
            EditorGUILayout.EndScrollView();
        }

        private void RemoveClothMesh(Mesh mesh)
        {
            if(!mesh || clothRemovedMeshes.Contains(mesh))return;
            EndClothStroke();RememberClothProfile();
            clothRemovedMeshes.Add(mesh);
            try { BuildClothPreview(); }
            catch { clothRemovedMeshes.Remove(mesh);throw; }
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }
        private void RestoreClothMeshes()
        {
            var removed=clothRemovedMeshes.ToArray();clothRemovedMeshes.Clear();
            try { BuildClothPreview(); }
            catch {clothRemovedMeshes.AddRange(removed);throw;}
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }

        private void RemoveClothModel(int index)
        {
            if (index < 0 || index >= clothMeshRows.Length || clothMeshRows[index].referenceOnly) return;
            var source = clothMeshSources[index];
            if (!source) return;
            RememberClothProfile();
            if (clothGarment == source) { clothGarment = null; clothOutfit = Array.Empty<GameObject>(); }
            clothOutfit = (clothOutfit ?? Array.Empty<GameObject>()).Where(g => g != source).ToArray();
            BuildClothPreview();
        }

        private void NewClothScene()
        {
            EndClothStroke(); RememberClothProfile();
            foreach (var profile in clothProfiles) if (profile) AssetDatabase.SaveAssetIfDirty(profile);
            clothCharacter = ResolveSdkSkeleton();
            clothGarment = null; clothOutfit = Array.Empty<GameObject>(); clothProfile = null; clothProfiles.Clear();
            clothMaterialAssignments.Clear(); clothClippingAssignments.Clear(); clothClippingPreview = 0; clothPreviewClipping = null;
            clothClip = null; clothCustomMotion = false; clothHiddenMeshes.Clear(); clothRemovedMeshes.Clear(); clothRendererIndex = -1;
            // A new Cloth scene starts with all four default references visible.
            clothDefaultClothingInitialized = true;
            clothSimulationTime = 0; clothTool = 0;
            BuildClothPreview(); SetClothPose(2); FrameCloth();
        }

        private void DrawClothFileMenu()
        {
            var rect = new Rect(0, 0, 48, 21);
            if (!EditorGUI.DropdownButton(rect, new GUIContent("File"), FocusType.Passive, StudioTheme.Styles.menuDropdown)) return;
            var menu = new StudioMenu();
            menu.AddItem(new GUIContent("New Scene"), false, () => ClothAction(NewClothScene));
            menu.AddItem(new GUIContent("Add Animation outfit"), false, () => ClothAction(() => AddClothModels(clothing ?? Array.Empty<GameObject>())));
            if(clothRemovedMeshes.Count>0)menu.AddItem(new GUIContent("Restore removed meshes"),false,()=>ClothAction(RestoreClothMeshes));
            menu.AddSeparator("");
            if(ClothEditable) menu.AddItem(new GUIContent("Export cloth prefab…"),false,()=>ClothAction(ExportClothPrefab));
            else menu.AddDisabledItem(new GUIContent("Export cloth prefab…"));
            menu.DropDown(rect);
        }
    }
}
