using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Npgsql;

internal static class Program
{
    private sealed record Migration(string Id, string Path, string Sql, string Hash);

    private static async Task<int> Main(string[] args)
    {
        var options = ParseArgs(args);
        var provider = NormalizeProvider(options.Provider
            ?? Environment.GetEnvironmentVariable("SHOUTECH_DB_PROVIDER")
            ?? "sqlite");
        var connectionString = options.ConnectionString
            ?? Environment.GetEnvironmentVariable("SHOUTECH_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("SHOUTECH_DB_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Connection string is required. Use --connection or SHOUTECH_CONNECTION_STRING.");
            return 2;
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            Console.Error.WriteLine("Repository root not found (expected dba/mig). Run this tool from the repository build output.");
            return 2;
        }

        var migrationDirectory = ResolveMigrationDirectory(repoRoot, provider);
        if (!Directory.Exists(migrationDirectory))
        {
            Console.Error.WriteLine($"No migration directory exists for provider '{provider}': {migrationDirectory}");
            return 2;
        }

        var migrations = Directory.GetFiles(migrationDirectory, "*.sql", SearchOption.AllDirectories)
            .Select(path => new Migration(
                Path.GetRelativePath(migrationDirectory, path).Replace('\\', '/'),
                path,
                File.ReadAllText(path),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (migrations.Count == 0)
        {
            Console.WriteLine($"No SQL migrations found in {migrationDirectory}.");
            return 0;
        }

        Console.WriteLine($"Provider: {provider}");
        Console.WriteLine($"Migration directory: {migrationDirectory}");
        Console.WriteLine($"Discovered migrations: {migrations.Count}");
        if (options.DryRun)
        {
            foreach (var migration in migrations)
                Console.WriteLine(migration.Id);
            return 0;
        }

        try
        {
            await using var connection = CreateConnection(provider, connectionString);
            await connection.OpenAsync();
            await EnsureHistoryTableAsync(connection, provider);

            var applied = await ReadAppliedAsync(connection);
            var hasExistingSchema = await HasExistingApplicationSchemaAsync(connection, provider);
            if (options.BaselineExisting && applied.Count == 0)
            {
                if (!hasExistingSchema)
                {
                    Console.Error.WriteLine("Baseline refused: no existing application tables were detected. Run migrations normally on an empty database.");
                    return 2;
                }
                Console.WriteLine("Baseline mode: recording current migration files without executing them. Verify the existing schema before using this option.");
                foreach (var migration in migrations)
                    await RecordMigrationAsync(connection, migration, provider, transaction: null);
                Console.WriteLine("Baseline complete.");
                return 0;
            }
            if (applied.Count == 0 && hasExistingSchema)
            {
                Console.Error.WriteLine("An existing application schema has no migration history. To prevent unsafe reapplication, verify the schema and run with --baseline-existing, or migrate a disposable copy first.");
                return 4;
            }

            var seenHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var migration in migrations)
            {
                if (applied.TryGetValue(migration.Id, out var existingHash))
                {
                    if (!string.Equals(existingHash, migration.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"Migration checksum mismatch: {migration.Id}. Applied migrations must be immutable; add a new migration instead.");
                        return 3;
                    }
                    Console.WriteLine($"SKIP {migration.Id}");
                    seenHashes.TryAdd(migration.Hash, migration.Id);
                    continue;
                }

                if (seenHashes.TryGetValue(migration.Hash, out var duplicateOf))
                {
                    // Preserve both historical IDs in schema_migrations but execute identical
                    // SQL only once per run. This is important for duplicated legacy files such
                    // as PostgreSQL 003_modules.sql and 004_modules.sql.
                    Console.WriteLine($"SKIP DUPLICATE {migration.Id} (identical SQL to {duplicateOf}); recording migration ID.");
                    await RecordMigrationAsync(connection, migration, provider, transaction: null);
                    applied[migration.Id] = migration.Hash;
                    continue;
                }

                Console.WriteLine($"APPLY {migration.Id}");
                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    await using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandTimeout = 600;
                        command.CommandText = migration.Sql;
                        await command.ExecuteNonQueryAsync();
                    }
                    await RecordMigrationAsync(connection, migration, provider, transaction);
                    await transaction.CommitAsync();
                    seenHashes.TryAdd(migration.Hash, migration.Id);
                    applied[migration.Id] = migration.Hash;
                    Console.WriteLine($"OK   {migration.Id}");
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            Console.WriteLine("Migration run completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Migration failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static DbConnection CreateConnection(string provider, string connectionString) => provider switch
    {
        "sqlite" => new SqliteConnection(connectionString),
        "postgresql" => new NpgsqlConnection(connectionString),
        _ => throw new InvalidOperationException($"Unsupported provider '{provider}'.")
    };

    private static async Task EnsureHistoryTableAsync(DbConnection connection, string provider)
    {
        var sql = provider switch
        {
            "sqlite" => "CREATE TABLE IF NOT EXISTS schema_migrations (migration_id TEXT PRIMARY KEY, checksum TEXT NOT NULL, applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);",
            "postgresql" => "CREATE TABLE IF NOT EXISTS schema_migrations (migration_id text PRIMARY KEY, checksum char(64) NOT NULL, applied_at timestamptz NOT NULL DEFAULT now());",
            _ => throw new InvalidOperationException("Unsupported provider.")
        };
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }


    private static async Task<bool> HasExistingApplicationSchemaAsync(DbConnection connection, string provider)
    {
        var sql = provider switch
        {
            "sqlite" => "SELECT 1 FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> 'schema_migrations' LIMIT 1",
            "postgresql" => "SELECT 1 FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema') AND table_name <> 'schema_migrations' LIMIT 1",
            _ => throw new InvalidOperationException("Unsupported provider.")
        };
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is not null && value != DBNull.Value;
    }

    private static async Task<Dictionary<string, string>> ReadAppliedAsync(DbConnection connection)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT migration_id, checksum FROM schema_migrations";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.GetString(0)] = reader.GetString(1).Trim();
        return result;
    }

    private static async Task RecordMigrationAsync(DbConnection connection, Migration migration, string provider, DbTransaction? transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO schema_migrations (migration_id, checksum) VALUES (@id, @checksum)";
        AddParameter(command, "@id", migration.Id);
        AddParameter(command, "@checksum", migration.Hash);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string ResolveMigrationDirectory(string root, string provider)
    {
        var migrationRoot = Path.Combine(root, "dba", "mig");
        return provider switch
        {
            "sqlite" => Path.Combine(migrationRoot, "sqlite"),
            "postgresql" => Path.Combine(migrationRoot, "postgresql"),
            _ => Path.Combine(migrationRoot, provider)
        };
    }

    private static string? FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        for (var depth = 0; depth < 12 && directory is not null; depth++, directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "dba", "mig")))
                return directory.FullName;
        return null;
    }

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "sqlite" or "sqlite3" => "sqlite",
        "postgres" or "postgresql" or "npgsql" => "postgresql",
        _ => throw new ArgumentException($"Unsupported provider '{provider}'. Use sqlite or postgresql.")
    };

    private sealed record Arguments(string? Provider, string? ConnectionString, bool DryRun, bool BaselineExisting);

    private static Arguments ParseArgs(string[] args)
    {
        string? provider = null;
        string? connection = null;
        var dryRun = false;
        var baseline = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--provider" when i + 1 < args.Length: provider = args[++i]; break;
                case "--connection" when i + 1 < args.Length: connection = args[++i]; break;
                case "--dry-run": dryRun = true; break;
                case "--baseline-existing": baseline = true; break;
                case "--help":
                    Console.WriteLine("SqlMigrator --provider sqlite|postgresql --connection <connection-string> [--dry-run] [--baseline-existing]");
                    Environment.Exit(0);
                    break;
                default: throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }
        return new Arguments(provider, connection, dryRun, baseline);
    }
}
