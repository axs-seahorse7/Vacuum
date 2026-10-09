
using MySqlConnector;

namespace VacuumPumpSystem.Services;

public class LineService
{
    private readonly string _connectionString;

    public LineService()
    {
        _connectionString =
            "Server=localhost;" +
            "Port=3306;" +
            "Database=vacuum_pump;" +
            "User ID=root;" +
            "Password=mysql;";
    }

    public async Task<List<ProductionLine>> GetLinesAsync()
    {
        var lines = new List<ProductionLine>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT id, line_code, line_name, lora_channel,
                   description, is_active, created_at
            FROM production_lines
            ORDER BY id;
            """;

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int channelIndex = reader.GetOrdinal("lora_channel");
            int descriptionIndex = reader.GetOrdinal("description");

            lines.Add(new ProductionLine
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                LineCode = reader.GetString(reader.GetOrdinal("line_code")),
                LineName = reader.GetString(reader.GetOrdinal("line_name")),
                LoraChannel = reader.IsDBNull(channelIndex)
                    ? null
                    : reader.GetInt32(channelIndex),
                Description = reader.IsDBNull(descriptionIndex)
                    ? ""
                    : reader.GetString(descriptionIndex),
                IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
            });
        }

        return lines;
    }

    public async Task<int> CreateLineAsync(
        string lineCode,
        string lineName,
        string? description)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = """
            INSERT INTO production_lines (line_code, line_name, description)
            VALUES (@lineCode, @lineName, @description);
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@lineCode", lineCode);
        command.Parameters.AddWithValue("@lineName", lineName);
        command.Parameters.AddWithValue(
            "@description",
            string.IsNullOrWhiteSpace(description) ? DBNull.Value : description);

        await command.ExecuteNonQueryAsync();
        return Convert.ToInt32(command.LastInsertedId);
    }

    public async Task UpdateLineAsync(
        int lineId,
        string lineCode,
        string lineName,
        string? description)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = """
            UPDATE production_lines
            SET line_code = @lineCode,
                line_name = @lineName,
                description = @description
            WHERE id = @lineId;
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@lineId", lineId);
        command.Parameters.AddWithValue("@lineCode", lineCode);
        command.Parameters.AddWithValue("@lineName", lineName);
        command.Parameters.AddWithValue(
            "@description",
            string.IsNullOrWhiteSpace(description) ? DBNull.Value : description);

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateLineChannelAsync(int lineId, int? channel)
    {
        if (channel is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(channel));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = """
            UPDATE production_lines
            SET lora_channel = @channel
            WHERE id = @lineId;
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@lineId", lineId);
        command.Parameters.AddWithValue(
            "@channel",
            channel.HasValue ? channel.Value : DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task SetLineActiveAsync(int lineId, bool isActive)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = """
            UPDATE production_lines
            SET is_active = @isActive
            WHERE id = @lineId;
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@lineId", lineId);
        command.Parameters.AddWithValue("@isActive", isActive);

        await command.ExecuteNonQueryAsync();
    }
}

public class ProductionLine
{
    public int Id { get; set; }
    public string LineCode { get; set; } = "";
    public string LineName { get; set; } = "";
    public int? LoraChannel { get; set; }
    public string Description { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public override string ToString()
    {
        return $"{LineName} ({LineCode})";
    }
}
