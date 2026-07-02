namespace MashBoxBridge.Common.Interfaces
{
    public interface ICommand
    {
        public void Execute();
    
        public void Undo();

        bool HasUndo { get; }

        string ActionType { get; }
        public string ActionName { get; }
        public string Parameters { get; }
    }
    public abstract class CommandBase : ICommand
    {
        protected string _actionType;

        public string Parameters => _parameters;
        protected string _parameters;

        public string ActionName => _actionName;
        protected string _actionName = "CommandBase";
        public string ActionType => _actionType;
        
        protected CommandBase(string actionType, string parameters = null)
        {
            _actionType = actionType;
            _parameters = parameters;
        }
    
        public abstract void Execute();
    
        public abstract void Undo();
        public abstract bool HasUndo { get; }
    }
}