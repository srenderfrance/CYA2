using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;

const string historyTable = "Cya2SchemaMigrations";
const string lockName = "cya2-schema-migrations";
const string baselineId = "000001";
const string baselineFileName = "001_InitialSchema.sql";

try
{
    var options = MigrationOptions.Parse(args);
    if (!options.Initialize)
    {
        throw new InvalidOperationException("Pre-production schema setup requires --initialize and an empty database.");
    }

    if (args.Contains("--migrations-directory", StringComparer.Ordinal))
    {
        throw new InvalidOperationException("Incremental migrations are not supported during pre-production.");
    }

    var baselinePath = Path.GetFullPath(options.BaselineScript);
    if (!File.Exists(baselinePath))
    {
        throw new InvalidOperationException($"Initial schema script was not found: {baselinePath}");
    }

    var content = await File.ReadAllTextAsync(baselinePath, Encoding.UTF8);
    var statements = MigrationFile.SplitStatementsForExecution(content);
    if (statements.Count == 0)
    {
        throw new InvalidOperationException($"Initial schema script '{baselinePath}' is empty.");
    }

    await using var connection = new MySqlConnection(options.ConnectionString);
    await connection.OpenAsync();
    await AcquireLockAsync(connection);
    try
    {
        await using var countCommand = new MySqlCommand(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE();", connection);
        if (Convert.ToInt32(await countCommand.ExecuteScalarAsync()) != 0)
        {
            throw new InvalidOperationException("Initialization requires an empty database.");
        }

        Console.WriteLine($"Initializing database with {baselineFileName}...");
        foreach (var statement in statements)
        {
            await ExecuteAsync(connection, statement);
        }

        Console.WriteLine($"Initialized database with {baselineFileName}.");
    }
    finally
    {
        await ReleaseLockAsync(connection);
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Migration failed: {ex.Message}");
    return 1;
}

static async Task<Dictionary<string, AppliedMigration>> LoadAppliedAsync(MySqlConnection connection)
{
    var result = new Dictionary<string, AppliedMigration>(StringComparer.OrdinalIgnoreCase);
    await using var command = new MySqlCommand($"SELECT MigrationId, ScriptName, Checksum FROM `{historyTable}` ORDER BY MigrationId;", connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var id = reader.GetString(0);
        if (!result.TryAdd(id, new AppliedMigration(id, reader.GetString(1), reader.GetString(2))))
        {
            throw new InvalidOperationException($"Migration history contains duplicate ID '{id}'.");
        }
    }

    return result;
}

static void ValidateAppliedChecksums(Dictionary<string, AppliedMigration> applied, IReadOnlyList<MigrationFile> migrations, string baselinePath)
{
    var current = migrations.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
    var baselineChecksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baselinePath))).ToLowerInvariant();
    foreach (var entry in applied.Values)
    {
        if (entry.Id.Equals(baselineId, StringComparison.OrdinalIgnoreCase))
        {
            if (!entry.ScriptName.Equals(baselineFileName, StringComparison.OrdinalIgnoreCase) ||
                !entry.Checksum.Equals(baselineChecksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Checksum mismatch for the applied baseline '{baselineFileName}'. Restore the original baseline or repair the migration history.");
            }

            continue;
        }

        if (!current.TryGetValue(entry.Id, out var migration))
        {
            throw new InvalidOperationException($"Applied migration '{entry.Id}' ({entry.ScriptName}) is missing from the migration directory.");
        }

        if (!string.Equals(entry.Checksum, migration.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Checksum mismatch for applied migration '{entry.Id}' ({entry.ScriptName}). Restore the original file or create a new migration.");
        }
    }
}

static async Task AcquireLockAsync(MySqlConnection connection)
{
    await using var command = new MySqlCommand("SELECT GET_LOCK(@name, 30);", connection);
    command.Parameters.AddWithValue("@name", lockName);
    var acquired = Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    if (!acquired)
    {
        throw new InvalidOperationException("Could not acquire the schema migration lock within 30 seconds.");
    }
}

static async Task ReleaseLockAsync(MySqlConnection connection)
{
    await using var command = new MySqlCommand("SELECT RELEASE_LOCK(@name);", connection);
    command.Parameters.AddWithValue("@name", lockName);
    await command.ExecuteScalarAsync();
}

static async Task ExecuteAsync(MySqlConnection connection, string sql, MySqlTransaction? transaction = null, params MySqlParameter[] parameters)
{
    await using var command = new MySqlCommand(sql, connection, transaction);
    command.CommandTimeout = 300;
    command.Parameters.AddRange(parameters);
    await command.ExecuteNonQueryAsync();
}

sealed record AppliedMigration(string Id, string ScriptName, string Checksum);

sealed class MigrationFile
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string Checksum { get; init; }
    public required IReadOnlyList<string> Statements { get; init; }

    public static IReadOnlyList<MigrationFile> LoadAll(string directory)
    {
        var files = Directory.GetFiles(directory, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(path => new { Path = path, Name = Path.GetFileName(path) })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var migrations = new List<MigrationFile>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var separator = file.Name.IndexOf('_');
            if (separator <= 0 || !int.TryParse(file.Name[..separator], out var numericId) || numericId <= 0)
                throw new InvalidOperationException($"Migration filename '{file.Name}' must use the format NNN_Name.sql.");

            var id = numericId.ToString("D6");
            if (id.Equals("000001", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Migration filename '{file.Name}' uses the reserved baseline ID 000001.");

            if (!ids.Add(id))
                throw new InvalidOperationException($"Duplicate migration ID '{id}'.");

            var content = File.ReadAllText(file.Path, Encoding.UTF8);
            var statements = SplitStatementsForExecution(content);
            if (statements.Count == 0)
                throw new InvalidOperationException($"Migration '{file.Name}' is empty.");

            migrations.Add(new MigrationFile
            {
                Id = id,
                FileName = file.Name,
                Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant(),
                Statements = statements
            });
        }

        return migrations.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<string> SplitStatementsForExecution(string content)
    {
        var statements = new List<string>();
        var builder = new StringBuilder();
        var quote = '\0';
        for (var i = 0; i < content.Length; i++)
        {
            var character = content[i];
            if (quote != '\0')
            {
                builder.Append(character);
                if (character == quote && (i == 0 || content[i - 1] != '\\')) quote = '\0';
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                builder.Append(character);
            }
            else if (character == ';')
            {
                AddStatement(statements, builder);
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }

        AddStatement(statements, builder);
        return statements;
    }

    private static void AddStatement(List<string> statements, StringBuilder builder)
    {
        var value = string.Join(
            Environment.NewLine,
            builder.ToString()
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)))
            .Trim();

        if (value.Length > 0) statements.Add(value);
    }
}

sealed record MigrationOptions(string ConnectionString, string MigrationsDirectory)
{
    public bool Initialize { get; init; }
    public string BaselineScript { get; init; } = "database/schema/001_InitialSchema.sql";

    public static MigrationOptions Parse(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CYA2_SCHEMA_CONNECTION_STRING");
        var migrationsDirectory = "database/migrations";
        var initialize = false;
        var baselineScript = "database/schema/001_InitialSchema.sql";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--connection-string" && i + 1 < args.Length) connectionString = args[++i];
            else if (args[i] == "--migrations-directory" && i + 1 < args.Length) migrationsDirectory = args[++i];
            else if (args[i] == "--baseline-script" && i + 1 < args.Length) baselineScript = args[++i];
            else if (args[i] == "--initialize") initialize = true;
            else if (args[i] is "--help" or "-h")
            {
                Console.WriteLine("Usage: --connection-string <value> [--initialize] [--baseline-script <path>] [--migrations-directory <path>]");
                Environment.Exit(0);
            }
            else throw new ArgumentException($"Unknown or incomplete option '{args[i]}'.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Provide --connection-string or CYA2_SCHEMA_CONNECTION_STRING.");
        return new MigrationOptions(connectionString, migrationsDirectory)
        {
            Initialize = initialize,
            BaselineScript = baselineScript
        };
    }
}
