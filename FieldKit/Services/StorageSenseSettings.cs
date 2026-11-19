using Microsoft.Win32;
using System.Security.Principal;
using System.Text.Json;

namespace FieldKit.Services;

// Saving only the enable flag leaves the user's cleanup schedule and retention choices intact.
internal sealed class StorageSenseSettings(RegistryKey hive, string keyPath, string accountSid, string recovery)
{
    internal const string PolicyKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";
    private const string EnableFlag = "01";
    private string RecordPath => Path.Combine(recovery, $"storage-sense-{accountSid}.json");
    private sealed record Value(RegistryValueKind Kind, string Json);
    private sealed record Undo(string Sid, string Key, string Name, Value? Before);

    internal static StorageSenseSettings ForCurrentAccount()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new(Registry.CurrentUser, PolicyKey, identity.User!.Value, RecoveryStore.DirectoryPath);
    }

    private static Value? Capture(RegistryKey? key)
    {
        var original = key?.GetValue(EnableFlag, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (original is null) return null;
        return new(key!.GetValueKind(EnableFlag), JsonSerializer.Serialize(original));
    }

    internal bool CanUndo => File.Exists(RecordPath);

    internal string Enable()
    {
        using var lease = new FileStream(RecordPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var existing = hive.OpenSubKey(keyPath);
        var before = Capture(existing);
        if (before == new Value(RegistryValueKind.DWord, "1")) return "Storage Sense is already enabled.";
        if (CanUndo) throw new InvalidOperationException("A previous change has an undo record. Restore it before changing this setting again.");
        var temp = RecordPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, new Undo(accountSid, keyPath, EnableFlag, before));
                file.Flush(true);
            }
            File.Move(temp, RecordPath);
        }
        finally { File.Delete(temp); }
        using var key = hive.CreateSubKey(keyPath, writable: true);
        key.SetValue(EnableFlag, 1, RegistryValueKind.DWord);
        key.Flush();
        return "Storage Sense enabled with this account’s existing cleanup preferences.";
    }

    internal string Restore()
    {
        using var lease = new FileStream(RecordPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!CanUndo) return "There is no saved Storage Sense change for this account.";
        if (AppPaths.IsReparsePoint(RecordPath)) throw new IOException("The recovery record is a link.");
        var undo = JsonSerializer.Deserialize<Undo>(File.ReadAllText(RecordPath));
        if (undo is null || undo.Sid != accountSid || undo.Key != keyPath || undo.Name != EnableFlag)
            throw new InvalidDataException("The recovery record does not match this account and setting.");
        using var key = hive.OpenSubKey(keyPath, writable: true);
        var current = Capture(key);
        if (current != undo.Before)
        {
            if (current != new Value(RegistryValueKind.DWord, "1"))
                throw new InvalidOperationException("Storage Sense changed outside FieldKit. Restore stopped to preserve the newer value.");
            if (undo.Before is not { } original) key!.DeleteValue(EnableFlag);
            else
            {
                object value = original.Kind switch
                {
                    RegistryValueKind.DWord => JsonSerializer.Deserialize<int>(original.Json),
                    RegistryValueKind.QWord => JsonSerializer.Deserialize<long>(original.Json),
                    RegistryValueKind.String or RegistryValueKind.ExpandString => JsonSerializer.Deserialize<string>(original.Json)!,
                    RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(original.Json)!,
                    RegistryValueKind.Binary or RegistryValueKind.None => JsonSerializer.Deserialize<byte[]>(original.Json)!,
                    _ => throw new InvalidDataException("Unrecognized saved registry type.")
                };
                key!.SetValue(EnableFlag, value, original.Kind);
            }
            key!.Flush();
        }
        File.Delete(RecordPath);
        return "Restored this account’s previous Storage Sense setting.";
    }
}
