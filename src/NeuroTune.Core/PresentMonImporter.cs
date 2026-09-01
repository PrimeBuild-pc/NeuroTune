using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.Text;

namespace NeuroTune;

public static class PresentMonImporter
{
    private const int MaxCsvBytes = 10 * 1024 * 1024;

    public static FrameTimeMetrics Parse(string csv, string processName)
    {
        if (string.IsNullOrWhiteSpace(csv) || Encoding.UTF8.GetByteCount(csv) > MaxCsvBytes)
            throw new InvalidOperationException("The PresentMon CSV is empty or larger than 10 MB.");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var parser = new TextFieldParser(stream, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidOperationException("The PresentMon CSV has no header.");
        var index = headers.Select((name, position) => (name: name.Trim(), position))
            .GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().position, StringComparer.OrdinalIgnoreCase);
        var frameColumn = Find(index, "MsBetweenPresents", "CPUFrameTime", "DisplayedTime");
        if (frameColumn < 0)
            throw new InvalidOperationException("The CSV does not contain a supported PresentMon frame-time column.");
        var processColumn = Find(index, "ProcessName", "Application");
        var modeColumn = Find(index, "PresentMode");
        var expectedProcess = Path.GetFileNameWithoutExtension(processName);
        var frames = new List<double>();
        var modes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null || frameColumn >= fields.Length) continue;
            if (processColumn >= 0 && processColumn < fields.Length &&
                !string.Equals(Path.GetFileNameWithoutExtension(fields[processColumn]), expectedProcess, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!double.TryParse(fields[frameColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds) ||
                !double.IsFinite(milliseconds) || milliseconds is < .05 or > 10_000)
                continue;
            frames.Add(milliseconds);
            if (modeColumn >= 0 && modeColumn < fields.Length && fields[modeColumn].Length is > 0 and <= 100)
                modes.Add(fields[modeColumn]);
        }
        if (frames.Count < 120)
            throw new InvalidOperationException("At least 120 valid frames for the selected executable are required.");
        frames.Sort();
        var mean = frames.Average();
        var p50 = Percentile(frames, .50);
        var p95 = Percentile(frames, .95);
        var p99 = Percentile(frames, .99);
        var stutterThreshold = Math.Max(50, p50 * 2);
        return new("PresentMon CSV (local aggregate)", frames.Count, frames.Sum(),
            1000 / mean, 1000 / p99, p50, p95, p99,
            frames.LongCount(value => value > stutterThreshold), modes.Order(StringComparer.OrdinalIgnoreCase).Take(20).ToList());
    }

    private static int Find(IReadOnlyDictionary<string, int> columns, params string[] names) =>
        names.Select(name => columns.TryGetValue(name, out var position) ? position : -1).FirstOrDefault(position => position >= 0, -1);

    private static double Percentile(IReadOnlyList<double> sorted, double percentile) =>
        sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Count), 1, sorted.Count) - 1];
}
