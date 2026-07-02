using UnityEngine;

namespace MashBoxSDK.Rutime.Utility.VehicleCustomization.Rowe
{
    public class RoweFlameTireRGB : MonoBehaviour
    {
        [SerializeField] private float colorLerpSpeed = 1f;
        [SerializeField] private bool loopXOffset;
        [SerializeField] private float xOffsetSpeed = 1f;
        [SerializeField] private AnimationCurve xOffsetCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private bool allowNegativeOffset;

        private Material _material;
        private MeshRenderer _mr;

        private int _currentColorIndex;
        private int _nextColorIndex = 1;
        private float _lerpT;
        private float _xOffsetTime;
        private int _uvPropertyId = -1;

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
            _mr = GetComponent<MeshRenderer>();
            if (_mr == null)
            {
                enabled = false;
                return;
            }

            _material = _mr.material;
            _material.EnableKeyword("_EMISSION");
            CacheUvProperty();
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
            }
        }

        private void SetEmissiveColor(Color color)
        {
            for (var i = 0; i < EmissiveColorPropertyIds.Length; i++)
            {
                var propertyId = EmissiveColorPropertyIds[i];
                if (_material.HasProperty(propertyId))
                {
                    _material.SetColor(propertyId, color);
                    return;
                }
            }
        }

        private void CacheUvProperty()
        {
            for (var i = 0; i < UvPropertyIds.Length; i++)
            {
                var propertyId = UvPropertyIds[i];
                if (_material.HasProperty(propertyId))
                {
                    _uvPropertyId = propertyId;
                    return;
                }
            }
        }

        private void UpdateXOffset()
        {
            if (!loopXOffset || _uvPropertyId == -1)
            {
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
    }
}
