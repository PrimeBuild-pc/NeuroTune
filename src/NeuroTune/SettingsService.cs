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
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_settingsPath)) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    public void Save(UserSettings settings, string? apiKey = null)
    {
        Directory.CreateDirectory(DataDirectory);
        AtomicWrite(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(apiKey.Trim()), null, DataProtectionScope.CurrentUser);
            AtomicWriteBytes(KeyPath(settings.Provider), protectedBytes);
        }
    }

    public string? LoadApiKey(LlmProvider provider)
    {
        try
        {
            var path = KeyPath(provider);
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

    private static string KeyPath(LlmProvider provider) =>
        Path.Combine(DataDirectory, $"{provider.ToString().ToLowerInvariant()}.key");

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
