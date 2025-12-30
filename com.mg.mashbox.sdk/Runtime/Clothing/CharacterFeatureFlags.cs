using System;
using UnityEngine.Scripting;

namespace MashBoxSDK.Clothing
{
    [Preserve]
    [Flags]
    public enum CharacterFeatureFlags
    {
        None        = 0,
        Eyes        = 1 << 0,
        Hair        = 1 << 1,
        Beard       = 1 << 2,
    }
}