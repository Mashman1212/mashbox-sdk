

using System;

namespace MashBoxBridge.Common.Sys
{
    public interface IUserSession
    {
        string PlayFabId { get; }
        string DisplayName { get; }
        int MK { get; }
        bool IsLoggedIn { get; }
    }
    
    public interface IAppEvents
    {
        event Action               OnLoginBegin;
        event Action<IUserSession> OnLoginSucceeded;
        event Action<string>       OnLoginFailed;
        event Action               OnUserSignedOut;
        event Action<IUserSession> OnUserChanged;
        
        void RaiseLoginBegin();
        void RaiseLoginSucceeded(IUserSession s);
        void RaiseLoginFailed(string msg);
        void RaiseUserSignedOut();
        void RaiseUserChanged(IUserSession s);
        
    }

    public static class AppEventsService
    {
  
        public static IAppEvents Service => _service;
        private static IAppEvents _service;

        public static Action OnCloseSettingsMenu;

        public static Action OnJoinedMultiplayerSession;
        
        public static void SetService(IAppEvents service)
        {
            _service = service;
        }
    }
}