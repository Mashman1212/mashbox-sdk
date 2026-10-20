using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // A typed root preserves links between exceptional embedded materials/textures.
    internal sealed class StudioWorkspaceResources : ScriptableObject
    {
        public Material[] materials;
    }
}
