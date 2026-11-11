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
using Microsoft.Win32.SafeHandles;
using Windows.Security.Credentials.UI;

namespace DesktopFolders;

internal static class WindowsServices
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopFolders");
    private static string Executable => Environment.ProcessPath!;
    internal static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string Arguments(Guid id) => $"--open {id:D}";

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

    public static void SyncDesktop(Folder folder, string? directory = null)
    {
        SyncLink(folder, directory);
    }

    public static void ForwardForeground(SafePipeHandle pipe)
    {
        if (GetNamedPipeServerProcessId(pipe, out var processId)) AllowSetForegroundWindow(processId);
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

    public static void RemoveLinks(Guid id, string? keep = null, string? directory = null)
    {
        foreach (var path in FindLinks(id, directory ?? Desktop))
            if (!string.Equals(path, keep, StringComparison.OrdinalIgnoreCase))
                File.Delete(path);
    }

    private static IEnumerable<string> FindLinks(Guid id, string directory) =>
        Directory.EnumerateFiles(directory, "*.lnk").Where(path => ShellShortcut.IsManaged(path, Arguments(id)));

    public static void SyncLink(Folder folder, string? directory = null)
    {
        var safe = SafeName(folder.Name, "Folder");
        var destination = directory ?? Desktop;
        var existing = FindLinks(folder.Id, destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(destination, safe + ".lnk");
        if (Directory.Exists(path) || (File.Exists(path) && !existing.Contains(path)))
            throw new IOException("An unrelated desktop item is already using this shortcut filename.");
        if (!File.Exists(path) && existing.FirstOrDefault() is { } previous)
        {
            File.Move(previous, path);
            existing.Remove(previous);
        }
        var iconPath = Executable;
        var iconIndex = 0;
        var stock = new StockIcon { Size = (uint)Marshal.SizeOf<StockIcon>() };
        if (SHGetStockIconInfo(3, 0, ref stock) == 0 && !string.IsNullOrWhiteSpace(stock.Path))
        {
            iconPath = stock.Path;
            iconIndex = stock.Index;
        }
        if (FolderArtwork.HasCustomArt(folder))
        {
            try
            {
                var bytes = FolderArtwork.Ico(folder);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..12];
                var icon = Path.Combine(directory ?? DataDirectory, $"{folder.Id:N}-{hash}.ico");
                if (!File.Exists(icon)) File.WriteAllBytes(icon, bytes);
                iconPath = icon;
                iconIndex = 0;
            }
            catch (Exception error) { Log("desktop-icon", error); }
        }
        ShellShortcut.Save(path, Executable, Arguments(folder.Id), AboutWindow.AppName,
            AppContext.BaseDirectory, iconPath, iconIndex);
        foreach (var duplicate in existing)
            if (!string.Equals(duplicate, path, StringComparison.OrdinalIgnoreCase))
                File.Delete(duplicate);
        RemoveFolderIcons(folder.Id, iconPath, directory);
    }

    public static void RemoveFolderIcons(Guid id, string? keep = null, string? directory = null)
    {
        var storage = directory ?? DataDirectory;
        if (!Directory.Exists(storage)) return;
        foreach (var icon in Directory.EnumerateFiles(storage, $"{id:N}*.ico"))
            if (!string.Equals(icon, keep, StringComparison.OrdinalIgnoreCase)) File.Delete(icon);
    }

    public static IReadOnlyList<Entry> InstalledApps()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        var entries = new List<Entry>();
        foreach (var item in folder.Items())
        {
            string path = item.Path;
            string target = Targets.Classify(path) == TargetKind.File && File.Exists(path)
                ? path : Targets.AppsPrefix + path;
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
    private static BitmapSource? windowsFolderImage;
    public static BitmapSource WindowsFolderImage => windowsFolderImage ??= StockImage(3) ?? (BitmapSource)FolderIcon;

    private static BitmapSource? StockImage(uint id)
    {
        var stock = new StockIcon { Size = (uint)Marshal.SizeOf<StockIcon>() };
        if (SHGetStockIconInfo(id, 0x4000, ref stock) == 0 && LargeIcon(stock.ImageIndex) is { } large)
            return large;
        if (SHGetStockIconInfo(id, 0x100, ref stock) != 0 || stock.Handle == 0) return null;
        return ReadIcon(stock.Handle);
    }
    public static ImageSource FolderIcon => folderIcon ??= LoadFolderIcon();

    private static ImageSource LoadFolderIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("Assets/folder.ico", UriKind.Relative)).Stream;
        return LargestFrame(stream);
    }

    private static BitmapFrame LargestFrame(Stream stream) => BitmapDecoder.Create(stream,
        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames
        .MaxBy(frame => (long)frame.PixelWidth * frame.PixelHeight)!;

    public static ImageSource EntryIcon(Entry entry)
    {
        try
        {
            if (IconPresets.Image(entry.Icon) is { } preset) return preset;
            if (!string.IsNullOrWhiteSpace(entry.Icon) && File.Exists(entry.Icon))
            {
                using var stream = File.OpenRead(entry.Icon);
                return LargestFrame(stream);
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

    private static ImageSource? defaultEntryIcon;
    private static ImageSource DefaultEntryIcon => defaultEntryIcon ??= (ImageSource?)StockImage(2) ?? FolderIcon;

    private static ImageSource? ShellIcon(nint pointer, uint flags)
    {
        var size = (uint)Marshal.SizeOf<ShellInfo>();
        if (SHGetFileInfo(pointer, 0, out var indexed, size, 0x4000 | flags) != 0
            && LargeIcon(indexed.IconIndex) is { } large) return large;
        SHGetFileInfo(pointer, 0, out var info, size, 0x100 | flags);
        return info.Icon == 0 ? null : ReadIcon(info.Icon);
    }

    private static BitmapSource? LargeIcon(int index)
    {
        var id = typeof(IImageList).GUID;
        if (SHGetImageList(4, ref id, out var list) != 0 || list is null) return null;
        try { return list.GetIcon(index, 1, out var icon) == 0 && icon != 0 ? ReadIcon(icon) : null; }
        finally { Marshal.ReleaseComObject(list); }
    }

    private static BitmapSource ReadIcon(nint handle)
    {
        try
        {
            var icon = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            if (icon.PixelWidth < 128) return icon;
            // Older applications can supply a 32px image inside the jumbo list's transparent 256px cell.
            var pixels = new FormatConvertedBitmap(icon, PixelFormats.Bgra32, null, 0);
            var stride = pixels.PixelWidth * 4;
            var buffer = new byte[stride * pixels.PixelHeight];
            pixels.CopyPixels(buffer, stride, 0);
            var left = pixels.PixelWidth;
            var top = pixels.PixelHeight;
            var right = -1;
            var bottom = -1;
            for (var y = 0; y < pixels.PixelHeight; y++)
            for (var x = 0; x < pixels.PixelWidth; x++)
            {
                if (buffer[y * stride + x * 4 + 3] == 0) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
            if (right < left) return icon;
            var span = Math.Max(right - left + 1, bottom - top + 1);
            return span < icon.PixelWidth * .72
                ? new CroppedBitmap(icon, new Int32Rect(left, top, right - left + 1, bottom - top + 1)) : icon;
        }
        finally { DestroyIcon(handle); }
    }

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        void Add(nint image, nint mask, out int index);
        void ReplaceIcon(int index, nint icon, out int replacement);
        void SetOverlayImage(int image, int overlay);
        void Replace(int index, nint image, nint mask);
        void AddMasked(nint image, uint mask, out int index);
        void Draw(nint parameters);
        void Remove(int index);
        [PreserveSig] int GetIcon(int index, uint flags, out nint icon);
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
    [DllImport("shell32.dll")] private static extern int SHGetImageList(int size, ref Guid id, out IImageList? list);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint context, out nint pidl, uint mask, out uint attributes);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StockIcon
    {
        public uint Size;
        public nint Handle;
        public int ImageIndex;
        public int Index;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Path;
    }
    [DllImport("shell32.dll")] private static extern int SHGetStockIconInfo(uint id, uint flags, ref StockIcon info);
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
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
