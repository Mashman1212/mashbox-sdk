using MashBoxBridge.Common.Commands;
using MashBoxBridge.CustomAttributes;
using UnityEngine;

namespace MashBoxBridge.Common.Sys
{
    public class UnlockAchievementBehaviour : MonoBehaviour
    {
        [SerializeField] private string _achievementID;

        [InspectorButton]
        public void Unlock()
        {
            GameCommand unlockCommand = new GameCommand
            {
                Command = "System",
                PlayerId = "-1",
                Action = "Unlock Achievement",
                Parameters = _achievementID
            };

            CommandSystemServiceHandler.ExecuteCommand(unlockCommand); 
        }
    }
}
