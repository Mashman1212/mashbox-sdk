using System;
using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    /// <summary>
    /// An interface object that facilitates assignment and retrieval of an interface instance from a Unity object.
    /// </summary>
    /// <typeparam name="T">The interface type to be assigned</typeparam>
    [Serializable]
    public class InterfaceObj<T> : ISerializationCallbackReceiver where T : class
    {
        /// <summary>
        /// An interface object that facilitates assignment and retrieval of an interface instance from a Unity object.
        /// </summary>
        /// <typeparam name="T">The interface type to be assigned</typeparam>
        [SerializeField] 
        private UnityEngine.Object unityObject;
        // Private set is used to restrict direct modification from outside the class.
        /// <summary>
        /// An interface object that facilitates assignment and retrieval of an interface instance from a Unity object.
        /// </summary>
        /// <typeparam name="T">The interface type to be assigned</typeparam>
        public T Interface { get;  private set; }

        /// <summary>
        /// An interface object that facilitates assignment and retrieval of an interface instance from a Unity object.
        /// </summary>
        /// <typeparam name="T">The interface type to be assigned</typeparam>
        public InterfaceObj()
        {
            if (!typeof(T).IsInterface)
            {
                throw new ArgumentException($"T must be an interface type. Current type is {typeof(T)}");
            }
        }

        /// <summary>
        /// This method is invoked after deserialization to assign the interface instance to the Unity object.
        /// </summary>
        /// <remarks>
        /// This method is called automatically by Unity after an object has been deserialized. It should not be called manually.
        /// </remarks>
        public void OnAfterDeserialize()
        {   
            //AssignInterface();
        }

        /// <summary>
        /// This method is called before the object is serialized.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is part of the Unity ISerializationCallbackReceiver interface.
        /// It is automatically called by Unity when the object is about to be serialized.
        /// </para>
        /// <para>
        /// This method has no parameters.
        /// </para>
        /// <para>
        /// This method does not return any value.
        /// </para>
        /// <para>
        /// This method is intended to be overridden by derived classes to perform custom logic before serialization.
        /// </para>
        /// <para>
        /// In the provided code, this method is empty and does not contain any logic.
        /// </para>
        /// </remarks>
        public void OnBeforeSerialize()
        {
            AssignInterface();
        }

        /// <summary>
        /// An interface object that facilitates assignment and retrieval of an interface instance from a Unity object.
        /// </summary>
        /// <typeparam name="T">The interface type to be assigned</typeparam>
        public UnityEngine.Object UnityObject 
        {
            get { return unityObject; }
            set
            {
                if (value is T)
                {
                    unityObject = value;
                    AssignInterface();
                } 
                else
                {
                    throw new ArgumentException($"value must be of type T. Current type is {value.GetType()}");
                }
            }
        }

        public void SetObjectAsGameObject(GameObject go)
        {
            unityObject = go;
        }

        /// <summary>
        /// Assigns the interface instance from the assigned Unity object.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the assigned Unity object is not of type T.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the assigned Unity object is null or not of type UnityEngine.</exception>
        public void AssignInterface()
        {
            if (unityObject != null && unityObject is T o)
            {
                Interface = o;
            }
            else if (unityObject != null && unityObject is GameObject gameObject)
            {
                Interface = gameObject.GetComponent<T>();

                if (Interface == null)
                {
                    unityObject = null;
                    Interface = null;
                }
            }
            else
            {
                unityObject = null;
                Interface = null;
                //throw new InvalidOperationException($"unityObject must be a non-null instance of UnityEngine. Current type is {unityObject?.GetType()}. T is of type {typeof(T).Name}");
            }
        }

        public static bool Exists(InterfaceObj<T> interfaceObject)
        {
            return interfaceObject != null && interfaceObject.Interface != null;
        }
    }
}