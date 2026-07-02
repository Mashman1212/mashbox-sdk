using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MashBox.Editor.Maps
{
    public abstract class HDRPSunFlareGenerator
    {
        //[MenuItem("MashBox/Map Tools/Rendering/Create Sun Flare Data")]
        static void Generate()
        {
            var flare = ScriptableObject.CreateInstance<LensFlareDataSRP>();

            flare.elements = new LensFlareDataElementSRP[3];

            // -----------------------
            // SUN HALO
            // -----------------------

            var halo = new LensFlareDataElementSRP();
            halo.flareType = SRPLensFlareType.Circle;
            halo.position = 0;
            halo.uniformScale = 0.35f;
            halo.localIntensity = 3f;
            halo.tint = new Color(1f, 0.95f, 0.8f, 0.6f);
            halo.blendMode = SRPLensFlareBlendMode.Additive;
            halo.fallOff = 0.4f;

            flare.elements[0] = halo;

            // -----------------------
            // GHOST CHAIN
            // -----------------------

            var ghosts = new LensFlareDataElementSRP();
            ghosts.flareType = SRPLensFlareType.Circle;
            ghosts.allowMultipleElement = true;
            ghosts.count = 6;

            ghosts.position = 0.3f;
            ghosts.lengthSpread = 1.2f;

            ghosts.uniformScale = 0.1f;
            ghosts.localIntensity = 1.2f;

            ghosts.tint = new Color(0.9f, 0.8f, 1f, 0.5f);

            ghosts.distribution = SRPLensFlareDistribution.Uniform;

            ghosts.positionCurve = new AnimationCurve(
                new Keyframe(0,0),
                new Keyframe(1,1)
            );

            ghosts.scaleCurve = new AnimationCurve(
                new Keyframe(0,0.6f),
                new Keyframe(1,0.2f)
            );

            ghosts.blendMode = SRPLensFlareBlendMode.Additive;

            flare.elements[1] = ghosts;

            // -----------------------
            // ANAMORPHIC STREAK
            // -----------------------

            var streak = new LensFlareDataElementSRP();

            streak.flareType = SRPLensFlareType.Polygon;
            streak.sideCount = 6;
            streak.sdfRoundness = 1;

            streak.uniformScale = 0.5f;
            streak.localIntensity = 0.8f;

            streak.tint = new Color(0.6f,0.8f,1f,0.4f);

            streak.position = 0;
            streak.angularOffset = 0;

            streak.blendMode = SRPLensFlareBlendMode.Screen;

            flare.elements[2] = streak;

            Scene scene = SceneManager.GetActiveScene();

            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("Scene must be saved before generating a flare.");
                return;
            }

// Scene path example:
// Assets/Scenes/MyScene.unity

            string sceneDirectory = Path.GetDirectoryName(scene.path);

// Build asset path
            string assetPath = Path.Combine(sceneDirectory, "HDRP_SunFlare.asset");
            assetPath = assetPath.Replace("\\", "/");

// Ensure unique name
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(flare, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = flare;

            Debug.Log($"Sun flare created at: {assetPath}");
        }
    }
}