using System;
using MashBoxBridge.Common.Interfaces;
using UnityEngine;

namespace MashBoxBridge.Common.Commands
{
    public interface ICommandSystemService
    {
        event Action<string> OnCommandExecuted;
        public void ExecuteCommandString(string command);
        public void ExecuteCommand(GameCommand command);
        public void BlockUndoForFrame();
        public string LocalPlayerStringID { get; }
        public void Undo();
        public void UndoGameplayStack();
    }
    
    public static class CommandSystemServiceHandler
    {
        private static ICommandSystemService _commandSystemService;
        public static ICommandSystemService CommandSystemService => _commandSystemService;

        public static Action<string> OnCommandExecuted;
        public static Action<ICharacterManager> OnLocalPlayerSet;
        public static void SetService(ICommandSystemService commandSystemService)
        {
            _commandSystemService = commandSystemService;
            
            if (_commandSystemService != null)
                _commandSystemService.OnCommandExecuted += CommandExecuted;
        }
        
        private static void CommandExecuted(string command)
        {
            OnCommandExecuted?.Invoke(command);
        }
       
        public static void ExecuteCommandString(string command)
        {
            if (_commandSystemService == null)
            {
                return;
            }
            
            _commandSystemService.ExecuteCommandString(command);
        }
        public static void Undo()
        {
            if (_commandSystemService == null)
            {
                return;
            }
            
            _commandSystemService.Undo();
        }
        
        public static void UndoGameplayStack()
        {
            if (_commandSystemService == null)
            {
                return;
            }
            
            _commandSystemService.UndoGameplayStack();
        }

        public static void ExecuteCommand(GameCommand command)
        {
            if (_commandSystemService == null)
            {
                return;
            }


            Debug.Log($"[CommandSystemServiceHandler] ExecuteCommand : {command.Command},PlayerID {command.PlayerId} , Params : {command.Parameters}");
            _commandSystemService.ExecuteCommand(command);

        }
        


        public static string LocalPlayerStringID => _commandSystemService != null ? _commandSystemService.LocalPlayerStringID : "null";


        private static ICharacterData _localCustomPlayerCharacterData;

        public static void SetLocalCustomPlayerCharacterData(ICharacterData characterData)
        {
            if (characterData != null)
            {
          
                _localCustomPlayerCharacterData = characterData;
            }
               
        }
        
        public static ICharacterData LocalCustomPlayerCharacterData => _localCustomPlayerCharacterData;
        public static ICharacterManager LocalCharacterManager => _localCharacterManager;
        
        private static ICharacterManager _localCharacterManager;
        public static void SetLocalCharacterManager(ICharacterManager characterManager)
        {
            if (characterManager != null)
            {
                _localCharacterManager = characterManager;
                OnLocalPlayerSet?.Invoke(_localCharacterManager);
            }
            
        }
        
        private static IPlayerTrickGameplay _playerTrickGameplay;
        public static IPlayerTrickGameplay LocalPlayerTrickGameplay => _playerTrickGameplay;
        private static void HandleComboEnded()
        {
            OnLocalPlayerComboEnded?.Invoke();
        }
        public static void HandleLocalPlayerRespawned()
        {
            OnLocalPlayerRespawned?.Invoke();
        }
        public static void HandleLocalPlayerKilled()
        {
            OnLocalPlayerKilled?.Invoke();
        }
        public static void SetLocalPlayerTrickGameplay(IPlayerTrickGameplay playerTrickGameplay)
        {
            if (playerTrickGameplay != null)
            {
                _playerTrickGameplay = playerTrickGameplay;
                _playerTrickGameplay.OnComboEnded += HandleComboEnded;
            }
        }
        public static Action OnLocalPlayerComboEnded;
        public static Action OnLocalPlayerRespawned;
        public static Action OnLocalPlayerKilled;
    }
}
