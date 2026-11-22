using System.Text.Json;
using Monolith.Storage;

namespace Monolith.Feedback;

internal sealed class FeedbackRecords(Database database)
{
    public const string Schema = """
        CREATE TABLE IF NOT EXISTS feedback_record (
            id TEXT PRIMARY KEY NOT NULL,
            kind TEXT NOT NULL,
            revision INTEGER NOT NULL CHECK (revision > 0),
            payload TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS feedback_kind_id ON feedback_record(kind, id);
        CREATE VIEW IF NOT EXISTS analysis_input AS
            SELECT id, kind, revision, payload FROM feedback_record;
        """;

    public void Save(string id, string kind, JsonElement payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The payload must be a JSON object.");

        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO feedback_record(id, kind, revision, payload) VALUES ($id, $kind, 1, $payload)
            ON CONFLICT(id) DO UPDATE SET kind = excluded.kind, payload = excluded.payload,
                revision = feedback_record.revision + 1
            WHERE feedback_record.kind <> excluded.kind OR feedback_record.payload <> excluded.payload;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(payload));
        command.ExecuteNonQuery();
    }
}
