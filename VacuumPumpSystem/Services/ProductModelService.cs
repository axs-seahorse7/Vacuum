
using MySqlConnector;
using VacuumPumpSystem.Models;

namespace VacuumPumpSystem.Services;

public sealed class ProductModelService
{
    private readonly string _connectionString = "Server=localhost;Port=3306;Database=vacuum_pump;User ID=root;Password=mysql;";

    public async Task<List<ProductModel>> GetAllAsync(bool activeOnly = false)
    {
        var models = new List<ProductModel>();

        const string sql = """
            SELECT id, model_name, serial_prefix, is_active
            FROM product_models
            WHERE (@activeOnly = 0 OR is_active = 1)
            ORDER BY model_name;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@activeOnly", activeOnly ? 1 : 0);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            models.Add(new ProductModel
            {
                Id = reader.GetInt32("id"),
                ModelName = reader.GetString("model_name"),
                SerialPrefix = reader.GetString("serial_prefix"),
                IsActive = reader.GetBoolean("is_active")
            });
        }

        return models;
    }

    public async Task SaveAsync(ProductModel model)
    {
        model.ModelName = model.ModelName.Trim();
        model.SerialPrefix = model.SerialPrefix.Trim();

        if (string.IsNullOrWhiteSpace(model.ModelName))
            throw new ArgumentException("Model name is required.");

        if (string.IsNullOrWhiteSpace(model.SerialPrefix))
            throw new ArgumentException("Serial prefix is required.");

        const string sql = """
            INSERT INTO product_models
                (model_name, serial_prefix, is_active)
            VALUES
                (@name, @prefix, @active)
            ON DUPLICATE KEY UPDATE
                serial_prefix = @prefix,
                is_active = @active;
            """;

        // Use an explicit update for existing models to avoid changing
        // the identity of a model through an upsert.
        if (model.Id > 0)
        {
            const string updateSql = """
                UPDATE product_models
                SET model_name = @name,
                    serial_prefix = @prefix,
                    is_active = @active
                WHERE id = @id;
                """;

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(updateSql, connection);
            command.Parameters.AddWithValue("@id", model.Id);
            command.Parameters.AddWithValue("@name", model.ModelName);
            command.Parameters.AddWithValue("@prefix", model.SerialPrefix);
            command.Parameters.AddWithValue("@active", model.IsActive);
            await command.ExecuteNonQueryAsync();
            return;
        }

        await using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@name", model.ModelName);
            command.Parameters.AddWithValue("@prefix", model.SerialPrefix);
            command.Parameters.AddWithValue("@active", model.IsActive);
            await command.ExecuteNonQueryAsync();
        }
    }
}
