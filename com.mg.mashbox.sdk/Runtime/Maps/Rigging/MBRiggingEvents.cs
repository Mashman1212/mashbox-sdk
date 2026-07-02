using System;
using UnityEngine.Events;

namespace MashBoxSDK.Maps.Rigging
{
    [Serializable]
    public class MBIntEvent : UnityEvent<int>
    {
    }

    [Serializable]
    public class MBFloatEvent : UnityEvent<float>
    {
    }

    [Serializable]
    public class MBBoolEvent : UnityEvent<bool>
    {
    }
}
