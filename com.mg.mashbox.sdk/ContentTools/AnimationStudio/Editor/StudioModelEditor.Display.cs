using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool displayLighting = true, displayTextures = true;
        private readonly Dictionary<(Material source, int slice), Material> previewMaterials = new Dictionary<(Material source, int slice), Material>();
        private Material neutralLit, neutralPreview;

        private void DrawDisplayToggles()
        {
            GUILayout.Space(4);
            bool lit = DisplayToggle(displayLighting, false, displayLighting ? "Lighting on — click for unlit" : "Unlit — click for lighting");
            bool textured = DisplayToggle(displayTextures, true, displayTextures ? "Materials and textures on — click for neutral shading" : "Neutral shading — click for materials and textures");
            if (lit != displayLighting || textured != displayTextures) SetDisplayMode(lit, textured);
            DrawObjectWireframeToggle();
        }
        private static readonly Vector3[] WireframeIconLines = {
            new Vector3(-7,-3),new Vector3(3,-3), new Vector3(3,-3),new Vector3(3,7),
            new Vector3(3,7),new Vector3(-7,7), new Vector3(-7,7),new Vector3(-7,-3),
            new Vector3(-3,-7),new Vector3(7,-7), new Vector3(7,-7),new Vector3(7,3),
            new Vector3(7,3),new Vector3(-3,3), new Vector3(-3,3),new Vector3(-3,-7),
            new Vector3(-7,-3),new Vector3(-3,-7), new Vector3(3,-3),new Vector3(7,-7),
            new Vector3(3,7),new Vector3(7,3), new Vector3(-7,7),new Vector3(-3,3)
        };
        private static void DrawObjectWireframeToggle()
        {
            var rect=GUILayoutUtility.GetRect(24,24,GUILayout.Width(24),GUILayout.Height(24));
            bool visible=!StudioSettings.Current.hideObjectWireframe;
            bool next=GUI.Toggle(rect,visible,new GUIContent("", "Object wireframe — show or hide selected-object outlines. Component editing overlays stay visible."),StudioTheme.Styles.overlayButton);
            if(next!=visible){StudioSettings.Current.hideObjectWireframe=!next;StudioSettings.Save();}
            if(Event.current.type!=EventType.Repaint)return;
            Handles.BeginGUI();
            using(new Handles.DrawingScope(StudioTheme.Current.Text,Matrix4x4.Translate(rect.center))) Handles.DrawLines(WireframeIconLines);
            Handles.EndGUI();
        }
        private static bool DisplayToggle(bool value, bool textureIcon, string tooltip)
        {
            var rect = GUILayoutUtility.GetRect(24,24,GUILayout.Width(24),GUILayout.Height(24));
            bool next = GUI.Toggle(rect,value,new GUIContent("",tooltip),StudioTheme.Styles.overlayButton);
            if (Event.current.type != EventType.Repaint) return next;
            var color = StudioTheme.Current.Text;
            if (textureIcon)
            {
                var area = new Rect(rect.center.x-7,rect.center.y-7,14,14);
                EditorGUI.DrawRect(area,StudioTheme.Alpha(color,.25f));
                for(int y=0;y<4;y++) for(int x=0;x<4;x++) if((x+y)%2==0)
                    EditorGUI.DrawRect(new Rect(area.x+x*3.5f,area.y+y*3.5f,3.5f,3.5f),color);
            }
            else
            {
                Handles.BeginGUI(); var previous = Handles.color; Handles.color = color;
                Vector3 center = rect.center;
                Handles.DrawWireDisc(center,Vector3.forward,4);
                for(int i=0;i<8;i++) {
                    float angle = i*Mathf.PI/4;
                    var direction = new Vector3(Mathf.Cos(angle),Mathf.Sin(angle));
                    Handles.DrawLine(center+direction*6,center+direction*9);
                }
                Handles.color = previous; Handles.EndGUI();
            }
            return next;
        }
        internal void SetDisplayMode(bool lighting, bool textures)
        {
            displayLighting = lighting; displayTextures = textures;
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
                var node = Document.nodes[i];
                var originals = node.materials ?? Array.Empty<Material>();
                int slots = Math.Max(originals.Length,node.mesh ? node.mesh.subMeshCount : 1);
                var materials = new Material[slots];
                for(int slot=0;slot<slots;slot++)
                    materials[slot] = DisplayMaterial(slot<originals.Length ? originals[slot] : null, node.previewTextureSlice);
                renderer.sharedMaterials = materials;
            }
        }
        private static bool IsGodGrass(Material source) => source && source.HasProperty("_GrassBaseArray");
        private Material DisplayMaterial(Material source, int slice = 0)
        {
            // Editable geometry already contains its pose; terrain deformation must not run again.
            if (LightingSettings.studioLights && displayLighting && displayTextures && !IsGodGrass(source) && source) return source;
            if (displayLighting && LightingSettings.studioLights && !displayTextures)
            {
                if (!neutralLit)
                {
                    var pipeline = GraphicsSettings.currentRenderPipeline;
                    var template = pipeline ? pipeline.defaultMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                    neutralLit = new Material(template) { name="Studio Neutral", hideFlags=HideFlags.HideAndDontSave };
                    foreach(var property in new[] {"_BaseColor","_Color"}) if(neutralLit.HasProperty(property)) neutralLit.SetColor(property,new Color(.65f,.65f,.65f));
                    foreach(var property in new[] {"_Smoothness","_Glossiness","_Metallic"}) if(neutralLit.HasProperty(property)) neutralLit.SetFloat(property,0);
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
            return material;
        }
        private void ClearDisplayMaterials()
        {
            foreach(var material in previewMaterials.Values) if(material) UnityEngine.Object.DestroyImmediate(material);
            previewMaterials.Clear();
            if(neutralLit) UnityEngine.Object.DestroyImmediate(neutralLit);
            if(neutralPreview) UnityEngine.Object.DestroyImmediate(neutralPreview);
        }
    }
}
