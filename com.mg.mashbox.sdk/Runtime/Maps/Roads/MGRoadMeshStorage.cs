using UnityEngine.Scripting.APIUpdating;
using UnityEngine;

namespace MashBoxSDK.Maps.Roads
{
    [AddComponentMenu("")]
    [MovedFrom(true, "MappyX.Roads", "Assembly-CSharp", null)]
    public sealed class MGRoadMeshStorage : MonoBehaviour
    {
        [HideInInspector] public Mesh mesh;
        [HideInInspector] public string owner;
    }
}
