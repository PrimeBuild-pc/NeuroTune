using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NeuroTune;

// Non-admin, short-lived worker, not a filesystem sandbox. All descendants die with its owner/job.
internal static class OptionalWorkerProcess
{
    internal static async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments, string directory,
        string input, IReadOnlyDictionary<string, string> environment, TimeSpan timeout, Action<string>? progress, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var job = new SafeFileHandle(CreateJobObject(IntPtr.Zero, null), true);
        Check(!job.IsInvalid);
        var limits = new JobLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE.
        Check(SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<JobLimits>()));
        using var token = NonAdminToken();
        var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
        Check(CreatePipe(out var stdinRead, out var stdinWrite, ref security, 0));
        using (stdinRead) using (stdinWrite)
        {
            Check(CreatePipe(out var stdoutRead, out var stdoutWrite, ref security, 0));
            using (stdoutRead) using (stdoutWrite)
            {
                Check(CreatePipe(out var stderrRead, out var stderrWrite, ref security, 0));
                using (stderrRead) using (stderrWrite)
                {
                    Check(SetHandleInformation(stdinWrite, 1, 0)); Check(SetHandleInformation(stdoutRead, 1, 0)); Check(SetHandleInformation(stderrRead, 1, 0));
                    var attributeSize = IntPtr.Zero;
                    InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
                    var attributes = Marshal.AllocHGlobal(attributeSize);
                    var handles = Marshal.AllocHGlobal(3 * IntPtr.Size);
                    var startup = new StartupInfoEx
                    {
                        Info = new StartupInfo
                        {
                            Size = Marshal.SizeOf<StartupInfoEx>(),
                            Flags = 0x100,
                            Input = stdinRead.DangerousGetHandle(),
                            Output = stdoutWrite.DangerousGetHandle(),
                            Error = stderrWrite.DangerousGetHandle()
                        },
                        Attributes = attributes
                    };
                    var command = new StringBuilder(string.Join(' ', new[] { executable }.Concat(arguments).Select(Quote)));
                    var block = Marshal.StringToHGlobalUni(string.Join('\0', environment.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => $"{item.Key}={item.Value}")) + "\0\0");
                    ProcessInfo info; var attributesInitialized = false;
                    try
                    {
                        Check(InitializeProcThreadAttributeList(attributes, 1, 0, ref attributeSize)); attributesInitialized = true;
                        Marshal.WriteIntPtr(handles, 0, startup.Info.Input); Marshal.WriteIntPtr(handles, IntPtr.Size, startup.Info.Output); Marshal.WriteIntPtr(handles, 2 * IntPtr.Size, startup.Info.Error);
                        Check(UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x20002, handles, (IntPtr)(3 * IntPtr.Size), IntPtr.Zero, IntPtr.Zero));
                        Check(token is null
                            ? CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08080404, block, directory, ref startup, out info)
                            : CreateProcessAsUser(token, executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08080404, block, directory, ref startup, out info));
                    }
                    finally { if (attributesInitialized) DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); Marshal.FreeHGlobal(handles); Marshal.FreeHGlobal(block); }
                    using var nativeProcess = new SafeFileHandle(info.Process, true);
                    using var thread = new SafeFileHandle(info.Thread, true);
                    if (!AssignProcessToJobObject(job, nativeProcess)) { TerminateProcess(nativeProcess, 1); Check(false); }
                    using var stop = deadline.Token.Register(job.Dispose);
                    deadline.Token.ThrowIfCancellationRequested();
                    using var process = Process.GetProcessById(info.Id);
                    Check(ResumeThread(thread) != uint.MaxValue);
                    stdinRead.Dispose(); stdoutWrite.Dispose(); stderrWrite.Dispose(); // Parent write ends must close before awaiting EOF.
                    using var writer = new StreamWriter(new FileStream(stdinWrite, FileAccess.Write), new UTF8Encoding(false));
                    using var stdout = new StreamReader(new FileStream(stdoutRead, FileAccess.Read), Encoding.UTF8);
                    using var stderr = new StreamReader(new FileStream(stderrRead, FileAccess.Read), Encoding.UTF8);
                    var output = Drain(stdout, null, deadline.Token);
                    var errors = Drain(stderr, progress, deadline.Token);
                    await writer.WriteAsync(input.AsMemory(), deadline.Token); writer.Close();
                    await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, errors);
                    deadline.Token.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0) throw new InvalidOperationException($"Optional worker failed (exit {process.ExitCode}). {(await errors)[..Math.Min(2000, (await errors).Length)]}");
                    return await output;
                }
            }
        }
    }
    internal const string TokenUnavailable = "No verified non-admin medium-integrity UAC token is available; the optional worker will not run. Administrator fallback and desktop ACL changes are forbidden.";
    internal static SafeFileHandle? NonAdminToken()
    {
        if (!LogService.IsAdministrator()) return null;
        Check(OpenProcessToken(GetCurrentProcess(), 8, out var original)); // TOKEN_QUERY.
        using (original)
        {
            // Use Windows' linked standard-user token. A hand-filtered admin token can be denied desktop access even when UserInteractive is true.
            if (!GetTokenInformation(original, 19, out var linked, IntPtr.Size, out _) || linked.Token == IntPtr.Zero)
                throw new InvalidOperationException(TokenUnavailable);
            var token = new SafeFileHandle(linked.Token, true);
            try
            {
                using var identity = new WindowsIdentity(token.DangerousGetHandle());
                using var owner = WindowsIdentity.GetCurrent();
                var label = Marshal.AllocHGlobal(256);
                try
                {
                    if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ||
                        identity.User != owner.User ||
                        !GetTokenInformation(token, 25, label, 256, out _) ||
                        new SecurityIdentifier(Marshal.PtrToStructure<MandatoryLabel>(label).Sid).Value != "S-1-16-8192")
                        throw new InvalidOperationException(TokenUnavailable);
                }
                finally { Marshal.FreeHGlobal(label); }
                return token;
            }
            catch { token.Dispose(); throw; }
        }
    }
    private static async Task<string> Drain(StreamReader reader, Action<string>? progress, CancellationToken cancellationToken)
    {
        var text = new StringBuilder(); var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) is var count && count > 0)
        {
            if (text.Length < 256000) text.Append(buffer, 0, Math.Min(count, 256000 - text.Length));
            if (progress is not null) foreach (var line in new string(buffer, 0, count).Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).Take(6)) progress(line.Trim()[..Math.Min(300, line.Trim().Length)]);
        }
        return text.ToString();
    }
    internal static string Quote(string value)
    {
        if (value.Any(character => character is '"' or '\0' or '\r' or '\n')) throw new ArgumentException("Invalid optional-worker argument.");
        return '"' + value + new string('\\', value.Length - value.TrimEnd('\\').Length) + '"';
    }
    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start/control the non-admin optional worker."); }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct MandatoryLabel { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct LinkedToken { public IntPtr Token; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo { public int Size; public string? Reserved, Desktop, Title; public uint X, Y, Width, Height, XCount, YCount, Fill, Flags; public short Show, ReservedSize; public IntPtr ReservedPointer, Input, Output, Error; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public int Id, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr MinWorking, MaxWorking; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits { public BasicLimits Basic; public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref JobLimits limits, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeFileHandle process, uint exitCode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string executable, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr attributes, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr attributes, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr attributes);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeFileHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeFileHandle token, int kind, out LinkedToken value, int size, out int returned);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeFileHandle token, int kind, IntPtr value, int size, out int returned);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessAsUser(SafeFileHandle token, string executable, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo info);
}
