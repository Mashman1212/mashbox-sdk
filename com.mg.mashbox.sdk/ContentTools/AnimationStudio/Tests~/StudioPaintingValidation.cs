// Compile with the Studio editor sources in a disposable graphics-enabled Unity project.
// This fixture exits Unity. Never run it in a working project.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioPaintingValidation
    {
        internal static int checks;
        internal static void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new InvalidOperationException(message);
        }
        public static void Run()
        {
            try
            {
                Geometry();
                StudioImagingWindow.ValidateSurfaceDocument();
                AnimationStudioWindow.ValidatePaintingWorkspace(() =>
                {
                    File.WriteAllText("painting-result.txt", "PASS " + checks + " checks, including native panel Layout/Repaint");
                    EditorApplication.Exit(0);
                });
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }
        internal static void Fail(Exception e)
        {
            File.WriteAllText("painting-result.txt", "FAIL after " + checks + " checks\n" + e);
            Debug.LogException(e); EditorApplication.Exit(1);
        }

        private static void Geometry()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.uv2 = new[] { new Vector2(.2f,.2f),new Vector2(.4f,.2f),new Vector2(.4f,.4f),new Vector2(.2f,.4f) };
            mesh.subMeshCount = 2; mesh.SetTriangles(new[] { 0, 2, 1 }, 0); mesh.SetTriangles(new[] { 0, 3, 2 }, 1);
            var a = new GameObject("Target"); SceneManager.MoveGameObjectToScene(a, scene);
            a.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = a.AddComponent<MeshRenderer>();
            a.transform.position = new Vector3(2, 3, 1); a.transform.localScale = new Vector3(2, 3, .5f);
            var b = new GameObject("Occluder"); SceneManager.MoveGameObjectToScene(b, scene);
            b.AddComponent<MeshFilter>().sharedMesh = mesh; var front = b.AddComponent<MeshRenderer>();
            b.transform.position = a.transform.position - Vector3.forward;
            try
            {
                using var surface = new StudioPaintingSurface(new[] { renderer, front });
                var ray = new Ray(new Vector3(2.5f, 2.5f, -3), Vector3.forward);
                Check(surface.Raycast(ray, 0, out var hit) && hit.renderer == front, "Foreground non-target geometry must occlude target");
                front.enabled = false;
                Check(surface.Raycast(ray, 0, out hit) && hit.renderer == renderer && hit.slot == 0, "Submesh picking after occluder hidden");
                Check(Mathf.Abs(hit.distance - 4) < .0001f, "Nonuniform scale preserves world hit distance");
                Check(Vector2.Distance(hit.uv, new Vector2(.625f, .4166667f)) < .0001f, "Barycentric UV0 interpolation");
                Check(surface.BrushAxes(hit, 0, out var u, out var v) && Vector3.Distance(u, new Vector3(4,0,0)) < .0001f && Vector3.Distance(v, new Vector3(0,6,0)) < .0001f, "Brush footprint accounts for transformed UV axes");
                Check(surface.Raycast(ray, 1, out hit) && Vector2.Distance(hit.uv, new Vector2(.325f,.2833333f)) < .0001f, "UV1 interpolation");
                Check(surface.Raycast(ray, 3, out hit) && !hit.hasUv, "Missing UVs must not silently paint UV0");
                Check(!surface.Raycast(new Ray(new Vector3(20,20,-3),Vector3.forward),0,out hit), "Miss does not return a stale hit");
                a.SetActive(false); Check(!surface.Raycast(ray,0,out hit), "Inactive meshes cannot receive paint"); a.SetActive(true);
                a.transform.localScale = new Vector3(-2,3,.5f);
                Check(surface.Raycast(ray,0,out hit) && hit.renderer==renderer, "Mirrored transforms remain pickable");
                // Exercise BVH internal nodes with a larger triangle set.
                var dense = new List<int>(); for(int i=0;i<40;i++)dense.AddRange(new[]{0,2,1});
                mesh.SetTriangles(dense,0);
                using var large = new StudioPaintingSurface(new[]{renderer});
                Check(large.Raycast(ray,0,out hit), "BVH internal node traversal");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(mesh); }
        }
    }

    public sealed partial class StudioImagingWindow
    {
        internal int SurfaceHistoryCountForValidation => undoImages.Count;
        private static string SurfaceDocumentSignature(StudioImagingWindow image)
        {
            var file=JsonUtility.FromJson<ImageFile>(image.CaptureImage());
            foreach(var entry in file.layers)
            {
                var layer=JsonUtility.FromJson<StickerLayer>(entry.settings);
                layer.texture=null; // Restoring pixels necessarily creates new texture instances.
                entry.settings=JsonUtility.ToJson(layer);
            }
            return JsonUtility.ToJson(file);
        }
        internal static void ValidateSurfaceDocument()
        {
            var image = CreateInstance<StudioImagingWindow>();
            image.resolutionIndex = 0;
            var material = new Material(Shader.Find("Unlit/Texture"));
            var baseImage = new Texture2D(512,512,TextureFormat.RGBA32,false);
            var colors = new Color[512*512]; for(int i=0;i<colors.Length;i++)colors[i]=Color.blue;
            baseImage.SetPixels(colors); baseImage.Apply(); material.mainTexture=baseImage;
            var root = new GameObject("Surface preview"); var renderer=root.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            var block = new MaterialPropertyBlock(); block.SetFloat("_PaintingSentinel",.37f); renderer.SetPropertyBlock(block,0);
            try
            {
                image.BindSurface(renderer,0,"_MainTex",new[]{renderer},true);
                StudioPaintingValidation.Check(image.layers.Count==2 && image.layers[0].texture==baseImage,"Existing texture plus separate paint layer");
                image.brushColor=Color.red; image.brushRadius=12;
                string before=SurfaceDocumentSignature(image);
                StudioPaintingValidation.Check(image.BeginSurfaceStroke(new Vector2(.2f,.8f)),"Begin stroke");
                image.FlushSurfaceStroke(); image.SurfaceDab(new Vector2(.22f,.8f)); image.FlushSurfaceStroke(); image.EndSurfaceStroke();
                StudioPaintingValidation.Check(image.undoImages.Count==1,"One stroke is one undo, even with repeated flushes");
                var paint=image.layers[image.selectedLayer].texture;
                StudioPaintingValidation.Check(paint.GetPixel(102,409).r>.9f && paint.GetPixel(102,409).a>.9f,"3D UV Y orientation matches 2D canvas");
                StudioPaintingValidation.Check(paint.GetPixel(102,102).a<.01f,"Opposite vertical location untouched");
                StudioPaintingValidation.Check(baseImage.GetPixel(102,409)==Color.blue,"Source map is unchanged");
                renderer.GetPropertyBlock(block,0);
                StudioPaintingValidation.Check(block.GetTexture("_MainTex")==image.composite && Mathf.Abs(block.GetFloat("_PaintingSentinel")-.37f)<.001f,"Live preview preserves existing property block data");
                StudioPaintingValidation.Check(material.mainTexture==baseImage,"Live preview does not modify source material");
                string painted=image.CaptureImage(), paintedSignature=SurfaceDocumentSignature(image); image.ImageUndo(false);
                StudioPaintingValidation.Check(SurfaceDocumentSignature(image)==before,"Undo restores exact document pixels and settings");
                image.ImageUndo(true); StudioPaintingValidation.Check(SurfaceDocumentSignature(image)==paintedSignature,"Redo restores exact painted document");
                image.BeginSurfaceStroke(new Vector2(.8f,.2f)); image.FlushSurfaceStroke(); image.CancelSurfaceStroke();
                StudioPaintingValidation.Check(SurfaceDocumentSignature(image)==paintedSignature && image.undoImages.Count==1,"Escape restores document and history");
                image.layers[image.selectedLayer].locked=true;
                StudioPaintingValidation.Check(!image.BeginSurfaceStroke(Vector2.one*.5f),"Locked layer rejected");
                image.layers[image.selectedLayer].locked=false;
                image.layers[image.selectedLayer].visible=false;
                StudioPaintingValidation.Check(!image.BeginSurfaceStroke(Vector2.one*.5f),"Hidden layer rejected");
                image.layers[image.selectedLayer].visible=true;
                image.imageTool=ImageTool.Eraser;
                image.BeginSurfaceStroke(new Vector2(.2f,.8f));image.EndSurfaceStroke();
                StudioPaintingValidation.Check(image.layers[image.selectedLayer].texture.GetPixel(102,409).a<.01f,"Erase uses shared brush engine");
                image.ImageUndo(false); image.RenderComposite();
                var exported=ReadTexture(image.composite);
                try { File.WriteAllBytes("painting-composite.png",exported.EncodeToPNG()); }
                finally { DestroyImmediate(exported); }
                image.RestoreImage(painted); StudioPaintingValidation.Check(SurfaceDocumentSignature(image)==paintedSignature,"Layered serialization roundtrip");
                image.SuspendSurface();renderer.GetPropertyBlock(block,0);
                StudioPaintingValidation.Check(block.GetTexture("_MainTex")==null && Mathf.Abs(block.GetFloat("_PaintingSentinel")-.37f)<.001f,"Suspend restores original preview state");
            }
            finally { image.hasUnsavedChanges=false; DestroyImmediate(image);DestroyImmediate(root);DestroyImmediate(material);DestroyImmediate(baseImage); }
        }
    }

    public sealed partial class AnimationStudioWindow
    {
        internal static void ValidatePaintingWorkspace(Action complete)
        {
            var studio=CreateInstance<AnimationStudioWindow>();
            var document=CreateInstance<StudioModelDocument>();
            document.SetWorkspacePath(StudioModelDocument.NewWorkspacePath("Painting mesh list validation"));
            var material=new Material(Shader.Find("Standard"));
            material.mainTexture=Texture2D.whiteTexture;
            var material2=new Material(Shader.Find("Unlit/Texture"));
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh=Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
            DestroyImmediate(cube);
            document.nodes.Add(new StudioModelDocument.Node { name="Paint cube", mesh=mesh, materials=new[]{material} });
            document.nodes.Add(new StudioModelDocument.Node { name="Other material", mesh=Instantiate(mesh), materials=new[]{material2}, position=Vector3.right*2 });
            try
            {
                studio.modelingMode=true;studio.paintingMode=false;
                studio.OpenViewport();studio.LoadStudioModel(document);
                string originalLighting=JsonUtility.ToJson(document.lighting);
                studio.OpenPaintingMode();
                studio.modelEditor.ValidatePaintingLightRig(true);
                StudioPaintingValidation.Check(studio.viewport.sceneLighting,"Painting enables real scene lighting");
                ValidateLitPaintingRender(studio.modelEditor);
                StudioPaintingValidation.Check(studio.IsPaintingMode && !studio.IsModelingMode && studio.paintingImage,"Painting binds a shared image document");
                var image=studio.paintingImage;
                image.BeginSurfaceStroke(Vector2.one*.5f);image.EndSurfaceStroke();
                studio.SwitchStudioMode(true);
                studio.modelEditor.ValidatePaintingLightRig(false);
                StudioPaintingValidation.Check(JsonUtility.ToJson(document.lighting)==originalLighting,"Painting does not overwrite Modeling lighting preferences");
                StudioPaintingValidation.Check(studio.IsModelingMode && !studio.IsPaintingMode && image,"Switch to Modeling retains image document");
                studio.OpenPaintingMode();
                studio.modelEditor.ValidatePaintingLightRig(true);
                StudioPaintingValidation.Check(studio.paintingImage==image && studio.paintingMaps.Count==1,"Returning to Painting reuses document");
                StudioPaintingValidation.Check(studio.modelEditor.Document==document,"Painting shares Modeling document");
                studio.SelectPaintingMesh(1);
                var other=studio.paintingImage;
                StudioPaintingValidation.Check(other!=image && studio.paintingMaps.Count==2,"Different materials have independent documents");
                studio.SelectPaintingMesh(0);
                StudioPaintingValidation.Check(studio.paintingImage==image,"Switching materials restores original document");
                studio.SetPaintingMeshVisible(0,false);
                StudioPaintingValidation.Check(!studio.paintingRenderers[0].gameObject.activeSelf && studio.paintingRenderers[1].gameObject.activeSelf,"Mesh-list visibility changes only the requested mesh");
                StudioPaintingValidation.Check(!document.nodes[0].active,"Mesh-list visibility is stored in the workspace");
                studio.SetPaintingMeshVisible(0,true);
                StudioPaintingValidation.Check(studio.paintingImage==image && image.SurfaceHistoryCountForValidation==1,"Visibility retains image document and stroke history");
                var additional=GameObject.CreatePrimitive(PrimitiveType.Sphere);additional.name="Additional paint mesh";
                additional.GetComponent<Renderer>().sharedMaterial=material;
                additional.transform.position=new Vector3(12,3,-8);
                additional.transform.rotation=Quaternion.Euler(15,60,5);additional.transform.localScale=Vector3.one*2;
                var child=new GameObject("Preserved child offset");child.transform.SetParent(additional.transform,false);
                child.transform.localPosition=new Vector3(.2f,1,.3f);child.transform.localRotation=Quaternion.Euler(0,25,0);
                try
                {
                    studio.ImportPaintingMeshes(()=>studio.modelEditor.ImportModel(additional,studio.LoadStudioModel));
                    StudioPaintingValidation.Check(additional.transform.position==new Vector3(12,3,-8) && additional.transform.localScale==Vector3.one*2,"Zeroing import does not change the source model");
                }
                finally { DestroyImmediate(additional); }
                StudioPaintingValidation.Check(studio.paintingRenderers.Length==3 && document.nodes[0].name=="Paint cube" && document.nodes[1].name=="Other material","Import appends a third mesh without replacing existing meshes");
                StudioPaintingValidation.Check(studio.PaintingRenderer.name=="Additional paint mesh","Newly imported mesh becomes the active list row");
                StudioPaintingValidation.Check(studio.paintingRenderers.All(r=>r.gameObject.activeInHierarchy),"All three meshes remain visible together");
                StudioPaintingValidation.Check(document.nodes[2].position==Vector3.zero && document.nodes[2].scale==Vector3.one && Quaternion.Angle(document.nodes[2].rotation,Quaternion.identity)<.001f,"New model root imports at zero position, identity rotation and unit scale");
                StudioPaintingValidation.Check(document.nodes[3].position==new Vector3(.2f,1,.3f) && Quaternion.Angle(document.nodes[3].rotation,Quaternion.Euler(0,25,0))<.001f,"Zeroing preserves child transform alignment");
                StudioPaintingValidation.Check(document.nodes[1].position==Vector3.right*2,"New imports do not move existing models");
                document.nodes[2].position=new Vector3(5,0,-4);document.ApplyTransform(2,studio.modelEditor.Transforms[2]);
                studio.ZeroPaintingPlacement(false);
                StudioPaintingValidation.Check(document.nodes[2].position==Vector3.zero && document.nodes[1].position==Vector3.right*2,"Zero Selected repairs existing placement without moving another model");
                studio.ZeroPaintingPlacement(true);
                StudioPaintingValidation.Check(document.nodes[1].position==Vector3.zero && document.nodes[3].position==new Vector3(.2f,1,.3f),"Zero All changes model roots only");
                studio.SelectPaintingMesh(0);
                StudioPaintingValidation.Check(studio.paintingImage==image && image.SurfaceHistoryCountForValidation==1,"Adding a mesh preserves existing paint and undo history");
                var probe=CreateInstance<PaintingGuiProbe>();
                probe.studio=studio;probe.complete=()=>{Cleanup();complete();};probe.cleanup=Cleanup;
                probe.position=new Rect(100,100,1000,750);probe.ShowUtility();probe.Begin();
            }
            catch { Cleanup(); throw; }

            void Cleanup()
            {
                studio.ReleasePainting();
                foreach(var map in studio.paintingMaps)if(map.image){map.image.DiscardChanges();map.image.Close();}
                if(studio.viewport){studio.modelEditor?.DiscardWorkspaceForClose();studio.viewport.DiscardChanges();studio.viewport.Close();}
                DestroyImmediate(studio);DestroyImmediate(material);DestroyImmediate(material2);
            }
        }

        private static void ValidateLitPaintingRender(StudioModelEditor editor)
        {
            var go=new GameObject("Painting lighting render check");
            SceneManager.MoveGameObjectToScene(go,editor.Scene);
            var camera=go.AddComponent<Camera>();camera.scene=editor.Scene;
            camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(editor.Scene);
            camera.transform.position=new Vector3(0,0,3);camera.transform.LookAt(Vector3.zero);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            Texture2D image=null;
            try
            {
                editor.ConfigurePaintingLighting(camera, .75f);
                camera.Render();image=StudioImagingWindow.ReadTexture(target);
                float sum=0;for(int y=56;y<72;y++)for(int x=56;x<72;x++)sum+=image.GetPixel(x,y).grayscale;
                StudioPaintingValidation.Check(sum/256>.05f,"Native lit material renders above black in Painting");
                File.WriteAllBytes("painting-lit-preview.png",image.EncodeToPNG());
                DestroyImmediate(image);
                camera.transform.position=new Vector3(0,0,-3);camera.transform.LookAt(Vector3.zero);
                editor.ConfigurePaintingLighting(camera,.75f);
                camera.Render();image=StudioImagingWindow.ReadTexture(target);
                float back=0;for(int y=56;y<72;y++)for(int x=56;x<72;x++)back+=image.GetPixel(x,y).grayscale;
                StudioPaintingValidation.Check(Mathf.Abs(back-sum)/256<.03f,"Orbiting behind the model preserves painting illumination");
                File.WriteAllBytes("painting-lit-back-preview.png",image.EncodeToPNG());
                DestroyImmediate(image);
                editor.ConfigurePaintingLighting(camera,-4);
                camera.Render();image=StudioImagingWindow.ReadTexture(target);
                float dim=0;for(int y=56;y<72;y++)for(int x=56;x<72;x++)dim+=image.GetPixel(x,y).grayscale;
                File.WriteAllBytes("painting-lit-dim-preview.png",image.EncodeToPNG());
                StudioPaintingValidation.Check(dim<back*.9f,$"Preview brightness changes rendered illumination ({back/256:F3} to {dim/256:F3})");
            }
            finally { if(image)DestroyImmediate(image);camera.targetTexture=null;target.Release();DestroyImmediate(target);DestroyImmediate(go); }
        }

        private sealed class PaintingGuiProbe : EditorWindow
        {
            internal AnimationStudioWindow studio;
            internal Action complete, cleanup;
            private int repaints, ticks;
            private int keyStage;
            private bool keyDelivered;
            private Exception failure;
            internal void Begin(){EditorApplication.update+=Tick;Application.logMessageReceived+=Log;}
            private void Log(string text,string trace,LogType type)
            {if(type==LogType.Exception || type==LogType.Assert)failure=new Exception(text+"\n"+trace);}
            private void OnGUI()
            {
                try
                {
                    using(StudioTheme.Scope())
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        using(new EditorGUILayout.VerticalScope(GUILayout.Width(380)))studio.DrawPaintingTargets();
                        using(new EditorGUILayout.VerticalScope())studio.DrawPaintingInspector();
                    }
                    if(Event.current.type==EventType.Repaint)repaints++;
                }
                catch(Exception e){failure=e;}
            }
            private void Tick()
            {
                ticks++;Repaint();studio?.viewport?.Repaint();
                if(failure==null && repaints<2 && ticks<300)return;
                if(failure==null && repaints>=2 && keyStage<2)
                {
                    if(!keyDelivered && keyStage==1)return;
                    if(keyStage==1)
                    {
                        try { StudioPaintingValidation.Check(studio.paintingImage.SurfaceHistoryCountForValidation==0,"Native Ctrl+Z routes to image history"); }
                        catch(Exception e){failure=e;}
                    }
                    if(failure==null)
                    {
                        var modifiers=keyStage==0?EventModifiers.Control:EventModifiers.Control|EventModifiers.Shift;
                        using(var key=UnityEngine.UIElements.KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.Z,modifiers=modifiers}))
                            studio.viewport.rootVisualElement.SendEvent(key);
                        keyStage++;keyDelivered=false;EditorApplication.delayCall+=()=>keyDelivered=true;
                        return;
                    }
                }
                if(failure==null && keyStage==2 && !keyDelivered)return;
                if(failure==null)
                {
                    try{StudioPaintingValidation.Check(studio.paintingImage.SurfaceHistoryCountForValidation==1,"Native Ctrl+Shift+Z restores image history");}
                    catch(Exception e){failure=e;}
                }
                EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
                Close();
                if(failure!=null || repaints<2){cleanup();StudioPaintingValidation.Fail(failure??new Exception("No native panel repaint"));return;}
                StudioPaintingValidation.Check(true,"Native painting panels laid out and repainted");complete();
            }
        }
    }

    internal sealed partial class StudioModelEditor
    {
        internal void ValidatePaintingLightRig(bool painting)
        {
            StudioPaintingValidation.Check(previewLighting.Key.enabled==painting && previewLighting.Fill.enabled==painting,"Painting owns enabled key/fill; Modeling restores disabled camera-light rig");
            int probes=Scene.GetRootGameObjects().Sum(root=>root.GetComponentsInChildren<ReflectionProbe>(true).Length);
            StudioPaintingValidation.Check(probes==(painting?1:0),"Painting reflection environment is created and removed without leaks");
        }
    }
}
