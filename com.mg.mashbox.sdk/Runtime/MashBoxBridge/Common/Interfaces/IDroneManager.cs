namespace MashBoxBridge.Common.Interfaces
{
    public interface IDroneManager 
    {
        void DeployDrone();
        void DisableDrone();

        bool IsDeployed { get; }
    }
}