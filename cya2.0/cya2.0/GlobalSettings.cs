using System.Net.Sockets;

namespace cya2._0
{
    public static class GlobalSettings
    {
        // TEMPORARY: Force everything to work regardless of actual database connection
        // TODO: Remove this property after Azure testing
        public static bool CompleteBypass { get; set; } = false; // No longer needed by default

        public static bool AllowMySqlLoading { get; set; } = false;
        public static bool BypassDatabaseMonitoring { get; set; } = true;

        public static bool CheckDatabaseTcpConnection(string host, int port, int timeoutMs = 1000)
        {
            // TEMPORARY: Bypass connection check when CompleteBypass is enabled
            // TODO: Remove this condition after Azure testing
            if (CompleteBypass)
            {
                return true; // Always report success in bypass mode
            }

            try
            {
                using (var client = new TcpClient())
                {
                    var connectResult = client.BeginConnect(host, port, null, null);
                    var success = connectResult.AsyncWaitHandle.WaitOne(timeoutMs);

                    if (!success)
                    {
                        return false; // Connection timed out
                    }

                    try
                    {
                        client.EndConnect(connectResult);
                        return true; // Successfully connected
                    }
                    catch
                    {
                        return false; // Connection failed
                    }
                }
            }
            catch
            {
                return false; // Any error means connection failed
            }
        }
    }
}
