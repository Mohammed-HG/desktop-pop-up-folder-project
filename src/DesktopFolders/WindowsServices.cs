using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopFolders.Core;
using Microsoft.Win32;
using Windows.Security.Credentials.UI;
using Drawing = System.Drawing;

namespace DesktopFolders;

internal static class WindowsServices
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopFolders");
    private static string Executable => Environment.ProcessPath!;
    internal static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string Suffix(Guid id) => $" [{id.ToString("N")[..8]}].lnk";
    private static string Arguments(Guid id) => $"--open {id:D}";
    private static dynamic ScriptShell() => Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;

    public static void Log(string operation, Exception error)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var path = Path.Combine(DataDirectory, "errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256_000) File.Delete(path);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {operation}: {error.GetType().Name} (0x{error.HResult:X8})\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("DesktopFolders", $"\"{Executable}\" --tray");
        else key.DeleteValue("DesktopFolders", false);
    }

    private const string DesktopMenu = @"Software\Classes\DesktopBackground\Shell\DesktopFolders.New";

    public static bool HasDesktopMenu()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopMenu + @"\command");
        return key?.GetValue(null) is string;
    }

    public static void SetDesktopMenu(bool enabled, Words words)
    {
        if (!enabled)
        {
            Registry.CurrentUser.DeleteSubKeyTree(DesktopMenu, throwOnMissingSubKey: false);
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(DesktopMenu);
        key.SetValue(null, words.Get("New Desktop Folder…", "مجلد سطح مكتب جديد…"));
        key.SetValue("Icon", $"\"{Executable}\",0");
        using var command = key.CreateSubKey("command");
        command.SetValue(null, $"\"{Executable}\" --new-folder");
    }

    public static void SyncDesktop(Folder folder)
    {
        if (folder.ExplorerFolder && !folder.Locked)
        {
            ExplorerFolders.Sync(folder, Desktop);
            RemoveLinks(folder.Id);
        }
        else SyncLink(folder);
    }

    internal static string SafeName(string? name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((name ?? "").Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)
            .Take(60).ToArray()).Trim().TrimEnd('.');
        if (safe.Length == 0) return fallback;
        var device = safe.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT"))
                && "0123456789¹²³".Contains(device[3])))
            return "_" + safe;
        return safe;
    }

    internal static bool WriteEntryLink(Entry entry, string output, string directory)
    {
        var kind = Targets.Classify(entry.Target);
        if (kind == TargetKind.Invalid || (kind == TargetKind.File && !File.Exists(entry.Target))) return false;
        string? icon = null;
        if (IconPresets.Icon(entry.Icon) is { } bytes)
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
            icon = Path.Combine(directory, $".desktopfolders-app-{hash}.ico");
            if (!File.Exists(icon))
            {
                using var stream = new FileStream(icon, FileMode.CreateNew);
                stream.Write(bytes);
                File.SetAttributes(icon, FileAttributes.Hidden);
            }
        }
        if (kind == TargetKind.Web)
        {
            var iconInfo = icon is null ? "" : $"IconFile={icon}\r\nIconIndex=0\r\n";
            File.WriteAllText(output, $"[InternetShortcut]\r\nURL={new Uri(entry.Target).AbsoluteUri}\r\n{iconInfo}",
                System.Text.Encoding.Unicode);
            return true;
        }
        var extension = Path.GetExtension(entry.Target);
        if (kind == TargetKind.File && extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(entry.Target, output);
            File.SetAttributes(output, FileAttributes.Normal);
            return true;
        }
        dynamic shell = ScriptShell();
        var copy = kind == TargetKind.File && extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
        if (copy)
        {
            File.Copy(entry.Target, output);
            File.SetAttributes(output, FileAttributes.Normal);
            if (icon is null) return true;
        }
        dynamic link = shell.CreateShortcut(output);
        if (!copy)
        {
            link.TargetPath = kind == TargetKind.AppsFolder
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")
                : entry.Target;
            link.Arguments = kind == TargetKind.AppsFolder ? $"\"{entry.Target}\"" : "";
            link.WorkingDirectory = Path.GetDirectoryName((string)link.TargetPath);
        }
        if (icon is not null) link.IconLocation = icon + ",0";
        link.Save();
        return true;
    }

    private static bool Owns(dynamic shell, string path, Guid id)
    {
        dynamic link = shell.CreateShortcut(path);
        return (string)link.Arguments == Arguments(id)
            && (string)link.Description == "Desktop Folders";
    }

    public static void RemoveLinks(Guid id, string? keep = null, string? directory = null)
    {
        dynamic shell = ScriptShell();
        foreach (var path in Directory.EnumerateFiles(directory ?? Desktop, "*" + Suffix(id)))
            if (!string.Equals(path, keep, StringComparison.OrdinalIgnoreCase) && Owns(shell, path, id))
                File.Delete(path);
    }

    public static void SyncLink(Folder folder, string? directory = null)
    {
        var safe = SafeName(folder.Name, "Folder");
        dynamic shell = ScriptShell();
        var destination = directory ?? Desktop;
        var path = Path.Combine(destination, safe + Suffix(folder.Id));
        for (var n = 2; File.Exists(path) && !Owns(shell, path, folder.Id); n++)
            path = Path.Combine(destination, $"{safe} ({n})" + Suffix(folder.Id));
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = Executable;
        link.Arguments = Arguments(folder.Id);
        link.Description = "Desktop Folders";
        link.WorkingDirectory = AppContext.BaseDirectory;
        link.IconLocation = Executable + ",0";
        if (!string.IsNullOrWhiteSpace(folder.Icon) || !string.IsNullOrWhiteSpace(folder.IconColor))
        {
            try
            {
                if (IconPresets.Icon(folder.Icon, folder.IconColor) is { } bytes)
                {
                    var icon = Path.Combine(DataDirectory, $"{folder.Id:N}.ico");
                    File.WriteAllBytes(icon, bytes);
                    link.IconLocation = icon + ",0";
                }
            }
            catch (Exception error) { Log("desktop-icon", error); }
        }
        link.Save();
        RemoveLinks(folder.Id, path, destination);
    }

    public static IReadOnlyList<Entry> InstalledApps()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        var entries = new List<Entry>();
        foreach (var item in folder.Items())
        {
            string path = item.Path;
            string target = Targets.Classify(path) == TargetKind.File ? path : Targets.AppsPrefix + path;
            if (Targets.Classify(target) != TargetKind.Invalid)
                entries.Add(new() { Name = item.Name, Target = target });
        }
        return entries.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static void Launch(Entry entry)
    {
        var kind = Targets.Classify(entry.Target);
        if (kind == TargetKind.Invalid) throw new InvalidOperationException();
        if (kind == TargetKind.File && !File.Exists(entry.Target)) throw new FileNotFoundException();
        var info = new ProcessStartInfo(entry.Target) { UseShellExecute = true };
        if (kind == TargetKind.AppsFolder)
        {
            if (SHParseDisplayName(entry.Target, 0, out var pidl, 0, out _) != 0)
                throw new FileNotFoundException();
            Marshal.FreeCoTaskMem(pidl);
            info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            info.ArgumentList.Add(entry.Target);
        }
        if (kind == TargetKind.File) info.WorkingDirectory = Path.GetDirectoryName(entry.Target)!;
        Process.Start(info)?.Dispose();
    }

    private static ImageSource? folderIcon;
    public static ImageSource FolderIcon => folderIcon ??= LoadFolderIcon();

    private static ImageSource LoadFolderIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("Assets/folder.ico", UriKind.Relative)).Stream;
        return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    public static ImageSource EntryIcon(Entry entry)
    {
        try
        {
            if (IconPresets.Image(entry.Icon) is { } preset) return preset;
            if (!string.IsNullOrWhiteSpace(entry.Icon) && File.Exists(entry.Icon))
            {
                using var stream = File.OpenRead(entry.Icon);
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            var kind = Targets.Classify(entry.Target);
            if (kind == TargetKind.File && File.Exists(entry.Target))
            {
                var pointer = Marshal.StringToCoTaskMemUni(entry.Target);
                try { return ShellIcon(pointer, 0) ?? DefaultEntryIcon; }
                finally { Marshal.FreeCoTaskMem(pointer); }
            }
            if (kind == TargetKind.AppsFolder && SHParseDisplayName(entry.Target, 0, out var pidl, 0, out _) == 0)
            {
                try { return ShellIcon(pidl, 8) ?? DefaultEntryIcon; }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
        }
        catch (Exception error) { Log("entry-icon", error); }
        return DefaultEntryIcon;
    }

    private static ImageSource DefaultEntryIcon => Imaging.CreateBitmapSourceFromHIcon(
        Drawing.SystemIcons.Application.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

    private static ImageSource? ShellIcon(nint pointer, uint flags)
    {
        SHGetFileInfo(pointer, 0, out var info, (uint)Marshal.SizeOf<ShellInfo>(), 0x100 | flags);
        if (info.Icon == 0) return null;
        try { return Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
        finally { DestroyIcon(info.Icon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW")]
    private static extern nint SHGetFileInfo(nint path, uint attributes, out ShellInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint context, out nint pidl, uint mask, out uint attributes);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}

internal static class Hello
{
    public static async Task<bool> Verify(Window owner, Words words)
    {
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                && await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available)
            {
                var hwnd = new WindowInteropHelper(owner).EnsureHandle();
                var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd,
                    words.Get("Verify to open this folder", "تحقق من هويتك لفتح هذا المجلد"));
                if (result == UserConsentVerificationResult.Verified) return true;
                if (result == UserConsentVerificationResult.Canceled) return false;
                MessageBox.Show(owner, words.Get("Windows Hello did not verify your identity.", "لم يتحقق Windows Hello من هويتك."));
                return false;
            }
        }
        catch (Exception error) { WindowsServices.Log("windows-hello", error); }
        MessageBox.Show(owner, words.Get(
            "Windows Hello verification is unavailable. Check Windows sign-in options and try again. The folder remains locked.",
            "التحقق بواسطة Windows Hello غير متاح. تحقق من خيارات تسجيل الدخول في Windows وحاول مجددًا. يبقى المجلد مقفلاً."));
        return false;
    }
}
