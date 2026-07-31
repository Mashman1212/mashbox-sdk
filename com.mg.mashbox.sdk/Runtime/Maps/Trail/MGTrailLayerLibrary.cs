using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.Trail
{
    /// <summary>
    /// A terrain-layer-style source library for MG Trail shaders. The generated
    /// arrays are editor-baked sub-assets, so they can be assigned directly to
    /// Texture2DArray properties in Shader Graph or a material.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MG Trail Layer Library",
        menuName = "MashBox/Maps/MG Trail Layer Library",
        order = 120)]
    public sealed class MGTrailLayerLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Layer
        {
            public string name = "Trail Layer";
            public Texture2D albedo;
            public Texture2D normal;
            public Texture2D mask;
        }

        [SerializeField] List<Layer> m_Layers = new List<Layer>();

        [Header("Array Settings")]
        [SerializeField, Min(32)] int m_Resolution = 1024;
        [SerializeField, Range(1, 64)] int m_Capacity = 16;
        [SerializeField] FilterMode m_FilterMode = FilterMode.Trilinear;
        [SerializeField] TextureWrapMode m_WrapMode = TextureWrapMode.Repeat;
        [SerializeField, Range(0, 16)] int m_AnisoLevel = 4;

        [Header("Missing Texture Defaults")]
        [SerializeField] Color m_DefaultAlbedo = Color.white;
        [SerializeField] Color m_DefaultNormal = new Color(0.5f, 0.5f, 1f, 1f);
        [SerializeField] Color m_DefaultMask = Color.white;

        [Header("Generated Arrays")]
        [SerializeField, HideInInspector] Texture2DArray m_AlbedoArray;
        [SerializeField, HideInInspector] Texture2DArray m_NormalArray;
        [SerializeField, HideInInspector] Texture2DArray m_MaskArray;

        public IReadOnlyList<Layer> Layers => m_Layers;
        public int LayerCount => Mathf.Min(m_Layers?.Count ?? 0, m_Capacity);
        public int Resolution => m_Resolution;
        public int Capacity => m_Capacity;
        public FilterMode ArrayFilterMode => m_FilterMode;
        public TextureWrapMode ArrayWrapMode => m_WrapMode;
        public int ArrayAnisoLevel => m_AnisoLevel;
        public Color DefaultAlbedo => m_DefaultAlbedo;
        public Color DefaultNormal => m_DefaultNormal;
        public Color DefaultMask => m_DefaultMask;
        public Texture2DArray AlbedoArray => m_AlbedoArray;
        public Texture2DArray NormalArray => m_NormalArray;
        public Texture2DArray MaskArray => m_MaskArray;

        public bool HasGeneratedArrays =>
            m_AlbedoArray != null &&
            m_NormalArray != null &&
            m_MaskArray != null;

        /// <summary>
        /// Convenience method for conventional MG Trail shader property names.
        /// Property names can be overridden for a custom shader.
        /// </summary>
        public void ApplyTo(
            Material material,
            string albedoProperty = "_TrailAlbedoArray",
            string normalProperty = "_TrailNormalArray",
            string maskProperty = "_TrailMaskArray",
            string layerCountProperty = "_TrailLayerCount")
        {
            if (material == null)
                return;

            if (!string.IsNullOrEmpty(albedoProperty))
                material.SetTexture(albedoProperty, m_AlbedoArray);
            if (!string.IsNullOrEmpty(normalProperty))
                material.SetTexture(normalProperty, m_NormalArray);
            if (!string.IsNullOrEmpty(maskProperty))
                material.SetTexture(maskProperty, m_MaskArray);
            if (!string.IsNullOrEmpty(layerCountProperty))
                material.SetInt(layerCountProperty, LayerCount);
        }

#if UNITY_EDITOR
        public void SetGeneratedArrays(
            Texture2DArray albedo,
            Texture2DArray normal,
            Texture2DArray mask)
        {
            m_AlbedoArray = albedo;
            m_NormalArray = normal;
            m_MaskArray = mask;
        }
#endif

        void OnValidate()
        {
            m_Resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(m_Resolution), 32, 8192);
            m_Capacity = Mathf.Clamp(m_Capacity, 1, 64);
            m_AnisoLevel = Mathf.Clamp(m_AnisoLevel, 0, 16);
            m_Layers ??= new List<Layer>();
        }
    }
}
