using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopFolders.Core;

namespace DesktopFolders;

internal static class FolderStorage
{
    private const string OwnerFile = ".desktopfolders-owner";
    private const string ManifestFile = ".desktopfolders-links.json";

    public static string Root(Guid id, string? dataDirectory = null) =>
        Path.Combine(dataDirectory ?? WindowsServices.DataDirectory, "Files", id.ToString("N"));

    private static IEnumerable<string> OwnedDesktopFolders(Guid id, string desktop)
    {
        if (!Directory.Exists(desktop)) yield break;
        foreach (var directory in new DirectoryInfo(desktop).EnumerateDirectories())
        {
            // OneDrive uses reparse points for real directories too. LinkTarget identifies junctions and symlinks.
            if (directory.LinkTarget is not null) continue;
            var marker = new FileInfo(Path.Combine(directory.FullName, OwnerFile));
            if (marker.Exists && marker.LinkTarget is null &&
                Guid.TryParse(File.ReadAllText(marker.FullName), out var owner) && owner == id)
                yield return directory.FullName;
        }
    }

    public static bool MigrateDesktopFolders(Folder folder, string desktop, string? dataDirectory = null)
    {
        var changed = false;
        foreach (var source in OwnedDesktopFolders(folder.Id, desktop).ToArray())
        {
            var root = Root(folder.Id, dataDirectory);
            Directory.CreateDirectory(root);
            var destination = UniquePath(root, "Legacy", directory: true);
            Directory.Move(source, destination);
            var prefix = source + Path.DirectorySeparatorChar;
            foreach (var entry in folder.Entries)
            {
                if (!entry.Target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                entry.Target = Path.Combine(destination, entry.Target[prefix.Length..]);
                changed = true;
            }
        }
        return changed;
    }

    public static IReadOnlyList<Entry> UserEntries(Guid id, string? dataDirectory = null, string? desktop = null)
    {
        var root = Root(id, dataDirectory);
        var legacy = Directory.Exists(root) ? Directory.EnumerateDirectories(root, "Legacy*") : [];
        var directories = legacy.Concat(OwnedDesktopFolders(id, desktop ?? WindowsServices.Desktop));
        return Targets.NewEntries(directories.SelectMany(VisibleFiles), []).ToArray();
    }

    private static IEnumerable<string> VisibleFiles(string directory)
    {
        if (new DirectoryInfo(directory).LinkTarget is not null) yield break;
        var manifest = Path.Combine(directory, ManifestFile);
        var managed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(manifest))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest));
                if (stored is not null) managed = new(stored, StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException) { }
        }
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles())
        {
            if (file.LinkTarget is not null || (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0
                || file.Name.StartsWith(".desktopfolders-", StringComparison.OrdinalIgnoreCase)
                || file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            if (managed.TryGetValue(file.Name, out var expected))
            {
                using var stream = file.OpenRead();
                if (Convert.ToHexString(SHA256.HashData(stream)) == expected) continue;
            }
            yield return file.FullName;
        }
    }

    public static bool MoveFiles(Folder folder, IEnumerable<string> paths, Func<bool> save, string? dataDirectory = null)
    {
        var destination = Path.Combine(Root(folder.Id, dataDirectory), "Moved");
        var moves = new List<(string Source, string Destination)>();
        var changes = new List<(Entry Entry, string? PreviousTarget)>();
        var committed = false;
        try
        {
            foreach (var source in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Targets.Classify(source) != TargetKind.File || !File.Exists(source)) return false;
                var file = new FileInfo(source);
                if (file.LinkTarget is not null || file.DirectoryName is null
                    || string.Equals(file.DirectoryName, destination, StringComparison.OrdinalIgnoreCase)) return false;
                if (ShellShortcut.IsFolderLauncher(file.FullName)) return false;
                Directory.CreateDirectory(destination);
                var target = UniquePath(destination, file.Name);
                File.Move(file.FullName, target);
                if (File.Exists(file.FullName))
                {
                    File.Delete(target);
                    throw new IOException("Windows copied the file but could not remove it from its original location.");
                }
                moves.Add((file.FullName, target));
                var matching = folder.Entries.Where(entry => string.Equals(entry.Target, file.FullName, StringComparison.OrdinalIgnoreCase)).ToArray();
                foreach (var entry in matching)
                {
                    changes.Add((entry, entry.Target));
                    entry.Target = target;
                }
                if (matching.Length != 0) continue;
                var added = Targets.NewEntries([target], []).Single();
                folder.Entries.Add(added);
                changes.Add((added, null));
            }
            if (moves.Count == 0) return false;
            committed = save();
            return committed;
        }
        finally
        {
            if (!committed)
            {
                foreach (var (entry, previous) in changes)
                {
                    if (previous is null) folder.Entries.Remove(entry);
                    else entry.Target = previous;
                }
                foreach (var (source, target) in moves.AsEnumerable().Reverse())
                {
                    try { File.Move(target, source); }
                    catch (Exception error) { WindowsServices.Log("restore-moved-file", error); }
                }
            }
        }
    }

    public static void Open(Guid id)
    {
        var path = Root(id);
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    private static string UniquePath(string parent, string name, bool directory = false)
    {
        var stem = directory ? name : Path.GetFileNameWithoutExtension(name);
        var extension = directory ? "" : Path.GetExtension(name);
        var path = Path.Combine(parent, name);
        for (var index = 2; File.Exists(path) || Directory.Exists(path); index++)
            path = Path.Combine(parent, $"{stem} ({index}){extension}");
        return path;
    }
}
