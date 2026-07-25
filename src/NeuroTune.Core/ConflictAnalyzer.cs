namespace NeuroTune;

public static class ConflictAnalyzer
{
    public static List<ConflictPattern> Analyze(SystemProfile profile, TuningGoals goals)
    {
        var facts = LlmClient.BuildEvidenceFacts(profile);
        var conflicts = new List<ConflictPattern>();

        Add("game-mode-policy", "Game Mode preference is blocked by policy", ConflictKind.Confirmed,
            [R(@"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled"), R(@"HKCU\Software\Microsoft\GameBar\AllowAutoGameMode")],
            values => values[0] == "1" && values[1] == "0",
            "The user preference enables automatic Game Mode while its policy value disables it; the policy wins.",
            "Windows cannot honor the selected gaming preference, so diagnosis based only on the preference would be misleading.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency], "High", ["gaming.game-mode"]);

        Add("capture-policy", "Game capture preference and machine policy disagree", ConflictKind.Confirmed,
            [R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled"), R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR")],
            values => values[0] == "1" && values[1] == "0",
            "Game capture is enabled for the user but disabled by machine policy.",
            "The effective state differs from the visible user preference and can confuse performance attribution.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency], "High", ["gaming.game-dvr-off", "gaming.app-capture-off"]);

        Add("capture-fps", "Background capture competes with a frame-rate goal", ConflictKind.Conditional,
            [R(@"HKCU\System\GameConfigStore\GameDVR_Enabled"), R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled")],
            values => goals.Priority is OptimizationPriority.Fps or OptimizationPriority.SystemLatency && values.Any(value => value == "1"),
            "At least one Windows capture path is enabled while the selected objective prioritizes frame rate or system latency.",
            "Capture can add encoding, storage, and presentation work, although the real impact depends on whether recording is active.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency], "Medium", ["gaming.game-dvr-off", "gaming.app-capture-off"]);

        Add("battery-power-throttling", "Battery hardware and disabled power throttling", ConflictKind.Conditional,
            ["hardware:Battery", R(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling\PowerThrottlingOff")],
            values => values[0] != "Not detected" && values[1] == "1",
            "System-wide power throttling is disabled on a battery-powered device.",
            "The override can raise background power use, temperature, and fan noise and may reduce sustained boost when efficiency or thermals matter.",
            [OptimizationPriority.Efficiency, OptimizationPriority.Balanced], "High", ["system.power-throttling-default"]);

        Add("large-system-cache", "Server cache policy on a client workload", ConflictKind.SuspiciousOverride,
            [R(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\LargeSystemCache"), "system:memory"],
            values => values[0] == "1",
            "LargeSystemCache is enabled even though NeuroTune targets interactive Windows client workloads.",
            "Favoring the system cache can reduce memory available to games and interactive applications without proving a benefit on this machine.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency, OptimizationPriority.Balanced], "High", ["system.large-cache-default"]);

        Add("paging-executive", "Kernel paging override replaces Windows memory policy", ConflictKind.SuspiciousOverride,
            [R(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\DisablePagingExecutive"), "system:memory"],
            values => values[0] == "1",
            "DisablePagingExecutive forces pageable kernel components to remain resident.",
            "It consumes physical memory regardless of pressure and can be counterproductive when a game or workload needs that capacity.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency, OptimizationPriority.Balanced], "High", ["system.paging-executive-default"]);

        Add("mpo-override", "Desktop compositor overlay override on the active display stack", ConflictKind.SuspiciousOverride,
            [R(@"HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayTestMode"), "hardware:Displays", "hardware:gpu:0"],
            values => values[0] != "Not configured",
            "A manual MPO override changes how Desktop Window Manager and the GPU driver choose presentation overlays.",
            "A fixed override may solve one driver issue but hurt power use, presentation latency, or compatibility after a driver/display change.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency, OptimizationPriority.Efficiency], "High", ["graphics.mpo-default"]);

        Add("gpu-timeout", "Manual GPU timeout values can hide instability", ConflictKind.SuspiciousOverride,
            [R(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\TdrDelay"), R(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\TdrDdiDelay"), "hardware:gpu:0"],
            values => values[0] != "Not configured" || values[1] != "Not configured",
            "Windows GPU timeout detection has manual delay values.",
            "Longer recovery delays do not improve GPU performance; they can turn a recoverable driver reset into a longer freeze and mask an unstable overclock.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency, OptimizationPriority.Balanced], "High", ["graphics.tdr-default"]);

        var tcpIds = new[]
        {
            R(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\TcpTimedWaitDelay"),
            R(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\MaxUserPort"),
            R(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\DefaultTTL"),
            R(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\GlobalMaxTcpWindowSize"),
            R(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\TcpWindowSize")
        };
        Add("global-tcp-overrides", "Generic TCP overrides bypass connection-specific tuning", ConflictKind.SuspiciousOverride,
            tcpIds, values => values.Any(value => value != "Not configured"),
            "One or more global TCP values are manually configured while Windows also reports its current TCP stack policy.",
            "Static Internet-era values can conflict with auto-tuning, adapter offloads, VPN filters, or the actual path and may worsen throughput or retransmission behavior rather than latency.",
            [OptimizationPriority.NetworkLatency, OptimizationPriority.Balanced], "High", ["network.tcp-default"]);

        Add("bcd-timer-policy", "BCD timer policy overrides platform timing", ConflictKind.SuspiciousOverride,
            ["boot:BCD useplatformclock", "boot:BCD useplatformtick", "boot:BCD disabledynamictick", "boot:BCD tscsyncpolicy", "hardware:Performance counter", "firmware:CPUID vendor"],
            values => values.Take(4).Any(value => value != "Not configured" && value != "Unavailable") &&
                values[4] != "Unavailable" && values[5] != "Unavailable",
            "The active boot entry overrides one or more timer choices while Windows reports a high-resolution performance counter and a known CPUID vendor.",
            "Forcing HPET, platform ticks, dynamic-tick behavior, or TSC synchronization can replace Windows platform selection and increase latency or timing jitter.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency, OptimizationPriority.Balanced], "High", []);

        foreach (var setting in new[] { "numproc", "truncatememory", "removememory" })
        {
            var id = $"boot:BCD {setting}";
            Add($"bcd-{setting}", $"Manual BCD override: {setting}", ConflictKind.SuspiciousOverride,
                [id, "system:operating-system", "system:cpu"], values => values[0] != "Not configured" && values[0] != "Unavailable",
                $"The active boot entry explicitly configures {setting}.",
                "The override replaces Windows hardware detection and can reduce available CPU or memory resources.",
                [OptimizationPriority.Fps, OptimizationPriority.SystemLatency, OptimizationPriority.Balanced], "High", []);
        }

        Add("vbs-performance-tradeoff", "Virtualization security and maximum-performance objective", ConflictKind.Conditional,
            ["hardware:Virtualization-based security status", R(@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled")],
            values => goals.Priority is OptimizationPriority.Fps or OptimizationPriority.SystemLatency && values[0] == "Enabled and running",
            "Virtualization-based security is active while the user selected maximum frame rate or system latency.",
            "VBS can add workload-dependent overhead, but disabling a security boundary is not an automatic optimization and NeuroTune will not offer that action.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency], "High", []);

        var overlayEvidence = facts.Where(pair => pair.Key.StartsWith("software-signal:", StringComparison.Ordinal) &&
            new[] { "Afterburner", "RTSS", "RivaTuner", "OBS", "Discord", "Overwolf", "NVIDIA App", "AMD Software" }
                .Any(name => pair.Value.Contains(name, StringComparison.OrdinalIgnoreCase))).Select(pair => pair.Key).ToList();
        if (overlayEvidence.Count >= 2)
            AddDirect("multiple-overlays", "Multiple overlay or capture stacks detected", ConflictKind.Conditional, overlayEvidence,
                "Several applications capable of injecting overlays, monitoring, or capture are present.",
                "Concurrent hooks can compete in a game's presentation path and make frametime regressions difficult to attribute; presence does not prove they are active.",
                [OptimizationPriority.Fps, OptimizationPriority.SystemLatency], "Medium", []);

        var vpnEvidence = facts.Where(pair => pair.Key.StartsWith("software-signal:", StringComparison.Ordinal) &&
            new[] { "VPN", "WireGuard", "OpenVPN", "ExitLag" }.Any(name => pair.Value.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Key).ToList();
        if (vpnEvidence.Count > 0)
            Add("vpn-offload-policy", "VPN/filter software and global offload overrides coexist", ConflictKind.Conditional,
                [vpnEvidence[0], R(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\DisableTaskOffload"), R(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\EnableRSS"), "network:Installed network components"],
                values => values[1] != "Not configured" || values[2] != "Not configured",
                "A VPN or routing software family is installed while one or more global adapter-offload values are manually configured.",
                "Static offload policy can interact with filter drivers and tunnel paths; it may trade throughput and CPU work without reducing end-to-end latency.",
                [OptimizationPriority.NetworkLatency, OptimizationPriority.Balanced], "Medium", ["network.tcp-default"]);

        var tuningEvidence = facts.Where(pair => pair.Key.StartsWith("software-signal:", StringComparison.Ordinal) &&
            new[] { "Afterburner", "RivaTuner", "Ryzen Master", "Intel Extreme Tuning", "ThrottleStop" }
                .Any(name => pair.Value.Contains(name, StringComparison.OrdinalIgnoreCase))).Select(pair => pair.Key).ToList();
        if (tuningEvidence.Count > 0)
            Add("tdr-tuning-stack", "GPU timeout overrides coexist with tuning software", ConflictKind.Conditional,
                [R(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\TdrDelay"), R(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\TdrDdiDelay"), tuningEvidence[0]],
                values => values[0] != "Not configured" || values[1] != "Not configured",
                "Manual GPU recovery delays are present alongside software capable of hardware tuning or presentation hooks.",
                "Longer timeout values can conceal an unstable tune and turn a recoverable driver reset into a longer freeze; software presence alone does not prove an active overclock.",
                [OptimizationPriority.Fps, OptimizationPriority.SystemLatency, OptimizationPriority.Balanced], "High", ["graphics.tdr-default"]);

        Add("manual-pagefile", "Manual page-file policy replaces Windows sizing", ConflictKind.Conditional,
            ["hardware:Page file and system type", "system:memory", R(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\DisablePagingExecutive")],
            values => values[0].Contains("Windows-managed=False", StringComparison.OrdinalIgnoreCase),
            "Windows reports that automatic page-file management is disabled.",
            "A fixed page-file policy can under-provision commit for the installed memory and workload; the scan does not expose enough detail to prescribe a size.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency, OptimizationPriority.Balanced], "Medium", []);

        Add("mobile-high-performance", "High-performance power plan on a mobile system", ConflictKind.Conditional,
            ["system:active-power-plan", "hardware:Page file and system type", "hardware:Battery", "hardware:ACPI thermal zones"],
            values => goals.Priority is OptimizationPriority.Efficiency or OptimizationPriority.Balanced &&
                (values[0].Contains("High performance", StringComparison.OrdinalIgnoreCase) ||
                 values[0].Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase)) &&
                (values[1].Contains("laptop", StringComparison.OrdinalIgnoreCase) || values[2] != "Not detected"),
            "The High Performance plan is active on a laptop or battery-equipped system.",
            "Higher background power and temperature can reduce efficiency and may reduce sustained boost after thermal limits are reached; ACPI temperatures may be unavailable or indirect.",
            [OptimizationPriority.Efficiency, OptimizationPriority.Balanced], "Medium", []);

        foreach (var issue in facts.Keys.Where(key => key.StartsWith("device-issue:", StringComparison.Ordinal)))
            AddDirect($"device-{issue[13..]}", "Windows reports a device error", ConflictKind.Confirmed, [issue],
                "A Plug and Play device has a non-zero Configuration Manager error code.",
                "Driver or device failures should be resolved before attributing instability or latency to performance settings.",
                [goals.Priority], "High", []);

        var deviceIssue = facts.Keys.FirstOrDefault(key => key.StartsWith("device-issue:", StringComparison.Ordinal));
        var staleDriver = facts.FirstOrDefault(pair => pair.Key.StartsWith("driver:", StringComparison.Ordinal) &&
            DateTime.TryParse(pair.Value.Split('|').Last().Trim(), out var date) && date < profile.CollectedAt.AddYears(-5));
        if (deviceIssue is not null && staleDriver.Key is not null)
            AddDirect("stale-driver-device-error", "Old driver record and device error coexist", ConflictKind.Conditional,
                [staleDriver.Key, deviceIssue],
                "A relevant driver date is more than five years older than this scan and Windows reports a Plug and Play device error.",
                "Resolve the concrete device/driver failure before changing broad performance settings; age alone does not prove that the driver is unsupported.",
                [goals.Priority], "Medium", []);

        var firmwareAssessment = "firmware:Memory profile assessment";
        var dimmEvidence = facts.Keys.Where(key => key.StartsWith("firmware:DIMM ", StringComparison.Ordinal)).ToList();
        Add("memory-training", "DIMM configuration requires firmware verification", ConflictKind.MissingEvidence,
            [firmwareAssessment, .. dimmEvidence, "hardware:ACPI thermal zones"], values => values[0].Contains("Possible", StringComparison.OrdinalIgnoreCase) || values[0].Contains("differ", StringComparison.OrdinalIgnoreCase),
            "Windows exposes DIMM identity and speed relationships that may indicate a profile or mismatched training; ACPI thermal evidence is included when available.",
            "SMBIOS/WMI cannot prove XMP, DOCP, EXPO, timings, memory temperature, or stability; a validated read-only telemetry provider is required before making a stronger claim.",
            [OptimizationPriority.Fps, OptimizationPriority.SystemLatency, OptimizationPriority.Balanced], "Low", []);

        return conflicts;

        void Add(string id, string title, ConflictKind kind, IReadOnlyList<string> evidenceIds,
            Func<IReadOnlyList<string>, bool> condition, string explanation, string impact,
            List<OptimizationPriority> objectives, string confidence, List<string> actions)
        {
            if (evidenceIds.Any(evidenceId => !facts.ContainsKey(evidenceId))) return;
            var values = evidenceIds.Select(evidenceId => facts[evidenceId]).ToList();
            if (condition(values)) AddDirect(id, title, kind, evidenceIds, explanation, impact, objectives, confidence, actions);
        }

        void AddDirect(string id, string title, ConflictKind kind, IReadOnlyList<string> evidenceIds,
            string explanation, string impact, List<OptimizationPriority> objectives, string confidence, List<string> actions) =>
            conflicts.Add(new()
            {
                Id = id,
                Title = title,
                Kind = kind,
                EvidenceIds = evidenceIds.ToList(),
                Evidence = evidenceIds.Where(facts.ContainsKey).ToDictionary(evidenceId => evidenceId, evidenceId => facts[evidenceId], StringComparer.Ordinal),
                Objectives = objectives,
                Explanation = explanation,
                WhyCounterproductive = impact,
                Confidence = confidence,
                SuggestedActionIds = actions
            });
    }

    private static string R(string path) => $"registry:{path}";
}
