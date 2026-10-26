using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using MashBoxSDK.Maps.TerrainSystem;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainCameraSelectionValidation
    {
        [InitializeOnLoadMethod]
        static void Requested()
        {
            string path=Path.Combine(Path.GetTempPath(),"mg-terrain-camera-validation-request.txt");
            if(File.Exists(path) && File.ReadAllText(path).Trim().Replace('\\','/')==Application.dataPath.Replace('\\','/'))
                EditorApplication.delayCall+=()=>{if(EditorApplication.isPlayingOrWillChangePlaymode)return; File.Delete(path);Run();};
        }
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Gameplay Camera Selection")]
        public static void Run()
        {
            var scene=EditorSceneManager.NewPreviewScene();
            var root=new GameObject("Camera Selection Validation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            RenderTexture rt=null;
            string result=Path.Combine(Path.GetTempPath(),"mg-terrain-camera-validation-result.txt");
            try
            {
                var tileObject=new GameObject("Inactive Tile"); tileObject.SetActive(false);tileObject.transform.SetParent(root.transform);
                var tile=tileObject.AddComponent<MGTerrain>();
                Camera MakeCamera(string name,string tag){var go=new GameObject(name);go.transform.SetParent(root.transform);go.tag=tag;return go.AddComponent<Camera>();}
                var fallback=MakeCamera("CBBrain","EditorOnly");
                var other=MakeCamera("Other View","Untagged");
                var main=MakeCamera("Main View","MainCamera");
                var method=typeof(MGTerrain).GetMethod("IsDensityDetailStreamingCamera",BindingFlags.Instance|BindingFlags.NonPublic);
                bool Accept(Camera c)=>(bool)method.Invoke(tile,new object[]{c});
                void Check(bool condition,string label){if(!condition)throw new Exception(label);}
                Check(Accept(fallback),"EditorOnly-tagged Game camera must stream without MainCamera.");
                Check(!Accept(other),"Keep a stable fallback when another Game camera renders.");
                Check(Accept(main),"Promote an actual MainCamera.");
                Check(!Accept(fallback),"Fallback must not displace active MainCamera.");
                main.enabled=false;
                Check(Accept(other),"Replace a disabled cached camera.");
                rt=new RenderTexture(8,8,0);
                fallback.targetTexture=rt;
                Check(!Accept(fallback),"Offscreen untagged captures do not claim streaming.");
                fallback.targetTexture=null;
                other.cullingMask=0;
                Check(Accept(fallback),"Replace a camera that stopped drawing the terrain layer.");
                fallback.cullingMask=0;
                Check(!Accept(fallback),"Respect camera terrain-layer masks.");
                main.enabled=true; main.targetTexture=rt;
                Check(Accept(main),"Tagged MainCamera can render gameplay to a texture.");

                // Edit Mode uses the same resident GPU state as gameplay. A rejected
                // camera must neither schedule nor submit another camera's selection.
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var resident = typeof(MGTerrain).GetProperty("UseResidentDetailStreaming", flags);
                if ((bool)resident.GetValue(tile))
                {
                    fallback.cullingMask = -1;
                    fallback.targetTexture = rt;
                    var worldObject = new GameObject("Inactive Validation World");
                    worldObject.SetActive(false);
                    worldObject.transform.SetParent(root.transform);
                    var world = worldObject.AddComponent<MGTerrainWorld>();
                    var owner = typeof(MGTerrain).GetField("m_World", flags);
                    owner.SetValue(tile, world);
                    var pending = typeof(MGTerrain).GetField("m_WorldPendingDraw", flags);
                    var prepare = typeof(MGTerrain).GetMethod("PrepareWorldCamera", flags);
                    prepare.Invoke(tile, new object[] { fallback, 100f });
                    Check(!(bool)pending.GetValue(tile), "Rejected offscreen camera cannot schedule cached detail submission in Edit Mode.");
                    var submitted = typeof(MGTerrain).GetField("m_LastSubmittedDensityDetailInstances", flags);
                    var submit = typeof(MGTerrain).GetMethod("SubmitWorldDetails", flags);
                    pending.SetValue(tile, true);
                    submitted.SetValue(tile, 123);
                    submit.Invoke(tile, new object[] { fallback, 0 });
                    Check(Convert.ToInt64(submitted.GetValue(tile)) == 123, "Rejected camera cannot clear resident draw visibility.");
                    pending.SetValue(tile, false);
                    owner.SetValue(tile, null);
                }

                var map=tileObject.AddComponent<MGGrassInteractionMap>();
                var observe=typeof(MGGrassInteractionMap).GetMethod("ObserveGameCamera",BindingFlags.Instance|BindingFlags.NonPublic);
                var focus=typeof(MGGrassInteractionMap).GetMethod("FocusPosition",BindingFlags.Instance|BindingFlags.NonPublic);
                if (observe != null && focus != null)
                {
                fallback.transform.position=new Vector3(57,23,-82);
                observe.Invoke(map,new object[]{default(ScriptableRenderContext),fallback});
                Check((Vector3)focus.Invoke(map,null)==fallback.transform.position,"Interaction follows untagged Game camera.");
                main.transform.position=new Vector3(123,45,67);
                observe.Invoke(map,new object[]{default(ScriptableRenderContext),main});
                Check((Vector3)focus.Invoke(map,null)==main.transform.position,"Interaction prefers MainCamera when it renders.");
                }
                string report="PASS: untagged/EditorOnly Game camera fallback, stable selection, MainCamera promotion, disabled/layer-excluded camera replacement, offscreen capture rejection, MainCamera render-texture support, Edit Mode world submission ownership. Interaction observer checks: "
                    + (observe != null && focus != null ? "passed." : "skipped (observer API absent).");
                File.WriteAllText(result,report); Debug.Log(report);
            }
            catch(Exception e){File.WriteAllText(result,"FAIL: "+e);Debug.LogException(e);}
            finally{UnityEngine.Object.DestroyImmediate(root);if(rt!=null)UnityEngine.Object.DestroyImmediate(rt);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
