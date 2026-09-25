using UnityEngine;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("MashBox/Maps/Line Challenge")]
    public sealed class MBLineChallenge : MBTrickChallenge
    {
        public override bool IsLine => true;
    }
}
