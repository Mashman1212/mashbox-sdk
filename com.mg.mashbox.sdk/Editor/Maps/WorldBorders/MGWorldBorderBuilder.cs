using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools.WorldBorders
{
    public static class MGWorldBorderBuilder
    {
        public const string ShaderPath = "Packages/com.mg.mashbox.sdk/Shaders/WorldBorders/MGWorldBorder.shadergraph";
        public const string DefaultMaterialPath = "Packages/com.mg.mashbox.sdk/Shaders/WorldBorders/World Border Default.mat";
        public const string DefaultAlbedoPath = "Packages/com.mg.mashbox.sdk/Shaders/WorldBorders/WORLD BORDER GRAPHIC.jpeg";
        public const int MaxTiles = 8192;
        const string Visuals = "Visual Tiles";
        const string Collisions = "Collision Walls";

        public static Vector2[] Outline(MGWorldBorderProfile p)
        {
            if (p.outline == MGWorldBorderProfile.Outline.Polygon) return p.points.ToArray();
            var h = p.size * .5f;
            return new[] { new Vector2(-h.x,-h.y), new Vector2(h.x,-h.y), new Vector2(h.x,h.y), new Vector2(-h.x,h.y) };
        }

        public static int TileCount(MGWorldBorderProfile p)
        {
            if (!Finite(p.tileSize) || p.tileSize < 1 || !Finite(p.height) || p.height <= 0) return 0;
            var outline = Outline(p);
            double count = 0;
            for (int i=0; i<outline.Length; i++)
                count += Math.Ceiling(Vector2.Distance(outline[i], outline[(i+1)%outline.Length]) / p.tileSize) * Math.Ceiling(p.height / p.tileSize);
            return count >= int.MaxValue || double.IsNaN(count) ? int.MaxValue : (int)count;
        }

        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static float Cross(Vector2 a, Vector2 b) => a.x*b.y-a.y*b.x;
        static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float ab = Cross(b-a,c-a), ac = Cross(b-a,d-a), cd = Cross(d-c,a-c), ce = Cross(d-c,b-c);
            const float e = .0001f;
            bool On(Vector2 p, Vector2 q, Vector2 r) => r.x >= Mathf.Min(p.x,q.x)-e && r.x <= Mathf.Max(p.x,q.x)+e && r.y >= Mathf.Min(p.y,q.y)-e && r.y <= Mathf.Max(p.y,q.y)+e;
            return (ab*ac < 0 && cd*ce < 0) || (Mathf.Abs(ab)<e && On(a,b,c)) || (Mathf.Abs(ac)<e && On(a,b,d)) || (Mathf.Abs(cd)<e && On(c,d,a)) || (Mathf.Abs(ce)<e && On(c,d,b));
        }

        public static string Problem(MGWorldBorderProfile p)
        {
            if (!p) return "Choose or create border settings.";
            if (new[] {p.origin.x,p.origin.y,p.origin.z,p.rotation,p.size.x,p.size.y,p.height,p.tileSize,p.visibilityDistance,p.fadeWidth,p.patternSize,p.opacity,p.collisionThickness,p.maxVerticalFov,p.minLodBias,p.cullPadding}.Any(v=>!Finite(v))) return "Enter finite numbers for all dimensions and distances.";
            if (p.height<=0 || p.size.x<=0 || p.size.y<=0 || p.tileSize<8) return "Width, depth and height must be positive. Tiles must be at least 8 metres.";
            if (p.visibilityDistance<=0 || p.fadeWidth<=0 || p.fadeWidth>p.visibilityDistance) return "Fade width must be positive and no greater than visibility distance.";
            if (p.patternSize<=0 || p.opacity<0 || p.opacity>1 || p.collisionThickness<=0 || p.cullPadding<0) return "Check pattern size, opacity, collision thickness and culling margin.";
            if (p.maxVerticalFov<30 || p.maxVerticalFov>150 || p.minLodBias<.1f || p.minLodBias>4) return "Use a vertical FOV of 30–150 degrees and minimum LOD bias of 0.1–4.";
            if (p.layer<0 || p.layer>31 || !UnityEditorInternal.InternalEditorUtility.tags.Contains(p.collisionTag)) return "Choose a valid layer and collision tag.";
            if (p.materialTemplate && (!p.materialTemplate.HasProperty("_VISIBILITY_DISTANCE") || !p.materialTemplate.HasProperty("_FADE_WIDTH") || !p.materialTemplate.HasProperty("_BORDER_OPACITY"))) return "Use a World Border material template with the supported fade properties.";
            var v = Outline(p);
            if (v.Length<3 || v.Length>128) return "A polygon needs 3–128 points.";
            if (v.Any(a=>!Finite(a.x)||!Finite(a.y))) return "Polygon points must be finite.";
            float area = 0;
            for (int i=0;i<v.Length;i++)
            {
                int n=(i+1)%v.Length;
                if (Vector2.Distance(v[i],v[n])<.1f) return "Adjacent polygon points must be at least 10 cm apart.";
                area += Cross(v[i],v[n]);
                for (int j=i+1;j<v.Length;j++)
                {
                    if (j==n || (j+1)%v.Length==i) continue;
                    if (Intersects(v[i],v[n],v[j],v[(j+1)%v.Length])) return "Polygon edges must not cross or overlap.";
                }
            }
            if (Mathf.Abs(area)<1) return "The outline must enclose an area.";
            if (TileCount(p)>MaxTiles) return "This outline exceeds 8,192 tiles. Increase tile size or reduce wall height.";
            return null;
        }

        public static string SettingsKey(MGWorldBorderProfile p)
        {
            var copy = Object.Instantiate(p);
            copy.rootId=copy.rootName=copy.sceneGuid=copy.generatedFolder=copy.builtSettings=null;
            try { return EditorJsonUtility.ToJson(copy); }
            finally { Object.DestroyImmediate(copy); }
        }

        public static GameObject ResolveRoot(MGWorldBorderProfile p)
        {
            if (!p) return null;
            if (!string.IsNullOrEmpty(p.rootId) && GlobalObjectId.TryParse(p.rootId, out var id))
            {
                var found = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                if (found) return found;
            }
            // Newly created scene objects acquire stable file IDs when the scene is saved.
            for (int i=0;i<SceneManager.sceneCount;i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (AssetDatabase.AssetPathToGUID(scene.path)!=p.sceneGuid) continue;
                var matches = scene.GetRootGameObjects().Where(g=>g.name==p.rootName).ToArray();
                if (matches.Length==1 && IsOwned(p,matches[0])) return matches[0];
            }
            return null;
        }

        public static bool IsOwned(MGWorldBorderProfile p, GameObject root)
        {
            if (!root || string.IsNullOrEmpty(p.generatedFolder)) return false;
            var visuals = root.transform.Find(Visuals);
            var mesh = visuals ? visuals.GetComponentInChildren<MeshFilter>(true) : null;
            return mesh && AssetDatabase.GetAssetPath(mesh.sharedMesh).StartsWith(p.generatedFolder + "/", StringComparison.Ordinal);
        }

        public static void Remember(MGWorldBorderProfile p, GameObject root)
        {
            p.rootId = GlobalObjectId.GetGlobalObjectIdSlow(root).ToString();
            p.sceneGuid = AssetDatabase.AssetPathToGUID(root.scene.path);
            p.rootName = root.name;
            EditorUtility.SetDirty(p);
        }

        public static GameObject Build(MGWorldBorderProfile p, GameObject existing = null)
        {
            var problem = Problem(p);
            if (problem!=null) throw new InvalidOperationException(problem);
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode to build borders.");
            if (!AssetDatabase.Contains(p)) throw new InvalidOperationException("Save the border settings asset first.");
            var scene = existing ? existing.scene : SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Save the scene before creating a border.");
            if (existing && (!IsOwned(p,existing) || PrefabUtility.IsPartOfPrefabInstance(existing) || existing.transform.parent))
                throw new InvalidOperationException("Select the original generated scene root. Prefab instances and parented roots must be unpacked and moved to the scene root first.");
            if (!existing && !string.IsNullOrEmpty(p.sceneGuid)) throw new InvalidOperationException("These settings already belong to a border. Open its scene and load it, or duplicate the settings for a new border.");
            EnsureFolder("Assets/World Borders");
            string assetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p));
            string folder = "Assets/World Borders/Generated-" + assetGuid;
            EnsureFolder(folder);
            string generation = AssetDatabase.GenerateUniqueAssetPath(folder + "/Build");
            AssetDatabase.CreateFolder(folder,Path.GetFileName(generation));
            var material = CreateMaterial(p,generation);
            GameObject root = null;
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Build world border");
            try
            {
                // Build a complete replacement before touching the current working border.
                root = new GameObject(existing ? existing.name : GameObjectUtility.GetUniqueNameForSibling(null,p.name));
                SceneManager.MoveGameObjectToScene(root,scene);
                Undo.RegisterCreatedObjectUndo(root,"Build world border");
                root.SetActive(false);
                root.transform.SetPositionAndRotation(p.origin,Quaternion.Euler(0,p.rotation,0));
                Populate(p,root,material,mesh => {
                    string path=generation+"/Tiles.asset";
                    if (!File.Exists(path)) AssetDatabase.CreateAsset(mesh,path); else AssetDatabase.AddObjectToAsset(mesh,path);
                });
                root.SetActive(existing ? existing.activeSelf : true);
                Verify(p,root);
                Undo.RecordObject(p,"Store border settings");
                p.generatedFolder=folder;
                if (existing)
                {
                    // Keep root identity and unrelated children so external scene references survive.
                    Undo.RecordObject(existing.transform,"Place world border");
                    existing.transform.SetPositionAndRotation(p.origin,Quaternion.Euler(0,p.rotation,0));
                    existing.transform.localScale=Vector3.one;
                    foreach(string name in new[]{Visuals,Collisions})
                    {
                        var old=existing.transform.Find(name);
                        if(old) Undo.DestroyObjectImmediate(old.gameObject);
                        Undo.SetTransformParent(root.transform.Find(name),existing.transform,"Replace border geometry");
                    }
                    Undo.DestroyObjectImmediate(root);
                    root=existing;
                }
                Remember(p,root);
                p.builtSettings=SettingsKey(p);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(group);
                return root;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                // Failed generation owns this fresh folder only; no previous build is removed.
                AssetDatabase.DeleteAsset(generation);
                throw;
            }
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent=Path.GetDirectoryName(folder).Replace('\\','/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));
        }

        static Material CreateMaterial(MGWorldBorderProfile p,string folder)
        {
            var template=p.materialTemplate ? p.materialTemplate : AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
            if (!template || !template.shader) throw new InvalidOperationException("World Border default material or shader is missing from the SDK.");
            var m=new Material(template);
            m.name=p.name+" Material";
            m.SetFloat("_VISIBILITY_DISTANCE",p.visibilityDistance);
            m.SetFloat("_FADE_WIDTH",p.fadeWidth);
            m.SetFloat("_BORDER_OPACITY",p.opacity);
            m.SetColor("_COLOUR_OVERLAY",p.color);
            // Preserve the template's emission and fade colours; the profile controls its tint.
            m.SetVector("_TILING",Vector4.one);
            m.SetVector("_OFFSET",Vector4.zero);
            m.SetFloat("_DoubleSidedEnable",1);
            m.SetFloat("_TransparentCullMode",0);
            m.SetFloat("_CullMode",0);
            m.SetFloat("_CullModeForward",0);
            Texture pattern=p.pattern ? p.pattern : template.GetTexture("_ALBEDO");
            if (!pattern) pattern=AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultAlbedoPath);
            if (!pattern) { Object.DestroyImmediate(m); throw new InvalidOperationException("World Border default albedo is missing from the SDK."); }
            m.SetTexture("_ALBEDO",pattern);
            HDShaderUtils.ResetMaterialKeywords(m);
            AssetDatabase.CreateAsset(m,folder+"/Border.mat");
            return m;
        }

        // Also used by isolated validation; sink persists meshes for real builds.
        public static void Populate(MGWorldBorderProfile p,GameObject root,Material material,Action<Mesh> sink)
        {
            var vertices=Outline(p);
            var visuals=new GameObject(Visuals); visuals.transform.SetParent(root.transform,false);
            var collisions=new GameObject(Collisions); collisions.transform.SetParent(root.transform,false);
            var cache=new Dictionary<string,Mesh>();
            int rows=Mathf.CeilToInt(p.height/p.tileSize);
            float dy=p.height/rows;
            for(int edge=0;edge<vertices.Length;edge++)
            {
                Vector2 a=vertices[edge],b=vertices[(edge+1)%vertices.Length];
                float length=Vector2.Distance(a,b);
                int columns=Mathf.CeilToInt(length/p.tileSize);
                float dx=length/columns;
                Vector3 start=new Vector3(a.x,0,a.y),direction=new Vector3(b.x-a.x,0,b.y-a.y).normalized;
                Quaternion rotation=Quaternion.LookRotation(Vector3.Cross(direction,Vector3.up),Vector3.up);
                if(p.collision)
                {
                    var wall=new GameObject("Wall "+(edge+1)); wall.transform.SetParent(collisions.transform,false);
                    wall.transform.localPosition=start+direction*(length*.5f)+Vector3.up*(p.height*.5f);
                    wall.transform.localRotation=rotation; wall.layer=p.layer; wall.tag=p.collisionTag;
                    var box=wall.AddComponent<BoxCollider>();
                    // Small end overlap closes seams at polygon corners.
                    box.size=new Vector3(length+p.collisionThickness,p.height,p.collisionThickness);
                    box.sharedMaterial=p.physicsMaterial;
                }
                for(int row=0;row<rows;row++) for(int col=0;col<columns;col++)
                {
                    // UV offsets are baked to maintain the same grid across tile boundaries.
                    string key=dx.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"_"+col+"_"+row;
                    if(!cache.TryGetValue(key,out var mesh))
                    {
                        mesh=Quad(dx,dy,col*dx/p.patternSize,row*dy/p.patternSize,p.patternSize);
                        mesh.name="Tile "+cache.Count;
                        sink(mesh); cache.Add(key,mesh);
                    }
                    var tile=new GameObject("Wall "+(edge+1)+" · "+(col+1)+","+(row+1));
                    tile.transform.SetParent(visuals.transform,false);
                    tile.transform.localPosition=start+direction*((col+.5f)*dx)+Vector3.up*((row+.5f)*dy);
                    tile.transform.localRotation=rotation; tile.layer=p.layer;
                    tile.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=tile.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
                    renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                    renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
                    renderer.rayTracingMode=UnityEngine.Experimental.Rendering.RayTracingMode.Off;
                    var lod=tile.AddComponent<LODGroup>(); lod.fadeMode=LODFadeMode.None;
                    lod.SetLODs(new[]{new LOD(.1f,new Renderer[]{renderer})}); lod.RecalculateBounds();
                    float radius=Mathf.Sqrt(dx*dx+dy*dy)*.5f;
                    float distance=p.visibilityDistance+radius+p.cullPadding;
                    float threshold=lod.size*p.minLodBias/(2*distance*Mathf.Tan(p.maxVerticalFov*Mathf.Deg2Rad*.5f));
                    lod.SetLODs(new[]{new LOD(Mathf.Min(.99f,threshold),new Renderer[]{renderer})});
                }
            }
        }

        static Mesh Quad(float w,float h,float u,float v,float repeat)
        {
            var m=new Mesh();
            m.vertices=new[]{new Vector3(-w/2,-h/2,0),new Vector3(w/2,-h/2,0),new Vector3(w/2,h/2,0),new Vector3(-w/2,h/2,0)};
            m.triangles=new[]{0,1,2,0,2,3};
            m.uv=new[]{new Vector2(u,v),new Vector2(u+w/repeat,v),new Vector2(u+w/repeat,v+h/repeat),new Vector2(u,v+h/repeat)};
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds(); return m;
        }

        public static string Verify(MGWorldBorderProfile p,GameObject root)
        {
            if(!root) throw new InvalidOperationException("Load the generated border first.");
            var visualRoot=root.transform.Find(Visuals);
            var collisionRoot=root.transform.Find(Collisions);
            if(!visualRoot || !collisionRoot) throw new InvalidOperationException("Generated border groups are missing.");
            var lods=visualRoot.GetComponentsInChildren<LODGroup>(true);
            if(lods.Length!=TileCount(p)) throw new InvalidOperationException("Tile count differs from these settings. Rebuild the border.");
            if(visualRoot.GetComponentsInChildren<MonoBehaviour>(true).Length!=0 || collisionRoot.GetComponentsInChildren<MonoBehaviour>(true).Length!=0) throw new InvalidOperationException("Generated border unexpectedly contains a runtime script.");
            int count=p.collision ? Outline(p).Length : 0;
            var colliders=collisionRoot.GetComponentsInChildren<Collider>(true);
            if(colliders.Length!=count || colliders.Any(c=>!c.enabled||c.isTrigger)) throw new InvalidOperationException("Collision walls are missing or disabled.");
            foreach(var lod in lods)
            {
                var levels=lod.GetLODs();
                var r=lod.GetComponent<MeshRenderer>();
                if(levels.Length!=1 || levels[0].renderers.Length!=1 || levels[0].renderers[0]!=r || !r.enabled) throw new InvalidOperationException("Unexpected LOD setup.");
                var mesh=lod.GetComponent<MeshFilter>().sharedMesh;
                if(!mesh || mesh.vertexCount!=4) throw new InvalidOperationException("A tile mesh is missing.");
                float radius=mesh.bounds.extents.magnitude;
                float distance=lod.size*p.minLodBias/(2*levels[0].screenRelativeTransitionHeight*Mathf.Tan(p.maxVerticalFov*Mathf.Deg2Rad*.5f));
                if(distance+.01f<p.visibilityDistance+radius+p.cullPadding) throw new InvalidOperationException("A tile can disappear before its fade completes.");
                if(!r.sharedMaterial || Mathf.Abs(r.sharedMaterial.GetFloat("_VISIBILITY_DISTANCE")-p.visibilityDistance)>.001f) throw new InvalidOperationException("Material visibility differs from the LOD settings. Rebuild the border.");
            }
            return lods.Length+" visual tiles · "+colliders.Length+" collision walls · no runtime scripts";
        }
    }
}
