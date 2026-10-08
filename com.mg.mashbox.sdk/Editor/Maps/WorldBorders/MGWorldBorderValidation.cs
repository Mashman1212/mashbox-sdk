using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools.WorldBorders
{
    [InitializeOnLoad]
    public static class MGWorldBorderValidation
    {
        static string Work => Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MGWorldBorderWindow).Assembly).resolvedPath,"Development~/WorldBorders");
        static MGWorldBorderValidation() { EditorApplication.delayCall+=Requested; }
        static void Requested()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall+=Requested; return; }
            string request=Path.Combine(Work,"validate.request");
            if(!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request); Run();
        }
        static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }

        [MenuItem("Tools/MashBox/World Borders/Run Validation")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            Directory.CreateDirectory(Work);
            var original=SceneManager.GetActiveScene();
            var selection=Selection.objects;
            string testFolder="Assets/World Borders/Validation-"+Guid.NewGuid().ToString("N");
            string generated=null;
            Scene scene=default;
            var results=new List<string>();
            try
            {
                MGWorldBorderBuilder.EnsureFolder(testFolder);
                scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                Check(EditorSceneManager.SaveScene(scene,testFolder+"/Validation.unity"),"Could not save isolated scene.");
                var p=ScriptableObject.CreateInstance<MGWorldBorderProfile>();
                p.name="Validation Border";
                Check(MGWorldBorderBuilder.ResolveRoot(p)==null,"Fresh settings should have no scene root.");
                p.rootId=null;
                Check(MGWorldBorderBuilder.ResolveRoot(p)==null,"Null scene IDs must be accepted for new settings.");
                AssetDatabase.CreateAsset(p,testFolder+"/Settings.asset");
                Check(MGWorldBorderBuilder.Problem(p)==null,"Default settings invalid.");
                Check(MGWorldBorderBuilder.TileCount(p)==32,"Default border must have 32 tiles.");
                var sdkMaterial=AssetDatabase.LoadAssetAtPath<Material>(MGWorldBorderBuilder.DefaultMaterialPath);
                var sdkAlbedo=AssetDatabase.LoadAssetAtPath<Texture2D>(MGWorldBorderBuilder.DefaultAlbedoPath);
                var sdkShader=AssetDatabase.LoadAssetAtPath<Shader>(MGWorldBorderBuilder.ShaderPath);
                Check(sdkMaterial && sdkAlbedo && sdkShader,"Packaged border defaults are missing.");
                Check(sdkMaterial.shader==sdkShader && sdkMaterial.GetTexture("_ALBEDO")==sdkAlbedo,"Default material does not reference SDK shader and albedo.");
                string materialBefore=EditorJsonUtility.ToJson(sdkMaterial);
                var root=MGWorldBorderBuilder.Build(p);
                generated=p.generatedFolder;
                Check(MGWorldBorderBuilder.Verify(p,root).Contains("32 visual tiles"),"Default build verification failed.");
                Check(root.GetComponentsInChildren<BoxCollider>().Length==4,"Default border must have four independent colliders.");
                var first=root.GetComponentInChildren<MeshRenderer>();
                Check(first.sharedMaterial.shader && !ShaderUtil.ShaderHasError(first.sharedMaterial.shader),"Default shader has compilation errors.");
                Check(first.sharedMaterial!=sdkMaterial && first.sharedMaterial.GetTexture("_ALBEDO")==sdkAlbedo,"Generated material must copy the SDK default and use its packaged graphic.");
                Check(materialBefore==EditorJsonUtility.ToJson(sdkMaterial),"Building mutated the shared SDK default material.");
                Check(!AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(first.sharedMaterial),true).Any(path=>path.StartsWith("Assets/Maps/",StringComparison.Ordinal)),"Default border depends on a map-specific asset.");
                results.Add("PASS: packaged shader, default material and exact albedo references; generated material is a private copy with no map-specific dependencies.");
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    Check(AssetDatabase.Contains(filter.sharedMesh),"Generated mesh was not persisted.");
                    Check(filter.sharedMesh.bounds.size.y==128,"Starter tiles should cover full 128 m height.");
                }
                results.Add("PASS: default 1024 m rectangle = 32 native LOD tiles, four independent colliders, persistent meshes and packaged HDRP artwork.");
                // Rebuild must retain the scene root and unrelated user children, and undo atomically.
                var extra=new GameObject("User-owned marker"); extra.transform.SetParent(root.transform,false);
                int rootId=root.GetInstanceID();
                var originalMesh=first.GetComponent<MeshFilter>().sharedMesh;
                p.tileSize=64;
                root=MGWorldBorderBuilder.Build(p,root);
                Check(root.GetInstanceID()==rootId && extra.transform.parent==root.transform,"Rebuild changed root identity or removed unrelated content.");
                Check(root.GetComponentsInChildren<LODGroup>().Length==128,"64 m tiles should produce 128 tiles.");
                Undo.PerformUndo();
                root=MGWorldBorderBuilder.ResolveRoot(p);
                Check(root && root.GetInstanceID()==rootId && root.GetComponentsInChildren<LODGroup>().Length==32,"Undo did not restore the first build.");
                Check(root.GetComponentInChildren<MeshFilter>().sharedMesh==originalMesh,"Undo lost the earlier mesh asset.");
                Undo.PerformRedo();
                Check(root.GetComponentsInChildren<LODGroup>().Length==128,"Redo did not restore the second build.");
                results.Add("PASS: rebuild, Undo and Redo preserve root identity, user children and previous mesh assets.");
                p.outline=MGWorldBorderProfile.Outline.Polygon;
                p.points=new List<Vector2>{new Vector2(0,0),new Vector2(200,0),new Vector2(200,100),new Vector2(100,100),new Vector2(100,200),new Vector2(0,200)};
                p.origin=new Vector3(17,-13,28); p.rotation=37; p.height=75; p.tileSize=128;
                Check(MGWorldBorderBuilder.Problem(p)==null,"Valid concave polygon rejected.");
                root=MGWorldBorderBuilder.Build(p,root);
                Check(root.GetComponentsInChildren<LODGroup>().Length==8,"Concave polygon tile count incorrect.");
                var walls=root.GetComponentsInChildren<BoxCollider>();
                Check(walls.Length==6,"Concave polygon needs six collision walls.");
                Physics.SyncTransforms();
                foreach(var wall in walls)
                {
                    Vector3 centre=wall.transform.TransformPoint(wall.center);
                    Vector3 normal=wall.transform.forward;
                    Check(wall.Raycast(new Ray(centre+normal*5,-normal),out var hit,10),"A rotated collision wall did not block a ray.");
                }
                // Check total surface area independent of tiling, and normals under rotation.
                float area=0;
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh=filter.sharedMesh; var v=mesh.vertices; var tris=mesh.triangles;
                    for(int i=0;i<tris.Length;i+=3) area+=Vector3.Cross(v[tris[i+1]]-v[tris[i]],v[tris[i+2]]-v[tris[i]]).magnitude*.5f;
                    Check(Vector3.Dot(mesh.normals[0],Vector3.forward)>.99f,"Unexpected tile winding.");
                }
                Check(Mathf.Abs(area-800*75)<1,"Tiled surface area differs from the polygon perimeter times wall height.");
                results.Add("PASS: rotated concave outline, expected surface area, six blocking collision walls.");
                p.collision=false;
                root=MGWorldBorderBuilder.Build(p,root);
                Check(root.GetComponentsInChildren<Collider>().Length==0,"Visual-only border retained colliders.");
                EditorSceneManager.SaveScene(scene);
                string saved=File.ReadAllText(scene.path);
                Check(!saved.Contains("MonoBehaviour:"),"Export scene contains custom runtime scripts.");
                Check(MGWorldBorderBuilder.ResolveRoot(p)==root,"Saved profile no longer resolves its border root.");
                results.Add("PASS: optional collision, scene save and profile lookup; output has no MonoBehaviours.");
                p.points=new List<Vector2>{new Vector2(0,0),new Vector2(100,100),new Vector2(0,100),new Vector2(100,0)};
                Check(MGWorldBorderBuilder.Problem(p)!=null,"Self-intersecting polygon accepted.");
                p.outline=MGWorldBorderProfile.Outline.Rectangle; p.tileSize=float.NaN;
                Check(MGWorldBorderBuilder.Problem(p)!=null,"NaN tile size accepted.");
                p.tileSize=8; p.height=10000;
                Check(MGWorldBorderBuilder.Problem(p)!=null,"Excessive tile count accepted.");
                p.height=128; p.tileSize=128; p.fadeWidth=p.visibilityDistance+1;
                Check(MGWorldBorderBuilder.Problem(p)!=null,"Invalid fade range accepted.");
                results.Add("PASS: invalid polygons, non-finite input, excessive tiling and unsafe fade settings rejected.");
                File.WriteAllLines(Path.Combine(Work,"validation.txt"),results);
                Debug.Log(string.Join("\n",results));
                MGWorldBorderWindow.Open();
            }
            catch(Exception e)
            {
                results.Add("FAIL: "+e); File.WriteAllLines(Path.Combine(Work,"validation.txt"),results); Debug.LogException(e);
            }
            finally
            {
                if(original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if(scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
                Selection.objects=selection;
                if(!string.IsNullOrEmpty(generated)) AssetDatabase.DeleteAsset(generated);
                AssetDatabase.DeleteAsset(testFolder);
            }
        }
    }
}
