using Microsoft.Data.Sqlite;

namespace Monolith.Storage;

internal sealed class Database(string path)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = path,
        DefaultTimeout = 5
    }.ToString();

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    public void Initialize(params string[] schemas)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        foreach (var schema in schemas)
        {
            command.CommandText = schema;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
