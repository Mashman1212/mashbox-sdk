using System;
using UnityEngine;

namespace MashBoxSDK.Services
{
    public struct AudioGUID
    {
        public int Data1;
        public int Data2;
        public int Data3;
        public int Data4;
    }

    
    public interface IAudioService
    {
        void PlayOneShotRecorded(AudioGUID guid, GameObject sourceObj, float velocity, float surface,float rootSpeed,float volumeMult = 1.0f);
        void PlayOneShotRecorded(string path, GameObject sourceObj, float velocity, float surface,float rootSpeed,float volumeMult = 1.0f);
        void PlayOneShot(AudioGUID guid, GameObject sourceObj, float velocity,float rootSpeed,float surface);
        void PlayOneShot(string path, GameObject sourceObj, float velocity, float surface,float rootSpeed,float volumeMult = 1.0f);
        void PlayOneShot2D(string path,float volumeMult = 1.0f);
        
        void PlayAmbiance(string key);
        
        void StopAmbiance(string key);
        
        void FadeMusic(bool on);
        public void PlayMenuTabShot();
        public void PlayMenuOpenShot();

        public void PlayMenuCloseShot();

        public void PlayMenuNavigationSelectShot();

        public void PlayMenuSubmitShot();
    }
    
    public static class AudioService
    {
  
        public static IAudioService Service => _service;
        private static IAudioService _service;
        

        public static void SetService(IAudioService service)
        {
            _service = service;
        }
        public static void PlayOneShot(string path, GameObject sourceObj, float velocity, float surface, float rootSpeed, float volumeMult = 1.0f)
        {
            if (_service != null)
            {
                _service.PlayOneShot(path,sourceObj,velocity,surface,rootSpeed,volumeMult);
            }
        }
        public static void PlayOneShot(AudioGUID guid, GameObject sourceObj, float velocity, float surface, float rootSpeed)
        {
            if (_service != null)
            {
                _service.PlayOneShot(guid,sourceObj,velocity,surface,rootSpeed);
            }
        }
        public static void PlayOneShotRecorded(string path, GameObject sourceObj, float velocity, float surface, float rootSpeed , float volumeMult = 1.0f)
        {
            if (_service != null)
            {
                _service.PlayOneShotRecorded(path,sourceObj,velocity,surface,rootSpeed,volumeMult);
            }
        }
        public static void PlayOneShotRecorded(AudioGUID guid, GameObject sourceObj, float velocity, float surface, float rootSpeed)
        {
            if (_service != null)
            {
                _service.PlayOneShotRecorded(guid,sourceObj,velocity,surface,rootSpeed);
            }
        }

        public static void PlayOneShot2D(string path,float volumeMult = 1.0f)
        {
            if (_service != null)
            {
                _service.PlayOneShot2D(path,volumeMult);
            }
        }
        
        public static void FadeMusic(bool on)
        {
            if (_service != null)
            {
                _service.FadeMusic(on);
            }
        }
        
        public static void PlayAmbiance(string key)
        {
            _service?.PlayAmbiance(key);
        }

        public static void StopAmbiance(string key)
        {
            _service?.StopAmbiance(key);
        }
        
    }
}