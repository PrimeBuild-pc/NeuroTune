using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NeuroTune;

// Read-only exposure/compromise indicators for the compromise-review preset. Counts, flags and process names only:
// never file paths, command lines, account names, hosts-file domains, proxy addresses or certificate subjects.
internal static partial class CompromiseAudit
{
    private const string Limitation = "Point-in-time Windows metadata, not a malware verdict. Legitimate software commonly matches these indicators; no hashes, signatures, memory or traffic were inspected.";
    private const int RowLimit = 80;

    internal static object ReadNetworkExposure()
    {
        var rows = new HashSet<(string Protocol, int Port, string Scope, string Process)>();
        var sources = new[] { ("tcp", "SELECT LocalAddress, LocalPort, OwningProcess FROM MSFT_NetTCPConnection WHERE State = 2"),
                              ("udp", "SELECT LocalAddress, LocalPort, OwningProcess FROM MSFT_NetUDPEndpoint") };
        foreach (var (protocol, query) in sources)
            foreach (var (address, port, pid) in SystemProfiler.Query(@"root\StandardCimv2", query,
                row => (Address: row["LocalAddress"]?.ToString() ?? "", Port: Convert.ToInt32(row["LocalPort"] ?? 0, CultureInfo.InvariantCulture), Pid: Convert.ToInt32(row["OwningProcess"] ?? 0, CultureInfo.InvariantCulture))))
                rows.Add((protocol, port, Scope(address), ProcessName(pid)));
        var ordered = rows.OrderBy(row => row.Scope == "all-interfaces" ? 0 : row.Scope == "specific-interface" ? 1 : 2).ThenBy(row => row.Port).ToList();
        return new
        {
            listeners = ordered.Take(RowLimit).Select(row => new { row.Protocol, row.Port, row.Scope, row.Process }),
            total = ordered.Count,
            truncated = ordered.Count > RowLimit,
            limitation = "Listening sockets and owning process names only. A listener is not an attack and a closed port is not proof of safety; firewall rules are not evaluated. " + Limitation
        };
    }

    internal static object ReadServiceIntegrity()
    {
        var services = SystemProfiler.Query("SELECT Name, State, StartMode, PathName FROM Win32_Service",
            row => (Name: row["Name"]?.ToString() ?? "", State: row["State"]?.ToString() ?? "Unknown", Start: row["StartMode"]?.ToString() ?? "Unknown", Path: row["PathName"]?.ToString() ?? ""));
        var flagged = services.Select(service => (service, flags: ServiceFlags(service.Path))).Where(item => item.flags.Length > 0).ToList();
        return new
        {
            totalServices = services.Count,
            flaggedServices = flagged.Count,
            rows = flagged.Take(60).Select(item => new { name = item.service.Name, state = item.service.State, startMode = item.service.Start, flags = item.flags }),
            truncated = flagged.Count > 60,
            limitation = "Flags come from the service image path shape only; paths are not exported and signatures are not checked. Unquoted paths are a local privilege-escalation weakness, user-writable locations are a hint, not proof of malware. " + Limitation
        };
    }

    internal static object ReadPatchLevel()
    {
        var fixes = SystemProfiler.Query("SELECT InstalledOn FROM Win32_QuickFixEngineering", row => row["InstalledOn"]?.ToString() ?? "");
        var dates = fixes.Select(text => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date) ? date : (DateTime?)null).Where(date => date is not null).Select(date => date!.Value).ToList();
        using var version = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        using var update = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install");
        return new
        {
            osBuild = $"{version?.GetValue("CurrentBuild")}.{version?.GetValue("UBR")}",
            reportedHotfixCount = fixes.Count,
            newestReportedHotfixDate = dates.Count == 0 ? "Unknown" : dates.Max().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            daysSinceNewestReportedHotfix = dates.Count == 0 ? (int?)null : (int)(DateTime.Today - dates.Max().Date).TotalDays,
            lastWindowsUpdateInstallUtc = update?.GetValue("LastSuccessTime")?.ToString() is { Length: <= 40 } text ? text : "Unknown",
            limitation = "Win32_QuickFixEngineering omits many cumulative/feature updates, so an old date can be a reporting gap. This is not a vulnerability scan or proof that patches are missing. " + Limitation
        };
    }

    internal static object ReadAccountSummary()
    {
        var accounts = SystemProfiler.Query("SELECT Disabled, SID FROM Win32_UserAccount WHERE LocalAccount = True",
            row => (Disabled: row["Disabled"] as bool? ?? false, Sid: row["SID"]?.ToString() ?? ""));
        var machine = Environment.MachineName;
        var admins = MachineNameRegex().IsMatch(machine)
            ? SystemProfiler.Query($"SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent = \"Win32_Group.Domain='{machine}',Name='Administrators'\"", row => 1).Count
            : (int?)null;
        return new
        {
            localAccounts = accounts.Count,
            enabledLocalAccounts = accounts.Count(account => !account.Disabled),
            builtInAdministratorEnabled = accounts.Any(account => !account.Disabled && account.Sid.EndsWith("-500", StringComparison.Ordinal)),
            guestEnabled = accounts.Any(account => !account.Disabled && account.Sid.EndsWith("-501", StringComparison.Ordinal)),
            administratorsGroupMembers = admins,
            limitation = "Counts and flags only; account names and domain accounts are not exported. A higher-than-expected administrator count is a question for the owner, not proof of intrusion. " + Limitation
        };
    }

    internal static object ReadWmiPersistence()
    {
        int Count(string query) => SystemProfiler.Query(@"root\subscription", query, row => 1).Count;
        return new
        {
            commandLineConsumers = Count("SELECT Name FROM CommandLineEventConsumer"),
            activeScriptConsumers = Count("SELECT Name FROM ActiveScriptEventConsumer"),
            eventFilters = Count("SELECT Name FROM __EventFilter WHERE Name <> 'SCM Event Log Filter'"),
            filterConsumerBindings = Count("SELECT * FROM __FilterToConsumerBinding"),
            limitation = "Permanent WMI event subscriptions are a known stealth persistence mechanism but are also used by some management software; consumer commands are not exported. " + Limitation
        };
    }

    internal static object ReadHostsAndProxy()
    {
        var active = 0; var redirecting = 0;
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts")).Take(20_000))
            {
                var entry = line.Split('#')[0].Trim();
                if (entry.Length == 0) continue;
                active++;
                if (entry.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0] is not ("127.0.0.1" or "::1" or "0.0.0.0")) redirecting++;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { active = -1; redirecting = -1; }
        using var internet = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        return new
        {
            hostsActiveEntries = active,
            hostsRedirectingEntries = redirecting,
            userProxyEnabled = internet?.GetValue("ProxyEnable") is int enabled && enabled == 1,
            userProxyAutoConfigUrlSet = !string.IsNullOrWhiteSpace(internet?.GetValue("AutoConfigURL")?.ToString()),
            limitation = "Hosts domains and proxy addresses are not exported (-1 means unreadable). Corporate proxies, ad-blocking hosts files and VPN software legitimately change these. " + Limitation
        };
    }

    internal static string Scope(string address) => address switch
    {
        "127.0.0.1" or "::1" => "loopback",
        "0.0.0.0" or "::" => "all-interfaces",
        _ => "specific-interface"
    };

    internal static string[] ServiceFlags(string path)
    {
        var image = path.Trim();
        var flags = new List<string>();
        var end = image.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (image.Length > 0 && image[0] != '"' && end > 0 && image[..(end + 4)].Contains(' ') &&
            !image.StartsWith("\\SystemRoot\\", StringComparison.OrdinalIgnoreCase)) flags.Add("unquoted-path-with-spaces");
        if (new[] { @"\Users\", @"\AppData\", @"\Temp\", @"\Downloads\", @"\Public\" }.Any(part => image.Contains(part, StringComparison.OrdinalIgnoreCase)) ||
            image.Contains(@"\ProgramData\", StringComparison.OrdinalIgnoreCase) && !image.Contains(@"\ProgramData\Microsoft\Windows Defender\", StringComparison.OrdinalIgnoreCase)) flags.Add("user-writable-location");
        return [.. flags];
    }

    private static string ProcessName(int pid)
    {
        if (pid is 0 or 4) return "System";
        try { return Process.GetProcessById(pid).ProcessName; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return "Unknown"; }
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,15}$")]
    private static partial Regex MachineNameRegex();
}
