#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MashBoxSDK.ContentTools
{
    /// <summary>
    /// Validates:
    ///  • Prefab asset (not a scene object)
    ///  • Name format: [SuperType]_[Type]_[Brand]_[Color]
    ///  • SuperType↔Type pair (from rules)
    ///  • Color whitelist (optional)
    ///  • Required anchor children (from rules)
    /// Brand is parsed but NOT validated.
    /// </summary>
    public static class ContentPackValidator
    {
        private const float FullSkinPackLimitMB = 10f;
        private static readonly Regex PrefabSourceGuidRegex = new Regex(
            @"m_SourcePrefab:\s*\{[^}]*guid:\s*([0-9a-fA-F]{32})[^}]*\}",
            RegexOptions.Compiled);

        public enum Severity { Info, Warning, Error }

        public class Issue
        {
            public Severity severity;
            public string message;
            public Object context; // offending asset (prefab or pack)
        }

        public sealed class NormalMapImportProblem
        {
            public Material material;
            public Texture texture;
            public string propertyName;
            public string texturePath;
        }

        public static List<Issue> ValidatePack(ContentPackDefinition pack, ContentValidationRules rules)
        {
            var issues = new List<Issue>();

            if (!pack)
            {
                issues.Add(new Issue { severity = Severity.Error, message = "Pack is null" });
                return issues;
            }

            if (pack._items == null || pack._items.Count == 0)
            {
                issues.Add(new Issue { severity = Severity.Warning, message = $"Pack '{pack.name}' has no items.", context = pack });
                return issues;
            }

            float totalTextureMB = 0f;
            int totalVertices = 0;
            var containsFullSkin = false;
            var nonFullSkinItems = new List<string>();

            var missingItemIndices = pack._items
                .Select((item, index) => new { item, index })
                .Where(entry => !entry.item)
                .Select(entry => entry.index + 1)
                .ToList();
            if (missingItemIndices.Count > 0)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = $"Pack '{pack.name}' contains missing prefab item reference{(missingItemIndices.Count == 1 ? "" : "s")} at slot{(missingItemIndices.Count == 1 ? "" : "s")}: {string.Join(", ", missingItemIndices)}. Restore the prefab asset or remove the broken item before publishing.",
                    context = pack
                });
            }

            foreach (var problem in FindPrefabIntegrityProblems(
                         pack._items.Where(item => item != null),
                         includePrefabDependencies: true))
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = problem.message,
                    context = problem.context ? problem.context : pack
                });
            }

            HashSet<string> seenPaths = new HashSet<string>();
            
            foreach (var go in pack._items)
            {
                if (!go) continue;
                var hasItemIdentity = TryParseItemIdentity(go, out var superType, out var itemType);
                var isFullSkin = hasItemIdentity && IsFullSkinType(superType, itemType);
                containsFullSkin |= isFullSkin;

                if (!isFullSkin)
                    nonFullSkinItems.Add(go.name);

                // 🔹 run existing validation
                ValidateItem(go, rules, issues, validatePrefabIntegrity: false);
                
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                

                foreach (var r in renderers)
                {
                    if (hasItemIdentity && IsScooterDeckGriptapeProxy(go, r, superType, itemType))
                        continue;

                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null) continue;

                        int propertyCount = ShaderUtil.GetPropertyCount(mat.shader);

                        for (int i = 0; i < propertyCount; i++)
                        {
                            if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                                continue;

                            string propName = ShaderUtil.GetPropertyName(mat.shader, i);
                            var tex = mat.GetTexture(propName);

                            if (tex != null)
                            {
                                string path = AssetDatabase.GetAssetPath(tex);

                                if (!string.IsNullOrEmpty(path) && seenPaths.Add(path))
                                {
                                    if (IsCoreCustomizationAsset(path))
                                        continue;

                                    totalTextureMB += EstimateTextureSizeBytes(tex) / (1024f * 1024f);
                                }
                            }
                        }
                    }
                }

                totalVertices += CountVertices(go);
            }

            if (containsFullSkin && nonFullSkinItems.Count > 0)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = $"Full Skin packs can only contain Human_Full Skin items. Remove: {string.Join(", ", nonFullSkinItems)}",
                    context = pack
                });
            }

            // Vanilla content still obeys item rules; only aggregate pack budget gates are skipped.
            bool skipAggregateBudgetValidation = pack.IsVanillaContent;

            var maxTextureBudgetMB = GetEffectiveTextureBudgetMB(pack);
            if (!skipAggregateBudgetValidation && totalTextureMB > maxTextureBudgetMB)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = $"Pack exceeds texture budget: {totalTextureMB:F2}MB / {maxTextureBudgetMB}MB",
                    context = pack
                });
            }

            if (!skipAggregateBudgetValidation && totalVertices > pack.MaxTotalVertices)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = $"Pack exceeds vertex budget: {totalVertices} / {pack.MaxTotalVertices}",
                    context = pack
                });
            }

            float estimatedBundleMB = totalTextureMB * 0.8f;
            var maxPackSizeMB = GetEffectivePackSizeMB(pack);

            if (!skipAggregateBudgetValidation && estimatedBundleMB > maxPackSizeMB)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = $"Estimated bundle size {estimatedBundleMB:F2}MB exceeds limit {maxPackSizeMB}MB",
                    context = pack
                });
            }

            return issues;
        }

        public sealed class PrefabIntegrityProblem
        {
            public string message;
            public Object context;
        }

        public static float GetEffectivePackSizeMB(ContentPackDefinition pack)
        {
            if (pack == null)
                return 0f;

            return IsFullSkinOnlyPack(pack) ? FullSkinPackLimitMB : pack.MaxPackSizeMB;
        }

        public static float GetEffectiveTextureBudgetMB(ContentPackDefinition pack)
        {
            if (pack == null)
                return 0f;

            return IsFullSkinOnlyPack(pack) ? FullSkinPackLimitMB : pack.MaxTextureBudgetMB;
        }

        public static bool IsFullSkinOnlyPack(ContentPackDefinition pack)
        {
            if (pack == null || pack._items == null || pack._items.Count == 0)
                return false;

            var foundFullSkin = false;
            foreach (var go in pack._items)
            {
                if (go == null)
                    continue;

                if (!TryParseItemIdentity(go, out var superType, out var itemType))
                    return false;

                if (!IsFullSkinType(superType, itemType))
                    return false;

                foundFullSkin = true;
            }

            return foundFullSkin;
        }

        private static bool IsFullSkinType(SuperType superType, ItemType itemType)
        {
            return superType == SuperType.Human && itemType == ItemType.Full_Skin;
        }

        private static bool IsHumanBustOrBodyType(SuperType superType, ItemType itemType)
        {
            return superType == SuperType.Human && (itemType == ItemType.Bust || itemType == ItemType.Body);
        }

        private static void ValidateHumanBustBodyMeshReadWrite(GameObject root, SuperType superType, ItemType itemType, List<Issue> issues)
        {
            if (!IsHumanBustOrBodyType(superType, itemType))
                return;

            var checkedMeshes = new HashSet<Mesh>();

            foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
                ValidateMeshReadWrite(root, meshFilter.sharedMesh, meshFilter.gameObject, checkedMeshes, issues);

            foreach (var skinnedMesh in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                ValidateMeshReadWrite(root, skinnedMesh.sharedMesh, skinnedMesh.gameObject, checkedMeshes, issues);
        }

        private static void ValidateMeshReadWrite(GameObject root, Mesh mesh, GameObject owner, HashSet<Mesh> checkedMeshes, List<Issue> issues)
        {
            if (mesh == null || !checkedMeshes.Add(mesh))
                return;

            var path = AssetDatabase.GetAssetPath(mesh);
            var modelImporter = !string.IsNullOrEmpty(path)
                ? AssetImporter.GetAtPath(path) as ModelImporter
                : null;

            if (modelImporter != null)
            {
                if (modelImporter.isReadable)
                    return;

                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = $"{root.name}: Human Bust/Body mesh '{mesh.name}' on '{GetGameObjectPath(owner)}' must have Read/Write Enabled in the model import settings.",
                    context = mesh
                });
                return;
            }

            if (mesh.isReadable)
                return;

            issues.Add(new Issue
            {
                severity = Severity.Error,
                message = $"{root.name}: Human Bust/Body mesh '{mesh.name}' on '{GetGameObjectPath(owner)}' must be readable.",
                context = mesh
            });
        }

        private static bool IsCoreCustomizationAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            path = path.Replace("\\", "/");

            return path.Contains("Packages/com.mg.mashbox.sdk/Core Customization Assets") ||
                   path.StartsWith("Packages/com.mg.mashbox.sdk");
        }
        
        public static List<Issue> ValidateItem(
            GameObject go,
            ContentValidationRules rules,
            List<Issue> buffer = null,
            bool validatePrefabIntegrity = true)
        {
            var issues = buffer ?? new List<Issue>();
            
            
            if (!go)
            {
                issues.Add(new Issue { severity = Severity.Error, message = "Null item reference." });
                return issues;
            }

            // Must be a prefab asset (not a scene object)
            var pType = PrefabUtility.GetPrefabAssetType(go);
            if (pType == PrefabAssetType.NotAPrefab || pType == PrefabAssetType.MissingAsset)
            {
                issues.Add(new Issue { severity = Severity.Error, message = $"'{go.name}' is not a prefab asset.", context = go });
                return issues;
            }

            if (validatePrefabIntegrity)
            {
                foreach (var problem in FindPrefabIntegrityProblems(new[] { go }, includePrefabDependencies: true))
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        message = problem.message,
                        context = problem.context ? problem.context : go
                    });
                }
            }

            ValidateNormalMapTextureImports(go, issues);
            
            if (!go.transform.localScale.Equals(Vector3.one))
            {
                issues.Add(new Issue { severity = Severity.Error, message = "Object Scale is not (1,1,1)" });
                return issues;
            }

            
            if (go.name.ToLower().Contains("mesh"))
            {
                if (go.transform.localPosition != Vector3.zero)
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        message = $"{go.name}: '{GetGameObjectPath(go.gameObject)}' must have localPosition (0,0,0). Found {go.transform.localPosition}.",
                        context = go.gameObject
                    });
                }

                if (go.transform.localRotation != Quaternion.identity)
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        message = $"{go.name}: '{GetGameObjectPath(go.gameObject)}' must have localRotation identity. Found {go.transform.localRotation.eulerAngles}.",
                        context = go.gameObject
                    });
                }
            }
            
// --- Name format validation ---
            string name = go.name;

// Must have exactly 5 tags (4 underscores)
            var parts = name.Split('_');
            if (parts.Length != 5)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = $"{name}: must have exactly 5 tags separated by 4 underscores (expected SuperType_Type_Brand_Name_Color, found {parts.Length - 1} underscores).",
                    context = go
                });
                return issues;
            }

// No double underscores
            if (name.Contains("__"))
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = $"{name}: contains double underscores '__', which are not allowed.",
                    context = go
                });
                return issues;
            }


// Validate that each part is alphanumeric and non-empty
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part))
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        message = $"{name}: tag {i + 1} is empty.",
                        context = go
                    });
                    return issues;
                }

                // Allow spaces and numbers in Brand (index 2) and Name (index 3)
                string pattern = @"^[A-Za-z0-9 ]+$";   // letters, digits, and spaces

                if (!Regex.IsMatch(part, pattern))
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        message = $"{name}: tag '{part}' contains invalid characters (letters, digits{(i == 2 || i == 3 ? ", spaces" : "")} only).",
                        context = go
                    });
                    return issues;
                }
            }


// Assign tokens explicitly
            string brand     = parts[2];
            string itemName  = parts[3];
            string color     = parts[4];

// Convert spaces to enum format
            string superTypeToken = parts[0].Replace(" ", "_");

            if (!Enum.TryParse(superTypeToken, ignoreCase: true, out SuperType superType))
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = $"{go.name}: invalid SuperType '{parts[0]}' (expected one of: {string.Join(", ", Enum.GetNames(typeof(SuperType)))})",
                    context = go
                });
                return issues;
            }

            ItemType type = default;
            bool found = false;

            string token = parts[1].Trim();

            foreach (ItemType t in Enum.GetValues(typeof(ItemType)))
            {
                if (string.Equals(t.ToDisplayName().Trim(), token, StringComparison.OrdinalIgnoreCase))
                {
                    type = t;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message = $"{go.name}: invalid Type '{parts[1]}' (expected one of: {string.Join(", ", Enum.GetValues(typeof(ItemType)).Cast<ItemType>().Select(t => t.ToDisplayName()))})",
                    context = go
                });
                return issues;
            }

            ValidateHumanBustBodyMeshReadWrite(go, superType, type, issues);

// ... inside ValidateItem(GameObject go, ContentValidationRules rules, List<Issue> buffer = null)

            if (rules != null && !go.name.Contains("Me The Person_Shreighk"))
            {
                if (!rules.IsAllowedPair(superType.ToString(), type.ToDisplayName()))
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        message = $"{go.name}: invalid Type '{type}' for SuperType '{superType}'", context = go
                    });

                if (rules.Colors != null && rules.Colors.Length > 0)
                {
                    var colorParts = color.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var c in colorParts)
                    {
                        if (!rules.IsAllowed(c, rules.Colors))
                        {
                            issues.Add(new Issue
                            {
                                severity = Severity.Error,
                                message = $"{go.name}: unknown Color '{c}' in color combination '{color}'",
                                context = go
                            });
                        }
                    }
                }


                
                foreach (var rule in rules.RulesFor(superType, type, brand))
                {
                    float totalMB = GetTotalTextureMB(go, go.GetComponentsInChildren<Renderer>(true), rule, superType, type);
       
                    // 1) Exact-path and hierarchy-independent child requirements
                    if (!rule.IgnoreRequiredChildren &&
                        (rule.RequiredChildren != null || rule.RequiredDescendantsAnywhere != null))
                    {
                        var required = (rule.RequiredChildren ?? Array.Empty<string>())
                            .Where(r => !string.IsNullOrWhiteSpace(r))
                            .Select(r => r.Trim().Replace("\\", "/"))
                            .Distinct(StringComparer.Ordinal)
                            .ToList();

                        var requiredAnywhere = new HashSet<string>((rule.RequiredDescendantsAnywhere ?? Array.Empty<string>())
                            .Where(r => !string.IsNullOrWhiteSpace(r))
                            .Select(r => r.Trim())
                            .Distinct(StringComparer.Ordinal), StringComparer.Ordinal);

                        foreach (var req in required)
                        {
                            if (go.transform.Find(req) == null)
                            {
                                issues.Add(new Issue
                                {
                                    severity = Severity.Error,
                                    message = $"{go.name}: missing child '{req}'",
                                    context = go
                                });
                            }
                        }

                        foreach (var requiredName in requiredAnywhere)
                        {
                            bool exists = go.transform
                                .GetComponentsInChildren<Transform>(true)
                                .Any(descendant => descendant != go.transform &&
                                                   string.Equals(descendant.name, requiredName, StringComparison.Ordinal));

                            if (!exists)
                            {
                                issues.Add(new Issue
                                {
                                    severity = Severity.Error,
                                    message = $"{go.name}: missing descendant '{requiredName}' (it may be parented anywhere below the item root)",
                                    context = go
                                });
                            }
                        }

                        var allowedHierarchyPaths = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var req in required)
                        {
                            var segments = req.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                            if (segments.Length == 0)
                                continue;

                            string currentPath = string.Empty;
                            for (int i = 0; i < segments.Length; i++)
                            {
                                currentPath = string.IsNullOrEmpty(currentPath)
                                    ? segments[i]
                                    : $"{currentPath}/{segments[i]}";
                                allowedHierarchyPaths.Add(currentPath);
                            }
                        }

                        foreach (var child in go.transform.GetComponentsInChildren<Transform>(true))
                        {
                            if (child == go.transform)
                                continue;

                            string childPath = GetRelativeTransformPath(go.transform, child);
                            if (string.IsNullOrEmpty(childPath) ||
                                allowedHierarchyPaths.Contains(childPath) ||
                                requiredAnywhere.Contains(child.name))
                                continue;

                            issues.Add(new Issue
                            {
                                severity = Severity.Error,
                                message = $"{go.name}: unexpected child '{childPath}'.",
                                context = child.gameObject
                            });
                        }
                    }
                    
                    var animators = go.GetComponentsInChildren<Animator>(true);

                    if (animators.Length > 0)
                    {
                        foreach (var animator in animators)
                        {
                            issues.Add(new Issue
                            {
                                severity = Severity.Error,
                                message = $"{go.name}: Animator not allowed on '{GetGameObjectPath(animator.gameObject)}'.",
                                context = animator.gameObject
                            });
                        }
                    }
                    
                    if (rule.PreferredOrientations != null)
                    {
                        foreach (var pref in rule.PreferredOrientations)
                        {
                            if (string.IsNullOrEmpty(pref.ChildPath))
                                continue;

                            var child = go.transform.Find(pref.ChildPath);
                            if (child == null)
                                continue; // already handled by RequiredChildren

                            var parent = child.parent;

                            Vector3 expectedForward = parent.TransformDirection(ContentValidationRules.DirectionUtils.ToVector(pref.Forward));
                            Vector3 expectedUp = parent.TransformDirection(ContentValidationRules.DirectionUtils.ToVector(pref.Up));

                            float forwardAngle = Vector3.Angle(child.forward, expectedForward);
                            float upAngle = Vector3.Angle(child.up, expectedUp);

                            if (forwardAngle > pref.Tolerance || upAngle > pref.Tolerance)
                            {
                                issues.Add(new Issue
                                {
                                    severity = pref.IsWarning ? Severity.Warning : Severity.Error,
                                    message = $"{go.name}: '{pref.ChildPath}' orientation is off. " +
                                              $"Forward Δ:{forwardAngle:F1}°, Up Δ:{upAngle:F1}° (tolerance {pref.Tolerance}°)",
                                    context = child.gameObject
                                });
                            }
                        }
                    }
                    
                    // Legacy scoped extra-child check kept disabled; recursive path validation above is now authoritative.
                    if (false && !rule.IgnoreRequiredChildren)
                    {
                        // Build a quick look-up of exact required names (root-relative)
                        var requiredExact = new HashSet<string>(rule.RequiredChildren ?? Array.Empty<string>());

                        // Group patterns by scope so we can evaluate “extras” once per scope.
                        // Key = (PathPrefix, DirectChildrenOnly)
                        var scopeMap = new Dictionary<(string path, bool direct), List<Regex>>();

                        // 🔹 Derive scopes from RequiredChildren so nested paths are enforced
                        // e.g., "FrontWheel_Anchor/Front_Left_Peg_Anchor" -> scope "FrontWheel_Anchor" (direct children only)
                        foreach (var req in requiredExact)
                        {
                            var slash = req.LastIndexOf('/');
                            if (slash > 0)
                            {
                                var prefix = req.Substring(0, slash);
                                var key = (prefix, true);
                                if (!scopeMap.ContainsKey(key))
                                    scopeMap[key] = new List<Regex>(); // no patterns; only exact names allowed here
                            }
                        }

// Always validate root transform
// This ensures prefabs without explicit anchor rules still enforce clean root hierarchy
                        if (!scopeMap.ContainsKey((string.Empty, true)))
                        {
                            scopeMap[(string.Empty, true)] = new List<Regex>();
                        }

                        foreach (var kv in scopeMap)
                        {
                            string pathPrefix = kv.Key.path;
                            bool directOnly = kv.Key.direct;
                            var regexes = kv.Value;

                            // resolve scope root
                            Transform scopeRoot = go.transform;
                            if (!string.IsNullOrEmpty(pathPrefix))
                            {
                                var sub = go.transform.Find(pathPrefix);
                                if (sub == null)
                                {
                                    // Missing subtree is already reported above during pattern checks; skip.
                                    continue;
                                }
                                scopeRoot = sub;
                            }

                            // collect candidates to test for "extra"
                            IEnumerable<Transform> candidates = directOnly
                                ? scopeRoot.Cast<Transform>()
                                : scopeRoot.GetComponentsInChildren<Transform>(true).Where(t => t != scopeRoot);

                            foreach (var t in candidates)
                            {
                                // 1) allow if exact root-relative path is in RequiredChildren
                                string rootRelative = string.IsNullOrEmpty(pathPrefix)
                                    ? t.name
                                    : $"{pathPrefix}/{t.name}";
                                bool allowedByExact = requiredExact.Contains(rootRelative);

                                // 2) allow if it matches ANY pattern for this scope
                                bool allowedByPattern = regexes.Any(rx => rx.IsMatch(t.name));

                                if (!allowedByExact && !allowedByPattern)
                                {
                                    string scope = string.IsNullOrEmpty(pathPrefix) ? "<root>" : pathPrefix;
                                    issues.Add(new Issue
                                    {
                                        severity = Severity.Error,
                                        message = $"{go.name}: unexpected child '{rootRelative}' under {scope}.",
                                        context = t.gameObject
                                    });
                                }
                            }
                        }
                    }
                    
                    var allowedShaderTypes = ContentValidationRules.GetAllowedShaderTypes(rule);
                    if (allowedShaderTypes.Count > 0)
                    {
                        var expectedShaders = allowedShaderTypes
                            .Select(ContentValidationRules.GetShader)
                            .Where(shader => shader != null)
                            .ToList();
                        var expectedShaderNames = new HashSet<string>(
                            allowedShaderTypes
                                .Select(ContentValidationRules.GetShaderName)
                                .Where(shaderName => !string.IsNullOrEmpty(shaderName)));
                        var allowedShaderLabel = ContentValidationRules.GetAllowedShaderLabel(rule);
                        var renderers = go.GetComponentsInChildren<Renderer>(true);
                        var distinctMaterials = new HashSet<Material>();
                        var maxRenderers = rule.MaxRenderers;
                        var maxDistinctMaterials = rule.MaxDistinctMaterials;

                        if (maxRenderers >= 0 && renderers.Length > maxRenderers)
                        {
                            issues.Add(new Issue
                            {
                                severity = Severity.Error,
                                message = $"{go.name}: uses {renderers.Length} renderers, but only {maxRenderers} renderer{(maxRenderers == 1 ? "" : "s")} {(maxRenderers == 1 ? "is" : "are")} allowed per item.",
                                context = go
                            });
                        }

                        foreach (var r in renderers)
                        {
                            if (r.sharedMaterials == null)
                                continue;

                            var ignoreMaterials = IsScooterDeckGriptapeProxy(go, r, superType, type);
                            if (ignoreMaterials)
                                continue;

                            if (r.sharedMaterials.Length > MaxMaterialsPerRenderer)
                            {
                                issues.Add(new Issue
                                {
                                    severity = Severity.Error,
                                    message = $"{go.name}: Renderer '{r.name}' has {r.sharedMaterials.Length} materials (max {MaxMaterialsPerRenderer}).",
                                    context = r.gameObject
                                });
                            }

                            foreach (var mat in r.sharedMaterials)
                            {
                                if (mat == null)
                                    continue;

                                distinctMaterials.Add(mat);

                                // Shader validation
                                var shaderIsAllowed =
                                    expectedShaders.Contains(mat.shader) ||
                                    (mat.shader != null && expectedShaderNames.Contains(mat.shader.name));

                                if (!shaderIsAllowed)
                                {
                                    issues.Add(new Issue
                                    {
                                        severity = Severity.Error,
                                        message =
                                            $"{go.name}: material '{mat.name}' must use one of these shaders: {allowedShaderLabel} (found '{mat.shader?.name ?? "None"}').",
                                        context = r.gameObject
                                    });

                                    continue; // don't texture-validate wrong shader
                                }

                                int propertyCount = ShaderUtil.GetPropertyCount(mat.shader);

                                for (int i = 0; i < propertyCount; i++)
                                {
                                    if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                                        continue;

                                    string propName = ShaderUtil.GetPropertyName(mat.shader, i);
                                    var tex = mat.GetTexture(propName);

                                    int limit = rules.GetTextureLimit(superType, type, propName);

                                    var isNormalMapSlot = IsNormalMapTextureProperty(mat.shader, i, propName);
                                    ValidateTexture(tex, propName, limit, isNormalMapSlot, go, issues);
                                }
                                
                            }
                        }

                        if (maxDistinctMaterials >= 0 && distinctMaterials.Count > maxDistinctMaterials)
                        {
                            issues.Add(new Issue
                            {
                                severity = Severity.Error,
                                message = $"{go.name}: uses {distinctMaterials.Count} distinct materials, but only {maxDistinctMaterials} material{(maxDistinctMaterials == 1 ? "" : "s")} {(maxDistinctMaterials == 1 ? "is" : "are")} allowed per item.",
                                context = go
                            });
                        }
                    }
                    

                    if (!rule.MaxTextureDataMB.IsUnlimited && totalMB > rule.MaxTextureDataMB.MB)
                    {
                        issues.Add(new Issue
                        {
                            severity = Severity.Error,
                            message = $"{go.name}: total texture size {totalMB:F2} MB exceeds limit {rule.MaxTextureDataMB.MB} MB.",
                            context = go
                        });
                    }
                    
                    // Vertex count validation
                    if (rule.MaxVertexCount >= 0)
                    {
                        int vertexCount = CountVertices(go);

                        if (vertexCount > (int)rule.MaxVertexCount)
                        {
                            issues.Add(new Issue
                            {
                                severity = Severity.Error,
                                message = $"{go.name}: vertex count {vertexCount} exceeds limit {rule.MaxVertexCount}.",
                                context = go
                            });
                        }
                    }
                }
            }
            
            if (go.transform.localScale != Vector3.one)
            {
                if (go.transform.GetComponent<SkinnedMeshRenderer>())
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        message = $"{go.name}: root transform scale must be (1,1,1). Found {go.transform.localScale}.",
                        context = go
                    });
                }
            }

            var skinnedMeshes = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (var smr in skinnedMeshes)
            {
                if (smr.transform.localScale != Vector3.one)
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        message = $"{go.name}: SkinnedMesh '{smr.name}' has non-identity scale {smr.transform.localScale}. Must be (1,1,1).",
                        context = smr.gameObject
                    });
                }
            }
            
            return issues;
        }

        /// <summary>
        /// Finds integrity failures that Unity can otherwise preserve as broken YAML until the
        /// prefab is imported in a clean project. Publishing must block on every returned problem.
        /// </summary>
        public static List<PrefabIntegrityProblem> FindPrefabIntegrityProblems(
            IEnumerable<GameObject> roots,
            bool includePrefabDependencies)
        {
            var problems = new List<PrefabIntegrityProblem>();
            var problemKeys = new HashSet<string>(StringComparer.Ordinal);
            var prefabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddProblem(string key, string message, Object context)
            {
                if (!problemKeys.Add(key))
                    return;

                problems.Add(new PrefabIntegrityProblem
                {
                    message = message,
                    context = context
                });
            }

            void InspectHierarchy(GameObject root, string ownerPath)
            {
                if (!root)
                    return;

                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!transform)
                        continue;

                    var gameObject = transform.gameObject;
                    var hierarchyPath = GetGameObjectPath(gameObject);
                    var missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                    if (missingScriptCount > 0)
                    {
                        AddProblem(
                            $"script|{ownerPath}|{hierarchyPath}",
                            $"{ownerPath}: '{hierarchyPath}' has {missingScriptCount} missing script{(missingScriptCount == 1 ? "" : "s")}. Remove the missing component or restore its script before publishing.",
                            gameObject);
                    }

                    if (HasMissingPrefabSource(gameObject))
                    {
                        AddProblem(
                            $"prefab-instance|{ownerPath}|{hierarchyPath}",
                            $"{ownerPath}: '{hierarchyPath}' has a missing nested prefab or Prefab Variant source. Restore or replace the missing prefab before publishing.",
                            gameObject);
                    }

                    try
                    {
                        AddPrefabPath(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject), prefabPaths);
                        var source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                        if (source)
                            AddPrefabPath(AssetDatabase.GetAssetPath(source), prefabPaths);
                    }
                    catch
                    {
                        // Broken prefab links are reported by HasMissingPrefabSource above.
                    }
                }
            }

            foreach (var root in roots ?? Enumerable.Empty<GameObject>())
            {
                if (!root)
                    continue;

                var rootPath = AssetDatabase.GetAssetPath(root)?.Replace('\\', '/');
                var ownerPath = string.IsNullOrEmpty(rootPath) ? root.name : rootPath;
                AddPrefabPath(rootPath, prefabPaths);
                InspectHierarchy(root, ownerPath);
            }

            if (includePrefabDependencies && prefabPaths.Count > 0)
            {
                try
                {
                    foreach (var dependency in AssetDatabase.GetDependencies(prefabPaths.ToArray(), true))
                        AddPrefabPath(dependency, prefabPaths);
                }
                catch (Exception exception)
                {
                    AddProblem(
                        "dependencies",
                        $"Unity could not inspect all prefab dependencies: {exception.Message}",
                        null);
                }
            }

            foreach (var prefabPath in prefabPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray())
            {
                var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (!prefabRoot)
                {
                    AddProblem(
                        $"load|{prefabPath}",
                        $"Prefab '{prefabPath}' could not be loaded. It may be corrupt or have a missing Prefab Variant parent.",
                        null);
                    continue;
                }

                var assetType = PrefabUtility.GetPrefabAssetType(prefabRoot);
                if (assetType == PrefabAssetType.MissingAsset)
                {
                    AddProblem(
                        $"asset|{prefabPath}",
                        $"Prefab '{prefabPath}' has a missing prefab asset or Prefab Variant parent.",
                        prefabRoot);
                }
                else if (assetType == PrefabAssetType.Variant)
                {
                    Object variantParent = null;
                    try
                    {
                        variantParent = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot);
                    }
                    catch
                    {
                        // A null parent below produces the actionable validation problem.
                    }

                    if (!variantParent)
                    {
                        AddProblem(
                            $"variant|{prefabPath}",
                            $"Prefab Variant '{prefabPath}' has no valid parent prefab. Restore its parent or unpack/recreate the variant before publishing.",
                            prefabRoot);
                    }
                }

                InspectHierarchy(prefabRoot, prefabPath);
                InspectSerializedPrefabSources(prefabPath, prefabRoot, AddProblem);
            }

            return problems;
        }

        private static void AddPrefabPath(string path, HashSet<string> prefabPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || prefabPaths == null)
                return;

            path = path.Replace('\\', '/');
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                prefabPaths.Add(path);
        }

        private static bool HasMissingPrefabSource(GameObject gameObject)
        {
            if (!gameObject)
                return false;

            try
            {
                if (PrefabUtility.IsPartOfAnyPrefab(gameObject) && PrefabUtility.IsPrefabAssetMissing(gameObject))
                    return true;

                return PrefabUtility.GetPrefabInstanceStatus(gameObject) == PrefabInstanceStatus.MissingAsset;
            }
            catch
            {
                return false;
            }
        }

        private static void InspectSerializedPrefabSources(
            string prefabPath,
            Object context,
            Action<string, string, Object> addProblem)
        {
            if (string.IsNullOrWhiteSpace(prefabPath) ||
                !prefabPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                var absolutePath = Path.GetFullPath(Path.Combine(projectRoot ?? string.Empty, prefabPath));
                if (!File.Exists(absolutePath))
                    return;

                var yaml = File.ReadAllText(absolutePath);
                foreach (Match match in PrefabSourceGuidRegex.Matches(yaml))
                {
                    var guid = match.Groups[1].Value;
                    if (string.IsNullOrEmpty(guid) || guid.All(character => character == '0'))
                        continue;

                    var sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrWhiteSpace(sourcePath))
                        continue;

                    addProblem(
                        $"guid|{prefabPath}|{guid}",
                        $"Prefab '{prefabPath}' references a missing nested prefab or Prefab Variant parent (GUID: {guid}). Restore that asset or remove the broken prefab link before publishing.",
                        context);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ContentPackValidator] Could not inspect prefab YAML '{prefabPath}': {exception.Message}");
            }
        }
        
        static string GetGameObjectPath(GameObject obj)
        {
            var path = obj.name;
            var current = obj.transform;

            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }

        static string GetRelativeTransformPath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return string.Empty;

            if (root == target)
                return string.Empty;

            var segments = new Stack<string>();
            var current = target;

            while (current != null && current != root)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return current == root ? string.Join("/", segments) : string.Empty;
        }

        internal static bool IsScooterDeckGriptapeProxy(GameObject root, Renderer renderer)
        {
            if (!TryParseItemIdentity(root, out var superType, out var type))
                return false;

            return IsScooterDeckGriptapeProxy(root, renderer, superType, type);
        }

        private static bool IsScooterDeckGriptapeProxy(GameObject root, Renderer renderer, SuperType superType, ItemType type)
        {
            if (root == null || renderer == null)
                return false;

            if (superType != SuperType.Scooter || type != ItemType.Deck)
                return false;

            var current = renderer.transform;
            while (current != null && current != root.transform)
            {
                if (IsGriptapeProxyName(current.name))
                    return true;

                current = current.parent;
            }

            return false;
        }

        private static bool IsGriptapeProxyName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var normalized = Regex.Replace(name, "[^a-z0-9]", string.Empty).ToLowerInvariant();
            return normalized == "griptape";
        }

        private static bool TryParseItemIdentity(GameObject go, out SuperType superType, out ItemType type)
        {
            return TryParseItemIdentity(go != null ? go.name : null, out superType, out type);
        }

        private static bool TryParseItemIdentity(string itemName, out SuperType superType, out ItemType type)
        {
            superType = default;
            type = default;

            if (string.IsNullOrWhiteSpace(itemName))
                return false;

            var parts = itemName.Split('_');
            if (parts.Length < 2)
                return false;

            var superTypeToken = parts[0].Replace(" ", "_");
            if (!Enum.TryParse(superTypeToken, ignoreCase: true, out superType))
                return false;

            var typeToken = parts[1].Trim();
            foreach (ItemType candidate in Enum.GetValues(typeof(ItemType)))
            {
                if (string.Equals(candidate.ToDisplayName().Trim(), typeToken, StringComparison.OrdinalIgnoreCase))
                {
                    type = candidate;
                    return true;
                }
            }

            return false;
        }
        
        static int CountVertices(GameObject root)
        {
            int total = 0;

            var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                    total += mf.sharedMesh.vertexCount;
            }

            var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skinnedMeshes)
            {
                if (smr.sharedMesh != null)
                    total += smr.sharedMesh.vertexCount;
            }

            return total;
        }
        
        static int MaxMaterialsPerRenderer = 1;
        
        private static void ValidateTexture(Texture tex, string slot, int maxSize, bool isNormalMapSlot, GameObject go, List<Issue> issues)
        {
            if (tex == null)
                return;

            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path))
                return;

            if (IsCoreCustomizationAsset(path))
                return;
            
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;

// Determine actual and effective resolution
            int actualSize = Mathf.Max(tex.width, tex.height);
            int importLimit = importer.maxTextureSize;
            int effectiveSize = Mathf.Min(actualSize, importLimit);

// Validate against rule
            if (maxSize > 0 && effectiveSize > maxSize)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message =
                        $"{go.name}: texture '{tex.name}' in slot '{slot}' exceeds max size ({maxSize}). " +
                        $"Actual:{actualSize} ImportLimit:{importLimit} Effective:{effectiveSize}.",
                    context = tex
                });
            }

            // Compression validation
            if (importer.textureCompression == TextureImporterCompression.Uncompressed)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message =
                        $"{go.name}: texture '{tex.name}' in slot '{slot}' must use compression.",
                    context = tex
                });
            }

            // Read/Write validation
            if (importer.isReadable)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message =
                        $"{go.name}: texture '{tex.name}' in slot '{slot}' must have Read/Write disabled.",
                    context = tex
                });
            }
            
            // Mipmap validation
            if (!importer.mipmapEnabled)
            {
                issues.Add(new Issue
                {
                    severity = Severity.Warning,
                    message =
                        $"{go.name}: texture '{tex.name}' in slot '{slot}' should have mipmaps enabled.",
                    context = tex
                });
            }

// Texture type validation (slot specific)
            TextureImporterType type = importer.textureType;

            
            if (isNormalMapSlot)
            {
                // Normal-map import enforcement is performed once for every referenced material
                // by ValidateNormalMapTextureImports, including slots without size rules.
            }
            else if (slot == "_DetailMap")
            {
                if (type != TextureImporterType.NormalMap &&
                    type != TextureImporterType.Default)
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        message =
                            $"{go.name}: texture '{tex.name}' in slot '{slot}' must use Texture Type 'Default' or 'Normal Map'.",
                        context = tex
                    });
                }
            }
            else
            {
                if (type != TextureImporterType.Default)
                {
                    issues.Add(new Issue
                    {
                        severity = Severity.Error,
                        message =
                            $"{go.name}: texture '{tex.name}' in slot '{slot}' must use Texture Type 'Default'.",
                        context = tex
                    });
                }
            }
        }

        private static void ValidateNormalMapTextureImports(GameObject root, List<Issue> issues)
        {
            if (root == null || issues == null)
                return;

            var materials = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null && renderer.sharedMaterials != null)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .ToList();

            var rootPath = AssetDatabase.GetAssetPath(root);
            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                materials.AddRange(AssetDatabase.GetDependencies(rootPath, true)
                    .Select(AssetDatabase.LoadAssetAtPath<Material>)
                    .Where(material => material != null));
            }

            foreach (var problem in FindNormalMapImportProblems(materials))
            {
                issues.Add(new Issue
                {
                    severity = Severity.Error,
                    message =
                        $"{root.name}: texture '{problem.texture.name}' in normal-map slot '{problem.propertyName}' " +
                        $"on material '{problem.material.name}' must use Texture Type 'Normal Map' in its import settings " +
                        $"(texture: {problem.texturePath}).",
                    context = problem.texture
                });
            }
        }

        public static List<NormalMapImportProblem> FindNormalMapImportProblems(IEnumerable<Material> materials)
        {
            var problems = new List<NormalMapImportProblem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var material in (materials ?? Enumerable.Empty<Material>()).Where(material => material != null).Distinct())
            {
                var shader = material.shader;
                if (shader == null)
                    continue;

                var propertyCount = shader.GetPropertyCount();
                for (var propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
                {
                    if (shader.GetPropertyType(propertyIndex) != ShaderPropertyType.Texture)
                        continue;

                    var propertyName = shader.GetPropertyName(propertyIndex);
                    if (!IsNormalMapTextureProperty(shader, propertyIndex, propertyName))
                        continue;

                    var texture = material.GetTexture(propertyName);
                    if (texture == null)
                        continue;

                    var texturePath = AssetDatabase.GetAssetPath(texture)?.Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(texturePath))
                        continue;

                    var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                    if (importer == null || importer.textureType == TextureImporterType.NormalMap)
                        continue;

                    var key = $"{AssetDatabase.GetAssetPath(material)}|{propertyName}|{texturePath}";
                    if (!seen.Add(key))
                        continue;

                    problems.Add(new NormalMapImportProblem
                    {
                        material = material,
                        texture = texture,
                        propertyName = propertyName,
                        texturePath = texturePath
                    });
                }
            }

            return problems;
        }

        private static bool IsNormalMapTextureProperty(Shader shader, int propertyIndex, string propertyName)
        {
            if (shader == null)
                return false;

            if ((shader.GetPropertyFlags(propertyIndex) & ShaderPropertyFlags.Normal) != 0)
                return true;

            return !string.IsNullOrWhiteSpace(propertyName) &&
                   (propertyName.IndexOf("normalmap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    string.Equals(propertyName, "_BumpMap", StringComparison.OrdinalIgnoreCase));
        }
        
        public static float GetTotalTextureMB(GameObject root, Renderer[] renderers, ContentValidationRules.ItemRule rule, SuperType superType, ItemType type)
        {
            long totalBytes = 0;
            HashSet<Texture> seen = new HashSet<Texture>();

            foreach (var r in renderers)
            {
                if (IsScooterDeckGriptapeProxy(root, r, superType, type))
                    continue;

                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;

                    foreach (var slot in rule.Slots)
                    {
                        if (string.IsNullOrEmpty(slot.ShaderProperty))
                            continue;

                        var tex = mat.GetTexture(slot.ShaderProperty);
                        if (tex == null)
                            continue;

                        string path = AssetDatabase.GetAssetPath(tex);

                        if (IsCoreCustomizationAsset(path))
                            continue; 

                        if (seen.Add(tex))
                        {
                            totalBytes += EstimateTextureSizeBytes(tex);
                        }
                    }
                }
            }

            return totalBytes / (1024f * 1024f);
        }
        public static long EstimateTextureSizeBytes(Texture tex)
        {
            if (tex == null) return 0;

#if UNITY_EDITOR
            var path = AssetDatabase.GetAssetPath(tex);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer != null)
            {
                int width = tex.width;
                int height = tex.height;

                int maxSize = importer.maxTextureSize;
                width = Mathf.Min(width, maxSize);
                height = Mathf.Min(height, maxSize);
                
                TextureImporterFormat format = importer.GetPlatformTextureSettings("Standalone").format;
            
                
                bool hasAlpha = importer.DoesSourceTextureHaveAlpha();

                if (importer.textureCompression == TextureImporterCompression.Uncompressed)
                {
                    format = TextureImporterFormat.RGBA32;
                }
                else
                {
                    format = hasAlpha ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1;
                }
                
                int bpp = 32;

                switch (format)
                {
                    case TextureImporterFormat.DXT1: bpp = 4; break;
                    case TextureImporterFormat.DXT5:
                    case TextureImporterFormat.BC7: bpp = 8; break;
                    case TextureImporterFormat.RGBA32: bpp = 32; break;
                }
                
                
                long size = (long)width * height * bpp / 8;

                
   
                if (importer.mipmapEnabled)
                    size = (long)(size * 1.33f);

                return size;
            }
#endif

            // fallback
            return (long)tex.width * tex.height * 4;
        }
        public static void LogReport(Object owner, IEnumerable<Issue> issues, string title = "Validate")
        {
            var list = issues?.ToList() ?? new List<Issue>();
            if (list.Count == 0)
            {
                Debug.Log($"[{title}] ✓ No issues found.", owner);
                return;
            }

            int errors = 0, warnings = 0;
            foreach (var i in list)
            {
                var ctx = i.context ? i.context : owner;
                switch (i.severity)
                {
                    case Severity.Error:   errors++;   Debug.LogError(i.message, ctx); break;
                    case Severity.Warning: warnings++; Debug.LogWarning(i.message, ctx); break;
                    default:                              Debug.Log(i.message, ctx); break;
                }
            }
            Debug.Log($"[{title}] → {errors} error(s), {warnings} warning(s).", owner);
        }
    }
}

#endif
