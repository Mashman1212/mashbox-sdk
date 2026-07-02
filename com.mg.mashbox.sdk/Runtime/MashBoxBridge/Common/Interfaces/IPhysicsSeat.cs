using System;
using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public interface IPhysicsSeat : IVehicleSeat
    {
        IManagedRigidbody ManagedRigidbody { get; }//the connected Managed rigidbody.. 
        Rigidbody Body { get; }
        
        Transform SystemRootTrans { get; }
        
        Joint ConnectedJoint { get; }
        new ISeatablePhysicsBody ConnectedBody { get; } // Override the ConnectedBody property in the base interface
        Joint Attach(ISeatablePhysicsBody seatablePhysicsBody, Action<Joint> callback);
        void UpdateJointAnchor();
        new void Detach();

        void TriggerGetOff();//for animation rig purposes.. this is not how to actually get off
        
        bool IsBodyAttachedToSystem(int instanceID);

        Action OnSeatStateChanged { get; set; }

        bool HasLimbSupportStrength { get; }

        bool CanDismount { get; }
    }
}