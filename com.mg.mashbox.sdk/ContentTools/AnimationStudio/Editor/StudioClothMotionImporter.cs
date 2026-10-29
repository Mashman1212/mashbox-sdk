using UnityEditor;

namespace MashBoxSDK.AnimationStudio
{
    // This bundled motion must be humanoid so it retargets to the preview body.
    internal sealed class StudioClothMotionImporter : AssetPostprocessor
    {
        private bool IsClothMotion => assetPath == "Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Animations/Gangnam Style.fbx";

        private void OnPreprocessModel()
        {
            if (!IsClothMotion) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsClothMotion) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips) { clip.name = "Gangnam Style"; clip.loopTime = true; }
            importer.clipAnimations = clips;
        }
    }
}
