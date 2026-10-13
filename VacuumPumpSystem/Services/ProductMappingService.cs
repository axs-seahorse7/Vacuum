
using MySqlConnector;
using VacuumPumpSystem.Models;

namespace VacuumPumpSystem.Services;

public class ProductMappingService
{
    private readonly string _connectionString =
        "Server=localhost;" +
        "Port=3306;" +
        "Database=vacuum_pump;" +
        "User ID=root;" +
        "Password=mysql;";

    public async Task<ProductMapping?> GetActiveMappingByDummyAsync(
        string dummyCode)
    {
        const string sql = """
            SELECT
                pm.id,
                pm.machine_id,
                vm.dummy_code,
                pm.product_code,
                pm.mapped_at,
                pm.unmapped_at
            FROM product_mappings pm
            INNER JOIN vacuum_machines vm
                ON vm.id = pm.machine_id
            WHERE vm.dummy_code = @dummyCode
              AND pm.unmapped_at IS NULL
            ORDER BY pm.mapped_at DESC
            LIMIT 1;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@dummyCode", dummyCode.Trim());

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new ProductMapping
        {
            Id = reader.GetInt64("id"),
            MachineId = reader.GetInt32("machine_id"),
            DummyCode = reader.GetString("dummy_code"),
            ProductCode = reader.GetString("product_code"),
            MappedAt = reader.GetDateTime("mapped_at"),
            UnmappedAt = reader.IsDBNull(
                reader.GetOrdinal("unmapped_at"))
                ? null
                : reader.GetDateTime("unmapped_at")
        };
    }

    public async Task MapProductAsync(
        string dummyCode,
        string productCode)
    {
        dummyCode = dummyCode.Trim();
        productCode = productCode.Trim();

        if (string.IsNullOrWhiteSpace(dummyCode))
            throw new ArgumentException("Dummy code is required.");

        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("Product serial number is required.");

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        try
        {
            // Lock and validate the target machine.
            const string machineSql = """
                SELECT id
                FROM vacuum_machines
                WHERE dummy_code = @dummyCode
                FOR UPDATE;
                """;

            int machineId;

            await using (var machineCommand = new MySqlCommand(
                machineSql, connection, transaction))
            {
                machineCommand.Parameters.AddWithValue(
                    "@dummyCode", dummyCode);

                var result = await machineCommand.ExecuteScalarAsync();

                if (result == null || result == DBNull.Value)
                    throw new InvalidOperationException(
                        $"Machine '{dummyCode}' was not found.");

                machineId = Convert.ToInt32(result);
            }

            // Prevent a product from being actively mapped
            // to a different machine.
            const string duplicateSql = """
                SELECT machine_id
                FROM product_mappings
                WHERE product_code = @productCode
                  AND unmapped_at IS NULL
                LIMIT 1
                FOR UPDATE;
                """;

            await using (var duplicateCommand = new MySqlCommand(
                duplicateSql, connection, transaction))
            {
                duplicateCommand.Parameters.AddWithValue(
                    "@productCode", productCode);

                var result =
                    await duplicateCommand.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                {
                    int existingMachineId = Convert.ToInt32(result);

                    if (existingMachineId == machineId)
                        throw new InvalidOperationException(
                            "This product is already mapped to this machine.");

                    throw new InvalidOperationException(
                        "This product is already mapped to another machine.");
                }
            }

            // A machine can have only one active product.
            const string activeMachineSql = """
                SELECT id
                FROM product_mappings
                WHERE machine_id = @machineId
                  AND unmapped_at IS NULL
                LIMIT 1
                FOR UPDATE;
                """;

            await using (var activeCommand = new MySqlCommand(
                activeMachineSql, connection, transaction))
            {
                activeCommand.Parameters.AddWithValue(
                    "@machineId", machineId);

                var result = await activeCommand.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                    throw new InvalidOperationException(
                        "This machine already has an active product mapping. Unmap it first.");
            }

            const string insertSql = """
                INSERT INTO product_mappings
                    (machine_id, product_code)
                VALUES
                    (@machineId, @productCode);
                """;

            await using (var insertCommand = new MySqlCommand(
                insertSql, connection, transaction))
            {
                insertCommand.Parameters.AddWithValue(
                    "@machineId", machineId);

                insertCommand.Parameters.AddWithValue(
                    "@productCode", productCode);

                await insertCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
