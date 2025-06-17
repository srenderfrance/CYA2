namespace DataLibrary
{
    public interface IDatabaseMonitor
    {
        bool IsConnected { get; }
        string LastError { get; }
        bool AllowMySqlOperations { get; }
        bool BypassMonitoring { get; } // This property needs to be implemented
        void Suspend();
        void Resume();
        void SetBypassMonitoring(bool bypass); // This method needs to be implemented
    }
}
