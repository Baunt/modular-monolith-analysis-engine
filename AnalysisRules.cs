using System.Text.Json;

namespace Monolith.Analysis;

internal interface IAnalysis
{
    string Key { get; }
    int Version { get; }
    IReadOnlyList<string> Kinds { get; }
    JsonElement Run(JsonElement input);
}

internal sealed class WordCount : IAnalysis
{
    public string Key => "word_count";
    public int Version => 1;
    public IReadOnlyList<string> Kinds { get; } = ["review", "ticket"];

    public JsonElement Run(JsonElement input)
    {
        string text = input.GetProperty("text").GetString()
            ?? throw new FormatException("Expected a text string.");
        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return JsonSerializer.SerializeToElement(new { words });
    }
}

internal sealed class LowRating : IAnalysis
{
    public string Key => "low_rating";
    public int Version => 1;
    public IReadOnlyList<string> Kinds { get; } = ["review"];

    public JsonElement Run(JsonElement input)
    {
        int rating = input.GetProperty("rating").GetInt32();
        if (rating is < 1 or > 5)
            throw new FormatException("Expected a rating from 1 to 5.");
        return JsonSerializer.SerializeToElement(new { low_rating = rating <= 2 });
    }
}
