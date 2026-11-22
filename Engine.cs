using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Monolith.Analysis;

internal sealed class Engine
{
    private readonly IAnalysis[] rules;
    private readonly AnalysisStore store;
    private readonly ILogger<Engine> log;

    public Engine(IEnumerable<IAnalysis> rules, AnalysisStore store, ILogger<Engine> log)
    {
        this.rules = rules.ToArray();
        this.store = store;
        this.log = log;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in this.rules)
            if (string.IsNullOrWhiteSpace(rule.Key) || !keys.Add(rule.Key) || rule.Version < 1
                || rule.Kinds.Count == 0 || rule.Kinds.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException($"Invalid analysis registration: {rule.Key}");
    }

    public int Tick(CancellationToken cancellation = default)
    {
        int saved = 0;
        foreach (var rule in rules)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var item in store.Pending(rule, 100))
            {
                cancellation.ThrowIfCancellationRequested();
                string? result = null;
                string? error = null;
                int? retryDelay = null;
                try
                {
                    using var input = JsonDocument.Parse(item.Payload);
                    result = rule.Run(input.RootElement).GetRawText();
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    error = ex.Message;
                    retryDelay = item.Attempts switch { 0 => 5, 1 => 10, _ => null };
                    log.LogWarning("Rule {Rule} failed for {Id}, attempt {Attempt}: {Error}",
                        rule.Key, item.Id, item.Attempts + 1, error);
                }
                if (store.Save(rule, item, result, error, retryDelay))
                    saved++;
            }
        }
        return saved;
    }
}
