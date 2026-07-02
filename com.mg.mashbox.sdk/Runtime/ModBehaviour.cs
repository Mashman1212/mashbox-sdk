using UnityEngine;
using UnityEngine.Scripting;

namespace MashBoxSDK
{
    [Preserve]
    public abstract class ModBehaviour : ScriptableObject
    {
        public abstract string ModId { get; }
        public virtual void OnLoad() {}
        public virtual void OnUnload() {}
    }
}