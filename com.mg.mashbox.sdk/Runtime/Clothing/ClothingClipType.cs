using UnityEngine.Scripting;

namespace MashBoxSDK.Clothing
{
    [Preserve]
    public enum ClothingClipType
    {
        None = 0,

        Shorts,
        ShortShorts,
        LongPants,

        ShortSleeve,
        LongSleeve,
        TankTop,
        
        Shoes,
        
        Body,
        Gloves,
        AnkleSocks,
        TallSocks,
        
        OpenFlannel,
        ClothShortSleeve,

        // Append new values so existing prefab preset numbers stay stable.
        Custom = 14
    }
}
