using System;

namespace MashBoxBridge.Common.Interfaces
{
    public interface IPlayerTrickGameplay
    {
        Action OnComboEnded { get; set; }
        bool IsRunningCombo { get; }
        string GetCurrentAbilityTrick();
    }
}
