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
            var sources = new List<GameObject> { defaults.body, defaults.bust, defaults.pants, defaults.shirt };
            if (!clothDefaultClothingInitialized)
            {
                foreach (var source in new[] { defaults.pants, defaults.shirt }.Where(s => s))
                    foreach (var mesh in ClothSourceMeshes(source))
                        if (!clothHiddenMeshes.Contains(mesh)) clothHiddenMeshes.Add(mesh);
                clothDefaultClothingInitialized = true;
            }
            // The old single-garment field overrides its stored Animation outfit.
            if (clothGarment) sources.Add(clothGarment);
            else sources.AddRange(clothOutfit ?? Array.Empty<GameObject>());
            return sources.Where(s => s).Distinct().ToArray();
        }

        private static HashSet<Mesh> ClothSourceMeshes(GameObject source) => new HashSet<Mesh>(source
            .GetComponentsInChildren<Renderer>(true).Select(MashBoxClothSession.GetMesh).Where(m => m));

        private void BuildClothMeshRows(GameObject[] sources, HashSet<Mesh> bodyMeshes, HashSet<Mesh> bustMeshes)
        {
            var defaults = StudioDefaultReferences.LoadSkinning(StudioDefaultReferences.SkinningPath);
            var pantsMeshes = defaults && defaults.pants ? ClothSourceMeshes(defaults.pants) : new HashSet<Mesh>();
            var shirtMeshes = defaults && defaults.shirt ? ClothSourceMeshes(defaults.shirt) : new HashSet<Mesh>();
            var sourceMeshes = sources.Select(ClothSourceMeshes).ToArray();
            // A skeleton prefab may contain its own preview geometry. Only explicitly
            // imported models belong to this workspace; keep bone transforms active.
            var included = new HashSet<Mesh>(sourceMeshes.SelectMany(meshes => meshes));
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
                int source = Array.FindIndex(sourceMeshes, set => set.Contains(mesh));
                clothMeshSources[i] = source >= 0 ? sources[source] : null;
                clothMeshRows[i] = new StudioSkinningDocument.Part {
                    name = renderer.name.Replace("Preview • ", ""), mesh = mesh, sourceMesh = mesh,
                    referenceOnly = isBody || isBust || isPants || isShirt, bound = renderer is SkinnedMeshRenderer,
                    defaultReferenceLabel = isPants ? "Default Pants" : isShirt ? "Default Shirt" : null,
                    role = isBody ? StudioSkinningDocument.MeshRole.Body : isBust ? StudioSkinningDocument.MeshRole.Bust : StudioSkinningDocument.MeshRole.Clothing,
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

        private void AddClothModels(IEnumerable<GameObject> models)
        {
            var existing = ClothSources(out var bodyMeshes, out var bustMeshes).ToList();
            var additions = models.Where(g => g && EditorUtility.IsPersistent(g) && !existing.Contains(g))
                .Distinct().Where(g => { var meshes = ClothSourceMeshes(g); return meshes.Count > 0 && !meshes.Overlaps(bodyMeshes) && !meshes.Overlaps(bustMeshes); }).ToArray();
            if (additions.Length == 0) return;
            RememberClothProfile();
            var garments = new List<GameObject>(clothGarment ? new[] { clothGarment } : clothOutfit ?? Array.Empty<GameObject>());
            garments.AddRange(additions);
            clothGarment = null; clothOutfit = garments.Distinct().ToArray();
            BuildClothPreview();
            SelectClothMesh(Array.FindIndex(clothMeshSources, source => source == additions[additions.Length - 1]));
            FrameCloth();
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
                        viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
                    }
                    var row = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.ExpandWidth(true));
                    if (!part.referenceOnly && GUI.Toggle(row, i == clothRendererIndex, GUIContent.none, "Button") && i != clothRendererIndex) SelectClothMesh(i);
                    StudioSkinningEditor.DrawMeshRoleRow(row, part, part.bound ? "Skinned mesh" : "Static mesh");
                    if (!part.referenceOnly && Event.current.type == EventType.ContextClick && row.Contains(Event.current.mousePosition))
                    {
                        int index = i;
                        var menu = new GenericMenu();
                        if (clothMeshSources[index]) menu.AddItem(new GUIContent("Remove model"), false, () => ClothAction(() => RemoveClothModel(index)));
                        menu.ShowAsContext(); Event.current.Use();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
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
            clothMaterialAssignments.Clear();
            clothClip = null; clothCustomMotion = false; clothHiddenMeshes.Clear(); clothRendererIndex = -1;
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
            menu.DropDown(rect);
        }
    }
}
