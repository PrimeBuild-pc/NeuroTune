using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace NeuroTune;

// Privileged recovery state must not inherit the interactive user's write/owner rights.
internal sealed class JournalStorage(string directory, bool privileged = false)
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly string MachineRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NeuroTune-journals");
    internal static string UserDirectory => Path.Combine(MachineRoot, WindowsIdentity.GetCurrent().User!.Value);
    internal string DirectoryPath { get; } = Path.GetFullPath(directory);

    internal void CheckLegacyJournals()
    {
        if (!privileged) return;
        RequireNoLegacyJournals(SettingsService.DataDirectory);
    }

    internal static void RequireNoLegacyJournals(string dataDirectory)
    {
        // Never silently abandon or elevate unsigned legacy recovery data.
        var blocked = new[] { "operations", "runs" }
            .Select(name => Path.Combine(Path.GetFullPath(dataDirectory), name))
            .Where(legacy => Path.Exists(legacy) && (File.GetAttributes(legacy).HasFlag(FileAttributes.ReparsePoint) ||
                !Directory.Exists(legacy) || Directory.EnumerateFileSystemEntries(legacy).Any()))
            .ToArray();
        if (blocked.Length > 0)
            throw new InvalidOperationException($"Legacy journals need manual review. Blocking paths for the Windows account running NeuroTune: {string.Join("; ", blocked)}. " +
                "Finish or review recovery with the previous build. Only after resolving old writes, move the reviewed folders to a backup outside the NeuroTune data directory. " +
                "Copying folders or reinstalling does not clear this block. Do not delete pending recovery or import legacy journals into privileged storage.");
    }

    internal void EnsureDirectory(string path)
    {
        CheckPathBoundary(path);
        var root = privileged ? MachineRoot : DirectoryPath;
        var current = root;
        Create(current);
        foreach (var part in Path.GetRelativePath(root, Path.GetFullPath(path)).Split(Path.DirectorySeparatorChar))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            Create(current);
        }
    }

    private void Create(string path)
    {
        if (Path.Exists(path)) { CheckOne(path); return; }
        if (privileged)
        {
            var security = new DirectorySecurity();
            security.SetOwner(Administrators);
            security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { Administrators, System })
                security.AddAccessRule(new(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).Create(security);
        }
        else Directory.CreateDirectory(path);
        CheckOne(path);
    }

    internal IEnumerable<string> Directories()
    {
        CheckPath(DirectoryPath);
        if (!Directory.Exists(DirectoryPath)) return [];
        return Directory.GetDirectories(DirectoryPath).Select(path => { CheckPath(path); return path; }).ToList();
    }

    internal string Read(string path)
    {
        CheckPath(path);
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidOperationException("The journal size is invalid.");
        return File.ReadAllText(path);
    }

    internal void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        EnsureDirectory(Path.GetDirectoryName(path)!);
        CheckPath(path);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            FileStream stream;
            if (privileged)
            {
                var security = new FileSecurity();
                security.SetOwner(Administrators);
                security.SetAccessRuleProtection(true, false);
                foreach (var sid in new[] { Administrators, System })
                    security.AddAccessRule(new(sid, FileSystemRights.FullControl, AccessControlType.Allow));
                stream = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough, security);
            }
            else stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            using (stream)
            {
                JsonSerializer.Serialize(stream, value, options);
                if (stream.Position > 16 * 1024 * 1024) throw new InvalidOperationException("The journal exceeds 16 MiB.");
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void CheckPathBoundary(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.Equals(DirectoryPath, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The journal path is outside its store.");
    }

    internal void CheckPath(string path)
    {
        CheckPathBoundary(path);
        var root = privileged ? MachineRoot : DirectoryPath;
        var current = root;
        CheckOne(current);
        foreach (var part in Path.GetRelativePath(root, Path.GetFullPath(path)).Split(Path.DirectorySeparatorChar))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            CheckOne(current);
        }
    }

    private void CheckOne(string path)
    {
        if (!Path.Exists(path)) return;
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Linked journal paths are forbidden.");
        if (!privileged) return;
        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        if (!HasTrustedAcl(security)) throw new InvalidOperationException("The journal owner or permissions are not trusted; refusing automatic repair/import.");
    }

    internal static bool HasTrustedAcl(FileSystemSecurity security)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        return (Administrators.Equals(owner) || System.Equals(owner)) && rules.Count > 0 &&
            rules.All(rule => rule.AccessControlType == AccessControlType.Allow &&
                (Administrators.Equals(rule.IdentityReference) || System.Equals(rule.IdentityReference))) &&
            rules.Any(rule => Administrators.Equals(rule.IdentityReference) && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)) &&
            rules.Any(rule => System.Equals(rule.IdentityReference) && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl));
    }
}
