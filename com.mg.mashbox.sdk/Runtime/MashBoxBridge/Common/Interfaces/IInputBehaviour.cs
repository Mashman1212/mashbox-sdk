namespace MashBoxBridge.Common.Interfaces
{
    public interface IInputBehaviour
    {
        public void SetActions(IPlayerActions actions);

        public void ResetRestrictions();
        public void RestrictPlaceMarker();
        public void RestrictDeployDrone();
        public void RestrictSpawnAtMarker();
        public void RestrictMounting();
        public void RestrictDismounting();
        public void RestrictStandingUp();
        public void HoldBraking();
        public void RestrictAccelerate();
        public void RestrictSteering();
    }
}