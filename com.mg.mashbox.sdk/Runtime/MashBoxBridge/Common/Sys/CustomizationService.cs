using UnityEngine;

namespace MashBoxBridge.Common.Sys
{
    public interface ICustomizationService
    {
        public void SetSelectedEntityRoot(Transform entityRoot);
        public void NullifySelectedEntityRoot();
    }

    public static class CustomizationService
    {
        private static ICustomizationService _service;
            
        public static void SetService(ICustomizationService service)
        {
            _service = service;
        }
        public static void SetSelectedEntityRoot(Transform entityRoot)
        {
            if (_service != null)
            {
                _service.SetSelectedEntityRoot(entityRoot);
            }
        }
        public static void NullifySelectedEntityRoot()
        {
            if (_service != null)
            {
                _service.NullifySelectedEntityRoot();
            }
        }
    }
}