using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NeuroTune;

// Short-lived worker, non-admin by default, not a filesystem sandbox. All descendants die with its owner/job.
internal static class OptionalWorkerProcess
{
    internal sealed class ExitException(int exitCode, string message, Exception? innerException = null) : InvalidOperationException(message, innerException)
    {
        internal int ExitCode { get; } = exitCode;
    }

    internal static async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments, string directory,
        string input, IReadOnlyDictionary<string, string> environment, TimeSpan timeout, Action<string>? progress, CancellationToken cancellationToken,
        Func<StreamReader, StreamWriter, CancellationToken, Task<string>>? interact = null, bool inheritHostToken = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var job = NewJob();
        // Only the bundled Pi bridge opts into the host token; read tools and optional workers stay non-admin.
        using var token = inheritHostToken ? null : NonAdminToken();
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
                    var errors = Drain(stderr, progress, deadline.Token);
                    var output = interact is null ? Drain(stdout, null, deadline.Token) : InteractAsync();
                    if (interact is null) { await writer.WriteAsync(input.AsMemory(), deadline.Token); writer.Close(); }
                    try { await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, errors); }
                    catch (Exception) when (deadline.IsCancellationRequested) { throw new OperationCanceledException(deadline.Token); }
                    deadline.Token.ThrowIfCancellationRequested();
                    if (!inheritHostToken && process.ExitCode == DesktopDenied) throw new InvalidOperationException(TokenUnavailable);
                    if (process.ExitCode != 0) throw new ExitException(process.ExitCode, $"Optional worker failed (exit {process.ExitCode}). {(await errors)[..Math.Min(2000, (await errors).Length)]}");
                    return await output;

                    async Task<string> InteractAsync()
                    {
                        try { return await interact!(stdout, writer, deadline.Token); }
                        catch (Exception error) when (error is InvalidOperationException or IOException && !deadline.IsCancellationRequested)
                        {
                            // A worker that quits early usually says why on stderr; keep a short tail instead of a bare protocol error.
                            var exited = process.WaitForExit(500);
                            if (!inheritHostToken && exited && process.ExitCode == DesktopDenied) { job.Dispose(); throw new InvalidOperationException(TokenUnavailable, error); }
                            var exit = exited ? $" Worker exit code 0x{process.ExitCode:X8}." : "";
                            job.Dispose();
                            var said = await Task.WhenAny(errors, Task.Delay(2000)) == errors && errors.IsCompletedSuccessfully ? errors.Result.Trim() : "";
                            var message = error.Message + exit + (said.Length == 0 ? "" : $" Worker said: {said[^Math.Min(600, said.Length)..]}");
                            if (exited) throw new ExitException(process.ExitCode, message, error);
                            throw new InvalidOperationException(message, error);
                        }
                        catch { job.Dispose(); throw; }
                        finally { writer.Close(); }
                    }
                }
            }
        }
    }
    internal static SafeFileHandle NewJob()
    {
        var job = new SafeFileHandle(CreateJobObject(IntPtr.Zero, null), true);
        Check(!job.IsInvalid);
        var limits = new JobLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE.
        Check(SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<JobLimits>()));
        return job;
    }

    // A local server without pipes, under the same non-admin token. It and its children die when the job closes,
    // including when this process exits for any reason.
    internal static void StartInJob(SafeFileHandle job, string executable, IReadOnlyList<string> arguments, string directory, IReadOnlyDictionary<string, string> environment)
    {
        using var token = NonAdminToken();
        var startup = new StartupInfoEx { Info = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() } };
        var command = new StringBuilder(string.Join(' ', new[] { executable }.Concat(arguments).Select(Quote)));
        var block = Marshal.StringToHGlobalUni(string.Join('\0', environment.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => $"{item.Key}={item.Value}")) + "\0\0");
        ProcessInfo info;
        try
        {
            Check(token is null
                ? CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000404, block, directory, ref startup, out info)
                : CreateProcessAsUser(token, executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000404, block, directory, ref startup, out info));
        }
        finally { Marshal.FreeHGlobal(block); }
        using var process = new SafeFileHandle(info.Process, true);
        using var thread = new SafeFileHandle(info.Thread, true);
        if (!AssignProcessToJobObject(job, process)) { TerminateProcess(process, 1); Check(false); }
        Check(ResumeThread(thread) != uint.MaxValue);
    }
    // STATUS_DLL_INIT_FAILED: the filtered token could not open this session's desktop (seen on service-hosted CI runners).
    private const int DesktopDenied = unchecked((int)0xC0000142);
    internal const string TokenUnavailable = "No verified non-admin medium-integrity UAC token is available; the optional worker will not run. Administrator fallback and desktop ACL changes are forbidden.";
    internal static SafeFileHandle? NonAdminToken()
    {
        if (!LogService.IsAdministrator()) return null;
        // TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ADJUST_DEFAULT.
        Check(OpenProcessToken(GetCurrentProcess(), 0x8B, out var original));
        using (original)
        {
            // The UAC linked token is identification-level for a non-TCB caller (CreateProcessAsUser fails with 1346), so
            // derive what UAC itself would: a LUA-filtered copy of our own token (Administrators deny-only, privileges
            // stripped) lowered to medium integrity. A restricted copy of the caller's token needs no extra privilege.
            if (!CreateRestrictedToken(original, 0x5, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, out var token)) // DISABLE_MAX_PRIVILEGE | LUA_TOKEN.
                throw new InvalidOperationException(TokenUnavailable);
            try
            {
                var medium = new SecurityIdentifier("S-1-16-8192");
                var sid = new byte[medium.BinaryLength]; medium.GetBinaryForm(sid, 0);
                var pinned = GCHandle.Alloc(sid, GCHandleType.Pinned);
                try
                {
                    var level = new MandatoryLabel { Sid = pinned.AddrOfPinnedObject(), Attributes = 0x20 }; // SE_GROUP_INTEGRITY.
                    if (!SetTokenInformation(token, 25, ref level, Marshal.SizeOf<MandatoryLabel>() + sid.Length)) throw new InvalidOperationException(TokenUnavailable);
                }
                finally { pinned.Free(); }
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
    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start/control the worker."); }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct MandatoryLabel { public IntPtr Sid; public uint Attributes; }
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
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool CreateRestrictedToken(SafeFileHandle token, uint flags, int disableCount, IntPtr disable, int deleteCount, IntPtr delete, int restrictCount, IntPtr restrict, out SafeFileHandle restricted);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(SafeFileHandle token, int kind, ref MandatoryLabel value, int size);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeFileHandle token, int kind, IntPtr value, int size, out int returned);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessAsUser(SafeFileHandle token, string executable, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo info);
}
