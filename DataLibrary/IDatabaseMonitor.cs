namespace DataLibrary
{
    public interface IDatabaseMonitor
    {
        bool IsConnected { get; }
        string LastError { get; }
        bool AllowMySqlOperations { get; }
        bool BypassMonitoring { get; }
        void Suspend();
        void Resume();
        void SetBypassMonitoring(bool bypass);
        void MarkAsDisconnected(string reason);
    }
}
