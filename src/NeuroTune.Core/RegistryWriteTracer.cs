using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace NeuroTune;

// F6: while a declared script runs, records the Registry keys written by processes this agent starts (and their
// children). Report only: the events do not carry the previous value, so nothing here can restore anything.
internal sealed class RegistryWriteTracer : IDisposable
{
    private static readonly Guid KernelRegistry = new("70EB4F03-C1DE-4F73-A051-33D13D5413BD");
    private static readonly Guid KernelProcess = new("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716");
    private readonly TraceEventSession _session;
    private readonly Task _processing;
    private readonly HashSet<int> _processes = [];
    private readonly Dictionary<ulong, string> _keyNames = [];
    private readonly SortedSet<string> _writes = new(StringComparer.OrdinalIgnoreCase);
    // One session name per process: concurrent tracers would replace each other's session, so they take turns.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private bool _released;

    internal RegistryWriteTracer()
    {
        Gate.Wait();
        _processes.Add(Environment.ProcessId);
        _session = new TraceEventSession($"NeuroTune-ScriptRegistry-{Environment.ProcessId}") { StopOnDispose = true };
        try
        {
            _session.EnableProvider(KernelProcess, TraceEventLevel.Informational, 0x10);
            _session.EnableProvider(KernelRegistry, TraceEventLevel.Verbose);
        }
        catch { _session.Dispose(); Gate.Release(); throw; }
        new RegisteredTraceEventParser(_session.Source).All += Handle;
        _processing = Task.Run(() => _session.Source.Process());
        Thread.Sleep(500); // Let the session start before the script does.
    }

    private void Handle(TraceEvent data)
    {
        lock (_writes)
        {
            if (data.ProviderGuid == KernelProcess)
            {
                if (data.EventName.StartsWith("ProcessStart", StringComparison.Ordinal) && Payload(data, "ParentProcessID") is { } parent &&
                    int.TryParse(parent, out var parentId) && _processes.Contains(parentId) && int.TryParse(Payload(data, "ProcessID"), out var child))
                    _processes.Add(child);
                return;
            }
            if (data.ProviderGuid != KernelRegistry || !_processes.Contains(data.ProcessID) || data.ProcessID == Environment.ProcessId) return;
            // The manifest exposes IDs only: 1 CreateKey, 2 OpenKey, 3 DeleteKey, 5 SetValueKey, 6 DeleteValueKey.
            var id = (int)data.ID;
            if (id is not (1 or 2 or 3 or 5 or 6)) return;
            var keyObject = ulong.TryParse(Payload(data, "KeyObject"), out var handle) ? handle : 0;
            var name = Payload(data, "KeyName") is { Length: > 0 } keyName ? keyName : Payload(data, "RelativeName") ?? "";
            if (id is 1 or 2)
            {
                if (keyObject != 0 && name.Length > 0) _keyNames[keyObject] = name;
                if (id == 2 || Payload(data, "Disposition") != "1") return; // 1 = REG_CREATED_NEW_KEY; opening an existing key writes nothing.
            }
            if (name.Length == 0 && keyObject != 0) _keyNames.TryGetValue(keyObject, out name!);
            var valueName = Payload(data, "ValueName") ?? "";
            if (valueName.StartsWith(@"\Device\", StringComparison.Ordinal)) return; // Windows BAM bookkeeping of the started executables.
            _writes.Add(Normalize(name ?? "") + (valueName.Length > 0 ? "\\" + valueName : ""));
        }
    }

    private static string? Payload(TraceEvent data, string name)
    {
        var index = data.PayloadIndex(name);
        return index < 0 ? null : data.PayloadValue(index)?.ToString();
    }

    // \REGISTRY\MACHINE\X -> HKLM\X, \REGISTRY\USER\<sid>\X -> HKU\<sid>\X; names are compared case-insensitively.
    internal static string Normalize(string key) =>
        key.StartsWith(@"\REGISTRY\MACHINE\", StringComparison.OrdinalIgnoreCase) ? @"HKLM\" + key[@"\REGISTRY\MACHINE\".Length..]
        : key.StartsWith(@"\REGISTRY\USER\", StringComparison.OrdinalIgnoreCase) ? @"HKU\" + key[@"\REGISTRY\USER\".Length..]
        : key;

    internal IReadOnlyList<string> Stop()
    {
        Thread.Sleep(1000); // Real-time buffers flush about once per second.
        _session.Stop();
        _processing.Wait(TimeSpan.FromSeconds(5));
        lock (_writes) return _writes.ToList();
    }

    public void Dispose()
    {
        _session.Dispose();
        if (!_released) { _released = true; Gate.Release(); }
    }
}
