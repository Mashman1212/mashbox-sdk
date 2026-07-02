using System;
using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public interface ICharacterManager
    {
        ISessionMarker SessionMarker { get; }
        
        Transform Root { get; }
        Action Killed { get; set; }
        Action<Vector3,Quaternion> ReSpawned { get; set; }
        Action<IPhysicsSeat> ChangedSeats { get; set; }
        IPhysicsSeat CurrentSeat { get; }
        IPhysicsSeat LastVehicleSeat { get; }
        Rigidbody CharacterControllerBody { get; }
        Rigidbody CharacterPhysicsHips { get; }

        public void TeleportPosDelta(Vector3 delta);
        public void TeleportRotDelta(Quaternion delta);
        public bool IsLocalPlayer { get; }

        public string StringNetId { get; }

        void Kill();
        
        bool IsDrivingVehicle { get; }
        
        bool Grounded { get; }

        bool IsAlive { get; }

        float TimeSinceLastJumpBegin { get; }

        float TimeSinceChangedSeat { get; }

        float TimeSinceStartDismountAnimation { get; }

        float TimeLeftInAir { get; }

        bool IsGrindPulling { get; }
        bool IsGrinding { get; }

        bool IsGoofyStance { get; }
        void SetStance(bool goofy);

        bool TrickSetEngaged { get; }
    }
}