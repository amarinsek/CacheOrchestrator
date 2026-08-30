using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Sample.Data;

/// <summary>
/// Thin SQLite-backed product store for the playground.
/// Labs 03–05 point both instances at the same file under <c>/shared</c>.
/// </summary>
public sealed class PlaygroundProductStore
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    public PlaygroundProductStore(IOptions<SampleSqliteOptions> options, IHostEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);

        string configured = options.Value.SqlitePath;
        if (string.IsNullOrWhiteSpace(configured))
            configured = "Data/playground.db";

        _dbPath = Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        _connectionString = builder.ConnectionString;
    }

    /// <summary>Creates the schema, enables WAL, and seeds demo products if missing.</summary>
    public async Task EnsureCreatedAndSeedAsync(CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (SqliteCommand create = connection.CreateCommand())
        {
            create.CommandText =
                """
                CREATE TABLE IF NOT EXISTS Products (
                    Id TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                    Name TEXT NOT NULL,
                    Price TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """;
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset seededAt = DateTimeOffset.UtcNow;
        await InsertIgnoreAsync(connection, "42", "Demo Widget", 10.00m, seededAt, cancellationToken)
            .ConfigureAwait(false);
        await InsertIgnoreAsync(connection, "7", "Sample Gadget", 19.50m, seededAt, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PlaygroundProduct?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, Price, UpdatedAt
            FROM Products
            WHERE Id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return ReadProduct(reader);
    }

    public async Task UpsertAsync(
        string id,
        string name,
        decimal price,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Products (Id, Name, Price, UpdatedAt)
            VALUES ($id, $name, $price, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Price = excluded.Price,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$price", price.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAt", updatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlaygroundProduct>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<PlaygroundProduct> items = [];
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, Price, UpdatedAt
            FROM Products
            ORDER BY Id COLLATE NOCASE;
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            items.Add(ReadProduct(reader));

        return items;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task InsertIgnoreAsync(
        SqliteConnection connection,
        string id,
        string name,
        decimal price,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO Products (Id, Name, Price, UpdatedAt)
            VALUES ($id, $name, $price, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$price", price.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAt", updatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static PlaygroundProduct ReadProduct(SqliteDataReader reader)
    {
        string id = reader.GetString(0);
        string name = reader.GetString(1);
        decimal price = decimal.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture);
        DateTimeOffset updatedAt = DateTimeOffset.Parse(
            reader.GetString(3),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        return new PlaygroundProduct(id, name, price, updatedAt);
    }
}
