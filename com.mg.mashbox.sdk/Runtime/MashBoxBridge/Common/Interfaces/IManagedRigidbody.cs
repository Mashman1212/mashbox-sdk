using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public interface IManagedRigidbody
    {
        Transform SystemRoot { get; }

        Rigidbody Rigidbody { get; }
    
        Vector3 Velocity { get; set; }
        Vector3 AngularVelocity { get; set; }
        float MaxAngularVelocity { get; set; }
        Quaternion Rotation { get; set; }
        Vector3 Position { get; set; }
        public void TeleportPosDelta(Vector3 delta, bool preserveVelocity = true);
        public void TeleportRotDelta(Quaternion delta, bool preserveAngularVelocity = true);
        float Mass { get; }

        bool Kinematic { get; set; }
    
        void MovePositionDelta(Vector3 deltaPosition);//use delta since not all bodies managed by a system would share the same position
        void AddForce(Vector3 force, ForceMode mode = ForceMode.Force);
        void AddRelativeForce(Vector3 force, ForceMode mode = ForceMode.Force);
        void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force);
        void AddRelativeTorque(Vector3 torque, ForceMode mode = ForceMode.Force);
    }
}