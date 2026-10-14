using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        // Uses the same shared/per-index switch and root pivot as GodGrass.
        // Union all slices: one draw/cell may contain any of the eight indices.
        bool TryGetGrassShaderSizeBounds(Prototype prototype, out Bounds bounds)
        {
            bounds = default;
            bool found = false, first = true;
            foreach (var part in GetDenseDetailRenderParts(prototype).parts)
            {
                if (part.mesh == null) continue;
                Bounds local = part.mesh.bounds;
                var material = part.material;
                if (material != null && material.HasProperty("_GrassWidthMultiplier"))
                {
                    found = true;
                    bool perIndex = material.HasProperty("_GrassUseLayerParameters") && material.GetFloat("_GrassUseLayerParameters") > .5f;
                    for (int i = 0; i < (perIndex ? 8 : 1); i++)
                    {
                        string prefix = perIndex ? $"_GrassLayer{i:00}_" : "_";
                        float Value(string name, float fallback) => material.HasProperty(prefix + name) ? material.GetFloat(prefix + name) : fallback;
                        float width = Mathf.Clamp(Value("GrassWidthMultiplier", 1), .1f, 4f);
                        float height = Mathf.Clamp(Value("GrassHeightMultiplier", 1), .1f, 4f);
                        Vector3 pivot = new Vector3(0, Value("WindRootHeight", 0), 0);
                        Vector3 scale = new Vector3(width, height, width);
                        local.Encapsulate(pivot + Vector3.Scale(part.mesh.bounds.min - pivot, scale));
                        local.Encapsulate(pivot + Vector3.Scale(part.mesh.bounds.max - pivot, scale));
                    }
                }
                Bounds transformed = TransformBounds(local, part.relativeMatrix);
                if (first) { bounds = transformed; first = false; }
                else bounds.Encapsulate(transformed);
            }
            return found;
        }
    }
}
