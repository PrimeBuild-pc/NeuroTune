using System.Text.Json;
using NeuroTune;

if (!args.Contains("--accept-disposable-vm", StringComparer.Ordinal))
    throw new InvalidOperationException("This probe changes system state briefly. Run it only in a disposable VM with --accept-disposable-vm.");

var requested = args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToList();
var catalog = new OptimizationCatalog();
var actions = requested.Count == 0 ? catalog.All.ToList() : requested.Select(catalog.Get).ToList();
var results = new List<ProbeResult>();

foreach (var action in actions)
{
    var captured = "";
    try
    {
        var before = action.Inspect();
        captured = action.Capture();
        action.Apply();
        if (!action.Verify()) throw new InvalidOperationException("Apply verification failed.");
        action.Restore(captured);
        if (!string.Equals(action.Capture(), captured, StringComparison.Ordinal))
            throw new InvalidOperationException("Restored state does not match the exact captured state.");
        results.Add(new(action.Id, "Passed", before.CurrentValue, null));
    }
    catch (Exception exception)
    {
        if (captured.Length > 0)
        {
            try { action.Restore(captured); }
            catch (Exception restoreException) { exception = new AggregateException(exception, restoreException); }
        }
        results.Add(new(action.Id, "Failed", null, exception.Message));
    }
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    os = Environment.OSVersion.VersionString,
    architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
    results
}, new JsonSerializerOptions { WriteIndented = true }));
return results.Any(result => result.Status == "Failed") ? 1 : 0;

internal sealed record ProbeResult(string ActionId, string Status, string? OriginalState, string? Error);
