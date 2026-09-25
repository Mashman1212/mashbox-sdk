using UnityEngine;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("MashBox/Maps/Spot Challenge")]
    public sealed class MBSpotChallenge : MBTrickChallenge
    {
        public override bool IsLine => false;
    }
}
