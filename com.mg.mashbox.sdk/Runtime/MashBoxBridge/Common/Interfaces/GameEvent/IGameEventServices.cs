namespace MashBoxBridge.Common.Interfaces.GameEvent
{
    public interface IGameEventService
    {
        IGameEvent LookUpGameEvent(string key);
    }

    
}