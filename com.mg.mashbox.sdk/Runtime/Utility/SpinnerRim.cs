using System;
using System.Collections;
using UnityEngine;

namespace MashBoxSDK.Utility
{
    public class SpinnerRim : MonoBehaviour
    {
        private Rigidbody _rootBody;
        private Transform _rotatorRootBone;
        
        float spinMultiplier = 100f;
        float damping = 0.1f;
        float maxSpinSpeed = 2000f;

        float idleSpinSpeed = 50f;

        private float currentSpinSpeed = 0f;
        private float spinnerAngle = 0f;
        private float forwardSpeed;
        private Quaternion parentRotation;

        private float targetSpin;
        IEnumerator Start()
        {
            yield return new WaitForSeconds(0.25f);
            Init();
        }

        private void OnEnable()
        {
            Init();
        }

        void Init()
        {
            parentRotation = Quaternion.identity;
            
            _rootBody = GetComponentInParent<Rigidbody>(true);
            _rotatorRootBone = FindParentByName(transform, "Rotator Bone");

//            if (_rotatorRootBone == null)
//                Debug.LogError("SpinnerRim: 'Rotator Bone' not found in parents!", this);
//
            //if (_rootBody == null)
            //    Debug.LogError("SpinnerRim: Rigidbody not found in parents!", this);
        }
        

        void LateUpdate()
        {
            if(_rootBody)
                #if UNITY_6000_0_OR_NEWER
                forwardSpeed = Vector3.Dot(_rootBody.linearVelocity, _rootBody.transform.forward);
#else
                forwardSpeed = Vector3.Dot(_rootBody.velocity, _rootBody.transform.forward);
#endif
            
            
            targetSpin += forwardSpeed * spinMultiplier * Time.deltaTime;

  
            targetSpin = Mathf.Clamp(targetSpin, -maxSpinSpeed, maxSpinSpeed);
            

            #if UNITY_6000_0_OR_NEWER
            if (_rootBody && _rootBody.linearVelocity.sqrMagnitude < 1.0f)
#else
            if (_rootBody && _rootBody.velocity.sqrMagnitude < 1.0f)
#endif
            {
                targetSpin = idleSpinSpeed;
            }
            
            currentSpinSpeed = Mathf.Lerp(currentSpinSpeed, targetSpin, damping * Time.deltaTime);

 

            spinnerAngle += currentSpinSpeed * Time.deltaTime;


            if(_rotatorRootBone)
                parentRotation = _rotatorRootBone.localRotation;
            
            Quaternion inverseParent = Quaternion.Inverse(parentRotation);


            Quaternion spinRotation = Quaternion.AngleAxis(spinnerAngle, Vector3.right);

            // --- 6. Combine ---
            transform.localRotation = inverseParent * spinRotation;
        }

        private Transform FindParentByName(Transform current, string name)
        {
            while (current != null)
            {
                if (current.name == name)
                    return current;

                current = current.parent;
            }
            return null;
        }
    }
}