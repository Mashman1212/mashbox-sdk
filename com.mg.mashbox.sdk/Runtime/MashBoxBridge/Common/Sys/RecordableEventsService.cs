using System;

namespace MashBoxBridge.Common.Sys
{
    
    public interface IRecordableEventsService
    { 
        void RecordEvent(Action forwardAction, Action reverseAction);
    }
    
    public static class RecordableEventsService
    {
        public static IRecordableEventsService Service => _service;
        private static IRecordableEventsService _service;
        

        public static void SetService(IRecordableEventsService service)
        {
            _service = service;
        }

        public static void RecordEvent(Action forwardAction, Action reverseAction)
        {
            if(_service == null)
            {
                return;
            }
            _service.RecordEvent(forwardAction,reverseAction);
        }
    }
}