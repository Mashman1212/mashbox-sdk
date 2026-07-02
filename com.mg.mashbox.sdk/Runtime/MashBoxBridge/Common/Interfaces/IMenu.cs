using System;
using MashBoxBridge.Common.Commands;
using MashBoxBridge.Common.Sys;
using UnityEngine;
using UnityEngine.Events;

namespace MashBoxBridge.Common.Interfaces
{
    public interface IMenu
    {
        string NameID { get; }
        public void Open();
        public void Close();
        public void CommandOpen();
        public void CommandClose();
        public UnityEngine.Events.UnityEvent OnOpen { get; set; }
        public  UnityEngine.Events.UnityEvent OnClose { get; set; }

        public bool IsOpen { get; }
    }

    public class ExampleMenuClass : IMenu
    {
     
        public string NameID => _nameID;
        public void Open()
        {
            throw new NotImplementedException();
        }

        public void Close()
        {
            throw new NotImplementedException();
        }

        [SerializeField] private string _nameID;
        [SerializeField] private bool _isGameplayMenu;
        public void CommandOpen()
        {
            if (_isGameplayMenu)
            {
                MenuService.AddGameplayMenu(NameID,this);
            }
            else
            {
                MenuService.Add(NameID,this);
            }
            
            GameCommand gameCommand = new GameCommand
            {
                Command = _isGameplayMenu ? "Player" : "System",
                PlayerId = CommandSystemServiceHandler.LocalPlayerStringID,
                Action = "OpenMenu",
                Parameters = _nameID
            };
            
            Debug.Log("[Menu] CommandOpen() :" + _nameID);
            CommandSystemServiceHandler.ExecuteCommand(gameCommand);
        }
   
        public void CommandClose()
        {
            Debug.Log("[Menu] TryClose :" + this.NameID);

            if (_isGameplayMenu)
            {
                MenuService.TryCloseGameplay(this);
            }
            else
            {
                MenuService.TryClose(this);
            }
        }

        public UnityEvent OnOpen { get; set; }
        public UnityEvent OnClose { get; set; }
        public bool IsOpen => false;
    }
}
