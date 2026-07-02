using UnityEngine;

namespace MashBoxSDK.Maps
{
    public class MBMapBoundary : MonoBehaviour
    {
        private void Reset()
        {
            if (gameObject.name != "Map Boundary")
                gameObject.name = "Map Boundary";
        }

        private void OnValidate()
        {
            if (gameObject.name != "Map Boundary")
                gameObject.name = "Map Boundary";
        }
    }
}
