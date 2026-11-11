using System.Collections.ObjectModel;

namespace DesktopFolders.Core;

public enum Backdrop { Light, Dark, Custom, Translucent }
public enum AnimationChoice { Default, On, Off }

public sealed class Preferences
{
    public bool Arabic { get; set; }
    public bool Animate { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public ObservableCollection<Folder> Folders { get; set; } = [];
}

public sealed class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Folder";
    public string Icon { get; set; } = "";
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
    public Backdrop Background { get; set; }
    public string Color { get; set; } = "#30364C";
    public double Transparency { get; set; }
    public bool OutsideClose { get; set; } = true;
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
        if (Path.IsPathFullyQualified(target) && Path.GetExtension(target).ToLowerInvariant() is ".exe" or ".lnk" or ".url")
            return TargetKind.File;
        return TargetKind.Invalid;
    }
}
