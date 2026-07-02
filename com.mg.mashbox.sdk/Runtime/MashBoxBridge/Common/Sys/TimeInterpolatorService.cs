using System;
using MashBoxBridge.Common.Interfaces;

namespace MashBoxBridge.Common.Sys
{
    
    public static class TimeInterpolatorService
    {
    
        public static ITimeInterpolator Service => _service;
        private static ITimeInterpolator _service;
        

        public static void SetService(ITimeInterpolator service)
        {
            _service = service;
        }
        public static void SetNormalTime()
        {
            if (_service != null)
            {
                _service.SetTimeNormal();
            }
        }
        public static void InterpolateToNormal()
        {
            if (_service != null)
            {
                _service.InterpolateNormal();
            }
        }
        public static void SetSlowMo()
        {
            if (_service != null)
            {
                _service.SetSlowMo();
            }
        }
    }
}