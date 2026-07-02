using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public interface IVehicleKillHandler
    {
        void KillDriver();
       // void Kill();
        void Revive();

        void TestCollisionDeath(Rigidbody thisBody,Collision collision);

        bool IsDead { get; }
    }
}