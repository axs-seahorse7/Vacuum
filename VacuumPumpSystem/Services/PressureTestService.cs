
using MySqlConnector;

namespace VacuumPumpSystem.Services;

public class PressureTestService
{
    private readonly string _connectionString =
        "Server=localhost;" +
        "Port=3306;" +
        "Database=vacuum_pump;" +
        "User ID=root;" +
        "Password=mysql;";

    public async Task SaveTestAsync(
        int lineId,
        int machineId,
        string dummyCode,
        string productSerialNo,
        double pressure)
    {
        if (string.IsNullOrWhiteSpace(dummyCode))
            throw new ArgumentException("Dummy code is required.");

        if (string.IsNullOrWhiteSpace(productSerialNo))
            throw new ArgumentException("Product serial number is required.");

        if (double.IsNaN(pressure) ||
            double.IsInfinity(pressure) ||
            pressure < 0)
            throw new ArgumentException("Pressure reading is invalid.");

        const string sql = """
            INSERT INTO pressure_test_records
                (line_id, machine_id, dummy_code,
                 product_serial_no, pressure_value, tested_at)
            SELECT
                vm.line_id, vm.id, vm.dummy_code,
                @serialNo, @pressure, CURRENT_TIMESTAMP
            FROM vacuum_machines vm
            WHERE vm.id = @machineId
              AND vm.line_id = @lineId
              AND vm.dummy_code = @dummyCode;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@lineId", lineId);
        command.Parameters.AddWithValue("@machineId", machineId);
        command.Parameters.AddWithValue("@dummyCode", dummyCode.Trim());
        command.Parameters.AddWithValue("@serialNo", productSerialNo.Trim());
        command.Parameters.AddWithValue("@pressure", pressure);

        int affected = await command.ExecuteNonQueryAsync();

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "The machine was not found on the selected production line. No test record was saved.");
        }
    }

    public async Task<List<PressureTestRecord>> GetRecentTestsAsync(
        int lineId,
        int limit = 10)
    {
        limit = Math.Clamp(limit, 1, 100);

        var records = new List<PressureTestRecord>();

        const string sql = """
            SELECT id, line_id, machine_id, dummy_code,
                   product_serial_no, pressure_value, tested_at
            FROM pressure_test_records
            WHERE line_id = @lineId
            ORDER BY tested_at DESC, id DESC
            LIMIT @limit;
            """;

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        await using var command =
            new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@lineId", lineId);
        command.Parameters.AddWithValue("@limit", limit);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            records.Add(new PressureTestRecord
            {
                Id = reader.GetInt64("id"),
                LineId = reader.GetInt32("line_id"),
                MachineId = reader.GetInt32("machine_id"),
                DummyCode = reader.GetString("dummy_code"),
                ProductSerialNo = reader.GetString("product_serial_no"),
                PressureValue = reader.GetDecimal("pressure_value"),
                TestedAt = reader.GetDateTime("tested_at")
            });
        }

        return records;
    }
}

public class PressureTestRecord
{
    public long Id { get; set; }
    public int LineId { get; set; }
    public int MachineId { get; set; }
    public string DummyCode { get; set; } = string.Empty;
    public string ProductSerialNo { get; set; } = string.Empty;
    public decimal PressureValue { get; set; }
    public DateTime TestedAt { get; set; }
}
