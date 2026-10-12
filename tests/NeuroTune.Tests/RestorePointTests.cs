using Microsoft.Win32;

namespace NeuroTune.Tests;

[TestClass]
public sealed class RestorePointTests
{
    private const string Name = "SystemRestorePointCreationFrequency";

    [TestMethod]
    public void Creation_limit_is_lifted_only_for_the_call_and_the_original_value_returns_even_on_failure()
    {
        var path = $@"Software\NeuroTune-tests\{Guid.NewGuid():N}";
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        try
        {
            Assert.AreEqual(0, BackupService.WithoutCreationLimit(key, () => (int)key.GetValue(Name)!));
            Assert.IsNull(key.GetValue(Name), "An absent value must stay absent.");

            key.SetValue(Name, 1440, RegistryValueKind.DWord);
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                BackupService.WithoutCreationLimit<int>(key, () => throw new InvalidOperationException("no point")));
            Assert.AreEqual(1440, key.GetValue(Name));
            Assert.AreEqual(RegistryValueKind.DWord, key.GetValueKind(Name));
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }
}
