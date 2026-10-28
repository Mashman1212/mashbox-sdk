using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Culling;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        const string CullingRegistryPath="Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Culling/Default Clothing Culling.asset";
        ClothingCullingRegistry cullingRegistry;
        Material cullingOverlayMaterial;
        bool cullingCutouts,cullingPreviewActive,cullingPassThrough;
        bool CullingWireframeOnly => Tool==Brush.Culling && CanCull;
        int cullingControl,cullingUndo=-1;
        readonly List<CullingPick> cullingPicks=new List<CullingPick>();
        sealed class CullingPick
        {
            public int part;public CullingMask mask;public Mesh mesh;public GameObject go;
            public MeshCollider collider;public ClothingCullingMap map;public Mesh overlay;
            public readonly List<int> overlayTriangles=new List<int>();
        }
        ClothingCullingRegistry CullingRegistry => cullingRegistry ? cullingRegistry : cullingRegistry=AssetDatabase.LoadAssetAtPath<ClothingCullingRegistry>(CullingRegistryPath);
        bool CanCull => Part!=null && !Part.referenceOnly && Part.role==StudioSkinningDocument.MeshRole.Clothing;
        CullingMask[] ActiveCullingMasks => Part==null ? Array.Empty<CullingMask>() : Part.customCulling ? Part.cullingMasks : Part.cullingPreset ? Part.cullingPreset.masks : Array.Empty<CullingMask>();

        void DrawCullingModeIcon(Rect rect)
        {
            if(DrawSkinModeButton(rect,Tool==Brush.Culling,"Culling — preview clothing presets or paint custom body/bust coverage"))
            {CancelBrushInteraction();ChooseBrush(Brush.Culling);UpdateDisplay();}
            if(Event.current.type!=EventType.Repaint)return;
            var previous=Handles.color;Handles.color=Tool==Brush.Culling?new Color(.5f,1,.87f):Color.gray;
            var c=rect.center;Handles.DrawAAPolyLine(1.6f,new Vector3(c.x-9,c.y-8),new Vector3(c.x+9,c.y-8),new Vector3(c.x+9,c.y+8),new Vector3(c.x-9,c.y+8),new Vector3(c.x-9,c.y-8));
            Handles.DrawAAPolyLine(2,new Vector3(c.x-10,c.y+10),new Vector3(c.x+10,c.y-10));Handles.color=previous;
        }
        void DrawCullingInspector()
        {
            GUILayout.Label("CLOTHING CULLING",StudioTheme.Styles.boldLabel);
            if(!CanCull){EditorGUILayout.HelpBox("Select your clothing mesh. Culling hides the body and bust underneath it; it does not change skin weights.",MessageType.Info);return;}
            var registry=CullingRegistry;
            if(!registry){EditorGUILayout.HelpBox("The SDK culling registry is missing.",MessageType.Error);return;}
            var presetRect=EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(),new GUIContent("Presets"));
            string presetLabel=Part.customCulling ? "Custom" : Part.cullingPreset ? Part.cullingPreset.name : "None";
            if(EditorGUI.DropdownButton(presetRect,new GUIContent(presetLabel),FocusType.Keyboard))
            {
                var menu=new GenericMenu();
                menu.AddItem(new GUIContent("None"),!Part.customCulling && !Part.cullingPreset,()=>SelectCullingPreset(null));
                foreach(var preset in registry.presets)
                {
                    var selected=preset;
                    menu.AddItem(new GUIContent(preset.name),!Part.customCulling && Part.cullingPreset==preset,()=>SelectCullingPreset(selected));
                }
                menu.DropDown(presetRect);
            }
            using(new EditorGUI.DisabledScope(Part.customCulling))
                if(GUILayout.Button(new GUIContent("Make Custom","Create an editable copy of the current culling mask.")))
                {
                    FinishCullingStroke();
                    Undo.RegisterCompleteObjectUndo(Document,"Make custom culling");
                    Part.cullingMasks=registry.surfaces.Select(s=>ActiveCullingMasks.FirstOrDefault(m=>m.surface==s)?.Copy() ?? new CullingMask{surface=s,hidden=new byte[s.TriangleCount]}).ToArray();
                    Part.customCulling=true;Part.cullingAssigned=true;
                    Changed();UpdateDisplay();
                }
            EditorGUI.BeginChangeCheck();
            cullingCutouts=EditorGUILayout.Toggle("Preview cutouts",cullingCutouts);
            if(EditorGUI.EndChangeCheck())UpdateDisplay();
            if(Part.customCulling)
            {
                radius=EditorGUILayout.Slider("Radius",radius,.005f,.5f);
                cullingPassThrough=EditorGUILayout.Toggle(new GUIContent("Pass through", "Paint through the body and bust, including back-facing surfaces within the brush radius. Ctrl restores through both sides."),cullingPassThrough);
                EditorGUILayout.HelpBox("+ Paint to hide body/bust triangles. Hold Ctrl for − Restore. Ctrl + middle-mouse drag left/right resizes the brush; Esc cancels resizing. Turn off Preview cutouts to see the mask in red.",MessageType.Info);
                if(GUILayout.Button("Clear custom coverage"))
                {Undo.RegisterCompleteObjectUndo(Document,"Clear culling");foreach(var mask in Part.cullingMasks)Array.Clear(mask.hidden,0,mask.hidden.Length);Changed();UpdateDisplay();}
            }
            else EditorGUILayout.HelpBox("Click Make Custom to paint an editable copy of this preset.",MessageType.Info);
            foreach(var mask in ActiveCullingMasks)if(mask.surface)GUILayout.Label(mask.surface.region+": "+mask.hidden.Count(v=>v!=0).ToString("N0")+" hidden triangles",StudioTheme.Styles.miniLabel);
            EditorGUILayout.HelpBox("Save FBX asks whether to also create a prefab when you have custom culling. Equip the prefab to use that culling in game. FBX alone does not store culling data.",MessageType.Info);
        }
        void SelectCullingPreset(ClothingCullingProfile preset)
        {
            if(!CanCull || (!Part.customCulling && Part.cullingPreset==preset))return;
            FinishCullingStroke();
            Undo.RegisterCompleteObjectUndo(Document,"Change culling preset");
            Part.cullingAssigned=true;Part.customCulling=false;Part.cullingPreset=preset;
            Changed();UpdateDisplay();
        }
        void DisposeCullingPicks()
        {
            foreach(var pick in cullingPicks){if(pick.go)Object.DestroyImmediate(pick.go);if(pick.mesh)Object.DestroyImmediate(pick.mesh);if(pick.overlay)Object.DestroyImmediate(pick.overlay);}
            if(cullingOverlayMaterial)Object.DestroyImmediate(cullingOverlayMaterial);cullingOverlayMaterial=null;
            cullingPicks.Clear();
        }
        void UpdateCullingDisplay()
        {
            if(Tool!=Brush.Culling && !cullingPreviewActive)return;
            cullingPreviewActive=Tool==Brush.Culling;
            DisposeCullingPicks();
            for(int i=0;i<objects.Count;i++)
            {
                var part=Document.parts[i];
                bool visible=part.visible;
                if(Tool==Brush.Culling && CanCull && part.referenceOnly)visible=part.role==StudioSkinningDocument.MeshRole.Body || part.role==StudioSkinningDocument.MeshRole.Bust;
                objects[i].SetActive(visible);objects[i].GetComponent<Renderer>().enabled=visible;
                for(int s=0;s<part.mesh.subMeshCount;s++)displayMeshes[i].SetTriangles(part.mesh.GetTriangles(s),s,false);
            }
            if(Tool!=Brush.Culling || !CanCull)return;
            // Keep the garment outline visible through the dedicated wireframe pass.
            objects[Document.selected].GetComponent<Renderer>().enabled=false;
            foreach(var mask in ActiveCullingMasks)
            {
                if(!mask.surface)continue;
                var role=mask.surface.region==CullingRegion.Body?StudioSkinningDocument.MeshRole.Body:StudioSkinningDocument.MeshRole.Bust;
                int index=Document.parts.FindIndex(p=>p.referenceOnly && p.role==role);
                if(index<0)continue;
                var part=Document.parts[index];var renderer=objects[index].GetComponent<SkinnedMeshRenderer>();if(!renderer)continue;
                // Picking always uses the complete surface so erased triangles can be painted back.
                var baked=new Mesh {name="Culling pick",hideFlags=HideFlags.HideAndDontSave};renderer.BakeMesh(baked);
                var go=new GameObject("Culling pick") {hideFlags=HideFlags.HideAndDontSave};UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,Scene);
                go.transform.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation);go.transform.localScale=renderer.transform.lossyScale;
                var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=baked;
                var pick=new CullingPick{part=index,mask=mask,mesh=baked,go=go,collider=collider};cullingPicks.Add(pick);
                pick.map=new ClothingCullingMap(mask.surface,part.mesh,bones.Select(b=>b.name).ToArray());
            }
            RefreshCullingMasks();
        }
        void RefreshCullingMasks()
        {
            foreach(var pick in cullingPicks)
            {
                int index=pick.part;var part=Document.parts[index];var baked=pick.mesh;
                var hidden=pick.map.Evaluate(pick.mask);
                if(cullingCutouts)ClothingCullingMap.Apply(displayMeshes[index],part.mesh,hidden);
                if(!pick.overlay)
                {
                    pick.overlay=new Mesh{name="Culling mask fill",hideFlags=HideFlags.HideAndDontSave,indexFormat=baked.indexFormat};
                    pick.overlay.vertices=baked.vertices;
                    pick.overlay.bounds=baked.bounds;
                    pick.overlay.MarkDynamic();
                }
                var tris=baked.triangles;var indices=pick.overlayTriangles;indices.Clear();
                for(int t=0;t<hidden.Length;t++)if(hidden[t])
                {
                    indices.Add(tris[t*3]);indices.Add(tris[t*3+1]);indices.Add(tris[t*3+2]);
                }
                pick.overlay.SetTriangles(indices,0,false);
            }
        }
        void DrawCullingFill()
        {
            if(cullingPicks.Count==0)return;
            if(!cullingOverlayMaterial)
            {
                // Reuse the Modeler's depth-tested face overlay, including its depth bias.
                var shader=Shader.Find("Hidden/MashBox/ModelerComponents");
                if(!shader || !shader.isSupported)return;
                cullingOverlayMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
                cullingOverlayMaterial.SetColor("_Color",new Color(1,.12f,.07f,.85f));
                cullingOverlayMaterial.SetFloat("_Mode",0);
                cullingOverlayMaterial.SetFloat("_ZTest",(float)UnityEngine.Rendering.CompareFunction.LessEqual);
            }
            foreach(var pick in cullingPicks)
                if(pick.overlay && pick.overlayTriangles.Count>0 && cullingOverlayMaterial.SetPass(0))
                    Graphics.DrawMeshNow(pick.overlay,pick.go.transform.localToWorldMatrix);
        }
        void FinishCullingStroke()
        {
            if(cullingUndo<0)return;
            Undo.CollapseUndoOperations(cullingUndo);cullingUndo=-1;
            if(GUIUtility.hotControl==cullingControl)GUIUtility.hotControl=0;
            Undo.IncrementCurrentGroup();
        }
        CullingPick PickCullingSurface(Ray ray, out RaycastHit hit)
        {
            CullingPick nearest=null;hit=default;float distance=float.MaxValue;
            foreach(var pick in cullingPicks)
                if(pick.collider && pick.collider.Raycast(ray,out var h,10000) && h.distance<distance)
                {nearest=pick;hit=h;distance=h.distance;}
            return nearest;
        }
        void DrawCullingCursor(SceneView view, Vector2 mouse, bool valid, RaycastHit hit, bool showOperation)
        {
            StudioBrushCursor.Draw(view,mouse,radius,valid,hit.point,hit.normal,new Color(1,.3f,.15f),ref brushCursorDepth);
            if(!showOperation || Event.current.type!=EventType.Repaint || !StudioBrushCursor.InViewport(view,mouse))return;
            Handles.BeginGUI();
            try
            {
                // Fixed screen size keeps the add/restore badge legible at any zoom.
                var badge=new Rect(mouse.x+12,mouse.y+12,16,16);
                EditorGUI.DrawRect(badge,new Color(0,0,0,.8f));
                Color ink=valid?Color.white:StudioBrushCursor.Inactive;
                EditorGUI.DrawRect(new Rect(badge.x+3,badge.y+7,10,2),ink);
                if(!Event.current.control)EditorGUI.DrawRect(new Rect(badge.x+7,badge.y+3,2,10),ink);
            }
            finally {Handles.EndGUI();}
        }
        void DrawCullingScene(SceneView view)
        {
            var e=Event.current;int id=GUIUtility.GetControlID("StudioCulling".GetHashCode(),FocusType.Passive);
            if(!cullingCutouts && e.type==EventType.Repaint)DrawCullingFill();
            if(e.rawType==EventType.MouseUp || e.type==EventType.Ignore)FinishCullingStroke();
            if(!CanCull || !Part.customCulling)return;
            bool overUI=ModeRect(view).Contains(e.mousePosition)||DisplayToolbarRect(view).Contains(e.mousePosition)||view is MashBoxStudioViewport v && (v.IsOverSkinningPanels(e.mousePosition)||!v.ModelCanvasRect.Contains(e.mousePosition));
            if(overUI || e.alt)return;
            if(e.type==EventType.Layout)HandleUtility.AddDefaultControl(id);
            var ray=HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var nearest=PickCullingSurface(ray,out var hit);
            DrawCullingCursor(view,e.mousePosition,nearest!=null,hit,true);
            if(nearest==null || e.button!=0 || (e.type!=EventType.MouseDown && e.type!=EventType.MouseDrag))return;
            if(e.type==EventType.MouseDown)
            {Undo.IncrementCurrentGroup();cullingUndo=Undo.GetCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Paint clothing culling");cullingControl=id;GUIUtility.hotControl=id;}
            if(cullingUndo<0)return;
            // Default reference imports retain their triangle ordering. Never paint an ambiguous remapped topology.
            // Validate every affected surface before changing any mask.
            foreach(var pick in cullingPicks)
            {
                if(!cullingPassThrough && pick!=nearest)continue;
                var triangles=pick.mesh.triangles;
                if(triangles.Length!=pick.mask.surface.triangles.Length || !triangles.SequenceEqual(pick.mask.surface.triangles))
                {view.ShowNotification(new GUIContent("Reload the SDK default references before painting culling."));FinishCullingStroke();return;}
            }
            foreach(var pick in cullingPicks)
            {
                if(!cullingPassThrough && pick!=nearest)continue;
                var points=pick.mesh.vertices;var triangles=pick.mesh.triangles;
                for(int t=0;t<triangles.Length/3;t++)
                {
                    var a=pick.go.transform.TransformPoint(points[triangles[t*3]]);var b=pick.go.transform.TransformPoint(points[triangles[t*3+1]]);var c=pick.go.transform.TransformPoint(points[triangles[t*3+2]]);
                    var center=(a+b+c)/3;
                    if(Vector3.Dot(center-ray.origin,ray.direction)<0)continue;
                    if(!cullingPassThrough && Vector3.Dot(Vector3.Cross(b-a,c-a),ray.direction)>=0)continue;
                    var offset=center-hit.point;
                    // A cylinder along the view ray reaches the back surface without enlarging the brush.
                    if(cullingPassThrough)offset-=ray.direction*Vector3.Dot(offset,ray.direction);
                    if((pick==nearest && t==hit.triangleIndex) || offset.sqrMagnitude<radius*radius)
                        pick.mask.hidden[t]=(byte)(e.control?0:1);
                }
            }
            Changed();RefreshCullingMasks();e.Use();repaint();
        }

        static ClothingCullingProfile ExportCullingProfile(StudioSkinningDocument.Part part,string path)
        {
            if(!part.customCulling)return part.cullingPreset;
            var profile=AssetDatabase.LoadAssetAtPath<ClothingCullingProfile>(path);
            if(!profile && AssetDatabase.LoadMainAssetAtPath(path))throw new InvalidOperationException("Another asset already occupies the culling profile path: "+path);
            if(!profile){profile=ScriptableObject.CreateInstance<ClothingCullingProfile>();AssetDatabase.CreateAsset(profile,path);}
            profile.masks=part.cullingMasks.Select(m=>m.Copy()).ToArray();EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);return profile;
        }
        static void AttachExportCulling(GameObject target,StudioSkinningDocument.Part part,string path)
        {
            if(part.role!=StudioSkinningDocument.MeshRole.Clothing || (!part.cullingAssigned && !part.customCulling && !part.cullingPreset))return;
            var settings=target.GetComponent<ClothingCullingSettings>();if(!settings)settings=target.AddComponent<ClothingCullingSettings>();
            settings.profile=ExportCullingProfile(part,path);
        }
        static void ExportCullingCompanion(StudioSkinningDocument.Part part,string fbxPath)
        {
            if(part.role!=StudioSkinningDocument.MeshRole.Clothing || (!part.cullingAssigned && !part.customCulling && !part.cullingPreset))return;
            string stem=Path.ChangeExtension(fbxPath,null);
            string prefabPath=stem+".culling.prefab",profilePath=stem+".culling.asset";
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if(AssetDatabase.LoadMainAssetAtPath(prefabPath) && (!existing || !existing.GetComponent<ClothingCullingSettings>()))
                throw new InvalidOperationException("Another prefab already occupies the culling export path: "+prefabPath);
            var paths=new[]{prefabPath,profilePath};
            var backups=paths.Select(p=>File.Exists(p)?File.ReadAllBytes(p):null).ToArray();
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();GameObject instance=null;
            try
            {
                instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath),scene);
                AttachExportCulling(instance,part,profilePath);
                if(!PrefabUtility.SaveAsPrefabAsset(instance,prefabPath))throw new InvalidOperationException("Unity could not save the culling companion prefab.");
            }
            catch
            {
                for(int i=0;i<paths.Length;i++)
                {
                    if(backups[i]==null)AssetDatabase.DeleteAsset(paths[i]);
                    else {File.WriteAllBytes(paths[i],backups[i]);AssetDatabase.ImportAsset(paths[i],ImportAssetOptions.ForceUpdate);}
                }
                throw;
            }
            finally {if(instance)Object.DestroyImmediate(instance);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
