using MySqlConnector;

namespace VacuumPumpSystem.Services;

public class MachineService
{
    private readonly string _connectionString;

    public MachineService()
    {
        _connectionString =
            "Server=localhost;" +
            "Port=3306;" +
            "Database=vacuum_pump;" +
            "User ID=root;" +
            "Password=mysql;";
    }

    // Check whether a Modbus address is already registered
    // inside a specific production line.
    public async Task<bool> IsModbusAddressRegisteredAsync(int lineId, byte modbusAddress)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT COUNT(*)
            FROM vacuum_machines
            WHERE line_id = @lineId
              AND modbus_address = @modbusAddress;
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@lineId", lineId);
        command.Parameters.AddWithValue("@modbusAddress", modbusAddress);
        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result) > 0;
    }

    // Register a machine under a specific production line.
    public async Task<string> RegisterMachineAsync(int lineId, byte modbusAddress, string machineName)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            
            machineName = machineName?.Trim() ?? throw new ArgumentNullException(nameof(machineName));

            if (string.IsNullOrWhiteSpace(machineName))
            {
                throw new ArgumentException("Machine name is required.", nameof(machineName));
            }

            const string duplicateNameSql = """
                SELECT COUNT(*)
                FROM vacuum_machines
                WHERE LOWER(TRIM(machine_name)) = LOWER(TRIM(@machineName));
                """;

            await using (var duplicateCommand = new MySqlCommand(duplicateNameSql, connection, transaction))
            {
                duplicateCommand.Parameters.AddWithValue("@machineName", machineName);
                int existingCount = Convert.ToInt32(await duplicateCommand.ExecuteScalarAsync());

                if (existingCount > 0)
                {
                    throw new InvalidOperationException($"Machine name '{machineName}' is already registered globally.");
                }
            }

            // Generate the next global Dummy Code.
            const string nextDummySql = """
                SELECT COALESCE( MAX(CAST(SUBSTRING(dummy_code, 3) AS UNSIGNED)), 0) + 1
                FROM vacuum_machines
                WHERE dummy_code LIKE 'DM%';
                """;

            await using var dummyCommand = new MySqlCommand(nextDummySql, connection, transaction);
            var result = await dummyCommand.ExecuteScalarAsync();
            int nextNumber = Convert.ToInt32(result);
            string dummyCode = $"DM{nextNumber}";

            // Insert machine.
            const string insertSql = """
                INSERT INTO vacuum_machines
                (
                    line_id,
                    dummy_code,
                    modbus_address,
                    machine_name,
                    current_pressure,
                    is_online,
                    last_seen_at
                )
                VALUES
                (
                    @lineId,
                    @dummyCode,
                    @modbusAddress,
                    @machineName,
                    NULL,
                    FALSE,
                    NULL
                );
                """;

            await using var insertCommand = new MySqlCommand(insertSql, connection, transaction);

            insertCommand.Parameters.AddWithValue("@lineId",lineId);
            insertCommand.Parameters.AddWithValue("@dummyCode",dummyCode);
            insertCommand.Parameters.AddWithValue("@modbusAddress",modbusAddress);
            insertCommand.Parameters.AddWithValue("@machineName",machineName);
            await insertCommand.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            return dummyCode;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<RegisteredMachine>>
        GetRegisteredMachinesAsync()
    {
        var machines =
            new List<RegisteredMachine>();

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        const string sql = """
            SELECT
                vm.id,
                vm.line_id,
                pl.line_code,
                pl.line_name,
                vm.dummy_code,
                vm.modbus_address,
                vm.machine_name,
                vm.current_pressure,
                vm.is_online,
                vm.last_seen_at
            FROM vacuum_machines vm
            INNER JOIN production_lines pl
                ON pl.id = vm.line_id
            ORDER BY vm.id;
            """;

        await using var command =
            new MySqlCommand(
                sql,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int idIndex =
                reader.GetOrdinal("id");

            int lineIdIndex =
                reader.GetOrdinal("line_id");

            int lineCodeIndex =
                reader.GetOrdinal("line_code");

            int lineNameIndex =
                reader.GetOrdinal("line_name");

            int dummyCodeIndex =
                reader.GetOrdinal("dummy_code");

            int modbusAddressIndex =
                reader.GetOrdinal("modbus_address");

            int machineNameIndex =
                reader.GetOrdinal("machine_name");

            int pressureIndex =
                reader.GetOrdinal("current_pressure");

            int onlineIndex =
                reader.GetOrdinal("is_online");

            int lastSeenIndex =
                reader.GetOrdinal("last_seen_at");

            machines.Add(new RegisteredMachine
            {
                Id =
                    reader.GetInt32(idIndex),

                LineId =
                    reader.GetInt32(lineIdIndex),

                LineCode =
                    reader.GetString(lineCodeIndex),

                LineName =
                    reader.GetString(lineNameIndex),

                DummyCode =
                    reader.GetString(dummyCodeIndex),

                ModbusAddress =
                    reader.GetByte(modbusAddressIndex),

                MachineName =
                    reader.IsDBNull(machineNameIndex)
                        ? ""
                        : reader.GetString(machineNameIndex),

                CurrentPressure =
                    reader.IsDBNull(pressureIndex)
                        ? null
                        : reader.GetDecimal(pressureIndex),

                IsOnline =
                    reader.GetBoolean(onlineIndex),

                LastSeen =
                    reader.IsDBNull(lastSeenIndex)
                        ? null
                        : reader.GetDateTime(lastSeenIndex)
            });
        }

        return machines;
    }

    
    
    public async Task<List<RegisteredMachine>> GetRegisteredMachinesAsync(int lineId)
    {
        var machines = new List<RegisteredMachine>();

        await using var connection = new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        const string sql = """
            SELECT
                vm.id,
                vm.line_id,
                pl.line_code,
                pl.line_name,
                vm.dummy_code,
                vm.modbus_address,
                vm.machine_name,
                vm.current_pressure,
                vm.is_online,
                vm.last_seen_at
            FROM vacuum_machines vm
            INNER JOIN production_lines pl
                ON pl.id = vm.line_id
            WHERE vm.line_id = @lineId
            ORDER BY vm.id;
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@lineId",lineId);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int idIndex = reader.GetOrdinal("id");
            int lineIdIndex = reader.GetOrdinal("line_id");
            int lineCodeIndex = reader.GetOrdinal("line_code");
            int lineNameIndex = reader.GetOrdinal("line_name");
            int dummyCodeIndex = reader.GetOrdinal("dummy_code");
            int modbusAddressIndex = reader.GetOrdinal("modbus_address");
            int machineNameIndex = reader.GetOrdinal("machine_name");
            int pressureIndex = reader.GetOrdinal("current_pressure");
            int onlineIndex = reader.GetOrdinal("is_online");
            int lastSeenIndex = reader.GetOrdinal("last_seen_at");

            machines.Add(new RegisteredMachine
            {
                Id = reader.GetInt32(idIndex),
                LineId = reader.GetInt32(lineIdIndex),
                LineCode = reader.GetString(lineCodeIndex),
                LineName = reader.GetString(lineNameIndex),
                DummyCode = reader.GetString(dummyCodeIndex),
                ModbusAddress = reader.GetByte(modbusAddressIndex),
                MachineName = reader.IsDBNull(machineNameIndex)? "": reader.GetString(machineNameIndex),
                CurrentPressure = reader.IsDBNull(pressureIndex)? null : reader.GetDecimal(pressureIndex),
                IsOnline = reader.GetBoolean(onlineIndex),
                LastSeen = reader.IsDBNull(lastSeenIndex)? null: reader.GetDateTime(lastSeenIndex)
            });
        }

        return machines;
    }
    public class RegisteredMachine
    {
        public int Id { get; set; }

        public int LineId { get; set; }

        public string LineCode { get; set; } = "";

        public string LineName { get; set; } = "";

        public string DummyCode { get; set; } = "";

        public byte ModbusAddress { get; set; }

        public string MachineName { get; set; } = "";

        public decimal? CurrentPressure { get; set; }

        public bool IsOnline { get; set; }

        public DateTime? LastSeen { get; set; }
    }
}