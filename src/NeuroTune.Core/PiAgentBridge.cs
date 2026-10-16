using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

// Pi is a short-lived non-admin child; credentials and provider requests stay in the host.
internal static class PiAgentBridge
{
    internal const string MissingRuntime = "The bundled Pi agent runtime is missing. Rebuild/reinstall NeuroTune; no alternate agent was used.";

    internal static string StartupFailureReason(Exception error)
    {
        // Only local literals and numeric OS codes are safe to export; stderr can contain paths or secrets.
        var cause = error switch
        {
            InvalidOperationException when error.Message == MissingRuntime => MissingRuntime,
            InvalidOperationException when error.Message == OptionalWorkerProcess.TokenUnavailable => OptionalWorkerProcess.TokenUnavailable,
            OptionalWorkerProcess.ExitException exited => $"The bundled Node/Pi worker exited before its first turn (exit 0x{exited.ExitCode:X8}). Check the complete NeuroTune installation and Windows security logs.",
            Win32Exception windows => $"Windows could not start/control the bundled Node/Pi worker (Win32 {windows.NativeErrorCode}). Check installation permissions and Windows security logs.",
            OperationCanceledException => "The bundled Node/Pi worker did not request its first turn before the investigation deadline.",
            UnauthorizedAccessException => "Access to the bundled Pi runtime was denied. Check installation permissions and Windows security logs.",
            IOException => "The bundled Pi runtime could not be read or its path was refused. Use a complete NeuroTune installation in a non-linked directory.",
            _ => "The bundled Node/Pi bridge failed before its first turn. Rebuild/reinstall NeuroTune and report this startup failure."
        };
        return $"Pi agent startup failed; no model request was sent by the investigation agent. {cause} Installing global Pi or changing API keys/models does not repair the bundled runtime. No alternate agent was used and nothing was applied.";
    }

    internal static async Task RunAsync(int maxTurns, Func<int, Task<string>> generate,
        Func<int, string, Task<bool>> handle, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "pi");
        var executable = Path.Combine(directory, "node.exe");
        var script = Path.Combine(directory, "agent.mjs");
        SystemOneService.CheckDirectory(directory);
        if (!File.Exists(executable) || !File.Exists(script))
            throw new InvalidOperationException(MissingRuntime);
        foreach (var path in new[] { executable, script })
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked Pi runtime files are not accepted.");
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP" })
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        await OptionalWorkerProcess.RunAsync(executable, [script], directory, "", environment,
            TimeSpan.FromMinutes(31), null, cancellationToken,
            (reader, writer, token) => ExchangeAsync(reader, writer, maxTurns, generate, handle, token));
    }

    internal static async Task<string> ExchangeAsync(StreamReader reader, StreamWriter writer, int maxTurns,
        Func<int, Task<string>> generate, Func<int, string, Task<bool>> handle, CancellationToken cancellationToken)
    {
        await WriteAsync(new { maxTurns });
        var turn = 0; var expectedId = 1; var awaitingHandle = false; var finished = false;
        string? pendingResponse = null;
        while (true)
        {
            var line = await ReadFrameAsync(reader, cancellationToken);
            using var frame = JsonDocument.Parse(line);
            var root = frame.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "done")
            {
                if (awaitingHandle || (!finished && turn != maxTurns)) throw new InvalidOperationException("Pi exited before completing the investigation.");
                writer.Close();
                return "";
            }
            if (finished || root.GetProperty("id").GetInt32() != expectedId++) throw new InvalidOperationException("Invalid Pi request correlation.");
            var requestedTurn = root.GetProperty("turn").GetInt32();
            if (type == "generate" && !awaitingHandle && requestedTurn == turn + 1 && requestedTurn <= maxTurns)
            {
                turn = requestedTurn; awaitingHandle = true;
                pendingResponse = await generate(turn);
                await WriteAsync(new { id = expectedId - 1, response = pendingResponse });
            }
            else if (type == "handle" && awaitingHandle && requestedTurn == turn)
            {
                var response = root.GetProperty("response").GetString() ?? throw new InvalidOperationException("Missing Pi envelope.");
                if (response != pendingResponse) throw new InvalidOperationException("Pi changed the provider envelope.");
                finished = await handle(turn, response); awaitingHandle = false; pendingResponse = null;
                await WriteAsync(new { id = expectedId - 1, finished });
            }
            else throw new InvalidOperationException("Invalid Pi turn sequence or investigation budget.");
        }
        async Task WriteAsync(object value)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
    }

    private static async Task<string> ReadFrameAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder(); var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer[0] == '\n') return text.ToString();
            if (text.Length >= 2_000_000) throw new InvalidOperationException("Pi frame exceeds its safety limit.");
            text.Append(buffer[0]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Pi closed its protocol before completion.");
    }
}
