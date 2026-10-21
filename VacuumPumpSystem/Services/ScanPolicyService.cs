

using System.IO;
using System.Text.Json;
using MySqlConnector;

namespace VacuumPumpSystem.Services;

public sealed class ScanPolicy
{
    public int Id { get; set; }
    public int LineId { get; set; }
    public int RequiredScanCount { get; set; }
    public int MinimumIntervalMinutes { get; set; }
    public decimal MaximumPressure { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? UpdatedAt { get; set; }
}

public sealed class ScanPolicyService
{
    private readonly string _connectionString;

    public ScanPolicyService()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException("appsettings.json was not found.", configPath);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement database = document.RootElement.GetProperty("Database");

        string server = database.GetProperty("Server").GetString() ?? "localhost";
        int port = database.GetProperty("Port").GetInt32();
        string databaseName = database.GetProperty("Database").GetString()
            ?? throw new InvalidOperationException("Database name is missing in appsettings.json.");
        string username = database.GetProperty("Username").GetString()
            ?? throw new InvalidOperationException("Database username is missing in appsettings.json.");
        string password = database.GetProperty("Password").GetString() ?? "";

        var builder = new MySqlConnectionStringBuilder
        {
            Server = server,
            Port = (uint)port,
            Database = databaseName,
            UserID = username,
            Password = password
        };
        _connectionString = builder.ConnectionString;
    }

   public async Task<ScanPolicy?> GetByLineAsync(int lineId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT id, line_id, required_scan_count,
                minimum_interval_minutes, maximum_pressure,
                is_active, updated_at
            FROM line_scan_policies
            WHERE line_id = @LineId
            LIMIT 1;";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@LineId", lineId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new ScanPolicy
        {
            Id = reader.GetInt32("id"),
            LineId = reader.GetInt32("line_id"),
            RequiredScanCount = reader.GetInt32("required_scan_count"),
            MinimumIntervalMinutes = reader.GetInt32("minimum_interval_minutes"),
            MaximumPressure = reader.GetDecimal("maximum_pressure"),
            IsActive = reader.GetBoolean("is_active"),
            UpdatedAt = reader.GetDateTime("updated_at")
        };
    }
    public async Task SaveAsync(ScanPolicy policy)
    {
        Validate(policy);

        const string sql = """
            INSERT INTO line_scan_policies
                (line_id, required_scan_count, minimum_interval_minutes,
                 maximum_pressure, is_active)
            VALUES
                (@lineId, @scanCount, @intervalMinutes, @maximumPressure, @isActive)
            ON DUPLICATE KEY UPDATE
                required_scan_count = VALUES(required_scan_count),
                minimum_interval_minutes = VALUES(minimum_interval_minutes),
                maximum_pressure = VALUES(maximum_pressure),
                is_active = VALUES(is_active);
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@lineId", policy.LineId);
        command.Parameters.AddWithValue("@scanCount", policy.RequiredScanCount);
        command.Parameters.AddWithValue("@intervalMinutes", policy.MinimumIntervalMinutes);
        command.Parameters.AddWithValue("@maximumPressure", policy.MaximumPressure);
        command.Parameters.AddWithValue("@isActive", policy.IsActive);
        await command.ExecuteNonQueryAsync();
    }

    private static void Validate(ScanPolicy policy)
    {
        if (policy.LineId <= 0)
            throw new ArgumentException("Select a valid production line.");
        if (policy.RequiredScanCount is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(policy.RequiredScanCount),
                "Required scan count must be between 1 and 10.");
        if (policy.MinimumIntervalMinutes < 0)
            throw new ArgumentOutOfRangeException(nameof(policy.MinimumIntervalMinutes),
                "Minimum interval cannot be negative.");
        if (policy.MaximumPressure <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumPressure),
                "Maximum pressure must be greater than zero.");
    }
}
