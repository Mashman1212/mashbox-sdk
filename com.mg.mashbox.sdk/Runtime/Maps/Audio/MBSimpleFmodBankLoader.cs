using UnityEngine;
using MashBoxSDK.Utility;

namespace MashBoxSDK.Maps.Audio
{
    [AddComponentMenu("MashBox/Maps/Audio/Simple FMOD Bank Loader")]
    [DisallowMultipleComponent]
    public class MBSimpleFmodBankLoader : MonoBehaviour
    {
        [Tooltip("FMOD bank name to load or unload when using the no-argument public methods.")]
        [SerializeField] private string bankName = string.Empty;

        [Tooltip("Load sample data when loading the configured bank.")]
        [SerializeField] private bool loadSamples = true;

        [Tooltip("Load the configured bank automatically when this component starts.")]
        [SerializeField] private bool loadOnStart = true;

        [Tooltip("Unload the configured bank automatically when this component disables.")]
        [SerializeField] private bool unloadOnDisable = true;

        public bool IsLoaded { get; private set; }

        private void Start()
        {
            if (loadOnStart)
                LoadBank();
        }

        public void LoadBank()
        {
            LoadBank(bankName);
        }

        public void LoadBank(string bankToLoad)
        {
            if (string.IsNullOrWhiteSpace(bankToLoad))
                return;

#if MGFMOD
            try
            {
                IsLoaded = MBFmodReflection.LoadBank(bankToLoad, loadSamples);
            }
            catch
            {
                // Optional FMOD integration only.
            }
#endif
        }

        public void UnloadBank()
        {
            UnloadBank(bankName);
        }

        public void UnloadBank(string bankToUnload)
        {
            if (string.IsNullOrWhiteSpace(bankToUnload))
                return;

#if MGFMOD
            try
            {
                if (MBFmodReflection.UnloadBank(bankToUnload))
                    IsLoaded = false;
            }
            catch
            {
                // Optional FMOD integration only.
            }
#else
            IsLoaded = false;
#endif
        }

        private void OnDisable()
        {
            if (unloadOnDisable)
                UnloadBank();
        }

        private void OnDestroy()
        {
            if (unloadOnDisable)
                UnloadBank();
        }
    }
}
