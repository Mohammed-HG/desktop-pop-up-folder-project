using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopFolders.Core;

public sealed class Store(string directory)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Pathname => Path.Combine(directory, "folders.json");
    public string? Recovery { get; private set; }
    public bool ReadOnly { get; private set; }

    public Preferences Load()
    {
        foreach (var path in new[] { Pathname, Pathname + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var data = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), Options)
                    ?? throw new JsonException();
                if (data.Folders is null || data.Folders.Any(folder =>
                    folder?.Panel is null || folder.Entries is null || folder.Entries.Any(entry => entry is null)))
                    throw new JsonException();
                var ids = new HashSet<Guid>();
                foreach (var folder in data.Folders)
                {
                    if (folder.Id == Guid.Empty || !ids.Add(folder.Id)) throw new JsonException();
                    folder.Panel.Normalize();
                }
                if (path != Pathname) Recovery = "backup";
                return data;
            }
            catch (JsonException)
            {
                Recovery = "reset";
                try { File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N")); }
                catch (IOException) { ReadOnly = true; }
                catch (UnauthorizedAccessException) { ReadOnly = true; }
            }
            catch (IOException) { ReadOnly = true; Recovery = "unreadable"; }
            catch (UnauthorizedAccessException) { ReadOnly = true; Recovery = "unreadable"; }
        }
        return new();
    }

    public void Save(Preferences data)
    {
        if (ReadOnly) throw new IOException("Settings could not be read.");
        Directory.CreateDirectory(directory);
        var temporary = Pathname + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, data, Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(Pathname)) File.Replace(temporary, Pathname, Pathname + ".bak");
            else File.Move(temporary, Pathname);
        }
        finally { File.Delete(temporary); }
    }

    public static Folder Clone(Folder folder) =>
        JsonSerializer.Deserialize<Folder>(JsonSerializer.Serialize(folder, Options), Options)!;
}

public static class FolderRules
{
    public static string UniqueName(IEnumerable<Folder> folders, string requested, Guid? exclude = null)
    {
        requested = requested.Trim();
        var names = folders.Where(folder => folder.Id != exclude)
            .Select(folder => folder.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = requested;
        for (var number = 2; names.Contains(name); number++)
            name = $"{requested} ({number})";
        return name;
    }
}
