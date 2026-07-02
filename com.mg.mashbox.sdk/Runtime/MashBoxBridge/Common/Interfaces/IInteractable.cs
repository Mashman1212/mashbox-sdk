namespace MashBoxBridge.Common.Interfaces
{
    public interface IInteractable
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="interactor"></param>
        /// <returns>false if is negative interaction such as a Exit Vehicle interaction</returns>
        bool Interact(IInteractor interactor);
        void Arm();
        void Disarm();
        
        //// Called when interaction starts
        //void OnInteractionStart();
        //
        //// Called when interaction is in progress
        //void OnInteractionUpdate();
        //
        //// Called when interaction ends
        //void OnInteractionEnd();
    }
}