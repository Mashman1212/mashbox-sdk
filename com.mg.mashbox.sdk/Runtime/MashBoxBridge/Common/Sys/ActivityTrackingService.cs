
using UnityEngine;

public interface IActivityTracker
{
    public void RecordActivity(string verb, string preposition, string adjective, bool isSportsTrick);
}


public static class  ActivityTrackingService
{
    private static IActivityTracker _service;

    
    public static void SetService(IActivityTracker service)
    {
        _service = service;
    }

    public static void RecordActivity(string verb, string preposition, string adjective, bool isSportsTrick)
    {
        if (_service == null)
        {
            Debug.LogError("No Activity Service Set");
            return;
        }
        _service.RecordActivity(verb, preposition, adjective,isSportsTrick);
        
    }

}
