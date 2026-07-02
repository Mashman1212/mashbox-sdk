using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Utility
{
    public class TriggerEvents : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Events.UnityEvent OnTriggerEnterEvent;
        [SerializeField] private UnityEngine.Events.UnityEvent OnTriggerExitEvent;
        private void OnTriggerEnter(Collider other)
        {
            OnTriggerEnterEvent?.Invoke();//
        }

        private void OnTriggerExit(Collider other)
        {
            OnTriggerExitEvent?.Invoke();
        }
    }
}