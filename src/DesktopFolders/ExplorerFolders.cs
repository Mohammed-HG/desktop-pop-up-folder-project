using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopFolders.Core;

namespace DesktopFolders;

internal static class ExplorerFolders
{
    private const string OwnerFile = ".desktopfolders-owner";
    private const string ManifestFile = ".desktopfolders-links.json";
    private const string FolderIcon = ".desktopfolders-icon.ico";
    private const string IniMarker = "; Desktop Folders";

    public static string? Find(Guid id, string desktop)
    {
        foreach (var directory in Directory.EnumerateDirectories(desktop))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            var marker = Path.Combine(directory, OwnerFile);
            try
            {
                if (RegularFile(marker) && Guid.TryParse(File.ReadAllText(marker), out var owner) && owner == id)
                    return directory;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    public static string Sync(Folder folder, string desktop)
    {
        if (folder.Locked) throw new InvalidOperationException("Explorer folders cannot use Windows Hello.");
        var current = Find(folder.Id, desktop);
        var name = WindowsServices.SafeName(folder.Name, "Folder");
        var destination = UniqueDirectory(desktop, name, current);
        if (current is null)
        {
            Directory.CreateDirectory(destination);
            using (var marker = new FileStream(Path.Combine(destination, OwnerFile), FileMode.CreateNew))
            using (var writer = new StreamWriter(marker))
                writer.Write(folder.Id.ToString("D"));
            File.SetAttributes(Path.Combine(destination, OwnerFile), FileAttributes.Hidden);
        }
        else if (!string.Equals(current, destination, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(current, destination);
            Notify(current);
        }
        else destination = current;

        SyncIcon(folder, destination);
        SyncLinks(folder, destination);
        Notify(destination);
        return destination;
    }

    public static void Detach(Guid id, string desktop)
    {
        if (Find(id, desktop) is not { } directory) return;
        File.Delete(Path.Combine(directory, OwnerFile));
        if (RegularFile(Path.Combine(directory, ManifestFile))) File.Delete(Path.Combine(directory, ManifestFile));
    }

    public static IReadOnlyList<Entry> UserEntries(Guid id, string desktop)
    {
        if (Find(id, desktop) is not { } directory) return [];
        var managed = ReadManifest(Path.Combine(directory, ManifestFile));
        var files = Directory.EnumerateFiles(directory).Where(path =>
        {
            var attributes = File.GetAttributes(path);
            var name = Path.GetFileName(path);
            return (attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0
                && !name.StartsWith(".desktopfolders-", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
                && (!managed.TryGetValue(name, out var hash) || !IsOwned(directory, name, hash));
        });
        return Targets.NewEntries(files, []).ToArray();
    }

    private static string UniqueDirectory(string desktop, string name, string? current)
    {
        var candidate = Path.Combine(desktop, name);
        for (var index = 2; Directory.Exists(candidate) || File.Exists(candidate); index++)
        {
            if (string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase)) break;
            candidate = Path.Combine(desktop, $"{name} ({index})");
        }
        return candidate;
    }

    private static void SyncIcon(Folder folder, string directory)
    {
        var ini = Path.Combine(directory, "desktop.ini");
        var ownsIni = RegularFile(ini) && File.ReadAllText(ini).StartsWith(IniMarker, StringComparison.Ordinal);
        if (File.Exists(ini) && !ownsIni) return;
        var bytes = IconPresets.Icon(folder.Icon, folder.IconColor);
        if (bytes is null)
        {
            if (ownsIni) File.Delete(ini);
            return;
        }
        // Use a content-based filename so Explorer refreshes its icon cache on color changes.
        var name = FolderIcon.Replace(".ico", $"-{Convert.ToHexString(SHA256.HashData(bytes))[..12]}.ico");
        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            using var stream = new FileStream(path, FileMode.CreateNew);
            stream.Write(bytes);
            File.SetAttributes(path, FileAttributes.Hidden);
        }
        WriteText(ini, $"{IniMarker}\r\n[.ShellClassInfo]\r\nIconResource={name},0\r\n", Encoding.Unicode,
            FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(directory, File.GetAttributes(directory) | FileAttributes.ReadOnly);
    }

    private static void SyncLinks(Folder folder, string directory)
    {
        var manifest = Path.Combine(directory, ManifestFile);
        var old = ReadManifest(manifest);
        var owned = old.Where(pair => IsOwned(directory, pair.Key, pair.Value))
            .Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Stage each Shell link before replacing a managed copy. Existing user files keep their names.
        foreach (var entry in folder.Entries)
        {
            var kind = Targets.Classify(entry.Target);
            if (kind == TargetKind.Invalid) continue;
            var extension = kind == TargetKind.Web || (kind == TargetKind.File
                && Path.GetExtension(entry.Target).Equals(".url", StringComparison.OrdinalIgnoreCase)) ? ".url" : ".lnk";
            var name = WindowsServices.SafeName(entry.Name, "Shortcut");
            var candidate = name + extension;
            for (var number = 2; next.ContainsKey(candidate) ||
                (File.Exists(Path.Combine(directory, candidate)) && !owned.Contains(candidate)) ||
                Directory.Exists(Path.Combine(directory, candidate)); number++)
                candidate = $"{name} ({number}){extension}";

            var temporary = Path.Combine(directory, ".desktopfolders-" + Guid.NewGuid().ToString("N") + extension);
            try
            {
                if (!WindowsServices.WriteEntryLink(entry, temporary, directory)) continue;
                var destination = Path.Combine(directory, candidate);
                var replace = owned.Contains(candidate) && IsOwned(directory, candidate, old[candidate]);
                File.Move(temporary, destination, overwrite: replace);
                next[candidate] = Hash(destination);
                WriteText(manifest, JsonSerializer.Serialize(Merge(old, next)), Encoding.UTF8, FileAttributes.Hidden);
            }
            finally { File.Delete(temporary); }
        }

        foreach (var name in owned)
            if (!next.ContainsKey(name) && IsOwned(directory, name, old[name]))
                File.Delete(Path.Combine(directory, name));
        WriteText(manifest, JsonSerializer.Serialize(next), Encoding.UTF8, FileAttributes.Hidden);
    }

    private static Dictionary<string, string> Merge(Dictionary<string, string> previous, Dictionary<string, string> next)
    {
        var merged = new Dictionary<string, string>(previous, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in next) merged[pair.Key] = pair.Value;
        return merged;
    }

    private static Dictionary<string, string> ReadManifest(string path)
    {
        if (!RegularFile(path)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            return data is null ? new(StringComparer.OrdinalIgnoreCase) : new(data, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static bool IsOwned(string directory, string name, string hash)
    {
        if (name != Path.GetFileName(name) || name.IndexOf(':') >= 0 ||
            Path.GetExtension(name).ToLowerInvariant() is not (".lnk" or ".url")) return false;
        var path = Path.Combine(directory, name);
        return RegularFile(path) && Hash(path) == hash;
    }

    private static bool RegularFile(string path) =>
        File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteText(string path, string text, Encoding encoding, FileAttributes attributes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, text, encoding);
            if (RegularFile(path))
            {
                // Windows does not replace hidden/system files with MoveFileEx.
                File.SetAttributes(path, FileAttributes.Normal);
                File.Replace(temporary, path, null);
            }
            else File.Move(temporary, path);
            File.SetAttributes(path, attributes);
        }
        finally { File.Delete(temporary); }
    }

    private static void Notify(string path) => SHChangeNotify(0x00002000, 0x0005, path, 0);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string path, nint other);
}
