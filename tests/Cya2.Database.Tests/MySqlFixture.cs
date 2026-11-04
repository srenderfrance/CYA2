using System.Diagnostics;
using MySql.Data.MySqlClient;
using Xunit;

namespace Cya2.Database.Tests;

public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (!MySqlFixture.Enabled)
            Skip = "Set CYA2_RUN_MYSQL_TESTS=1 before discovery to run disposable Docker MySQL tests.";
    }
}

public sealed class MySqlFixture : IAsyncLifetime
{
    public static bool Enabled => Environment.GetEnvironmentVariable("CYA2_RUN_MYSQL_TESTS") == "1";
    private readonly string _container = "cya2-tests-" + Guid.NewGuid().ToString("N");
    private readonly string _password = Guid.NewGuid().ToString("N");
    private string _serverConnection = string.Empty;
    private bool _started;

    public async Task InitializeAsync()
    {
        if (!Enabled) return;
        try
        {
            var started = await RunAsync("docker", ["run", "--detach", "--rm", "--name", _container,
                "--publish", "127.0.0.1::3306", "--env", "MYSQL_ROOT_PASSWORD", "--env", "MYSQL_ROOT_HOST=%", "mysql:8.0.40"],
                new Dictionary<string, string> { ["MYSQL_ROOT_PASSWORD"] = _password }, TimeSpan.FromMinutes(5));
            if (started.ExitCode != 0) throw new InvalidOperationException("Unable to start disposable MySQL container. Check Docker and image availability.");
            _started = true;
            var port = await RunAsync("docker", ["port", _container, "3306/tcp"]);
            if (port.ExitCode != 0) throw new InvalidOperationException("Unable to resolve MySQL container port.");
            _serverConnection = new MySqlConnectionStringBuilder
            {
                Server = "127.0.0.1", Port = uint.Parse(port.Output.Trim().Split(':').Last()),
                UserID = "root", Password = _password, SslMode = MySqlSslMode.Disabled,
                Pooling = false, ConnectionTimeout = 2
            }.ConnectionString;
            var timeout = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    await using var connection = new MySqlConnection(_serverConnection);
                    await connection.OpenAsync();
                    break;
                }
                catch (MySqlException) when (timeout.Elapsed < TimeSpan.FromMinutes(2))
                {
                    await Task.Delay(1000);
                }
            }
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_started) return;
        MySqlConnection.ClearAllPools();
        var result = await RunAsync("docker", ["rm", "--force", _container]);
        if (result.ExitCode != 0) throw new InvalidOperationException("Unable to remove integration-test container: " + _container);
        _started = false;
    }

    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = "test_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(_serverConnection, $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci");
        var setup = new MySqlConnectionStringBuilder(_serverConnection) { Database = name }.ConnectionString;
        var initialized = await MigrateAsync(setup, initialize: true);
        Assert.True(initialized.ExitCode == 0, "Schema initialization failed: " + initialized.Output);
        var user = "app_" + Guid.NewGuid().ToString("N")[..20];
        var password = Guid.NewGuid().ToString("N");
        await ExecuteAsync(_serverConnection, $"CREATE USER '{user}'@'%' IDENTIFIED BY '{password}'");
        await ExecuteAsync(_serverConnection, $"GRANT SELECT, INSERT, UPDATE, DELETE, CREATE TEMPORARY TABLES ON `{name}`.* TO '{user}'@'%'");
        var runtime = new MySqlConnectionStringBuilder(setup) { UserID = user, Password = password }.ConnectionString;
        return new TestDatabase(setup, runtime);
    }

    public static Task<CommandResult> MigrateAsync(string connectionString, bool initialize = false, string? baselinePath = null)
    {
        var args = new List<string> { Path.Combine(AppContext.BaseDirectory, "migrator", "Cya2.DatabaseMigrator.dll") };
        if (initialize) args.Add("--initialize");
        if (baselinePath is not null) args.AddRange(["--baseline-script", baselinePath]);
        return RunAsync("dotnet", args, new Dictionary<string, string> { ["CYA2_SCHEMA_CONNECTION_STRING"] = connectionString });
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<string> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static async Task<CommandResult> RunAsync(string executable, IEnumerable<string> args,
        Dictionary<string, string>? environment = null, TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (environment is not null)
            foreach (var item in environment) start.Environment[item.Key] = item.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(cancellation.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException(executable + " exceeded the test timeout."); }
        return new CommandResult(process.ExitCode, await output + await error);
    }

    public sealed record CommandResult(int ExitCode, string Output);
    public sealed record TestDatabase(string SetupConnection, string RuntimeConnection);
}
