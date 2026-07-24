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

        foreach (var setting in new[] { "useplatformclock", "disabledynamictick", "tscsyncpolicy", "useplatformtick", "numproc", "truncatememory", "removememory" })
        {
            var id = $"boot:BCD {setting}";
            Add($"bcd-{setting}", $"Manual BCD override: {setting}", ConflictKind.SuspiciousOverride,
                [id, "system:operating-system", "system:cpu"], values => values[0] != "Not configured" && values[0] != "Unavailable",
                $"The active boot entry explicitly configures {setting}.",
                "Generic boot timer, CPU, or memory overrides replace Windows hardware detection and can increase latency, reduce available resources, or destabilize timing on modern hardware.",
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

        foreach (var issue in facts.Keys.Where(key => key.StartsWith("device-issue:", StringComparison.Ordinal)))
            AddDirect($"device-{issue[13..]}", "Windows reports a device error", ConflictKind.Confirmed, [issue],
                "A Plug and Play device has a non-zero Configuration Manager error code.",
                "Driver or device failures should be resolved before attributing instability or latency to performance settings.",
                [goals.Priority], "High", []);

        var firmwareAssessment = "firmware:Memory profile assessment";
        Add("memory-training", "DIMM configuration requires firmware verification", ConflictKind.MissingEvidence,
            [firmwareAssessment], values => values[0].Contains("Possible", StringComparison.OrdinalIgnoreCase) || values[0].Contains("differ", StringComparison.OrdinalIgnoreCase),
            "Windows exposes a memory speed relationship that may indicate a profile or mismatched training.",
            "SMBIOS/WMI cannot prove XMP, DOCP, EXPO, timings, or stability; a low-level read-only telemetry provider is required before making a stronger claim.",
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
