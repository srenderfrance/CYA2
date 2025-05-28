namespace DataLibrary
{
    public interface IDatabaseMonitor
    {
        bool IsConnected { get; }
        string LastError { get; }
        bool AllowMySqlOperations { get; } // Add this property
        void Suspend();
        void Resume();
    }
}
