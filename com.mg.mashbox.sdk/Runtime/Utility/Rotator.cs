using UnityEngine;

namespace MashBoxSDK.Utility
{
    [AddComponentMenu("MashBox/Utility/Constant Force Targeted")]
    public class Rotator : MonoBehaviour
    {
        [SerializeField] private Vector3 _axis = Vector3.up;
        [SerializeField] float _rotateSpeed = 500.0f;
   
        void Update()
        {
            this.transform.Rotate(_axis,_rotateSpeed * Time.deltaTime,Space.Self);
        }
    }
}
