using UnityEngine;

namespace MashBoxSDK.ContentUtility
{
    [AddComponentMenu("MashBox/Content Utility/Simple Material Panner")]
    public class MBSimpleMaterialPanner : MonoBehaviour
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private string textureProperty = "_MainTex";
        [SerializeField] private Vector2 speed = new Vector2(0.1f, 0f);
        [SerializeField] private Vector2 startOffset = Vector2.zero;
        [SerializeField] private bool useSharedMaterial;
        [SerializeField] private bool useUnscaledTime;

        private Material runtimeMaterial;
        private int texturePropertyId;

        private void Awake()
        {
            ResolveRendererAndMaterial();
        }

        private void OnEnable()
        {
            ResolveRendererAndMaterial();
        }

        private void Update()
        {
            if (runtimeMaterial == null || !runtimeMaterial.HasProperty(texturePropertyId))
                return;

            var time = useUnscaledTime ? Time.unscaledTime : Time.time;
            var offset = startOffset + (speed * time);
            runtimeMaterial.SetTextureOffset(texturePropertyId, offset);
        }

        private void ResolveRendererAndMaterial()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();

            texturePropertyId = Shader.PropertyToID(textureProperty);

            if (targetRenderer == null)
            {
                runtimeMaterial = null;
                return;
            }

            runtimeMaterial = useSharedMaterial ? targetRenderer.sharedMaterial : targetRenderer.material;
        }
    }
}
