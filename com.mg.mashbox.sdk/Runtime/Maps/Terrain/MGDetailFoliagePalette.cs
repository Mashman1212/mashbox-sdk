using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    [CreateAssetMenu(fileName = "MG Detail Foliage Palette", menuName = "MashBox/Maps/Detail Foliage Palette")]
    public sealed class MGDetailFoliagePalette : ScriptableObject
    {
        public enum FoliageRole
        {
            DenseCarpet,
            MediumGrass,
            TallClump,
            DarkUndergrowth,
            TrailEdge,
            HeroPlant,
            Custom
        }

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] bool m_Enabled = true;
            [SerializeField] string m_Name = "Grass Variant";
            [SerializeField] FoliageRole m_Role = FoliageRole.Custom;
            [SerializeField] GameObject m_Prefab;
            [SerializeField] Mesh m_Mesh;
            [SerializeField] Material m_Material;
            [SerializeField, Min(0f)] float m_Weight = 1f;
            [SerializeField, Range(0f, 4f)] float m_DensityMultiplier = 1f;
            [SerializeField, Min(0.001f)] float m_MinWidth = 0.8f;
            [SerializeField, Min(0.001f)] float m_MaxWidth = 1.2f;
            [SerializeField, Min(0.001f)] float m_MinHeight = 0.8f;
            [SerializeField, Min(0.001f)] float m_MaxHeight = 1.2f;
            [SerializeField, Tooltip("Local Y offset applied to this palette entry after it is conformed to the surface.")]
            float m_YOffset;
            [SerializeField, Range(1f, 512f), Tooltip("Approximate clump size measured in density-map cells.")]
            float m_ClumpSize = 24f;
            [SerializeField, Range(0f, 1f)] float m_ClumpStrength = 0.5f;
            [SerializeField, Range(0.1f, 4f)] float m_ClumpContrast = 1.25f;
            [SerializeField, Range(-1f, 1f), Tooltip("Positive values favour painted edges; negative values favour the interior.")]
            float m_EdgeBias;
            [SerializeField, Range(0f, 90f)] float m_MinSlope;
            [SerializeField, Range(0f, 90f)] float m_MaxSlope = 90f;
            [SerializeField] float m_MinWorldHeight = -10000f;
            [SerializeField] float m_MaxWorldHeight = 10000f;
            [SerializeField] int m_SeedOffset;

            public bool Enabled => m_Enabled;
            public string Name => string.IsNullOrWhiteSpace(m_Name) ? m_Role.ToString() : m_Name;
            public FoliageRole Role => m_Role;
            public GameObject Prefab => m_Prefab;
            public Mesh Mesh => m_Mesh;
            public Material Material => m_Material;
            public float Weight => Mathf.Max(0f, m_Weight);
            public float DensityMultiplier => Mathf.Max(0f, m_DensityMultiplier);
            public float MinWidth => Mathf.Max(0.001f, Mathf.Min(m_MinWidth, m_MaxWidth));
            public float MaxWidth => Mathf.Max(MinWidth, Mathf.Max(m_MinWidth, m_MaxWidth));
            public float MinHeight => Mathf.Max(0.001f, Mathf.Min(m_MinHeight, m_MaxHeight));
            public float MaxHeight => Mathf.Max(MinHeight, Mathf.Max(m_MinHeight, m_MaxHeight));
            public float YOffset => m_YOffset;
            public float ClumpSize => Mathf.Max(1f, m_ClumpSize);
            public float ClumpStrength => Mathf.Clamp01(m_ClumpStrength);
            public float ClumpContrast => Mathf.Max(0.1f, m_ClumpContrast);
            public float EdgeBias => Mathf.Clamp(m_EdgeBias, -1f, 1f);
            public float MinSlope => Mathf.Clamp(Mathf.Min(m_MinSlope, m_MaxSlope), 0f, 90f);
            public float MaxSlope => Mathf.Clamp(Mathf.Max(m_MinSlope, m_MaxSlope), 0f, 90f);
            public float MinWorldHeight => Mathf.Min(m_MinWorldHeight, m_MaxWorldHeight);
            public float MaxWorldHeight => Mathf.Max(m_MinWorldHeight, m_MaxWorldHeight);
            public int SeedOffset => m_SeedOffset;
            public bool HasRenderablePrototype => m_Prefab != null || (m_Mesh != null && m_Material != null);

            internal Entry(string name, FoliageRole role)
            {
                m_Name = name;
                m_Role = role;
                ApplyRoleDefaults(role);
            }

            internal bool TrySetPrototype(MGTerrain.Prototype prototype)
            {
                if (prototype == null || HasRenderablePrototype)
                    return false;
                if (prototype.Prefab != null)
                {
                    m_Prefab = prototype.Prefab;
                    return true;
                }
                if (prototype.Mesh == null || prototype.Material == null)
                    return false;
                m_Mesh = prototype.Mesh;
                m_Material = prototype.Material;
                return true;
            }

            void ApplyRoleDefaults(FoliageRole role)
            {
                switch (role)
                {
                    case FoliageRole.DenseCarpet:
                        m_Weight = 5f; m_DensityMultiplier = 1f; m_ClumpSize = 42f; m_ClumpStrength = 0.2f; m_EdgeBias = -0.2f;
                        break;
                    case FoliageRole.MediumGrass:
                        m_Weight = 2.5f; m_DensityMultiplier = 0.8f; m_ClumpSize = 28f; m_ClumpStrength = 0.45f;
                        break;
                    case FoliageRole.TallClump:
                        m_Weight = 0.8f; m_DensityMultiplier = 0.45f; m_MinHeight = 1.1f; m_MaxHeight = 1.8f; m_ClumpSize = 18f; m_ClumpStrength = 0.9f; m_ClumpContrast = 2f;
                        break;
                    case FoliageRole.DarkUndergrowth:
                        m_Weight = 1.25f; m_DensityMultiplier = 0.7f; m_ClumpSize = 34f; m_ClumpStrength = 0.65f; m_EdgeBias = -0.35f;
                        break;
                    case FoliageRole.TrailEdge:
                        m_Weight = 0.65f; m_DensityMultiplier = 0.35f; m_ClumpSize = 14f; m_ClumpStrength = 0.7f; m_EdgeBias = 0.9f;
                        break;
                    case FoliageRole.HeroPlant:
                        m_Weight = 0.08f; m_DensityMultiplier = 0.08f; m_MinWidth = 0.9f; m_MaxWidth = 1.4f; m_MinHeight = 0.9f; m_MaxHeight = 1.5f; m_ClumpSize = 48f; m_ClumpStrength = 0.85f; m_ClumpContrast = 2.5f;
                        break;
                }
            }
        }

        [SerializeField] int m_Seed = 739391;
        [SerializeField, Range(0f, 4f)] float m_OverallDensity = 1f;
        [SerializeField, Range(1f, 1024f), Tooltip("Size of broad dense/sparse regions in density-map cells.")]
        float m_BreakupScale = 96f;
        [SerializeField, Range(0f, 1f)] float m_BreakupStrength = 0.45f;
        [SerializeField, Range(0f, 1f), Tooltip("Minimum density retained inside the sparsest breakup regions.")]
        float m_MinimumBreakupDensity = 0.18f;
        [SerializeField, Range(0.1f, 4f)] float m_BreakupContrast = 1.4f;
        [SerializeField, Range(0f, 1f), Tooltip("Reduces density around the edge of the painted mask to avoid a hard cut.")]
        float m_EdgeFeather = 0.65f;
        [SerializeField, Range(1, 32), Tooltip("Width of the trail/mask transition in density-map cells.")]
        int m_EdgeFeatherCells = 6;
        [SerializeField] List<Entry> m_Entries = new List<Entry>();

        public int Seed => m_Seed;
        public float OverallDensity => Mathf.Max(0f, m_OverallDensity);
        public float BreakupScale => Mathf.Max(1f, m_BreakupScale);
        public float BreakupStrength => Mathf.Clamp01(m_BreakupStrength);
        public float MinimumBreakupDensity => Mathf.Clamp01(m_MinimumBreakupDensity);
        public float BreakupContrast => Mathf.Max(0.1f, m_BreakupContrast);
        public float EdgeFeather => Mathf.Clamp01(m_EdgeFeather);
        public int EdgeFeatherCells => Mathf.Clamp(m_EdgeFeatherCells, 1, 32);
        public IReadOnlyList<Entry> Entries => m_Entries;

        public void RandomizeSeed()
        {
            m_Seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        }

        public void ConfigureNaturalStarterSet()
        {
            m_Entries = new List<Entry>
            {
                new Entry("Dense Carpet", FoliageRole.DenseCarpet),
                new Entry("Medium Breakup", FoliageRole.MediumGrass),
                new Entry("Tall Warm Clumps", FoliageRole.TallClump),
                new Entry("Dark Undergrowth", FoliageRole.DarkUndergrowth),
                new Entry("Trail Edge", FoliageRole.TrailEdge),
                new Entry("Hero Plants / Flowers", FoliageRole.HeroPlant)
            };
        }

        public bool TrySeedPrimaryPrototype(MGTerrain.Prototype prototype)
        {
            if (m_Entries == null || m_Entries.Count == 0)
                ConfigureNaturalStarterSet();
            return m_Entries[0] != null && m_Entries[0].TrySetPrototype(prototype);
        }
    }
}
