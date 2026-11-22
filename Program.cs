using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monolith.Analysis;
using Monolith.Feedback;
using Monolith.Host;
using Monolith.Storage;

if (args is not ([] or ["seed"] or ["once"] or ["results"] or ["worker"] or ["put", _, _, _]))
{
    Console.Error.WriteLine("Commands: worker | seed | once | results | put <id> <kind> '<json>'");
    return 2;
}

var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders().AddSimpleConsole(options => options.SingleLine = true);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
var db = new Database(Environment.GetEnvironmentVariable("ANALYSIS_DB") ?? "analysis.db");
builder.Services.AddSingleton(db);
builder.Services.AddSingleton<FeedbackRecords>();
builder.Services.AddSingleton<AnalysisStore>();
builder.Services.AddSingleton<IAnalysis, WordCount>();
builder.Services.AddSingleton<IAnalysis, LowRating>();
builder.Services.AddSingleton<Engine>();
bool workerMode = args is [] or ["worker"];
if (workerMode)
{
    string seconds = Environment.GetEnvironmentVariable("ANALYSIS_INTERVAL_SECONDS") ?? "5";
    if (!int.TryParse(seconds, out int interval) || interval is < 1 or > 86400)
    {
        Console.Error.WriteLine("ANALYSIS_INTERVAL_SECONDS must be between 1 and 86400.");
        return 2;
    }
    builder.Services.AddSingleton(new PollingOptions(TimeSpan.FromSeconds(interval)));
    builder.Services.AddHostedService<AnalysisWorker>();
}

using var host = builder.Build();
db.Initialize(FeedbackRecords.Schema, AnalysisStore.Schema);
var feedback = host.Services.GetRequiredService<FeedbackRecords>();
switch (args)
{
    case ["seed"]:
        feedback.Save("R1", "review", JsonSerializer.SerializeToElement(new { text = "Waited too long", rating = 2 }));
        feedback.Save("T1", "ticket", JsonSerializer.SerializeToElement(new { text = "Missing confirmation email" }));
        break;
    case ["put", var id, var kind, var json]:
        try
        {
            using var document = JsonDocument.Parse(json);
            feedback.Save(id, kind, document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        break;
    case ["once"]:
        Console.WriteLine(host.Services.GetRequiredService<Engine>().Tick());
        break;
    case ["results"]:
        var store = host.Services.GetRequiredService<AnalysisStore>();
        foreach (var rule in host.Services.GetServices<IAnalysis>())
            foreach (var outcome in store.Current(rule))
                Console.WriteLine(outcome.GetRawText());
        break;
    default:
        await host.RunAsync();
        break;
}
return 0;
