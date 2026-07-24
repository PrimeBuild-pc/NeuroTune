using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace NeuroTune;

public partial class MainWindow : Window
{
    private const string KeyPlaceholder = "••••••••••••";
    private readonly SettingsService _settingsService = new();
    private readonly OptimizationCatalog _catalog = new();
    private readonly BackupService _backup = new();
    private readonly PerformanceSnapshotService _performance = new();
    private readonly ObservableCollection<OptimizationOption> _options = [];
    private SystemProfile? _profile;
    private PerformanceSnapshot? _baseline;
    private DiagnosisResult? _diagnosis;
    private bool _loading = true;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev"}";
        ProviderCombo.ItemsSource = Enum.GetValues<LlmProvider>();
        PresetCombo.ItemsSource = new[]
        {
            new PresetChoice("Safe / Balanced", OptimizationPreset.Balanced),
            new PresetChoice("Extreme Gaming", OptimizationPreset.Gaming),
            new PresetChoice("Custom", OptimizationPreset.Custom)
        };
        PresetCombo.DisplayMemberPath = nameof(PresetChoice.Display);
        PresetCombo.SelectedIndex = 0;
        ActionsList.ItemsSource = _options;

        var settings = _settingsService.Load();
        ProviderCombo.SelectedItem = settings.Provider;
        ModelCombo.Text = settings.Model;
        var hasKey = _settingsService.LoadApiKey(settings.Provider) is not null;
        ApiKeyBox.Password = hasKey ? KeyPlaceholder : "";
        ProviderStatusText.Text = hasKey ? "Encrypted key found — test the connection to load models" : "No key saved for this provider";

        PopulateOptions();
        RefreshHistory();
        _loading = false;

        var isAdmin = LogService.IsAdministrator();
        AdminText.Text = isAdmin ? "Administrator session" : "Administrator privileges required";
        AdminText.Foreground = (System.Windows.Media.Brush)FindResource(isAdmin ? "Success" : "Warning");
        if (!isAdmin) StatusText.Text = "Restart NeuroTune as administrator before applying changes.";
    }

    private UserSettings CurrentSettings() => new()
    {
        Provider = ProviderCombo.SelectedItem is LlmProvider provider ? provider : LlmProvider.OpenRouter,
        Model = ModelCombo.Text.Trim()
    };

    private string? CurrentApiKey(UserSettings settings) =>
        ApiKeyBox.Password.StartsWith('•') ? _settingsService.LoadApiKey(settings.Provider) : ApiKeyBox.Password.Trim();

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveCurrentSettings();
            ProviderStatusText.Text = "Configuration saved securely";
            StatusText.Text = "Provider configuration saved.";
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private void SaveCurrentSettings()
    {
        var settings = CurrentSettings();
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Enter or select a model.");
        var existingKey = CurrentApiKey(settings);
        if (string.IsNullOrWhiteSpace(existingKey)) throw new InvalidOperationException("Enter an API key.");
        var newKey = ApiKeyBox.Password.StartsWith('•') ? null : ApiKeyBox.Password;
        _settingsService.Save(settings, newKey);
        ApiKeyBox.Password = KeyPlaceholder;
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProviderCombo.SelectedItem is not LlmProvider provider) return;
        ModelCombo.ItemsSource = null;
        ModelCombo.Text = provider switch
        {
            LlmProvider.OpenAI => "gpt-4o-mini",
            LlmProvider.Anthropic => "claude-3-5-haiku-latest",
            _ => "openai/gpt-4o-mini"
        };
        var hasKey = _settingsService.LoadApiKey(provider) is not null;
        ApiKeyBox.Password = hasKey ? KeyPlaceholder : "";
        ProviderStatusText.Text = hasKey ? "Encrypted key found — connection not tested" : "No key saved for this provider";
    }

    private async void LoadModels_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Connecting to the provider and loading available models…");
        try
        {
            var settings = CurrentSettings();
            var key = CurrentApiKey(settings) ?? throw new InvalidOperationException("Enter an API key.");
            var models = await new LlmClient(_catalog).ListModelsAsync(settings.Provider, key);
            var previous = settings.Model;
            ModelCombo.ItemsSource = models;
            ModelCombo.Text = models.Contains(previous, StringComparer.OrdinalIgnoreCase) ? previous : models[0];
            _settingsService.Save(CurrentSettings(), ApiKeyBox.Password.StartsWith('•') ? null : key);
            ApiKeyBox.Password = KeyPlaceholder;
            ProviderStatusText.Text = $"Connected — {models.Count} models available";
            StatusText.Text = "Provider connection verified. Select a model and continue to Scan.";
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Collecting the local system profile…");
        try
        {
            await CollectScanAsync();
            StatusText.Text = "Local scan complete. Review the sanitized profile, then run the AI diagnosis.";
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
    }

    private async Task CollectScanAsync()
    {
        _profile = await Task.Run(() => new SystemProfiler().Collect());
        _baseline = await Task.Run(_performance.Collect);
        ProfileTextBox.Text = ProfileSanitizer.Serialize(_profile);
        OsMetricText.Text = _profile.OperatingSystem;
        CpuMetricText.Text = _profile.Cpu;
        MemoryMetricText.Text = _profile.Memory;
        GpuMetricText.Text = _profile.Gpus.FirstOrDefault() ?? "No GPU detected";
        AnalyzeButton.IsEnabled = true;
        PopulateOptions();
        ApplyPreset();
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Preparing the system diagnosis…");
        try
        {
            if (_profile is null) await CollectScanAsync();
            var settings = CurrentSettings();
            var key = CurrentApiKey(settings) ?? throw new InvalidOperationException("Configure an API key in Setup first.");
            SaveCurrentSettings();
            StatusText.Text = "The sanitized profile is being analyzed by the selected model…";
            _diagnosis = await new LlmClient(_catalog).DiagnoseAsync(_profile!, settings, key);
            DiagnosisTextBox.Text = FormatDiagnosis(_diagnosis);
            PopulateOptions();
            ApplyPreset();
            MainTabs.SelectedIndex = 2;
            StatusText.Text = $"Diagnosis complete: {_diagnosis.Recommendations.Count} allowlisted recommendations.";
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
    }

    private void PopulateOptions()
    {
        var recommendations = (_diagnosis?.Recommendations ?? []).ToDictionary(x => x.ActionId, StringComparer.OrdinalIgnoreCase);
        _options.Clear();
        foreach (var action in _catalog.All.OrderBy(x => x.Category).ThenBy(x => x.Risk))
        {
            var availability = action.Inspect();
            recommendations.TryGetValue(action.Id, out var recommendation);
            _options.Add(new OptimizationOption
            {
                Id = action.Id,
                Name = action.Name + (action.RequiresRestart ? " · restart required" : ""),
                Description = action.Description,
                Category = action.Category,
                Risk = action.Risk,
                RequiresRestart = action.RequiresRestart,
                Reason = recommendation?.Reason ?? "Not recommended by the current diagnosis",
                IsRecommended = recommendation is not null,
                CanApply = availability.CanApply,
                Availability = availability.Status,
                CurrentValue = availability.CurrentValue
            });
        }
        UpdateActionCount();
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) ApplyPreset();
    }

    private void ApplyPreset()
    {
        if (PresetCombo.SelectedItem is not PresetChoice choice || choice.Value == OptimizationPreset.Custom) return;
        foreach (var option in _options)
            option.IsSelected = option.CanApply && OptimizationCatalog.SelectForPreset(_catalog.Get(option.Id), option.IsRecommended, choice.Value);
        ActionsList.Items.Refresh();
        UpdateActionCount();
    }

    private void ActionSelection_Changed(object sender, RoutedEventArgs e)
    {
        _loading = true;
        PresetCombo.SelectedIndex = 2;
        _loading = false;
        UpdateActionCount();
    }

    private void UpdateActionCount()
    {
        var count = _options.Count(x => x.IsSelected && x.CanApply);
        ActionCountText.Text = $"{count} action{(count == 1 ? "" : "s")} selected";
        ApplyButton.IsEnabled = !_busy && count > 0 && LogService.IsAdministrator();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var selected = _options.Where(x => x.IsSelected && x.CanApply).ToList();
        if (selected.Count == 0) { ShowError(new InvalidOperationException("Select at least one compatible optimization.")); return; }
        var names = string.Join(Environment.NewLine, selected.Select(x => $"• {x.Name} — {x.RiskLabel} risk"));
        if (MessageBox.Show($"NeuroTune will create and verify a restore point, back up affected Registry keys, then apply:\n\n{names}\n\nContinue?",
                "Confirm system changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        SetBusy(true, "Creating a verified restore point and applying selected changes…");
        try
        {
            var manifest = await new OptimizationEngine(_catalog, _backup, _performance).ApplyAsync(selected.Select(x => x.Id));
            ShowResults(manifest);
            PopulateOptions();
            RefreshHistory();
            MainTabs.SelectedIndex = 3;
            var restart = selected.Any(x => x.RequiresRestart) ? " Restart Windows before evaluating the result." : "";
            StatusText.Text = $"Operation completed safely.{restart}";
        }
        catch (Exception exception)
        {
            RefreshHistory();
            CheckRecovery();
            ShowError(exception);
        }
        finally { SetBusy(false); }
    }

    private void ShowResults(OperationManifest manifest)
    {
        ResultsSummaryText.Text = $"{manifest.Status} · {manifest.Actions.Count(x => x.Applied)} actions · {manifest.CreatedAt:g}";
        ComparisonText.Text = FormatComparison(manifest.Before, manifest.After);
    }

    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();

    private void RefreshHistory()
    {
        var items = _backup.LoadHistory().Select(x => new HistoryItem(x)).ToList();
        HistoryList.ItemsSource = items;
        if (items.FirstOrDefault() is { } latest && ResultsSummaryText.Text.StartsWith("No operation", StringComparison.Ordinal))
        {
            ResultsSummaryText.Text = latest.Display;
            ComparisonText.Text = FormatComparison(latest.Manifest.Before, latest.Manifest.After);
        }
        CheckRecovery(items);
    }

    private void CheckRecovery(IReadOnlyList<HistoryItem>? items = null)
    {
        items ??= _backup.LoadHistory().Select(x => new HistoryItem(x)).ToList();
        var pending = items.FirstOrDefault(x => x.Manifest.HasPendingRollback);
        RecoveryBanner.Visibility = pending is null ? Visibility.Collapsed : Visibility.Visible;
        if (pending is not null)
            RecoveryText.Text = $"An interrupted or incomplete operation needs attention: {pending.Manifest.Id:N}.";
    }

    private void OpenRecovery_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 3;
        var pending = (HistoryList.ItemsSource as IEnumerable<HistoryItem>)?.FirstOrDefault(x => x.Manifest.HasPendingRollback);
        if (pending is not null) HistoryList.SelectedItem = pending;
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryItem selected) { ShowError(new InvalidOperationException("Select an operation first.")); return; }
        if (!selected.Manifest.Actions.Any(x => (x.Attempted || x.Applied) && !x.RolledBack))
        {
            ShowError(new InvalidOperationException("This operation has no changes left to restore."));
            return;
        }
        if (MessageBox.Show("A new restore point will be created before restoring this operation. Continue?", "Confirm rollback",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        SetBusy(true, "Creating a safety restore point and rolling back…");
        try
        {
            await new OptimizationEngine(_catalog, _backup, _performance).RollbackAsync(selected.Manifest);
            StatusText.Text = "Rollback completed and verified.";
            ResultsSummaryText.Text = $"Rollback completed · {selected.Manifest.Id:N}";
            PopulateOptions();
            RefreshHistory();
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        ScanButton.IsEnabled = !busy;
        AnalyzeButton.IsEnabled = !busy && _profile is not null;
        SaveButton.IsEnabled = !busy;
        LoadModelsButton.IsEnabled = !busy;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (status is not null) StatusText.Text = status;
        UpdateActionCount();
    }

    private void ShowError(Exception exception)
    {
        LogService.Write($"{exception.GetType().Name}: {exception.Message}");
        StatusText.Text = exception.Message;
        MessageBox.Show(exception.Message, "NeuroTune", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string FormatDiagnosis(DiagnosisResult diagnosis)
    {
        var text = new StringBuilder(diagnosis.Summary);
        if (diagnosis.Findings.Count > 0)
        {
            text.AppendLine().AppendLine().AppendLine("Findings");
            foreach (var finding in diagnosis.Findings) text.AppendLine($"• {finding}");
        }
        if (diagnosis.Recommendations.Count > 0)
        {
            text.AppendLine().AppendLine("Allowlisted recommendations");
            foreach (var recommendation in diagnosis.Recommendations) text.AppendLine($"• {recommendation.Reason}");
        }
        return text.ToString();
    }

    public static string FormatComparison(PerformanceSnapshot? before, PerformanceSnapshot? after)
    {
        if (before is null || after is null) return "No paired telemetry is available for this operation.";
        static string Value<T>(T? first, T? second, string suffix = "") where T : struct =>
            first is null || second is null ? "n/a" : $"{first}{suffix} → {second}{suffix}";
        var memory = before.UsedMemoryGb is null || after.UsedMemoryGb is null
            ? "n/a"
            : $"{before.UsedMemoryGb:0.0} GB → {after.UsedMemoryGb:0.0} GB";
        return $"CPU load: {Value(before.CpuLoadPercent, after.CpuLoadPercent, "%")}   •   Memory used: {memory}\n" +
               $"Processes: {before.ProcessCount} → {after.ProcessCount}   •   Network latency: {Value(before.LatencyMs, after.LatencyMs, " ms")}\n" +
               "These are immediate observations, not proof of a performance gain.";
    }

    private sealed record PresetChoice(string Display, OptimizationPreset Value);
    private sealed class HistoryItem
    {
        public HistoryItem(OperationManifest manifest) => Manifest = manifest;
        public OperationManifest Manifest { get; }
        public string Display => $"{Manifest.CreatedAt:g}   ·   {Manifest.Status}   ·   {Manifest.Actions.Count} actions   ·   {Manifest.Id:N}";
    }
}
