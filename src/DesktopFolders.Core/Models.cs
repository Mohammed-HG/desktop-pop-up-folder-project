using System.Collections.ObjectModel;

namespace DesktopFolders.Core;

public enum Backdrop { Light, Dark, Custom, Translucent, Windows }
public enum AppAppearance { Windows, Light, Dark }
public enum FolderMark { None, Screwdriver, Wrench, Hammer, Gear, Home, Cloud, Star, Bolt }
public enum MarkStyle { Outline, Solid }
public enum AnimationChoice { Default, On, Off }

public sealed class Preferences
{
    public bool Arabic { get; set; }
    public bool Animate { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public AppAppearance Appearance { get; set; }
    public ObservableCollection<Folder> Folders { get; set; } = [];
}

public sealed class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Folder";
    public string Icon { get; set; } = "";
    public string IconColor { get; set; } = "";
    public FolderMark Mark { get; set; }
    public MarkStyle MarkStyle { get; set; }
    public bool ExplorerFolder { get; set; }
    public bool Locked { get; set; }
    public ObservableCollection<Entry> Entries { get; set; } = [];
    public PanelSettings Panel { get; set; } = new();

    public override string ToString() => Name;
}

public sealed class Entry
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Icon { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class PanelSettings
{
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 340;
    public int Columns { get; set; } = 4;
    public double IconSize { get; set; } = 42;
    public double Spacing { get; set; } = 6;
    public double Padding { get; set; } = 14;
    public double FontSize { get; set; } = 12;
    public double Radius { get; set; } = 22;
    public bool Labels { get; set; } = true;
    public Backdrop Background { get; set; } = Backdrop.Windows;
    public string Color { get; set; } = "#30364C";
    public double Transparency { get; set; }
    public bool OutsideClose { get; set; }
    public bool ShowCloseButton { get; set; } = true;
    public AnimationChoice Animation { get; set; }
    public bool Hover { get; set; } = true;
    public int Duration { get; set; } = 140;
    public int LockMinutes { get; set; }
    public bool RememberPosition { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }

    public void Normalize()
    {
        Width = Clamp(Width, 240, 1200);
        Height = Clamp(Height, 160, 1000);
        Columns = Math.Clamp(Columns, 1, 10);
        IconSize = Clamp(IconSize, 20, 96);
        Spacing = Clamp(Spacing, 0, 24);
        Padding = Clamp(Padding, 0, 40);
        FontSize = Clamp(FontSize, 9, 24);
        Radius = Clamp(Radius, 0, 48);
        Transparency = Clamp(Transparency, 0, 100);
        Duration = Math.Clamp(Duration, 0, 400);
        LockMinutes = Math.Clamp(LockMinutes, 0, 120);
        if (!Enum.IsDefined(Background)) Background = Backdrop.Light;
        if (!Enum.IsDefined(Animation)) Animation = AnimationChoice.Default;
        if (Color is not { Length: 7 } || Color[0] != '#' || !Color[1..].All(Uri.IsHexDigit))
            Color = "#30364C";
    }

    private static double Clamp(double value, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}

public enum TargetKind { Invalid, File, Web, AppsFolder }

public static class Targets
{
    public const string AppsPrefix = @"shell:AppsFolder\";

    public static TargetKind Classify(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return TargetKind.Invalid;
        if (target.StartsWith(AppsPrefix, StringComparison.OrdinalIgnoreCase)
            && target.Length > AppsPrefix.Length
            && target[AppsPrefix.Length..].All(c => char.IsLetterOrDigit(c) || "._!-{} ".Contains(c)))
            return TargetKind.AppsFolder;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" && uri.Host.Length != 0)
            return TargetKind.Web;
        if (Path.IsPathFullyQualified(target) && !string.IsNullOrEmpty(Path.GetFileName(target)))
            return TargetKind.File;
        return TargetKind.Invalid;
    }

    public static IEnumerable<Entry> NewEntries(IEnumerable<string> inputs, IEnumerable<Entry> existing)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in existing)
            if (Classify(entry.Target) == TargetKind.Web) urls.Add(new Uri(entry.Target).AbsoluteUri);
            else files.Add(entry.Target);
        foreach (var input in inputs)
        {
            var target = input.Trim().Trim('"');
            var kind = Classify(target);
            if (kind == TargetKind.File && File.Exists(target))
            {
                target = Path.GetFullPath(target);
                if (!files.Add(target)) continue;
                var extension = Path.GetExtension(target).ToLowerInvariant();
                var name = extension is ".exe" or ".lnk" or ".url"
                    ? Path.GetFileNameWithoutExtension(target) : Path.GetFileName(target);
                yield return new Entry { Name = name, Target = target };
            }
            else if (kind == TargetKind.Web)
            {
                var uri = new Uri(target);
                if (urls.Add(uri.AbsoluteUri)) yield return new Entry { Name = uri.Host, Target = uri.AbsoluteUri };
            }
        }
    }
}
