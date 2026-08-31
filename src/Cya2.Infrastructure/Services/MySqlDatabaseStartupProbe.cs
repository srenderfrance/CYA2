using Cya2.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Net.Sockets;

namespace Cya2.Infrastructure.Services;

public sealed class MySqlDatabaseStartupProbe : IDatabaseStartupProbe
{
    private const int DefaultPort = 3306;
    private const int StartupTimeoutMilliseconds = 2000;
    private readonly IConfiguration _configuration;

    public MySqlDatabaseStartupProbe(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool Check(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (host, port) = ParseHostPort();
            using var client = new TcpClient();
            var connectResult = client.BeginConnect(host, port, null, null);
            using (connectResult.AsyncWaitHandle)
            {
                if (!connectResult.AsyncWaitHandle.WaitOne(StartupTimeoutMilliseconds))
                {
                    return false;
                }
            }

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private (string host, int port) ParseHostPort()
    {
        var connectionString = _configuration.GetConnectionString("default") ?? string.Empty;
        var host = "localhost";
        var port = DefaultPort;

        foreach (var part in connectionString.Split(';'))
        {
            if (part.StartsWith("server=", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("host=", StringComparison.OrdinalIgnoreCase))
            {
                host = part[(part.IndexOf('=') + 1)..].Trim();
            }
            else if (part.StartsWith("port=", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(part[(part.IndexOf('=') + 1)..].Trim(), out port);
            }
        }

        return (host, port);
    }
}
