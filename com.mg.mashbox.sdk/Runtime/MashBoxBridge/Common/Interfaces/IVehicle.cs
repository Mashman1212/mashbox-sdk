using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public interface IVehicleTick//way we can manage ticking all the components ion one place
    {
        public void RunVehicleTick(float deltaTime);
    }

    public interface IVehicle
    {
        Transform RootTrans { get; }

        IPhysicsSeat DriverSeat { get; }
        bool HasOperator();

        bool IsRemoteControl { get; }

        bool IsLandedTrickCombo { get; }
    }
    
    public interface IVehicleSeat
    {
        IVehicle Vehicle {get;}
        void Detach();
        bool IsOccupied { get; }
        IVehicleSeatable ConnectedBody { get; } // This might be a new interface you'd define
        void Attach(IVehicleSeatable seatableBody);
    }

    
    public interface IVehicleSeatable
    {
     
    }


}