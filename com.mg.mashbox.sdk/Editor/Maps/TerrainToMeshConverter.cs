#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    [Serializable]
    internal sealed class TerrainConversionOptions
    {
        public bool ConvertMesh;
        public bool AddMeshCollider;
        public bool ExportSplatMaps;
        public bool ConvertTrees;
        public bool ConvertDetails;
        public bool DisableSourceTerrain;
        public int MaximumMeshResolution = 513;
    }

    internal sealed class TerrainConversionSummary
    {
        public long TreeCount;
        public long DetailCount;
        public int SplatMapCount;
    }

    internal static class TerrainToMeshConverter
    {
        public const int MaxTreeGameObjects = 20000;
        public const int MaxDetailGameObjects = 100000;

        public static TerrainConversionSummary Analyze(Terrain terrain)
        {
            var summary = new TerrainConversionSummary();
            if (terrain == null || terrain.terrainData == null)
                return summary;

            TerrainData data = terrain.terrainData;
            summary.TreeCount = data.treeInstanceCount;
            summary.SplatMapCount = Mathf.CeilToInt(data.alphamapLayers / 4f);

            long detailCount = 0;
            for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            {
                int[,] density = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, layer);
                foreach (int count in density)
                    detailCount += count;
            }

            summary.DetailCount = detailCount;
            return summary;
        }

        public static TerrainConversionSummary Analyze(IEnumerable<Terrain> terrains)
        {
            var combined = new TerrainConversionSummary();
            if (terrains == null)
                return combined;

            foreach (Terrain terrain in terrains.Where(terrain => terrain != null).Distinct())
            {
                TerrainConversionSummary terrainSummary = Analyze(terrain);
                combined.TreeCount += terrainSummary.TreeCount;
                combined.DetailCount += terrainSummary.DetailCount;
                combined.SplatMapCount += terrainSummary.SplatMapCount;
            }

            return combined;
        }

        public static bool ConfirmLargeGameObjectConversions(TerrainConversionSummary summary, TerrainConversionOptions options)
        {
            if (summary == null || options == null)
                return false;

            if (options.ConvertTrees && summary.TreeCount > 0 &&
                !EditorUtility.DisplayDialog(
                    "Convert Painted Trees?",
                    $"This will instantiate {summary.TreeCount:N0} tree GameObjects. Large GameObject counts can make the Editor and scene very slow.\n\nContinue?",
                    "Convert Trees",
                    "Cancel"))
                return false;

            if (options.ConvertDetails && summary.DetailCount > 0 &&
                !EditorUtility.DisplayDialog(
                    "Convert Painted Details?",
                    $"This will instantiate {summary.DetailCount:N0} detail GameObjects. This is intended as a temporary workflow until a dedicated instancing system is available.\n\nContinue?",
                    "Convert Details",
                    "Cancel"))
                return false;

            return true;
        }

        public static GameObject Convert(Terrain terrain, string assetFolder, TerrainConversionOptions options)
        {
            if (terrain == null || terrain.terrainData == null)
                throw new ArgumentException("A Terrain with valid Terrain Data is required.", nameof(terrain));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            TerrainConversionSummary summary = Analyze(terrain);
            if (options.ConvertTrees && summary.TreeCount > MaxTreeGameObjects)
                throw new InvalidOperationException($"Tree conversion is limited to {MaxTreeGameObjects:N0} instances; this terrain contains {summary.TreeCount:N0}.");
            if (options.ConvertDetails && summary.DetailCount > MaxDetailGameObjects)
                throw new InvalidOperationException($"Detail conversion is limited to {MaxDetailGameObjects:N0} instances; this terrain contains {summary.DetailCount:N0}.");

            EnsureAssetFolder(assetFolder);
            TerrainData data = terrain.terrainData;
            string safeTerrainName = SanitizeName(terrain.name);
            var root = new GameObject(terrain.name + "_Converted");
            Undo.RegisterCreatedObjectUndo(root, "Convert Terrain");
            SceneManager.MoveGameObjectToScene(root, terrain.gameObject.scene);
            CopyTransformAndParent(terrain.transform, root.transform);

            int skippedTrees = 0;
            long skippedDetails = 0;
            var createdAssets = new List<string>();
            bool sourceTerrainWasEnabled = terrain.enabled;
            TerrainCollider sourceTerrainCollider = terrain.GetComponent<TerrainCollider>();
            bool sourceColliderWasEnabled = sourceTerrainCollider != null && sourceTerrainCollider.enabled;
            try
            {
                if (options.ConvertMesh)
                {
                    EditorUtility.DisplayProgressBar("Converting Terrain", "Building terrain mesh...", 0.05f);
                    Mesh mesh = BuildTerrainMesh(data, options.MaximumMeshResolution);
                    mesh.name = safeTerrainName + "_Mesh";
                    string meshPath = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder}/{mesh.name}.asset");
                    AssetDatabase.CreateAsset(mesh, meshPath);
                    createdAssets.Add(meshPath);

                    MeshFilter filter = root.AddComponent<MeshFilter>();
                    MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                    filter.sharedMesh = mesh;
                    renderer.sharedMaterial = terrain.materialTemplate;
                    if (options.AddMeshCollider)
                        root.AddComponent<MeshCollider>().sharedMesh = mesh;
                }

                if (options.ExportSplatMaps)
                {
                    EditorUtility.DisplayProgressBar("Converting Terrain", "Exporting splat maps...", 0.15f);
                    ExportSplatMaps(data, assetFolder, safeTerrainName, createdAssets);
                }

                if (options.ConvertTrees)
                    skippedTrees = ConvertTrees(terrain, root.transform, summary.TreeCount);

                if (options.ConvertDetails)
                    skippedDetails = ConvertDetails(terrain, root.transform, assetFolder, summary.DetailCount, createdAssets);

                if (options.DisableSourceTerrain)
                {
                    Undo.RecordObject(terrain, "Disable Source Terrain");
                    terrain.enabled = false;
                    EditorUtility.SetDirty(terrain);
                    TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
                    if (terrainCollider != null)
                    {
                        Undo.RecordObject(terrainCollider, "Disable Source Terrain Collider");
                        terrainCollider.enabled = false;
                        EditorUtility.SetDirty(terrainCollider);
                    }
                }

                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            }
            catch
            {
                Undo.DestroyObjectImmediate(root);
                foreach (string createdAsset in createdAssets)
                    AssetDatabase.DeleteAsset(createdAsset);
                terrain.enabled = sourceTerrainWasEnabled;
                if (sourceTerrainCollider != null)
                    sourceTerrainCollider.enabled = sourceColliderWasEnabled;
                throw;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (skippedTrees > 0 || skippedDetails > 0)
                Debug.LogWarning($"Terrain conversion skipped {skippedTrees:N0} trees and {skippedDetails:N0} details whose prototypes were missing or unsupported.", terrain);

            Debug.Log($"Converted terrain '{terrain.name}' beneath '{root.name}'. Assets were written to {assetFolder}.", root);
            return root;
        }

        public static string ToProjectAssetPath(string absoluteFolder)
        {
            if (string.IsNullOrWhiteSpace(absoluteFolder))
                return null;

            string dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            string candidate = Path.GetFullPath(absoluteFolder).Replace('\\', '/').TrimEnd('/');
            if (string.Equals(candidate, dataPath, StringComparison.OrdinalIgnoreCase))
                return "Assets";
            if (!candidate.StartsWith(dataPath + "/", StringComparison.OrdinalIgnoreCase))
                return null;
            return "Assets" + candidate.Substring(dataPath.Length);
        }

        private static Mesh BuildTerrainMesh(TerrainData data, int maximumResolution)
        {
            int sourceResolution = data.heightmapResolution;
            int step = Mathf.Max(1, Mathf.CeilToInt((sourceResolution - 1f) / Mathf.Max(1, maximumResolution - 1)));
            List<int> samples = BuildSampleIndices(sourceResolution, step);
            int width = samples.Count;
            int height = samples.Count;
            var vertices = new Vector3[width * height];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            Vector3 size = data.size;

            for (int z = 0; z < height; z++)
            {
                float nz = samples[z] / (sourceResolution - 1f);
                for (int x = 0; x < width; x++)
                {
                    float nx = samples[x] / (sourceResolution - 1f);
                    int index = z * width + x;
                    vertices[index] = new Vector3(nx * size.x, data.GetInterpolatedHeight(nx, nz), nz * size.z);
                    normals[index] = data.GetInterpolatedNormal(nx, nz);
                    uvs[index] = new Vector2(nx, nz);
                }
            }

            var triangles = new List<int>((width - 1) * (height - 1) * 6);
            for (int z = 0; z < height - 1; z++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    float centerX = (samples[x] + samples[x + 1]) * 0.5f / (sourceResolution - 1f);
                    float centerZ = (samples[z] + samples[z + 1]) * 0.5f / (sourceResolution - 1f);
                    if (IsTerrainHole(data, centerX, centerZ))
                        continue;

                    int a = z * width + x;
                    int b = (z + 1) * width + x;
                    int c = a + 1;
                    int d = b + 1;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(d);
                }
            }

            var mesh = new Mesh
            {
                indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = vertices,
                normals = normals,
                uv = uvs
            };
            // Keep UV1 free for Unity lightmapping. MashBox splat painting uses
            // TEXCOORD2 (shown as UV2 in the SDK), so terrain conversions start
            // with a paintable copy of the terrain's normalized UV0 layout.
            mesh.SetUVs(2, new List<Vector2>(uvs));
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static List<int> BuildSampleIndices(int resolution, int step)
        {
            var result = new List<int>();
            for (int sample = 0; sample < resolution - 1; sample += step)
                result.Add(sample);
            if (result.Count == 0 || result[result.Count - 1] != resolution - 1)
                result.Add(resolution - 1);
            return result;
        }

        private static bool IsTerrainHole(TerrainData data, float normalizedX, float normalizedZ)
        {
            if (data.holesResolution <= 0)
                return false;
            int x = Mathf.Clamp(Mathf.RoundToInt(normalizedX * (data.holesResolution - 1)), 0, data.holesResolution - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(normalizedZ * (data.holesResolution - 1)), 0, data.holesResolution - 1);
            return data.IsHole(x, z);
        }

        private static void ExportSplatMaps(TerrainData data, string assetFolder, string terrainName, ICollection<string> createdAssets)
        {
            if (data.alphamapLayers == 0)
                return;

            float[,,] weights = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            int mapCount = Mathf.CeilToInt(data.alphamapLayers / 4f);
            for (int map = 0; map < mapCount; map++)
            {
                var colors = new Color[data.alphamapWidth * data.alphamapHeight];
                for (int y = 0; y < data.alphamapHeight; y++)
                {
                    for (int x = 0; x < data.alphamapWidth; x++)
                    {
                        int firstLayer = map * 4;
                        colors[y * data.alphamapWidth + x] = new Color(
                            GetWeight(weights, y, x, firstLayer),
                            GetWeight(weights, y, x, firstLayer + 1),
                            GetWeight(weights, y, x, firstLayer + 2),
                            GetWeight(weights, y, x, firstLayer + 3));
                    }
                }

                var texture = new Texture2D(data.alphamapWidth, data.alphamapHeight, TextureFormat.RGBA32, false, true);
                texture.SetPixels(colors);
                texture.Apply(false, false);
                string path = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder}/{terrainName}_Splat{map}.png");
                File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                createdAssets.Add(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.sRGBTexture = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }
        }

        private static float GetWeight(float[,,] weights, int y, int x, int layer)
        {
            return layer < weights.GetLength(2) ? weights[y, x, layer] : 0f;
        }

        private static int ConvertTrees(Terrain terrain, Transform convertedRoot, long totalCount)
        {
            TerrainData data = terrain.terrainData;
            TreePrototype[] prototypes = data.treePrototypes;
            Transform treesRoot = CreateChildRoot("Trees", convertedRoot);
            var prototypeRoots = new Dictionary<int, Transform>();
            int skipped = 0;
            TreeInstance[] instances = data.treeInstances;

            for (int index = 0; index < instances.Length; index++)
            {
                TreeInstance tree = instances[index];
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length || prototypes[tree.prototypeIndex].prefab == null)
                {
                    skipped++;
                    continue;
                }

                GameObject prefab = prototypes[tree.prototypeIndex].prefab;
                if (!prototypeRoots.TryGetValue(tree.prototypeIndex, out Transform prototypeRoot))
                {
                    prototypeRoot = CreateChildRoot(SanitizeName(prefab.name), treesRoot);
                    prototypeRoots.Add(tree.prototypeIndex, prototypeRoot);
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, terrain.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    skipped++;
                    continue;
                }

                instance.name = prefab.name;
                instance.transform.SetParent(prototypeRoot, false);
                Vector3 normalizedPosition = tree.position;
                instance.transform.localPosition = Vector3.Scale(normalizedPosition, data.size);
                instance.transform.localRotation = Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f) * prefab.transform.localRotation;
                instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));

                if ((index & 127) == 0)
                    EditorUtility.DisplayProgressBar("Converting Terrain", $"Instantiating trees ({index:N0} / {totalCount:N0})...", 0.2f + 0.3f * index / Mathf.Max(1f, instances.Length));
            }

            return skipped;
        }

        private static long ConvertDetails(Terrain terrain, Transform convertedRoot, string assetFolder, long totalCount, ICollection<string> createdAssets)
        {
            TerrainData data = terrain.terrainData;
            DetailPrototype[] prototypes = data.detailPrototypes;
            Transform detailsRoot = CreateChildRoot("Details", convertedRoot);
            long completed = 0;
            long skipped = 0;

            for (int layer = 0; layer < prototypes.Length; layer++)
            {
                DetailPrototype prototype = prototypes[layer];
                int[,] density = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, layer);
                GameObject prefab = prototype.prototype;
                Mesh textureMesh = null;
                Material textureMaterial = null;
                if (prefab == null && prototype.prototypeTexture != null)
                    CreateTextureDetailAssets(prototype, layer, assetFolder, createdAssets, out textureMesh, out textureMaterial);

                string prototypeName = prefab != null ? prefab.name : prototype.prototypeTexture != null ? prototype.prototypeTexture.name : $"Detail {layer}";
                Transform prototypeRoot = CreateChildRoot(SanitizeName(prototypeName), detailsRoot);
                var random = new System.Random(unchecked(GetStableSeed(data) * 397 ^ layer));

                for (int z = 0; z < data.detailHeight; z++)
                {
                    for (int x = 0; x < data.detailWidth; x++)
                    {
                        int count = density[z, x];
                        if (prefab == null && textureMesh == null)
                        {
                            skipped += count;
                            completed += count;
                            continue;
                        }

                        for (int item = 0; item < count; item++)
                        {
                            float nx = (x + (float)random.NextDouble()) / data.detailWidth;
                            float nz = (z + (float)random.NextDouble()) / data.detailHeight;
                            float width = Mathf.Lerp(prototype.minWidth, prototype.maxWidth, (float)random.NextDouble());
                            float height = Mathf.Lerp(prototype.minHeight, prototype.maxHeight, (float)random.NextDouble());
                            GameObject instance;
                            Quaternion baseRotation;
                            Vector3 baseScale;
                            if (prefab != null)
                            {
                                instance = PrefabUtility.InstantiatePrefab(prefab, terrain.gameObject.scene) as GameObject;
                                if (instance == null)
                                {
                                    skipped++;
                                    completed++;
                                    continue;
                                }
                                baseRotation = prefab.transform.localRotation;
                                baseScale = prefab.transform.localScale;
                            }
                            else
                            {
                                instance = new GameObject(prototypeName);
                                instance.AddComponent<MeshFilter>().sharedMesh = textureMesh;
                                instance.AddComponent<MeshRenderer>().sharedMaterial = textureMaterial;
                                baseRotation = Quaternion.identity;
                                baseScale = Vector3.one;
                            }

                            instance.name = prototypeName;
                            instance.transform.SetParent(prototypeRoot, false);
                            instance.transform.localPosition = new Vector3(nx * data.size.x, data.GetInterpolatedHeight(nx, nz), nz * data.size.z);
                            instance.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f) * baseRotation;
                            instance.transform.localScale = Vector3.Scale(baseScale, new Vector3(width, height, width));
                            completed++;

                            if ((completed & 127) == 0)
                                EditorUtility.DisplayProgressBar("Converting Terrain", $"Instantiating details ({completed:N0} / {totalCount:N0})...", 0.5f + 0.48f * completed / Mathf.Max(1f, totalCount));
                        }
                    }
                }
            }

            return skipped;
        }

        private static void CreateTextureDetailAssets(DetailPrototype prototype, int layer, string assetFolder, ICollection<string> createdAssets, out Mesh mesh, out Material material)
        {
            mesh = BuildCrossedQuadMesh();
            mesh.name = SanitizeName(prototype.prototypeTexture.name) + "_DetailMesh";
            string meshPath = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder}/{mesh.name}.asset");
            AssetDatabase.CreateAsset(mesh, meshPath);
            createdAssets.Add(meshPath);

            Shader shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Transparent Cutout") ?? Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("No suitable shader was found for texture-based terrain details.");
            material = new Material(shader) { name = SanitizeName(prototype.prototypeTexture.name) + $"_DetailMaterial_{layer}" };
            material.mainTexture = prototype.prototypeTexture;
            if (material.HasProperty("_BaseColorMap"))
                material.SetTexture("_BaseColorMap", prototype.prototypeTexture);
            if (material.HasProperty("_UnlitColorMap"))
                material.SetTexture("_UnlitColorMap", prototype.prototypeTexture);
            if (material.HasProperty("_AlphaCutoffEnable"))
                material.SetFloat("_AlphaCutoffEnable", 1f);
            if (material.HasProperty("_AlphaCutoff"))
                material.SetFloat("_AlphaCutoff", 0.5f);
            if (material.HasProperty("_CullMode"))
                material.SetFloat("_CullMode", 0f);
            material.doubleSidedGI = true;
            string materialPath = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder}/{material.name}.mat");
            AssetDatabase.CreateAsset(material, materialPath);
            createdAssets.Add(materialPath);
        }

        private static Mesh BuildCrossedQuadMesh()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(0f, 0f, -0.5f), new Vector3(0f, 1f, -0.5f), new Vector3(0f, 1f, 0.5f), new Vector3(0f, 0f, 0.5f)
            });
            mesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f)
            });
            mesh.SetTriangles(new[]
            {
                0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0,
                4, 5, 6, 4, 6, 7, 6, 5, 4, 7, 6, 4
            }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Transform CreateChildRoot(string name, Transform parent)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void CopyTransformAndParent(Transform source, Transform destination)
        {
            destination.SetParent(source.parent, false);
            destination.localPosition = source.localPosition;
            destination.localRotation = Quaternion.identity;
            destination.localScale = source.localScale;
        }

        private static void EnsureAssetFolder(string assetFolder)
        {
            if (string.IsNullOrWhiteSpace(assetFolder) || (!assetFolder.Equals("Assets", StringComparison.Ordinal) && !assetFolder.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new ArgumentException("The output folder must be inside the project's Assets folder.", nameof(assetFolder));
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;

            string[] parts = assetFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Terrain";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Trim();
        }

        private static int GetStableSeed(UnityEngine.Object value)
        {
#if UNITY_6000_0_OR_NEWER
            return value.GetEntityId().GetHashCode();
#else
            return value.GetInstanceID();
#endif
        }
    }
}

#endif
