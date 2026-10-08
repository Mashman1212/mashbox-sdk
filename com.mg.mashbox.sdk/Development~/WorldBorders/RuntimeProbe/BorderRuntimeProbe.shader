Shader "Hidden/BorderRuntimeProbe"
{
    Properties
    {
        _ALBEDO ("Albedo", 2D) = "white" {}
        _VISIBILITY_DISTANCE ("Distance", Float) = 20
        _FADE_WIDTH ("Fade", Float) = 10
        _BORDER_OPACITY ("Opacity", Float) = 0.25
        _COLOUR_OVERLAY ("Tint", Color) = (1,1,1,1)
        _TILING ("Tiling", Vector) = (1,1,0,0)
        _OFFSET ("Offset", Vector) = (0,0,0,0)
        _DoubleSidedEnable ("Double sided", Float) = 1
        _TransparentCullMode ("Transparent cull", Float) = 0
        _CullMode ("Cull", Float) = 0
        _CullModeForward ("Forward cull", Float) = 0
        _DoubleSidedConstants ("Double sided constants", Vector) = (1,1,-1,0)
    }
    SubShader { Pass {} }
}
