using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    namespace MashBoxBridge.Common.Interfaces
    {
        // This is implemented by MapData ScriptableObject on the game side.
        public interface IMapData : IBundleURL, IScenePath
        {
            string MapName { get; }
            ulong Uid { get; }
            Object DataObj { get; }
        }
    }

}
