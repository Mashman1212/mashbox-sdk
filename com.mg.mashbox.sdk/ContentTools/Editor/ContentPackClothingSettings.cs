using MashBoxSDK.Clothing;
using MashBoxSDK.Culling;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.ContentTools.Editor
{
    internal static class ContentPackClothingSettings
    {
        internal static ClothingClipType GetClippingType(GameObject prefab)
        {
            if(prefab.GetComponentInChildren<ClothingCullingSettings>(true))return ClothingClipType.Custom;
            var settings=prefab.GetComponent<ClothingClipSettings>();
            return settings?settings.clipType:ClothingClipType.None;
        }

        internal static ClothingCullingProfile GetProfile(GameObject prefab)
        {
            var culling=prefab.GetComponentInChildren<ClothingCullingSettings>(true);
            return culling?culling.profile:null;
        }

        internal static void Apply(GameObject prefab,ClothingClipType type,CharacterFeatureFlags features,ClothingCullingProfile profile)
        {
            bool changeCulling=GetClippingType(prefab)!=type ||
                (type==ClothingClipType.Custom && GetProfile(prefab)!=profile);
            var settings=prefab.GetComponent<ClothingClipSettings>();
            if(!settings)settings=Undo.AddComponent<ClothingClipSettings>(prefab);
            Undo.RecordObject(settings,"Modify Clothing Settings");
            settings.clipType=type;settings.disableFeatures=features;
            EditorUtility.SetDirty(settings);
            PrefabUtility.RecordPrefabInstancePropertyModifications(settings);
            if(changeCulling)
            {
                // The selection applies to the complete item. Retire child overrides
                // as well, otherwise they would continue to override the chosen preset.
                foreach(var previous in prefab.GetComponentsInChildren<ClothingCullingSettings>(true))
                    Undo.DestroyObjectImmediate(previous);
                if(type==ClothingClipType.Custom)
                {
                    var culling=Undo.AddComponent<ClothingCullingSettings>(prefab);
                    Undo.RecordObject(culling,"Assign Custom Culling Data");
                    culling.profile=profile;
                    EditorUtility.SetDirty(culling);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(culling);
                }
            }
            EditorUtility.SetDirty(prefab);
        }
    }
}
