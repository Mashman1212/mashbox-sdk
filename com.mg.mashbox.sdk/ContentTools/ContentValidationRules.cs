using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.ContentTools
{
    /// <summary>
    /// Project-wide configuration for validating item names and required anchors.
    /// Create via: Assets → Create → Content → Validation Rules.
    /// Brand is NOT validated; we only parse it to scope anchor rules if you want.
    /// </summary>
    [CreateAssetMenu(fileName = "ContentValidationRules",
        menuName = "Content/Validation Rules", order = 2100)]
    public class ContentValidationRules : ScriptableObject
    {
        public enum ShaderType
        {
            Null,
            MG_Vehicle,
            MG_Clothing,
            Griptape,
            MG_Tire,
            MG_Chain
        }
        
        [Serializable]
        public struct TextureDataBudget
        {
            public float MB;

            public bool IsUnlimited => false;
        }
        
        public enum TextureSize
        {
            _64 = 64,
            _128 = 128,
            _256 = 256,
            _512 = 512,
            _1024 = 1024,
            _2048 = 2048,
            _4096 = 4096,
            _8192 = 8192
        }
        
        public enum VertexLimit
        {
            Unlimited = -1,
            _500 = 500,
            _1000 = 1000,
            _2000 = 2000,
            _5000 = 5000,
            _10000 = 10000,
            _15000 = 15000,
            _20000 = 20000,
            _30000 = 30000,
            _50000 = 50000
        }
        
        [Serializable]
        public class TextureSlotLimit
        {
            public string ShaderProperty;
            public TextureSize MaxSize = TextureSize._64;
        }
        

        [Header("Allowed tokens (case-sensitive)")]
        [Tooltip("Optional master list. Type validation uses AllowedPairs below.")]
        public string[] SuperTypes;   // e.g. ["Scooter","BMX"]

        [Tooltip("Optional color whitelist. Leave empty to allow any.")]
        public string[] Colors;       // e.g. ["Black","Blue","Vanilla_Bean"]

        [Serializable]
        public class SuperTypeTypes
        {
            public string SuperType;  // e.g. "Scooter"
            public string[] Types;    // e.g. ["Deck","Bars","Clamp", ...]
        }

        [Header("Allowed SuperType → Types (paired)")]
        public List<SuperTypeTypes> AllowedPairs = new();

        public enum Direction
        {
            Forward,
            Back,
            Left,
            Right,
            Up,
            Down
        }
        
        public static class DirectionUtils
        {
            public static Vector3 ToVector(Direction dir)
            {
                switch (dir)
                {
                    case Direction.Forward: return Vector3.forward;
                    case Direction.Back:    return Vector3.back;
                    case Direction.Left:    return Vector3.left;
                    case Direction.Right:   return Vector3.right;
                    case Direction.Up:      return Vector3.up;
                    case Direction.Down:    return Vector3.down;
                    default: return Vector3.forward;
                }
            }
        }
        
        [Serializable]
        public class PreferredChildOrientation
        {
            [Tooltip("Path to the child (same format as RequiredChildren)")]
            public string ChildPath;

            [Tooltip("Expected local forward direction")]
            public Direction Forward = Direction.Forward;

            [Tooltip("Expected up direction")]
            public Direction Up = Direction.Up;

            [Tooltip("Allowed angle deviation in degrees")]
            public float Tolerance = 5f;

            [Tooltip("If true, this produces a Warning instead of Error")]
            public bool IsWarning = true;
        }
        
        [Serializable]
        public class ItemRule
        {
            public SuperType AppliesToSuperType;
            public ItemType AppliesToType;
            public string AppliesToBrand;

            public bool IgnoreRequiredChildren;
            // Anchor validation
            public string[] RequiredChildren;

            [Tooltip("Transform names that must exist somewhere below the item root, but may be parented anywhere in its hierarchy.")]
            public string[] RequiredDescendantsAnywhere;

            public PreferredChildOrientation[] PreferredOrientations;
            
            // Texture validation
            public ShaderType ShaderType;

            [Tooltip("Optional extra shaders accepted by this rule. The primary ShaderType is always allowed when it is not Null.")]
            public List<ShaderType> AdditionalAllowedShaderTypes = new();
            
            [Tooltip("Max total compressed texture size in MB across all textures used by this item")]
            public TextureDataBudget MaxTextureDataMB;
            
            public List<TextureSlotLimit> Slots = new();
            
            // 🔹 NEW: Mesh validation
            [Tooltip("Maximum allowed vertex count across the entire prefab (-1 = unlimited)")]
            public VertexLimit MaxVertexCount = VertexLimit._500;

            [Min(-1)]
            [Tooltip("Maximum allowed renderer count across the entire prefab (-1 = unlimited)")]
            public int MaxRenderers = 1;

            [Min(-1)]
            [Tooltip("Maximum allowed distinct material count across the entire prefab (-1 = unlimited)")]
            public int MaxDistinctMaterials = 1;

        }
        
        public static Shader GetShader(ShaderType type)
        {
            var shaderName = GetShaderName(type);
            return string.IsNullOrEmpty(shaderName) ? null : Shader.Find(shaderName);
        }

        public static string GetShaderName(ShaderType type)
        {
            switch (type)
            {
                case ShaderType.MG_Vehicle:
                    return "MGShaders/HDRP/Lit/MG_Vehicle";

                case ShaderType.MG_Clothing:
                    return "MGShaders/HDRP/Lit/MG_Clothing";

                case ShaderType.Griptape:
                    return "MGShaders/HDRP/Lit/Griptape";
                case ShaderType.MG_Tire:
                    return "MGShaders/HDRP/Lit/MG_Tire";
                case ShaderType.MG_Chain:
                    return "MGShaders/HDRP/Lit/MG_Chain";
                case ShaderType.Null:
                    return null;
                default:
                    return null;
            }
        }

        public static List<ShaderType> GetAllowedShaderTypes(ItemRule rule)
        {
            var allowed = new List<ShaderType>();

            if (rule == null)
                return allowed;

            AddAllowedShaderType(allowed, rule.ShaderType);

            if (rule.AdditionalAllowedShaderTypes != null)
            {
                foreach (var shaderType in rule.AdditionalAllowedShaderTypes)
                    AddAllowedShaderType(allowed, shaderType);
            }

            return allowed;
        }

        public static string GetAllowedShaderLabel(ItemRule rule)
        {
            var allowed = GetAllowedShaderTypes(rule);
            if (allowed.Count == 0)
                return "<none>";

            return string.Join(", ", allowed.Select(GetShaderLabel));
        }

        private static void AddAllowedShaderType(List<ShaderType> allowed, ShaderType shaderType)
        {
            if (shaderType == ShaderType.Null || allowed.Contains(shaderType))
                return;

            allowed.Add(shaderType);
        }

        private static string GetShaderLabel(ShaderType shaderType)
        {
            return GetShaderName(shaderType) ?? shaderType.ToString();
        }
        
        public List<ItemRule> ItemRules = new();
        
        // ---------- helpers ----------
        public bool IsAllowed(string tok, string[] list)
            => !string.IsNullOrEmpty(tok) && list != null && Array.IndexOf(list, tok) >= 0;

        public bool IsAllowedPair(string superType, string type)
        {
            if (string.IsNullOrEmpty(superType) || string.IsNullOrEmpty(type)) return false;
            var entry = AllowedPairs.Find(p => p.SuperType == superType);
            return entry != null && Array.IndexOf(entry.Types, type) >= 0;
        }

        public IEnumerable<ItemRule> RulesFor(SuperType superType, ItemType type, string brand)
        {
            return ItemRules.Where(r =>
                r.AppliesToSuperType == superType &&
                r.AppliesToType == type &&
                (string.IsNullOrEmpty(r.AppliesToBrand) || r.AppliesToBrand == brand));
        }
        public int GetTextureLimit(SuperType superType, ItemType type, string property)
        {
            foreach (var r in ItemRules)
            {
                if (r.AppliesToSuperType != superType)
                    continue;

                if (r.AppliesToType != type)
                    continue;

                if (r.Slots == null)
                    continue;

                foreach (var slot in r.Slots)
                {
                    if (slot.ShaderProperty == property)
                        return (int)slot.MaxSize;
                }
            }

            return -1;
        }
        
        
        
        public static float GetTotalTextureMB(Renderer[] renderers)
        {
            long totalBytes = 0;

            foreach (var r in renderers)
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;

                    var textures = mat.GetTexturePropertyNames();
                    foreach (var name in textures)
                    {
                        var tex = mat.GetTexture(name);
                        totalBytes += EstimateTextureSizeBytes(tex);
                    }
                }
            }

            return totalBytes / (1024f * 1024f);
        }
        public static long EstimateTextureSizeBytes(Texture tex)
        {
            if (tex == null) return 0;

            int bpp = 32; // fallback

#if UNITY_EDITOR
            var path = UnityEditor.AssetDatabase.GetAssetPath(tex);
            var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;

            if (importer != null)
            {
                var format = importer.GetPlatformTextureSettings("Standalone").format;

                switch (format)
                {
                    case UnityEditor.TextureImporterFormat.DXT1:
                        bpp = 4;
                        break;
                    case UnityEditor.TextureImporterFormat.DXT5:
                    case UnityEditor.TextureImporterFormat.BC7:
                        bpp = 8;
                        break;
                }
            }
#endif

            return (long)tex.width * tex.height * bpp / 8;
        }
        
        public static int CountVertices(GameObject root)
        {
            int total = 0;

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var f in filters)
            {
                if (f.sharedMesh != null)
                    total += f.sharedMesh.vertexCount;
            }

            var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var s in skinned)
            {
                if (s.sharedMesh != null)
                    total += s.sharedMesh.vertexCount;
            }

            return total;
        }
        
        [ContextMenu("Sort")]
        void Sort()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (ItemRules == null)
                return;

            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;

                ItemRules = ItemRules
                    .OrderBy(r => r.AppliesToSuperType)
                    .ThenBy(r => r.AppliesToType)
                    .ThenBy(r => r.AppliesToBrand)
                    .ToList();

                UnityEditor.EditorUtility.SetDirty(this);
            };
#endif
        }
        
        [ContextMenu("Print Texture Budgets")]
        public void PrintTextureBudgets()
        {
            if (ItemRules == null || ItemRules.Count == 0)
            {
                Debug.Log("No ItemRules found.");
                return;
            }

            var grouped = ItemRules
                .Where(r => !r.MaxTextureDataMB.IsUnlimited && r.MaxTextureDataMB.MB > 0f)
                .GroupBy(r => r.AppliesToSuperType);

            foreach (var superGroup in grouped)
            {
                float totalPerPlayer = 0f;

                Debug.Log($"===== {superGroup.Key} =====");

                // Build sorted type list
                var typeGroups = superGroup
                    .GroupBy(r => r.AppliesToType)
                    .Select(g => new
                    {
                        Type = g.Key,
                        Budget = g
                            .Where(r => !r.MaxTextureDataMB.IsUnlimited)
                            .Select(r => r.MaxTextureDataMB.MB)
                            .DefaultIfEmpty(0f)
                            .Max()
                    })
                    .OrderByDescending(x => x.Budget);

                foreach (var type in typeGroups)
                {
                    totalPerPlayer += type.Budget;
                    Debug.Log($"  {type.Type}: {FormatMB(type.Budget)}");
                }

                float total8Players = totalPerPlayer * 8f;

                Debug.Log($"  TOTAL (1 player): {FormatMB(totalPerPlayer)}");
                Debug.Log($"  TOTAL (8 players): {FormatMB(total8Players)}");

                // Optional budget warnings
                if (total8Players > 64f)
                    Debug.LogError($"  ⚠ Exceeds 64MB budget!");
                else if (total8Players > 32f)
                    Debug.LogWarning($"  ⚠ Getting heavy (>32MB)");
            }
        }
        
        static string FormatMB(float mb)
        {
            if (mb < 1f)
                return $"{mb:0.##} MB";   // 0.25, 0.5
            else
                return $"{mb:0.##} MB";   // 1, 2, 6.75
        }
        
//#if UNITY_EDITOR
//        [ContextMenu("Set All Texture Slots To 64")]
//        public void SetAllSlotsTo64()
//        {
//            if (ItemRules == null)
//                return;
//
//            foreach (var rule in ItemRules)
//            {
//                if (rule.Slots == null)
//                    continue;
//
//                foreach (var slot in rule.Slots)
//                {
//                    slot.MaxSize = TextureSize._64;
//                }
//            }
//
//            UnityEditor.EditorUtility.SetDirty(this);
//        }
//#endif
//        
//#if UNITY_EDITOR
//        [ContextMenu("Set All Vertex Limits To 500")]
//        public void SetAllVertexLimitsTo500()
//        {
//            if (ItemRules == null)
//                return;
//
//            foreach (var rule in ItemRules)
//            {
//                rule.MaxVertexCount = VertexLimit._500;
//            }
//
//            UnityEditor.EditorUtility.SetDirty(this);
//        }
//#endif
        
        //
        //[ContextMenu("PopulateItemRulesFromLegacy")]
        //public void PopulateItemRulesFromLegacy()
        //{
        //    ItemRules.Clear();
        //
        //    foreach (var anchor in AnchorRules)
        //    {
        //        var item = new ItemRule
        //        {
        //            AppliesToSuperType = anchor.AppliesToSuperType,
        //            AppliesToType = anchor.AppliesToType,
        //            AppliesToBrand = anchor.AppliesToBrand,
        //            RequiredChildren = anchor.RequiredChildren,
        //            RequiredPatterns = anchor.RequiredPatterns
        //        };
        //
        //        // Try match texture rule
        //        var tex = TextureRules.FirstOrDefault(t =>
        //            t.AppliesToSuperType == anchor.AppliesToSuperType &&
        //            t.AppliesToType == anchor.AppliesToType);
        //
        //        if (tex != null)
        //        {
        //            item.Shader = tex.Shader;
        //            item.Slots = tex.Slots;
        //        }
        //
        //        ItemRules.Add(item);
        //        
        //        #if UNITY_EDITOR
        //            UnityEditor.EditorUtility.SetDirty(this);
        //        #endif
        //    }
        //}
    }
}
