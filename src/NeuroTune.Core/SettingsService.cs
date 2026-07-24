using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

public sealed class SettingsService
{
    public static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuroTune");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath = Path.Combine(DataDirectory, "settings.json");

    public UserSettings Load()
    {
        try
        {
            var settings = File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_settingsPath)) ?? new()
                : new();
            return Normalize(settings);
        }
        catch
        {
            return new();
        }
    }

    public void Save(UserSettings settings, string? apiKey = null)
    {
        settings = Normalize(settings);
        Directory.CreateDirectory(DataDirectory);
        AtomicWrite(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(apiKey.Trim()), null, DataProtectionScope.CurrentUser);
            AtomicWriteBytes(KeyPath(settings.CredentialId), protectedBytes);
        }
    }

    public string? LoadApiKey(LlmProvider provider) => LoadApiKey(provider.ToString().ToLowerInvariant());

    public string? LoadApiKey(UserSettings settings) => LoadApiKey(settings.CredentialId);

    private static string? LoadApiKey(string credentialId)
    {
        try
        {
            var path = KeyPath(credentialId);
            return File.Exists(path)
                ? Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string KeyPath(string credentialId) =>
        Path.Combine(DataDirectory, $"{credentialId}.key");

    private static UserSettings Normalize(UserSettings settings)
    {
        if (settings.Provider is LlmProvider.Custom or LlmProvider.Local) return settings;
        var defaults = LlmClient.Defaults(settings.Provider);
        settings.ProviderName = defaults.ProviderName;
        settings.BaseUrl = defaults.BaseUrl;
        settings.Protocol = defaults.Protocol;
        settings.RequiresApiKey = defaults.RequiresApiKey;
        return settings;
    }

    private static void AtomicWrite(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, true);
    }

    private static void AtomicWriteBytes(string path, byte[] content)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, true);
    }
}
