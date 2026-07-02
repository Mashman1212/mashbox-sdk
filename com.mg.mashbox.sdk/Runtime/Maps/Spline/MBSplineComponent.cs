using UnityEngine;
using UnityEngine.Splines;

namespace MashBoxSDK.Maps.Spline
{
    [ExecuteAlways]
    public class MBSplineComponent : MonoBehaviour
    {
        public SplineContainer container;

        void OnValidate()
        {
            EnsureContainer();
        }

        void Reset()
        {
            EnsureContainer();
        }

        void EnsureContainer()
        {
            if (container == null)
            {
                container = GetComponent<SplineContainer>();

                if (container == null)
                {
                    container = gameObject.AddComponent<SplineContainer>();
                }
            }
        }
    }
}