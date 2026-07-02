using System;

namespace MashBoxBridge.Common.Sys
{
    
    public interface IChallengeSystemService
    { 
        void ToggleChallenges(bool on);
        void ToggleChallenges();
    }
    
    public static class ChallengeSystemService
    {
        public static IChallengeSystemService Service => _service;
        private static IChallengeSystemService _service;
        public static bool ChallengesOn = true;

        public static void SetService(IChallengeSystemService service)
        {
            _service = service;
        }

        public static void ToggleChallenges()
        {
            if(_service == null)
            {
                return;
            }

            
            ChallengesOn = !ChallengesOn;
            _service.ToggleChallenges(ChallengesOn);
            RecordableEventsService.RecordEvent(()=> _service.ToggleChallenges(ChallengesOn),()=> _service.ToggleChallenges(!ChallengesOn));
        }
        public static void ToggleChallenges(bool on)
        {
            if(_service == null)
            {
                return;
            }
            
            ChallengesOn = on;

            
            RecordableEventsService.RecordEvent(()=> _service.ToggleChallenges(on),()=> _service.ToggleChallenges(!on));
            _service.ToggleChallenges(on);
        }
    }
}