using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Cloth;
using MashBoxSDK.Culling;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [Serializable] private sealed class ClothClippingAssignment
        {
            public Mesh mesh;
            public ClothingCullingProfile profile;
        }
        [SerializeField] private List<ClothClippingAssignment> clothClippingAssignments = new List<ClothClippingAssignment>();
        // 0: visible garments' assignments, 1: none, 2: explicit preview profile.
        [SerializeField] private int clothClippingPreview;
        [SerializeField] private ClothingCullingProfile clothPreviewClipping;
        private readonly Dictionary<int, Mesh> clothClippedMeshes = new Dictionary<int, Mesh>();
        private readonly HashSet<Mesh> clothMeshDetails = new HashSet<Mesh>();
        private ClothingCullingRegistry ClothClippingRegistry => AssetDatabase.LoadAssetAtPath<ClothingCullingRegistry>(
            "Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Culling/Default Clothing Culling.asset");

        internal static ClothingCullingProfile DetectClothClipping(Renderer source, ClothingCullingRegistry registry)
        {
            if (!source) return null;
            var settings = source.GetComponentInParent<ClothingCullingSettings>(true);
            if (settings) return settings.profile;
            var legacy = source.GetComponentsInParent<Component>(true).FirstOrDefault(c => c && c.GetType().FullName == "MashBoxSDK.Clothing.ClothingClipSettings");
            var field = legacy ? legacy.GetType().GetField("clipType") : null;
            return field != null && registry ? registry.Get(Convert.ToInt32(field.GetValue(legacy))) : null;
        }
        private ClothingCullingProfile ClothAssignedClipping(int index)
        {
            var mesh = clothMeshRows[index].sourceMesh;
            var assignment = clothClippingAssignments.Find(a => a.mesh == mesh);
            if (assignment != null) return assignment.profile;
            var source = clothMeshSources[index];
            var renderer = source ? source.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => MashBoxClothSession.GetMesh(r) == mesh) : null;
            return DetectClothClipping(renderer, ClothClippingRegistry);
        }
        private void SetClothClipping(int index, ClothingCullingProfile profile)
        {
            var mesh = clothMeshRows[index].sourceMesh;
            clothClippingAssignments.RemoveAll(a => a.mesh == mesh);
            clothClippingAssignments.Add(new ClothClippingAssignment { mesh = mesh, profile = profile });
            clothClippingPreview = 0;
            ApplyClothClipping();
        }
        private void DrawClothMeshReferences(int index)
        {
            var part = clothMeshRows[index]; var mesh = part.sourceMesh;
            bool open = EditorGUILayout.Foldout(clothMeshDetails.Contains(mesh), "Clipping & materials", true);
            if (open) clothMeshDetails.Add(mesh); else clothMeshDetails.Remove(mesh);
            if (!open) return;
            if (part.role == StudioSkinningDocument.MeshRole.Clothing)
            {
                var current = ClothAssignedClipping(index);
                EditorGUI.BeginChangeCheck();
                var next = (ClothingCullingProfile)EditorGUILayout.ObjectField(new GUIContent("Clipping data", "Preset or custom clipping profile; hides the body beneath this garment."), current, typeof(ClothingCullingProfile), false);
                if (EditorGUI.EndChangeCheck()) ClothAction(() => SetClothClipping(index, next));
                if (GUILayout.Button("Choose clipping preset...", EditorStyles.miniButton))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("None"), !current, () => ClothAction(() => SetClothClipping(index, null)));
                    foreach (var preset in ClothClippingRegistry ? ClothClippingRegistry.presets : Array.Empty<ClothingCullingProfile>())
                        if (preset) { var selected = preset; menu.AddItem(new GUIContent(preset.name), current == preset, () => ClothAction(() => SetClothClipping(index, selected))); }
                    menu.ShowAsContext();
                }
            }
            var materials = ClothAssignedMaterials(index);
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                var current = materials != null && slot < materials.Length ? materials[slot] : null;
                EditorGUI.BeginChangeCheck();
                var next = (Material)EditorGUILayout.ObjectField("Material " + (slot + 1), current, typeof(Material), false);
                if (EditorGUI.EndChangeCheck()) { int selectedSlot = slot; ClothAction(() => AssignClothMaterial(index, selectedSlot, next)); }
            }
        }
        private bool DrawClothClippingSelector()
        {
            var canvas = viewport.ModelCanvasRect;
            var rect = new Rect(canvas.x + 8, canvas.yMax - 70, Mathf.Min(245, canvas.width - 16), 22);
            if (rect.width < 60 || canvas.height < 100) return false;
            Handles.BeginGUI();
            string label = clothClippingPreview == 0 ? "Assigned clothing" : clothClippingPreview == 1 ? "None" : clothPreviewClipping ? clothPreviewClipping.name : "None";
            if (EditorGUI.DropdownButton(rect, new GUIContent("Body clipping: " + label), FocusType.Passive))
            {
                var menu = new GenericMenu();
                void Choose(int mode, ClothingCullingProfile profile) { clothClippingPreview = mode; clothPreviewClipping = profile; ClothAction(ApplyClothClipping); }
                menu.AddItem(new GUIContent("Assigned clothing"), clothClippingPreview == 0, () => Choose(0, null));
                menu.AddItem(new GUIContent("None"), clothClippingPreview == 1, () => Choose(1, null));
                foreach (var preset in ClothClippingRegistry ? ClothClippingRegistry.presets : Array.Empty<ClothingCullingProfile>())
                    if (preset) { var selected = preset; menu.AddItem(new GUIContent("Presets/" + preset.name), clothClippingPreview == 2 && clothPreviewClipping == preset, () => Choose(2, selected)); }
                foreach (var profile in Enumerable.Range(0, clothMeshRows.Length).Select(ClothAssignedClipping).Where(p => p).Distinct())
                { var selected = profile; menu.AddItem(new GUIContent("Referenced/" + profile.name), clothClippingPreview == 2 && clothPreviewClipping == profile, () => Choose(2, selected)); }
                menu.DropDown(rect);
            }
            Handles.EndGUI();
            return rect.Contains(Event.current.mousePosition) && Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout;
        }
        private void ReleaseClothClipping()
        {
            foreach (var entry in clothClippedMeshes)
            {
                if (entry.Key < clothRenderers.Length && clothRenderers[entry.Key])
                {
                    var source = clothMeshRows[entry.Key].sourceMesh;
                    if (clothRenderers[entry.Key] is SkinnedMeshRenderer skin) skin.sharedMesh = source;
                    else { var filter = clothRenderers[entry.Key].GetComponent<MeshFilter>(); if (filter) filter.sharedMesh = source; }
                }
                if (entry.Value) DestroyImmediate(entry.Value);
            }
            clothClippedMeshes.Clear();
        }
        private void ApplyClothClipping()
        {
            ReleaseClothClipping();
            var profiles = new List<ClothingCullingProfile>();
            if (clothClippingPreview == 2 && clothPreviewClipping) profiles.Add(clothPreviewClipping);
            else if (clothClippingPreview == 0)
                for (int i = 0; i < clothMeshRows.Length; i++)
                    if (clothMeshRows[i].visible && clothMeshRows[i].role == StudioSkinningDocument.MeshRole.Clothing)
                    { var profile = ClothAssignedClipping(i); if (profile && !profiles.Contains(profile)) profiles.Add(profile); }
            try
            {
                for (int i = 0; i < clothMeshRows.Length; i++)
                {
                    var part = clothMeshRows[i];
                    if (part.role != StudioSkinningDocument.MeshRole.Body && part.role != StudioSkinningDocument.MeshRole.Bust) continue;
                    var region = part.role == StudioSkinningDocument.MeshRole.Body ? CullingRegion.Body : CullingRegion.Bust;
                    var masks = profiles.SelectMany(p => p.masks ?? Array.Empty<CullingMask>()).Where(m => m != null && m.surface && m.surface.region == region).ToArray();
                    if (masks.Length == 0) continue;
                    var source = part.sourceMesh;
                    var names = clothRenderers[i] is SkinnedMeshRenderer skin ? skin.bones.Select(b => b ? b.name : "").ToArray() : Array.Empty<string>();
                    var hidden = new bool[source.triangles.Length / 3];
                    foreach (var mask in masks)
                    {
                        var covered = new ClothingCullingMap(mask.surface, source, names).Evaluate(mask);
                        for (int t = 0; t < hidden.Length && t < covered.Length; t++) hidden[t] |= covered[t];
                    }
                    var copy = Instantiate(source); copy.name = source.name + " (cloth clipping preview)"; copy.hideFlags = HideFlags.HideAndDontSave;
                    clothClippedMeshes.Add(i, copy);
                    ClothingCullingMap.Apply(copy, source, hidden);
                    if (clothRenderers[i] is SkinnedMeshRenderer target) target.sharedMesh = copy;
                    else clothRenderers[i].GetComponent<MeshFilter>().sharedMesh = copy;
                }
            }
            catch { ReleaseClothClipping(); throw; }
            viewport?.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
        }
    }
}
