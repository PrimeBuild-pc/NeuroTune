using System.Diagnostics;
using System.Text;

namespace NeuroTune;

internal static class WindowsCommand
{
    internal static string PathFor(string name)
    {
        if (name is not ("powercfg.exe" or "bcdedit.exe" or "reg.exe" or "powershell.exe" or
            "fsutil.exe" or "fltmc.exe" or "netsh.exe" or "netcfg.exe" or "wpr.exe"))
            throw new InvalidOperationException("The Windows executable is not allowlisted.");
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32");
        return Path.Combine(system, name == "powershell.exe" ? @"WindowsPowerShell\v1.0\powershell.exe" : name);
    }

    internal static string Run(string name, params string[] arguments)
    {
        var start = new ProcessStartInfo(PathFor(name));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return RunAsync(start, TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
    }

    internal static async Task<string> RunAsync(ProcessStartInfo start, TimeSpan timeout)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        if (Path.GetFileName(start.FileName).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            start.Environment["PSModulePath"] = Path.Combine(Path.GetDirectoryName(start.FileName)!, "Modules");
            var command = start.ArgumentList.IndexOf("-Command");
            if (command < 0 || command + 1 >= start.ArgumentList.Count)
                throw new InvalidOperationException("Only the fixed PowerShell command mode is supported.");
            // Windows PowerShell prepends its AllUsers path at startup: reset before any cmdlet autoload.
            start.ArgumentList[command + 1] = "$env:PSModulePath = $PSHOME + '\\Modules'; " + start.ArgumentList[command + 1];
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The Windows command could not start.");
        using var deadline = new CancellationTokenSource(timeout);
        var output = Drain(process.StandardOutput, deadline.Token);
        var error = Drain(process.StandardError, deadline.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{Path.GetFileName(start.FileName)} failed ({process.ExitCode}): {(await error)[..Math.Min(2000, (await error).Length)]}");
            return await output;
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException($"{Path.GetFileName(start.FileName)} exceeded its deadline; its writes may require recovery.", exception);
        }
        finally
        {
            // A timed-out/failed writer is never reported as successful.
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            try { await Task.WhenAll(output, error); }
            catch (Exception) { /* Preserve the original deadline/output failure. */ }
        }
    }

    private static async Task<string> Drain(StreamReader reader, CancellationToken cancellationToken)
    {
        const int maximum = 1_048_576;
        var text = new StringBuilder();
        var buffer = new char[4096];
        var overflow = false;
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) is var count && count > 0)
        {
            if (count > maximum - text.Length) overflow = true;
            text.Append(buffer, 0, Math.Min(count, maximum - text.Length));
        }
        if (overflow) throw new InvalidOperationException("The Windows command output exceeded 1,048,576 characters.");
        return text.ToString();
    }
}
