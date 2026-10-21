using MySqlConnector;

namespace VacuumPumpSystem.Services;

public sealed class SerialScanResult
{
    public int AcceptedScanCount { get; init; }
    public int RequiredScanCount { get; init; }
    public bool IsCompleted { get; init; }
    public decimal Pressure { get; init; }
    public decimal MaximumPressure { get; init; }
    public DateTime ScannedAt { get; init; }
}

public sealed class SerialScanService
{
    // Matches the current local connection convention in PressureTestService.
    private readonly string _connectionString =
        "Server=localhost;Port=3306;Database=vacuum_pump;User ID=root;Password=mysql;";

    public async Task<SerialScanResult> RecordScanAsync(
        int lineId, int machineId, string dummyCode, string productSerial, decimal pressure)
    {
        if (lineId <= 0) throw new ArgumentOutOfRangeException(nameof(lineId));
        if (machineId <= 0) throw new ArgumentOutOfRangeException(nameof(machineId));
        if (string.IsNullOrWhiteSpace(dummyCode)) throw new ArgumentException("Dummy code is required.");
        if (string.IsNullOrWhiteSpace(productSerial)) throw new ArgumentException("Product serial is required.");
        if (pressure < 0) throw new ArgumentOutOfRangeException(nameof(pressure), "Pressure cannot be negative.");

        dummyCode = dummyCode.Trim();
        productSerial = productSerial.Trim();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {

            // A product serial must belong to only one production line.
            const string serialLineSql = """
                SELECT line_id
                FROM serial_scan_sessions
                WHERE product_serial = @serial
                LIMIT 1
                FOR UPDATE;
                """;

            await using (var command = new MySqlCommand(
                serialLineSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@serial", productSerial);

                object? existingLine = await command.ExecuteScalarAsync();

                if (existingLine != null && existingLine != DBNull.Value)
                {
                    int existingLineId = Convert.ToInt32(existingLine);

                    if (existingLineId != lineId)
                    {
                        throw new InvalidOperationException(
                            $"Serial '{productSerial}' is already assigned to another production line.");
                    }
                }
            }

            const string policySql = """
                SELECT required_scan_count, minimum_interval_minutes, maximum_pressure
                FROM line_scan_policies
                WHERE line_id = @lineId AND is_active = 1
                FOR UPDATE;
                """;

            int currentRequired, currentInterval;
            decimal currentMaximum;

            await using (var command = new MySqlCommand(policySql, connection, transaction))
            {
                command.Parameters.AddWithValue("@lineId", lineId);
                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("Scanning is disabled: this line has no active scan policy.");
                currentRequired = reader.GetInt32("required_scan_count");
                currentInterval = reader.GetInt32("minimum_interval_minutes");
                currentMaximum = reader.GetDecimal("maximum_pressure");
            }

            if (pressure >= currentMaximum)
                throw new InvalidOperationException(
                    $"Pressure rejected: {pressure:0.######} must be below {currentMaximum:0.######}.");

            const string machineSql = """
                SELECT id FROM vacuum_machines
                WHERE id = @machineId AND line_id = @lineId AND dummy_code = @dummyCode
                FOR UPDATE;
                """;
            await using (var command = new MySqlCommand(machineSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@machineId", machineId);
                command.Parameters.AddWithValue("@lineId", lineId);
                command.Parameters.AddWithValue("@dummyCode", dummyCode);
                if (await command.ExecuteScalarAsync() is null)
                    throw new InvalidOperationException("Machine/dummy is not registered on this production line.");
            }

            // Requires UNIQUE(line_id, product_serial), added by migration 010.
            const string createSessionSql = """
                INSERT IGNORE INTO serial_scan_sessions
                    (line_id, product_serial, required_scan_count, minimum_interval_minutes,
                     maximum_pressure, accepted_scan_count, status, started_at, created_at, updated_at)
                VALUES
                    (@lineId, @serial, @required, @interval, @maximum,
                     0, 'IN_PROGRESS', CURRENT_TIMESTAMP(3), CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                """;
            await using (var command = new MySqlCommand(createSessionSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@lineId", lineId);
                command.Parameters.AddWithValue("@serial", productSerial);
                command.Parameters.AddWithValue("@required", currentRequired);
                command.Parameters.AddWithValue("@interval", currentInterval);
                command.Parameters.AddWithValue("@maximum", currentMaximum);
                await command.ExecuteNonQueryAsync();
            }

            const string sessionSql = """
                SELECT id, required_scan_count, minimum_interval_minutes, maximum_pressure,
                       accepted_scan_count, status, last_scanned_at
                FROM serial_scan_sessions
                WHERE line_id = @lineId AND product_serial = @serial
                FOR UPDATE;
                """;

            long sessionId;
            int requiredCount, minimumInterval, acceptedCount;
            decimal sessionMaximum;
            string status;
            DateTime? lastScannedAt;

            await using (var command = new MySqlCommand(sessionSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@lineId", lineId);
                command.Parameters.AddWithValue("@serial", productSerial);
                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("Could not create or load the serial session.");
                sessionId = reader.GetInt64("id");
                requiredCount = reader.GetInt32("required_scan_count");
                minimumInterval = reader.GetInt32("minimum_interval_minutes");
                sessionMaximum = reader.GetDecimal("maximum_pressure");
                acceptedCount = reader.GetInt32("accepted_scan_count");
                status = reader.GetString("status");
                lastScannedAt = reader.IsDBNull(reader.GetOrdinal("last_scanned_at"))
                    ? null : reader.GetDateTime("last_scanned_at");
            }

            
            const string firstScanSql = """
                SELECT machine_id, dummy_code
                FROM serial_scan_events
                WHERE session_id = @sessionId
                ORDER BY scan_number ASC
                LIMIT 1;
                """;

            await using (var command = new MySqlCommand(
                firstScanSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@sessionId", sessionId);

                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    int firstMachineId = reader.GetInt32("machine_id");
                    string firstDummyCode = reader.GetString("dummy_code");

                    if (firstMachineId != machineId ||
                        !string.Equals(
                            firstDummyCode,
                            dummyCode,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Serial '{productSerial}' is already bound to machine " +
                            $"'{firstDummyCode}'. Scan it using that same machine.");
                    }
                }
            }


            if (status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                acceptedCount >= requiredCount)
                throw new InvalidOperationException(
                    $"Serial '{productSerial}' has already completed its {requiredCount} required scan(s).");

            // Existing serials continue using the policy snapshot from their first accepted scan.
            if (pressure >= sessionMaximum)
                throw new InvalidOperationException(
                    $"Pressure rejected: {pressure:0.######} must be below this serial's saved limit {sessionMaximum:0.######}.");

            DateTime databaseNow = await GetDatabaseNowAsync(connection, transaction);
            if (lastScannedAt.HasValue && minimumInterval > 0)
            {
                DateTime nextAllowed = lastScannedAt.Value.AddMinutes(minimumInterval);
                if (databaseNow < nextAllowed)
                {
                    var remaining = nextAllowed - databaseNow;
                    throw new InvalidOperationException(
                        $"Minimum scan interval not met. Try again in about {Math.Ceiling(remaining.TotalSeconds)} second(s).");
                }
            }

            int scanNumber = acceptedCount + 1;
            bool completed = scanNumber >= requiredCount;
            DateTime scannedAt = databaseNow;

            const string eventSql = """
                INSERT INTO serial_scan_events
                    (session_id, scan_number, machine_id, dummy_code, pressure_value, scanned_at)
                VALUES (@sessionId, @scanNumber, @machineId, @dummyCode, @pressure, @scannedAt);
                """;
            await using (var command = new MySqlCommand(eventSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@sessionId", sessionId);
                command.Parameters.AddWithValue("@scanNumber", scanNumber);
                command.Parameters.AddWithValue("@machineId", machineId);
                command.Parameters.AddWithValue("@dummyCode", dummyCode);
                command.Parameters.AddWithValue("@pressure", pressure);
                command.Parameters.AddWithValue("@scannedAt", scannedAt);
                await command.ExecuteNonQueryAsync();
            }

            // Preserve the app's existing recent-scan/reporting history in this same transaction.
            const string historySql = """
                INSERT INTO pressure_test_records
                    (line_id, machine_id, dummy_code, product_serial_no, pressure_value, tested_at)
                VALUES (@lineId, @machineId, @dummyCode, @serial, @pressure, @scannedAt);
                """;
            await using (var command = new MySqlCommand(historySql, connection, transaction))
            {
                command.Parameters.AddWithValue("@lineId", lineId);
                command.Parameters.AddWithValue("@machineId", machineId);
                command.Parameters.AddWithValue("@dummyCode", dummyCode);
                command.Parameters.AddWithValue("@serial", productSerial);
                command.Parameters.AddWithValue("@pressure", pressure);
                command.Parameters.AddWithValue("@scannedAt", scannedAt);
                await command.ExecuteNonQueryAsync();
            }

            const string updateSql = """
                UPDATE serial_scan_sessions
                SET accepted_scan_count = @count, last_scanned_at = @scannedAt,
                    status = @status, completed_at = @completedAt, updated_at = CURRENT_TIMESTAMP
                WHERE id = @sessionId;
                """;
            await using (var command = new MySqlCommand(updateSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@count", scanNumber);
                command.Parameters.AddWithValue("@scannedAt", scannedAt);
                command.Parameters.AddWithValue("@status", completed ? "COMPLETED" : "IN_PROGRESS");
                command.Parameters.AddWithValue("@completedAt", completed ? scannedAt : DBNull.Value);
                command.Parameters.AddWithValue("@sessionId", sessionId);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return new SerialScanResult
            {
                AcceptedScanCount = scanNumber,
                RequiredScanCount = requiredCount,
                IsCompleted = completed,
                Pressure = pressure,
                MaximumPressure = sessionMaximum,
                ScannedAt = scannedAt
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<DateTime> GetDatabaseNowAsync(
        MySqlConnection connection, MySqlTransaction transaction)
    {
        await using var command = new MySqlCommand("SELECT CURRENT_TIMESTAMP(3);", connection, transaction);
        return Convert.ToDateTime(await command.ExecuteScalarAsync());
    }
}
