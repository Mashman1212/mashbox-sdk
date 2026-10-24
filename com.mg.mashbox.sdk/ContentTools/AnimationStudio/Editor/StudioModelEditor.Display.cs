using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private void SaveDisplayConfiguration()
        {
            StudioPreferenceStore.SetFloat("Model.ThroughSelection", xray ? 1 : 0);
            StudioPreferenceStore.SetFloat("Model.PreventNegativeScale", preventNegativeScale ? 1 : 0);
            StudioPreferenceStore.SetFloat("Model.LocalAxes", local ? 1 : 0);
            StudioPreferenceStore.SetFloat("Model.Lighting", displayLighting ? 1 : 0);
            StudioPreferenceStore.SetFloat("Model.Textures", displayTextures ? 1 : 0);
            StudioPreferenceStore.SetFloat("Model.XRay", displayXray ? 1 : 0);
            StudioPreferenceStore.SetFloat("Model.BackfaceCulling", backfaceCulling ? 1 : 0);
            StudioPreferenceStore.SetFloat("Model.WireframeOnly", wireframeOnly ? 1 : 0);
        }
        private void LoadDisplayConfiguration()
        {
            xray = StudioPreferenceStore.GetFloat("Model.ThroughSelection", 1) >= .5f;
            preventNegativeScale = StudioPreferenceStore.GetFloat("Model.PreventNegativeScale", 1) >= .5f;
            local = StudioPreferenceStore.GetFloat("Model.LocalAxes", 0) >= .5f;
            displayLighting = StudioPreferenceStore.GetFloat("Model.Lighting", 1) >= .5f;
            displayTextures = StudioPreferenceStore.GetFloat("Model.Textures", 1) >= .5f;
            displayXray = StudioPreferenceStore.GetFloat("Model.XRay", 0) >= .5f;
            backfaceCulling = StudioPreferenceStore.GetFloat("Model.BackfaceCulling", 1) >= .5f;
            wireframeOnly = StudioPreferenceStore.GetFloat("Model.WireframeOnly", 0) >= .5f;
            ApplyDisplayMaterials();
        }
        private bool displayLighting = true, displayTextures = true;
        private bool displayXray;
        private bool backfaceCulling = true;
        internal bool BackfaceCulling => backfaceCulling;
        internal void SetBackfaceCulling(bool enabled)
        {
            if (backfaceCulling == enabled) return;
            backfaceCulling = enabled;
            SaveDisplayConfiguration(); ApplyDisplayMaterials(); repaint();
        }
        private readonly Dictionary<Material, Material> culledSourceMaterials = new Dictionary<Material, Material>();
        private static readonly string[] DisplayCullProperties = { "_Cull", "_CullMode", "_CullModeForward", "_TransparentCullMode" };
        private CommandBuffer blackBackfaceCommands;
        private readonly List<(Renderer renderer, int submesh, Material material)> blackBackfaceDraws = new List<(Renderer, int, Material)>();
        private readonly Dictionary<Material, Material> blackBackfaceMaterials = new Dictionary<Material, Material>();
        private void AddBlackBackfaces(Renderer renderer, Material[] materials)
        {
            if (backfaceCulling || wireframeOnly || displayXray) return;
            var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (!mesh || materials.Length == 0) return;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var source = materials[Math.Min(sub, materials.Length - 1)];
                if (!source || source.HasProperty("_BlackBackfaces")) continue;
                if (!blackBackfaceMaterials.TryGetValue(source, out var black))
                {
                    black = NewPreviewMaterial(source);
                    black.name = "Studio black back faces";
                    black.SetFloat("_Cull", (float)CullMode.Front);
                    black.SetFloat("_ZWrite", 0);
                    if (source.HasProperty("_Cutoff")) black.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
                    else if (source.HasProperty("_AlphaCutoff")) black.SetFloat("_Cutoff", source.GetFloat("_AlphaCutoff"));
                    blackBackfaceMaterials.Add(source, black);
                }
                if (blackBackfaceCommands == null) blackBackfaceCommands = new CommandBuffer { name = "Studio black back faces" };
                blackBackfaceDraws.Add((renderer, sub, black));
            }
        }
        private void DrawBlackBackfaces()
        {
            if (blackBackfaceCommands == null) return;
            blackBackfaceCommands.Clear();
            foreach (var draw in blackBackfaceDraws)
                if (draw.renderer && draw.renderer.enabled && !draw.renderer.forceRenderingOff && draw.renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly && draw.renderer.gameObject.activeInHierarchy)
                    blackBackfaceCommands.DrawRenderer(draw.renderer, draw.material, draw.submesh, 0);
            Graphics.ExecuteCommandBuffer(blackBackfaceCommands);
        }
        private void ConfigureDisplayCulling(Material material)
        {
            if (material.HasProperty("_BlackBackfaces")) material.SetFloat("_BlackBackfaces", 1);
            foreach (var property in DisplayCullProperties)
                if (material.HasProperty(property)) material.SetFloat(property, (float)(backfaceCulling ? CullMode.Back : CullMode.Off));
            if (material.HasProperty("_DoubleSidedEnable"))
            {
                material.SetFloat("_DoubleSidedEnable", backfaceCulling ? 0 : 1);
                if (backfaceCulling) material.DisableKeyword("_DOUBLESIDED_ON");
                else material.EnableKeyword("_DOUBLESIDED_ON");
            }
        }
        private Material CulledSourceMaterial(Material source)
        {
            if (!culledSourceMaterials.TryGetValue(source, out var material))
            {
                material = new Material(source) { name = source.name + " (Studio preview)", hideFlags = HideFlags.HideAndDontSave };
                ConfigureDisplayCulling(material);
                culledSourceMaterials.Add(source, material);
            }
            return material;
        }
        private bool wireframeOnly;
        internal DrawCameraMode ModelDrawMode => wireframeOnly ? DrawCameraMode.Wireframe :
            StudioSettings.Current.hideObjectWireframe ? DrawCameraMode.Textured : DrawCameraMode.TexturedWire;
        // Shared on/off semantics for toolbar buttons, menu clicks and shortcuts.
        // Wireframe-only returns to shaded wireframe; shaded wireframe returns to shaded.
        internal void SetModelDrawModeEnabled(SceneView view, DrawCameraMode mode, bool enabled)
        {
            SetModelDrawMode(view, enabled ? mode :
                mode == DrawCameraMode.TexturedWire ? DrawCameraMode.Textured : DrawCameraMode.TexturedWire);
        }
        internal void SetModelDrawMode(SceneView view, DrawCameraMode mode)
        {
            wireframeOnly = mode == DrawCameraMode.Wireframe;
            StudioSettings.Current.hideObjectWireframe = mode == DrawCameraMode.Textured;
            SaveDisplayConfiguration(); StudioSettings.Save();
            // Native wire modes add hard rasterized lines and force triangle wire drawing.
            // Studio owns its edge overlay; the camera supplies shaded color or depth only.
            var cameraMode = DrawCameraMode.Textured;
            if (view.cameraMode.drawMode != cameraMode) view.cameraMode = SceneView.GetBuiltinCameraMode(cameraMode);
            ApplyDisplayMaterials(); repaint(); view.Repaint();
        }
        internal void ConfigureModelDisplay(SceneView view)
        {
            if (view is MashBoxStudioViewport studio && studio.NativeToolbarsEnabled) return;
            var mode = view.cameraMode.drawMode;
            if (mode == DrawCameraMode.Wireframe || mode == DrawCameraMode.TexturedWire) SetModelDrawMode(view, mode);
        }
        internal bool XRayMode => displayXray;
        internal void SetXRayMode(bool enabled)
        {
            if (displayXray == enabled) return;
            displayXray = enabled; SaveDisplayConfiguration(); ApplyDisplayMaterials(); repaint();
        }
        private readonly Dictionary<(Material source, int slice), Material> previewMaterials = new Dictionary<(Material source, int slice), Material>();
        private Material neutralLit, neutralPreview;

        internal bool ToolbarLocalAxes => local;
        internal bool ToolbarLighting => displayLighting;
        internal bool ToolbarTextures => displayTextures;
        internal void SetToolbarLocalAxes(bool value)
        {
            if (local == value) return;
            local = value; SaveDisplayConfiguration(); repaint(); toolOptions?.Repaint();
        }
        internal void SetDisplayMode(bool lighting, bool textures)
        {
            displayLighting = lighting; displayTextures = textures; SaveDisplayConfiguration();
            ApplyDisplayMaterials(); repaint();
        }
        private void ApplyDisplayMaterials()
        {
            ClearDisplayMaterials();
            if (!Document) return;
            for(int i=0;i<Transforms.Length;i++)
            {
                var renderer = Transforms[i] ? Transforms[i].GetComponent<Renderer>() : null;
                if (!renderer) continue;
                // A depth-only surface still occludes the grid and rear edges.
                // Pure wireframe renders only Studio's edge overlay; keep the
                // renderer enabled for picking and restore surfaces in shaded modes.
                renderer.forceRenderingOff = wireframeOnly;
                var node = Document.nodes[i];
                var originals = node.materials ?? Array.Empty<Material>();
                int slots = Math.Max(originals.Length,node.mesh ? node.mesh.subMeshCount : 1);
                var materials = new Material[slots];
                for(int slot=0;slot<slots;slot++)
                    materials[slot] = DisplayMaterial(slot<originals.Length ? originals[slot] : null, node.previewTextureSlice);
                renderer.sharedMaterials = materials;
                AddBlackBackfaces(renderer, materials);
            }
        }
        private static bool IsGodGrass(Material source) => source && source.HasProperty("_GrassBaseArray");
        private Material DisplayMaterial(Material source, int slice = 0)
        {
            // Editable geometry already contains its pose; terrain deformation must not run again.
            if (!displayXray && !wireframeOnly && LightingSettings.studioLights && displayLighting && displayTextures && !IsGodGrass(source) && source) return CulledSourceMaterial(source);
            if (!displayXray && !wireframeOnly && displayLighting && LightingSettings.studioLights && !displayTextures)
            {
                if (!neutralLit)
                {
                    var pipeline = GraphicsSettings.currentRenderPipeline;
                    var template = pipeline ? pipeline.defaultMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                    neutralLit = new Material(template) { name="Studio Neutral", hideFlags=HideFlags.HideAndDontSave };
                    foreach(var property in new[] {"_BaseColor","_Color"}) if(neutralLit.HasProperty(property)) neutralLit.SetColor(property,new Color(.65f,.65f,.65f));
                    foreach(var property in new[] {"_Smoothness","_Glossiness","_Metallic"}) if(neutralLit.HasProperty(property)) neutralLit.SetFloat(property,0);
                    ConfigureDisplayCulling(neutralLit);
                }
                return neutralLit;
            }
            if (!displayTextures || !source)
            {
                if (!neutralPreview) neutralPreview = NewPreviewMaterial(null);
                return neutralPreview;
            }
            if (!previewMaterials.TryGetValue((source,slice),out var material))
            { material = NewPreviewMaterial(source,slice); previewMaterials.Add((source,slice),material); }
            return material;
        }
        private Material NewPreviewMaterial(Material source, int slice = 0)
        {
            var material = new Material(Shader.Find("Hidden/MashBox/AnimationStudioPreview")) { name=displayLighting ? "Studio Camera Light" : "Studio Unlit", hideFlags=HideFlags.HideAndDontSave };
            material.SetFloat("_Unlit",displayLighting ? 0 : 1);
            material.SetFloat("_CameraLighting",1);
            Color color = new Color(.65f,.65f,.65f);
            if(source)
            {
                color = Color.white;
                foreach(var property in new[] {"_BaseColor","_UnlitColor","_Color"})
                    if(source.HasProperty(property)) { color=source.GetColor(property); break; }
                foreach(var property in new[] {"_BaseColorMap","_BaseMap","_UnlitColorMap","_MainTex"})
                    if(source.HasProperty(property) && source.GetTexture(property)) {
                        material.SetTexture("_MainTex",source.GetTexture(property));
                        material.SetTextureScale("_MainTex",source.GetTextureScale(property));
                        material.SetTextureOffset("_MainTex",source.GetTextureOffset(property)); break;
                    }
                if(source.HasProperty("_Cull")) material.SetFloat("_Cull",source.GetFloat("_Cull"));
                else if(source.HasProperty("_CullMode")) material.SetFloat("_Cull",source.GetFloat("_CullMode"));
            }
            material.SetColor("_Color",color);
            ConfigureTexturePreview(material,source,slice);
            if (wireframeOnly && !displayXray) material.SetFloat("_ColorMask", 0);
            if (displayXray)
            {
                material.name = "Studio X-ray";
                material.SetFloat("_Opacity", .22f);
                material.SetFloat("_ZWrite", 0);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            ConfigureDisplayCulling(material);
            return material;
        }
        private void ClearDisplayMaterials()
        {
            blackBackfaceCommands?.Release(); blackBackfaceCommands = null;
            blackBackfaceDraws.Clear();
            foreach (var material in blackBackfaceMaterials.Values) if (material) UnityEngine.Object.DestroyImmediate(material);
            blackBackfaceMaterials.Clear();
            foreach (var material in culledSourceMaterials.Values) if (material) UnityEngine.Object.DestroyImmediate(material);
            culledSourceMaterials.Clear();
            foreach(var material in previewMaterials.Values) if(material) UnityEngine.Object.DestroyImmediate(material);
            previewMaterials.Clear();
            if(neutralLit) UnityEngine.Object.DestroyImmediate(neutralLit);
            if(neutralPreview) UnityEngine.Object.DestroyImmediate(neutralPreview);
        }
    }
}
