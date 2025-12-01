using System.Net.Sockets;

namespace cya2
{
    public static class GlobalSettings
    {
        public static bool CompleteBypass { get; set; } = false;
        public static bool AllowMySqlLoading { get; set; } = false;
        public static bool BypassDatabaseMonitoring { get; set; } = false;

        public static bool CheckDatabaseTcpConnection(string host, int port, int timeoutMs = 1000)
        {
            try
            {
                using var client = new TcpClient();
                var connectResult = client.BeginConnect(host, port, null, null);
                var success = connectResult.AsyncWaitHandle.WaitOne(timeoutMs);
                if (!success) return false;

                try
                {
                    client.EndConnect(connectResult);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
                return false; // Any error means connection failed
            }
        }
    }
}
