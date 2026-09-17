#if UNITY_EDITOR
using System;
using System.IO;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        [InitializeOnLoadMethod]
        static void QueueSharedDetailsValidation()
        {
            EditorApplication.delayCall += ValidateSharedDetailsOnce;
        }
        static void ValidateSharedDetailsOnce()
        {
            const string report = "C:/Users/matth/AppData/Local/Temp/MGWorldSharedDetails/validation.txt";
            if (File.Exists(report) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            GameObject first = null, second = null;
            Texture2D sourceMap = null, destinationMap = null;
            int undo = Undo.GetCurrentGroup();
            Undo.IncrementCurrentGroup();
            int testUndo = Undo.GetCurrentGroup();
            try
            {
                first = new GameObject("SharedDetailsValidationSource") { hideFlags = HideFlags.HideAndDontSave };
                first.SetActive(false);
                second = new GameObject("SharedDetailsValidationDestination") { hideFlags = HideFlags.HideAndDontSave };
                second.SetActive(false);
                var source = first.AddComponent<MGTerrain>();
                var destination = second.AddComponent<MGTerrain>();
                sourceMap = new Texture2D(2,2,TextureFormat.R16,false,true);
                destinationMap = new Texture2D(2,2,TextureFormat.R16,false,true);
                foreach (var tile in new[] { source, destination })
                {
                    using var data = new SerializedObject(tile);
                    data.FindProperty("m_Prototypes").arraySize = 1;
                    var layers = data.FindProperty("m_DensityDetailLayers");
                    layers.arraySize = 2;
                    for (int i = 0; i < 2; i++)
                    {
                        var entry = layers.GetArrayElementAtIndex(i);
                        entry.FindPropertyRelative("m_PrototypeIndex").intValue = 0;
                        entry.FindPropertyRelative("m_DensityMap").objectReferenceValue = tile == source ? sourceMap : destinationMap;
                        entry.FindPropertyRelative("m_SizeMultiplier").floatValue = tile == source ? 3 : 1;
                    }
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                SharedIds(source);
                MergeSharedDefinitions(source,destination,true,null);
                if (destination.DensityDetailLayerCount != 2) throw new Exception("Duplicate layer count changed");
                for (int i = 0; i < 2; i++)
                {
                    if (destination.DensityDetailLayers[i].DensityMap != destinationMap) throw new Exception("Painted density overwritten");
                    if (destination.DensityDetailLayers[i].WorldDetailId != source.DensityDetailLayers[i].WorldDetailId) throw new Exception("Shared IDs differ");
                    if (FindWorldPaintLayer(source,i,destination) != i) throw new Exception("Duplicate paint routing failed");
                }
                using (var data = new SerializedObject(source))
                {
                    data.FindProperty("m_DensityDetailLayers").GetArrayElementAtIndex(1).FindPropertyRelative("m_RenderDisabled").boolValue = true;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                MergeSharedDefinitions(source,destination,true,null);
                if (destination.DensityDetailLayers[1].RenderingEnabled) throw new Exception("Visibility did not propagate");
                if (!destination.DensityDetailLayers[0].RenderingEnabled) throw new Exception("Wrong duplicate hidden");
                var deleted = source.DensityDetailLayers[0].WorldDetailId;
                using (var data = new SerializedObject(source))
                {
                    data.FindProperty("m_DensityDetailLayers").DeleteArrayElementAtIndex(0);
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                MergeSharedDefinitions(source,destination,true,new System.Collections.Generic.HashSet<string> {deleted});
                if (destination.DensityDetailLayerCount != 1 || destination.DensityDetailLayers[0].DensityMap != destinationMap)
                    throw new Exception("Removal did not preserve surviving painting");
                File.WriteAllText(report,"PASS: existing density maps preserved; duplicate layers retain separate IDs; painting routes by shared ID; visibility propagates; removal preserves surviving painted layers.");
            }
            catch (Exception error) { File.WriteAllText(report,"FAIL: " + error); Debug.LogException(error); }
            finally
            {
                Undo.RevertAllDownToGroup(testUndo);
                if (first != null) DestroyImmediate(first);
                if (second != null) DestroyImmediate(second);
                if (sourceMap != null) DestroyImmediate(sourceMap);
                if (destinationMap != null) DestroyImmediate(destinationMap);
            }
        }
    }
}
#endif
