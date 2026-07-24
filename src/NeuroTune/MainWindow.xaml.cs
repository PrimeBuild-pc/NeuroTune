using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace NeuroTune;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly OptimizationCatalog _catalog = new();
    private readonly BackupService _backup = new();
    private readonly ObservableCollection<OptimizationOption> _options = [];
    private DiagnosisResult? _diagnosis;
    private bool _loading = true;

    public MainWindow()
    {
        InitializeComponent();
        ProviderCombo.ItemsSource = Enum.GetValues<LlmProvider>();
        PresetCombo.ItemsSource = new[]
        {
            new PresetChoice("Sicuro / Bilanciato", OptimizationPreset.Balanced),
            new PresetChoice("Extreme Gaming", OptimizationPreset.Gaming),
            new PresetChoice("Personalizzato", OptimizationPreset.Custom)
        };
        PresetCombo.DisplayMemberPath = nameof(PresetChoice.Display);
        PresetCombo.SelectedIndex = 0;
        ActionsList.ItemsSource = _options;

        var settings = _settingsService.Load();
        ProviderCombo.SelectedItem = settings.Provider;
        ModelTextBox.Text = settings.Model;
        ApiKeyBox.Password = _settingsService.LoadApiKey(settings.Provider) is null ? "" : "••••••••••••";
        PopulateOptions();
        RefreshHistory();
        _loading = false;

        if (!LogService.IsAdministrator())
        {
            ApplyButton.IsEnabled = false;
            StatusText.Text = "Avvia NeuroTune come amministratore per applicare ottimizzazioni.";
        }
    }

    private UserSettings CurrentSettings() => new()
    {
        Provider = ProviderCombo.SelectedItem is LlmProvider provider ? provider : LlmProvider.OpenRouter,
        Model = ModelTextBox.Text.Trim()
    };

    private string? CurrentApiKey(UserSettings settings) =>
        ApiKeyBox.Password.StartsWith('•') ? _settingsService.LoadApiKey(settings.Provider) : ApiKeyBox.Password.Trim();

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = CurrentSettings();
            var key = ApiKeyBox.Password.StartsWith('•') ? null : ApiKeyBox.Password;
            _settingsService.Save(settings, key);
            if (!string.IsNullOrWhiteSpace(key)) ApiKeyBox.Password = "••••••••••••";
            StatusText.Text = "Configurazione salvata in modo sicuro.";
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProviderCombo.SelectedItem is not LlmProvider provider) return;
        ModelTextBox.Text = provider switch
        {
            LlmProvider.OpenAI => "gpt-4o-mini",
            LlmProvider.Anthropic => "claude-3-5-haiku-latest",
            _ => "openai/gpt-4o-mini"
        };
        ApiKeyBox.Password = _settingsService.LoadApiKey(provider) is null ? "" : "••••••••••••";
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Raccolta del profilo di sistema…");
        try
        {
            var settings = CurrentSettings();
            var key = CurrentApiKey(settings) ?? throw new InvalidOperationException("Inserisci e salva una API key.");
            _settingsService.Save(settings, ApiKeyBox.Password.StartsWith('•') ? null : key);
            ApiKeyBox.Password = "••••••••••••";

            var profile = await Task.Run(() => new SystemProfiler().Collect());
            ProfileTextBox.Text = ProfileSanitizer.Serialize(profile);
            StatusText.Text = "Analisi AI in corso…";
            _diagnosis = await new LlmClient(_catalog).DiagnoseAsync(profile, settings, key);
            DiagnosisTextBox.Text = FormatDiagnosis(_diagnosis);
            PopulateOptions();
            ApplyPreset();
            MainTabs.SelectedIndex = 2;
            StatusText.Text = $"Analisi completata: {_diagnosis.Recommendations.Count} ottimizzazioni raccomandate.";
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
            recommendations.TryGetValue(action.Id, out var recommendation);
            _options.Add(new OptimizationOption
            {
                Id = action.Id,
                Name = action.Name + (action.RequiresRestart ? " (riavvio)" : ""),
                Description = action.Description,
                Category = action.Category,
                Risk = action.Risk,
                RequiresRestart = action.RequiresRestart,
                Reason = recommendation?.Reason ?? "Non raccomandata dall'analisi corrente",
                IsRecommended = recommendation is not null
            });
        }
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) ApplyPreset();
    }

    private void ApplyPreset()
    {
        if (PresetCombo.SelectedItem is not PresetChoice choice || choice.Value == OptimizationPreset.Custom) return;
        foreach (var option in _options)
            option.IsSelected = OptimizationCatalog.SelectForPreset(_catalog.Get(option.Id), option.IsRecommended, choice.Value);
        ActionsList.Items.Refresh();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var selected = _options.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) { ShowError(new InvalidOperationException("Seleziona almeno un'ottimizzazione.")); return; }
        var names = string.Join(Environment.NewLine, selected.Select(x => $"• {x.Name} — rischio {x.RiskLabel}"));
        if (MessageBox.Show($"Verranno applicate queste modifiche:\n\n{names}\n\nContinuare?",
                "Conferma ottimizzazioni", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        SetBusy(true, "Creazione del punto di ripristino e applicazione…");
        try
        {
            var manifest = await new OptimizationEngine(_catalog, _backup).ApplyAsync(selected.Select(x => x.Id));
            var restart = selected.Any(x => x.RequiresRestart) ? " Riavvia Windows per completare le modifiche." : "";
            StatusText.Text = $"Ottimizzazione completata. Operazione {manifest.Id:N}.{restart}";
            RefreshHistory();
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
    }

    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();

    private void RefreshHistory() => HistoryList.ItemsSource = _backup.LoadHistory()
        .Select(x => new HistoryItem(x)).ToList();

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryItem selected) { ShowError(new InvalidOperationException("Seleziona un'operazione.")); return; }
        if (!selected.Manifest.Actions.Any(x => x.Applied && !x.RolledBack))
        {
            ShowError(new InvalidOperationException("Questa operazione non contiene modifiche da ripristinare."));
            return;
        }
        if (MessageBox.Show("Ripristinare le modifiche dell'operazione selezionata?", "Conferma rollback",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        SetBusy(true, "Rollback in corso…");
        try
        {
            await new OptimizationEngine(_catalog, _backup).RollbackAsync(selected.Manifest);
            StatusText.Text = "Rollback completato.";
            RefreshHistory();
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        AnalyzeButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy && LogService.IsAdministrator();
        AnalysisProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (status is not null) StatusText.Text = status;
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
            text.AppendLine().AppendLine().AppendLine("Rilievi:");
            foreach (var finding in diagnosis.Findings) text.AppendLine($"• {finding}");
        }
        if (diagnosis.Recommendations.Count > 0)
        {
            text.AppendLine().AppendLine("Raccomandazioni:");
            foreach (var recommendation in diagnosis.Recommendations) text.AppendLine($"• {recommendation.Reason}");
        }
        return text.ToString();
    }

    private sealed record PresetChoice(string Display, OptimizationPreset Value);
    private sealed class HistoryItem
    {
        public HistoryItem(OperationManifest manifest) => Manifest = manifest;
        public OperationManifest Manifest { get; }
        public string Display => $"{Manifest.CreatedAt:dd/MM/yyyy HH:mm} — {Manifest.Status} — {Manifest.Actions.Count} azioni — {Manifest.Id:N}";
    }
}
