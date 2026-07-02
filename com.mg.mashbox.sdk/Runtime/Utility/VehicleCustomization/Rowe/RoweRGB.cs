using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Rowe
{
    public class RoweRGB : MonoBehaviour
    {
        [SerializeField] private float colorLerpSpeed = 1f;
        [SerializeField] private bool loopXOffset;
        [SerializeField] private float xOffsetSpeed = 1f;
        [SerializeField] private AnimationCurve xOffsetCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private bool allowNegativeOffset;
        [SerializeField] private bool enableDebugLogs;

        private Material _material;
        private Renderer _renderer;

        private int _currentColorIndex;
        private int _nextColorIndex = 1;
        private float _lerpT;
        private float _xOffsetTime;
        private int _emissivePropertyId = -1;
        private int _uvPropertyId = -1;
        private bool _loggedMissingXOffsetProperty;

        private static readonly Color[] RgbColors =
        {
            Color.red,
            Color.green,
            Color.blue
        };

        private static readonly int[] EmissiveColorPropertyIds =
        {
            Shader.PropertyToID("_EmissiveColor"),
            Shader.PropertyToID("EmissiveColor"),
            Shader.PropertyToID("_EmmisiveColor"),
            Shader.PropertyToID("EmmisiveColor"),
            Shader.PropertyToID("_EmissionColor")
        };

        private static readonly int[] UvPropertyIds =
        {
            Shader.PropertyToID("_EmissiveColorMap"),
            Shader.PropertyToID("EmissiveColorMap"),
            Shader.PropertyToID("_EmmisiveColorMap"),
            Shader.PropertyToID("EmmisiveColorMap"),
            Shader.PropertyToID("_MainTex"),
            Shader.PropertyToID("_BaseMap")
        };

        // Start is called before the first frame update
        void Start()
        {
            if (TryGetComponent<MeshRenderer>(out var meshRenderer))
            {
                _renderer = meshRenderer;
                LogDebug("Using MeshRenderer.");
            }
            else if (TryGetComponent<SkinnedMeshRenderer>(out var skinnedMeshRenderer))
            {
                _renderer = skinnedMeshRenderer;
                LogDebug("Using SkinnedMeshRenderer.");
            }
            else
            {
                LogWarning("No MeshRenderer or SkinnedMeshRenderer found. Disabling RoweRGB.");
                enabled = false;
                return;
            }

            _material = _renderer.material;
            _material.EnableKeyword("_EMISSION");
            CacheEmissiveColorProperty();
            CacheUvProperty();
            LogDebug("Initialized RoweRGB.");
        }

        // Update is called once per frame
        void Update()
        {
            if (_material == null)
            {
                return;
            }

            _lerpT += Mathf.Max(0f, colorLerpSpeed) * Time.deltaTime;
            var color = Color.Lerp(RgbColors[_currentColorIndex], RgbColors[_nextColorIndex], _lerpT);
            SetEmissiveColor(color);
            UpdateXOffset();

            if (_lerpT >= 1f)
            {
                _lerpT = 0f;
                _currentColorIndex = _nextColorIndex;
                _nextColorIndex = (_nextColorIndex + 1) % RgbColors.Length;
                LogDebug(
                    $"Color transition complete. Current index: {_currentColorIndex}, next index: {_nextColorIndex}.");
            }
        }

        private void SetEmissiveColor(Color color)
        {
            if (_emissivePropertyId == -1)
            {
                return;
            }

            _material.SetColor(_emissivePropertyId, color);
        }

        private void CacheEmissiveColorProperty()
        {
            for (var i = 0; i < EmissiveColorPropertyIds.Length; i++)
            {
                var propertyId = EmissiveColorPropertyIds[i];
                if (_material.HasProperty(propertyId))
                {
                    _emissivePropertyId = propertyId;
                    LogDebug($"Using emissive color property id: {_emissivePropertyId}.");
                    return;
                }
            }

            LogWarning("No compatible emissive color property found on material.");
        }

        private void CacheUvProperty()
        {
            for (var i = 0; i < UvPropertyIds.Length; i++)
            {
                var propertyId = UvPropertyIds[i];
                if (_material.HasProperty(propertyId))
                {
                    _uvPropertyId = propertyId;
                    LogDebug($"Using UV property id: {_uvPropertyId}.");
                    return;
                }
            }

            if (loopXOffset)
            {
                LogWarning("loopXOffset is enabled, but no compatible texture property was found for UV offset.");
            }
        }

        private void UpdateXOffset()
        {
            if (!loopXOffset || _uvPropertyId == -1)
            {
                if (loopXOffset && _uvPropertyId == -1 && !_loggedMissingXOffsetProperty)
                {
                    _loggedMissingXOffsetProperty = true;
                    LogWarning("Skipping X offset updates because no UV property was cached.");
                }

                return;
            }

            var speed = allowNegativeOffset ? xOffsetSpeed : Mathf.Max(0f, xOffsetSpeed);
            _xOffsetTime = Mathf.Repeat(_xOffsetTime + speed * Time.deltaTime, 1f);
            var curveValue = xOffsetCurve.Evaluate(_xOffsetTime);
            var offsetX = allowNegativeOffset ? curveValue : Mathf.Repeat(curveValue, 1f);
            SetMaterialOffset(offsetX);
        }

        private void SetMaterialOffset(float xOffset)
        {
            if (_uvPropertyId == -1)
            {
                return;
            }

            var currentOffset = _material.GetTextureOffset(_uvPropertyId);
            currentOffset.x = xOffset;
            _material.SetTextureOffset(_uvPropertyId, currentOffset);
        }

        private void LogDebug(string message)
        {
            if (!enableDebugLogs)
            {
                return;
            }

            Debug.Log($"[RoweRGB] {message}", this);
        }

        private void LogWarning(string message)
        {
            if (!enableDebugLogs)
            {
                return;
            }

            Debug.LogWarning($"[RoweRGB] {message}", this);
        }
    }
}