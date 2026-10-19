using System;
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

        private void DrawMaterialChannels()
        {
            if (!Document || Active<0 || !ActiveMesh) return;
            var node=Document.nodes[Active];
            GUILayout.Space(8);GUILayout.Label("MATERIALS",StudioTheme.Styles.boldLabel);
            int depth=0;
            for(int slot=0;slot<ActiveMesh.subMeshCount;slot++)
            {
                var material=node.materials!=null && slot<node.materials.Length ? node.materials[slot] : null;
                EditorGUI.BeginChangeCheck();
                var next=(Material)EditorGUILayout.ObjectField("Slot "+slot,material,typeof(Material),false);
                int target=slot;
                if(EditorGUI.EndChangeCheck())Run(()=>AssignMaterial(Active,target,next));
                if(IsGodGrass(material) && material.GetTexture("_GrassBaseArray") is Texture2DArray array)depth=Math.Max(depth,array.depth);
            }
            if(depth>0)
            {
                EditorGUI.BeginChangeCheck();int slice=EditorGUILayout.IntSlider("Preview Sub-ID",node.previewTextureSlice,0,depth-1);
                if(EditorGUI.EndChangeCheck()) { Undo.RecordObject(Document,"Preview material sub-ID");node.previewTextureSlice=slice;ApplyDisplayMaterials();Changed(false); }
                EditorGUILayout.HelpBox("Previews the selected texture-array layer on the authored mesh. Terrain wind and shape deformation are disabled in this modeling preview.",MessageType.None);
            }
            if(GUILayout.Button("Refresh Material Preview")) { displayTextures=true;ApplyDisplayMaterials();repaint(); }
        }

        private static void ConfigureTexturePreview(Material preview,Material source,int slice)
        {
            if(!source)return;
            bool grass=IsGodGrass(source);
            float Get(string property,float fallback) => source.HasProperty(property)?source.GetFloat(property):fallback;
            bool arrays=grass && Get("_GrassUseArrays",0)>.5f && source.GetTexture("_GrassBaseArray") is Texture2DArray;
            if(arrays)
            {
                var array=(Texture2DArray)source.GetTexture("_GrassBaseArray");slice=Mathf.Clamp(slice,0,array.depth-1);
                preview.SetTexture("_BaseArray",array);preview.SetFloat("_UseArray",1);preview.SetFloat("_ArraySlice",slice);
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
                if(source.HasProperty(name)&&source.GetTexture(name) is Texture2D normal)
                { preview.SetTexture("_NormalTex",normal);preview.SetFloat("_UseNormal",1);break; }
        }
    }
}
