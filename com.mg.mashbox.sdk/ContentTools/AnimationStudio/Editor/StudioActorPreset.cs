using System.Collections.Generic;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed class StudioActorPreset : ScriptableObject
    {
        public GameObject character, body;
        public GameObject[] clothing;
        public OutfitOptions[] outfitOptions;
        public List<StudioVehiclePart> vehicleParts = new List<StudioVehiclePart>();
        public List<StudioHandleOffset> handleOffsets = new List<StudioHandleOffset>();
    }
}
