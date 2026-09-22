using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainDataCleanup
    {
        internal sealed class Item
        {
            internal Object Asset;
            internal string Source, Destination;
            internal readonly List<Action<Object>> Assign = new List<Action<Object>>();
            internal bool Copy;
        }
        internal sealed class Plan
        {
            internal UnityEngine.SceneManagement.Scene Scene;
            internal string Folder;
            internal readonly List<Item> Items = new List<Item>();
            internal readonly List<GameObject> ObsoleteSurfaces = new List<GameObject>();
            internal readonly HashSet<string> ProtectedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        internal static bool IsGeneratedMaterialTexture(string property)
        {
            // Opt in only known terrain outputs. In particular BaseMap00/NormalMap00/
            // MaskMap00 and ordinary material textures are source library assets.
            switch (property)
            {
                case "_ControlMap1": case "_ControlMap2": case "_FarGrassBake":
                case "_FarRangeAppearanceMap": case "_FarRangeAppearanceNormalMap":
                case "_DistantSurfaceHeightMap":
                case "_BaseMapArray": case "_TrailAlbedoArray": case "_AlbedoArray":
                case "_HeightMapArray": case "_TrailHeightArray": case "_HeightArray":
                case "_SurfaceMapArray": case "_TrailSurfaceArray": case "_SurfaceArray":
                    return true;
                default: return false;
            }
        }
        internal static Plan Build(MGTerrainWorld world)
        {
            var scene = world.gameObject.scene;
            if (string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Save the scene before organizing its terrain data.");
            var plan = new Plan { Scene = scene, Folder = MGTerrainSceneAssets.Folder(scene) };
            var items = new Dictionary<Object,Item>();
            var terrains = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MGTerrain>(true)).ToArray();
            var sourceTextures = new HashSet<Object>();
            void Protect(Texture texture)
            {
                if (texture == null) return;
                sourceTextures.Add(texture);
                string path = AssetDatabase.GetAssetPath(texture);
                if (!string.IsNullOrEmpty(path)) plan.ProtectedSources.Add(path);
            }
            // Asset identity wins over a generated-looking filename or an alias in another slot.
            foreach (string guid in AssetDatabase.FindAssets("t:TerrainLayer"))
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(guid));
                if (layer == null) continue;
                Protect(layer.diffuseTexture); Protect(layer.normalMapTexture); Protect(layer.maskMapTexture);
            }
            foreach (var tile in terrains)
            {
                var material = tile.MeshRenderer != null ? tile.MeshRenderer.sharedMaterial : null;
                if (material == null) continue;
                foreach (string property in material.GetTexturePropertyNames())
                    if (!IsGeneratedMaterialTexture(property)) Protect(material.GetTexture(property));
                // Legacy serialized slots can remain after their shader no longer exposes them.
                var slots = new SerializedObject(material).FindProperty("m_SavedProperties.m_TexEnvs");
                if (slots != null)
                    for (int i = 0; i < slots.arraySize; i++)
                    {
                        var slot = slots.GetArrayElementAtIndex(i);
                        if (!IsGeneratedMaterialTexture(slot.FindPropertyRelative("first").stringValue))
                            Protect(slot.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture);
                    }
            }
            void Add(Object asset,MGTerrain tile,string role,Action<Object> assign=null,bool forceCopy=false)
            {
                if (asset == null || sourceTextures.Contains(asset) || !(asset is Texture || asset is Mesh || asset is Material)) return;
                string source = AssetDatabase.GetAssetPath(asset);
                // Built-in shader defaults are engine resources, not scene terrain data.
                if (!string.IsNullOrEmpty(source) && !source.StartsWith("Assets/", StringComparison.Ordinal) && !source.StartsWith("Packages/", StringComparison.Ordinal)) return;
                if (items.TryGetValue(asset,out var shared) && !forceCopy)
                { if(assign!=null) shared.Assign.Add(assign); return; }
                bool copy=forceCopy || string.IsNullOrEmpty(source) || !source.StartsWith("Assets/",StringComparison.Ordinal)
                    || (asset is Mesh && Path.GetExtension(source)!=".asset");
                string ext=asset is Material ? ".mat" : copy ? ".asset" : Path.GetExtension(source);
                var item = new Item {Asset=asset,Source=source,Destination=MGTerrainAssetStore.PathFor(tile,role,ext),Copy=copy};
                if(assign!=null)item.Assign.Add(assign);
                if(!forceCopy)items.Add(asset,item);
                plan.Items.Add(item);
            }
            foreach(var tile in terrains)
            {
                if(tile.MeshRenderer!=null)
                {
                    var renderer=tile.MeshRenderer; var material=renderer.sharedMaterial;
                    bool shared=material!=null && (terrains.Count(t=>t.MeshRenderer!=null && t.MeshRenderer.sharedMaterial==material)>1
                        || scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MGTerrainWorld>(true)).Any(w=>w.TileMaterial==material));
                    Add(material,tile,"Material",o=>{var slots=renderer.sharedMaterials;slots[0]=(Material)o;renderer.sharedMaterials=slots;EditorUtility.SetDirty(renderer);},shared);
                    if(material!=null)
                        foreach(string property in material.GetTexturePropertyNames())
                        {
                            if (!IsGeneratedMaterialTexture(property)) continue;
                            var texture=material.GetTexture(property); string role=property.TrimStart('_');
                            if(property==MGTerrainAppearanceCaptureAssets.ColourProperty) role=IsDistant(texture)?"Distant":"Appearance";
                            if(property==MGTerrainAppearanceCaptureAssets.NormalProperty)role=IsDistant(texture)?"Distant_NormalWS":"Appearance_NormalWS";
                            if(property=="_DistantSurfaceHeightMap")role="Distant_Height";
                            // Shader/library source assets stay dependencies; scene-local texture data is organized.
                            if(texture is Texture2D || texture is Texture2DArray)
                                Add(texture,tile,role,o=>{renderer.sharedMaterial.SetTexture(property,(Texture)o);EditorUtility.SetDirty(renderer.sharedMaterial);});
                        }
                }
                foreach(var component in tile.GetComponents<Component>())
                {
                    if(component==null || component is Transform || component is Renderer)continue;
                    var serialized=new SerializedObject(component);var iterator=serialized.GetIterator();
                    while(iterator.Next(true))
                    {
                        if(iterator.propertyType!=SerializedPropertyType.ObjectReference)continue;
                        string property=iterator.propertyPath;
                        // Never relocate foliage prefab source meshes or materials.
                        if(property.StartsWith("m_Prototypes",StringComparison.Ordinal))continue;
                        var value=iterator.objectReferenceValue;
                        if(!(value is Texture2D) && !(value is Mesh))continue;
                        string role=property.Replace("m_","").Replace(".Array.data[","_").Replace("]","").Replace('.','_');
                        if(property=="m_Mesh")role="Mesh";
                        var layerMatch = System.Text.RegularExpressions.Regex.Match(property, @"m_DensityDetailLayers\.Array\.data\[(\d+)\]\.m_(DensityMap|SizeMap|GrassIdMap)$");
                        if (layerMatch.Success) role = "Layer_" + layerMatch.Groups[1].Value + "_" + (layerMatch.Groups[2].Value == "GrassIdMap" ? "GrassIDs" : layerMatch.Groups[2].Value.Replace("Map", ""));
                        if (layerMatch.Success && layerMatch.Groups[2].Value == "DensityMap")
                        {
                            var layer = tile.DensityDetailLayers[int.Parse(layerMatch.Groups[1].Value)];
                            if (layer.GeneratedByPalette != null)
                                role = MGDetailFoliagePaletteBaker.DensityAssetRole(layer.GeneratedByPalette, layer.PaletteSourceMap, layer.PaletteEntryIndex);
                        }
                        Add(value,tile,role,o=>{var so=new SerializedObject(component);so.FindProperty(property).objectReferenceValue=o;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(component);});
                    }
                }
                foreach(var distant in tile.GetComponentsInChildren<MGTerrainDistantSurface>(true))
                {
                    if(distant.Source!=tile)continue;
                    var terrainMaterial = tile.MeshRenderer != null ? tile.MeshRenderer.sharedMaterial : null;
                    if (!distant.gameObject.activeSelf && terrainMaterial != null
                        && terrainMaterial.HasProperty("_DistantSurfaceHeightMap") && terrainMaterial.GetTexture("_DistantSurfaceHeightMap") != null
                        && terrainMaterial.HasProperty("_DistantSurfaceStrength") && terrainMaterial.GetFloat("_DistantSurfaceStrength") > 0)
                    {
                        // Legacy morph bakes hid this generated proxy rather than removing it.
                        // Its references must not keep obsolete mesh/material outputs alive.
                        plan.ObsoleteSurfaces.Add(distant.gameObject);
                        continue;
                    }
                    foreach(var filter in distant.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null) continue;
                        var match = System.Text.RegularExpressions.Regex.Match(filter.sharedMesh.name, @"(?:Distant_Mesh_|Surface_)(\d+)_(\d+)$");
                        string role = match.Success ? "Distant_Mesh_" + match.Groups[1].Value + "_" + match.Groups[2].Value : "Distant_Mesh_" + filter.transform.GetSiblingIndex();
                        Add(filter.sharedMesh,tile,role,o=>{filter.sharedMesh=(Mesh)o;EditorUtility.SetDirty(filter);});
                    }
                    foreach(var renderer in distant.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Add(renderer.sharedMaterial,tile,"Distant_Surface",o=>{renderer.sharedMaterial=(Material)o;EditorUtility.SetDirty(renderer);});
                        if(renderer.sharedMaterial!=null)
                            Add(renderer.sharedMaterial.GetTexture("_BaseMap"),tile,"Distant");
                    }
                }
            }
            // Multiple subassets of one native file move as one file, preserving fileIDs.
            var destinations = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in plan.Items)
                if(!item.Copy)
                {
                    if(destinations.TryGetValue(item.Source,out var destination))item.Destination=destination;
                    else destinations.Add(item.Source,item.Destination);
                }
            foreach (var item in plan.Items)
                if (!item.Copy && item.Asset is Texture2D && item.Assign.Count > 1
                    && item.Destination.Contains("_Layer_"))
                    item.Destination = plan.Folder + "/Shared_" + AssetDatabase.AssetPathToGUID(item.Source).Substring(0, 8) + "_" + Path.GetFileName(item.Destination);
            return plan;
        }
        static bool IsDistant(Object asset)=>asset!=null && Path.GetFileName(AssetDatabase.GetAssetPath(asset)).IndexOf("_Distant",StringComparison.OrdinalIgnoreCase)>=0;

        internal static HashSet<string> Referenced(Plan plan)
        {
            var live = new HashSet<string>(plan.ProtectedSources, StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            {
                var scene=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if(!scene.isLoaded)continue;
                foreach(var dependency in EditorUtility.CollectDependencies(scene.GetRootGameObjects()))
                { string path=AssetDatabase.GetAssetPath(dependency);if(!string.IsNullOrEmpty(path))live.Add(path); }
            }
            // Outside assets are roots too, including other scenes and prefabs not loaded.
            string[] roots=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)
                && !p.StartsWith(plan.Folder+"/",StringComparison.OrdinalIgnoreCase) && p!=plan.Scene.path && !AssetDatabase.IsValidFolder(p)).ToArray();
            foreach(string dependency in AssetDatabase.GetDependencies(roots,true))live.Add(dependency);
            foreach(var root in plan.Scene.GetRootGameObjects())
                foreach(var tile in root.GetComponentsInChildren<MGTerrain>(true))
                    foreach(string role in new[]{"Appearance","Appearance_NormalWS","Distant","Distant_NormalWS","Distant_Height"})
                        foreach(string extension in new[]{".png",".asset"})
                        {
                            string path=MGTerrainAssetStore.PathFor(tile,role,extension);
                            if(File.Exists(path))live.Add(path);
                        }
            // MG shader links and TerrainLayer selections are GUID-valued material tags,
            // not Unity object fields, so GetDependencies alone cannot see them.
            var materials = new Queue<string>(roots.Concat(live).Where(p=>p.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)).Distinct());
            var checkedMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while(materials.Count>0)
            {
                string path=materials.Dequeue(); if(!checkedMaterials.Add(path) || !File.Exists(path))continue;
                foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path), @"\b[0-9a-fA-F]{32}\b"))
                {
                    string referenced=AssetDatabase.GUIDToAssetPath(match.Value);
                    if(string.IsNullOrEmpty(referenced))continue;
                    live.Add(referenced);
                    if(referenced.EndsWith(".mat",StringComparison.OrdinalIgnoreCase))materials.Enqueue(referenced);
                }
            }
            // Keep dependency chains originating in the data folder as well.
            foreach(string dependency in AssetDatabase.GetDependencies(live.ToArray(),true))live.Add(dependency);
            return live;
        }
        internal static string[] Unused(Plan plan)
        {
            var keep=Referenced(plan);
            return AssetDatabase.FindAssets("",new[]{plan.Folder}).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p=>!AssetDatabase.IsValidFolder(p) && !keep.Contains(p)).ToArray();
        }
        internal static void Show(MGTerrainWorld world)
        {
            try
            {
                var plan=Build(world);
                string[] unused=Unused(plan);
                string message=$"Organize {plan.Items.Count} terrain asset references for all tiles in this scene.\n\n{plan.Folder}\n\nMaterials and maps keep their references. Shared materials are separated per tile. The scene will be saved. {plan.ObsoleteSurfaces.Count} obsolete inactive distant-surface objects will be removed. Unused files are moved to the OS trash after checking references again.\n\nCurrently unused: {unused.Length} files.\n"+string.Join("\n",unused.Take(8).Select(Path.GetFileName));
                if(!EditorUtility.DisplayDialog("Clean Terrain Data",message,"Clean and Save Scene","Cancel"))return;
                Execute(plan);
            }
            catch(Exception e){Debug.LogException(e,world);EditorUtility.DisplayDialog("Terrain Data Cleanup",e.Message,"OK");}
        }
        internal static void Execute(Plan plan)
        {
            var moves=new List<(string from,string to)>();
            using var transaction=new MGTerrainAssetTransaction();
            bool committed=false;
            UnityEngine.SceneManagement.Scene retiredScene = default;
            try
            {
                // Vacate destinations first. Moving retains GUIDs, including references from other scenes.
                var moved=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach(var item in plan.Items.Where(i=>!i.Copy && i.Source!=i.Destination))
                {
                    if(moved.ContainsKey(item.Source))continue;
                    string temp=AssetDatabase.GenerateUniqueAssetPath(plan.Folder+"/Cleanup_"+Path.GetFileName(item.Source));
                    Move(item.Source,temp);moves.Add((item.Source,temp));moved.Add(item.Source,temp);
                }
                foreach(var item in plan.Items)
                {
                    if(item.Copy)
                    {
                        var copy=Object.Instantiate(item.Asset);
                        try
                        {
                            transaction.OnRollback(() => { foreach(var assign in item.Assign) assign(item.Asset); });
                            Object saved;
                            if(copy is Material mat)saved=MGTerrainAssetStore.Save(mat,item.Destination,transaction);
                            else if(copy is Mesh mesh)saved=MGTerrainAssetStore.Save(mesh,item.Destination,transaction);
                            else if(copy is Texture2D texture)saved=MGTerrainAssetStore.Save(texture,item.Destination,transaction);
                            else if(copy is Texture2DArray array)saved=MGTerrainAssetStore.Save(array,item.Destination,transaction);
                            else throw new InvalidOperationException("Unsupported terrain asset: "+copy.GetType());
                            foreach(var assign in item.Assign)assign(saved);
                        }
                        finally{if(!EditorUtility.IsPersistent(copy))Object.DestroyImmediate(copy);}
                    }
                    else if(moved.TryGetValue(item.Source,out string current))
                    {
                        if(File.Exists(item.Destination))
                        {
                            string displaced=AssetDatabase.GenerateUniqueAssetPath(plan.Folder+"/Unused_"+Path.GetFileName(item.Destination));
                            Move(item.Destination,displaced);moves.Add((item.Destination,displaced));
                        }
                        Move(current,item.Destination);moves.Add((current,item.Destination));moved.Remove(item.Source);

                    }
                }
                // Stage obsolete objects outside the saved scene so failure can restore
                // their exact references/hierarchy. Delete only after the scene save succeeds.
                if (plan.ObsoleteSurfaces.Count > 0)
                {
                    retiredScene = EditorSceneManager.NewPreviewScene();
                    foreach (var obsolete in plan.ObsoleteSurfaces)
                    {
                        if (obsolete == null) continue;
                        var parent = obsolete.transform.parent;
                        int sibling = obsolete.transform.GetSiblingIndex();
                        var originalScene = obsolete.scene;
                        transaction.OnRollback(() => {
                            if (obsolete == null) return;
                            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obsolete, originalScene);
                            obsolete.transform.SetParent(parent, false);
                            obsolete.transform.SetSiblingIndex(sibling);
                        });
                        obsolete.transform.SetParent(null, false);
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obsolete, retiredScene);
                    }
                }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(plan.Scene);
                if(!EditorSceneManager.SaveScene(plan.Scene))throw new IOException("Scene could not be saved. Cleanup stopped before removing unused files.");
                transaction.Commit();committed=true;
                if (retiredScene.IsValid()) EditorSceneManager.ClosePreviewScene(retiredScene);
                string[] unused=Unused(plan);
                int removed=0;
                foreach(string path in unused)
                    if(AssetDatabase.MoveAssetToTrash(path))removed++;
                Debug.Log($"Terrain data cleanup complete: {plan.Items.Count} asset references organized; {removed} unused files moved to trash. {plan.Folder}");
            }
            finally
            {
                transaction.Dispose();
                if (retiredScene.IsValid() && retiredScene.isLoaded) EditorSceneManager.ClosePreviewScene(retiredScene);
                if(!committed)
                    for(int i=moves.Count-1;i>=0;i--)
                    {var move=moves[i]; if(File.Exists(move.to) && !File.Exists(move.from))Move(move.to,move.from);}
                EditorUtility.ClearProgressBar();
            }
        }
        static void Move(string from,string to)
        {
            string error=AssetDatabase.MoveAsset(from,to);
            if(!string.IsNullOrEmpty(error))throw new IOException(error);
        }
    }
}
