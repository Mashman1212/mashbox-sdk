using UnityEngine;
using System;
using MashBoxSDK.Utility;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("MashBox/Maps/Audio/Ambiance Audio")]
    [DisallowMultipleComponent]
    public class MashBoxAmbianceAudio : MonoBehaviour
    {
        public enum AmbianceAudioPreset
        {
            Conservatory,
            DowntownDay,
            DowntownNight,
            SpillwayDay,
            SuburbsDay,
            SuburbsNight
        }

        [SerializeField] private AmbianceAudioPreset ambianceAudio = AmbianceAudioPreset.Conservatory;
        [SerializeField] private string fmodEventPathOverride = string.Empty;

        private object fmodAmbianceInstance;

        public AmbianceAudioPreset AmbianceAudio
        {
            get => ambianceAudio;
            set => ambianceAudio = value;
        }

        /// <summary>
        /// Converts enum to the key expected by AudioManager
        /// </summary>
        public string AudioKey => ambianceAudio switch
        {
            AmbianceAudioPreset.Conservatory => "Conservatory",
            AmbianceAudioPreset.DowntownDay => "Downtown Day",
            AmbianceAudioPreset.DowntownNight => "Downtown Night",
            AmbianceAudioPreset.SpillwayDay => "Spillway Day",
            AmbianceAudioPreset.SuburbsDay => "Suburbs Day",
            AmbianceAudioPreset.SuburbsNight => "Suburbs Night",
            _ => "Unknown"
        };

        public string FmodEventPath => !string.IsNullOrWhiteSpace(fmodEventPathOverride)
            ? fmodEventPathOverride
            : ambianceAudio switch
            {
                AmbianceAudioPreset.Conservatory => "event:/Environment/Ambiance/Ambiance Conservatory",
                AmbianceAudioPreset.DowntownDay => "event:/Environment/Ambiance/Ambiance Downtown Day",
                AmbianceAudioPreset.DowntownNight => "event:/Environment/Ambiance/Ambiance Downtown Night",
                AmbianceAudioPreset.SpillwayDay => "event:/Environment/Ambiance/Ambiance Spillway Day",
                AmbianceAudioPreset.SuburbsDay => "event:/Environment/Ambiance/Ambiance Suburb Day",
                AmbianceAudioPreset.SuburbsNight => "event:/Environment/Ambiance/Ambiance Suburb Night",
                _ => string.Empty
            };

        private void OnEnable()
        {
            if (MashBoxSDK.Services.AudioService.Service != null)
            {
                MashBoxSDK.Services.AudioService.PlayAmbiance(AudioKey);
                return;
            }

            PlayFmodAmbianceFallback();
        }

        private void OnDisable()
        {
            if (MashBoxSDK.Services.AudioService.Service != null)
            {
                MashBoxSDK.Services.AudioService.StopAmbiance(AudioKey);
                return;
            }

            StopFmodAmbianceFallback();
        }

        private void PlayFmodAmbianceFallback()
        {
#if MGFMOD
            try
            {
                if (fmodAmbianceInstance != null)
                    return;

                fmodAmbianceInstance = MBFmodReflection.CreateInstance(FmodEventPath);
                if (fmodAmbianceInstance == null)
                    return;

                MBFmodReflection.Start(fmodAmbianceInstance);
            }
            catch
            {
                fmodAmbianceInstance = null;
            }
#endif
        }

        private void StopFmodAmbianceFallback()
        {
#if MGFMOD
            try
            {
                if (fmodAmbianceInstance == null)
                    return;

                MBFmodReflection.Stop(fmodAmbianceInstance, immediate: true);
                MBFmodReflection.Release(fmodAmbianceInstance);
            }
            catch
            {
                // Optional FMOD fallback only.
            }
            finally
            {
                fmodAmbianceInstance = null;
            }
#endif
        }
    }
}
