using UnityEngine;

namespace MashBoxSDK.Maps
{
    [DisallowMultipleComponent]
    public class MBSideHitFlagVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Renderer[] coloredRenderers;
        [SerializeField, ColorUsage(false, true)] private Color orangeColor = new Color(1f, 0.48f, 0.06f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color blueColor = new Color(0.08f, 0.46f, 1f, 1f);

        private MaterialPropertyBlock propertyBlock;

        public Color OrangeColor => orangeColor;
        public Color BlueColor => blueColor;

        public void ApplyColor(MBSideHit.FlagVisualColor flagColor)
        {
            ApplyColor(flagColor == MBSideHit.FlagVisualColor.Blue ? blueColor : orangeColor);
        }

        public void ApplyColor(Color color)
        {
            if (coloredRenderers == null || coloredRenderers.Length == 0)
                return;

            propertyBlock ??= new MaterialPropertyBlock();
            for (int i = 0; i < coloredRenderers.Length; i++)
            {
                Renderer renderer = coloredRenderers[i];
                if (!renderer)
                    continue;

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, color);
                propertyBlock.SetColor(ColorId, color);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
