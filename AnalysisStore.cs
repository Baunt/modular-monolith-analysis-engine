using Microsoft.Data.Sqlite;
using Monolith.Storage;
using System.Text.Json;

namespace Monolith.Analysis;

internal sealed record WorkItem(string Id, long Revision, string Payload, int Attempts);

internal sealed class AnalysisStore(Database database)
{
    public const string Schema = """
        CREATE TABLE IF NOT EXISTS analysis_outcome (
            record_id TEXT NOT NULL, revision INTEGER NOT NULL,
            analysis_key TEXT NOT NULL, version INTEGER NOT NULL,
            result TEXT, error TEXT, attempts INTEGER NOT NULL, retry_at INTEGER,
            PRIMARY KEY (record_id, revision, analysis_key, version),
            CHECK ((result IS NULL) <> (error IS NULL))
        );
        """;

    private static string Scope(SqliteCommand command, IAnalysis rule)
    {
        command.Parameters.AddWithValue("$key", rule.Key);
        command.Parameters.AddWithValue("$version", rule.Version);
        var kinds = new List<string>();
        foreach (string kind in rule.Kinds)
        {
            string name = "$kind" + kinds.Count;
            kinds.Add(name);
            command.Parameters.AddWithValue(name, kind);
        }
        return $"""
            FROM analysis_input i LEFT JOIN analysis_outcome o
              ON o.record_id = i.id AND o.revision = i.revision
             AND o.analysis_key = $key AND o.version = $version
            WHERE i.kind IN ({string.Join(", ", kinds)})
            """;
    }

    public List<WorkItem> Pending(IAnalysis rule, int limit)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT i.id, i.revision, i.payload, coalesce(o.attempts, 0) "
            + Scope(cmd, rule)
            + " AND (o.record_id IS NULL OR o.retry_at <= unixepoch()) ORDER BY i.kind, i.id LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        using var rows = cmd.ExecuteReader();
        var batch = new List<WorkItem>();
        while (rows.Read())
            batch.Add(new(rows.GetString(0), rows.GetInt64(1), rows.GetString(2), rows.GetInt32(3)));
        return batch;
    }

    public bool Save(IAnalysis rule, WorkItem item, string? result, string? error, int? retryDelay)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        // Recheck the source revision and attempt number in the same write.
        cmd.CommandText = """
            INSERT INTO analysis_outcome(record_id, revision, analysis_key, version, result, error, attempts, retry_at)
            SELECT id, revision, $key, $version, $result, $error, $attempts, unixepoch() + $delay
            FROM analysis_input WHERE id = $id AND revision = $revision
            ON CONFLICT(record_id, revision, analysis_key, version) DO UPDATE SET
                result = excluded.result, error = excluded.error,
                attempts = excluded.attempts, retry_at = excluded.retry_at
            WHERE analysis_outcome.result IS NULL AND analysis_outcome.attempts = excluded.attempts - 1;
            """;
        cmd.Parameters.AddWithValue("$key", rule.Key);
        cmd.Parameters.AddWithValue("$version", rule.Version);
        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$revision", item.Revision);
        cmd.Parameters.AddWithValue("$attempts", item.Attempts + 1);
        cmd.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$delay", (object?)retryDelay ?? DBNull.Value);
        return cmd.ExecuteNonQuery() == 1;
    }

    public IEnumerable<JsonElement> Current(IAnalysis rule)
    {
        using var connection = database.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT i.id, i.kind, i.revision, o.result, o.error, o.attempts, o.retry_at "
            + Scope(cmd, rule) + " AND o.record_id IS NOT NULL ORDER BY i.id";
        using var rows = cmd.ExecuteReader();
        while (rows.Read())
            yield return JsonSerializer.SerializeToElement(new
            {
                id = rows.GetString(0), kind = rows.GetString(1), revision = rows.GetInt64(2),
                analysis = rule.Key, version = rule.Version,
                result = rows.IsDBNull(3) ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(rows.GetString(3)),
                error = rows.IsDBNull(4) ? null : rows.GetString(4),
                attempts = rows.GetInt32(5), retry_at = rows.IsDBNull(6) ? (long?)null : rows.GetInt64(6)
            });
    }
}
