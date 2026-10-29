using System;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [Serializable]
    public sealed class StudioLightingSettings
    {
        public bool studioLights;
        public float exposure;
        public float cameraReflectionIntensity = .35f;
        public float keyIntensity = 1.3f, fillIntensity = .3f;
        public float elevation = 40, azimuth = -30;
        public Color keyColor = Color.white, fillColor = Color.white;
        public bool shadows = true, ground;
        public float shadowStrength = .85f, groundHeight, groundSize = 20;
        public Color groundColor = new Color(.25f,.25f,.25f);
    }
}
