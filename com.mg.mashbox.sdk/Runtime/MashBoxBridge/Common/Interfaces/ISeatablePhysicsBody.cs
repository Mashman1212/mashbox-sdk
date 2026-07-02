using System;
using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public delegate void SeatablePhysicsBodyActionHandler(IPhysicsSeat physicsSeat);

    public interface ISeatablePhysicsBody
    {
        Transform PhysicsBodyHips { get; }
        bool IsSeated { get; }
        bool MountedFromHolding { get; }
        Rigidbody Body { get; }
        Joint Joint { get; }
        void BreakJoint();
        void SeatOn(IPhysicsSeat seat,bool holdingMount = false);
        void GetOffSeat();//This should be immediate and not trigger any special animation triggers

        void TriggerGetOff();//The preferred method to call for proper dismounting and trigger animations before breaking the joint
        
        IPhysicsSeat CurrentSeat { get; }

        void ReceiveVehicleCollisionData(Collision collision);

        //need this for human to vehicle contact augmentation so we know if we are hitting the vehicle we are driving
        
        bool CanSit { set; get; }

        // Events
        event SeatablePhysicsBodyActionHandler Seated;
        event Action Unseated;
    }
}
