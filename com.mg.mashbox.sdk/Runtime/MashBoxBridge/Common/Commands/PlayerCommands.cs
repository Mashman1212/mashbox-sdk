using MashBoxBridge.Common.Interfaces;
using MashBoxBridge.Common.Interfaces.GameEvent;
using MashBoxBridge.Common.Sys;
using UnityEngine;

namespace MashBoxBridge.Common.Commands
{
    public class OpenMenuCommand : CommandBase
    {
        private readonly string _menuName;
        public OpenMenuCommand(string menuName, string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "OpenMenuCommand";
            _menuName = menuName;
        }

        public override void Execute()
        {
            MenuService.OpenMenu(_menuName);
        }

        public override void Undo()
        {
            if(MenuService.StackCount > 1 && !MenuService.BlockUndo)//dont close the last standing menu
                MenuService.CloseMenu();
        }
        
        public override bool HasUndo => true;
    }
    
    public class OpenGameplayMenuCommand : CommandBase
    {
        private readonly string _menuName;
        public OpenGameplayMenuCommand(string menuName, string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "OpenGameplayMenuCommand";
            _menuName = menuName;
        }

        public override void Execute()
        {
            MenuService.OpenMenuGameplay(_menuName);
        }

        public override void Undo()
        {
            MenuService.CloseMenuGameplay();
        }
        
        public override bool HasUndo => true;
    }
    
    public class UnlockAchievementCommand : CommandBase
    {
        private string _achievementID;
        public UnlockAchievementCommand(string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "UnlockAchievementCommand";
            _achievementID = parameters;
        }

        public override void Execute()
        {
            PlatformManagerService.UnlockAchievement(_achievementID);
        }

        public override void Undo()
        {
        }

        public override bool HasUndo => false;
    }
    
    public class PauseCommand : CommandBase
    {
        public PauseCommand(string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "PauseCommand";
        }

        public override void Execute()
        {
            GameEventsManager.InvokeEvent("GameEvent_TitleLoop_TransitionTrigger_Pause");
        }

        public override void Undo()
        {
            //GameEventsManager.InvokeEvent("GameEvent_TitleLoop_TransitionTrigger_Pause");
        }
        
        public override bool HasUndo => false;
    }
    
    
    
    public interface IDroneStateController
    {
        void RequestDeployDrone();
        void RequestDisableDrone();
    }
    public class DeployDroneCommand : CommandBase
    {
        private readonly IDroneStateController _droneState;

        public DeployDroneCommand(IDroneStateController droneState, string actionType, string parameters = null)
            : base(actionType, parameters)
        {
            _actionName = "Deploy Drone";
            _droneState = droneState;
        }

        public override void Execute()
        {
            Debug.Log("[DeployDroneCommand] Execute → RequestDeploy");
            _droneState?.RequestDeployDrone();
        }

        public override void Undo()
        {
            Debug.Log("[DeployDroneCommand] Undo → RequestDisable");
            _droneState?.RequestDisableDrone();
        }

        public override bool HasUndo => true;
    }

    public class PlaceMarkerCommand : CommandBase
    {
        private readonly ISessionMarker _sessionMarker;
        public PlaceMarkerCommand(ISessionMarker sessionMarker, string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "PlaceMarkerCommand";
            _sessionMarker = sessionMarker;
        }

        public override void Execute()
        {
            if(_sessionMarker != null)
                _sessionMarker.Place();
        }

        public override void Undo()
        {
            Debug.Log("[PlaceMarkerCommand] Undo");
        }
        
        public override bool HasUndo => false;
    }
    public class RespawnCommand : CommandBase
    {
        private readonly ISessionMarker _sessionMarker;
        public RespawnCommand(ISessionMarker sessionMarker, string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "RespawnCommand";
            _sessionMarker = sessionMarker;
        }

        public override void Execute()
        {
            if(_sessionMarker != null)
                _sessionMarker.Respawn();
        }

        public override void Undo()
        {
            Debug.Log("[RespawnCommand] Undo");
        }
        
        public override bool HasUndo => false;
    }
    
    public class PlayerEmoteCommand : CommandBase
    {
        private readonly IPlayerEmote _playerEmote;
        public PlayerEmoteCommand(IPlayerEmote playerEmote, string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "PlayerEmoteCommand";
            _playerEmote = playerEmote;
        }

        public override void Execute()
        {
            if(_playerEmote != null)
                _playerEmote.Play(int.Parse(_parameters));
        }

        public override void Undo()
        {
            //if(_sessionMarker != null)
            //    _sessionMarker.DisableDrone();
        }
        
        public override bool HasUndo => false;
    }
    
    public class ResetDynamicsCommand : CommandBase
    {
        public ResetDynamicsCommand(string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "ResetDynamicsCommand";
        }

        public override void Execute()
        {
            GameLoopService.ResetDynamics();
        }

        public override void Undo()
        {
            //GameEventsManager.InvokeEvent("GameEvent_TitleLoop_TransitionTrigger_Pause");
        }
        
        public override bool HasUndo => false;
    }
    public class SlowMotionCommand : CommandBase
    {
        private bool _on;
        public SlowMotionCommand(string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "SlowMotionCommand";   
            if (!string.IsNullOrEmpty(parameters) && parameters.ToLowerInvariant() == "on")
            {
                _on = true;
            }
        }

        public override void Execute()
        {
            if (_on)
            {
                TimeInterpolatorService.SetSlowMo();
            }
            else
            {
                TimeInterpolatorService.InterpolateToNormal();
            }
            
        }

        public override void Undo()
        {
        }
        
        public override bool HasUndo => false;
    }
    public class StandardTimeSpeedCommand : CommandBase
    {
        private readonly ITimeInterpolator _timeInterpolator;
        public StandardTimeSpeedCommand(ITimeInterpolator timeInterpolator,string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "StandardTimeSpeedCommand";   
            _timeInterpolator = timeInterpolator;
        }

        public override void Execute()
        {
            _timeInterpolator.SetTimeNormal();
        }

        public override void Undo()
        {
        }
        
        public override bool HasUndo => false;
    }
    
    public class ToggleChallengesCommand : CommandBase
    {
        //private bool _on;
        public ToggleChallengesCommand(string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "ToggleChallengesCommand";   
            //if (!string.IsNullOrEmpty(parameters) && parameters.ToLowerInvariant() == "on")
            //{
            //    _on = true;
            //}
        }

        public override void Execute()
        {
            ChallengeSystemService.ToggleChallenges();
        }

        public override void Undo()
        {
        }
        
        public override bool HasUndo => false;
    }
    
    public class MusicFaderCommand : CommandBase
    {
        private bool _on;
        public MusicFaderCommand(string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "MusicFaderCommand";
            
            if (!string.IsNullOrEmpty(parameters) && parameters.ToLowerInvariant() == "on")
            {
                _on = true;
            }
        }

        public override void Execute()
        {
            if (_on)
            {
                MashBoxSDK.Services.AudioService.FadeMusic(true);
            }
            else
            {
                MashBoxSDK.Services.AudioService.FadeMusic(false);
            }
            
        }

        public override void Undo()
        {
        }
        
        public override bool HasUndo => false;
    }
    
    public class LoadMapCommand : CommandBase
    {
        public LoadMapCommand(string actionType, string parameters = null) : base(actionType, parameters)
        {
            _actionName = "LoadMapCommand";
        }

        public override void Execute()
        {
            if (string.IsNullOrWhiteSpace(_parameters))
            {
                Debug.LogError("[LoadMapCommand] Map name parameter is empty.");
                return;
            }

            if (!MapService.TryLoadMap(_parameters))
            {
                Debug.LogError($"[LoadMapCommand] Failed to load map '{_parameters}'.");
            }
        }

        public override void Undo()
        {
            // No-op for now, unless you want a "return to previous map" system.
        }

        public override bool HasUndo => false;
    }
    
}