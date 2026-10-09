using System.IO;
using System.Text.Json;
using MySqlConnector;

namespace VacuumPumpSystem.Services;

public class DatabaseMigrationService
{
    private readonly string _serverConnectionString;
    private readonly string _databaseConnectionString;

    public DatabaseMigrationService()
    {
        string configPath = Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.json"
        );

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                "appsettings.json was not found.",
                configPath
            );
        }

        string json = File.ReadAllText(configPath);

        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement database =
            document.RootElement.GetProperty("Database");

        string server =
            database.GetProperty("Server").GetString() ?? "localhost";

        string port =
            database.GetProperty("Port").GetInt32().ToString();

        string databaseName =
            database.GetProperty("Database").GetString()
            ?? "vacuum_pump";

        string username =
            database.GetProperty("Username").GetString()
            ?? "root";

        string password =
            database.GetProperty("Password").GetString()
            ?? "";

        _serverConnectionString =
            $"Server={server};Port={port};" +
            $"User ID={username};Password={password};";

        _databaseConnectionString =
            $"{_serverConnectionString}" +
            $"Database={databaseName};";
    }

    public async Task InitializeAsync()
    {
        Console.WriteLine("Initializing database...");

        await CreateDatabaseAsync();

        await CreateMigrationTableAsync();

        await RunPendingMigrationsAsync();

        Console.WriteLine("Database initialization completed.");
    }

    private async Task CreateDatabaseAsync()
    {
        await using var connection =
            new MySqlConnection(_serverConnectionString);

        await connection.OpenAsync();

        const string sql = """
            CREATE DATABASE IF NOT EXISTS vacuum_pump
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_unicode_ci;
            """;

        await using var command =
            new MySqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();

        Console.WriteLine("Database checked: vacuum_pump");
    }

    private async Task CreateMigrationTableAsync()
    {
        await using var connection =
            new MySqlConnection(_databaseConnectionString);

        await connection.OpenAsync();

        const string sql = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id INT AUTO_INCREMENT PRIMARY KEY,
                migration_name VARCHAR(255) NOT NULL UNIQUE,
                applied_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;

        await using var command =
            new MySqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();

        Console.WriteLine("Migration table checked.");
    }

    private async Task RunPendingMigrationsAsync()
    {
        string migrationsPath = Path.Combine(
            AppContext.BaseDirectory,
            "Database",
            "Migrations"
        );

        if (!Directory.Exists(migrationsPath))
        {
            throw new DirectoryNotFoundException(
                $"Migration directory not found: {migrationsPath}"
            );
        }

        string[] migrationFiles =
            Directory.GetFiles(
                migrationsPath,
                "*.sql"
            )
            .OrderBy(Path.GetFileName)
            .ToArray();

        await using var connection =
            new MySqlConnection(_databaseConnectionString);

        await connection.OpenAsync();

        foreach (string migrationFile in migrationFiles)
        {
            string migrationName =
                Path.GetFileName(migrationFile);

            if (await IsMigrationAppliedAsync(
                connection,
                migrationName))
            {
                Console.WriteLine(
                    $"Already applied: {migrationName}"
                );

                continue;
            }

            Console.WriteLine(
                $"Running migration: {migrationName}"
            );

            string sql =
                await File.ReadAllTextAsync(migrationFile);

            await using var transaction =
                await connection.BeginTransactionAsync();

            try
            {
                await using var command =
                    new MySqlCommand(
                        sql,
                        connection,
                        transaction
                    );

                await command.ExecuteNonQueryAsync();

                const string recordSql = """
                    INSERT INTO schema_migrations
                    (migration_name)
                    VALUES (@migrationName);
                    """;

                await using var recordCommand =
                    new MySqlCommand(
                        recordSql,
                        connection,
                        transaction
                    );

                recordCommand.Parameters.AddWithValue(
                    "@migrationName",
                    migrationName
                );

                await recordCommand.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                Console.WriteLine(
                    $"Applied: {migrationName}"
                );
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }

    private static async Task<bool> IsMigrationAppliedAsync(
        MySqlConnection connection,
        string migrationName)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM schema_migrations
            WHERE migration_name = @migrationName;
            """;

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@migrationName",
            migrationName
        );

        object? result =
            await command.ExecuteScalarAsync();

        return Convert.ToInt32(result) > 0;
    }
}