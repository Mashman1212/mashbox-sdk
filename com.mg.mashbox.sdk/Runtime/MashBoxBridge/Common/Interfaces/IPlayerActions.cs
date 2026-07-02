using UnityEngine;

namespace MashBoxBridge.Common.Interfaces
{
    public enum HookGrind
    {
        Null,
        SouthHookEast,
        SouthHookWest,
        NorthHookEast,
        NorthHookWest,
        EastHookNorth,
        EastHookSouth,
        WestHookNorth,
        WestHookSouth,
    }

    public enum FaceButtonComboID
    {
        None = 0,
        JamWhip = 1,
    }

    public interface IFaceButtonComboHandler
    {
        void FaceButtonCombo(FaceButtonComboID comboID);
    }


    
    public interface IPlayerActions
    {
        string ActionMapName { get; }

        bool IsRemoteControl { get; }

        void Accelerate(float input);
        void Throttle(float input);
        void Brake(float value);
        void Drift(bool on);
        void Revert(bool on);
        void Slider(float input);
        void Jump(Vector2 direction);
        void StartSprinting();
        void StopSprinting();
        void Climb(bool on);
        void Boost(bool on);
        void Look(Vector2 input);
        void Elevate(float input);
        void ExtraLean(float input);
        void ExtraLeanStick(float input);
        void DropDown();
        void ForcePull();
        void DropHeld();
        void Mount();
        void Dismount();
        void StandUp();
        void Kill();
        void Steer(Vector2 inputRaw,Vector2 relativeDirection);
        void Pump(float value);
        void Manny(float value);
        void Nosey(float value);
        void Preload(float value);
        void GrindPose(int sliceIndex, HookGrind hookGrind);
        void LeftHandAbility(bool on);
        void RightHandAbility(bool on);
        void LeftFootAbility(bool on);
        void RightFootAbility(bool on);
        void FaceButtonCombo(FaceButtonComboID comboID);
    }
}
