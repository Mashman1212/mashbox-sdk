#if UNITY_EDITOR

using Content_Icon_Capture.Editor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ContentTools.PhotoBooth.Editor
{
    public static class PhotoBoothUtility
    {
        public static void Capture(string path, Camera cam)
        {
            var backdrop = FindBackdropSphere(cam);
            var previousActiveState = backdrop != null && backdrop.activeSelf;

            try
            {
                if (backdrop != null)
                    backdrop.SetActive(false);

                ContentIconCaptureUtility.CaptureAndSaveImage(
                    path,
                    cam,
                    2560,
                    1440,
                    ContentIconCaptureUtility.ImageType.PNG
                );
            }
            finally
            {
                if (backdrop != null)
                    backdrop.SetActive(previousActiveState);
            }
        }

        private static GameObject FindBackdropSphere(Camera cam)
        {
            if (cam == null)
                return null;

            var scene = cam.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                return GameObject.Find("BackDropSphere");

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                var transforms = rootObject.GetComponentsInChildren<Transform>(true);
                foreach (var child in transforms)
                {
                    if (child.name == "BackDropSphere")
                        return child.gameObject;
                }
            }

            return null;
        }
    }
}

#endif
