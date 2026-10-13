using BCrypt.Net;
using MySqlConnector;

namespace VacuumPumpSystem.Services;

public class UserService
{
    private readonly string _connectionString = "Server=localhost;Port=3306;Database=vacuum_pump;User ID=root;Password=mysql;";

    public async Task<List<UserModel>> GetUsersAsync()
    {
        var users = new List<UserModel>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT
                u.id,
                u.username,
                u.role,
                u.is_active,
                u.created_at,
                GROUP_CONCAT(pl.line_name ORDER BY pl.line_name SEPARATOR ', ') AS line_names
            FROM users u
            LEFT JOIN user_lines ul
                ON u.id = ul.user_id
            LEFT JOIN production_lines pl
                ON ul.line_id = pl.id
            GROUP BY
                u.id,
                u.username,
                u.role,
                u.is_active,
                u.created_at
            ORDER BY u.id;
            """;

        await using var command = new MySqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            users.Add(new UserModel
            {
                Id = reader.GetInt32("id"),
                Username = reader.GetString("username"),
                Role = reader.GetString("role"),
                IsActive = reader.GetBoolean("is_active"),
                CreatedAt = reader.GetDateTime("created_at"),
                LineNames = reader.IsDBNull(reader.GetOrdinal("line_names"))
                    ? ""
                    : reader.GetString("line_names")
            });
        }

        return users;
    }

   public async Task<List<ProductionLine>> GetActiveLinesAsync()
    {
        var lines = new List<ProductionLine>();

        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        const string sql = """
            SELECT
                id,
                line_code,
                line_name,
                description,
                is_active,
                created_at
            FROM production_lines
            WHERE is_active = TRUE
            ORDER BY line_name;
            """;

        await using var command =
            new MySqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            lines.Add(new ProductionLine
            {
                Id = reader.GetInt32("id"),
                LineCode = reader.GetString("line_code"),
                LineName = reader.GetString("line_name"),
                Description = reader.IsDBNull(
                    reader.GetOrdinal("description"))
                    ? null
                    : reader.GetString("description"),
                IsActive = reader.GetBoolean("is_active"),
                CreatedAt = reader.GetDateTime("created_at")
            });
        }

        return lines;
    }
    
    public async Task<int> CreateUserAsync(string username, string password, string role, List<int> lineIds)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

            const string userSql = """
                INSERT INTO users
                (
                    username,
                    password_hash,
                    role,
                    is_active
                )
                VALUES
                (
                    @username,
                    @passwordHash,
                    @role,
                    TRUE
                );

                SELECT LAST_INSERT_ID();
                """;

            int userId;

            await using (var command = new MySqlCommand(
                userSql,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue("@passwordHash", passwordHash);
                command.Parameters.AddWithValue("@role", role);

                userId = Convert.ToInt32(
                    await command.ExecuteScalarAsync());
            }

            const string lineSql = """
                INSERT INTO user_lines
                (
                    user_id,
                    line_id
                )
                VALUES
                (
                    @userId,
                    @lineId
                );
                """;

            foreach (int lineId in lineIds)
            {
                await using var command = new MySqlCommand(
                    lineSql,
                    connection,
                    transaction);

                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@lineId", lineId);

                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            return userId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task SetUserActiveAsync(int userId, bool isActive)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = """
            UPDATE users
            SET is_active = @isActive
            WHERE id = @userId;
            """;

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@isActive", isActive);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<AuthenticatedUser?> AuthenticateAsync(string username, string password)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT
                id,
                username,
                password_hash,
                role,
                is_active
            FROM users
            WHERE username = @username
            LIMIT 1;
            """;

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@username", username);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        bool isActive = reader.GetBoolean("is_active");

        if (!isActive)
            return null;

        string passwordHash = reader.GetString("password_hash");

        bool passwordValid =
            BCrypt.Net.BCrypt.Verify(password, passwordHash);

        if (!passwordValid)
            return null;

        return new AuthenticatedUser
        {
            Id = reader.GetInt32("id"),
            Username = reader.GetString("username"),
            Role = reader.GetString("role")
        };
    }

    public async Task<List<ProductionLine>> GetUserLinesAsync(int userId)
    {
        var lines = new List<ProductionLine>();

        await using var connection = new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        const string sql = """
            SELECT
                pl.id,
                pl.line_code,
                pl.line_name,
                pl.lora_channel,
                pl.description,
                pl.is_active,
                pl.created_at
            FROM user_lines ul
            INNER JOIN production_lines pl
                ON pl.id = ul.line_id
            WHERE ul.user_id = @userId
            AND pl.is_active = TRUE
            ORDER BY pl.line_name;
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            lines.Add(new ProductionLine
            {
                Id = reader.GetInt32("id"),
                LineCode = reader.GetString("line_code"),
                LineName = reader.GetString("line_name"),
                LoraChannel = reader.IsDBNull(reader.GetOrdinal("lora_channel"))? null : reader.GetInt32("lora_channel"),
                Description = reader.IsDBNull(reader.GetOrdinal("description"))? null : reader.GetString("description"),
                IsActive = reader.GetBoolean("is_active"),
                CreatedAt = reader.GetDateTime("created_at")
            });
        }

        return lines;
    }

    public async Task SeedAdminAsync()
    {
        await using var connection =
            new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        const string checkSql = """
            SELECT COUNT(*)
            FROM users
            WHERE username = 'admin';
            """;

        await using var checkCommand =
            new MySqlCommand(checkSql, connection);

        long count = Convert.ToInt64(
            await checkCommand.ExecuteScalarAsync());

        if (count > 0)
            return;

        string passwordHash =
            BCrypt.Net.BCrypt.HashPassword("admin123");

        const string insertSql = """
            INSERT INTO users
            (
                username,
                password_hash,
                role,
                is_active
            )
            VALUES
            (
                'admin',
                @passwordHash,
                'ADMIN',
                TRUE
            );
            """;

        await using var command =
            new MySqlCommand(insertSql, connection);

        command.Parameters.AddWithValue(
            "@passwordHash",
            passwordHash);

        await command.ExecuteNonQueryAsync();
    }

}

public class UserModel
{
    public int Id { get; set; }

    public string Username { get; set; } = "";

    public string Role { get; set; } = "";

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public string LineNames { get; set; } = "";
}


public class AuthenticatedUser
{
    public int Id { get; set; }

    public string Username { get; set; } = "";

    public string Role { get; set; } = "";
}

